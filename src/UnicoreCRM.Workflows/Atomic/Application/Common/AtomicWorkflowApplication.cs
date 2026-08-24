namespace UnicoreCRM.Workflows.Atomic.Application.Common;

internal sealed record AtomicWorkflowMetadata(string RequestId, string CorrelationId, string IdempotencyKey);

internal sealed record AtomicWorkflowError(
    string Code,
    int Status,
    string Title,
    string? Detail = null,
    IReadOnlyDictionary<string, string[]>? FieldErrors = null,
    string? IdempotencyKey = null);

internal sealed record AtomicWorkflowResult<T>(T? Value, AtomicWorkflowError? Error, int SuccessStatus)
{
    internal bool IsSuccess => Error is null;
    internal static AtomicWorkflowResult<T> Success(T value, int successStatus) => new(value, null, successStatus);
    internal static AtomicWorkflowResult<T> Failure(AtomicWorkflowError error) => new(default, error, 0);
}

internal static class AtomicWorkflowErrors
{
    internal static AtomicWorkflowError AuthenticationRequired() =>
        new("AUTHENTICATION_REQUIRED", 401, "Authentication required");

    internal static AtomicWorkflowError AccessDenied() =>
        new("ACCESS_DENIED", 403, "Access denied");

    internal static AtomicWorkflowError Validation(IReadOnlyDictionary<string, string[]> fields) =>
        new("VALIDATION_FAILED", 422, "Validation failed", FieldErrors: fields);

    internal static AtomicWorkflowError IdempotencyReused(string key) =>
        new("IDEMPOTENCY_KEY_REUSED", 409, "Idempotency key reused", IdempotencyKey: key);

    internal static AtomicWorkflowError WorkspaceAlreadyProvisioned() =>
        new(
            "WORKSPACE_ALREADY_PROVISIONED",
            409,
            "Workspace already provisioned",
            "The authenticated account already holds active Workspace access, so initial Workspace provisioning does not apply.");
}
