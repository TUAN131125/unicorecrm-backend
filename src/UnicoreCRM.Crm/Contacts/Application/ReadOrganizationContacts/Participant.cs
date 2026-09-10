using UnicoreCRM.Crm.Contacts.Application.Common;
using UnicoreCRM.Crm.Contacts.Contracts;
using UnicoreCRM.Crm.Contacts.Domain;
using UnicoreCRM.Platform.Workspace.Contracts;
namespace UnicoreCRM.Crm.Contacts.Application.ReadOrganizationContacts;
internal sealed class Participant(ContactAuthorization authorization,IContactsPersistence persistence,TimeProvider timeProvider):IOrganizationContactReadParticipant
{
 public async Task<IReadOnlyList<string>> ReadVisibleActiveContactIdsAsync(TrustedWorkspaceContext trusted,string organizationId,string requestId,string correlationId,CancellationToken ct){var meta=new ContactRequestMetadata(requestId,correlationId);var access=await authorization.AuthorizeAsync(meta,ContactCapabilities.Read,ct);if(!access.IsSuccess||access.Value!.Trusted.WorkspaceId!=trusted.WorkspaceId)return [];var contacts=await persistence.ReadActiveOrganizationContactsAsync(trusted.WorkspaceId,organizationId,ct);var ids=new List<string>();foreach(var contact in contacts){if(await authorization.EnforceRecordAsync(access.Value,contact,"getOrganizationOverview",meta,ct) is null){ids.Add(contact.ContactId);persistence.AddReadAudit(new ContactReadAuditRecord("getOrganizationOverview",trusted.WorkspaceId,trusted.MemberId,contact.ContactId,requestId,correlationId,contact.Version,timeProvider.GetUtcNow()));}}if(ids.Count>0)await persistence.SaveChangesAsync(ct);return ids;}
}
