using UnicoreCRM.Platform.AccessControl.Domain;

namespace UnicoreCRM.Platform.AccessControl.Application.AccessDirectory;

internal sealed record AccessControlDirectoryState(
    long Revision,
    IReadOnlyList<AccessRole> Roles,
    IReadOnlyList<RoleCapability> Capabilities,
    IReadOnlyList<MembershipRoleAssignment> Assignments,
    IReadOnlyList<RoleDataScopePolicy> DataScopes,
    IReadOnlyList<RoleFieldSecurityPolicy> FieldSecurity);

internal interface IAccessDirectoryPersistence
{
    Task<AccessControlDirectoryState> ReadDirectoryAsync(
        string workspaceId,
        CancellationToken cancellationToken);

    Task AppendReadEvidenceAsync(
        AccessDirectoryReadEvidence evidence,
        CancellationToken cancellationToken);
}

internal static class AccessDirectoryWire
{
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
}
