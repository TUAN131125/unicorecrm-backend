using Xunit;
using static UnicoreCRM.ArchitectureTests.ArchitectureInventory;

namespace UnicoreCRM.ArchitectureTests;

public sealed class ImplementationOwnerMappingTests
{
    [Theory]
    [InlineData("UnicoreCRM.Crm.Customers.Application.GetCustomer360", "customers")]
    [InlineData("UnicoreCRM.Crm.Organizations.Contracts", "organizations")]
    [InlineData("UnicoreCRM.Platform.IdentityAuth.Infrastructure.Persistence", "platform/identity-auth")]
    [InlineData("UnicoreCRM.Platform.Workspace.Application.GetWorkspaceBootstrap", "platform/workspace-context")]
    [InlineData("UnicoreCRM.Platform.Workspace.Infrastructure.Persistence", "platform/workspace-context")]
    [InlineData("UnicoreCRM.Workflows.Atomic.Application.QualifyLeadForOpportunity", "workflows")]
    [InlineData("UnicoreCRM.Workflows.Durable.Application.ProvisionInitialWorkspace", "workflows")]
    [InlineData("UnicoreCRM.AI.Context.Application", "ai")]
    [InlineData("UnicoreCRM.PlatformOperations.Inbox.Contracts", "platform-operations")]
    [InlineData("UnicoreCRM.PlatformOperations.Outbox.Contracts", "platform-operations")]
    public void Adopted_module_first_owner_scopes_have_explicit_bindings(string ns, string owner) =>
        Assert.Equal(owner, Owner(ns));

    [Theory]
    [InlineData("UnicoreCRM.Communications.Application")]
    [InlineData("UnicoreCRM.PlatformOperations.AiExecution.Infrastructure")]
    [InlineData("UnicoreCRM.Platform.Workspace.Application.Common")]
    [InlineData("UnicoreCRM.Platform.Workspace.Contracts")]
    [InlineData("UnicoreCRM.Platform.Workspace.Domain")]
    [InlineData("UnicoreCRM.Workflows.Unapproved.Application")]
    [InlineData("UnicoreCRM.PlatformOperations.InboxUnrelated.Contracts")]
    public void Unknown_or_mixed_business_owner_scopes_are_not_implicitly_assigned(string ns) => Assert.Null(Owner(ns));

    [Theory]
    [InlineData("UnicoreCRM.Crm.Customers.Infrastructure.Persistence.CustomersDbContext", "customers", "customers")]
    [InlineData("UnicoreCRM.Crm.Organizations.Infrastructure.Persistence.OrganizationsDbContext", "organizations", "organizations")]
    [InlineData("UnicoreCRM.Billing.Invoices.Infrastructure.Persistence.InvoicesDbContext", "invoices", "invoices")]
    [InlineData("UnicoreCRM.Billing.Payments.Infrastructure.Persistence.PaymentsDbContext", "payments", "payments")]
    [InlineData("UnicoreCRM.Sales.Orders.Infrastructure.Persistence.OrdersDbContext", "orders", "orders")]
    [InlineData("UnicoreCRM.Operations.Support.Infrastructure.Persistence.SupportDbContext", "support", "support")]
    [InlineData("UnicoreCRM.CommercialEvidence.CommercialEvidence.Infrastructure.Persistence.CommercialEvidenceDbContext", "commercial-evidence", "commercial_evidence")]
    [InlineData("UnicoreCRM.Platform.IdentityAuth.Infrastructure.Persistence.IdentityAuthDbContext", "platform/identity-auth", "iam")]
    [InlineData("UnicoreCRM.Platform.AccessControl.Infrastructure.Persistence.AccessControlDbContext", "platform/access-control", "access")]
    [InlineData("UnicoreCRM.Platform.Workspace.Infrastructure.Persistence.WorkspaceDbContext", "platform/workspace-context", "workspace")]
    [InlineData("UnicoreCRM.Integrations.Infrastructure.Persistence.IntegrationsDbContext", "platform/integrations", "integration")]
    [InlineData("UnicoreCRM.PlatformOperations.Inbox.Infrastructure.Persistence.InboxDbContext", "platform-operations", "ops")]
    [InlineData("UnicoreCRM.PlatformOperations.Outbox.IntegrationEventJournalDbContext", "platform-operations", "ops")]
    public void Existing_canonical_context_assignments_retain_exact_owner_schema_and_migration_evidence(string context, string owner, string schema)
    {
        var record = Assert.Single(Manifest("persistence-ownership.json").GetProperty("owners").EnumerateArray(),
            row => row.GetProperty("dbContext").ValueKind == System.Text.Json.JsonValueKind.String
                && row.GetProperty("dbContext").GetString() == context);
        Assert.Equal(owner, record.GetProperty("owner").GetString());
        Assert.Equal(schema, record.GetProperty("schema").GetString());
        Assert.Equal("IMPLEMENTED_OWNER_MAPPING", record.GetProperty("assignmentStatus").GetString());
        Assert.Contains(record.GetProperty("implementationMappingAuthority").EnumerateArray(),
            item => item.GetString() == "design-authority/canonical-design/authority/owner-context-map.json");
        Assert.NotEmpty(Directory.EnumerateFiles(ResolveMigrationPath(record.GetProperty("migrationRoot").GetString()!), "*.cs"));
    }
}
