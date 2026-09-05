using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Leads.Contracts;

/// <summary>
/// The Leads-owned Lead Qualification participant. It is the only way the Workflows coordinator may
/// touch Lead state: Workflows opens no <c>LeadsDbContext</c> and assigns no Lead identity.
///
/// Current access is checked before workflow replay or conflict disclosure. Preparation separately
/// checks that the Lead is qualifiable before any foreign owner commits.
/// </summary>
public interface ILeadQualificationParticipant
{
    /// <summary>
    /// Establishes current capability and record access without mutable qualification preconditions.
    /// A completed workflow must remain replayable for an authorized caller after the Lead closes.
    /// </summary>
    Task<LeadQualificationAuthorization> AuthorizeAsync(
        LeadQualificationAccessQuery query,
        CancellationToken cancellationToken);

    /// <summary>
    /// Validates every frozen qualification precondition and returns the facts the coordinator needs
    /// to drive the remaining participants. It mutates nothing and writes no evidence.
    /// </summary>
    Task<LeadQualificationPreparation> PrepareAsync(
        LeadQualificationPrepareCommand command,
        CancellationToken cancellationToken);

    /// <summary>
    /// The terminal close. It runs through the ordinary Leads command execution, so it carries the
    /// owner's own idempotency, record-access guard, If-Match check, immutable command audit and
    /// atomic outbox staging exactly as every other declared Lead mutation does.
    /// </summary>
    Task<LeadQualificationClosure> CloseForNurtureAsync(
        LeadQualificationCloseCommand command,
        CancellationToken cancellationToken);
}

/// <summary>
/// The Leads-owned OPPORTUNITY participant. It is separate from the NURTURE surface so the
/// coordinator cannot accidentally close a Lead under the wrong admitted operation.
/// </summary>
public interface ILeadOpportunityQualificationParticipant
{
    Task<LeadQualificationAuthorization> AuthorizeOpportunityAsync(
        LeadQualificationAccessQuery query,
        CancellationToken cancellationToken);

    Task<LeadQualificationPreparation> PrepareOpportunityAsync(
        LeadQualificationPrepareCommand command,
        CancellationToken cancellationToken);

    Task<LeadQualificationClosure> CloseForOpportunityAsync(
        LeadOpportunityQualificationCloseCommand command,
        CancellationToken cancellationToken);
}

public sealed record LeadQualificationAccessQuery(string LeadId, string RequestId, string CorrelationId);

public sealed record LeadQualificationAuthorization(
    bool IsSuccess,
    TrustedWorkspaceContext? TrustedWorkspace,
    string? ErrorCode,
    int? ErrorStatus);

public sealed record LeadQualificationPrepareCommand(
    string LeadId,
    string RequestId,
    string CorrelationId,
    long ExpectedVersion);

/// <summary>
/// On failure the coordinator receives the owner's own canonical error, which it maps to the
/// admitted qualification vocabulary. No Lead value is disclosed on a failure.
/// </summary>
public sealed record LeadQualificationPreparation(
    bool IsSuccess,
    TrustedWorkspaceContext? TrustedWorkspace,
    string? OwnerId,
    long? Version,
    string? ErrorCode,
    int? ErrorStatus,
    IReadOnlyDictionary<string, string[]>? FieldErrors,
    /// <summary>
    /// The Lead's communication restrictions, supplied by their owner because only Leads may read
    /// Lead state. They exist so a restriction survives conversion; the coordinator forwards them
    /// unchanged and never interprets them.
    /// </summary>
    bool? DoNotCall = null,
    bool? DoNotEmail = null,
    string? SuggestedOpportunityCloseDate = null,
    string? EstimatedValueAmount = null,
    string? EstimatedValueCurrency = null);

public sealed record LeadQualificationCloseCommand(
    string LeadId,
    string ContactId,
    string RequestId,
    string CorrelationId,
    string IdempotencyKey,
    long ExpectedVersion,
    /// <summary>Original workflow member for idempotency identity only; authorization and audit use the current caller.</summary>
    string? IdempotencyScopeActorId = null);

public sealed record LeadOpportunityQualificationCloseCommand(
    string LeadId,
    string ContactId,
    string DealId,
    string RequestId,
    string CorrelationId,
    string IdempotencyKey,
    long ExpectedVersion,
    string? IdempotencyScopeActorId = null);

/// <summary>
/// The terminal closure. The command identity, instant and evidence identifiers come from the Leads
/// command record itself, so the workflow response reports what this owner actually committed rather
/// than anything the coordinator composed.
/// </summary>
public sealed record LeadQualificationClosure(
    bool IsSuccess,
    string? Outcome,
    long? Version,
    string? ErrorCode,
    int? ErrorStatus,
    long? ExpectedVersion,
    long? CurrentVersion,
    IReadOnlyDictionary<string, string[]>? FieldErrors,
    string? CommandId = null,
    string? OccurredAt = null,
    IReadOnlyList<string>? EmittedEventIds = null,
    IReadOnlyList<string>? AuditEvidenceIds = null,
    string? CorrelationId = null);
