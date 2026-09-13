using System.Data;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UnicoreCRM.Integrations.Infrastructure.Persistence;
using UnicoreCRM.Integrations.Webhooks.Outbound.Domain;
using UnicoreCRM.PlatformOperations.Outbox;

namespace UnicoreCRM.Integrations.Webhooks.Outbound.Infrastructure;

internal interface IWebhookHostResolver { Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct); }
internal sealed class DnsWebhookHostResolver : IWebhookHostResolver
{ public Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct) => Dns.GetHostAddressesAsync(host, ct); }

internal static class WebhookDestinationPolicy
{
    internal static bool IsPublic(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any) || ip.IsIPv6Multicast || ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal) return false;
        if (ip.IsIPv4MappedToIPv6) return IsPublic(ip.MapToIPv4());
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            if (b[0] is 0 or 10 or 127 || b[0] >= 224 || b[0] == 169 && b[1] == 254 || b[0] == 172 && b[1] is >= 16 and <= 31 || b[0] == 192 && b[1] == 168 || b[0] == 100 && b[1] is >= 64 and <= 127 || b[0] == 198 && b[1] is >= 18 and <= 19) return false;
        }
        else if (ip.AddressFamily == AddressFamily.InterNetworkV6 && (ip.GetAddressBytes()[0] & 0xfe) == 0xfc) return false;
        return true;
    }

    internal static async Task<bool> IsSafeAsync(Uri uri, IWebhookHostResolver resolver, CancellationToken ct)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo)) return false;
        try { var addresses = await resolver.ResolveAsync(uri.DnsSafeHost, ct); return addresses.Length > 0 && addresses.All(IsPublic); }
        catch { return false; }
    }
}

internal interface IOutboundWebhookTransport
{
    Task<(int? Status, string? Error)> SendAsync(Uri endpoint, string deliveryId, string eventId, string eventType, string payload, string secret, CancellationToken ct);
}

internal sealed class SafeOutboundWebhookTransport(IWebhookHostResolver resolver, TimeProvider clock) : IOutboundWebhookTransport
{
    public async Task<(int?, string?)> SendAsync(Uri endpoint, string deliveryId, string eventId, string eventType, string payload, string secret, CancellationToken ct)
    {
        using var handler = new SocketsHttpHandler { AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromSeconds(10) };
        handler.ConnectCallback = async (context, token) =>
        {
            var addresses = await resolver.ResolveAsync(context.DnsEndPoint.Host, token);
            var address = addresses.FirstOrDefault(WebhookDestinationPolicy.IsPublic) ?? throw new HttpRequestException("WEBHOOK_DESTINATION_BLOCKED");
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try { await socket.ConnectAsync(address, context.DnsEndPoint.Port, token); return new NetworkStream(socket, true); }
            catch { socket.Dispose(); throw; }
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        var bytes = Encoding.UTF8.GetBytes(payload);
        var timestamp = clock.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var signature = CreateSignature(timestamp, deliveryId, bytes, secret);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = new ByteArrayContent(bytes) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Add("X-Unicore-Delivery-Id", deliveryId); request.Headers.Add("X-Unicore-Timestamp", timestamp);
        request.Headers.Add("X-Unicore-Signature", $"sha256={signature}"); request.Headers.Add("X-Unicore-Event-Id", eventId);
        request.Headers.Add("X-Unicore-Event-Type", eventType); request.Headers.Add("X-Unicore-Event-Version", "1");
        try { using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct); return ((int)response.StatusCode, null); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return (null, "TIMEOUT"); }
        catch (HttpRequestException ex) { return (null, ex.ToString().Contains("WEBHOOK_DESTINATION_BLOCKED", StringComparison.Ordinal) ? "DESTINATION_BLOCKED" : "NETWORK_ERROR"); }
    }

    internal static string CreateSignature(string timestamp, string deliveryId, byte[] payload, string secret)
    {
        var prefix = Encoding.UTF8.GetBytes($"{timestamp}\n{deliveryId}\n");
        var material = new byte[prefix.Length + payload.Length]; prefix.CopyTo(material, 0); payload.CopyTo(material, prefix.Length);
        return Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), material)).ToLowerInvariant();
    }
}

internal sealed class OutboundWebhookConsumerProcessor(IntegrationsDbContext db, IIntegrationEventFeed feed, TimeProvider clock)
{
    internal const string ConsumerId = "outbound-webhooks-v1";
    internal async Task<bool> ProcessOnceAsync(CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var cursor = await db.OutboundWebhookConsumerCursors.SingleOrDefaultAsync(x => x.ConsumerId == ConsumerId, ct);
        if (cursor is null)
        {
            var now = clock.GetUtcNow(); db.OutboundWebhookConsumerCursors.Add(new(ConsumerId, await feed.GetTailSequenceAsync(ct), now));
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return true;
        }
        var events = await feed.ReadAfterAsync(cursor.LastProcessedSequence, 100, ct);
        if (events.Count == 0) { await tx.CommitAsync(ct); return false; }
        foreach (var item in events)
        {
            var subscriptions = await db.OutboundWebhookSubscriptions.Where(x => x.WorkspaceId == item.WorkspaceId && x.EventType == item.EventType && (x.Status == "ACTIVE" || x.Status == "PAUSED")).ToArrayAsync(ct);
            var existing = await db.OutboundWebhookDeliveries.Where(x => x.EventId == item.EventId).Select(x => x.SubscriptionId).ToArrayAsync(ct);
            foreach (var subscription in subscriptions.Where(x => !existing.Contains(x.SubscriptionId))) db.OutboundWebhookDeliveries.Add(new(subscription, item.EventId, item.Sequence, item.EventType, item.CanonicalEnvelopeJson, clock.GetUtcNow()));
            cursor.Advance(item.Sequence, clock.GetUtcNow());
        }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return true;
    }
}

internal sealed class OutboundWebhookConsumer(IServiceScopeFactory scopes, TimeProvider clock, ILogger<OutboundWebhookConsumer> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested) try
        {
            await using var scope = scopes.CreateAsyncScope();
            var processor = new OutboundWebhookConsumerProcessor(scope.ServiceProvider.GetRequiredService<IntegrationsDbContext>(), scope.ServiceProvider.GetRequiredService<IIntegrationEventFeed>(), clock);
            if (!await processor.ProcessOnceAsync(stop)) await Task.Delay(TimeSpan.FromSeconds(1), clock, stop);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { return; }
        catch (Exception ex) { log.LogError(ex, "Outbound webhook event consumer failed."); await Task.Delay(TimeSpan.FromSeconds(2), clock, stop); }
    }
}

internal sealed class OutboundWebhookSenderProcessor(IntegrationsDbContext db, IDataProtectionProvider protection, IOutboundWebhookTransport transport, TimeProvider clock, ILogger log)
{
    internal async Task<bool> ProcessOnceAsync(CancellationToken ct)
    {
        string deliveryId, attemptId;
        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct))
        {
            var now = clock.GetUtcNow();
            var delivery = await db.OutboundWebhookDeliveries.Join(db.OutboundWebhookSubscriptions.Where(s => s.Status == "ACTIVE"), d => d.SubscriptionId, s => s.SubscriptionId, (d, _) => d)
                .Where(d => (d.Status == "PENDING" || d.Status == "RETRY_SCHEDULED" || d.Status == "IN_FLIGHT" && d.LeaseExpiresAt <= now) && d.NextAttemptAt <= now).OrderBy(d => d.NextAttemptAt).FirstOrDefaultAsync(ct);
            if (delivery is null) { await tx.CommitAsync(ct); return false; }
            if (delivery.Status == "IN_FLIGHT" && delivery.ExecutionAttemptId is { } expiredId)
            {
                var expired = await db.OutboundWebhookDeliveryAttempts.SingleAsync(x => x.AttemptId == expiredId, ct);
                expired.Complete(now, "LEASE_EXPIRED", null, "LEASE_EXPIRED");
            }
            attemptId = Guid.NewGuid().ToString("N"); delivery.Lease(attemptId, now, now.AddMinutes(2));
            db.OutboundWebhookDeliveryAttempts.Add(new(attemptId, delivery.DeliveryId, delivery.AttemptCount, now)); deliveryId = delivery.DeliveryId;
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        db.ChangeTracker.Clear();
        var leased = await db.OutboundWebhookDeliveries.SingleAsync(x => x.DeliveryId == deliveryId, ct);
        var subscription = await db.OutboundWebhookSubscriptions.SingleAsync(x => x.SubscriptionId == leased.SubscriptionId, ct);
        var secret = protection.CreateProtector("UnicoreCRM.Integrations.Webhooks.Outbound.SigningSecret.v1").Unprotect(subscription.ProtectedSecret);
        var result = await transport.SendAsync(new Uri(subscription.EndpointUrl), leased.DeliveryId, leased.EventId, leased.EventType, leased.CanonicalPayloadJson, secret, ct);
        var completedAt = clock.GetUtcNow(); var success = result.Status is >= 200 and <= 299; var retryable = result.Status is null or 408 or 425 or 429 or >= 500;
        db.ChangeTracker.Clear();
        leased = await db.OutboundWebhookDeliveries.SingleAsync(x => x.DeliveryId == deliveryId, ct);
        try
        {
            if (success) leased.Succeed(attemptId, completedAt, result.Status!.Value); else leased.Fail(attemptId, completedAt, result.Status, result.Error ?? $"HTTP_{result.Status}", retryable);
            var attempt = await db.OutboundWebhookDeliveryAttempts.SingleAsync(x => x.AttemptId == attemptId, ct);
            attempt.Complete(completedAt, success ? "SUCCEEDED" : retryable ? "RETRY_SCHEDULED" : "DEAD_LETTER", result.Status, result.Error ?? (success ? null : $"HTTP_{result.Status}"));
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is InvalidOperationException or DbUpdateConcurrencyException) { db.ChangeTracker.Clear(); log.LogWarning("Stale webhook execution {AttemptId} could not update delivery {DeliveryId}", attemptId, deliveryId); }
        return true;
    }
}

internal sealed class OutboundWebhookSender(IServiceScopeFactory scopes, TimeProvider clock, ILogger<OutboundWebhookSender> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested) try
        {
            await using var scope = scopes.CreateAsyncScope();
            var processor = new OutboundWebhookSenderProcessor(scope.ServiceProvider.GetRequiredService<IntegrationsDbContext>(), scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>(), scope.ServiceProvider.GetRequiredService<IOutboundWebhookTransport>(), clock, log);
            if (!await processor.ProcessOnceAsync(stop)) await Task.Delay(TimeSpan.FromSeconds(1), clock, stop);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { return; }
        catch (Exception ex) { log.LogError(ex, "Outbound webhook sender failed."); await Task.Delay(TimeSpan.FromSeconds(2), clock, stop); }
    }
}
