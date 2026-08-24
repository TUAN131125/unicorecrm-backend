using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Platform.Workspace.Domain;

namespace UnicoreCRM.Platform.Workspace.Application.Common;

internal interface IWorkspacePersistence
{
    Task<IReadOnlyList<WorkspaceMembershipReadModel>> ListMembershipsAsync(string accountId, string memberId, CancellationToken cancellationToken);
    Task<WorkspaceBootstrapReadModel?> FindActiveBootstrapAsync(string accountId, string memberId, string workspaceId, CancellationToken cancellationToken);
    Task<TrustedWorkspaceContext?> ResolveActiveAsync(string accountId, string memberId, string workspaceId, CancellationToken cancellationToken);
    void AddAccessRecord(WorkspaceAccessRecord record);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

internal sealed record WorkspaceMembershipReadModel(
    string MembershipId,
    string WorkspaceId,
    string WorkspaceKey,
    string Name,
    string Status,
    string LogoText);

internal sealed record WorkspaceBootstrapReadModel(
    WorkspaceMembershipReadModel Workspace,
    long ContextVersion,
    long ConfigurationVersion,
    string Locale,
    string TimeZone,
    string BaseCurrency,
    string CapabilitiesJson,
    string EnabledModuleKeysJson,
    string AvailableProductSpacesJson);
