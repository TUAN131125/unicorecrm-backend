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
        // Resource and field keys are one canonical identity regardless of casing. Grouping them
        // case-sensitively would let two roles that spell the same key differently produce two
        // separate effective entries, so a role holding `Support`/`READ_WRITE` could sit beside a
        // role holding `support`/`MASKED` and the restrictive entry would never win. The emitted
        // spelling is the ordinal-least member of the group, so the projection stays deterministic
        // and is unchanged whenever the stored rows already agree on casing.
        var dataScopes = state.DataScopes
            .GroupBy(policy => policy.ResourceKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => new EffectiveDataScopePolicy(
                CanonicalKey(group.Select(policy => policy.ResourceKey)),
                group.Max(policy => policy.Scope)))
            .OrderBy(policy => policy.ResourceKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var fieldSecurity = state.FieldSecurity
            .GroupBy(
                policy => (policy.ResourceKey, policy.FieldKey),
                RecordAccessKey.PairComparer)
            .Select(group => new EffectiveFieldSecurityPolicy(
                CanonicalKey(group.Select(policy => policy.ResourceKey)),
                CanonicalKey(group.Select(policy => policy.FieldKey)),
                group.Min(policy => policy.Access)))
            .OrderBy(policy => policy.ResourceKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(policy => policy.FieldKey, StringComparer.OrdinalIgnoreCase)
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

    private static string CanonicalKey(IEnumerable<string> spellings) =>
        spellings.Order(StringComparer.Ordinal).First();

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
