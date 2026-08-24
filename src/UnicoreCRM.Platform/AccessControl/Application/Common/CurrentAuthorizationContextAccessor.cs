using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Platform.AccessControl.Application.Common;

internal sealed class CurrentAuthorizationContextAccessor : ICurrentAuthorizationContext, IResolvedAuthorizationContextSetter
{
    private AuthorizationContextDocument? current;

    public bool IsResolved => current is not null;

    public AuthorizationContextDocument Require() =>
        current ?? throw new InvalidOperationException("An authoritative access context has not been resolved for this request.");

    public void Set(AuthorizationContextDocument context)
    {
        if (current is not null && !SameAuthority(current, context))
            throw new InvalidOperationException("The authorization context cannot change during a request.");
        current = context;
    }

    private static bool SameAuthority(
        AuthorizationContextDocument current,
        AuthorizationContextDocument next) =>
        current.WorkspaceId == next.WorkspaceId
        && current.MembershipId == next.MembershipId
        && current.MemberId == next.MemberId
        && current.AccountId == next.AccountId
        && current.RoleIds.SequenceEqual(next.RoleIds, StringComparer.Ordinal)
        && current.RoleTemplateIds.SequenceEqual(next.RoleTemplateIds, StringComparer.Ordinal)
        && current.Capabilities.SequenceEqual(next.Capabilities, StringComparer.Ordinal)
        && current.ProductSpaces.SequenceEqual(next.ProductSpaces, StringComparer.Ordinal)
        && current.DataScopes.SequenceEqual(next.DataScopes)
        && current.FieldSecurity.SequenceEqual(next.FieldSecurity);
}
