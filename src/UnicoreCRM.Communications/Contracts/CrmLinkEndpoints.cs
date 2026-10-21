using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using UnicoreCRM.Communications.Application;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Communications.Contracts;

public static partial class CommunicationsEndpoints
{
    private static void MapCrmLinks(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/communications/conversations/{id}/crm-link", (HttpContext http, ConversationLinker linker, string id, CancellationToken ct) => Crm(http, linker, id, "read", ct))
            .RequireAuthorization().RequireTrustedWorkspace().WithName("getConversationCrmContext");
        endpoints.MapGet("/communications/conversations/{id}/crm-candidates", (HttpContext http, ConversationLinker linker, string id, CancellationToken ct) => Crm(http, linker, id, "search", ct))
            .RequireAuthorization().RequireTrustedWorkspace().WithName("searchConversationCrmCandidates");
        endpoints.MapPost("/communications/conversations/{id}/crm-link", (HttpContext http, ConversationLinker linker, string id, CancellationToken ct) => Crm(http, linker, id, "link", ct))
            .RequireAuthorization().RequireTrustedWorkspace().WithName("linkConversationCrmRecord");
        endpoints.MapPost("/communications/conversations/{id}/crm-link/unlink", (HttpContext http, ConversationLinker linker, string id, CancellationToken ct) => Crm(http, linker, id, "unlink", ct))
            .RequireAuthorization().RequireTrustedWorkspace().WithName("unlinkConversationCrmRecord");
    }

    private static async Task<IResult> Crm(HttpContext http, ConversationLinker linker, string id, string operation, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store, private";
        http.Response.Headers.Pragma = "no-cache";
        var request = new RecordAccessRequestContext(http.Request.Headers["X-Request-Id"].ToString(), http.Request.Headers["X-Correlation-Id"].ToString());
        try
        {
            if (!long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed < 1)
                throw new CommunicationReadException("COMMUNICATION_VALIDATION", 400);
            if (operation == "search")
            {
                if (http.Request.Query.Count != 2 || http.Request.Query.Any(x => x.Key is not ("targetType" or "search") || x.Value.Count != 1))
                    throw new CommunicationReadException("COMMUNICATION_VALIDATION", 400);
                var type = http.Request.Query["targetType"].ToString();
                var search = http.Request.Query["search"].ToString().Trim();
                if (type is not ("LEAD" or "CONTACT") || search.Length is < 1 or > 100 || search.Any(char.IsControl))
                    throw new CommunicationReadException("COMMUNICATION_VALIDATION", 400);
                return Results.Json(await linker.SearchAsync(parsed, type, search, request, ct));
            }
            if (http.Request.Query.Count != 0) throw new CommunicationReadException("COMMUNICATION_VALIDATION", 400);
            if (operation == "read") return Results.Json(await linker.ReadAsync(parsed, request, ct));
            if (!http.Request.HasJsonContentType()) throw new CommunicationReadException("COMMUNICATION_VALIDATION", 400);
            var bytes = new byte[4097];
            var count = 0;
            while (count < bytes.Length)
            {
                var read = await http.Request.Body.ReadAsync(bytes.AsMemory(count), ct);
                if (read == 0) break;
                count += read;
            }
            if (count > 4096) throw new CommunicationReadException("COMMUNICATION_VALIDATION", 400);
            using var document = JsonDocument.Parse(bytes.AsMemory(0, count));
            var body = document.RootElement;
            var names = operation == "unlink" ? new[] { "expectedVersion" } : ["targetType", "targetId", "expectedVersion"];
            if (body.ValueKind != JsonValueKind.Object || body.EnumerateObject().Count() != names.Length
                || body.EnumerateObject().Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != names.Length
                || body.EnumerateObject().Any(x => !names.Contains(x.Name, StringComparer.Ordinal))
                || !body.TryGetProperty("expectedVersion", out var version) || version.ValueKind != JsonValueKind.Number
                || !version.TryGetInt64(out var expected) || expected < 0)
                throw new CommunicationReadException("COMMUNICATION_VALIDATION", 400);
            string? targetType = null;
            string? targetId = null;
            if (operation == "link")
            {
                if (body.GetProperty("targetType").ValueKind != JsonValueKind.String || body.GetProperty("targetId").ValueKind != JsonValueKind.String)
                    throw new CommunicationReadException("COMMUNICATION_VALIDATION", 400);
                targetType = body.GetProperty("targetType").GetString();
                targetId = body.GetProperty("targetId").GetString();
                if (targetType is not ("LEAD" or "CONTACT") || targetId is null || !Regex.IsMatch(targetId, "^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant))
                    throw new CommunicationReadException("COMMUNICATION_VALIDATION", 400);
            }
            return Results.Json(await linker.ChangeAsync(parsed, targetType, targetId, expected, request, ct));
        }
        catch (JsonException) { return CrmError("COMMUNICATION_VALIDATION", 400, request.CorrelationId); }
        catch (CommunicationReadException error) { return CrmError(error.Code, error.Status, request.CorrelationId); }
    }

    private static IResult CrmError(string code, int status, string correlation) => Results.Json(
        new AccessProblemDetails($"urn:unicore:error:{code.ToLowerInvariant()}", "CRM link unavailable", status, code, false, correlation),
        statusCode: status, contentType: "application/problem+json");
}
