using UnicoreCRM.Crm.Deals.Domain;

namespace UnicoreCRM.Crm.Deals.Application.Common;

internal sealed record DealRequestMetadata(string RequestId, string CorrelationId);

internal sealed record DealCommandMetadata(
    string RequestId,
    string CorrelationId,
    string IdempotencyKey,
    long? ExpectedVersion);

internal sealed record DealOperationError(
    string Code,
    int Status,
    string Title,
    string? Detail = null,
    IReadOnlyDictionary<string, string[]>? FieldErrors = null,
    IReadOnlyList<string>? BusinessBlockers = null,
    string? AggregateId = null,
    long? ExpectedVersion = null,
    long? CurrentVersion = null,
    string? IdempotencyKey = null);

internal sealed record DealOperationResult<T>(T? Value, DealOperationError? Error)
{
    internal bool IsSuccess => Error is null;
    internal static DealOperationResult<T> Success(T value) => new(value, null);
    internal static DealOperationResult<T> Failure(DealOperationError error) => new(default, error);
}

internal interface IDealsPersistence
{
    Task<IDealsTransaction> BeginSerializableAsync(CancellationToken cancellationToken);
    Task<Deal?> LoadDealAsync(string workspaceId, string dealId, CancellationToken cancellationToken);
    Task<Deal?> ReadDealAsync(string workspaceId, string dealId, CancellationToken cancellationToken);
    /// <param name="scopeOwnerMemberId">
    /// The AccessControl-resolved record-scope owner. When set, only deals owned by that member are
    /// in scope, and the predicate is part of the SQL query rather than an in-memory filter, so a
    /// hidden row never reaches the count or the page.
    /// </param>
    Task<IReadOnlyList<Deal>> ReadDealsAsync(string workspaceId, string? scopeOwnerMemberId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Deal>> LoadDealsAsync(string workspaceId, IReadOnlyCollection<string> dealIds, CancellationToken cancellationToken);
    Task<DealIdempotencyRecord?> FindIdempotencyAsync(string scopeKey, CancellationToken cancellationToken);
    void AddDeal(Deal deal);
    void AddIdempotency(DealIdempotencyRecord record);
    void AddAudit(DealAuditRecord audit);
    void AddOutbox(DealOutboxMessage message);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

internal interface IDealsTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
}

internal sealed class DealsPersistenceConcurrencyException(Exception innerException)
    : Exception("The Deal resource changed concurrently.", innerException);

internal static class DealErrors
{
    internal static DealOperationError AccessDenied() => new("ACCESS_DENIED", 403, "Access denied");
    internal static DealOperationError WorkspaceMismatch() => new("WORKSPACE_MISMATCH", 403, "Workspace context mismatch");
    internal static DealOperationError NotFound() => new("RESOURCE_NOT_FOUND", 404, "Resource not found");
    internal static DealOperationError VersionConflict(string dealId, long expected, long current) =>
        new("VERSION_CONFLICT", 412, "Resource version conflict", AggregateId: dealId, ExpectedVersion: expected, CurrentVersion: current);
    internal static DealOperationError BatchVersionConflict(string dealId, long expected, long current) =>
        new("DEAL_BATCH_VERSION_CONFLICT", 412, "Deal batch version conflict", AggregateId: dealId, ExpectedVersion: expected, CurrentVersion: current);
    internal static DealOperationError IdempotencyReused(string key) =>
        new("IDEMPOTENCY_KEY_REUSED", 409, "Idempotency key reused", IdempotencyKey: key);
    internal static DealOperationError Validation(IReadOnlyDictionary<string, string[]> fields) =>
        new("VALIDATION_FAILED", 422, "Validation failed", FieldErrors: fields);
    internal static DealOperationError FieldValidation(IReadOnlyDictionary<string, string[]> fields) =>
        new("FIELD_VALIDATION_FAILED", 422, "Field validation failed", FieldErrors: fields);
    internal static DealOperationError ProgressiveProfile(IReadOnlyDictionary<string, string[]> fields) =>
        new("DEAL_PROGRESSIVE_PROFILE_INCOMPLETE", 422, "Deal progressive profile is incomplete", FieldErrors: fields);
    internal static DealOperationError StageNotFound(string stageCode) =>
        new("DEAL_STAGE_NOT_FOUND", 422, "Deal stage was not found", FieldErrors: new Dictionary<string, string[]> { ["stageCode"] = [$"Stage '{stageCode}' is not configured."] });
    internal static DealOperationError TerminalStageRequiresOutcome() =>
        new("DEAL_TERMINAL_TRANSITION_REQUIRES_OUTCOME", 409, "Terminal Deal stages require a typed outcome command");
    internal static DealOperationError InvalidStageTransition(string dealId) =>
        new("DEAL_INVALID_STAGE_TRANSITION", 409, "Deal stage transition is not allowed", AggregateId: dealId);
    internal static DealOperationError LifecycleConflict(string dealId) =>
        new("LIFECYCLE_CONFLICT", 409, "Deal lifecycle does not allow this operation", AggregateId: dealId);
    internal static DealOperationError OwnerNotAssignable() =>
        new("DEAL_OWNER_NOT_ASSIGNABLE", 422, "Deal owner is not assignable", FieldErrors: new Dictionary<string, string[]> { ["ownerId"] = ["ownerId must reference an active member of the trusted workspace."] });
    internal static DealOperationError WinEvidenceInvalid(IReadOnlyDictionary<string, string[]> fields) =>
        new("DEAL_WIN_EVIDENCE_INVALID", 422, "Deal win evidence is invalid", FieldErrors: fields);
    internal static DealOperationError WonTransitionBlocked(string dealId) =>
        new("DEAL_WON_TRANSITION_BLOCKED", 409, "Deal cannot be marked won from its current lifecycle state", AggregateId: dealId);
    internal static DealOperationError LossReason(IReadOnlyDictionary<string, string[]> fields) =>
        new("DEAL_LOSS_REASON_REQUIRED", 422, "Deal loss reason is required", FieldErrors: fields);
    internal static DealOperationError RecycleDate(IReadOnlyDictionary<string, string[]> fields) =>
        new("DEAL_RECYCLE_DATE_REQUIRED", 422, "Deal recycle date is required", FieldErrors: fields);
    internal static DealOperationError BatchEmpty() =>
        new("DEAL_BATCH_EMPTY", 422, "Deal batch cannot be empty", FieldErrors: new Dictionary<string, string[]> { ["items"] = ["items must contain at least one Deal."] });
}
