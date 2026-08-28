using System.Text.RegularExpressions;
using UnicoreCRM.Crm.Contacts.Application.Common;
using UnicoreCRM.Crm.Contacts.Contracts;
using UnicoreCRM.Crm.Contacts.Domain;

namespace UnicoreCRM.Crm.Contacts.Application.GetContact;

internal sealed record Query(string ContactId, ContactRequestMetadata Metadata);

internal sealed partial class Handler(
    ContactAuthorization authorization,
    IContactsPersistence persistence,
    TimeProvider timeProvider)
{
    internal async Task<ContactOperationResult<ContactDocument>> HandleAsync(
        Query query,
        CancellationToken cancellationToken)
    {
        var access = await authorization.AuthorizeAsync(query.Metadata, cancellationToken);
        if (!access.IsSuccess)
            return ContactOperationResult<ContactDocument>.Failure(access.Error!);
        if (!EntityIdPattern().IsMatch(query.ContactId))
            return ContactOperationResult<ContactDocument>.Failure(ContactErrors.NotFound());

        var contact = await persistence.ReadContactAsync(
            access.Value!.Trusted.WorkspaceId,
            query.ContactId,
            cancellationToken);
        if (contact is null)
            return ContactOperationResult<ContactDocument>.Failure(ContactErrors.NotFound());

        var denied = await authorization.EnforceRecordAsync(
            access.Value,
            contact,
            "getContact",
            query.Metadata,
            cancellationToken);
        if (denied is not null)
            return ContactOperationResult<ContactDocument>.Failure(denied);

        persistence.AddReadAudit(new ContactReadAuditRecord(
            "getContact",
            access.Value.Trusted.WorkspaceId,
            access.Value.Trusted.MemberId,
            contact.ContactId,
            query.Metadata.RequestId,
            query.Metadata.CorrelationId,
            contact.Version,
            timeProvider.GetUtcNow()));
        await persistence.SaveChangesAsync(cancellationToken);
        return ContactOperationResult<ContactDocument>.Success(
            ContactFieldSecurity.Project(ContactProjection.Document(contact), access.Value.Authorization));
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex EntityIdPattern();
}
