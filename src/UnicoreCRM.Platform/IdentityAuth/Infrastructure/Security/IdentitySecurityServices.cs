using System.Globalization;
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

/// <summary>
/// Six-digit verification codes and their keyed hashes.
///
/// The code is drawn from a cryptographic generator over the whole six-digit space without modulo
/// bias. Only a keyed HMAC-SHA256 digest is ever persisted, and the digest is bound to the owning
/// account so a digest read from one account row cannot be replayed against another. The HMAC key
/// is derived from the configured identity pepper under a distinct purpose label, so the same
/// secret never produces interchangeable digests across refresh tokens, idempotency fingerprints
/// and verification codes. Comparison is fixed-time.
///
/// A six-digit code has a small keyspace by contract, so the hash is a containment measure and not
/// the primary control: short expiry, a per-code attempt ceiling, single use and resend supersession
/// carry that weight.
/// </summary>
internal sealed class HmacIdentityVerificationCodeProtector : IIdentityVerificationCodeProtector
{
    private const string PurposeLabel = "unicore:identity:email-verification-code:v1";
    private readonly byte[] key;

    public HmacIdentityVerificationCodeProtector(IOptions<IdentityAuthOptions> options) =>
        key = HMACSHA256.HashData(Encoding.UTF8.GetBytes(options.Value.RefreshTokenPepper), Encoding.UTF8.GetBytes(PurposeLabel));

    public string Create() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);

    public string Hash(string accountId, string code) =>
        Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"{accountId}\u001f{code}")));

    public bool Matches(string accountId, string code, string expectedHash)
    {
        if (expectedHash.Length != 64)
        {
            return false;
        }

        byte[] expected;
        try
        {
            expected = Convert.FromHexString(expectedHash);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"{accountId}\u001f{code}"));
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}

/// <summary>
/// Authenticated encryption for the one value the email outbox must reconstruct after its issuing
/// transaction commits: the code it still has to send.
///
/// AES-GCM under a purpose-separated key derived from the configured identity pepper, with the
/// owning challenge identifier as associated data, so a stored payload cannot be moved to another
/// challenge row and cannot be read without the host's configured secret. Verification never uses
/// this path - it compares against the one-way digest on the challenge - so an unreadable payload
/// costs at most one undeliverable queued message.
/// </summary>
internal sealed class AesGcmIdentityEmailPayloadProtector : IIdentityEmailPayloadProtector
{
    private const string PurposeLabel = "unicore:identity:email-outbox-payload:v1";
    private const int NonceLength = 12;
    private const int TagLength = 16;
    private readonly byte[] key;

    public AesGcmIdentityEmailPayloadProtector(IOptions<IdentityAuthOptions> options) =>
        key = HMACSHA256.HashData(Encoding.UTF8.GetBytes(options.Value.RefreshTokenPepper), Encoding.UTF8.GetBytes(PurposeLabel));

    public string Protect(string challengeId, string code)
    {
        var plaintext = Encoding.UTF8.GetBytes(code);
        var nonce = RandomNumberGenerator.GetBytes(NonceLength);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagLength];
        using var aes = new AesGcm(key, TagLength);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, Encoding.UTF8.GetBytes(challengeId));
        var payload = new byte[NonceLength + TagLength + ciphertext.Length];
        nonce.CopyTo(payload, 0);
        tag.CopyTo(payload, NonceLength);
        ciphertext.CopyTo(payload, NonceLength + TagLength);
        return Convert.ToBase64String(payload);
    }

    public bool TryUnprotect(string challengeId, string protectedPayload, out string code)
    {
        code = string.Empty;
        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(protectedPayload);
        }
        catch (FormatException)
        {
            return false;
        }

        if (payload.Length <= NonceLength + TagLength)
        {
            return false;
        }

        var plaintext = new byte[payload.Length - NonceLength - TagLength];
        try
        {
            using var aes = new AesGcm(key, TagLength);
            aes.Decrypt(
                payload.AsSpan(0, NonceLength),
                payload.AsSpan(NonceLength + TagLength),
                payload.AsSpan(NonceLength, TagLength),
                plaintext,
                Encoding.UTF8.GetBytes(challengeId));
        }
        catch (CryptographicException)
        {
            return false;
        }

        code = Encoding.UTF8.GetString(plaintext);
        return true;
    }
}

internal sealed class ConfiguredIdentityEmailVerificationPolicy(IOptions<IdentityAuthOptions> options) : IIdentityEmailVerificationPolicy
{
    // Nested option members are not covered by the host's data-annotation validation, so the
    // admitted contract windows are enforced here rather than trusted from configuration.
    public TimeSpan CodeLifetime => TimeSpan.FromMinutes(Math.Clamp(options.Value.EmailVerification.ExpiryMinutes, 5, 10));
    public TimeSpan ResendInterval => TimeSpan.FromSeconds(Math.Clamp(options.Value.EmailVerification.ResendIntervalSeconds, 30, 3600));
    public int MaxAttempts => Math.Clamp(options.Value.EmailVerification.MaxAttempts, 1, 10);
    public int DeliveryMaxAttempts => Math.Clamp(options.Value.EmailVerification.Outbox.MaxAttempts, 1, 20);
}
