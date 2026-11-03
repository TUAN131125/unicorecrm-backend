using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Organizations.Contracts;

public sealed record OrganizationCustomerSubject(string OrganizationId, string DisplayName, string? Email, string? Phone, bool IsEligible, long Version);
public interface IOrganizationCustomerSubjectParticipant
{
    Task<OrganizationCustomerSubject?> ResolveVisibleAsync(TrustedWorkspaceContext trusted, string organizationId,
        string requestId, string correlationId, CancellationToken cancellationToken);
}

internal interface IOrganizationCustomerListIdentityParticipant
{
    async Task<IReadOnlySet<string>> MatchVisibleAsync(TrustedWorkspaceContext trusted, IReadOnlyCollection<string> ids,
        string search, string requestId, string correlationId, CancellationToken ct)
        => (await ResolveVisibleAsync(trusted, ids, requestId, correlationId, ct))
            .Where(x=>x.Value.Contains(search,StringComparison.OrdinalIgnoreCase)).Select(x=>x.Key).ToHashSet(StringComparer.Ordinal);

    Task<IReadOnlyDictionary<string, string>> ResolveVisibleAsync(TrustedWorkspaceContext trusted,
        IReadOnlyCollection<string> organizationIds, string requestId, string correlationId, CancellationToken cancellationToken);
}
