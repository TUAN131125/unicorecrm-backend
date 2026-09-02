using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UnicoreCRM.Platform.AccessControl.Application.AccessDirectory;
using UnicoreCRM.Platform.AccessControl.Application.Common;
using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Platform.AccessControl.Application.CreateAccessRole;

/// <summary>
/// Wire deserialization and the <c>createAccessRole:v1</c> effective-request fingerprint. Every
/// field rule is delegated to <see cref="AccessRoleInputNormalizer"/>, which is the one shared
/// custom-Workspace-role normalization; only the operation-specific fingerprint shape lives here.
/// </summary>
internal static class CreateAccessRoleNormalizer
{
    private static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);

    internal static bool TryNormalize(
        string rawBody,
        out NormalizedCreateAccessRole? normalized,
        out IReadOnlyDictionary<string, string[]> errors)
    {
        normalized = null;
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        CreateAccessRoleRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<CreateAccessRoleRequest>(rawBody, JsonOptions);
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

        if (fields.Count != 0)
        {
            errors = fields;
            return false;
        }

        var sortedCapabilities = AccessRoleInputNormalizer.SortCapabilities(capabilityInputs!);
        var sortedScopes = AccessRoleInputNormalizer.Sort(dataScopes!);
        var sortedFields = AccessRoleInputNormalizer.Sort(fieldSecurity!);
        var canonical = JsonSerializer.Serialize(new
        {
            schema = "createAccessRole:v1",
            name,
            description,
            sourceTemplateId,
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
        normalized = new NormalizedCreateAccessRole(
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
