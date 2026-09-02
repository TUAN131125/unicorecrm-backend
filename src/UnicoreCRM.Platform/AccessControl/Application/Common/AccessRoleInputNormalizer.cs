using System.Text;
using System.Text.RegularExpressions;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.AccessControl.Domain;

namespace UnicoreCRM.Platform.AccessControl.Application.Common;

internal sealed record NormalizedCapabilityInput(string Value, int OriginalIndex);

internal sealed record NormalizedDataScope(
    string ResourceKey,
    AccessDataScope Scope,
    IReadOnlyList<string> AllowedOwnerIds);

internal sealed record NormalizedFieldSecurity(
    string ResourceKey,
    string FieldKey,
    AccessFieldAccess Access);

/// <summary>
/// The single custom-Workspace-role input normalization shared by <c>createAccessRole</c> and
/// <c>replaceAccessRole</c>. The rules are frozen once by
/// <c>DEC-CREATEACCESSROLE-AUTHORITY-CLOSURE</c> and reused unchanged by
/// <c>DEC-REPLACEACCESSROLE-AUTHORITY-CLOSURE</c>, so both commands must derive their canonical
/// values here rather than restating them: a replacement can never bypass a create-time input rule.
///
/// <para>Normalization runs after wire/schema validation and before the effective-request
/// fingerprint is calculated. Text trimming removes leading and trailing Unicode whitespace and
/// public length limits are counted in Unicode scalar values after trimming. Array order carries no
/// business meaning, so collections are returned in canonical ordinal order.</para>
/// </summary>
internal static partial class AccessRoleInputNormalizer
{
    internal static string? RequiredText(
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

    /// <summary>
    /// An omitted, explicitly null or empty-after-trimming optional scalar is the canonical null.
    /// Under the full-replacement semantics of <c>replaceAccessRole</c> this clears the stored
    /// value; there is no preserve-on-omission behavior for either command.
    /// </summary>
    internal static string? OptionalText(
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

    /// <summary>
    /// The effective maximum is 500 rather than the 1,000 the wire schema accepts, because
    /// <c>AccessRoleDocument.capabilities</c> can represent at most 500 and every success response
    /// of both commands must carry the role document.
    /// </summary>
    internal static IReadOnlyList<NormalizedCapabilityInput>? Capabilities(
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

    internal static IReadOnlyList<NormalizedDataScope>? DataScopes(
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
            var resource = Key(value.ResourceKey, $"dataScopes[{index}].resourceKey", errors);
            if (resource is not null && !seen.Add(resource))
                errors[$"dataScopes[{index}].resourceKey"] = ["Canonical resource key is duplicated."];
            if (!TryScope(value.Scope, out var scope))
                errors[$"dataScopes[{index}].scope"] = ["scope must be OWN, TEAM, WORKSPACE, or CUSTOM."];
            var owners = OwnerIds(value.AllowedOwnerIds, index, errors);
            if (scope is not AccessDataScope.Custom && owners.Count != 0)
                errors[$"dataScopes[{index}].allowedOwnerIds"] = ["allowedOwnerIds may be non-empty only for CUSTOM."];
            if (resource is not null && TryScope(value.Scope, out scope))
                result.Add(new NormalizedDataScope(resource, scope, owners));
        }
        return result;
    }

    internal static IReadOnlyList<NormalizedFieldSecurity>? FieldSecurity(
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
            var resource = Key(value.ResourceKey, $"fieldSecurity[{index}].resourceKey", errors);
            var field = Key(value.FieldKey, $"fieldSecurity[{index}].fieldKey", errors);
            if (resource is not null && field is not null && !seen.Add(resource + "\n" + field))
                errors[$"fieldSecurity[{index}].fieldKey"] = ["Canonical resource/field pair is duplicated."];
            if (!TryAccess(value.Access, out var access))
                errors[$"fieldSecurity[{index}].access"] = ["access must be READ_WRITE, READ_ONLY, MASKED, or HIDDEN."];
            if (resource is not null && field is not null && TryAccess(value.Access, out access))
                result.Add(new NormalizedFieldSecurity(resource, field, access));
        }
        return result;
    }

    internal static IReadOnlyList<string> SortCapabilities(IReadOnlyList<NormalizedCapabilityInput> inputs) =>
        inputs.Select(item => item.Value).Order(StringComparer.Ordinal).ToArray();

    internal static IReadOnlyList<NormalizedDataScope> Sort(IReadOnlyList<NormalizedDataScope> scopes) =>
        scopes.OrderBy(item => item.ResourceKey, StringComparer.Ordinal).ToArray();

    internal static IReadOnlyList<NormalizedFieldSecurity> Sort(IReadOnlyList<NormalizedFieldSecurity> fields) =>
        fields
            .OrderBy(item => item.ResourceKey, StringComparer.Ordinal)
            .ThenBy(item => item.FieldKey, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<string> OwnerIds(
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

    private static string? Key(string? value, string field, Dictionary<string, string[]> errors)
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

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex EntityIdPattern();
}
