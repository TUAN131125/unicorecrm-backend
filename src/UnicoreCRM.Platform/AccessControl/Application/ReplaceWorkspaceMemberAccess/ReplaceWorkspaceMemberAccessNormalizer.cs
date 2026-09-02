using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Platform.AccessControl.Application.ReplaceWorkspaceMemberAccess;

internal static partial class ReplaceWorkspaceMemberAccessNormalizer
{
    private static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);

    internal static bool TryNormalize(
        string membershipId,
        long expectedMemberAccessVersion,
        string rawBody,
        out NormalizedReplaceWorkspaceMemberAccess? normalized,
        out IReadOnlyDictionary<string, string[]> errors)
    {
        normalized = null;
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        ReplaceWorkspaceMemberAccessRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<ReplaceWorkspaceMemberAccessRequest>(rawBody, JsonOptions);
        }
        catch (JsonException)
        {
            errors = new Dictionary<string, string[]> { ["body"] = ["The JSON request body does not match the contract."] };
            return false;
        }

        if (request is null)
        {
            errors = new Dictionary<string, string[]> { ["body"] = ["A JSON request body is required."] };
            return false;
        }

        if (!EntityIdPattern().IsMatch(membershipId))
            fields["membershipId"] = ["membershipId must satisfy EntityId exactly."];

        var roleIds = NormalizeRoleIds(request.RoleIds, fields);
        ValidateTeamIds(request.TeamIds, fields);
        if (fields.Count != 0)
        {
            errors = fields;
            return false;
        }

        var canonical = JsonSerializer.Serialize(new
        {
            schema = "replaceWorkspaceMemberAccess:v1",
            membershipId,
            expectedMemberAccessVersion,
            roleIds,
            teamIds = Array.Empty<string>()
        }, JsonOptions);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        normalized = new NormalizedReplaceWorkspaceMemberAccess(
            membershipId,
            expectedMemberAccessVersion,
            roleIds!,
            fingerprint);
        errors = fields;
        return true;
    }

    private static IReadOnlyList<string>? NormalizeRoleIds(
        IReadOnlyList<string?>? values,
        IDictionary<string, string[]> errors)
    {
        if (values is null)
        {
            errors["roleIds"] = ["roleIds is required."];
            return null;
        }
        if (values.Count > 100)
        {
            errors["roleIds"] = ["roleIds must contain at most 100 values."];
            return null;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var normalized = new List<string>(values.Count);
        for (var index = 0; index < values.Count; index++)
        {
            var value = values[index];
            var field = $"roleIds[{index}]";
            if (value is null || !EntityIdPattern().IsMatch(value))
            {
                errors[field] = ["Role ID must satisfy EntityId exactly."];
                continue;
            }
            if (!seen.Add(value))
            {
                errors[field] = ["Role IDs must be unique."];
                continue;
            }
            normalized.Add(value);
        }
        normalized.Sort(StringComparer.Ordinal);
        return normalized;
    }

    private static void ValidateTeamIds(
        IReadOnlyList<string?>? values,
        IDictionary<string, string[]> errors)
    {
        if (values is null)
        {
            errors["teamIds"] = ["teamIds is required."];
            return;
        }
        if (values.Count > 100)
        {
            errors["teamIds"] = ["teamIds must contain at most 100 values."];
            return;
        }
        if (values.Count != 0)
            errors["teamIds"] = ["teamIds must be the empty array because team membership is Workspace-owned."];
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex EntityIdPattern();
}
