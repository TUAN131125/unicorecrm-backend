using System.Text.Json;
using Mono.Cecil;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Xunit;
using static UnicoreCRM.ArchitectureTests.ArchitectureInventory;

namespace UnicoreCRM.ArchitectureTests;

public sealed class CheckerRegressionTests
{
    [Theory]
    [InlineData("byref")]
    [InlineData("array")]
    [InlineData("pointer")]
    public void Reference_wrappers_preserve_exact_approved_or_unapproved_contract_identity(string kind)
    {
        using var module = ModuleDefinition.CreateModule("ContractReferenceFixture", ModuleKind.Dll);
        TypeReference Wrapped(string name)
        {
            var element = new TypeReference("UnicoreCRM.Crm.Contacts.Contracts", name, module, module.TypeSystem.CoreLibrary);
            return kind switch
            {
                "byref" => new ByReferenceType(element),
                "array" => new ArrayType(element),
                _ => new PointerType(element)
            };
        }
        const string source = "UnicoreCRM.Crm.Deals.Application.CreateDeal";
        var approved = Assert.Single(Expand(Wrapped("IContactSummaryReader")));
        Assert.IsNotAssignableFrom<TypeSpecification>(approved);
        Assert.True(ContractAllowed(ContractEdge(source, Namespace(approved), approved.Name)));
        var unapproved = Assert.Single(Expand(Wrapped("IUnapprovedContract")));
        Assert.Equal("IUnapprovedContract", unapproved.Name);
        Assert.False(ContractAllowed(ContractEdge(source, Namespace(unapproved), unapproved.Name)));
    }

    [Fact]
    public void Generic_container_and_each_underlying_argument_remain_inspected()
    {
        using var module = ModuleDefinition.CreateModule("GenericContractFixture", ModuleKind.Dll);
        var generic = new GenericInstanceType(new TypeReference("UnicoreCRM.Unapproved.Contracts", "Container`1", module, module.TypeSystem.CoreLibrary));
        var argument = new TypeReference("UnicoreCRM.Crm.Contacts.Contracts", "IUnapprovedContract", module, module.TypeSystem.CoreLibrary);
        generic.GenericArguments.Add(new ArrayType(argument));
        var expanded = Expand(new ByReferenceType(generic)).ToArray();
        Assert.Equal(2, expanded.Length);
        Assert.Same(generic, expanded[0]);
        Assert.Same(argument, expanded[1]);
        Assert.False(ContractAllowed(ContractEdge("UnicoreCRM.Crm.Deals.Application", Namespace(expanded[0]), expanded[0].Name)));
        Assert.False(ContractAllowed(ContractEdge("UnicoreCRM.Crm.Deals.Application", Namespace(expanded[1]), expanded[1].Name)));
    }

    [Theory]
    [InlineData("UnicoreCRM.Workflows.Atomic.Application.QualifyLeadForOpportunity", "Workflows.Atomic.Application -> Leads.Contracts.ILeadOpportunityQualificationParticipant")]
    [InlineData("UnicoreCRM.Workflows.Durable.Application.ProvisionInitialWorkspace", "Workflows.Durable.Application -> Leads.Contracts.ILeadOpportunityQualificationParticipant")]
    [InlineData("UnicoreCRM.Crm.Deals.Application.CreateDeal", "Deals.Application -> Leads.Contracts.ILeadOpportunityQualificationParticipant")]
    public void Contract_edge_preserves_exact_owner_and_workflow_scope(string source, string expected) =>
        Assert.Equal(expected, ContractEdge(source, "UnicoreCRM.Crm.Leads.Contracts", "ILeadOpportunityQualificationParticipant"));

    [Fact]
    public void Approved_atomic_participant_edge_does_not_admit_durable_or_another_contract()
    {
        const string target = "UnicoreCRM.Crm.Leads.Contracts";
        const string participant = "ILeadOpportunityQualificationParticipant";
        Assert.True(ContractAllowed(ContractEdge("UnicoreCRM.Workflows.Atomic.Application.QualifyLeadForOpportunity", target, participant)));
        Assert.False(ContractAllowed(ContractEdge("UnicoreCRM.Workflows.Durable.Application.ProvisionInitialWorkspace", target, participant)));
        Assert.False(ContractAllowed(ContractEdge("UnicoreCRM.Workflows.Atomic.Application.QualifyLeadForOpportunity", target, "IUnapprovedParticipant")));
        Assert.False(ContractAllowed(ContractEdge("UnicoreCRM.Other.Atomic.Application", target, participant)));
    }

    [Fact]
    public void Workflow_contract_target_scope_is_not_collapsed_to_atomic_or_durable()
    {
        Assert.Equal("Deals.Application -> Workflows.Atomic.Contracts.IUnapprovedWorkflow",
            ContractEdge("UnicoreCRM.Crm.Deals.Application", "UnicoreCRM.Workflows.Atomic.Contracts", "IUnapprovedWorkflow"));
        Assert.Equal("Deals.Application -> Workflows.Durable.Contracts.IUnapprovedWorkflow",
            ContractEdge("UnicoreCRM.Crm.Deals.Application", "UnicoreCRM.Workflows.Durable.Contracts", "IUnapprovedWorkflow"));
        Assert.False(ContractAllowed(ContractEdge("UnicoreCRM.Crm.Deals.Application", "UnicoreCRM.Workflows.Atomic.Contracts", "IUnapprovedWorkflow")));
        Assert.Equal("UNCLASSIFIED", ContractEdge("UnicoreCRM.Unknown", "UnicoreCRM.Crm.Leads.Contracts", "IUnapprovedParticipant"));
    }

    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);
    private static JsonElement Operation(string status = "ADMITTED_IMPLEMENTED", string supersession = "ACTIVE_EXTENSION",
        string owner = "Owner", string id = "extensionOperation", string path = "/fixture") => Json(new
        {
            operationId = id, method = "POST", path, canonicalOwner = owner, finalStatus = status,
            supersessionStatus = supersession, backendAuthorityStatus = "ADMITTED", wireContractAuthority = "PROJECT_EXTENSION_FIXTURE",
            businessDecisionAuthority = "PROJECT_EXTENSION_FIXTURE", authentication = "REQUIRED", workspaceRequired = true,
            requiredCapability = "fixture.read", resourceScope = "RESOURCE", dataScope = "WORKSPACE", successStatus = "200", responseSchema = "FixtureResponse"
        });
    private static JsonElement Registry(params JsonElement[] operations) => Json(new { status = "CANONICAL", operations });
    private static JsonElement Extensions(string owner = "Owner", string status = "IMPLEMENTED", string id = "extensionOperation") => Json(new
    {
        status = "CANONICAL", extensions = new[] { new { authorityId = "PROJECT_EXTENSION_FIXTURE", canonicalOwner = owner, implementationStatus = status, operations = new[] { id } } }
    });

    [Theory]
    [InlineData("requestEmailVerification")]
    [InlineData("provisionInitialWorkspace")]
    [InlineData("receiveGenericSignedLeadWebhook")]
    [InlineData("requestAiAdvisory")]
    [InlineData("updateProductConfigurationType")]
    public void Actual_canonical_extensions_resolve_exact_current_contract(string id)
    {
        var actual = EffectiveOperations()[id];
        Assert.Equal("PRODUCTION_CONTRACT_READY", actual.GetProperty("status").GetString());
        Assert.False(string.IsNullOrWhiteSpace(actual.GetProperty("requiredCapability").GetString()));
        Assert.StartsWith("PROJECT_EXTENSION_", actual.GetProperty("wireContractAuthority").GetString());
    }

    [Fact]
    public void Superseded_blocked_record_uses_current_contract_but_other_blocked_stays_blocked()
    {
        var pinned = new[] { Json(new { operationId = "extensionOperation", method = "POST", path = "/fixture", status = "BLOCKED" }),
            Json(new { operationId = "stillBlocked", method = "POST", path = "/blocked", status = "BLOCKED" }) };
        var result = AuthorityResolver.Resolve(pinned, Registry(Operation()), Extensions());
        Assert.Equal("PRODUCTION_CONTRACT_READY", result["extensionOperation"].GetProperty("status").GetString());
        Assert.Equal("BLOCKED", result["stillBlocked"].GetProperty("status").GetString());
    }

    [Theory]
    [InlineData("BLOCKED", "ACTIVE_EXTENSION")]
    [InlineData("ADMITTED_IMPLEMENTED", "SUPERSEDED")]
    public void Invalid_or_obsolete_effective_authority_fails(string status, string supersession) =>
        Assert.Throws<InvalidDataException>(() => AuthorityResolver.Resolve([], Registry(Operation(status, supersession)), Extensions()));

    [Theory]
    [InlineData("OtherOwner", "IMPLEMENTED", "extensionOperation")]
    [InlineData("Owner", "PENDING", "extensionOperation")]
    [InlineData("Owner", "IMPLEMENTED", "wrongIdentity")]
    public void Wrong_owner_status_or_operation_is_not_an_extension_grant(string owner, string status, string id) =>
        Assert.Throws<InvalidDataException>(() => AuthorityResolver.Resolve([], Registry(Operation()), Extensions(owner, status, id)));

    [Fact]
    public void Missing_or_duplicate_extension_authority_fails()
    {
        Assert.Throws<InvalidDataException>(() => AuthorityResolver.Resolve([], Registry(Operation()), Json(new { status = "CANONICAL", extensions = Array.Empty<object>() })));
        Assert.Throws<InvalidDataException>(() => AuthorityResolver.Resolve([], Registry(Operation(), Operation()), Extensions()));
    }

    [Fact]
    public void Extension_cannot_change_pinned_route_identity()
    {
        var pinned = Json(new { operationId = "extensionOperation", method = "POST", path = "/original", status = "BLOCKED" });
        Assert.Throws<InvalidDataException>(() => AuthorityResolver.Resolve([pinned], Registry(Operation()), Extensions()));
    }

    [Fact]
    public async Task Wrong_operation_identifier_and_missing_extension_authentication_still_fail()
    {
        await using var app = WebApplication.CreateBuilder().Build();
        app.MapPost("/ai/advisories", () => Results.Ok()).WithName("requestAiAdvisory");
        app.MapPost("/unknown", () => Results.Ok()).WithName("wrongIdentity");
        var errors = EndpointMetadataTests.Validate(((IEndpointRouteBuilder)app).DataSources.SelectMany(s => s.Endpoints).OfType<RouteEndpoint>());
        Assert.Contains("Authentication metadata missing: requestAiAdvisory", errors);
        Assert.Contains("Trusted Workspace metadata missing: requestAiAdvisory", errors);
        Assert.Contains(errors, e => e.Contains("Unadmitted actual endpoint: /unknown", StringComparison.Ordinal));
    }

    [Fact]
    public void Current_Leads_path_corrects_one_registry_entry_only()
    {
        var owners = Manifest("persistence-ownership.json").GetProperty("owners").EnumerateArray().ToArray();
        var leads = owners.Single(o => o.GetProperty("owner").GetString() == "leads");
        var path = ResolveMigrationPath(leads.GetProperty("migrationRoot").GetString()!);
        Assert.True(Directory.Exists(path));
        Assert.NotEmpty(Directory.EnumerateFiles(path, "*.cs"));
        Assert.DoesNotContain("backend" + Path.DirectorySeparatorChar + "backend", path, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(owners, o => o.GetProperty("dbContext").ValueKind == JsonValueKind.String
            && o.GetProperty("dbContext").GetString() == "UnicoreCRM.Communications.Infrastructure.Persistence.CommunicationsDbContext");
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("src/../../outside")]
    [InlineData("C:/outside")]
    [InlineData("")]
    public void Escaping_or_invalid_migration_path_fails(string path) => Assert.Throws<InvalidDataException>(() => ResolveMigrationPath(path));

    [Fact]
    public void Missing_migration_directory_and_wrong_context_do_not_become_approved()
    {
        Assert.False(Directory.Exists(ResolveMigrationPath("src/NoSuchMigrationFixture/Migrations")));
        var owners = Manifest("persistence-ownership.json").GetProperty("owners").EnumerateArray();
        Assert.DoesNotContain(owners, o => o.GetProperty("dbContext").ValueKind == JsonValueKind.String && o.GetProperty("dbContext").GetString() == "UnapprovedDbContext");
    }
}
