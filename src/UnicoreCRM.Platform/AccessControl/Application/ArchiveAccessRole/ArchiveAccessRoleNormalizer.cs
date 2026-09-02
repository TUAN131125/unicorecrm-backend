using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UnicoreCRM.Platform.AccessControl.Application.Common;
using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Platform.AccessControl.Application.ArchiveAccessRole;

/// <summary>
/// Wire deserialization and the <c>archiveAccessRole:v1</c> effective-request fingerprint. The
/// <c>If-Match</c> syntax lives in <see cref="AccessRoleCommandMetadata"/>, shared with the other
/// versioned role commands.
/// </summary>
internal static class ArchiveAccessRoleNormalizer
{
    private static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);

    internal static bool TryNormalize(
        string roleId,
        long expectedVersion,
        string rawBody,
        out NormalizedArchiveAccessRole? normalized,
        out IReadOnlyDictionary<string, string[]> errors)
    {
        normalized = null;
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        ArchiveAccessRoleRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<ArchiveAccessRoleRequest>(rawBody, JsonOptions);
        }
        catch (JsonException)
        {
            errors = new Dictionary<string, string[]> { ["body"] = ["The JSON request body does not match the contract."] };
            return false;
        }

        if (request is null)
        {
            errors = new Dictionary<string, string[]> { ["body"] = ["A JSON request body is required."] };
            return false;
        }

        // reason is explanatory governance provenance only. It is normalized by exactly the shared
        // optional-scalar rule - omitted, null or empty after Unicode-whitespace trimming all become
        // the canonical null - and it never influences authorization, the lifecycle decision or any
        // other business rule.
        var reason = AccessRoleInputNormalizer.OptionalText(request.Reason, 500, "reason", fields);

        if (fields.Count != 0)
        {
            errors = fields;
            return false;
        }

        // The target role and the expected version are part of effective command identity: the same
        // idempotency key aimed at another role, or replayed after the role moved on, is a different
        // command rather than a replay.
        var canonical = JsonSerializer.Serialize(new
        {
            schema = "archiveAccessRole:v1",
            roleId,
            expectedVersion,
            reason
        }, JsonOptions);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        normalized = new NormalizedArchiveAccessRole(roleId, expectedVersion, reason, fingerprint);
        errors = fields;
        return true;
    }
}
