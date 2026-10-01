using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Operations.Tasks.Contracts;

/// <summary>Trusted internal participant; Tasks owns discovery, authorization and its atomic commit.</summary>
public interface ILeadHandoverTaskParticipant
{
    Task<LeadHandoverTaskResult> ValidateAsync(LeadHandoverTaskCommand command, CancellationToken cancellationToken);
    Task<LeadHandoverTaskResult> ExecuteAsync(LeadHandoverTaskCommand command, CancellationToken cancellationToken);
    /// <summary>Service-only. Success returns exact committed proof. HANDOVER_TASKS_FENCED (409)
    /// proves durable cancellation and is the only failure allowing Lead reservation release.</summary>
    Task<LeadHandoverTaskResult> ResolveOrFenceAsync(LeadHandoverTaskCommand command, CancellationToken cancellationToken);
    /// <summary>Human-only disclosure guard over existing proof and current Tasks capabilities and proof field visibility; never creates Tasks.</summary>
    Task<LeadHandoverTaskResult> AuthorizeReplayAsync(LeadHandoverTaskCommand command, CancellationToken cancellationToken);
}

/// <param name="ExecutorServicePrincipalId">Null for a current human request; recovery must use svc_lead_handover_recovery.</param>
/// <param name="OriginalActorId">Frozen initiating member; never used as current authorization.</param>
public sealed record LeadHandoverTaskCommand(
    TrustedWorkspaceContext TrustedWorkspace,
    string LeadId,
    string HandoverId,
    string NextOwnerId,
    string Reason,
    DateTimeOffset FrozenDueAt,
    string RequestId,
    string CorrelationId,
    string OriginalActorId,
    string? ExecutorServicePrincipalId = null);

public sealed record LeadHandoverTaskResult(
    bool IsSuccess,
    IReadOnlyList<string> ReassignedTaskIds,
    string? HandoverTaskId,
    long? HandoverTaskVersion,
    DateTimeOffset? HandoverTaskDueAt,
    string? Outcome,
    string? ErrorCode = null,
    int? ErrorStatus = null,
    IReadOnlyDictionary<string, string[]>? FieldErrors = null)
{
    public IReadOnlyList<string> EmittedEventIds { get; init; } = [];
    public IReadOnlyList<string> AuditEvidenceIds { get; init; } = [];
}
