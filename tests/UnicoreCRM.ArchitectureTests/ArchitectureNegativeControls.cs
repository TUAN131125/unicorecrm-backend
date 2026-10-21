using Xunit;
using static UnicoreCRM.ArchitectureTests.ArchitectureInventory;

namespace UnicoreCRM.ArchitectureTests;

public sealed class ArchitectureNegativeControls
{
    [Theory]
    [InlineData("Domain", "Microsoft.EntityFrameworkCore.DbContext")]
    [InlineData("Domain", "Microsoft.AspNetCore.Http")]
    [InlineData("Domain", "UnicoreCRM.Crm.Contacts.Infrastructure.Persistence")]
    [InlineData("Application", "UnicoreCRM.Operations.Tasks.Infrastructure")]
    public void Forbidden_layer_dependencies_are_rejected(string layer, string target) => Assert.True(ForbiddenLayer(layer, target));

    [Fact]
    public void Unknown_owner_cannot_gain_an_implicit_mapping() => Assert.Null(Owner("UnicoreCRM.UnapprovedOwner.Application"));

    [Fact]
    public void Unapproved_contract_edge_is_rejected_by_the_same_allowlist_rule() =>
        Assert.False(ContractAllowed("Contacts.Application -> Tasks.Contracts.IUnapprovedArchitectureControl"));

    [Fact]
    public void Cycle_is_rejected_by_the_same_project_graph_rule() =>
        Assert.True(HasCycle(new Dictionary<string, string[]> { ["A"] = ["B"], ["B"] = ["A"] }));
}
