using UnicoreCRM.Crm.Organizations.Application.Common;
using UnicoreCRM.Crm.Customers.Contracts;
namespace UnicoreCRM.Crm.Organizations.Application.ResolveCustomerSubject;
internal sealed class AcceptedQuoteBuyer(IOrganizationsPersistence persistence):IAcceptedQuoteOrganizationBuyer
{
    public async Task<bool> ExistsActiveAsync(string workspaceId,string organizationId,CancellationToken ct)
        =>await persistence.ReadOrganizationAsync(workspaceId,organizationId,ct) is { Status:"active" };
}
