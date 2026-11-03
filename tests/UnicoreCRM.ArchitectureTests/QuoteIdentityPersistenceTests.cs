using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Sales.Quotes.Domain;
using UnicoreCRM.Sales.Quotes.Infrastructure.Persistence;
using Xunit;
namespace UnicoreCRM.ArchitectureTests;
public sealed class QuoteIdentityPersistenceTests
{
    [Theory][InlineData(false,false,false)][InlineData(false,false,true)][InlineData(false,true,false)][InlineData(false,true,true)]
    [InlineData(true,false,false)][InlineData(true,false,true)][InlineData(true,true,false)][InlineData(true,true,true)]
    public async Task Committed_identity_and_source_history_cannot_be_modified_or_deleted(bool source,bool delete,bool asynchronous)
    {
        foreach(var booleanOverload in new[]{false,true})
        {
            using var db=new QuotesDbContext(new DbContextOptionsBuilder<QuotesDbContext>().UseSqlServer("Server=127.0.0.1,1;Database=UnicoreCRM_Test;Integrated Security=True;Connect Timeout=1;Encrypt=False").Options);
            object row=source?new QuoteEvidenceSourceVersion("w","s",1,"CONTACT","buyer","DRAFT","actor","{}",DateTimeOffset.UtcNow)
                :new QuoteIdentityVersion("w","c","q",1,"BINDING_RECORDED","actor","initial","{}","v1",DateTimeOffset.UtcNow);
            db.Attach(row);if(delete)db.Remove(row);else db.Entry(row).Property("SnapshotJson").CurrentValue="{\"forged\":true}";
            if(asynchronous)await Assert.ThrowsAsync<InvalidOperationException>(()=>booleanOverload?db.SaveChangesAsync(false):db.SaveChangesAsync());
            else Assert.Throws<InvalidOperationException>(()=>booleanOverload?db.SaveChanges(false):db.SaveChanges());
        }
    }
}
