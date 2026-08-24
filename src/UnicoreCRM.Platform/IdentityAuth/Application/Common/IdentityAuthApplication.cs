
namespace UnicoreCRM.Platform.IdentityAuth.Application.Common;

internal sealed record RequestMetadata(string RequestId, string CorrelationId, string IdempotencyKey, string? UserAgent);

internal sealed record OperationError(string Code, int Status, string Title, bool Retryable = false, string? Detail = null, IReadOnlyDictionary<string, string[]>? FieldErrors = null);

internal sealed record OperationResult<T>(T? Value, OperationError? Error)
{
    internal bool IsSuccess => Error is null;
    internal static OperationResult<T> Success(T value) => new(value, null);
    internal static OperationResult<T> Failure(OperationError error) => new(default, error);
}

internal static class IdentityErrors
{
    internal static OperationError Validation(IReadOnlyDictionary<string, string[]> fields) => new("VALIDATION_FAILED", 422, "Validation failed", false, null, fields);
    internal static OperationError InvalidCredentials() => new("INVALID_CREDENTIALS", 401, "Authentication failed");
    internal static OperationError EmailNotVerified() => new("EMAIL_NOT_VERIFIED", 403, "Email verification required");
    internal static OperationError AccountSuspended() => new("ACCOUNT_SUSPENDED", 403, "Account suspended");
    internal static OperationError SessionInvalid() => new("TOKEN_INVALID", 401, "Session token is invalid");
    internal static OperationError SessionExpired() => new("SESSION_EXPIRED", 401, "Session expired");
    internal static OperationError SessionRevoked() => new("SESSION_REVOKED", 401, "Session revoked");
    internal static OperationError DuplicateEmail() => new("DUPLICATE_BUSINESS_KEY", 409, "Account already exists");
    internal static OperationError IdempotencyReused() => new("IDEMPOTENCY_KEY_REUSED", 409, "Idempotency key was reused with a different request");
}
