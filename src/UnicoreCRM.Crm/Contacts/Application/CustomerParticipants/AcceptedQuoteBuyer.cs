using UnicoreCRM.Crm.Contacts.Application.Common;
using UnicoreCRM.Crm.Customers.Contracts;
namespace UnicoreCRM.Crm.Contacts.Application.CustomerParticipants;
internal sealed class AcceptedQuoteBuyer(IContactsPersistence persistence):IAcceptedQuoteContactBuyer
{
    public async Task<bool> ExistsActiveAsync(string workspaceId,string contactId,CancellationToken ct)
        =>await persistence.ReadContactAsync(workspaceId,contactId,ct) is { ArchivedAt:null, Status:not "archived" };
}
