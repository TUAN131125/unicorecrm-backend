using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UnicoreCRM.Platform.AccessControl.Application.AccessDirectory;
using UnicoreCRM.Platform.AccessControl.Application.Common;
using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Platform.AccessControl.Application.ReplaceAccessRole;

/// <summary>
/// Wire deserialization and the <c>replaceAccessRole:v1</c> effective-request fingerprint. The
/// <c>If-Match</c> syntax lives in <see cref="AccessRoleCommandMetadata"/>, shared with the other
/// versioned role commands. Every field rule is delegated to
/// <see cref="AccessRoleInputNormalizer"/> so a replacement cannot bypass a create-time input rule.
/// </summary>
internal static class ReplaceAccessRoleNormalizer
{
    private static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);

    internal static bool TryNormalize(
        string roleId,
        long expectedVersion,
        string rawBody,
        out NormalizedReplaceAccessRole? normalized,
        out IReadOnlyDictionary<string, string[]> errors)
    {
        normalized = null;
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        ReplaceAccessRoleRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<ReplaceAccessRoleRequest>(rawBody, JsonOptions);
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

        var name = AccessRoleInputNormalizer.RequiredText(request.Name, 160, "name", fields);
        var description = AccessRoleInputNormalizer.OptionalText(request.Description, 500, "description", fields);
        var sourceTemplateId = AccessRoleInputNormalizer.OptionalText(request.SourceTemplateId, 160, "sourceTemplateId", fields);
        var capabilityInputs = AccessRoleInputNormalizer.Capabilities(request.Capabilities, fields);
        var dataScopes = AccessRoleInputNormalizer.DataScopes(request.DataScopes, fields);
        var fieldSecurity = AccessRoleInputNormalizer.FieldSecurity(request.FieldSecurity, fields);

        // replaceAccessRole never performs a lifecycle transition in either direction:
        // archiveAccessRole exclusively owns active -> inactive and no reactivation is admitted.
        // isActive is therefore a required assertion that the role stays active, and any other
        // value is an unconditionally invalid field. Rejecting it here - with the rest of the
        // normalized body and before the target is ever read - keeps the choice of error from
        // disclosing the stored active state of a role the caller may not be entitled to observe.
        if (request.IsActive is null)
            fields["isActive"] = ["isActive is required."];
        else if (request.IsActive is not true)
            fields["isActive"] = ["isActive must be true. replaceAccessRole cannot change the active state of a role."];

        if (fields.Count != 0)
        {
            errors = fields;
            return false;
        }

        var sortedCapabilities = AccessRoleInputNormalizer.SortCapabilities(capabilityInputs!);
        var sortedScopes = AccessRoleInputNormalizer.Sort(dataScopes!);
        var sortedFields = AccessRoleInputNormalizer.Sort(fieldSecurity!);

        // The target role and the expected version are part of effective command identity: the same
        // idempotency key aimed at another role, or replayed after the role moved on, is a
        // different command rather than a replay.
        var canonical = JsonSerializer.Serialize(new
        {
            schema = "replaceAccessRole:v1",
            roleId,
            expectedVersion,
            name,
            description,
            sourceTemplateId,
            isActive = true,
            capabilities = sortedCapabilities,
            dataScopes = sortedScopes.Select(item => new
            {
                item.ResourceKey,
                scope = AccessDirectoryWire.ToWire(item.Scope),
                allowedOwnerIds = item.AllowedOwnerIds
            }),
            fieldSecurity = sortedFields.Select(item => new
            {
                item.ResourceKey,
                item.FieldKey,
                access = AccessDirectoryWire.ToWire(item.Access)
            })
        }, JsonOptions);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        normalized = new NormalizedReplaceAccessRole(
            roleId,
            expectedVersion,
            name!,
            name!.ToUpperInvariant(),
            description,
            sourceTemplateId,
            sortedCapabilities,
            capabilityInputs!,
            sortedScopes,
            sortedFields,
            fingerprint);
        errors = fields;
        return true;
    }
}
