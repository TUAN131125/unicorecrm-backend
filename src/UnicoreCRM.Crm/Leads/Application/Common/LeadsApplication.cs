using UnicoreCRM.Crm.Leads.Domain;

namespace UnicoreCRM.Crm.Leads.Application.Common;

internal sealed record LeadCommandMetadata(
    string RequestId,
    string CorrelationId,
    string IdempotencyKey,
    long? ExpectedVersion,
    string ActorType = "Member",
    string? ActorId = null,
    string? DelegatedSubjectId = null,
    string? SourceReference = null);

internal sealed record LeadOperationError(
    string Code,
    int Status,
    string Title,
    string? Detail = null,
    IReadOnlyDictionary<string, string[]>? FieldErrors = null,
    string? AggregateId = null,
    long? ExpectedVersion = null,
    long? CurrentVersion = null,
    string? IdempotencyKey = null);

internal sealed record LeadOperationResult<T>(T? Value, LeadOperationError? Error)
{
    internal bool IsSuccess => Error is null;
    internal static LeadOperationResult<T> Success(T value) => new(value, null);
    internal static LeadOperationResult<T> Failure(LeadOperationError error) => new(default, error);
}

internal interface ILeadsPersistence
{
    Task<ILeadsTransaction> BeginSerializableAsync(CancellationToken cancellationToken);
    Task<Lead?> LoadLeadAsync(string workspaceId, string leadId, CancellationToken cancellationToken);
    Task<Lead?> ReadLeadAsync(string workspaceId, string leadId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Lead>> ListLeadsAsync(string workspaceId, CancellationToken cancellationToken);
    Task<LeadIdempotencyRecord?> FindIdempotencyAsync(string scopeKey, CancellationToken cancellationToken);
    void AddLead(Lead lead);
    void AddIdempotency(LeadIdempotencyRecord record);
    void AddAudit(LeadAuditRecord audit);
    void AddOutbox(LeadOutboxMessage message);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

internal interface ILeadsTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
}

internal sealed class LeadsPersistenceConcurrencyException(Exception innerException)
    : Exception("The Lead resource changed concurrently.", innerException);

internal static class LeadErrors
{
    internal static LeadOperationError AccessDenied() => new("ACCESS_DENIED", 403, "Access denied");
    internal static LeadOperationError WorkspaceMismatch() => new("WORKSPACE_MISMATCH", 403, "Workspace context mismatch");
    internal static LeadOperationError NotFound() => new("RESOURCE_NOT_FOUND", 404, "Resource not found");
    internal static LeadOperationError VersionConflict(string leadId, long expected, long current) =>
        new("VERSION_CONFLICT", 412, "Resource version conflict", AggregateId: leadId, ExpectedVersion: expected, CurrentVersion: current);
    internal static LeadOperationError IdempotencyReused(string key) =>
        new("IDEMPOTENCY_KEY_REUSED", 409, "Idempotency key reused", IdempotencyKey: key);
    internal static LeadOperationError InvalidTransition(string leadId) =>
        new("LEAD_INVALID_TRANSITION", 409, "Lead transition is not allowed", AggregateId: leadId);
    internal static LeadOperationError ProgressiveProfile(IReadOnlyDictionary<string, string[]> fields) =>
        new("LEAD_PROGRESSIVE_PROFILE_INCOMPLETE", 422, "Lead progressive profile is incomplete", FieldErrors: fields);
    internal static LeadOperationError DisqualificationEvidence(IReadOnlyDictionary<string, string[]> fields) =>
        new("LEAD_DISQUALIFICATION_EVIDENCE_REQUIRED", 422, "Lead disqualification evidence is required", FieldErrors: fields);
    internal static LeadOperationError ReopenNotAllowed(string leadId) =>
        new("LEAD_REOPEN_NOT_ALLOWED", 409, "Lead reopen is not allowed", AggregateId: leadId);
    internal static LeadOperationError Validation(IReadOnlyDictionary<string, string[]> fields) =>
        new("VALIDATION_FAILED", 422, "Validation failed", FieldErrors: fields);
}
