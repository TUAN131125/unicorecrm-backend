using Xunit;
using static UnicoreCRM.ArchitectureTests.ArchitectureInventory;

namespace UnicoreCRM.ArchitectureTests;

public sealed class QuoteCoreArchitectureTests
{
    [Fact]
    public void Quote_application_consumes_product_contracts_and_never_foreign_persistence()
    {
        var found = false;
        // Types owns each Cecil module lifetime while yielding. Consume method bodies inside
        // that iteration; materializing TypeDefinitions would outlive their PE streams.
        foreach (var type in Types().Where(type => Under(Namespace(type), "UnicoreCRM.Sales.Quotes.Application")))
        {
            found = true;
            foreach (var reference in References(type).SelectMany(Expand))
            {
            var target = Namespace(reference);
            Assert.False(target.StartsWith("UnicoreCRM.Crm.", StringComparison.Ordinal), $"{type.FullName} -> {reference.FullName}");
            if (target.StartsWith("UnicoreCRM.Sales.Products.", StringComparison.Ordinal))
                Assert.True(Under(target, "UnicoreCRM.Sales.Products.Contracts"), $"{type.FullName} -> {reference.FullName}");
            Assert.False(target.Contains(".Infrastructure", StringComparison.Ordinal), $"{type.FullName} -> {reference.FullName}");
            Assert.False(target.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal), $"{type.FullName} -> {reference.FullName}");
            }
        }
        Assert.True(found);
    }

    [Fact]
    public void Crm_quote_source_participant_consumes_only_sales_quote_contracts()
    {
        var found = false;
        foreach (var type in Types().Where(type => Under(Namespace(type), "UnicoreCRM.Crm.Deals.Application.VerifyQuoteSource")))
        {
            found = true;
            foreach (var reference in References(type).SelectMany(Expand)
                     .Where(reference => Namespace(reference).StartsWith("UnicoreCRM.Sales.", StringComparison.Ordinal)))
            Assert.True(Under(Namespace(reference), "UnicoreCRM.Sales.Quotes.Contracts"), reference.FullName);
        }
        Assert.True(found);
    }

    [Fact]
    public void Quotes_existing_schema_and_migration_owner_are_explicit()
    {
        var row = Manifest("persistence-ownership.json").GetProperty("owners").EnumerateArray().Single(x => x.GetProperty("owner").GetString() == "quotes");
        Assert.Equal("quotes", row.GetProperty("schema").GetString());
        Assert.Equal("UnicoreCRM.Sales.Quotes.Infrastructure.Persistence.QuotesDbContext", row.GetProperty("dbContext").GetString());
        Assert.Equal("IMPLEMENTED_OWNER_MAPPING", row.GetProperty("assignmentStatus").GetString());
        var path = ResolveMigrationPath(row.GetProperty("migrationRoot").GetString()!);
        Assert.True(Directory.EnumerateFiles(path, "*_QuoteDraftCore.cs").Any());
    }
}
