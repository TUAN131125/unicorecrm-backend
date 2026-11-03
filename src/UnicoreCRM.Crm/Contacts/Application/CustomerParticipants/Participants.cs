using UnicoreCRM.Crm.Contacts.Application.Common;
using UnicoreCRM.Crm.Contacts.Contracts;
using UnicoreCRM.Crm.Contacts.Domain;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Contacts.Application.CustomerParticipants;

internal sealed class SubjectParticipant(ContactAuthorization authorization, IContactsPersistence persistence, TimeProvider timeProvider)
    : IContactCustomerSubjectParticipant, IContactCustomerListIdentityParticipant
{
    public async Task<UnicoreCRM.Crm.Customers.Contracts.CustomerSourceSearchPlan?> SearchPlanAsync(TrustedWorkspaceContext trusted,
        string requestId, string correlationId, CancellationToken ct)
    {
        var grant = await authorization.AuthorizeAsync(new(requestId, correlationId), ContactCapabilities.Read, ct);
        if (!grant.IsSuccess || grant.Value!.Trusted.WorkspaceId != trusted.WorkspaceId) return null;
        var policy = grant.Value.Authorization;
        if (policy.ScopeFilter is not (UnicoreCRM.Platform.AccessControl.Contracts.RecordAccessScopeFilter.Workspace or UnicoreCRM.Platform.AccessControl.Contracts.RecordAccessScopeFilter.OwnedByMember)) return null;
        var owner = policy.ScopeFilter == UnicoreCRM.Platform.AccessControl.Contracts.RecordAccessScopeFilter.OwnedByMember ? policy.ScopeOwnerMemberId : null;
        if (policy.ScopeFilter == UnicoreCRM.Platform.AccessControl.Contracts.RecordAccessScopeFilter.OwnedByMember && owner is null) return null;
        return persistence.CustomerSearchPlan(trusted.WorkspaceId, owner, new[] { "fullName", "displayName", "personalEmail", "workEmail", "mobilePhone", "workPhone", "otherPhone" }.Where(policy.CanRead).ToHashSet(StringComparer.Ordinal));
    }
    public async Task<IReadOnlySet<string>> MatchVisibleAsync(TrustedWorkspaceContext trusted, IReadOnlyCollection<string> ids,
        string search, string requestId, string correlationId, CancellationToken ct)
    {
        if(ids.Count>250)throw new ArgumentOutOfRangeException(nameof(ids));
        var metadata=new ContactRequestMetadata(requestId,correlationId);
        var grant=await authorization.AuthorizeAsync(metadata, ContactCapabilities.Read, ct);
        var matches=new HashSet<string>(StringComparer.Ordinal);
        if(!grant.IsSuccess || grant.Value!.Trusted.WorkspaceId!=trusted.WorkspaceId)return matches;
        var records=await persistence.ReadContactsByIdsAsync(trusted.WorkspaceId,ids,ct);
        foreach(var row in records)
        {
            var projected=ContactProjection.Document(row);
            if(!((grant.Value.Authorization.CanRead("fullName") && projected.FullName?.Contains(search,StringComparison.OrdinalIgnoreCase)==true) || (grant.Value.Authorization.CanRead("displayName") && projected.DisplayName?.Contains(search,StringComparison.OrdinalIgnoreCase)==true) || (grant.Value.Authorization.CanRead("personalEmail") && projected.PersonalEmail?.Contains(search,StringComparison.OrdinalIgnoreCase)==true) || (grant.Value.Authorization.CanRead("workEmail") && projected.WorkEmail?.Contains(search,StringComparison.OrdinalIgnoreCase)==true) || (grant.Value.Authorization.CanRead("mobilePhone") && projected.MobilePhone?.Contains(search,StringComparison.OrdinalIgnoreCase)==true) || (grant.Value.Authorization.CanRead("workPhone") && projected.WorkPhone?.Contains(search,StringComparison.OrdinalIgnoreCase)==true)))continue;
            if(await authorization.EnforceRecordAsync(grant.Value,row,"searchCustomerIdentity",metadata,ct) is not null)continue;
            matches.Add(row.ContactId);
        }
        return matches;
    }


    public async Task<IReadOnlyDictionary<string, string>> ResolveVisibleAsync(TrustedWorkspaceContext trusted,
        IReadOnlyCollection<string> contactIds, string requestId, string correlationId, CancellationToken cancellationToken)
    {
        if (contactIds.Count > 250) throw new ArgumentOutOfRangeException(nameof(contactIds));
        var visible = new Dictionary<string, string>(StringComparer.Ordinal);
        if (contactIds.Count == 0) return visible;
        var metadata = new ContactRequestMetadata(requestId, correlationId);
        var access = await authorization.AuthorizeAsync(metadata, ContactCapabilities.Read, cancellationToken);
        if (!access.IsSuccess || access.Value!.Trusted.WorkspaceId != trusted.WorkspaceId
            || !access.Value.Authorization.CanRead("fullName")) return visible;
        var records = await persistence.ReadContactsByIdsAsync(trusted.WorkspaceId, contactIds, cancellationToken);
        foreach (var record in records)
        {
            if (await authorization.EnforceRecordAsync(access.Value, record, "listCustomerIdentities", metadata, cancellationToken) is not null) continue;
            var projected = ContactFieldSecurity.Project(ContactProjection.Document(record), access.Value.Authorization);
            visible[record.ContactId] = projected.FullName;
            persistence.AddReadAudit(new ContactReadAuditRecord("listCustomerIdentities", trusted.WorkspaceId,
                trusted.MemberId, record.ContactId, requestId, correlationId, record.Version, timeProvider.GetUtcNow()));
        }
        if (visible.Count > 0) await persistence.SaveChangesAsync(cancellationToken);
        return visible;
    }

    public async Task<ContactCustomerSubject?> ResolveVisibleAsync(TrustedWorkspaceContext trusted, string contactId,
        string requestId, string correlationId, CancellationToken cancellationToken)
    {
        var metadata = new ContactRequestMetadata(requestId, correlationId);
        var access = await authorization.AuthorizeAsync(metadata, ContactCapabilities.Read, cancellationToken);
        if (!access.IsSuccess || access.Value!.Trusted.WorkspaceId != trusted.WorkspaceId) return null;
        var contact = await persistence.ReadContactAsync(trusted.WorkspaceId, contactId, cancellationToken);
        if (contact is null || await authorization.EnforceRecordAsync(access.Value, contact, "resolveCustomerAccountSubject", metadata, cancellationToken) is not null) return null;
        persistence.AddReadAudit(new ContactReadAuditRecord("resolveCustomerAccountSubject", trusted.WorkspaceId,
            trusted.MemberId, contact.ContactId, requestId, correlationId, contact.Version, timeProvider.GetUtcNow()));
        await persistence.SaveChangesAsync(cancellationToken);
        var projected = ContactFieldSecurity.Project(ContactProjection.Document(contact), access.Value.Authorization);
        return new(projected.Id, projected.FullName, projected.PersonalEmail ?? projected.WorkEmail,
            projected.MobilePhone ?? projected.WorkPhone, contact.ArchivedAt is null, contact.Version);
    }

}

internal sealed class StakeholderParticipant(ContactAuthorization authorization, IContactsPersistence persistence, TimeProvider timeProvider)
    : ICustomerStakeholderReadParticipant
{
    public async Task<IReadOnlyList<CustomerStakeholderContact>> ReadVisibleAsync(TrustedWorkspaceContext trusted, string customerId,
        string requestId, string correlationId, CancellationToken cancellationToken)
    {
        var metadata = new ContactRequestMetadata(requestId, correlationId);
        var access = await authorization.AuthorizeAsync(metadata, ContactCapabilities.Read, cancellationToken);
        if (!access.IsSuccess || access.Value!.Trusted.WorkspaceId != trusted.WorkspaceId) return [];
        var relationships = await persistence.ReadCustomerStakeholderRelationshipsAsync(trusted.WorkspaceId, customerId, cancellationToken);
        var result = new List<CustomerStakeholderContact>();
        foreach (var relationship in relationships)
        {
            var contact = await persistence.ReadContactAsync(trusted.WorkspaceId, relationship.ContactId, cancellationToken);
            if (contact is null || await authorization.EnforceRecordAsync(access.Value, contact, "getCustomer360", metadata, cancellationToken) is not null) continue;
            result.Add(new(relationship.RelationshipId, contact.ContactId, contact.FullName, relationship.Role,
                relationship.EffectiveFrom, relationship.EffectiveTo));
            persistence.AddReadAudit(new ContactReadAuditRecord("getCustomer360", trusted.WorkspaceId, trusted.MemberId,
                contact.ContactId, requestId, correlationId, contact.Version, timeProvider.GetUtcNow()));
        }
        if (result.Count > 0) await persistence.SaveChangesAsync(cancellationToken);
        return result;
    }
}

internal sealed class LeadConversionStakeholderParticipant(Application.Relationships.Handler relationships)
    : ILeadCustomerStakeholderParticipant
{
    public async Task<ResolveLeadConversionStakeholderResult> ResolveAsync(ResolveLeadConversionStakeholderCommand command, CancellationToken cancellationToken)
    {
        var result=await relationships.CreateCustomerAsync(new(command.ContactId,
            new CreateContactCustomerRelationshipRequest(command.CustomerId,command.Role),
            new ContactCommandMetadata(command.RequestId,command.CorrelationId,command.ParticipantKey,command.ExpectedContactVersion)),cancellationToken);
        if(!result.IsSuccess)return new(false,null,false,result.Error!.Code,result.Error.Status);
        return new(true,result.Value!.Result.CustomerRelationship?.RelationshipId,result.Value.Outcome=="REPLAYED");
    }
}
