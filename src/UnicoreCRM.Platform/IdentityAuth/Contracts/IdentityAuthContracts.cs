using System.Text.Json.Serialization;

namespace UnicoreCRM.Platform.IdentityAuth.Contracts;

public sealed record RegisterAccountRequest(string Email, string Password, string DisplayName);
public sealed record SignInRequest(string Email, string Password, string? DeviceLabel);
public sealed record RefreshSessionRequest;
public sealed record SignOutRequest(string? Reason);

public sealed record UserAccountDocument(
    string AccountId,
    string Email,
    string DisplayName,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] AccountStatusDocument Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? EmailVerifiedAt);

public enum AccountStatusDocument
{
    [JsonStringEnumMemberName("PENDING_VERIFICATION")]
    PendingVerification,
    [JsonStringEnumMemberName("ACTIVE")]
    Active,
    [JsonStringEnumMemberName("SUSPENDED")]
    Suspended
}

public sealed record AuthenticatedSessionResponse(
    AuthSessionDocument Session,
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt);

public sealed record AuthSessionDocument(
    string SessionId,
    AuthenticatedPrincipalDocument Principal,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] SessionStatusDocument Status,
    DateTimeOffset IssuedAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset IdleExpiresAt,
    DateTimeOffset AbsoluteExpiresAt,
    int RefreshCounter,
    string AssuranceLevel,
    DeviceDocument Device,
    DateTimeOffset? MfaVerifiedAt,
    DateTimeOffset? RevokedAt,
    string? RevokeReason);

public enum SessionStatusDocument
{
    [JsonStringEnumMemberName("ACTIVE")]
    Active,
    [JsonStringEnumMemberName("EXPIRED")]
    Expired,
    [JsonStringEnumMemberName("REVOKED")]
    Revoked
}

public sealed record AuthenticatedPrincipalDocument(string AccountId, string MemberId, string Email, string DisplayName);
public sealed record DeviceDocument(string DeviceId, string Label, DateTimeOffset LastSeenAt, string? UserAgent);
public sealed record SessionRevocationResponse(string SessionId, DateTimeOffset RevokedAt);

public sealed record IdentityProblemDetails(
    string Type,
    string Title,
    int Status,
    string Code,
    bool Retryable,
    string CorrelationId,
    string? Detail = null,
    string? Instance = null,
    IReadOnlyDictionary<string, string[]>? FieldErrors = null);
