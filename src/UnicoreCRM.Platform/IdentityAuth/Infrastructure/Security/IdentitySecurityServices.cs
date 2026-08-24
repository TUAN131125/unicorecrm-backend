using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using UnicoreCRM.Platform.IdentityAuth.Application.Common;
using UnicoreCRM.Platform.IdentityAuth.Domain;

namespace UnicoreCRM.Platform.IdentityAuth.Infrastructure.Security;

internal sealed class FrameworkPasswordHasher : IIdentityPasswordHasher
{
    private readonly PasswordHasher<IdentityAccount> hasher = new();
    private readonly IdentityAccount dummyAccount = new("unknown@invalid.local", "Unknown", DateTimeOffset.UnixEpoch, true);
    private readonly string dummyHash;

    public FrameworkPasswordHasher()
    {
        dummyHash = hasher.HashPassword(dummyAccount, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
    }

    public string Hash(IdentityAccount account, string password) => hasher.HashPassword(account, password);

    public bool Verify(IdentityAccount account, string hash, string password) =>
        hasher.VerifyHashedPassword(account, hash, password) is not PasswordVerificationResult.Failed;

    public void ConsumeUnknownPassword(string password) => hasher.VerifyHashedPassword(dummyAccount, dummyHash, password);
}

internal sealed class JwtIdentityTokenIssuer(IOptions<IdentityAuthOptions> options) : IIdentityTokenIssuer
{
    private readonly IdentityAuthOptions options = options.Value;

    public IssuedAccessToken Issue(IdentityAccount account, IdentitySession session)
    {
        var expiresAt = session.LastSeenAt.AddMinutes(options.Jwt.AccessTokenMinutes);
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Jwt.SigningKey)),
            SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, account.AccountId),
            new Claim("sid", session.SessionId),
            new Claim("member_id", account.MemberId),
            new Claim(JwtRegisteredClaimNames.Email, account.Email),
            new Claim(JwtRegisteredClaimNames.Name, account.DisplayName),
            new Claim("aal", "PASSWORD")
        };
        var token = new JwtSecurityToken(
            options.Jwt.Issuer,
            options.Jwt.Audience,
            claims,
            session.LastSeenAt.UtcDateTime,
            expiresAt.UtcDateTime,
            credentials);
        return new IssuedAccessToken(new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}

internal sealed class HmacRefreshTokenProtector(IOptions<IdentityAuthOptions> options) : IRefreshTokenProtector
{
    private readonly byte[] pepper = Encoding.UTF8.GetBytes(options.Value.RefreshTokenPepper);

    public string Create(IdentitySession session)
    {
        var material = $"{session.SessionId}:{session.RefreshCounter}";
        var signature = HMACSHA256.HashData(pepper, Encoding.UTF8.GetBytes(material));
        return $"{session.SessionId}.{Base64UrlEncoder.Encode(signature)}";
    }

    public string Hash(string rawToken) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

    public bool Matches(string rawToken, string expectedHash)
    {
        if (expectedHash.Length != 64)
            return false;
        byte[] expected;
        try
        {
            expected = Convert.FromHexString(expectedHash);
        }
        catch (FormatException)
        {
            return false;
        }
        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    public bool HasExpectedShape(string rawToken, out string sessionId)
    {
        sessionId = string.Empty;
        var separator = rawToken.IndexOf('.', StringComparison.Ordinal);
        if (separator < 1 || separator == rawToken.Length - 1)
        {
            return false;
        }

        sessionId = rawToken[..separator];
        return sessionId.StartsWith("ses_", StringComparison.Ordinal) && sessionId.Length <= 64;
    }
}

internal sealed class HmacIdentityRequestFingerprinter(IOptions<IdentityAuthOptions> options) : IIdentityRequestFingerprinter
{
    private readonly byte[] key = Encoding.UTF8.GetBytes(options.Value.RefreshTokenPepper);

    public string Create(params string?[] values)
    {
        var canonical = string.Join('\u001f', values.Select(value => value?.Trim() ?? string.Empty));
        return Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(canonical)));
    }
}

internal sealed class ConfiguredIdentitySessionPolicy(IOptions<IdentityAuthOptions> options) : IIdentitySessionPolicy
{
    public TimeSpan IdleLifetime => TimeSpan.FromDays(options.Value.Session.IdleDays);
    public TimeSpan AbsoluteLifetime => TimeSpan.FromDays(options.Value.Session.AbsoluteDays);
}
