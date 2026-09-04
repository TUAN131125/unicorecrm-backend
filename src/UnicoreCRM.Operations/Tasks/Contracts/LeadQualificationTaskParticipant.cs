namespace UnicoreCRM.Operations.Tasks.Contracts;

/// <summary>
/// The Tasks-owned Lead Qualification participant. It is deliberately not a generic Task creation
/// gateway: it accepts only the facts the frozen NURTURE contract carries, composes the follow-up
/// Task itself, and assigns Task identity inside Tasks (LAW-08). Workflows opens no
/// <c>TasksDbContext</c> and cannot choose a Task identifier, status or priority.
///
/// It runs the ordinary <c>createTask</c> execution, so <c>tasks.create</c> is enforced at the Tasks
/// application boundary, and Tasks' own idempotency, audit and outbox apply unchanged. That is the
/// frozen split: <c>tasks.create</c> is required of the caller because it is grantable and seeded,
/// unlike the BLOCKED and ungrantable <c>contacts.create</c>.
/// </summary>
public interface ILeadQualificationTaskParticipant
{
    Task<LeadNurtureTaskAssigneeValidationResult> ValidateNurtureAssigneeAsync(
        string assigneeId,
        CancellationToken cancellationToken);

    Task<LeadNurtureTaskResult> CreateNurtureFollowUpAsync(
        LeadNurtureTaskCommand command,
        CancellationToken cancellationToken);
}

public sealed record LeadNurtureTaskAssigneeValidationResult(
    bool IsSuccess,
    string? ErrorCode,
    int? ErrorStatus,
    IReadOnlyDictionary<string, string[]>? FieldErrors);

/// <param name="RevisitAt">The frozen NURTURE revisit instant; it becomes the Task due date.</param>
/// <param name="Reason">
/// The frozen NURTURE reason. It is preserved in full as the Lead source reference's evidence; the
/// Task title is a bounded derived summary of it, because the two contracts' bounds differ.
/// </param>
/// <param name="AssigneeId">The Lead owner, validated by Tasks as an active Workspace member.</param>
/// <param name="ContactId">The resolved Contact, carried as a scalar relationship reference.</param>
/// <param name="LeadId">The source Lead, carried as scalar provenance evidence.</param>
/// <param name="IdempotencyKey">
/// Derived by the coordinator from the workflow anchor identity, so a replay reaches Tasks' own
/// idempotency record and converges on one Task even if the anchor update was lost.
/// </param>
public sealed record LeadNurtureTaskCommand(
    string LeadId,
    string ContactId,
    string AssigneeId,
    string RevisitAt,
    string Reason,
    string? Note,
    string RequestId,
    string CorrelationId,
    string IdempotencyKey,
    /// <summary>Original workflow member for idempotency identity only; authorization and audit use the current caller.</summary>
    string? IdempotencyScopeActorId = null);

public sealed record LeadNurtureTaskResult(
    bool IsSuccess,
    string? TaskId,
    string? Outcome,
    string? ErrorCode,
    int? ErrorStatus,
    IReadOnlyDictionary<string, string[]>? FieldErrors,
    /// <summary>The Task's own resource version, required by the qualification wire result.</summary>
    long? TaskVersion = null);
