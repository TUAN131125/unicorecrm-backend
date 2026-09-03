namespace UnicoreCRM.Workflows.Atomic.Contracts;

/// <summary>
/// The internal NURTURE Lead Qualification workflow. It has no HTTP route: public exposure of
/// <c>qualifyLeadForNurture</c> remains blocked by the G-1 consent-transfer gate, and this contract
/// deliberately admits no transport.
/// </summary>
public interface ILeadNurtureQualificationWorkflow
{
    Task<LeadNurtureQualificationResult> ExecuteAsync(
        LeadNurtureQualificationCommand command,
        CancellationToken cancellationToken);
}

/// <summary>The caller-declared relationship intent, passed through to the Contacts participant.</summary>
public enum LeadNurtureRelationshipMode
{
    Existing,
    New
}

/// <param name="ContactSupplied">
/// Whether the request carried a <c>relationship.contact</c> object at all. The pinned schema
/// requires it for both modes, and an absent object is not the same as an object whose
/// <c>displayName</c> is missing, so the distinction is carried rather than flattened into a null
/// name - otherwise the coordinator could not validate the contract it is enforcing.
/// </param>
/// <param name="OrganizationSupplied">
/// Whether the request carried <c>relationship.organization</c>. It is declared by the schema only
/// for the unadmitted ORGANIZATION_ACCOUNT kind, so carrying it is a rejection rather than a value.
/// </param>
public sealed record LeadNurtureContactIntent(
    LeadNurtureRelationshipMode Mode,
    string? SelectedContactId,
    string? DisplayName,
    string? Email,
    string? Phone,
    string? Title,
    bool ContactSupplied = true,
    bool OrganizationSupplied = false);

/// <param name="ExpectedVersion">The <c>If-Match</c> Lead version. Exact match is required.</param>
/// <param name="RevisitAt">Frozen NURTURE input; becomes the follow-up Task due date.</param>
/// <param name="Reason">Frozen NURTURE input; becomes the follow-up Task title.</param>
public sealed record LeadNurtureQualificationCommand(
    string LeadId,
    LeadNurtureContactIntent Contact,
    string RevisitAt,
    string Reason,
    string? Note,
    string RequestId,
    string CorrelationId,
    string IdempotencyKey,
    long ExpectedVersion,
    /// <summary>
    /// The follow-up Task's owner, from the admitted request. It assigns the Task only; the Contact's
    /// record owner is always the Lead owner. Null means the Lead owner assigns the Task too.
    /// </summary>
    string? TaskOwnerId = null);

public sealed record LeadNurtureQualificationResult(
    bool IsSuccess,
    /// <summary>COMMITTED on the execution that completed the workflow; REPLAYED afterwards.</summary>
    string? Outcome,
    string? LeadId,
    long? LeadVersion,
    string? ContactId,
    string? TaskId,
    string? QualificationOutcome,
    string? ErrorCode,
    int? ErrorStatus,
    IReadOnlyDictionary<string, string[]>? FieldErrors,
    /// <summary>The authoritative wire response, composed once and replayed verbatim.</summary>
    LeadQualificationWorkflowResponse? Response = null,
    long? ExpectedVersion = null,
    long? CurrentVersion = null,
    string? IdempotencyKey = null);
