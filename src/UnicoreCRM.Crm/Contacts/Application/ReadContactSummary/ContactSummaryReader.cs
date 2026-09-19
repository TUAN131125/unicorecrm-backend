using System.Text.RegularExpressions;
using UnicoreCRM.Crm.Contacts.Application.Common;
using UnicoreCRM.Crm.Contacts.Contracts;
using UnicoreCRM.Crm.Contacts.Domain;
using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Crm.Contacts.Application.ReadContactSummary;

internal sealed class ContactSummaryReader(ContactAuthorization authorization, IContactsPersistence persistence, TimeProvider clock) : IContactSummaryReader
{
    private static readonly RecordAccessRepresentation Representation = RecordAccessRepresentation.Create("contact.summary", "displayName", "status", "jobTitle", "relationshipLevel", "needSummary");
    public async Task<ContactSummaryReadResult> ReadAsync(string id, string requestId, string correlationId, CancellationToken ct)
    {
        var metadata = new ContactRequestMetadata(requestId, correlationId);
        var access = await authorization.AuthorizeAsync(metadata, ContactCapabilities.Read, ct, Representation);
        if (!access.IsSuccess) return new(access.Error!.Code == "WORKSPACE_MISMATCH" ? ContactSummaryReadStatus.WorkspaceMismatch : ContactSummaryReadStatus.AccessDenied);
        if (!Regex.IsMatch(id, "^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)) return new(ContactSummaryReadStatus.InvalidReference);
        var record = await persistence.ReadContactAsync(access.Value!.Trusted.WorkspaceId, id, ct);
        if (record is null || await authorization.EnforceRecordAsync(access.Value, record, "readContactSummary", metadata, ct) is not null) return new(ContactSummaryReadStatus.NotFound);
        var document = ContactFieldSecurity.Project(ContactProjection.Document(record), access.Value.Authorization);
        var policy = access.Value.Authorization;
        var projection = new ContactSummaryProjection(record.ContactId, policy.CanRead("displayName") ? document.DisplayName : null, policy.CanRead("status") ? document.Status : null, policy.CanRead("jobTitle") ? document.JobTitle : null, policy.CanRead("relationshipLevel") ? document.RelationshipLevel : null, policy.CanRead("needSummary") ? document.NeedSummary : null, record.Version);
        persistence.AddReadAudit(new ContactReadAuditRecord("readContactSummary", access.Value.Trusted.WorkspaceId, access.Value.Trusted.MemberId, record.ContactId, requestId, correlationId, record.Version, clock.GetUtcNow()));
        await persistence.SaveChangesAsync(ct);
        return new(ContactSummaryReadStatus.Succeeded, projection);
    }
}
