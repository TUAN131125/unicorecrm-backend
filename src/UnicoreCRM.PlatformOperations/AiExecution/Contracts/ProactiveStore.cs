namespace UnicoreCRM.PlatformOperations.AiExecution.Contracts;

public static class ProactiveValues
{
    public const string CustomerHealthRisk = "CUSTOMER_HEALTH_RISK";
    public const string Customer = "CUSTOMER";
    public const string High = "HIGH";
    public const string Critical = "CRITICAL";
    public const string Open = "OPEN";
    public const string Snoozed = "SNOOZED";
    public const string Dismissed = "DISMISSED";
    public const string Resolved = "RESOLVED";
}

public sealed record ProactiveItemState(
    string ItemId, string WorkspaceId, string OwnerMemberId, string TriggerType,
    string SubjectType, string SubjectId, string Severity, string ReasonCode,
    string TriggerFingerprint, string RiskCycleKey, string Status,
    DateTimeOffset FirstDetectedAt, DateTimeOffset LastDetectedAt,
    DateTimeOffset? SeenAt, DateTimeOffset? SnoozedUntil, DateTimeOffset? DismissedAt,
    DateTimeOffset? ResolvedAt, string? SourceVersion, long Version,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record WorkspaceProactivePolicyState(
    string WorkspaceId, bool Enabled, long Version, DateTimeOffset? LastEvaluationAt,
    DateTimeOffset? NextEvaluationAt, string? LeaseId, DateTimeOffset? LeaseExpiresAt,
    string UpdatedBy, DateTimeOffset UpdatedAt);

public sealed record ProactiveAuditEvidence(
    string AuditId, string WorkspaceId, string? MemberId, string Action,
    string? ItemId, string? SubjectId, string SafeSummaryJson,
    string CorrelationId, DateTimeOffset OccurredAt);

public interface IProactiveStore
{
    Task<WorkspaceProactivePolicyState?> ReadPolicyAsync(string workspaceId, CancellationToken cancellationToken);
    Task<IReadOnlyList<WorkspaceProactivePolicyState>> ReadDuePoliciesAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken);
    Task<bool> TryClaimPolicyAsync(string workspaceId, long version, string leaseId, DateTimeOffset now, DateTimeOffset leaseUntil, CancellationToken cancellationToken);
    Task CompletePolicyEvaluationAsync(string workspaceId, string leaseId, DateTimeOffset evaluatedAt, DateTimeOffset nextEvaluationAt, CancellationToken cancellationToken);
    Task<ProactiveItemState?> ReadActiveCycleAsync(string workspaceId, string subjectId, string triggerType, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProactiveItemState>> ReadOwnerItemsAsync(string workspaceId, string ownerMemberId, string status, int limit, CancellationToken cancellationToken);
    Task<ProactiveItemState?> ReadItemAsync(string workspaceId, string itemId, CancellationToken cancellationToken);
    Task SavePolicyAsync(WorkspaceProactivePolicyState policy, string idempotencyKey, string fingerprint, ProactiveAuditEvidence audit, CancellationToken cancellationToken);
    Task SaveItemAsync(ProactiveItemState item, ProactiveAuditEvidence audit, CancellationToken cancellationToken);
    Task RecordAuditAsync(ProactiveAuditEvidence audit, CancellationToken cancellationToken);
}

public sealed class ProactiveActiveCycleConflictException : Exception;
