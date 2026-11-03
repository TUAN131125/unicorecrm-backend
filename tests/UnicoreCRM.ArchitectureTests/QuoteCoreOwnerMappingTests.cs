using Xunit;
using static UnicoreCRM.ArchitectureTests.ArchitectureInventory;

namespace UnicoreCRM.ArchitectureTests;

public sealed class QuoteCoreOwnerMappingTests
{
    [Theory]
    [InlineData("UnicoreCRM.Crm.Deals.Application.VerifyQuoteSource", "deals")]
    [InlineData("UnicoreCRM.Sales.Products.Application.ReadQuoteProductSnapshots", "products")]
    [InlineData("UnicoreCRM.Platform.AccessControl.Application.CheckQuoteApprovalAvailability", "platform/access-control")]
    public void Quote_core_owner_participants_and_owned_siblings_have_explicit_mappings(string prefix, string expectedOwner)
    {
        Assert.Equal(expectedOwner, Owner(prefix));
        Assert.Equal(expectedOwner, Owner(prefix + ".Nested"));
        Assert.Equal(expectedOwner, Owner(prefix + "Unrelated"));
        Assert.Null(Owner("UnicoreCRM.UnknownOwner.Application." + prefix.Split('.').Last()));
    }

    [Theory]
    [InlineData("UnicoreCRM.Crm.DealsUnrelated.Application.VerifyQuoteSource")]
    [InlineData("UnicoreCRM.Sales.ProductsUnrelated.Application.ReadQuoteProductSnapshots")]
    [InlineData("UnicoreCRM.Platform.AccessControlUnrelated.Application.CheckQuoteApprovalAvailability")]
    public void Similar_names_outside_the_approved_owner_namespace_remain_unmapped(string ns) => Assert.Null(Owner(ns));
}
