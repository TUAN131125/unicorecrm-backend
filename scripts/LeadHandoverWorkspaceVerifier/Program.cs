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
using UnicoreCRM.Platform.AccessControl.Infrastructure.Persistence.Migrations;

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
Check(currentOwner.SequenceEqual(oldOwner, StringComparer.Ordinal), "current owner equals frozen pre-Handover owner without new human authority");
Check(!currentOwner.Contains("leads.handover", StringComparer.Ordinal), "human handover capability absent from owner catalog");
var assignableCatalog = assembly.GetType("UnicoreCRM.Platform.AccessControl.Application.Common.AssignableCapabilityCatalog", true)!
    .GetMethod("Contains", BindingFlags.Static | BindingFlags.NonPublic)!;
Check(!(bool)assignableCatalog.Invoke(null, ["leads.handover"])!, "human handover capability not assignable to custom roles");
Check(new[] { "leads.assign", "tasks.assign", "tasks.create" }.All(capability =>
    (bool)assignableCatalog.Invoke(null, [capability])!), "canonical human admission capabilities remain assignable");
Check(!IsKnown(oldOwner.Where(capability => capability != "tasks.create").ToArray()), "arbitrary custom subset cannot receive owner upgrade");
Check(!IsKnown(["leads.assign"]), "assign capability alone cannot receive owner upgrade");
Check(!IsKnown(oldOwner.Append("leads.handover").Order(StringComparer.Ordinal).ToArray()), "obsolete human capability cannot receive additive owner upgrade");
Check(!IsKnown(oldOwner.Append("leads.handover.recover").Order(StringComparer.Ordinal).ToArray()), "unexpected capability cannot receive owner upgrade");
var predecessors = accessPolicy.GetProperties(BindingFlags.Static | BindingFlags.NonPublic)
    .Where(property => property.Name.StartsWith("Pre", StringComparison.Ordinal) && property.PropertyType == typeof(IReadOnlyList<string>))
    .ToArray();
Check(predecessors.All(property => !((IReadOnlyList<string>)property.GetValue(null)!).Contains("leads.handover", StringComparer.Ordinal)),
    "historical predecessor chain excludes leads.handover");
var removal = new RemoveHumanLeadHandoverCapability();
Check(removal.UpOperations.Single() is SqlOperation removalSql
    && removalSql.Sql.Contains("[access].[RoleCapabilities]", StringComparison.Ordinal)
    && removalSql.Sql.Contains("N'leads.handover'", StringComparison.Ordinal)
    && removalSql.Sql.Contains("[WorkspaceDirectoryRevisions]", StringComparison.Ordinal), "additive migration removes stored human grants and invalidates directory revision");
Check(removal.DownOperations.Count == 0, "rollback does not restore obsolete human authority");

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

    var accessContextType = assembly.GetType("UnicoreCRM.Platform.AccessControl.Infrastructure.Persistence.AccessControlDbContext", true)!;
    var accessDb = (DbContext)scope.ServiceProvider.GetRequiredService(accessContextType);
    var accessMigrator = accessDb.GetService<IMigrator>();
    await accessMigrator.MigrateAsync("20261001044231_LeadHandoverRecoveryGrant");
    await accessDb.Database.ExecuteSqlRawAsync("""
        INSERT INTO [access].[WorkspaceDirectoryRevisions] ([WorkspaceId], [Revision])
        VALUES (N'ws_legacy', 7), (N'ws_custom', 11), (N'ws_clean', 13);
        INSERT INTO [access].[Roles]
            ([RoleId], [WorkspaceId], [Name], [NormalizedName], [Description], [SourceTemplateId], [IsActive], [Version], [CreatedAt], [UpdatedAt])
        VALUES
            (N'role_owner', N'ws_legacy', N'Workspace Owner', N'WORKSPACE OWNER', NULL, N'system:workspace-owner', 1, 0, SYSUTCDATETIME(), SYSUTCDATETIME()),
            (N'role_custom', N'ws_custom', N'Custom', N'CUSTOM', NULL, NULL, 1, 3, SYSUTCDATETIME(), SYSUTCDATETIME()),
            (N'role_inactive', N'ws_custom', N'Inactive', N'INACTIVE', NULL, NULL, 0, 4, SYSUTCDATETIME(), SYSUTCDATETIME()),
            (N'role_clean', N'ws_clean', N'Clean', N'CLEAN', NULL, NULL, 1, 2, SYSUTCDATETIME(), SYSUTCDATETIME());
        INSERT INTO [access].[RoleCapabilities] ([RoleId], [Capability])
        VALUES (N'role_owner', N'leads.handover'), (N'role_custom', N'leads.handover'),
            (N'role_inactive', N'leads.handover'), (N'role_custom', N'leads.assign'),
            (N'role_inactive', N'tasks.create'), (N'role_clean', N'tasks.assign');
        INSERT INTO [access].[WorkspaceServiceCapabilityGrants] ([WorkspaceId], [ServicePrincipalId], [Capability], [GrantedAt])
        VALUES (N'ws_legacy', N'svc_lead_handover_recovery', N'leads.handover.recover', SYSUTCDATETIME());
        """);
    foreach (var capability in oldOwner)
        await accessDb.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO [access].[RoleCapabilities] ([RoleId], [Capability]) VALUES (N'role_owner', {capability})");
    await accessMigrator.MigrateAsync("20261001100000_RemoveHumanLeadHandoverCapability");
    Check(await accessDb.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM [access].[RoleCapabilities] WHERE [Capability] = N'leads.handover'").SingleAsync() == 0,
        "migration removes human grant from owner, custom and inactive roles");
    var storedOwner = await accessDb.Database.SqlQuery<string>($"SELECT [Capability] AS [Value] FROM [access].[RoleCapabilities] WHERE [RoleId] = N'role_owner'").ToArrayAsync();
    Check(storedOwner.Order(StringComparer.Ordinal).SequenceEqual(currentOwner, StringComparer.Ordinal), "migrated owner converges to exact current capability set");
    Check(await accessDb.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM [access].[RoleCapabilities] WHERE ([RoleId] = N'role_custom' AND [Capability] = N'leads.assign') OR ([RoleId] = N'role_inactive' AND [Capability] = N'tasks.create') OR ([RoleId] = N'role_clean' AND [Capability] = N'tasks.assign')").SingleAsync() == 3,
        "migration preserves unrelated custom and inactive role permissions");
    Check(await accessDb.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM [access].[WorkspaceServiceCapabilityGrants] WHERE [ServicePrincipalId] = N'svc_lead_handover_recovery' AND [Capability] = N'leads.handover.recover'").SingleAsync() == 1,
        "migration preserves recovery service authority");
    Check(await accessDb.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM [access].[WorkspaceDirectoryRevisions] WHERE ([WorkspaceId] = N'ws_legacy' AND [Revision] = 8) OR ([WorkspaceId] = N'ws_custom' AND [Revision] = 12) OR ([WorkspaceId] = N'ws_clean' AND [Revision] = 13)").SingleAsync() == 3,
        "migration advances affected workspaces once and preserves unaffected revision");
    Check(await accessDb.Database.SqlQuery<long>($"SELECT [Version] AS [Value] FROM [access].[Roles] WHERE [RoleId] = N'role_owner'").SingleAsync() == 0,
        "capability cleanup preserves untouched seed identity for historical upgrades");
    await accessDb.Database.ExecuteSqlRawAsync(((SqlOperation)removal.UpOperations.Single()).Sql);
    Check(await accessDb.Database.SqlQuery<long>($"SELECT [Revision] AS [Value] FROM [access].[WorkspaceDirectoryRevisions] WHERE [WorkspaceId] = N'ws_custom'").SingleAsync() == 12,
        "repeated cleanup does not advance revision again");
    Console.WriteLine($"Lead Handover Workspace verification: PASS={passed} FAIL=0");
}
finally
{
    SqlConnection.ClearAllPools();
    await using var drop = new SqlCommand($"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]", admin);
    await drop.ExecuteNonQueryAsync();
}
