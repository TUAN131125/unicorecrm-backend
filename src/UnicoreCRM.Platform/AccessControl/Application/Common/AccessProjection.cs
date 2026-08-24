using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.AccessControl.Domain;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Platform.AccessControl.Application.Common;

/// <summary>
/// Maps evaluated authorization state onto the public authorization-context contract.
/// Authorization decisions remain in <see cref="AccessAuthorizer"/>.
/// </summary>
internal static class AccessProjection
{
    internal static AuthorizationContextDocument Context(
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
            effective.DataScopes.Select(policy => new AuthorizationDataScopeEntry(policy.ResourceKey, ToWireValue(policy.Scope))).ToArray(),
            effective.FieldSecurity.Select(policy => new AuthorizationFieldAccessEntry(policy.ResourceKey, policy.FieldKey, ToWireValue(policy.Access))).ToArray(),
            evaluatedAt);

    private static string ToWireValue(AccessDataScope scope) => scope switch
    {
        AccessDataScope.Own => "OWN",
        AccessDataScope.Team => "TEAM",
        AccessDataScope.Workspace => "WORKSPACE",
        _ => "CUSTOM"
    };

    private static string ToWireValue(AccessFieldAccess access) => access switch
    {
        AccessFieldAccess.Masked => "MASKED",
        AccessFieldAccess.ReadOnly => "READ_ONLY",
        AccessFieldAccess.ReadWrite => "READ_WRITE",
        _ => "HIDDEN"
    };
}
