using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using UnicoreCRM.Communications.Application;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Communications.Contracts;

public static partial class CommunicationsEndpoints
{
    public static IEndpointRouteBuilder MapCommunicationsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        MapCrmLinks(endpoints);
        MapChannels(endpoints);
        endpoints.MapPost("/communications/conversations/{id}/claim", Claim)
            .RequireAuthorization().RequireTrustedWorkspace().WithName("claimConversation");
        endpoints.MapPost("/communications/conversations/{id}/transition", Transition)
            .RequireAuthorization().RequireTrustedWorkspace().WithName("transitionConversation");
        endpoints.MapGet("/communications/conversations", (HttpContext http, CommunicationsReader reader, CancellationToken ct) => Execute(http, reader, "list", null, ct))
            .RequireAuthorization().RequireTrustedWorkspace().WithName("listConversations");
        endpoints.MapGet("/communications/conversations/{id}", (HttpContext http, CommunicationsReader reader, string id, CancellationToken ct) => Execute(http, reader, "detail", id, ct))
            .RequireAuthorization().RequireTrustedWorkspace().WithName("getConversation");
        endpoints.MapGet("/communications/conversations/{id}/messages", (HttpContext http, CommunicationsReader reader, string id, CancellationToken ct) => Execute(http, reader, "messages", id, ct))
            .RequireAuthorization().RequireTrustedWorkspace().WithName("listConversationMessages");
        return endpoints;
    }

    private static async Task<IResult> Transition(HttpContext http, ConversationTransitioner handler, string id, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store, private";
        http.Response.Headers.Pragma = "no-cache";
        var request = new RecordAccessRequestContext(http.Request.Headers["X-Request-Id"].ToString(),
            http.Response.Headers["X-Correlation-Id"].ToString());
        try
        {
            if (http.Request.Query.Count != 0 || !http.Request.HasJsonContentType()
                || !long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed < 1)
                throw new CommunicationReadException("COMMUNICATION_VALIDATION", 400);
            // Bounded parsing also handles chunked/HTTP2 bodies; duplicate fields are rejected.
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
            if (body.ValueKind != JsonValueKind.Object || body.EnumerateObject().Count() != 2
                || body.EnumerateObject().Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != 2
                || !body.TryGetProperty("targetStatus", out var target) || target.ValueKind != JsonValueKind.String
                || !Domain.Conversation.IsStatus(target.GetString()!)
                || !body.TryGetProperty("expectedVersion", out var version) || version.ValueKind != JsonValueKind.Number
                || !version.TryGetInt64(out var expected) || expected < 0)
                throw new CommunicationReadException("COMMUNICATION_VALIDATION", 400);
            return Results.Json(await handler.TransitionAsync(parsed, target.GetString()!, expected, request, ct));
        }
        catch (JsonException) { return TransitionError("COMMUNICATION_VALIDATION", 400, request.CorrelationId); }
        catch (CommunicationReadException error) { return TransitionError(error.Code, error.Status, request.CorrelationId); }
    }

    private static IResult TransitionError(string code, int status, string correlation) => Results.Json(
        new AccessProblemDetails($"urn:unicore:error:{code.ToLowerInvariant()}", "Transition unavailable", status, code,
            false, correlation), statusCode: status, contentType: "application/problem+json");

    private static async Task<IResult> Claim(HttpContext http, ConversationClaimer claimer, string id, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store, private";
        http.Response.Headers.Pragma = "no-cache";
        var request = new RecordAccessRequestContext(http.Request.Headers["X-Request-Id"].ToString(),
            http.Response.Headers["X-Correlation-Id"].ToString());
        try
        {
            // A conditional null-to-self transition, with no client actor/Workspace/assignee.
            if (http.Request.Query.Count != 0 || http.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody == true
                || !long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed < 1)
                throw new CommunicationReadException("COMMUNICATION_VALIDATION", 400);
            return Results.Json(await claimer.ClaimAsync(parsed, request, ct));
        }
        catch (CommunicationReadException error)
        {
            return Results.Json(new AccessProblemDetails($"urn:unicore:error:{error.Code.ToLowerInvariant()}",
                error.Status == 409 ? "Conversation is no longer available to claim" : "Claim unavailable",
                error.Status, error.Code, false, request.CorrelationId), statusCode: error.Status, contentType: "application/problem+json");
        }
    }

    private static async Task<IResult> Execute(HttpContext http, CommunicationsReader reader, string operation, string? rawId, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store, private";
        http.Response.Headers.Pragma = "no-cache";
        var request = new RecordAccessRequestContext(http.Request.Headers["X-Request-Id"].ToString(),
            http.Response.Headers["X-Correlation-Id"].ToString());
        try
        {
            var supported = operation == "list" ? new[] { "view", "statusView", "limit", "cursor" } : operation == "messages" ? ["limit", "cursor"] : Array.Empty<string>();
            if (http.Request.Query.Any(p => !supported.Contains(p.Key, StringComparer.Ordinal) || p.Value.Count != 1))
                throw new CommunicationReadException("COMMUNICATION_VALIDATION", 400);
            var limit = 25;
            if (http.Request.Query.TryGetValue("limit", out var supplied) && (!int.TryParse(supplied, NumberStyles.None, CultureInfo.InvariantCulture, out limit) || limit is < 1 or > 100))
                throw new CommunicationReadException("COMMUNICATION_VALIDATION", 400);
            var view = http.Request.Query["view"].ToString();
            if (operation == "list" && view is not ("" or "assigned" or "unassigned")) throw new CommunicationReadException("COMMUNICATION_VALIDATION", 400);
            long id = 0;
            if (rawId is not null && (!long.TryParse(rawId, NumberStyles.None, CultureInfo.InvariantCulture, out id) || id < 1))
                throw new CommunicationReadException("COMMUNICATION_VALIDATION", 400);
            var cursor = http.Request.Query.TryGetValue("cursor", out var token) ? token.ToString() : null;
            object result = operation switch
            {
                "list" => await reader.ListAsync(view == "unassigned", limit, cursor, request, ct,
                    http.Request.Query.TryGetValue("statusView", out var statusView) ? statusView.ToString() : "active"),
                "detail" => await reader.GetAsync(id, request, ct),
                _ => await reader.MessagesAsync(id, limit, cursor, request, ct)
            };
            return Results.Json(result);
        }
        catch (CommunicationReadException error)
        {
            return Results.Json(new AccessProblemDetails($"urn:unicore:error:{error.Code.ToLowerInvariant()}",
                error.Status == 403 ? "Access denied" : error.Status == 404 ? "Conversation not found" : "Invalid request",
                error.Status, error.Code, false, request.CorrelationId), statusCode: error.Status, contentType: "application/problem+json");
        }
    }
}
