using Microsoft.EntityFrameworkCore;
using UnicoreCRM.BuildingBlocks;
using UnicoreCRM.Sales.Quotes.Contracts;
namespace UnicoreCRM.Crm.Contacts.Infrastructure.Persistence;
internal sealed class QuoteAutomaticBuyerReader(ContactsDbContext db):IQuoteAutomaticBuyerReader
{
    public string BuyerType=>"CONTACT";
    public async Task<bool> IsEligibleAsync(OwnerTransaction tx,string workspaceId,string buyerId,CancellationToken ct)
    {
        db.Database.SetDbConnection(tx.Connection,false);await db.Database.UseTransactionAsync(tx.Transaction,ct);
        return await db.Contacts.AnyAsync(x=>x.WorkspaceId==workspaceId && x.ContactId==buyerId && x.Status!="archived" && x.ArchivedAt==null,ct);
    }
}
