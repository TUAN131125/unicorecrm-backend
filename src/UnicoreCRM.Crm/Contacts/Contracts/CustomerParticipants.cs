using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Contacts.Contracts;

internal sealed record ContactCustomerSubject(string ContactId, string DisplayName, string? Email, string? Phone, bool IsEligible);
internal interface IContactCustomerSubjectParticipant
{
    Task<ContactCustomerSubject?> ResolveVisibleAsync(TrustedWorkspaceContext trusted, string contactId,
        string requestId, string correlationId, CancellationToken cancellationToken);
}

internal sealed record CustomerStakeholderContact(string RelationshipId, string ContactId, string DisplayName,
    string Role, DateTimeOffset EffectiveFrom, DateTimeOffset? EffectiveTo);
internal interface ICustomerStakeholderReadParticipant
{
    Task<IReadOnlyList<CustomerStakeholderContact>> ReadVisibleAsync(TrustedWorkspaceContext trusted, string customerId,
        string requestId, string correlationId, CancellationToken cancellationToken);
}
