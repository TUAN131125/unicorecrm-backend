using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using UnicoreCRM.Communications.Application;
using UnicoreCRM.Communications.Infrastructure.Telegram;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Communications.Contracts;

public static partial class CommunicationsEndpoints
{
    private static void MapTelegramConnections(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/communications/channel-accounts/{id:guid}/connection", (HttpContext h, TelegramConnectionManagement app, ITelegramCredentialCustody custody, Guid id, CancellationToken ct) => ExecuteTelegram(h, app, custody, id, "get", ct))
            .RequireAuthorization().RequireTrustedWorkspace().WithName("getTelegramConnection");
        endpoints.MapPut("/communications/channel-accounts/{id:guid}/credentials", (HttpContext h, TelegramConnectionManagement app, ITelegramCredentialCustody custody, Guid id, CancellationToken ct) => ExecuteTelegram(h, app, custody, id, "put", ct))
            .RequireAuthorization().RequireTrustedWorkspace().WithName("replaceTelegramCredential");
        endpoints.MapDelete("/communications/channel-accounts/{id:guid}/credentials", (HttpContext h, TelegramConnectionManagement app, ITelegramCredentialCustody custody, Guid id, CancellationToken ct) => ExecuteTelegram(h, app, custody, id, "remove", ct))
            .RequireAuthorization().RequireTrustedWorkspace().WithName("removeTelegramCredential");
        endpoints.MapPost("/communications/channel-accounts/{id:guid}/verify", (HttpContext h, TelegramConnectionManagement app, ITelegramCredentialCustody custody, Guid id, CancellationToken ct) => ExecuteTelegram(h, app, custody, id, "verify", ct))
            .RequireAuthorization().RequireTrustedWorkspace().WithName("verifyTelegramConnection");
        foreach (var operation in new[] { "activate", "deactivate", "reconnect" })
            endpoints.MapPost("/communications/channel-accounts/{id:guid}/" + operation, (HttpContext h, TelegramConnectionManagement app, ITelegramCredentialCustody custody, Guid id, CancellationToken ct) => ExecuteTelegram(h, app, custody, id, operation, ct))
                .RequireAuthorization().RequireTrustedWorkspace().WithName(operation + "TelegramConnection");
    }

    private static async Task<IResult> ExecuteTelegram(HttpContext http, TelegramConnectionManagement app,
        ITelegramCredentialCustody custody, Guid id, string operation, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store, private";
        http.Response.Headers.Pragma = "no-cache";
        var correlation = http.Response.Headers["X-Correlation-Id"].ToString();
        try
        {
            if (http.Request.Query.Count != 0) throw new CommunicationReadException("COMMUNICATION_VALIDATION", 400);
            if (operation == "get")
            {
                var result = await app.GetAsync(id, correlation, ct);
                return Results.Json(result);
            }
            if (operation is "verify" or "activate" or "reconnect")
            {
                var status = await app.GetAsync(id, correlation, ct);
                if (!status.ProviderAllowed) throw new CommunicationReadException("LIVE_NOT_ADMITTED", 503);
            }
            // Synthetic exemption is an internal DI admission in a loopback host, never a request header/Development flag.
            if (!http.Request.IsHttps && !(custody.SyntheticReady && http.Connection.RemoteIpAddress is { } ip && System.Net.IPAddress.IsLoopback(ip)))
                throw new CommunicationReadException("SECURITY_NOT_READY", 503);
            if (!http.Request.HasJsonContentType()) throw new CommunicationReadException("COMMUNICATION_VALIDATION", 400);
            var bytes = new byte[4097]; var count = 0;
            while (count < bytes.Length)
            {
                var read = await http.Request.Body.ReadAsync(bytes.AsMemory(count), ct);
                if (read == 0) break; count += read;
            }
            if (count > 4096) throw new CommunicationReadException("COMMUNICATION_VALIDATION", 400);
            using var document = JsonDocument.Parse(bytes.AsMemory(0, count));
            var body = document.RootElement;
            var fields = operation == "put" ? new[] { "expectedVersion", "botToken" } : ["expectedVersion"];
            if (body.ValueKind != JsonValueKind.Object || body.EnumerateObject().Count() != fields.Length
                || body.EnumerateObject().Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != fields.Length
                || body.EnumerateObject().Any(p => !fields.Contains(p.Name, StringComparer.Ordinal))
                || !body.TryGetProperty("expectedVersion", out var v) || v.ValueKind != JsonValueKind.Number || !v.TryGetInt64(out var version))
                throw new CommunicationReadException("COMMUNICATION_VALIDATION", 400);
            string? token = null;
            if (operation == "put")
            {
                var value = body.GetProperty("botToken");
                if (value.ValueKind != JsonValueKind.String) throw new CommunicationReadException("COMMUNICATION_VALIDATION", 400);
                token = value.GetString();
            }
            var key = http.Request.Headers["Idempotency-Key"];
            if (key.Count != 1) throw new CommunicationReadException("COMMUNICATION_VALIDATION", 400);
            if (operation is "verify" or "activate" or "deactivate" or "reconnect")
                return Results.Json(await app.CommandAsync(id, operation, version, key.ToString(), correlation, ct));
            return Results.Json(await app.MutateAsync(id, token, version, key.ToString(), correlation, ct));
        }
        catch (JsonException) { return ChannelError("COMMUNICATION_VALIDATION", 400, correlation); }
        catch (CommunicationReadException error) { return ChannelError(error.Code, error.Status, correlation); }
        catch (TelegramFailure error) { return ChannelError(error.Code,
            error.Code is "BOT_IDENTITY_CONFLICT" or "BOT_REBIND_REQUIRED" or "CONNECTION_GENERATION_STALE" ? 409 : 503, correlation); }
    }
}
