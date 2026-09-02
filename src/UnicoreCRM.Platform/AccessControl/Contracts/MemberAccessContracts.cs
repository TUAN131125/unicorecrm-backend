using System.Text.Json.Serialization;

namespace UnicoreCRM.Platform.AccessControl.Contracts;

/// <summary>
/// Full replacement of the AccessControl-owned role assignments for one Workspace membership.
/// Team membership remains Workspace-owned, so the only currently admitted <c>teamIds</c> value is
/// the required empty array.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReplaceWorkspaceMemberAccessRequest(
    IReadOnlyList<string?>? RoleIds,
    IReadOnlyList<string?>? TeamIds);
