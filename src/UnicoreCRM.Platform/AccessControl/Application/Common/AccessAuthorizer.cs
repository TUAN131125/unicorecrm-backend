using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.AccessControl.Domain;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Platform.AccessControl.Application.Common;

internal sealed class AccessAuthorizer(
    ICurrentWorkspace currentWorkspace,
    IAccessControlPersistence persistence,
    IResolvedAuthorizationContextSetter contextSetter,
    TimeProvider timeProvider) : IAccessAuthorizer, IDelegatedAccessAuthorizer, IAccessContextAuthorizer
{
    public async Task<AccessAuthorizationDecision> AuthorizeAsync(
        AccessRequirement requirement,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        var evaluation = await AuthorizeWithContextAsync(requirement, correlationId, cancellationToken);
        return new AccessAuthorizationDecision(
            evaluation.IsAllowed,
            evaluation.Code,
            evaluation.IsAllowed ? evaluation.Context : null);
    }

    /// <summary>
    /// The single authoritative evaluation of one capability. The effective policy is loaded once,
    /// the supplied business capability is the capability that is evaluated and the capability that
    /// the <see cref="AuthorizationDecisionRecord"/> audit evidence records, and the effective
    /// context produced by that same evaluation is returned to the caller.
    ///
    /// <para>The context is returned on a denial too. A denied caller is still a resolved
    /// membership of the trusted Workspace, and the record-access projection has to be able to
    /// answer "denied for this Workspace" without a second policy load that could observe different
    /// state. Only an allowed decision publishes the context as the request's resolved authorization
    /// context.</para>
    /// </summary>
    public async Task<AccessContextAuthorization> AuthorizeWithContextAsync(
        AccessRequirement requirement,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        if (!currentWorkspace.IsResolved)
            return new AccessContextAuthorization(false, AccessErrors.WorkspaceMismatch().Code, null);

        var trusted = currentWorkspace.Require();
        var evaluation = await EvaluateAsync(trusted, requirement, correlationId, cancellationToken);
        if (evaluation.IsAllowed)
            contextSetter.Set(evaluation.Context!);
        return evaluation;
    }

    public async Task<AccessAuthorizationDecision> AuthorizeAsync(
        TrustedWorkspaceContext trustedWorkspace,
        AccessRequirement requirement,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(trustedWorkspace);
        ArgumentNullException.ThrowIfNull(requirement);
        var evaluation = await EvaluateAsync(trustedWorkspace, requirement, correlationId, cancellationToken);
        return new AccessAuthorizationDecision(
            evaluation.IsAllowed,
            evaluation.Code,
            evaluation.IsAllowed ? evaluation.Context : null);
    }

    private async Task<AccessContextAuthorization> EvaluateAsync(
        TrustedWorkspaceContext trusted,
        AccessRequirement requirement,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var state = await persistence.LoadEffectiveStateAsync(trusted.WorkspaceId, trusted.MembershipId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var effective = EffectiveAuthorizationPolicy.Evaluate(state);
        var context = AccessProjection.Context(trusted, effective, now);
        var allowed = effective.Capabilities.Contains(requirement.Capability, StringComparer.Ordinal);
        persistence.AddDecision(new AuthorizationDecisionRecord(
            trusted.WorkspaceId,
            trusted.MembershipId,
            requirement.Capability,
            allowed,
            correlationId,
            now));
        await persistence.SaveChangesAsync(cancellationToken);

        return new AccessContextAuthorization(
            allowed,
            allowed ? "AUTHORIZED" : AccessErrors.AccessDenied().Code,
            context);
    }
}
