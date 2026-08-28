using UnicoreCRM.Crm.Contacts.Application.Common;
using UnicoreCRM.Crm.Contacts.Contracts;
using UnicoreCRM.Crm.Contacts.Domain;
using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Crm.Contacts.Application.ListContacts;

internal sealed record Query(ContactRequestMetadata Metadata);

internal sealed class Handler(
    ContactAuthorization authorization,
    IContactsPersistence persistence,
    TimeProvider timeProvider)
{
    internal async Task<ContactOperationResult<IReadOnlyList<ContactDocument>>> HandleAsync(
        Query query,
        CancellationToken cancellationToken)
    {
        var access = await authorization.AuthorizeAsync(query.Metadata, cancellationToken);
        if (!access.IsSuccess)
            return ContactOperationResult<IReadOnlyList<ContactDocument>>.Failure(access.Error!);

        IReadOnlyList<Contact> contacts;
        switch (access.Value!.Authorization.ScopeFilter)
        {
            case RecordAccessScopeFilter.Workspace:
                contacts = await persistence.ReadContactsAsync(
                    access.Value.Trusted.WorkspaceId,
                    null,
                    cancellationToken);
                break;
            case RecordAccessScopeFilter.OwnedByMember when
                !string.IsNullOrWhiteSpace(access.Value.Authorization.ScopeOwnerMemberId):
                contacts = await persistence.ReadContactsAsync(
                    access.Value.Trusted.WorkspaceId,
                    access.Value.Authorization.ScopeOwnerMemberId,
                    cancellationToken);
                break;
            default:
                contacts = [];
                break;
        }

        persistence.AddReadAudit(new ContactReadAuditRecord(
            "listContacts",
            access.Value.Trusted.WorkspaceId,
            access.Value.Trusted.MemberId,
            null,
            query.Metadata.RequestId,
            query.Metadata.CorrelationId,
            null,
            timeProvider.GetUtcNow()));
        await persistence.SaveChangesAsync(cancellationToken);
        return ContactOperationResult<IReadOnlyList<ContactDocument>>.Success(
            contacts
                .Select(contact => ContactFieldSecurity.Project(
                    ContactProjection.Document(contact),
                    access.Value.Authorization))
                .ToArray());
    }
}
