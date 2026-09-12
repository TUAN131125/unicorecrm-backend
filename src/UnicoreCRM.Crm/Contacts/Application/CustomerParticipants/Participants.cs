using UnicoreCRM.Crm.Contacts.Application.Common;
using UnicoreCRM.Crm.Contacts.Contracts;
using UnicoreCRM.Crm.Contacts.Domain;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Contacts.Application.CustomerParticipants;

internal sealed class SubjectParticipant(ContactAuthorization authorization, IContactsPersistence persistence, TimeProvider timeProvider)
    : IContactCustomerSubjectParticipant
{
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

    public async Task<ContactCustomerSubject?> ResolveAcceptedWorkflowAsync(TrustedWorkspaceContext trusted, string contactId,
        string workflowId, string recoveryExecutorId, CancellationToken cancellationToken)
    {
        if (!workflowId.StartsWith("conversion_",StringComparison.Ordinal) || recoveryExecutorId != "workflow-recovery") return null;
        var contact = await persistence.ReadContactAsync(trusted.WorkspaceId, contactId, cancellationToken);
        if (contact is null) return null;
        persistence.AddReadAudit(new ContactReadAuditRecord("recoverLeadCustomerConversion",trusted.WorkspaceId,
            trusted.MemberId,contact.ContactId,workflowId,recoveryExecutorId,contact.Version,timeProvider.GetUtcNow()));
        await persistence.SaveChangesAsync(cancellationToken);
        var projected = ContactProjection.Document(contact);
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
