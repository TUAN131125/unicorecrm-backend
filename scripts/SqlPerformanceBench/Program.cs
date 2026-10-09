using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Security.Cryptography;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using UnicoreCRM.Crm.Contacts.Application.ListContacts;
using UnicoreCRM.Crm.Contacts.Infrastructure.Persistence;
using UnicoreCRM.Crm.Leads.Infrastructure.Persistence;
using UnicoreCRM.Operations.Tasks.Contracts;
using UnicoreCRM.Operations.Tasks.Infrastructure.Persistence;

// SQL fixture mode does not read application configuration or accept connection strings.
// API load mode validates its explicitly supplied fixture settings in ApiLoad.
// Integrated identity only. All writes are confined to a freshly allocated database.
if(args.Length==2 && args[0]=="--api-load") {await ApiLoad.Run(args[1]);return;}
if(args.Length==2 && args[0]=="--validate-harness") {await HarnessVerification.Run(args[1]);return;}
if (args.Length < 2 || !Path.IsPathRooted(args[1]))
    throw new ArgumentException("Usage: SqlPerformanceBench <server> <absolute evidence directory> [max-tier:10000|100000|500000] [iterations:20..100]");
var server = args[0];
var output = Path.GetFullPath(args[1]);
for(var ancestor=new DirectoryInfo(output);ancestor is not null;ancestor=ancestor.Parent)
    if(File.Exists(Path.Combine(ancestor.FullName,".git")) || Directory.Exists(Path.Combine(ancestor.FullName,".git")))
        throw new ArgumentException("Evidence directory must be outside a Git checkout.");
if(File.Exists(Path.Combine(output,"manifest.json"))) throw new ArgumentException("Use a fresh evidence directory; prior run manifests must be preserved.");
Directory.CreateDirectory(output);
var maximum = args.Length > 2 ? int.Parse(args[2], CultureInfo.InvariantCulture) : 100000;
var iterations = args.Length > 3 ? int.Parse(args[3], CultureInfo.InvariantCulture) : 20;
var countIndexExperiment = args.Length > 4 && args[4] == "--count-index-experiment";
var ownerIndexExperiment = args.Length > 4 && args[4] == "--owner-index-experiment";
var searchProjectionExperiment = args.Length > 4 && args[4] == "--search-projection-experiment";
var indexedSearchExperiment = args.Length > 4 && args[4] == "--indexed-search-experiment";
var migrationVerification = args.Length > 4 && args[4] == "--migration-verification";
var cursorVerification = args.Length > 4 && args[4] == "--cursor-verification";
var baselineMeasurements = args.Length > 4 && args[4] == "--baseline";
var searchInvestigation = args.Length > 4 && args[4] == "--search-investigation";
var searchParityEdge = args.Length > 4 && args[4] == "--search-parity-edge";
var searchArchitecture = args.Length > 4 && args[4] == "--search-architecture";
var architectureValidation = args.Length > 4 && args[4] == "--search-architecture-validation";
if(architectureValidation && maximum!=10000)throw new ArgumentException("Focused architecture validation requires 10K only");
if (searchArchitecture && maximum > 100000) throw new ArgumentException("Search architecture proof is bounded to 10K/100K.");
if (searchInvestigation && maximum > 100000) throw new ArgumentException("Search investigation is bounded to 10K/100K.");
if (args.Length > 4 && !countIndexExperiment && !ownerIndexExperiment && !searchProjectionExperiment && !indexedSearchExperiment && !migrationVerification && !cursorVerification && !baselineMeasurements && !searchInvestigation && !searchParityEdge && !searchArchitecture && !architectureValidation) throw new ArgumentException("Unknown experiment.");
if (!new[] { 10000, 100000, 500000 }.Contains(maximum) || iterations is < 20 or > 100)
    throw new ArgumentException("Invalid bounded tier/iteration selection.");
var runId = Guid.NewGuid().ToString("N");
var database = "UniCoreCRM_PerfBench_" + runId;
string Connection(string db) => new SqlConnectionStringBuilder { DataSource = server, InitialCatalog = db,
    IntegratedSecurity = true, TrustServerCertificate = true, ApplicationName = "UniCoreCRM.SqlPerformanceBench", ConnectTimeout = 15 }.ConnectionString;
await using var control = new SqlConnection(Connection("master"));
await control.OpenAsync();
var environment = await Rows(control, "SELECT CONVERT(nvarchar(128),@@SERVERNAME) ServerName,CONVERT(nvarchar(128),SERVERPROPERTY('Edition')) Edition,CONVERT(nvarchar(40),SERVERPROPERTY('ProductVersion')) ProductVersion,CONVERT(nvarchar(260),SERVERPROPERTY('InstanceDefaultDataPath')) DataPath,CONVERT(nvarchar(260),SERVERPROPERTY('InstanceDefaultLogPath')) LogPath; SELECT cpu_count,physical_memory_kb FROM sys.dm_os_sys_info; SELECT available_physical_memory_kb FROM sys.dm_os_sys_memory; SELECT name FROM sys.databases; SELECT COUNT(*) activeRequests FROM sys.dm_exec_requests r JOIN sys.dm_exec_sessions s ON s.session_id=r.session_id WHERE s.is_user_process=1 AND r.session_id<>@@SPID AND r.status IN ('running','runnable','suspended');");
await Save("environment.json", environment);
var edition=(string)environment[0]["Edition"]!;
if(!edition.Contains("Developer",StringComparison.OrdinalIgnoreCase) && !edition.Contains("Express",StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Benchmark requires a development/test SQL edition. No database created.");
var dataPath = (string)environment[0]["DataPath"]!;
var logPath = (string)environment[0]["LogPath"]!;
// A managed SQL DATA directory is required. Refuse root/user/repo defaults.
foreach (var path in new[] { dataPath, logPath })
    if (!Path.IsPathRooted(path) || !path.Contains("Microsoft SQL Server", StringComparison.OrdinalIgnoreCase)
        || !path.TrimEnd('\\', '/').EndsWith("DATA", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Default SQL file location is not a verified managed DATA directory. No database created.");
var manifest = new Dictionary<string, object?> { ["runId"] = runId, ["databaseName"] = database,
    ["databaseServer"] = environment[0]["ServerName"], ["createdByBenchmark"] = false,
    ["creationTimestamp"] = DateTimeOffset.UtcNow, ["cleanupEligibility"] = "PENDING_CREATION", ["syntheticOnly"] = true };
await Save("manifest.json", manifest);
var created = false;
var measured = new List<object>();
try
{
    await ResourceGuard(control, 10000);
    if ((await Rows(control, $"SELECT name FROM sys.databases WHERE name=N'{database}'")).Count != 0)
        throw new InvalidOperationException("Fresh name collision. Refusing to reuse database.");
    await Execute(control, $"CREATE DATABASE [{database}]");
    created = true;
    // Capture identity/files immediately, before schema/data writes.
    var files = await Rows(control, $"SELECT name,type_desc,physical_name FROM sys.master_files WHERE database_id=DB_ID(N'{database}')");
    manifest["createdByBenchmark"] = true; manifest["files"] = files;
    manifest["cleanupEligibility"] = "OWNED_REQUIRES_REVALIDATION";
    await Save("manifest.json", manifest);
    if (files.Any(f => !((string)f["physical_name"]!).StartsWith((string)f["type_desc"]! == "LOG" ? logPath : dataPath, StringComparison.OrdinalIgnoreCase)))
        throw new InvalidOperationException("Unexpected created database file path; retain for investigation.");
    await using var connection = new SqlConnection(Connection(database));
    await connection.OpenAsync();
    await Execute(connection, $"EXEC sys.sp_addextendedproperty @name=N'UniCoreCRM_PerfBench_RunId',@value=N'{runId}';");
    var capture = new Capture();
    await using var contacts = new ContactsDbContext(new DbContextOptionsBuilder<ContactsDbContext>().UseSqlServer(Connection(database), x => x.MigrationsHistoryTable("__EFMigrationsHistory", "contacts").CommandTimeout(60)).AddInterceptors(capture).Options);
    await using var tasks = new TasksDbContext(new DbContextOptionsBuilder<TasksDbContext>().UseSqlServer(Connection(database), x => x.MigrationsHistoryTable("__EFMigrationsHistory", "tasks").CommandTimeout(60)).Options);
    await using var leads = new LeadsDbContext(new DbContextOptionsBuilder<LeadsDbContext>().UseSqlServer(Connection(database), x => x.MigrationsHistoryTable("__EFMigrationsHistory", "leads").CommandTimeout(60)).AddInterceptors(capture).Options);
    await tasks.Database.MigrateAsync(); await contacts.Database.MigrateAsync(); await leads.Database.MigrateAsync();
    if (contacts.Database.HasPendingModelChanges() || tasks.Database.HasPendingModelChanges() || leads.Database.HasPendingModelChanges())
        throw new InvalidOperationException("Migration/model mismatch.");
    await Save("migrations.json", await Rows(connection,"SELECT * FROM contacts.__EFMigrationsHistory; SELECT * FROM tasks.__EFMigrationsHistory; SELECT * FROM leads.__EFMigrationsHistory"));
    var seedSql = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Seed.sql"));
    var persistence = new EfContactsPersistence(contacts);
    var leadPersistence = new EfLeadsPersistence(leads);
    var previous = 0;
    foreach (var tier in new[] { 10000, 100000, 500000 }.Where(t => t <= maximum))
    {
        try { await Save($"tier-{tier}-resources.json",await ResourceGuard(control, tier)); }
        catch (InvalidOperationException e) { await Save($"tier-{tier}-skipped.json", new { result="NOT_RUN_RESOURCE_LIMIT",reason=e.Message }); break; }
        var seedWatch = Stopwatch.StartNew();
        var baselineMigration=contacts.Database.GetMigrations().TakeWhile(m=>!m.Contains("LifecycleIndexCoverage",StringComparison.Ordinal)).Last();
        if(migrationVerification || baselineMeasurements || countIndexExperiment || ownerIndexExperiment || searchProjectionExperiment || indexedSearchExperiment)
            await contacts.Database.GetService<IMigrator>().MigrateAsync(baselineMigration);
        for (var start = previous+1; start <= tier; start += 5000)
        {
            try { await ResourceGuard(control,tier); }
            catch(InvalidOperationException e){throw new ResourceLimitException(e.Message);}
            using var seed = new SqlCommand(seedSql, connection) { CommandTimeout = 60 };
            seed.Parameters.AddWithValue("@from", start); seed.Parameters.AddWithValue("@to", Math.Min(start+4999,tier));
            await seed.ExecuteNonQueryAsync();
            await Save("seed-progress.json",new{requestedTier=tier,seededContacts=Math.Min(start+4999,tier),seededLeads=Math.Min(start+4999,tier),batch=5000});
        }
        previous = tier;
        Console.WriteLine($"SEEDED {tier} contacts and leads in {seedWatch.Elapsed.TotalSeconds:F1}s");
        await Save($"tier-{tier}-seed.json",new{seed="deterministic integer arithmetic v1",contacts=tier,leads=tier,workspaces=26,batch=5000,incrementalSeedSeconds=seedWatch.Elapsed.TotalSeconds});
        try { await ResourceGuard(control,tier); }
        catch(InvalidOperationException e){throw new ResourceLimitException(e.Message);}
        if(searchParityEdge)
        {
            await SearchInvestigation.Differential(tier,contacts,connection,capture,Save);
            break;
        }
        if(searchArchitecture || architectureValidation)
        {
            await SearchArchitecture.Run(tier,contacts,persistence,connection,capture,Measure,Save,async()=>
            {
                try {return await ResourceGuard(control,tier);}
                catch(InvalidOperationException e){throw new ResourceLimitException(e.Message);}
            },architectureValidation);
            continue;
        }
        if(searchInvestigation)
        {
            await SearchInvestigation.Run(tier,contacts,persistence,connection,capture,Measure,Save);
            continue;
        }
        if(cursorVerification)
        {
            await Save($"tier-{tier}-cursor-validation.json",await CursorVerification.Run(persistence,connection));
            Console.WriteLine("CURSOR VALIDATED expiry, tamper and changed authority/query scopes");
            break;
        }
        if(migrationVerification)
        {
            var before=await Fingerprint(connection);
            var upgradeWatch=Stopwatch.StartNew();await contacts.Database.MigrateAsync();var upgradeMs=upgradeWatch.Elapsed.TotalMilliseconds;
            if(before!=await Fingerprint(connection)) throw new InvalidOperationException("Upgrade changed Contact data");
            var included=await Rows(connection,"SELECT i.name IndexName,c.name ColumnName,ic.key_ordinal,ic.is_included_column FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE i.object_id=OBJECT_ID('contacts.Contacts') AND i.name IN ('IX_Contacts_WorkspaceId_UpdatedAt_ContactId','IX_Contacts_WorkspaceId_OwnerId_CreatedAt_ContactId') ORDER BY i.name,ic.index_column_id");
            if(included.Count(x=>Equals(x["IndexName"],"IX_Contacts_WorkspaceId_UpdatedAt_ContactId") && Equals(x["is_included_column"],true))!=3) throw new InvalidOperationException("Lifecycle include set incorrect");
            if(included.Count(x=>Equals(x["IndexName"],"IX_Contacts_WorkspaceId_OwnerId_CreatedAt_ContactId") && Equals(x["is_included_column"],true))!=2) throw new InvalidOperationException("Owner lifecycle include set incorrect");
            await contacts.Database.GetService<IMigrator>().MigrateAsync(baselineMigration);
            if(before!=await Fingerprint(connection)) throw new InvalidOperationException("Rollback changed Contact data");
            await contacts.Database.MigrateAsync();
            if(before!=await Fingerprint(connection) || contacts.Database.HasPendingModelChanges()) throw new InvalidOperationException("Reapply/model mismatch");
            await Save($"tier-{tier}-migration-validation.json",new{result="PASS",cleanInstall="PASS",baselineMigration,upgradeMs,rows=tier,dataFingerprint=before,rollback="PASS",reapply="PASS",included});
            Console.WriteLine($"MIGRATION VALIDATED {tier} rows upgrade {upgradeMs:F1}ms");
            continue;
        }
        if(countIndexExperiment || ownerIndexExperiment)
        {
            var indexWatch=Stopwatch.StartNew();
            var beforeSize=await Rows(connection,"SELECT SUM(p.used_page_count)*8.0 KB FROM sys.dm_db_partition_stats p JOIN sys.indexes i ON i.object_id=p.object_id AND i.index_id=p.index_id WHERE i.name='IX_Contacts_WorkspaceId_UpdatedAt_ContactId'");
            await Execute(connection,"CREATE INDEX IX_Contacts_WorkspaceId_UpdatedAt_ContactId ON contacts.Contacts (WorkspaceId,UpdatedAt,ContactId) INCLUDE (Status,ArchivedAt,OwnerId) WITH (DROP_EXISTING=ON)");
            await Save($"tier-{tier}-count-index-experiment.json",new{elapsedMs=indexWatch.Elapsed.TotalMilliseconds,scope="DISPOSABLE_DATABASE_ONLY",reason="Measured count/summary clustered scan reads the full JSON-bearing table; include lifecycle predicates in the existing narrow paging index."});
            await Save($"tier-{tier}-index-size.json",new{beforeKB=beforeSize,afterKB=await Rows(connection,"SELECT SUM(p.used_page_count)*8.0 KB FROM sys.dm_db_partition_stats p JOIN sys.indexes i ON i.object_id=p.object_id AND i.index_id=p.index_id WHERE i.name='IX_Contacts_WorkspaceId_UpdatedAt_ContactId'")});
        }
        if(ownerIndexExperiment)
        {
            var ownerIndexWatch=Stopwatch.StartNew();
            await Execute(connection,"CREATE INDEX IX_Contacts_WorkspaceId_OwnerId_CreatedAt_ContactId ON contacts.Contacts (WorkspaceId,OwnerId,CreatedAt,ContactId) INCLUDE (Status,ArchivedAt) WITH (DROP_EXISTING=ON)");
            await Save($"tier-{tier}-owner-index-experiment.json",new{elapsedMs=ownerIndexWatch.Elapsed.TotalMilliseconds,scope="DISPOSABLE_DATABASE_ONLY",reason="Owner aggregate chooses broader lifecycle scan when the existing owner index lacks status/archive predicates."});
        }
        if(searchProjectionExperiment || indexedSearchExperiment)
        {
            var projectionWatch=Stopwatch.StartNew();
            foreach(var field in Capture.SearchFields)
                await Execute(connection,$"IF COL_LENGTH('contacts.Contacts','Search_{field}') IS NULL ALTER TABLE contacts.Contacts ADD [Search_{field}] AS UPPER(JSON_VALUE([Profile],'$.{field}')){(searchProjectionExperiment ? " PERSISTED" : "")}");
            if(indexedSearchExperiment)
            {
                await Save($"tier-{tier}-index-before.json",await Rows(connection,"SELECT SUM(p.used_page_count)*8.0 KB FROM sys.dm_db_partition_stats p JOIN sys.indexes i ON i.object_id=p.object_id AND i.index_id=p.index_id WHERE i.name='IX_Contacts_WorkspaceId_UpdatedAt_ContactId'"));
                await Execute(connection,"CREATE INDEX IX_Contacts_WorkspaceId_UpdatedAt_ContactId ON contacts.Contacts (WorkspaceId,UpdatedAt,ContactId) INCLUDE (Status,ArchivedAt,OwnerId,FullName,Search_displayName,Search_workEmail,Search_personalEmail,Search_mobilePhone,Search_workPhone,Search_otherPhone) WITH (DROP_EXISTING=ON)");
                await Save($"tier-{tier}-index-after.json",await Rows(connection,"SELECT SUM(p.used_page_count)*8.0 KB FROM sys.dm_db_partition_stats p JOIN sys.indexes i ON i.object_id=p.object_id AND i.index_id=p.index_id WHERE i.name='IX_Contacts_WorkspaceId_UpdatedAt_ContactId'"));
            }
            capture.SearchProjection=true;
            await Save($"tier-{tier}-search-projection-experiment.json",new{elapsedMs=projectionWatch.Elapsed.TotalMilliseconds,scope="DISPOSABLE_DATABASE_ONLY",reason="Measured repeated JSON extraction/UPPER CPU in substring search; persisted expressions preserve each readable field predicate and SQL null/collation semantics."});
        }
        await Save($"tier-{tier}-distribution.json", await Rows(connection,"SELECT WorkspaceId,COUNT_BIG(*) Contacts FROM contacts.Contacts GROUP BY WorkspaceId; SELECT Status,COUNT_BIG(*) Contacts FROM contacts.Contacts GROUP BY Status; SELECT COUNT_BIG(*) Tasks FROM tasks.Tasks; SELECT COUNT_BIG(*) Relationships FROM contacts.CustomerRelationships; SELECT SUM(size)*8.0/1024 DatabaseMB FROM sys.database_files"));
        await Save($"tier-{tier}-indexes.json", await Rows(connection,"SELECT SCHEMA_NAME(t.schema_id) SchemaName,t.name TableName,i.name IndexName,i.type_desc,i.filter_definition,c.name ColumnName,ic.key_ordinal,ic.is_included_column FROM sys.tables t JOIN sys.indexes i ON i.object_id=t.object_id JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE SCHEMA_NAME(t.schema_id) IN ('contacts','tasks','leads') ORDER BY SchemaName,TableName,IndexName,ic.index_column_id; SELECT SCHEMA_NAME(t.schema_id) SchemaName,t.name TableName,s.name StatisticsName,p.last_updated,p.rows,p.rows_sampled,p.modification_counter FROM sys.tables t JOIN sys.stats s ON s.object_id=t.object_id OUTER APPLY sys.dm_db_stats_properties(s.object_id,s.stats_id) p WHERE SCHEMA_NAME(t.schema_id) IN ('contacts','tasks','leads')"));
        var fields = new[] {"displayName","workEmail","personalEmail","mobilePhone","workPhone","otherPhone"};
        ContactListSpecification Spec(ContactListFilters f, string workspace="bench_large", string? taskOwner=null, bool follow=true) => new(workspace,null,f,fields,FollowUpAuthority:follow ? new ContactFollowUpReadAuthority(workspace,taskOwner) : null,
            DayStart:f.FollowUp is null ? null : DateTimeOffset.Parse("2026-10-08T00:00:00Z"),DayEnd:f.FollowUp is null ? null : DateTimeOffset.Parse("2026-10-09T00:00:00Z"));
        var cases = new (string Name,ContactListFilters Filters)[] {
            ("first",new()),("name",new(Sort:"nameAsc")),("due",new(Sort:"nextFollowUp")),
            ("search-common",new(Search:"Synthetic")),("search-rare",new(Search:"RareZebra")),("search-none",new(Search:"NoSuchSyntheticMatch")),
            ("status",new(Status:"needs_follow_up")),("archived",new(Status:"archived")),("owner",new(OwnerId:"member_1")),
            ("source",new(Source:"web")),("source-rare",new(Source:"rare_campaign")),("relationship",new(RelationshipLevel:"close")),
            ("decision",new(DecisionRole:"decision_maker")),("dnc",new(DoNotContact:true)),("linked",new(Link:"linked")),("unlinked",new(Link:"unlinked")),
            ("today",new(FollowUp:"today")),("overdue",new(FollowUp:"overdue")),
            ("combined",new(OwnerId:"member_1",Source:"import",RelationshipLevel:"close",Link:"unlinked")),
            ("search-followup",new(Search:"Synthetic",FollowUp:"overdue")) };
        foreach (var (name,f) in cases)
        {
            if((countIndexExperiment || ownerIndexExperiment || searchProjectionExperiment || indexedSearchExperiment) && name is not ("first" or "name" or "owner" or "status" or "source" or "search-common" or "search-rare" or "search-none" or "due")) continue;
            if((countIndexExperiment || ownerIndexExperiment) && name is "search-rare" or "search-none") continue;
            var spec = Spec(f);
            await Measure($"{tier}-contact-{name}",async()=> { var p=await persistence.ReadContactPageAsync(spec,26,default); if(p.Items.Count>26 || p.Items.Any(x=>x.Contact.WorkspaceId!=spec.WorkspaceId)) throw new InvalidOperationException("Bound/isolation"); },capture,connection);
            if (name is "first" or "search-common" or "owner" or "source" or "today" or "combined")
                await Measure($"{tier}-summary-{name}",async()=> { await persistence.ReadContactStatusCountsAsync(spec,default); },capture,connection);
        }
        if(countIndexExperiment || ownerIndexExperiment || searchProjectionExperiment || indexedSearchExperiment) continue;
        foreach (var workspace in new[]{"bench_medium_0","bench_small_8"})
            foreach (var f in new[]{new ContactListFilters(),new ContactListFilters(Search:"Synthetic")})
                await Measure($"{tier}-{workspace}-{(f.Search is null ? "first":"search")}",async()=>{await persistence.ReadContactPageAsync(Spec(f,workspace),26,default);},capture,connection);
        await Measure($"{tier}-tasks-own",async()=>{await persistence.ReadContactPageAsync(Spec(new(FollowUp:"overdue"),taskOwner:"member_1"),26,default);},capture,connection);
        await Measure($"{tier}-no-task-authority",async()=>{await persistence.ReadContactPageAsync(Spec(new(),follow:false),26,default);},capture,connection);
        foreach (var sort in new[]{"recentlyUpdated","nameAsc","nextFollowUp"})
        {
            var spec=Spec(new(Sort:sort)); var seen=new HashSet<string>();
            for (var page=1;page<=101;page++)
            {
                var p=await persistence.ReadContactPageAsync(spec,26,default);
                foreach(var row in p.Items.Take(25)) if(!seen.Add(row.Contact.ContactId)) throw new InvalidOperationException("Keyset duplicate");
                if(page is 2 or 10 or 100) { var saved=spec; await Measure($"{tier}-deep-{sort}-{page}",async()=>{await persistence.ReadContactPageAsync(saved,26,default);},capture,connection); }
                if(p.Items.Count<=25) break;
                var last=p.Items[24]; spec=spec with {CursorId=last.Contact.ContactId,CursorName=last.Contact.FullName,CursorUpdatedAt=last.Contact.UpdatedAt,CursorFollowUpAt=last.NextFollowUpAt,CursorFollowUpIsNull=last.NextFollowUpAt is null};
            }
            await Save($"{tier}-keyset-{sort}.json",new{uniqueRows=seen.Count,result="PASS",liveSnapshot="NOT_PROVIDED"});
        }
        foreach(var column in new[]{"NEW","CONTACTING","VERIFYING","POSITIVE_OUTCOME"})
        {
            await Measure($"{tier}-lead-{column}",async()=>{await leadPersistence.CountColumnAsync("bench_large",null,null,null,false,column,null,null,true,default);await leadPersistence.ListColumnAsync("bench_large",null,null,null,false,column,null,null,true,null,null,26,default);},capture,connection);
        }
        var leadRows=await leadPersistence.ListColumnAsync("bench_large",null,null,null,false,"NEW",null,null,true,null,null,26,default);
        for(var page=2;page<=100 && leadRows.Count>25;page++)
        {
            var last=leadRows[24];
            if(page is 2 or 10 or 100) await Measure($"{tier}-lead-deep-{page}",async()=>{await leadPersistence.CountColumnAsync("bench_large",null,null,null,false,"NEW",null,null,true,default);await leadPersistence.ListColumnAsync("bench_large",null,null,null,false,"NEW",null,null,true,last.UpdatedAt,last.LeadId,26,default);},capture,connection);
            leadRows=await leadPersistence.ListColumnAsync("bench_large",null,null,null,false,"NEW",null,null,true,last.UpdatedAt,last.LeadId,26,default);
        }
        await Measure($"{tier}-lead-search",async()=>{await leadPersistence.CountColumnAsync("bench_large",null,null,null,false,"NEW",null,"SYNTHETIC",true,default);await leadPersistence.ListColumnAsync("bench_large",null,null,null,false,"NEW",null,"SYNTHETIC",true,null,null,26,default);},capture,connection);
        await Save("measurements.json",measured);
    }
    async Task Measure(string label,Func<Task> operation,Capture collector,SqlConnection replay)
    {
        var measurementTier=int.TryParse(label.Split('-')[0],out var parsedTier)?parsedTier:maximum;
        try { await ResourceGuard(control,measurementTier); }
        catch(InvalidOperationException e){throw new ResourceLimitException(e.Message);}
        collector.Commands.Clear(); collector.Enabled=true;
        var sw=Stopwatch.StartNew(); await operation(); var first=sw.Elapsed.TotalMilliseconds;
        collector.Enabled=false; var commands=collector.Commands.ToArray();
        if(commands.Length is <1 or >2) throw new InvalidOperationException("Unexpected query count/N+1");
        var times=new List<double>();
        await operation(); // one explicit warm-up; first observation did not flush shared caches
        for(var n=0;n<iterations;n++){sw.Restart();await operation();times.Add(sw.Elapsed.TotalMilliseconds);}
        var sqlResults=new List<object>();
        for(var n=0;n<commands.Length;n++)
        {
            var snapshot=commands[n]; var messages=new List<string>();
            SqlInfoMessageEventHandler handler=(_,e)=>messages.Add(e.Message);
            replay.InfoMessage+=handler;
            try
            {
                using var cmd=snapshot.Create(replay,"SET STATISTICS IO ON; SET STATISTICS TIME ON; SET STATISTICS XML ON;\n", "\n;SET STATISTICS XML OFF; SET STATISTICS IO OFF; SET STATISTICS TIME OFF;");
                await using var reader=await cmd.ExecuteReaderAsync();
                var rows=0; var xml="";
                do { while(await reader.ReadAsync()) { if(reader.FieldCount==1 && reader.GetValue(0) is string s && s.Contains("<ShowPlanXML",StringComparison.Ordinal)) xml=s; else rows++; } } while(await reader.NextResultAsync());
                await reader.CloseAsync();
                if(xml.Length==0) throw new InvalidOperationException("Actual execution plan missing");
                await File.WriteAllTextAsync(Path.Combine(output,$"{label}-{n}.sqlplan"),xml);
                await File.WriteAllTextAsync(Path.Combine(output,$"{label}-{n}.sql"),snapshot.Text);
                await Save($"{label}-{n}-parameters.json",snapshot.Parameters.Select(p=>new{p.ParameterName,type=p.SqlDbType.ToString(),p.Size,p.Value}));
                await File.WriteAllLinesAsync(Path.Combine(output,$"{label}-{n}-io.txt"),messages);
                var doc=XDocument.Parse(xml); XNamespace ns="http://schemas.microsoft.com/sqlserver/2004/07/showplan";
                var runtime=doc.Descendants(ns+"RunTimeCountersPerThread").ToArray();
                var queryTimes=doc.Descendants(ns+"QueryTimeStats").Select(x=>new{cpuMs=(double?)x.Attribute("CpuTime"),elapsedMs=(double?)x.Attribute("ElapsedTime")}).ToArray();
                var grants=doc.Descendants(ns+"MemoryGrantInfo").Select(x=>x.Attributes().ToDictionary(a=>a.Name.LocalName,a=>a.Value)).ToArray();
                var reads=Regex.Matches(string.Join("\n",messages),@"(?<!physical )(?<!read-ahead )logical reads (\d+)").Sum(m=>long.Parse(m.Groups[1].Value,CultureInfo.InvariantCulture));
                var latency=new List<double>();
                for(var k=0;k<iterations;k++){using var plain=snapshot.Create(replay);sw.Restart();await using var rr=await plain.ExecuteReaderAsync();while(await rr.ReadAsync()){}latency.Add(sw.Elapsed.TotalMilliseconds);}
                sqlResults.Add(new{command=n,rowsReturned=rows,logicalReads=reads,queryTimes,actualRowsReadAcrossOperators=runtime.Sum(x=>(double?)x.Attribute("ActualRowsRead")??0),
                    actualRowsAcrossOperators=runtime.Sum(x=>(double?)x.Attribute("ActualRows")??0),operators=doc.Descendants(ns+"RelOp").Select(x=>(string?)x.Attribute("PhysicalOp")).Distinct(),
                    indexes=doc.Descendants(ns+"Object").Select(x=>(string?)x.Attribute("Index")).Where(x=>x is not null).Distinct(),memoryGrants=grants,
                    spills=doc.Descendants().Where(x=>x.Name.LocalName.Contains("Spill",StringComparison.Ordinal)).Select(x=>x.ToString()).ToArray(),latency=Stats(latency),samplesMs=latency});
            } finally {replay.InfoMessage-=handler;}
        }
        measured.Add(new{label,firstObservationMs=first,warmup=1,iterations,combinedLatency=Stats(times),samplesMs=times,commands=sqlResults});
        await Save("measurements.json",measured);
        Console.WriteLine($"MEASURED {label} p95={Stats(times).p95:F2}ms");
    }
}
catch(ResourceLimitException e)
{
    await Save("resource-stop.json",new{result="PARTIAL_NOT_RUN_RESOURCE_LIMIT",reason=e.Message});
    Console.WriteLine("Stopped remaining measurements at resource guard; completed samples preserved.");
}
finally
{
    if(created)
    {
        // Do not force-close somebody else's session or delete files manually.
        try
        {
            SqlConnection.ClearAllPools();
            var identity=await Rows(control,$"SELECT CONVERT(nvarchar(128),@@SERVERNAME) ServerName; SELECT name,type_desc,physical_name FROM sys.master_files WHERE database_id=DB_ID(N'{database}'); SELECT COUNT(*) Sessions FROM sys.dm_exec_sessions WHERE database_id=DB_ID(N'{database}') AND session_id<>@@SPID;");
            await using var verify=new SqlConnection(Connection(database));await verify.OpenAsync();
            var marker=await Rows(verify,"SELECT CONVERT(nvarchar(128),value) RunId FROM sys.extended_properties WHERE class=0 AND name=N'UniCoreCRM_PerfBench_RunId'");
            await verify.CloseAsync(); SqlConnection.ClearAllPools();
            if(marker.Count!=1 || (string)marker[0]["RunId"]! !=runId || !Equals(identity[0]["ServerName"],manifest["databaseServer"]) || Convert.ToInt32(identity[^1]["Sessions"],CultureInfo.InvariantCulture)!=0)
                throw new InvalidOperationException("Ownership/session revalidation failed");
            var original=(List<Dictionary<string,object?>>)manifest["files"]!;
            var current=identity.Skip(1).Take(original.Count).ToList();
            if(JsonSerializer.Serialize(original)!=JsonSerializer.Serialize(current)) throw new InvalidOperationException("File manifest changed");
            await Execute(control,$"DROP DATABASE [{database}]");
            manifest["cleanupEligibility"]="CLEANED";
        }
        catch(Exception e){manifest["cleanupEligibility"]="RETAINED";manifest["cleanupReason"]=e.Message;Console.WriteLine("RETAINED owned database; see manifest");}
        await Save("manifest.json",manifest);
    }
}
async Task Save(string name,object value)=>await File.WriteAllTextAsync(Path.Combine(output,name),JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true}));
static Distribution Stats(List<double> samples){var a=samples.Order().ToArray();double P(double q)=>a[Math.Clamp((int)Math.Ceiling(q*a.Length)-1,0,a.Length-1)];return new(P(.5),P(.95),P(.99));}
static async Task Execute(SqlConnection c,string sql){using var cmd=new SqlCommand(sql,c){CommandTimeout=60};await cmd.ExecuteNonQueryAsync();}
static async Task<List<Dictionary<string,object?>>> Rows(SqlConnection c,string sql)
{
    using var cmd=new SqlCommand(sql,c){CommandTimeout=30};await using var r=await cmd.ExecuteReaderAsync();var rows=new List<Dictionary<string,object?>>();
    do{while(await r.ReadAsync()){var row=new Dictionary<string,object?>();for(var n=0;n<r.FieldCount;n++)row[r.GetName(n)]=await r.IsDBNullAsync(n)?null:r.GetValue(n);rows.Add(row);}}while(await r.NextResultAsync());return rows;
}
static async Task<string> Fingerprint(SqlConnection c)
{
    using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    using var cmd=new SqlCommand("SELECT * FROM contacts.Contacts ORDER BY ContactId",c){CommandTimeout=60};
    await using var reader=await cmd.ExecuteReaderAsync();
    hash.AppendData(JsonSerializer.SerializeToUtf8Bytes(Enumerable.Range(0,reader.FieldCount).Select(reader.GetName).ToArray()));
    while(await reader.ReadAsync()){var values=new object[reader.FieldCount];reader.GetValues(values);hash.AppendData(JsonSerializer.SerializeToUtf8Bytes(values));}
    return Convert.ToHexString(hash.GetHashAndReset());
}
static async Task<List<Dictionary<string,object?>>> ResourceGuard(SqlConnection c,int tier)
{
    var resources=await Rows(c,"SELECT available_physical_memory_kb MemoryKB FROM sys.dm_os_sys_memory; SELECT MIN(v.available_bytes) FreeBytes FROM sys.master_files f CROSS APPLY sys.dm_os_volume_stats(f.database_id,f.file_id) v; SELECT COUNT(*) Requests FROM sys.dm_exec_requests r JOIN sys.dm_exec_sessions s ON s.session_id=r.session_id WHERE s.is_user_process=1 AND r.session_id<>@@SPID AND r.status IN ('running','runnable','suspended');");
    var requiredMemoryKB=(tier<=10000 ? 1L : 2L)*1024*1024;
    if(Convert.ToInt64(resources[0]["MemoryKB"],CultureInfo.InvariantCulture)<requiredMemoryKB || Convert.ToInt64(resources[1]["FreeBytes"],CultureInfo.InvariantCulture)<8L*1024*1024*1024+tier*12000L || Convert.ToInt32(resources[2]["Requests"],CultureInfo.InvariantCulture)>8)
        throw new InvalidOperationException($"RESOURCE_LIMIT: need {requiredMemoryKB/1024}MiB available RAM, 8GiB disk reserve plus 12KiB/row estimate, <=8 concurrent other active requests. Observed: {JsonSerializer.Serialize(resources)}");
    return resources;
}
sealed record Distribution(double p50,double p95,double p99);
sealed class ResourceLimitException(string message):Exception(message);
sealed record Snapshot(string Text,SqlParameter[] Parameters)
{
    public SqlCommand Create(SqlConnection c,string prefix="",string suffix="") {var cmd=new SqlCommand(prefix+Text+suffix,c){CommandTimeout=60};foreach(var p in Parameters)cmd.Parameters.Add((SqlParameter)((ICloneable)p).Clone());return cmd;}
}
sealed class Capture:DbCommandInterceptor
{
    public static readonly string[] SearchFields=["displayName","workEmail","personalEmail","mobilePhone","workPhone","otherPhone"];
    public bool SearchProjection {get;set;}
    public Func<string,string>? Rewrite {get;set;}
    public bool Enabled {get;set;}
    public List<Snapshot> Commands {get;}=[];
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,CommandEventData eventData,InterceptionResult<DbDataReader> result,CancellationToken cancellationToken=default)
    {
        if(SearchProjection)foreach(var field in SearchFields)command.CommandText=command.CommandText.Replace($"UPPER(JSON_VALUE(c.[Profile], '$.{field}'))",$"c.[Search_{field}]",StringComparison.Ordinal);
        if(Rewrite is not null) command.CommandText=Rewrite(command.CommandText);
        if(Enabled)Commands.Add(new(command.CommandText,command.Parameters.Cast<SqlParameter>().Select(p=>(SqlParameter)((ICloneable)p).Clone()).ToArray()));return ValueTask.FromResult(result);
    }
}
