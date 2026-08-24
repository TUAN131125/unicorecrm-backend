namespace UnicoreCRM.Workflows.Durable.Application.Common;

internal sealed record DurableWorkflowMetadata(string RequestId, string CorrelationId, string IdempotencyKey);

internal sealed record DurableWorkflowError(
    string Code,
    int Status,
    string Title,
    string? Detail = null,
    IReadOnlyDictionary<string, string[]>? FieldErrors = null,
    string? IdempotencyKey = null);

internal sealed record DurableWorkflowResult<T>(T? Value, DurableWorkflowError? Error, int SuccessStatus)
{
    internal bool IsSuccess => Error is null;
    internal static DurableWorkflowResult<T> Success(T value, int successStatus) => new(value, null, successStatus);
    internal static DurableWorkflowResult<T> Failure(DurableWorkflowError error) => new(default, error, 0);
}

internal static class DurableWorkflowErrors
{
    internal static DurableWorkflowError AuthenticationRequired() =>
        new("AUTHENTICATION_REQUIRED", 401, "Authentication required");

    internal static DurableWorkflowError AccessDenied() =>
        new("ACCESS_DENIED", 403, "Access denied");

    internal static DurableWorkflowError Validation(IReadOnlyDictionary<string, string[]> fields) =>
        new("VALIDATION_FAILED", 422, "Validation failed", FieldErrors: fields);

    internal static DurableWorkflowError IdempotencyReused(string key) =>
        new("IDEMPOTENCY_KEY_REUSED", 409, "Idempotency key reused", IdempotencyKey: key);

    internal static DurableWorkflowError WorkspaceAlreadyProvisioned() =>
        new(
            "WORKSPACE_ALREADY_PROVISIONED",
            409,
            "Workspace already provisioned",
            "The authenticated account already holds active Workspace access, so initial Workspace provisioning does not apply.");
}
