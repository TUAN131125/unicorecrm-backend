using System.Text.RegularExpressions;
using UnicoreCRM.Crm.Contacts.Application.Common;
using UnicoreCRM.Crm.Contacts.Contracts;
using UnicoreCRM.Operations.Tasks.Contracts;
using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Crm.Contacts.Application.ValidateTaskReference;

internal sealed class ContactTaskReferenceAuthority(ContactAuthorization authorization, IContactsPersistence persistence)
    : IContactTaskReferenceAuthority
{
    private static readonly RecordAccessRepresentation Representation = RecordAccessRepresentation.Create(
        "contact.taskReference", ContactFieldSecurity.FieldKeys.Where(field => field != "id").ToArray());

    public async Task<ContactTaskReferenceStatus> ValidateAsync(string workspaceId, string contactId,
        string requestId, string correlationId, CancellationToken cancellationToken)
    {
        var metadata = new ContactRequestMetadata(requestId, correlationId);
        var access = await authorization.AuthorizeAsync(metadata, ContactCapabilities.Read, cancellationToken, Representation);
        if (!access.IsSuccess) return ContactTaskReferenceStatus.AccessDenied;
        if (access.Value!.Trusted.WorkspaceId != workspaceId
            || !Regex.IsMatch(contactId, "^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant))
            return ContactTaskReferenceStatus.NotFound;
        var contact = await persistence.ReadContactAsync(workspaceId, contactId, cancellationToken);
        if (contact is null || await authorization.EnforceRecordAsync(access.Value, contact,
            "validateTaskContactReference", metadata, cancellationToken) is not null)
            return ContactTaskReferenceStatus.NotFound;
        // Validation-point eligibility only; no cross-owner lifecycle lock is acquired.
        return contact.ArchivedAt is not null || contact.Status == "archived"
            ? ContactTaskReferenceStatus.Archived : ContactTaskReferenceStatus.Available;
    }
}
