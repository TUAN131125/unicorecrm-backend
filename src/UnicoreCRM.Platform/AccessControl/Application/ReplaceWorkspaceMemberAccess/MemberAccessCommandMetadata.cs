using UnicoreCRM.Platform.AccessControl.Application.Common;

namespace UnicoreCRM.Platform.AccessControl.Application.ReplaceWorkspaceMemberAccess;

internal static class MemberAccessCommandMetadata
{
    internal static IReadOnlyDictionary<string, string[]> Validate(
        string requestId,
        string suppliedCorrelationId,
        string idempotencyKey,
        string ifMatch,
        out long expectedMemberAccessVersion)
    {
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (requestId.Length is < 8 or > 128)
            fields["X-Request-Id"] = ["X-Request-Id must contain between 8 and 128 characters."];
        if (suppliedCorrelationId.Length != 0 && suppliedCorrelationId.Length is < 8 or > 128)
            fields["X-Correlation-Id"] = ["X-Correlation-Id must contain between 8 and 128 characters."];
        if (idempotencyKey.Length is < 8 or > 128)
            fields["Idempotency-Key"] = ["Idempotency-Key must contain between 8 and 128 characters."];
        if (!AccessRoleCommandMetadata.TryParseIfMatch(ifMatch, out expectedMemberAccessVersion))
        {
            fields["If-Match"] =
                ["If-Match must be exactly one strong quoted non-negative decimal MemberAccessVersion, for example \"3\"."];
        }
        return fields;
    }
}
