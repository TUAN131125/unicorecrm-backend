using UnicoreCRM.Platform.Workspace.Contracts;
namespace UnicoreCRM.Crm.Contacts.Contracts;
internal interface IOrganizationContactReadParticipant
{
    Task<IReadOnlyList<string>> ReadVisibleActiveContactIdsAsync(TrustedWorkspaceContext trusted, string organizationId,
        string requestId, string correlationId, CancellationToken cancellationToken);
}
