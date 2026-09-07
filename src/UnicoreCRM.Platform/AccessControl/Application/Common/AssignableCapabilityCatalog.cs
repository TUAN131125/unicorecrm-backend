namespace UnicoreCRM.Platform.AccessControl.Application.Common;

/// <summary>
/// Immutable runtime projection of capability-authorization-matrix.json filtered by the frozen
/// custom-Workspace-role assignability rule at the active Design Authority baseline. The rule is
/// shared by createAccessRole and replaceAccessRole, so a replacement can never assign a capability
/// a fresh create would reject.
/// </summary>
internal static class AssignableCapabilityCatalog
{
    private static HashSet<string> Values { get; } = new(
        WorkspaceCapabilityPolicy.CustomRoleAssignableCapabilities,
        StringComparer.Ordinal);

    internal static bool Contains(string capability) => Values.Contains(capability);
}
