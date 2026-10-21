using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using UnicoreCRM.Communications.Application;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Communications.Contracts;

public static partial class CommunicationsEndpoints
{
    private static void MapChannels(IEndpointRouteBuilder endpoints)
    {
        MapTelegramConnections(endpoints);
        endpoints.MapGet("/communications/channel-providers", (HttpContext h, ChannelManagement app, CancellationToken ct) => ChannelExecute(h, app, "providers", null, ct))
            .RequireAuthorization().RequireTrustedWorkspace().WithName("listCommunicationChannelProviders");
        endpoints.MapGet("/communications/channel-accounts", (HttpContext h, ChannelManagement app, CancellationToken ct) => ChannelExecute(h, app, "list", null, ct))
            .RequireAuthorization().RequireTrustedWorkspace().WithName("listCommunicationChannelAccounts");
        endpoints.MapPost("/communications/channel-accounts", (HttpContext h, ChannelManagement app, CancellationToken ct) => ChannelExecute(h, app, "create", null, ct))
            .RequireAuthorization().RequireTrustedWorkspace().WithName("createCommunicationChannelAccount");
        endpoints.MapGet("/communications/channel-accounts/{id:guid}", (HttpContext h, ChannelManagement app, Guid id, CancellationToken ct) => ChannelExecute(h, app, "get", id, ct))
            .RequireAuthorization().RequireTrustedWorkspace().WithName("getCommunicationChannelAccount");
        endpoints.MapPatch("/communications/channel-accounts/{id:guid}", (HttpContext h, ChannelManagement app, Guid id, CancellationToken ct) => ChannelExecute(h, app, "rename", id, ct))
            .RequireAuthorization().RequireTrustedWorkspace().WithName("updateCommunicationChannelAccount");
        foreach (var operation in new[] { "disable", "enable", "disconnect" })
        {
            var action = operation;
            endpoints.MapPost($"/communications/channel-accounts/{{id:guid}}/{action}", (HttpContext h, ChannelManagement app, Guid id, CancellationToken ct) => ChannelExecute(h, app, action, id, ct))
                .RequireAuthorization().RequireTrustedWorkspace().WithName(action + "CommunicationChannelAccount");
        }
    }

    private static async Task<IResult> ChannelExecute(HttpContext http, ChannelManagement app, string operation, Guid? id, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store, private";
        http.Response.Headers.Pragma = "no-cache";
        var correlation = http.Response.Headers["X-Correlation-Id"].ToString();
        try
        {
            var keys = operation == "list" ? new[] { "limit", "cursor", "provider", "status" } : [];
            if (http.Request.Query.Any(x => !keys.Contains(x.Key, StringComparer.Ordinal) || x.Value.Count != 1))
                throw new CommunicationReadException("COMMUNICATION_VALIDATION", 400);
            if (operation == "providers") return Results.Json(await app.Providers(correlation, ct));
            if (operation == "get") return Results.Json(await app.Get(id!.Value, correlation, ct));
            if (operation == "list")
            {
                var limit = 25;
                if (http.Request.Query.TryGetValue("limit", out var raw) && !int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out limit))
                    throw new CommunicationReadException("COMMUNICATION_VALIDATION", 400);
                string? Query(string key) => http.Request.Query.TryGetValue(key, out var value) ? value.ToString() : null;
                return Results.Json(await app.List(limit, Query("cursor"), Query("provider"), Query("status"), correlation, ct));
            }
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
            using var doc = JsonDocument.Parse(bytes.AsMemory(0, count));
            var body = doc.RootElement;
            var fields = operation == "create" ? new[] { "providerKey", "displayName" } : operation == "rename" ? ["displayName", "expectedVersion"] : ["expectedVersion"];
            if (body.ValueKind != JsonValueKind.Object || body.EnumerateObject().Count() != fields.Length
                || body.EnumerateObject().Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != fields.Length
                || body.EnumerateObject().Any(x => !fields.Contains(x.Name, StringComparer.Ordinal)))
                throw new CommunicationReadException("COMMUNICATION_VALIDATION", 400);
            string? Text(string field)
            {
                if (!body.TryGetProperty(field, out var p)) return null;
                if (p.ValueKind != JsonValueKind.String) throw new CommunicationReadException("COMMUNICATION_VALIDATION", 400);
                return p.GetString();
            }
            long version = 0;
            if (operation != "create" && (!body.TryGetProperty("expectedVersion", out var v) || v.ValueKind != JsonValueKind.Number || !v.TryGetInt64(out version)))
                throw new CommunicationReadException("COMMUNICATION_VALIDATION", 400);
            var key = http.Request.Headers["Idempotency-Key"];
            if (key.Count != 1) throw new CommunicationReadException("COMMUNICATION_VALIDATION", 400);
            var response = await app.Mutate(operation, id, Text("providerKey"), Text("displayName"), version, key.ToString(), correlation, ct);
            return Results.Json(response, statusCode: operation == "create" && !response.Replayed ? 201 : 200);
        }
        catch (JsonException) { return ChannelError("COMMUNICATION_VALIDATION", 400, correlation); }
        catch (CommunicationReadException e) { return ChannelError(e.Code, e.Status, correlation); }
    }
    private static IResult ChannelError(string code, int status, string correlation) => Results.Json(
        new AccessProblemDetails($"urn:unicore:error:{code.ToLowerInvariant()}", "Channel operation unavailable", status, code, code == "CHANNEL_REQUEST_BUSY", correlation),
        statusCode: status, contentType: "application/problem+json");
}
