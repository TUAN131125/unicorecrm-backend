using UnicoreCRM.Platform.IdentityAuth.Contracts;
using UnicoreCRM.Platform.IdentityAuth.Domain;

namespace UnicoreCRM.Platform.IdentityAuth.Application.Common;

internal sealed record RequestMetadata(string RequestId, string CorrelationId, string IdempotencyKey, string? UserAgent);

internal sealed record OperationError(string Code, int Status, string Title, bool Retryable = false, string? Detail = null, IReadOnlyDictionary<string, string[]>? FieldErrors = null);

internal sealed record OperationResult<T>(T? Value, OperationError? Error)
{
    internal bool IsSuccess => Error is null;
    internal static OperationResult<T> Success(T value) => new(value, null);
    internal static OperationResult<T> Failure(OperationError error) => new(default, error);
}

internal interface IIdentityAuthPersistence
{
    Task<IIdentityTransaction> BeginSerializableAsync(CancellationToken cancellationToken);
    Task<IdentityAccount?> FindAccountByEmailAsync(string normalizedEmail, CancellationToken cancellationToken);
    Task<IdentityAccount?> FindAccountByIdAsync(string accountId, CancellationToken cancellationToken);
    Task<IdentityCredential?> FindCredentialAsync(string accountId, CancellationToken cancellationToken);
    Task<IdentitySession?> FindSessionAsync(string sessionId, CancellationToken cancellationToken);
    Task<IdentityIdempotencyRecord?> FindIdempotencyAsync(string operation, string key, CancellationToken cancellationToken);
    void AddAccount(IdentityAccount account);
    void AddCredential(IdentityCredential credential);
    void AddSession(IdentitySession session);
    void AddIdempotency(IdentityIdempotencyRecord record);
    void AddAudit(IdentityAuditRecord record);
    void AddSecurityEvent(IdentitySecurityEvent securityEvent);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

internal interface IIdentityTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
}

internal interface IIdentityPasswordHasher
{
    string Hash(IdentityAccount account, string password);
    bool Verify(IdentityAccount account, string hash, string password);
    void ConsumeUnknownPassword(string password);
}

internal interface IIdentityTokenIssuer
{
    IssuedAccessToken Issue(IdentityAccount account, IdentitySession session);
}

internal sealed record IssuedAccessToken(string Value, DateTimeOffset ExpiresAt);

internal interface IRefreshTokenProtector
{
    string Create(IdentitySession session);
    string Hash(string rawToken);
    bool Matches(string rawToken, string expectedHash);
    bool HasExpectedShape(string rawToken, out string sessionId);
}

internal interface IIdentityRequestFingerprinter
{
    string Create(params string?[] values);
}

internal interface IIdentitySessionPolicy
{
    TimeSpan IdleLifetime { get; }
    TimeSpan AbsoluteLifetime { get; }
}

internal static class IdentityProjection
{
    internal static UserAccountDocument Account(IdentityAccount account) => new(
        account.AccountId,
        account.Email,
        account.DisplayName,
        account.Status switch
        {
            AccountStatus.Active => AccountStatusDocument.Active,
            AccountStatus.Suspended => AccountStatusDocument.Suspended,
            _ => AccountStatusDocument.PendingVerification
        },
        account.CreatedAt,
        account.EmailVerifiedAt);

    internal static AuthSessionDocument Session(IdentityAccount account, IdentitySession session) => new(
        session.SessionId,
        new AuthenticatedPrincipalDocument(account.AccountId, account.MemberId, account.Email, account.DisplayName),
        session.Status == SessionStatus.Active ? SessionStatusDocument.Active : SessionStatusDocument.Revoked,
        session.IssuedAt,
        session.LastSeenAt,
        session.IdleExpiresAt,
        session.AbsoluteExpiresAt,
        session.RefreshCounter,
        "AAL1",
        new DeviceDocument(session.DeviceId, session.DeviceLabel, session.LastSeenAt, session.UserAgent),
        null,
        session.RevokedAt,
        session.RevokeReason);
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
