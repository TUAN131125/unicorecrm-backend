using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using UnicoreCRM.Sales.Quotes.Application.Common;
using UnicoreCRM.Sales.Quotes.Domain;
using UnicoreCRM.Sales.Quotes.Infrastructure.Persistence;
using Xunit;
namespace UnicoreCRM.ArchitectureTests;

public sealed class QuoteProofScanPersistenceTests
{
    [Theory][InlineData(false,false)][InlineData(false,true)][InlineData(true,false)][InlineData(true,true)]
    public async Task Scan_results_cannot_be_updated_or_deleted_through_any_save_overload(bool delete,bool asynchronous)
    {
        var now=DateTimeOffset.UtcNow;var hash=new string('A',64);var job=new QuoteProofScanJob("w","c","p",hash,610,now);
        Assert.True(job.TryClaim("epoch","a","l",now,TimeSpan.FromSeconds(90),3,12));var attempt=new QuoteProofScanAttempt(job,"v1",hash,hash,now);
        foreach(var useBooleanOverload in new[]{false,true})
        {
            // No SQL call is expected: the owner guard must reject changes before the provider can connect.
            using var db=new QuotesDbContext(new DbContextOptionsBuilder<QuotesDbContext>().UseSqlServer("Server=127.0.0.1,1;Database=UnicoreCRM_Test;Integrated Security=True;Connect Timeout=1;Encrypt=False").Options);
            var result=new QuoteProofScanResult(attempt,"SCAN_FAILED","SCANNER_UNAVAILABLE","NOT_COMPLETED","NOT_COMPLETED","{}",now);
            db.Attach(result);if(delete)db.Remove(result);else db.Entry(result).Property(x=>x.ReasonCode).CurrentValue="FORGED_CLEAN";
            if(asynchronous)await Assert.ThrowsAsync<InvalidOperationException>(()=>useBooleanOverload?db.SaveChangesAsync(false):db.SaveChangesAsync());
            else Assert.Throws<InvalidOperationException>(()=>useBooleanOverload?db.SaveChanges(false):db.SaveChanges());
        }
    }
    [Fact]public void Malformed_scanner_configuration_disables_policy_instead_of_inventing_defaults()
    {
        var reader=new QuoteProofScanPolicyReader(new BrokenOptions());Assert.Null(reader.Read("w"));Assert.Empty(reader.EnabledWorkspaces());
    }
    private sealed class BrokenOptions:IOptionsMonitor<QuoteProofScanOptions>
    {
        public QuoteProofScanOptions CurrentValue=>throw new InvalidOperationException("Invalid scanner configuration");
        public QuoteProofScanOptions Get(string? name)=>CurrentValue;
        public IDisposable? OnChange(Action<QuoteProofScanOptions,string?> listener)=>null;
    }
}
