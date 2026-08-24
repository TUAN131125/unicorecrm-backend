namespace UnicoreCRM.Platform.AccessControl.Domain;

internal sealed record EffectiveAccessState(
    IReadOnlyList<EffectiveRoleState> Roles,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<EffectiveDataScopePolicy> DataScopes,
    IReadOnlyList<EffectiveFieldSecurityPolicy> FieldSecurity);

internal sealed record EffectiveRoleState(string RoleId, string? SourceTemplateId);
internal sealed record EffectiveDataScopePolicy(string ResourceKey, AccessDataScope Scope);
internal sealed record EffectiveFieldSecurityPolicy(string ResourceKey, string FieldKey, AccessFieldAccess Access);

internal sealed record EffectiveAuthorizationState(
    IReadOnlyList<string> RoleIds,
    IReadOnlyList<string> RoleTemplateIds,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> ProductSpaces,
    IReadOnlyList<EffectiveDataScopePolicy> DataScopes,
    IReadOnlyList<EffectiveFieldSecurityPolicy> FieldSecurity);

internal static class EffectiveAuthorizationPolicy
{
    internal static EffectiveAuthorizationState Evaluate(EffectiveAccessState state)
    {
        var capabilities = state.Capabilities
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var dataScopes = state.DataScopes
            .GroupBy(policy => policy.ResourceKey, StringComparer.Ordinal)
            .Select(group => new EffectiveDataScopePolicy(group.Key, group.Max(policy => policy.Scope)))
            .OrderBy(policy => policy.ResourceKey, StringComparer.Ordinal)
            .ToArray();
        var fieldSecurity = state.FieldSecurity
            .GroupBy(policy => (policy.ResourceKey, policy.FieldKey))
            .Select(group => new EffectiveFieldSecurityPolicy(
                group.Key.ResourceKey,
                group.Key.FieldKey,
                group.Min(policy => policy.Access)))
            .OrderBy(policy => policy.ResourceKey, StringComparer.Ordinal)
            .ThenBy(policy => policy.FieldKey, StringComparer.Ordinal)
            .ToArray();

        if (dataScopes.Length > 1000)
            throw new InvalidOperationException("Effective authorization exceeds the contract data-scope limit.");
        if (fieldSecurity.Length > 2000)
            throw new InvalidOperationException("Effective authorization exceeds the contract field-security limit.");

        return new EffectiveAuthorizationState(
            state.Roles.Select(role => role.RoleId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            state.Roles.Select(role => role.SourceTemplateId).OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            capabilities,
            ProductSpaces(capabilities),
            dataScopes,
            fieldSecurity);
    }

    private static IReadOnlyList<string> ProductSpaces(IReadOnlyList<string> capabilities)
    {
        var result = new List<string>(3);
        if (capabilities.Any(capability =>
                !capability.StartsWith("studio.", StringComparison.Ordinal)
                && !capability.StartsWith("access.", StringComparison.Ordinal)
                && !capability.StartsWith("audit.", StringComparison.Ordinal)))
            result.Add("crm");
        if (capabilities.Contains("studio.read", StringComparer.Ordinal)
            || capabilities.Contains("studio.configure", StringComparer.Ordinal))
            result.Add("studio");
        if (capabilities.Contains("access.read", StringComparer.Ordinal)
            || capabilities.Contains("access.configure", StringComparer.Ordinal)
            || capabilities.Contains("audit.read", StringComparer.Ordinal))
            result.Add("people");
        return result;
    }
}

internal static class AccessPolicyWireValues
{
    internal static string ToWireValue(this AccessDataScope scope) => scope switch
    {
        AccessDataScope.Own => "OWN",
        AccessDataScope.Team => "TEAM",
        AccessDataScope.Workspace => "WORKSPACE",
        _ => "CUSTOM"
    };

    internal static string ToWireValue(this AccessFieldAccess access) => access switch
    {
        AccessFieldAccess.Masked => "MASKED",
        AccessFieldAccess.ReadOnly => "READ_ONLY",
        AccessFieldAccess.ReadWrite => "READ_WRITE",
        _ => "HIDDEN"
    };
}
