using Microsoft.EntityFrameworkCore;
using UnicoreCRM.BuildingBlocks;
using UnicoreCRM.Sales.Quotes.Contracts;
namespace UnicoreCRM.Crm.Organizations.Infrastructure.Persistence;
internal sealed class QuoteAutomaticBuyerReader(OrganizationsDbContext db):IQuoteAutomaticBuyerReader
{
    public string BuyerType=>"ORGANIZATION_ACCOUNT";
    public async Task<bool> IsEligibleAsync(OwnerTransaction tx,string workspaceId,string buyerId,CancellationToken ct)
    {
        db.Database.SetDbConnection(tx.Connection,false);await db.Database.UseTransactionAsync(tx.Transaction,ct);
        return await db.Organizations.AnyAsync(x=>x.WorkspaceId==workspaceId && x.OrganizationId==buyerId && x.Status=="active",ct);
    }
}
