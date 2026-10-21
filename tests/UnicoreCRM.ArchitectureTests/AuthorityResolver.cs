using System.Text.Json;

namespace UnicoreCRM.ArchitectureTests;

// Resolves the already-canonical typed operation registry against its exact extension record.
// This is test-only authority normalization, not operation admission or runtime authorization.
internal static class AuthorityResolver
{
    internal static IReadOnlyDictionary<string, JsonElement> Resolve(IEnumerable<JsonElement> pinned,
        JsonElement registry, JsonElement extensions)
    {
        var result = pinned.ToDictionary(o => Text(o, "operationId"), StringComparer.Ordinal);
        if (Text(registry, "status") != "CANONICAL" || Text(extensions, "status") != "CANONICAL")
            throw new InvalidDataException("Effective authority documents must be canonical.");
        var operations = registry.GetProperty("operations").EnumerateArray().ToArray();
        if (operations.GroupBy(o => Text(o, "operationId"), StringComparer.Ordinal).Any(g => g.Count() != 1))
            throw new InvalidDataException("Ambiguous effective operation identity.");
        foreach (var operation in operations)
        {
            var authority = Text(operation, "wireContractAuthority");
            if (!authority.StartsWith("PROJECT_EXTENSION_", StringComparison.Ordinal)) continue;
            var id = Text(operation, "operationId");
            var candidates = extensions.GetProperty("extensions").EnumerateArray()
                .Where(e => Text(e, "authorityId") == authority).ToArray();
            if (candidates.Length != 1) throw new InvalidDataException($"Missing or ambiguous extension: {id}");
            var extension = candidates[0];
            if (!extension.GetProperty("operations").EnumerateArray().Any(x => x.GetString() == id)
                || Text(extension, "canonicalOwner") != Text(operation, "canonicalOwner")
                || Text(extension, "implementationStatus") != "IMPLEMENTED"
                || Text(operation, "backendAuthorityStatus") != "ADMITTED"
                || Text(operation, "finalStatus") is not ("ADMITTED_IMPLEMENTED" or "ADMITTED_NOT_IMPLEMENTED")
                || Text(operation, "supersessionStatus") is not ("ACTIVE" or "ACTIVE_EXTENSION" or "FRONTEND_REQUEST_BODY_SUPERSEDED"))
                throw new InvalidDataException($"Extension does not admit exact current operation scope: {id}");
            foreach (var required in new[] { "canonicalOwner", "method", "path", "requiredCapability", "resourceScope", "dataScope", "responseSchema" })
                if (string.IsNullOrWhiteSpace(Text(operation, required))) throw new InvalidDataException($"Missing extension contract field {required}: {id}");
            var method = Text(operation, "method"); var path = Text(operation, "path");
            if (!new[] { "GET", "POST", "PUT", "PATCH", "DELETE" }.Contains(method, StringComparer.Ordinal)
                || !path.StartsWith("/", StringComparison.Ordinal) || path.Contains("..", StringComparison.Ordinal))
                throw new InvalidDataException($"Invalid extension route: {id}");
            var statuses = Text(operation, "successStatus").Split('|');
            if (statuses.Length == 0 || statuses.Any(s => !int.TryParse(s, out var code) || code is < 200 or >= 300))
                throw new InvalidDataException($"Invalid extension success contract: {id}");
            var authentication = Text(operation, "authentication");
            var actor = authentication switch
            {
                "REQUIRED" => true,
                "NONE" or "PUBLIC" or "HMAC_SIGNED_PROVIDER" => false,
                _ => throw new InvalidDataException($"Unknown extension authentication mode: {id}")
            };
            var workspace = operation.GetProperty("workspaceRequired");
            var workspaceRequired = workspace.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String when workspace.GetString() == "REQUIRED" => true,
                JsonValueKind.String when workspace.GetString() == "NOT_REQUIRED" => false,
                JsonValueKind.String when workspace.GetString() == "SERVER_BOUND" && authentication == "HMAC_SIGNED_PROVIDER" => false,
                _ => throw new InvalidDataException($"Unknown extension Workspace mode: {id}")
            };
            if (result.TryGetValue(id, out var historical)
                && (Text(historical, "method") != method || Text(historical, "path") != path))
                throw new InvalidDataException($"Extension conflicts with pinned route identity: {id}");
            if (historical.ValueKind == JsonValueKind.Object && Text(historical, "status") == "BLOCKED"
                && Text(operation, "businessDecisionAuthority") != authority)
                throw new InvalidDataException($"Wire-only extension cannot unblock business authority: {id}");
            result[id] = JsonSerializer.SerializeToElement(new
            {
                operationId = id, method, path, owner = Text(operation, "canonicalOwner"),
                status = "PRODUCTION_CONTRACT_READY", actorRequired = actor, workspaceRequired,
                requiredCapability = Text(operation, "requiredCapability"), authentication,
                resourceScope = Text(operation, "resourceScope"), dataScope = Text(operation, "dataScope"),
                wireContractAuthority = authority, successStatus = Text(operation, "successStatus")
            });
        }
        return result;
    }

    private static string Text(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString()! : "";
}
