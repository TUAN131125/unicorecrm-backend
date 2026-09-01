using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.AccessControl.Domain;

namespace UnicoreCRM.Platform.AccessControl.Application.CreateAccessRole;

internal static partial class CreateAccessRoleNormalizer
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

        var name = NormalizeRequiredText(request.Name, 160, "name", fields);
        var description = NormalizeOptionalText(request.Description, 500, "description", fields);
        var sourceTemplateId = NormalizeOptionalText(request.SourceTemplateId, 160, "sourceTemplateId", fields);
        var capabilityInputs = NormalizeCapabilities(request.Capabilities, fields);
        var dataScopes = NormalizeDataScopes(request.DataScopes, fields);
        var fieldSecurity = NormalizeFieldSecurity(request.FieldSecurity, fields);

        if (fields.Count != 0)
        {
            errors = fields;
            return false;
        }

        var sortedCapabilities = capabilityInputs!.Select(item => item.Value).Order(StringComparer.Ordinal).ToArray();
        var sortedScopes = dataScopes!.OrderBy(item => item.ResourceKey, StringComparer.Ordinal).ToArray();
        var sortedFields = fieldSecurity!
            .OrderBy(item => item.ResourceKey, StringComparer.Ordinal)
            .ThenBy(item => item.FieldKey, StringComparer.Ordinal)
            .ToArray();
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
                scope = ToWire(item.Scope),
                allowedOwnerIds = item.AllowedOwnerIds
            }),
            fieldSecurity = sortedFields.Select(item => new
            {
                item.ResourceKey,
                item.FieldKey,
                access = ToWire(item.Access)
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

    private static string? NormalizeRequiredText(
        string? value,
        int maximum,
        string field,
        Dictionary<string, string[]> errors)
    {
        if (value is null)
        {
            errors[field] = [$"{field} is required."];
            return null;
        }
        var trimmed = value.Trim();
        if (trimmed.Length == 0 || ScalarLength(trimmed) > maximum)
            errors[field] = [$"{field} must contain between 1 and {maximum} Unicode scalar values after trimming."];
        return trimmed;
    }

    private static string? NormalizeOptionalText(
        string? value,
        int maximum,
        string field,
        Dictionary<string, string[]> errors)
    {
        if (value is null)
            return null;
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
            return null;
        if (ScalarLength(trimmed) > maximum)
            errors[field] = [$"{field} cannot exceed {maximum} Unicode scalar values after trimming."];
        return trimmed;
    }

    private static IReadOnlyList<NormalizedCapabilityInput>? NormalizeCapabilities(
        IReadOnlyList<string?>? values,
        Dictionary<string, string[]> errors)
    {
        if (values is null)
        {
            errors["capabilities"] = ["capabilities is required."];
            return null;
        }
        if (values.Count > 500)
            errors["capabilities"] = ["capabilities cannot contain more than 500 items."];
        var result = new List<NormalizedCapabilityInput>(Math.Min(values.Count, 500));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < values.Count; index++)
        {
            var value = values[index];
            if (value is null)
            {
                errors[$"capabilities[{index}]"] = ["Capability cannot be null."];
                continue;
            }
            var trimmed = value.Trim();
            if (trimmed.Length == 0 || ScalarLength(trimmed) > 160)
                errors[$"capabilities[{index}]"] = ["Capability must contain between 1 and 160 Unicode scalar values after trimming."];
            else if (!seen.Add(trimmed))
                errors[$"capabilities[{index}]"] = ["Capability is duplicated."];
            else
                result.Add(new NormalizedCapabilityInput(trimmed, index));
        }
        return result;
    }

    private static IReadOnlyList<NormalizedDataScope>? NormalizeDataScopes(
        IReadOnlyList<AccessRoleDataScopeInput?>? values,
        Dictionary<string, string[]> errors)
    {
        if (values is null)
        {
            errors["dataScopes"] = ["dataScopes is required."];
            return null;
        }
        if (values.Count > 5000)
            errors["dataScopes"] = ["dataScopes cannot contain more than 5000 items."];
        var result = new List<NormalizedDataScope>(Math.Min(values.Count, 5000));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < values.Count; index++)
        {
            var value = values[index];
            if (value is null)
            {
                errors[$"dataScopes[{index}]"] = ["Data scope cannot be null."];
                continue;
            }
            var resource = NormalizeKey(value.ResourceKey, $"dataScopes[{index}].resourceKey", errors);
            if (resource is not null && !seen.Add(resource))
                errors[$"dataScopes[{index}].resourceKey"] = ["Canonical resource key is duplicated."];
            if (!TryScope(value.Scope, out var scope))
                errors[$"dataScopes[{index}].scope"] = ["scope must be OWN, TEAM, WORKSPACE, or CUSTOM."];
            var owners = NormalizeOwnerIds(value.AllowedOwnerIds, index, errors);
            if (scope is not AccessDataScope.Custom && owners.Count != 0)
                errors[$"dataScopes[{index}].allowedOwnerIds"] = ["allowedOwnerIds may be non-empty only for CUSTOM."];
            if (resource is not null && TryScope(value.Scope, out scope))
                result.Add(new NormalizedDataScope(resource, scope, owners));
        }
        return result;
    }

    private static IReadOnlyList<string> NormalizeOwnerIds(
        IReadOnlyList<string?>? values,
        int scopeIndex,
        Dictionary<string, string[]> errors)
    {
        if (values is null)
            return [];
        if (values.Count > 500)
            errors[$"dataScopes[{scopeIndex}].allowedOwnerIds"] = ["allowedOwnerIds cannot contain more than 500 items."];
        var result = new List<string>(Math.Min(values.Count, 500));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < values.Count; index++)
        {
            var value = values[index];
            var field = $"dataScopes[{scopeIndex}].allowedOwnerIds[{index}]";
            if (value is null || !EntityIdPattern().IsMatch(value))
                errors[field] = ["Owner ID must satisfy EntityId exactly."];
            else if (!seen.Add(value))
                errors[field] = ["Owner ID is duplicated."];
            else
                result.Add(value);
        }
        return result.Order(StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<NormalizedFieldSecurity>? NormalizeFieldSecurity(
        IReadOnlyList<AccessRoleFieldSecurityInput?>? values,
        Dictionary<string, string[]> errors)
    {
        if (values is null)
        {
            errors["fieldSecurity"] = ["fieldSecurity is required."];
            return null;
        }
        if (values.Count > 10000)
            errors["fieldSecurity"] = ["fieldSecurity cannot contain more than 10000 items."];
        var result = new List<NormalizedFieldSecurity>(Math.Min(values.Count, 10000));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < values.Count; index++)
        {
            var value = values[index];
            if (value is null)
            {
                errors[$"fieldSecurity[{index}]"] = ["Field-security policy cannot be null."];
                continue;
            }
            var resource = NormalizeKey(value.ResourceKey, $"fieldSecurity[{index}].resourceKey", errors);
            var field = NormalizeKey(value.FieldKey, $"fieldSecurity[{index}].fieldKey", errors);
            if (resource is not null && field is not null && !seen.Add(resource + "\n" + field))
                errors[$"fieldSecurity[{index}].fieldKey"] = ["Canonical resource/field pair is duplicated."];
            if (!TryAccess(value.Access, out var access))
                errors[$"fieldSecurity[{index}].access"] = ["access must be READ_WRITE, READ_ONLY, MASKED, or HIDDEN."];
            if (resource is not null && field is not null && TryAccess(value.Access, out access))
                result.Add(new NormalizedFieldSecurity(resource, field, access));
        }
        return result;
    }

    private static string? NormalizeKey(string? value, string field, Dictionary<string, string[]> errors)
    {
        if (value is null)
        {
            errors[field] = ["Value is required."];
            return null;
        }
        var trimmed = value.Trim();
        if (trimmed.Length == 0 || ScalarLength(trimmed) > 160)
        {
            errors[field] = ["Value must contain between 1 and 160 Unicode scalar values after trimming."];
            return null;
        }
        return trimmed.ToLowerInvariant();
    }

    private static int ScalarLength(string value) => value.EnumerateRunes().Count();

    private static bool TryScope(string? value, out AccessDataScope scope)
    {
        scope = value switch
        {
            "OWN" => AccessDataScope.Own,
            "TEAM" => AccessDataScope.Team,
            "WORKSPACE" => AccessDataScope.Workspace,
            "CUSTOM" => AccessDataScope.Custom,
            _ => default
        };
        return value is "OWN" or "TEAM" or "WORKSPACE" or "CUSTOM";
    }

    private static bool TryAccess(string? value, out AccessFieldAccess access)
    {
        access = value switch
        {
            "READ_WRITE" => AccessFieldAccess.ReadWrite,
            "READ_ONLY" => AccessFieldAccess.ReadOnly,
            "MASKED" => AccessFieldAccess.Masked,
            "HIDDEN" => AccessFieldAccess.Hidden,
            _ => default
        };
        return value is "READ_WRITE" or "READ_ONLY" or "MASKED" or "HIDDEN";
    }

    internal static string ToWire(AccessDataScope value) => value switch
    {
        AccessDataScope.Own => "OWN",
        AccessDataScope.Team => "TEAM",
        AccessDataScope.Workspace => "WORKSPACE",
        AccessDataScope.Custom => "CUSTOM",
        _ => throw new InvalidOperationException("Unsupported data scope.")
    };

    internal static string ToWire(AccessFieldAccess value) => value switch
    {
        AccessFieldAccess.ReadWrite => "READ_WRITE",
        AccessFieldAccess.ReadOnly => "READ_ONLY",
        AccessFieldAccess.Masked => "MASKED",
        AccessFieldAccess.Hidden => "HIDDEN",
        _ => throw new InvalidOperationException("Unsupported field access.")
    };

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex EntityIdPattern();
}
