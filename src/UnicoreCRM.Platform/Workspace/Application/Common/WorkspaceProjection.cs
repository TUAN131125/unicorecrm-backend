using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Platform.Workspace.Application.Common;

internal static class WorkspaceProjection
{
    internal static WorkspaceMembershipSummary Membership(WorkspaceMembershipReadModel model) => new(
        model.MembershipId,
        model.WorkspaceId,
        model.WorkspaceKey,
        model.Name,
        model.Status,
        model.LogoText);
}
