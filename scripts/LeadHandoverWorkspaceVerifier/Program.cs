using System.Reflection;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UnicoreCRM.Platform;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Platform.Workspace.Infrastructure.Persistence.Migrations;

if (args.Length > 1) throw new ArgumentException("Pass at most one SQL Server connection string; a fresh isolated database is created automatically.");
var passed = 0;
void Check(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException("FAIL " + label);
    passed++;
    Console.WriteLine("PASS " + label);
}

var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
var workflow = new WorkspaceWorkflowDocument("OPTIONAL", "QUOTE", null, null, null, "COMPANY", "SUBSCRIPTION", "ENTERPRISE_SALES", "B2B_SALES");
Check(workflow.HandoverAcceptanceSlaHours == 24, "constructor default 24");
var legacyJson = JsonSerializer.Serialize(workflow, options).Replace(",\"handoverAcceptanceSlaHours\":24", "", StringComparison.Ordinal);
Check(JsonSerializer.Deserialize<WorkspaceWorkflowDocument>(legacyJson, options)!.HandoverAcceptanceSlaHours == 24, "legacy JSON default 24");
Check(JsonDocument.Parse(JsonSerializer.Serialize(workflow, options)).RootElement.GetProperty("handoverAcceptanceSlaHours").GetInt32() == 24, "canonical numeric JSON property");
var assembly = typeof(WorkspaceWorkflowDocument).Assembly;
var validation = assembly.GetType("UnicoreCRM.Platform.Workspace.Application.Common.StudioValidation", true)!
    .GetMethod("Blueprint", BindingFlags.Static | BindingFlags.NonPublic)!;
foreach (var hours in new[] { -1, 0, 1, 24, 72, 168, 169 })
{
    var request = new UpdateWorkspaceBlueprintRequest(new("B2B", workflow with { HandoverAcceptanceSlaHours = hours }),
        new(true, true, true, true, true, true, true, true, true, true, true, true, true));
    var errors = (IReadOnlyDictionary<string, string[]>)validation.Invoke(null, [request])!;
    Check(errors.ContainsKey("blueprint.workflow.handoverAcceptanceSlaHours") == (hours < 1 || hours > 168), "validation " + hours);
}
var migration = new LeadHandoverAcceptanceSla();
Check(migration.UpOperations.Count == 1 && migration.UpOperations[0] is SqlOperation sql
    && sql.Sql.Contains("OPENJSON", StringComparison.Ordinal) && sql.Sql.Contains("NOT EXISTS", StringComparison.Ordinal)
    && sql.Sql.Contains("JSON_MODIFY", StringComparison.Ordinal), "missing-key-only SQL backfill");
Check(migration.DownOperations.Count == 0, "rollback preserves configured SLA");
Check(typeof(ILeadHandoverPolicyReader).GetMethods().Single().ReturnType == typeof(Task<LeadHandoverPolicy>), "narrow reader contract");

// Frozen from backend 1106272021ea0f733c1f610c177e85e907142cac, independently of the current projection.
var oldOwner = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "PreHandoverOwnerCapabilities.txt"))
    .Order(StringComparer.Ordinal).ToArray();
var accessPolicy = assembly.GetType("UnicoreCRM.Platform.AccessControl.Application.ProvisionInitialWorkspaceAccess.InitialWorkspaceAccessPolicy", true)!;
var knownPrevious = accessPolicy.GetMethod("IsKnownPreviousCapabilitySet", BindingFlags.Static | BindingFlags.NonPublic)!;
bool IsKnown(string[] capabilities) => (bool)knownPrevious.Invoke(null, [capabilities])!;
Check(IsKnown(oldOwner), "exact sorted frozen pre-Handover owner admitted for upgrade");
// Historical sets come from the frozen baseline fixture, never from the runtime predecessor properties.
var preClaimOwner = oldOwner.Where(capability => capability != "leads.claim").ToArray();
var preQueueOwner = preClaimOwner.Where(capability => capability != "leads.queue.read").ToArray();
Check(IsKnown(preClaimOwner), "exact sorted frozen pre-Claim owner admitted for upgrade");
Check(IsKnown(preQueueOwner), "exact sorted frozen pre-Queue owner admitted for upgrade");
Check(!IsKnown(preClaimOwner.Where(capability => capability != "tasks.create").ToArray()),
    "custom pre-Claim subset cannot receive owner upgrade");
Check(!IsKnown(preQueueOwner.Where(capability => capability != "tasks.create").ToArray()),
    "custom pre-Queue subset cannot receive owner upgrade");
var currentOwner = (IReadOnlyList<string>)accessPolicy.GetMethod("Validated", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!;
Check(currentOwner.Except(oldOwner, StringComparer.Ordinal).SequenceEqual(["leads.handover"])
    && !oldOwner.Except(currentOwner, StringComparer.Ordinal).Any(), "frozen owner upgrade adds only leads.handover");
Check(!IsKnown(oldOwner.Where(capability => capability != "tasks.create").ToArray()), "arbitrary custom subset cannot receive owner upgrade");
Check(!IsKnown(["leads.assign"]), "assign capability alone cannot receive handover grant");
Check(!IsKnown(oldOwner.Append("leads.handover.recover").Order(StringComparer.Ordinal).ToArray()), "unexpected capability cannot receive owner upgrade");
var predecessors = accessPolicy.GetProperties(BindingFlags.Static | BindingFlags.NonPublic)
    .Where(property => property.Name.StartsWith("Pre", StringComparison.Ordinal) && property.PropertyType == typeof(IReadOnlyList<string>))
    .ToArray();
Check(predecessors.All(property => !((IReadOnlyList<string>)property.GetValue(null)!).Contains("leads.handover", StringComparer.Ordinal)),
    "historical predecessor chain excludes leads.handover");

var connection = new SqlConnectionStringBuilder(args.Length == 1 ? args[0]
    : "Server=(localdb)\\MSSQLLocalDB;Integrated Security=True;TrustServerCertificate=True")
{
    InitialCatalog = "UnicoreCRM_HandoverWorkspaceVerifier_" + Guid.NewGuid().ToString("N")
};
var database = connection.InitialCatalog;
var master = new SqlConnectionStringBuilder(connection.ConnectionString) { InitialCatalog = "master" };
await using var admin = new SqlConnection(master.ConnectionString);
await admin.OpenAsync();
await using (var create = new SqlCommand($"CREATE DATABASE [{database}]", admin)) await create.ExecuteNonQueryAsync();
try
{
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddPlatformModule(new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["ConnectionStrings:UnicoreCRM"] = connection.ConnectionString }).Build());
    await using var provider = services.BuildServiceProvider();
    await using var scope = provider.CreateAsyncScope();
    var contextType = assembly.GetType("UnicoreCRM.Platform.Workspace.Infrastructure.Persistence.WorkspaceDbContext", true)!;
    var db = (DbContext)scope.ServiceProvider.GetRequiredService(contextType);
    var migrator = db.GetService<IMigrator>();
    await migrator.MigrateAsync("20260906134920_StudioRuntime");

    var legacyBlueprint = "{\"businessModel\":\"B2B\",\"workflow\":" + legacyJson + "}";
    var customBlueprint = JsonSerializer.Serialize(new WorkspaceBlueprintDocument("B2B", workflow with { HandoverAcceptanceSlaHours = 72 }), options);
    foreach (var (id, json) in new[] { ("ws_legacy", legacyBlueprint), ("ws_custom", customBlueprint) })
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO [workspace].[Workspaces] ([WorkspaceId], [Key], [Name], [LogoText], [CreatedAt]) VALUES ({id}, {id}, {id}, N'WS', {DateTimeOffset.UtcNow})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO [workspace].[StudioConfigurations] ([WorkspaceId], [Revision], [PublicationStatus], [BusinessInformationJson], [AddressesJson], [LocaleRegionJson], [BlueprintJson], [FeaturesJson], [UpdatedAt]) VALUES ({id}, 1, N'DRAFT', N'{{}}', N'[]', N'{{}}', {json}, N'{{}}', {DateTimeOffset.UtcNow})");
    }
    await migrator.MigrateAsync("20261001090000_LeadHandoverAcceptanceSla");
    var reader = scope.ServiceProvider.GetRequiredService<ILeadHandoverPolicyReader>();
    Check((await reader.FindAsync("ws_legacy", default))?.AcceptanceSlaHours == 24, "reader returns migrated legacy SLA");
    Check((await reader.FindAsync("ws_custom", default))?.AcceptanceSlaHours == 72, "reader preserves custom SLA and workspace isolation");
    Check(await reader.FindAsync("ws_missing", default) is null, "reader missing workspace returns null");
    var stored = await db.Database.SqlQuery<string>($"SELECT [BlueprintJson] AS [Value] FROM [workspace].[StudioConfigurations] WHERE [WorkspaceId] = N'ws_legacy'").SingleAsync();
    Check(JsonDocument.Parse(stored).RootElement.GetProperty("workflow").GetProperty("handoverAcceptanceSlaHours").GetInt32() == 24,
        "backfill persists numeric 24 in legacy JSON");
    await db.Database.ExecuteSqlRawAsync(((SqlOperation)migration.UpOperations.Single()).Sql);
    Check((await reader.FindAsync("ws_custom", default))?.AcceptanceSlaHours == 72, "backfill repeat preserves custom SLA");
    await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [workspace].[StudioConfigurations] SET [BlueprintJson] = {customBlueprint.Replace(":72", ":168", StringComparison.Ordinal)} WHERE [WorkspaceId] = N'ws_custom'");
    Check((await reader.FindAsync("ws_custom", default))?.AcceptanceSlaHours == 168, "reader observes current effective policy");
    await db.Database.ExecuteSqlRawAsync("UPDATE [workspace].[StudioConfigurations] SET [BlueprintJson] = JSON_MODIFY([BlueprintJson], '$.workflow.handoverAcceptanceSlaHours', 0) WHERE [WorkspaceId] = N'ws_custom'");
    var rejected = false;
    try { await reader.FindAsync("ws_custom", default); }
    catch (InvalidOperationException exception) when (exception.Message == "Stored workspace handover acceptance SLA is invalid.") { rejected = true; }
    Check(rejected, "reader rejects invalid persisted SLA");
    Check(!db.ChangeTracker.Entries().Any(), "reader does not track configuration entities");
    Console.WriteLine($"Lead Handover Workspace verification: PASS={passed} FAIL=0");
}
finally
{
    SqlConnection.ClearAllPools();
    await using var drop = new SqlCommand($"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]", admin);
    await drop.ExecuteNonQueryAsync();
}
