using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Xunit;
using static UnicoreCRM.ArchitectureTests.ArchitectureInventory;

namespace UnicoreCRM.ArchitectureTests;

public sealed class CheckerRegressionTests
{
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
