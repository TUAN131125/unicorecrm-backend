using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace UnicoreCRM.Integrations.Webhooks.Inbound.Infrastructure;

internal sealed class GenericSignedJsonVerifier(TimeProvider timeProvider)
{
    internal static readonly TimeSpan ReplayWindow = TimeSpan.FromMinutes(5);

    internal SignatureVerificationResult Verify(
        string timestamp,
        string deliveryId,
        string suppliedSignature,
        ReadOnlySpan<byte> rawPayload,
        string secret)
    {
        if (!long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var unixSeconds)
            || !string.Equals(timestamp, unixSeconds.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
        {
            return SignatureVerificationResult.MalformedTimestamp;
        }

        DateTimeOffset signedAt;
        try
        {
            signedAt = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return SignatureVerificationResult.MalformedTimestamp;
        }

        if ((timeProvider.GetUtcNow() - signedAt).Duration() > ReplayWindow)
            return SignatureVerificationResult.ExpiredTimestamp;
        if (!TryDecodeSignature(suppliedSignature, out var supplied))
            return SignatureVerificationResult.InvalidSignature;

        var prefix = Encoding.UTF8.GetBytes($"{timestamp}\n{deliveryId}\n");
        var signingMaterial = new byte[prefix.Length + rawPayload.Length];
        prefix.CopyTo(signingMaterial, 0);
        rawPayload.CopyTo(signingMaterial.AsSpan(prefix.Length));
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), signingMaterial);
        return CryptographicOperations.FixedTimeEquals(expected, supplied)
            ? SignatureVerificationResult.Valid
            : SignatureVerificationResult.InvalidSignature;
    }

    private static bool TryDecodeSignature(string value, out byte[] bytes)
    {
        bytes = [];
        const string prefix = "sha256=";
        if (!value.StartsWith(prefix, StringComparison.Ordinal) || value.Length != prefix.Length + 64)
            return false;
        try
        {
            bytes = Convert.FromHexString(value[prefix.Length..]);
            return bytes.Length == 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

internal enum SignatureVerificationResult
{
    Valid,
    MalformedTimestamp,
    ExpiredTimestamp,
    InvalidSignature
}
