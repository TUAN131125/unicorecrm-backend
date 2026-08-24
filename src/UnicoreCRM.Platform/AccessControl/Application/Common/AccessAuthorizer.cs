using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.AccessControl.Domain;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Platform.AccessControl.Application.Common;

internal sealed class AccessAuthorizer(
    ICurrentWorkspace currentWorkspace,
    IAccessControlPersistence persistence,
    IResolvedAuthorizationContextSetter contextSetter,
    TimeProvider timeProvider) : IAccessAuthorizer, IDelegatedAccessAuthorizer
{
    public async Task<AccessAuthorizationDecision> AuthorizeAsync(
        AccessRequirement requirement,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        if (!currentWorkspace.IsResolved)
            return new AccessAuthorizationDecision(false, AccessErrors.WorkspaceMismatch().Code, null);

        var trusted = currentWorkspace.Require();
        var decision = await EvaluateAsync(trusted, requirement, correlationId, cancellationToken);
        if (decision.IsAllowed)
            contextSetter.Set(decision.Context!);
        return decision;
    }

    public Task<AccessAuthorizationDecision> AuthorizeAsync(
        TrustedWorkspaceContext trustedWorkspace,
        AccessRequirement requirement,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(trustedWorkspace);
        ArgumentNullException.ThrowIfNull(requirement);
        return EvaluateAsync(trustedWorkspace, requirement, correlationId, cancellationToken);
    }

    private async Task<AccessAuthorizationDecision> EvaluateAsync(
        TrustedWorkspaceContext trusted,
        AccessRequirement requirement,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var state = await persistence.LoadEffectiveStateAsync(trusted.WorkspaceId, trusted.MembershipId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var effective = EffectiveAuthorizationPolicy.Evaluate(state);
        var context = Project(trusted, effective, now);
        var allowed = effective.Capabilities.Contains(requirement.Capability, StringComparer.Ordinal);
        persistence.AddDecision(new AuthorizationDecisionRecord(
            trusted.WorkspaceId,
            trusted.MembershipId,
            requirement.Capability,
            allowed,
            correlationId,
            now));
        await persistence.SaveChangesAsync(cancellationToken);

        if (!allowed)
            return new AccessAuthorizationDecision(false, AccessErrors.AccessDenied().Code, null);

        return new AccessAuthorizationDecision(true, "AUTHORIZED", context);
    }

    private static AuthorizationContextDocument Project(
        TrustedWorkspaceContext trusted,
        EffectiveAuthorizationState effective,
        DateTimeOffset evaluatedAt) =>
        new(
            trusted.WorkspaceId,
            trusted.MembershipId,
            trusted.MemberId,
            trusted.AccountId,
            effective.RoleIds,
            effective.RoleTemplateIds,
            effective.Capabilities,
            effective.ProductSpaces,
            effective.DataScopes.Select(policy => new AuthorizationDataScopeEntry(policy.ResourceKey, policy.Scope.ToWireValue())).ToArray(),
            effective.FieldSecurity.Select(policy => new AuthorizationFieldAccessEntry(policy.ResourceKey, policy.FieldKey, policy.Access.ToWireValue())).ToArray(),
            evaluatedAt);
}
