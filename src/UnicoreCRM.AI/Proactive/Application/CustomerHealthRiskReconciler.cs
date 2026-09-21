using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UnicoreCRM.Crm.Customers.Contracts;
using UnicoreCRM.PlatformOperations.AiExecution.Contracts;

namespace UnicoreCRM.AI.Proactive.Application;

internal sealed class CustomerHealthRiskReconciler(IProactiveStore store, TimeProvider clock)
{
    internal async Task ReconcileAsync(string workspaceId, ProactiveCustomerHealthFact fact, string correlationId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var active = await store.ReadActiveCycleAsync(workspaceId, fact.CustomerId, ProactiveValues.CustomerHealthRisk, ct);
        var severity = fact.HealthBand switch { "AT_RISK" => ProactiveValues.High, "CRITICAL" => ProactiveValues.Critical, _ => null };
        if (severity is null || string.Equals(fact.CustomerStatus, "ARCHIVED", StringComparison.Ordinal))
        {
            if (active is not null) await SaveAsync(active with { Status=ProactiveValues.Resolved, ResolvedAt=now, UpdatedAt=now, Version=active.Version+1 }, "ITEM_RESOLVED", correlationId, ct);
            return;
        }
        if (string.IsNullOrWhiteSpace(fact.OwnerMemberId))
        {
            if (active is not null) await SaveAsync(active with { Status=ProactiveValues.Resolved, ResolvedAt=now, UpdatedAt=now, Version=active.Version+1 }, "ITEM_RESOLVED", correlationId, ct);
            await store.RecordAuditAsync(new($"proaudit_{Guid.NewGuid():N}", workspaceId, null, "UNOWNED_TRIGGER_SUPPRESSED", null,
                fact.CustomerId, "{}", correlationId, now), ct);
            return;
        }
        var fingerprint = Fingerprint(fact.HealthBand!, fact.ReasonCode, fact.AlgorithmVersion);
        if (active is null)
        {
            var id = $"proactive_{Guid.NewGuid():N}";
            var created = new ProactiveItemState(id, workspaceId, fact.OwnerMemberId, ProactiveValues.CustomerHealthRisk,
                ProactiveValues.Customer, fact.CustomerId, severity, fact.ReasonCode ?? "PURCHASE_RECENCY_RISK", fingerprint,
                $"cycle_{Guid.NewGuid():N}", ProactiveValues.Open, now, now, null, null, null, null,
                fact.SourceVersion, 0, now, now);
            try { await SaveAsync(created, "ITEM_CREATED", correlationId, ct); }
            catch (ProactiveActiveCycleConflictException) { }
            return;
        }
        var ownerChanged = !string.Equals(active.OwnerMemberId, fact.OwnerMemberId, StringComparison.Ordinal);
        var escalated = active.Severity == ProactiveValues.High && severity == ProactiveValues.Critical;
        var status = ownerChanged || escalated ? ProactiveValues.Open : active.Status;
        var updated = active with { OwnerMemberId=fact.OwnerMemberId, Severity=severity, ReasonCode=fact.ReasonCode ?? active.ReasonCode,
            TriggerFingerprint=fingerprint, Status=status, LastDetectedAt=now, SeenAt=ownerChanged ? null : active.SeenAt,
            SnoozedUntil=ownerChanged || escalated ? null : active.SnoozedUntil, DismissedAt=ownerChanged || escalated ? null : active.DismissedAt,
            SourceVersion=fact.SourceVersion, Version=active.Version+1, UpdatedAt=now };
        await SaveAsync(updated, ownerChanged ? "ITEM_OWNER_CHANGED" : escalated ? "ITEM_ESCALATED" : "ITEM_REFRESHED", correlationId, ct);
    }

    private Task SaveAsync(ProactiveItemState item, string action, string correlationId, CancellationToken ct) =>
        store.SaveItemAsync(item, new($"proaudit_{Guid.NewGuid():N}", item.WorkspaceId, null, action, item.ItemId, item.SubjectId,
            JsonSerializer.Serialize(new { item.TriggerType, item.Severity, item.Status }), correlationId, clock.GetUtcNow()), ct);

    private static string Fingerprint(string band, string? reason, string? algorithm) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{band}\n{reason}\n{algorithm}"))).ToLowerInvariant();
}
