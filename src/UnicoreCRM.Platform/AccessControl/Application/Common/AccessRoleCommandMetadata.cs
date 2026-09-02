using System.Text.RegularExpressions;

namespace UnicoreCRM.Platform.AccessControl.Application.Common;

/// <summary>
/// The transport metadata rules shared by the versioned AccessControl role commands. They are
/// evaluated after the capability decision and before anything reads the target role, so a caller
/// who fails them learns nothing about whether the role exists, its version or its state.
/// </summary>
internal static partial class AccessRoleCommandMetadata
{
    /// <summary>
    /// <c>If-Match</c> is optimistic concurrency control over <c>AccessRole.Version</c>, never the
    /// Workspace directory revision. Exactly one strong, double-quoted, non-negative decimal
    /// validator is accepted: a weak validator, the wildcard, an unquoted value, a signed or
    /// non-decimal value and a multi-value header are all rejected. A comma-separated list fails
    /// this pattern, so a single header carrying two validators is rejected too.
    /// </summary>
    [GeneratedRegex("^\"(?<version>[0-9]{1,19})\"$", RegexOptions.CultureInvariant)]
    private static partial Regex IfMatchPattern();

    internal static bool TryParseIfMatch(string value, out long expectedVersion)
    {
        expectedVersion = 0;
        var match = IfMatchPattern().Match(value);
        return match.Success && long.TryParse(match.Groups["version"].Value, out expectedVersion);
    }

    /// <summary>
    /// Validates the required command headers and the <c>If-Match</c> syntax together, so a request
    /// that is malformed in several ways reports every offending header at once.
    /// </summary>
    internal static IReadOnlyDictionary<string, string[]> Validate(
        string requestId,
        string suppliedCorrelationId,
        string idempotencyKey,
        string ifMatch,
        out long expectedVersion)
    {
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (requestId.Length is < 8 or > 128)
            fields["X-Request-Id"] = ["X-Request-Id must contain between 8 and 128 characters."];
        if (suppliedCorrelationId.Length != 0 && suppliedCorrelationId.Length is < 8 or > 128)
            fields["X-Correlation-Id"] = ["X-Correlation-Id must contain between 8 and 128 characters."];
        if (idempotencyKey.Length is < 8 or > 128)
            fields["Idempotency-Key"] = ["Idempotency-Key must contain between 8 and 128 characters."];
        if (!TryParseIfMatch(ifMatch, out expectedVersion))
            fields["If-Match"] = ["If-Match must be exactly one strong quoted non-negative decimal role version, for example \"3\"."];
        return fields;
    }
}
