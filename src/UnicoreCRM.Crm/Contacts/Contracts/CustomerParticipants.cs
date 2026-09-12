using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Contacts.Contracts;

public sealed record ContactCustomerSubject(string ContactId, string DisplayName, string? Email, string? Phone, bool IsEligible, long Version);
public interface IContactCustomerSubjectParticipant
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

public sealed record ResolveLeadConversionStakeholderCommand(string ContactId, long ExpectedContactVersion, string CustomerId,
    string Role, string ParticipantKey, string RequestId, string CorrelationId);
public sealed record ResolveLeadConversionStakeholderResult(bool IsSuccess, string? RelationshipId, bool Replayed,
    string? ErrorCode = null, int? ErrorStatus = null);
public interface ILeadCustomerStakeholderParticipant
{
    Task<ResolveLeadConversionStakeholderResult> ResolveAsync(ResolveLeadConversionStakeholderCommand command, CancellationToken cancellationToken);
}
