namespace UnicoreCRM.AI.Gateway;

internal sealed record AiOperationError(
    string Code,
    int Status,
    string Title,
    bool Retryable = false,
    IReadOnlyDictionary<string, string[]>? FieldErrors = null);

internal sealed record AiOperationResult<T>(T? Value, AiOperationError? Error)
{
    internal bool IsSuccess => Error is null;
    internal static AiOperationResult<T> Success(T value) => new(value, null);
    internal static AiOperationResult<T> Failure(AiOperationError error) => new(default, error);
}

internal static class AiErrors
{
    internal static AiOperationError Malformed() =>
        new("AI_REQUEST_INVALID", 400, "AI advisory request JSON is invalid");

    internal static AiOperationError TooLarge() =>
        new("AI_REQUEST_TOO_LARGE", 413, "AI advisory request is too large");

    internal static AiOperationError UnsupportedMediaType() =>
        new("AI_UNSUPPORTED_MEDIA_TYPE", 415, "AI advisory request must use JSON");

    internal static AiOperationError Invalid(IReadOnlyDictionary<string, string[]> fields) =>
        new("AI_REQUEST_INVALID", 422, "AI advisory request is invalid", FieldErrors: fields);

    internal static AiOperationError AccessDenied() =>
        new("AI_CONTEXT_ACCESS_DENIED", 403, "AI context access denied");

    internal static AiOperationError WorkspaceMismatch() =>
        new("WORKSPACE_MISMATCH", 403, "Workspace context mismatch");

    internal static AiOperationError ContextNotFound() =>
        new("AI_CONTEXT_NOT_FOUND", 404, "AI context was not found or is not visible");

    internal static AiOperationError ProviderUnavailable() =>
        new("AI_PROVIDER_UNAVAILABLE", 503, "AI provider is unavailable", true);

    internal static AiOperationError ProviderTimeout() =>
        new("AI_PROVIDER_TIMEOUT", 504, "AI provider timed out", true);

    internal static AiOperationError InvalidProviderResponse() =>
        new("AI_PROVIDER_RESPONSE_INVALID", 502, "AI provider returned an invalid response", true);
}
