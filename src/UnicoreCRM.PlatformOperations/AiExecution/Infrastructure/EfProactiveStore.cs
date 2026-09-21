using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using UnicoreCRM.PlatformOperations.AiExecution.Contracts;

namespace UnicoreCRM.PlatformOperations.AiExecution.Infrastructure;

internal sealed class EfProactiveStore(AiExecutionDbContext db) : IProactiveStore
{
    public async Task<WorkspaceProactivePolicyState?> ReadPolicyAsync(string workspaceId, CancellationToken ct) =>
        Map(await db.ProactivePolicies.AsNoTracking().SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId, ct));

    public async Task<IReadOnlyList<WorkspaceProactivePolicyState>> ReadDuePoliciesAsync(DateTimeOffset now, int limit, CancellationToken ct) =>
        (await db.ProactivePolicies.AsNoTracking()
            .Where(x => x.Enabled && (x.NextEvaluationAt == null || x.NextEvaluationAt <= now)
                && (x.LeaseExpiresAt == null || x.LeaseExpiresAt <= now))
            .OrderBy(x => x.NextEvaluationAt).ThenBy(x => x.WorkspaceId)
            .Take(Math.Clamp(limit, 1, 100)).ToListAsync(ct)).Select(x => Map(x)!).ToArray();

    public async Task<bool> TryClaimPolicyAsync(string workspaceId, long version, string leaseId, DateTimeOffset now, DateTimeOffset leaseUntil, CancellationToken ct) =>
        await db.ProactivePolicies.Where(x => x.WorkspaceId == workspaceId && x.Enabled && x.Version == version
                && (x.LeaseExpiresAt == null || x.LeaseExpiresAt <= now))
            .ExecuteUpdateAsync(update => update
                .SetProperty(x => x.LeaseId, leaseId)
                .SetProperty(x => x.LeaseExpiresAt, leaseUntil), ct) == 1;

    public async Task CompletePolicyEvaluationAsync(string workspaceId, string leaseId, DateTimeOffset evaluatedAt, DateTimeOffset nextEvaluationAt, CancellationToken ct)
    {
        var changed = await db.ProactivePolicies.Where(x => x.WorkspaceId == workspaceId && x.LeaseId == leaseId)
            .ExecuteUpdateAsync(update => update
                .SetProperty(x => x.LastEvaluationAt, evaluatedAt)
                .SetProperty(x => x.NextEvaluationAt, nextEvaluationAt)
                .SetProperty(x => x.LeaseId, (string?)null)
                .SetProperty(x => x.LeaseExpiresAt, (DateTimeOffset?)null), ct);
        if (changed != 1) throw new InvalidOperationException("The proactive evaluation lease is not owned by this worker.");
    }

    public async Task<ProactiveItemState?> ReadActiveCycleAsync(string workspaceId, string subjectId, string triggerType, CancellationToken ct) =>
        Map(await db.ProactiveItems.AsNoTracking().SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId
            && x.SubjectType == ProactiveValues.Customer && x.SubjectId == subjectId && x.TriggerType == triggerType
            && x.Status != ProactiveValues.Resolved, ct));

    public async Task<IReadOnlyList<ProactiveItemState>> ReadOwnerItemsAsync(string workspaceId, string ownerMemberId, string status, int limit, CancellationToken ct) =>
        (await db.ProactiveItems.AsNoTracking().Where(x => x.WorkspaceId == workspaceId && x.OwnerMemberId == ownerMemberId && x.Status == status)
            .OrderByDescending(x => x.Severity).ThenByDescending(x => x.LastDetectedAt).ThenBy(x => x.ItemId)
            .Take(Math.Clamp(limit, 1, 100)).ToListAsync(ct)).Select(x => Map(x)!).ToArray();

    public async Task<ProactiveItemState?> ReadItemAsync(string workspaceId, string itemId, CancellationToken ct) =>
        Map(await db.ProactiveItems.AsNoTracking().SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.ItemId == itemId, ct));

    public async Task SavePolicyAsync(WorkspaceProactivePolicyState policy, string idempotencyKey, string fingerprint, ProactiveAuditEvidence audit, CancellationToken ct)
    {
        var scopeKey = $"{policy.WorkspaceId}:{audit.MemberId}:saveProactiveConfiguration:{idempotencyKey}";
        var replay = await db.ProactiveCommands.AsNoTracking().SingleOrDefaultAsync(x => x.ScopeKey == scopeKey, ct);
        if (replay is not null)
        {
            if (!string.Equals(replay.Fingerprint, fingerprint, StringComparison.Ordinal)) throw new ProactiveCommandConflictException();
            return;
        }
        var row = await db.ProactivePolicies.SingleOrDefaultAsync(x => x.WorkspaceId == policy.WorkspaceId, ct);
        if (row is null) db.ProactivePolicies.Add(Row(policy));
        else Copy(policy, row);
        db.ProactiveCommands.Add(new() { ScopeKey = scopeKey, WorkspaceId = policy.WorkspaceId, MemberId = audit.MemberId ?? "system",
            Operation = "saveProactiveConfiguration", IdempotencyKey = idempotencyKey, Fingerprint = fingerprint,
            ResultJson = System.Text.Json.JsonSerializer.Serialize(policy), CreatedAt = audit.OccurredAt });
        db.ProactiveAudits.Add(Row(audit));
        await db.SaveChangesAsync(ct);
    }

    public async Task SaveItemAsync(ProactiveItemState item, ProactiveAuditEvidence audit, CancellationToken ct)
    {
        var row = await db.ProactiveItems.SingleOrDefaultAsync(x => x.ItemId == item.ItemId, ct);
        if (row is null) db.ProactiveItems.Add(Row(item));
        else Copy(item, row);
        db.ProactiveAudits.Add(Row(audit));
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException exception) when (ContainsUniqueConflict(exception))
        {
            db.ChangeTracker.Clear();
            throw new ProactiveActiveCycleConflictException();
        }
    }
    public async Task RecordAuditAsync(ProactiveAuditEvidence audit, CancellationToken ct)
    { db.ProactiveAudits.Add(Row(audit)); await db.SaveChangesAsync(ct); }

    private static WorkspaceProactivePolicyState? Map(WorkspaceProactivePolicyRow? x) => x is null ? null :
        new(x.WorkspaceId, x.Enabled, x.Version, x.LastEvaluationAt, x.NextEvaluationAt, x.LeaseId, x.LeaseExpiresAt, x.UpdatedBy, x.UpdatedAt);
    private static ProactiveItemState? Map(ProactiveItemRow? x) => x is null ? null :
        new(x.ItemId, x.WorkspaceId, x.OwnerMemberId, x.TriggerType, x.SubjectType, x.SubjectId, x.Severity, x.ReasonCode,
            x.TriggerFingerprint, x.RiskCycleKey, x.Status, x.FirstDetectedAt, x.LastDetectedAt, x.SeenAt, x.SnoozedUntil,
            x.DismissedAt, x.ResolvedAt, x.SourceVersion, x.Version, x.CreatedAt, x.UpdatedAt);
    private static WorkspaceProactivePolicyRow Row(WorkspaceProactivePolicyState x) { var row = new WorkspaceProactivePolicyRow(); Copy(x, row); return row; }
    private static void Copy(WorkspaceProactivePolicyState x, WorkspaceProactivePolicyRow row)
    { row.WorkspaceId=x.WorkspaceId; row.Enabled=x.Enabled; row.Version=x.Version; row.LastEvaluationAt=x.LastEvaluationAt; row.NextEvaluationAt=x.NextEvaluationAt; row.LeaseId=x.LeaseId; row.LeaseExpiresAt=x.LeaseExpiresAt; row.UpdatedBy=x.UpdatedBy; row.UpdatedAt=x.UpdatedAt; }
    private static ProactiveItemRow Row(ProactiveItemState x) { var row = new ProactiveItemRow(); Copy(x, row); return row; }
    private static void Copy(ProactiveItemState x, ProactiveItemRow row)
    { row.ItemId=x.ItemId; row.WorkspaceId=x.WorkspaceId; row.OwnerMemberId=x.OwnerMemberId; row.TriggerType=x.TriggerType; row.SubjectType=x.SubjectType; row.SubjectId=x.SubjectId; row.Severity=x.Severity; row.ReasonCode=x.ReasonCode; row.TriggerFingerprint=x.TriggerFingerprint; row.RiskCycleKey=x.RiskCycleKey; row.Status=x.Status; row.FirstDetectedAt=x.FirstDetectedAt; row.LastDetectedAt=x.LastDetectedAt; row.SeenAt=x.SeenAt; row.SnoozedUntil=x.SnoozedUntil; row.DismissedAt=x.DismissedAt; row.ResolvedAt=x.ResolvedAt; row.SourceVersion=x.SourceVersion; row.Version=x.Version; row.CreatedAt=x.CreatedAt; row.UpdatedAt=x.UpdatedAt; }
    private static ProactiveAuditRow Row(ProactiveAuditEvidence x) => new() { AuditId=x.AuditId, WorkspaceId=x.WorkspaceId, MemberId=x.MemberId,
        Action=x.Action, ItemId=x.ItemId, SubjectId=x.SubjectId, SafeSummaryJson=x.SafeSummaryJson, CorrelationId=x.CorrelationId, OccurredAt=x.OccurredAt };
    private static bool ContainsUniqueConflict(Exception exception)
    { for (Exception? current=exception; current is not null; current=current.InnerException) if (current is SqlException sql && sql.Number is 2601 or 2627) return true; return false; }
}

public sealed class ProactiveCommandConflictException : Exception;
