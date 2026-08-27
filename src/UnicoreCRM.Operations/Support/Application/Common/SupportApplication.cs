using UnicoreCRM.Operations.Support.Domain;

namespace UnicoreCRM.Operations.Support.Application.Common;

internal sealed record SupportRequestMetadata(string RequestId, string CorrelationId);

internal sealed record SupportCommandMetadata(
    string RequestId,
    string CorrelationId,
    string IdempotencyKey,
    long? ExpectedVersion);

internal sealed record SupportOperationError(
    string Code,
    int Status,
    string Title,
    string? Detail = null,
    IReadOnlyDictionary<string, string[]>? FieldErrors = null,
    string? AggregateId = null,
    long? ExpectedVersion = null,
    long? CurrentVersion = null,
    string? IdempotencyKey = null);

internal sealed record SupportOperationResult<T>(T? Value, SupportOperationError? Error)
{
    internal bool IsSuccess => Error is null;
    internal static SupportOperationResult<T> Success(T value) => new(value, null);
    internal static SupportOperationResult<T> Failure(SupportOperationError error) => new(default, error);
}

/// <summary>
/// Canonical Support error codes. <c>SUPPORT_CASE_INVALID_TRANSITION</c> comes from the
/// canonical error catalog entry owned by Support; the remainder are the shared canonical
/// codes the Support operation registry rows declare.
/// </summary>
internal static class SupportErrors
{
    internal static SupportOperationError AccessDenied() => new("ACCESS_DENIED", 403, "Access denied");

    /// <summary>
    /// Owner assignment is governed by <c>support.assign</c>. The profile-replacement contract also
    /// carries <c>ownerId</c>, so a caller holding only <c>support.update</c> is refused here rather
    /// than silently acquiring the assignment privilege through the wider command.
    /// </summary>
    internal static SupportOperationError OwnerAssignmentDenied() =>
        new("ACCESS_DENIED", 403, "Access denied", "Changing the Support Case owner requires the support.assign capability.");
    internal static SupportOperationError WorkspaceMismatch() => new("WORKSPACE_MISMATCH", 403, "Workspace context mismatch");
    internal static SupportOperationError NotFound() => new("RESOURCE_NOT_FOUND", 404, "Resource not found");
    internal static SupportOperationError VersionConflict(string caseId, long expected, long current) =>
        new("VERSION_CONFLICT", 412, "Resource version conflict", AggregateId: caseId, ExpectedVersion: expected, CurrentVersion: current);
    internal static SupportOperationError IdempotencyReused(string key) =>
        new("IDEMPOTENCY_KEY_REUSED", 409, "Idempotency key reused", IdempotencyKey: key);
    internal static SupportOperationError Validation(IReadOnlyDictionary<string, string[]> fields) =>
        new("VALIDATION_FAILED", 422, "Validation failed", FieldErrors: fields);
    internal static SupportOperationError InvalidTransition(string caseId) =>
        new("SUPPORT_CASE_INVALID_TRANSITION", 409, "Support Case transition is not allowed", AggregateId: caseId);
}

internal sealed record SupportCaseListSpecification(
    int Offset,
    int Limit,
    string? Search,
    SupportCaseStatus? Status,
    SupportCasePriority? Priority,
    SupportCaseCategory? Category,
    string? OwnerId,
    string? RelationshipType,
    string? RelationshipId,
    string? SlaStatus,
    string SortBy,
    bool Descending,
    /// <summary>
    /// The AccessControl-resolved record-scope owner. When set, only records owned by that member
    /// are in scope, and the predicate is applied before the count, the ordering and the page.
    /// </summary>
    string? ScopeOwnerMemberId = null);

internal sealed record SupportPage<T>(IReadOnlyList<T> Items, bool HasNextPage, int? NextOffset, int TotalCount);

internal interface ISupportPersistence
{
    Task<ISupportTransaction> BeginSerializableAsync(CancellationToken cancellationToken);
    Task<SupportCase?> LoadCaseAsync(string workspaceId, string caseId, CancellationToken cancellationToken);
    Task<SupportCase?> ReadCaseAsync(string workspaceId, string caseId, CancellationToken cancellationToken);
    Task<SupportPage<SupportCase>> ListCasesAsync(string workspaceId, SupportCaseListSpecification specification, CancellationToken cancellationToken);
    Task<int> MaxCaseSequenceAsync(string workspaceId, int caseYear, CancellationToken cancellationToken);
    Task<SupportIdempotencyRecord?> FindIdempotencyAsync(string scopeKey, CancellationToken cancellationToken);
    void AddCase(SupportCase supportCase);
    void AddComment(SupportCaseComment comment);
    void AddIdempotency(SupportIdempotencyRecord record);
    void AddAudit(SupportAuditRecord audit);
    void AddOutbox(SupportOutboxMessage message);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

internal interface ISupportTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
}

internal sealed class SupportPersistenceConcurrencyException(Exception innerException)
    : Exception("The Support Case resource changed concurrently.", innerException);
