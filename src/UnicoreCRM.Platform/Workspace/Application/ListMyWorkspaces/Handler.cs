using UnicoreCRM.Platform.Workspace.Application.Common;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Platform.Workspace.Domain;

namespace UnicoreCRM.Platform.Workspace.Application.ListMyWorkspaces;

internal sealed class Handler(IWorkspacePersistence persistence, TimeProvider timeProvider)
{
    internal async Task<WorkspaceOperationResult<WorkspaceMembershipListResponse>> HandleAsync(
        Query query,
        CancellationToken cancellationToken)
    {
        var memberships = await persistence.ListMembershipsAsync(query.AccountId, query.MemberId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var response = new WorkspaceMembershipListResponse(
            memberships.Select(WorkspaceProjection.Membership).ToArray(),
            now);
        persistence.AddAccessRecord(new WorkspaceAccessRecord("listMyWorkspaces", query.AccountId, null, query.CorrelationId, now));
        await persistence.SaveChangesAsync(cancellationToken);
        return WorkspaceOperationResult<WorkspaceMembershipListResponse>.Success(response);
    }
}
