using UnicoreCRM.Platform.IdentityAuth.Domain;

namespace UnicoreCRM.Platform.IdentityAuth.Application.Common;

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
