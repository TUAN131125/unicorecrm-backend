namespace UnicoreCRM.Platform.IdentityAuth.Application.Common;

internal enum IdentityAbuseOperation
{
    RegisterAccount,
    RequestEmailVerification,
    VerifyEmail,
    SignIn,
    RefreshSession
}

internal sealed record IdentityAbuseDecision(bool IsAllowed, TimeSpan RetryAfter)
{
    internal static readonly IdentityAbuseDecision Allowed = new(true, TimeSpan.Zero);
}

internal interface IIdentityAbuseProtector
{
    IdentityAbuseDecision CheckOrigin(IdentityAbuseOperation operation, string origin);
    IdentityAbuseDecision CheckEmailSubject(IdentityAbuseOperation operation, string? email);
    IdentityAbuseDecision CheckRefreshSubject(string refreshToken);
}
