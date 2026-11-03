using UnicoreCRM.Platform.AccessControl.Application.ProvisionInitialWorkspaceAccess;
using Xunit;

namespace UnicoreCRM.ArchitectureTests;

public sealed class DirectCustomerOwnerCapabilitiesTests
{
    [Fact]
    public void New_workspace_owner_has_manual_creation_and_both_source_capabilities()
    {
        var capabilities = InitialWorkspaceAccessPolicy.Validated();
        foreach (var capability in new[] { "customers.create_direct", "customers.onboard_existing", "contacts.create", "organizations.create" })
            Assert.Contains(capability, capabilities);
        Assert.DoesNotContain("customers.create_system", capabilities);
    }

    [Fact]
    public void Only_the_exact_predecessor_set_is_recognized_not_an_arbitrary_custom_role()
    {
        var previous = InitialWorkspaceAccessPolicy.Validated()
            .Where(c => c is not "customers.create_direct" and not "customers.onboard_existing").ToArray();
        Assert.True(InitialWorkspaceAccessPolicy.IsKnownPreviousCapabilitySet(previous));
        Assert.False(InitialWorkspaceAccessPolicy.IsKnownPreviousCapabilitySet(previous.Where(c => c != "access.configure").ToArray()));
        Assert.False(InitialWorkspaceAccessPolicy.IsKnownPreviousCapabilitySet(["customers.view"]));
    }
}
