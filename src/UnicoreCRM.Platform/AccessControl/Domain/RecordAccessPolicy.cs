namespace UnicoreCRM.Platform.AccessControl.Domain;

/// <summary>
/// The outcome of evaluating an effective data scope against authoritative record facts.
/// </summary>
internal enum RecordScopeOutcome
{
    /// <summary>The record is inside the caller's effective record scope.</summary>
    Allowed = 0,

    /// <summary>
    /// The record is outside the caller's effective record scope, does not exist inside the
    /// trusted Workspace, or is governed by a scope with no admitted semantics. The three
    /// deliberately collapse onto one outcome so a caller cannot distinguish them.
    /// </summary>
    Denied = 1,

    /// <summary>No record identifier was supplied, so record scope was not applied.</summary>
    NotEvaluated = 2
}

internal sealed record RecordScopeDecision(RecordScopeOutcome Outcome, AccessDataScope Scope, bool? OwnerMatch);

/// <summary>
/// The single record-scope and field-security authority. It is deliberately pure: callers hand it
/// already-evaluated effective policy plus authoritative record facts, so no evaluation path can
/// reach persistence, a foreign owner, or the HTTP layer, and no second authority can form.
///
/// Absent-policy semantics are the ones the existing implemented readers
/// (<c>ITaskSummaryReader</c>, <c>ILeadSummaryReader</c>, <c>IDealSummaryReader</c>) already apply
/// and that the authorization contract persists: the stored policy model is explicit-restriction,
/// so a resource with no data-scope row is Workspace-scoped and a field with no field-security row
/// carries no restriction. Applying a different rule here would create a second, contradictory
/// authorization authority for the same stored state.
/// </summary>
internal static class RecordAccessPolicy
{
    internal static AccessDataScope ResolveScope(
        IReadOnlyList<EffectiveDataScopePolicy> dataScopes,
        string resourceKey)
    {
        foreach (var policy in dataScopes)
        {
            if (string.Equals(policy.ResourceKey, resourceKey, StringComparison.OrdinalIgnoreCase))
                return policy.Scope;
        }

        return AccessDataScope.Workspace;
    }

    /// <param name="recordFound">
    /// Whether the owning module holds the record inside the trusted Workspace. A foreign-Workspace
    /// record is reported as not found by the owner, so it can never reach an allowed outcome.
    /// </param>
    internal static RecordScopeDecision EvaluateScope(
        AccessDataScope scope,
        bool recordRequested,
        bool recordFound,
        string? ownerMemberId,
        string callerMemberId)
    {
        if (!recordRequested)
            return new RecordScopeDecision(RecordScopeOutcome.NotEvaluated, scope, null);
        if (!recordFound)
            return new RecordScopeDecision(RecordScopeOutcome.Denied, scope, null);

        switch (scope)
        {
            case AccessDataScope.Workspace:
                return new RecordScopeDecision(RecordScopeOutcome.Allowed, scope, null);
            case AccessDataScope.Own:
                var match = !string.IsNullOrEmpty(ownerMemberId)
                    && string.Equals(ownerMemberId, callerMemberId, StringComparison.Ordinal);
                return new RecordScopeDecision(
                    match ? RecordScopeOutcome.Allowed : RecordScopeOutcome.Denied,
                    scope,
                    match);
            default:
                // TEAM has no authoritative team ownership or team membership behind it, and CUSTOM
                // has no admitted allowed-owner semantics. Both fail closed rather than being
                // silently widened to Workspace.
                return new RecordScopeDecision(RecordScopeOutcome.Denied, scope, null);
        }
    }

    /// <summary>
    /// The effective field access for one field before the record decision caps it. The stored
    /// policy is already reduced to the most restrictive entry per resource/field pair by
    /// <see cref="EffectiveAuthorizationPolicy"/>; an absent entry carries no restriction.
    /// </summary>
    internal static AccessFieldAccess ResolveFieldAccess(
        IReadOnlyList<EffectiveFieldSecurityPolicy> fieldSecurity,
        string resourceKey,
        string fieldKey)
    {
        foreach (var policy in fieldSecurity)
        {
            if (string.Equals(policy.ResourceKey, resourceKey, StringComparison.OrdinalIgnoreCase)
                && string.Equals(policy.FieldKey, fieldKey, StringComparison.OrdinalIgnoreCase))
            {
                return policy.Access;
            }
        }

        return AccessFieldAccess.ReadWrite;
    }

    /// <summary>
    /// Caps field access by the record decision. A field can never be more permissive than the
    /// record it belongs to: nothing is visible when the record is not readable, and nothing is
    /// writable when the record is not updatable.
    /// </summary>
    internal static AccessFieldAccess Cap(AccessFieldAccess access, bool canRead, bool canUpdate)
    {
        if (!canRead)
            return AccessFieldAccess.Hidden;
        if (!canUpdate && access == AccessFieldAccess.ReadWrite)
            return AccessFieldAccess.ReadOnly;
        return access;
    }
}
