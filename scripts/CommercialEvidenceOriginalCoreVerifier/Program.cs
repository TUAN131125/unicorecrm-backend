using System.Collections.Concurrent;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using UnicoreCRM.CommercialEvidence;
using UnicoreCRM.CommercialEvidence.CommercialEvidence.Application;
using UnicoreCRM.CommercialEvidence.CommercialEvidence.Contracts;
using UnicoreCRM.CommercialEvidence.CommercialEvidence.Infrastructure.Persistence;
using UnicoreCRM.Platform.Workspace.Contracts;

if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
    throw new ArgumentException("Pass exactly one isolated SQL Server connection string.");

var verifier = new CommercialEvidenceVerifier(args[0]);
await verifier.RunAsync();

internal sealed class CommercialEvidenceVerifier(string connectionString)
{
    private readonly List<string> results = [];
    private int passed;
    private int failed;

    internal async Task RunAsync()
    {
        await RecreateDatabaseAsync();
        var ids = new ControlledIdGenerator();
        var policy = new MutablePolicyVersionProvider("policy-A");
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:UnicoreCRM"] = connectionString
            })
            .Build();
        services.AddCommercialEvidenceModule(configuration);
        services.RemoveAll<IPurchaseEvidenceIdGenerator>();
        services.RemoveAll<ICommercialEvidencePolicyVersionProvider>();
        services.RemoveAll<TimeProvider>();
        services.AddSingleton<IPurchaseEvidenceIdGenerator>(ids);
        services.AddSingleton<ICommercialEvidencePolicyVersionProvider>(policy);
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(
            new DateTimeOffset(2026, 8, 29, 16, 0, 0, TimeSpan.Zero)));

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        try
        {
            await using (var scope = provider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<CommercialEvidenceDbContext>();
                await db.Database.MigrateAsync();
            }

            await VerifyPhysicalModelAsync();
            await VerifyAppendReplayAndReadAsync(provider, ids, policy);
            await VerifyExactSourceEqualityAsync(provider);
            await VerifyConcurrentReplayAsync(provider);
            await VerifyConcurrentConflictAsync(provider);
            await VerifyAggregateIdCollisionAsync(provider, ids);
            await VerifyWorkspaceQualifiedIdentityAsync(provider, ids);
            await VerifyAuditAtomicityAsync(provider);
            VerifyCallableSurface();
        }
        finally
        {
            foreach (var result in results)
                Console.WriteLine(result);
            Console.WriteLine($"CommercialEvidence Original Core verification: passed={passed} failed={failed}");
        }

        if (failed != 0)
            throw new InvalidOperationException("CommercialEvidence Original Core verification failed.");
    }

    private async Task VerifyPhysicalModelAsync()
    {
        Check("schema exists", 1L, await ScalarLongAsync(
            "SELECT COUNT(*) FROM sys.schemas WHERE name = N'commercial_evidence'"));
        Check("PurchaseEvidence table exists", 1L, await ScalarLongAsync(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA=N'commercial_evidence' AND TABLE_NAME=N'PurchaseEvidence'"));
        Check("AuditRecords table exists", 1L, await ScalarLongAsync(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA=N'commercial_evidence' AND TABLE_NAME=N'AuditRecords'"));
        Check("composite aggregate primary key", "WorkspaceId,EvidenceId", await ScalarStringAsync("""
            SELECT STRING_AGG(c.name, ',') WITHIN GROUP (ORDER BY ic.key_ordinal)
            FROM sys.key_constraints kc
            JOIN sys.index_columns ic ON ic.object_id=kc.parent_object_id AND ic.index_id=kc.unique_index_id
            JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
            WHERE kc.name=N'PK_PurchaseEvidence'
            """));
        Check("no global EvidenceId unique key", 0L, await ScalarLongAsync("""
            SELECT COUNT(*) FROM sys.indexes i
            WHERE i.object_id=OBJECT_ID(N'commercial_evidence.PurchaseEvidence') AND i.is_unique=1
              AND NOT EXISTS (SELECT 1 FROM sys.index_columns ic JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE ic.object_id=i.object_id AND ic.index_id=i.index_id AND c.name=N'WorkspaceId')
            """));
        Check("source unique index exists", 1L, await ScalarLongAsync("""
            SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID(N'commercial_evidence.PurchaseEvidence')
              AND name=N'UX_PurchaseEvidence_Workspace_Source' AND is_unique=1 AND filter_definition IS NULL
            """));
        Check("source unique index exact columns", "WorkspaceId,SourceType,SourceSystem,SourceId", await ScalarStringAsync("""
            SELECT STRING_AGG(c.name, ',') WITHIN GROUP (ORDER BY ic.key_ordinal)
            FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
            JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
            WHERE i.object_id=OBJECT_ID(N'commercial_evidence.PurchaseEvidence') AND i.name=N'UX_PurchaseEvidence_Workspace_Source'
            """));
        Check("source columns use binary collation", 4L, await ScalarLongAsync("""
            SELECT COUNT(*) FROM sys.columns
            WHERE object_id=OBJECT_ID(N'commercial_evidence.PurchaseEvidence')
              AND name IN (N'WorkspaceId',N'SourceType',N'SourceSystem',N'SourceId')
              AND collation_name=N'Latin1_General_100_BIN2'
            """));
        Check("closed vocabulary constraints exist", 3L, await ScalarLongAsync("""
            SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'commercial_evidence.PurchaseEvidence')
              AND name IN (N'CK_PurchaseEvidence_EvidenceType',N'CK_PurchaseEvidence_SourceType',N'CK_PurchaseEvidence_BuyerRefType')
            """));
        Check("source mapping constraint exists", 1L, await ScalarLongAsync("""
            SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'commercial_evidence.PurchaseEvidence')
              AND name=N'CK_PurchaseEvidence_SourceMapping'
            """));
        Check("no foreign-owner FK", 0L, await ScalarLongAsync("""
            SELECT COUNT(*) FROM sys.foreign_keys fk
            WHERE fk.parent_object_id IN (OBJECT_ID(N'commercial_evidence.PurchaseEvidence'),OBJECT_ID(N'commercial_evidence.AuditRecords'))
              AND OBJECT_SCHEMA_NAME(fk.referenced_object_id)<>N'commercial_evidence'
            """));

        await ExpectSqlFailureAsync("invalid lowercase EvidenceType rejected", BaseRawInsert("raw_vocab_1", "raw_vocab_e1", "order_completed", "ORDER", "CONTACT", "NULL"));
        await ExpectSqlFailureAsync("padded EvidenceType rejected", BaseRawInsert("raw_vocab_2", "raw_vocab_e2", "ORDER_COMPLETED ", "ORDER", "CONTACT", "NULL"));
        await ExpectSqlFailureAsync("invalid SourceType rejected", BaseRawInsert("raw_vocab_3", "raw_vocab_e3", "ORDER_COMPLETED", "order", "CONTACT", "NULL"));
        await ExpectSqlFailureAsync("invalid BuyerRefType rejected", BaseRawInsert("raw_vocab_4", "raw_vocab_e4", "ORDER_COMPLETED", "ORDER", "contact", "NULL"));
        await ExpectSqlFailureAsync("ORDER mapping rejected", BaseRawInsert("raw_map_1", "raw_map_e1", "EXTERNAL_PURCHASE_CONFIRMED", "ORDER", "CONTACT", "NULL"));
        await ExpectSqlFailureAsync("ORDER SourceSystem rejected", BaseRawInsert("raw_map_2", "raw_map_e2", "ORDER_COMPLETED", "ORDER", "CONTACT", "N'external'"));
    }

    private async Task VerifyAppendReplayAndReadAsync(
        ServiceProvider provider,
        ControlledIdGenerator ids,
        MutablePolicyVersionProvider policy)
    {
        var workspace = Trusted("workspace_primary");
        var occurredAt = new DateTimeOffset(2026, 8, 20, 9, 10, 11, TimeSpan.FromHours(7)).AddTicks(2345);
        var expectedUtc = occurredAt.ToUniversalTime();
        ids.EnqueueEvidence("pe_primary_server_generated");
        var first = await AppendAsync(provider, Intent(workspace, "order_primary", "contact_primary", occurredAt, "corr-original"));
        Check("new Order fact APPENDED", PurchaseEvidenceAppendOutcome.Appended, first.Outcome);
        Check("server evidenceId returned", "pe_primary_server_generated", first.EvidenceId);
        Check("evidenceId differs from orderId", false, first.EvidenceId == "order_primary");
        Check("one PurchaseEvidence row", 1L, await CountEvidenceAsync("workspace_primary", "order_primary"));
        Check("stored EvidenceType", "ORDER_COMPLETED", await EvidenceValueAsync("EvidenceType", first.EvidenceId, "workspace_primary"));
        Check("stored SourceType", "ORDER", await EvidenceValueAsync("SourceType", first.EvidenceId, "workspace_primary"));
        Check("stored SourceId", "order_primary", await EvidenceValueAsync("SourceId", first.EvidenceId, "workspace_primary"));
        Check("stored SourceSystem null", null, await EvidenceValueAsync("SourceSystem", first.EvidenceId, "workspace_primary"));
        Check("stored BuyerRefType", "CONTACT", await EvidenceValueAsync("BuyerRefType", first.EvidenceId, "workspace_primary"));
        Check("stored BuyerRefId", "contact_primary", await EvidenceValueAsync("BuyerRefId", first.EvidenceId, "workspace_primary"));
        Check("stored occurredAt canonical UTC", expectedUtc, await EvidenceDateAsync("OccurredAt", first.EvidenceId, "workspace_primary"));
        Check("policy assigned by owner", "policy-A", await EvidenceValueAsync("PolicyVersion", first.EvidenceId, "workspace_primary"));
        Check("initial correlation stored", "corr-original", await EvidenceValueAsync("CorrelationId", first.EvidenceId, "workspace_primary"));
        Check("one success audit", 1L, await CountAuditAsync("workspace_primary", first.EvidenceId));
        Check("audit operation", "ORIGINAL_APPEND", await AuditValueAsync("Operation", first.EvidenceId, "workspace_primary"));
        Check("audit correlation", "corr-original", await AuditValueAsync("CorrelationId", first.EvidenceId, "workspace_primary"));
        Check("audit policy", "policy-A", await AuditValueAsync("PolicyVersion", first.EvidenceId, "workspace_primary"));
        Check("audit time present", true, await AuditDateAsync(first.EvidenceId, "workspace_primary") is not null);

        var replay = await AppendAsync(provider, Intent(workspace, "order_primary", "contact_primary", occurredAt, "corr-original"));
        Check("identical source replay", PurchaseEvidenceAppendOutcome.Replayed, replay.Outcome);
        Check("replay returns canonical evidenceId", first.EvidenceId, replay.EvidenceId);
        Check("replay row count remains one", 1L, await CountEvidenceAsync("workspace_primary", "order_primary"));
        Check("replay has no second audit", 1L, await CountAuditAsync("workspace_primary", first.EvidenceId));

        var correlationReplay = await AppendAsync(provider, Intent(workspace, "order_primary", "contact_primary", occurredAt, "corr-retry"));
        Check("correlation-only retry replayed", PurchaseEvidenceAppendOutcome.Replayed, correlationReplay.Outcome);
        Check("correlation-only retry preserves original", "corr-original", await EvidenceValueAsync("CorrelationId", first.EvidenceId, "workspace_primary"));
        Check("correlation-only retry adds no audit", 1L, await CountAuditAsync("workspace_primary", first.EvidenceId));

        policy.Current = "policy-B";
        var policyReplay = await AppendAsync(provider, Intent(workspace, "order_primary", "contact_primary", occurredAt, "corr-policy-B"));
        Check("new current policy still replays", PurchaseEvidenceAppendOutcome.Replayed, policyReplay.Outcome);
        Check("policy replay preserves recorded policy", "policy-A", await EvidenceValueAsync("PolicyVersion", first.EvidenceId, "workspace_primary"));
        Check("policy replay adds no audit", 1L, await CountAuditAsync("workspace_primary", first.EvidenceId));

        var buyerConflict = await AppendAsync(provider, Intent(workspace, "order_primary", "contact_changed", occurredAt, "corr-conflict-buyer"));
        Check("changed BuyerRef conflicts", PurchaseEvidenceAppendOutcome.Conflict, buyerConflict.Outcome);
        var timeConflict = await AppendAsync(provider, Intent(workspace, "order_primary", "contact_primary", occurredAt.AddTicks(1), "corr-conflict-time"));
        Check("changed occurredAt conflicts", PurchaseEvidenceAppendOutcome.Conflict, timeConflict.Outcome);
        Check("conflicts preserve one row", 1L, await CountEvidenceAsync("workspace_primary", "order_primary"));
        Check("conflicts preserve original BuyerRef", "contact_primary", await EvidenceValueAsync("BuyerRefId", first.EvidenceId, "workspace_primary"));
        Check("conflicts add no audit", 1L, await CountAuditAsync("workspace_primary", first.EvidenceId));

        await using var scope = provider.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IEffectivePurchaseEvidenceReader>();
        var snapshot = await reader.GetByIdAsync(workspace, first.EvidenceId, CancellationToken.None);
        Check("effective read returns original", true, snapshot is not null);
        Check("effective snapshot evidenceId", first.EvidenceId, snapshot?.EvidenceId);
        Check("effective snapshot policy", "policy-A", snapshot?.PolicyVersion);
        Check("effective snapshot BuyerRef", "contact_primary", snapshot?.BuyerRef.Id);
        Check("unknown effective read returns null", null, await reader.GetByIdAsync(workspace, "pe_unknown", CancellationToken.None));
        Check("foreign Workspace effective read returns null", null, await reader.GetByIdAsync(Trusted("workspace_foreign"), first.EvidenceId, CancellationToken.None));
    }

    private async Task VerifyExactSourceEqualityAsync(ServiceProvider provider)
    {
        var workspace = Trusted("workspace_exact");
        var time = Utc(1);
        var lower = await AppendAsync(provider, Intent(workspace, "order_case", "contact_case", time, "corr-case-1"));
        var upper = await AppendAsync(provider, Intent(workspace, "ORDER_CASE", "contact_case", time, "corr-case-2"));
        Check("case-distinct source one appended", PurchaseEvidenceAppendOutcome.Appended, lower.Outcome);
        Check("case-distinct source two appended", PurchaseEvidenceAppendOutcome.Appended, upper.Outcome);
        Check("binary application/database equality agrees", 2L, await ScalarLongAsync(
            "SELECT COUNT(*) FROM commercial_evidence.PurchaseEvidence WHERE WorkspaceId=N'workspace_exact'"));
    }

    private async Task VerifyConcurrentReplayAsync(ServiceProvider provider)
    {
        var workspace = Trusted("workspace_race_same");
        var intent = Intent(workspace, "order_race_same", "contact_race", Utc(2), "corr-race-same");
        var results = await Task.WhenAll(AppendAsync(provider, intent), AppendAsync(provider, intent));
        Check("concurrent identical exactly one APPENDED", 1, results.Count(item => item.Outcome == PurchaseEvidenceAppendOutcome.Appended));
        Check("concurrent identical exactly one REPLAYED", 1, results.Count(item => item.Outcome == PurchaseEvidenceAppendOutcome.Replayed));
        Check("concurrent identical resolves same evidenceId", 1, results.Select(item => item.EvidenceId).Distinct(StringComparer.Ordinal).Count());
        Check("concurrent identical one row", 1L, await CountEvidenceAsync("workspace_race_same", "order_race_same"));
        Check("concurrent identical one audit", 1L, await ScalarLongAsync(
            "SELECT COUNT(*) FROM commercial_evidence.AuditRecords WHERE WorkspaceId=N'workspace_race_same'"));
    }

    private async Task VerifyConcurrentConflictAsync(ServiceProvider provider)
    {
        var workspace = Trusted("workspace_race_changed");
        var time = Utc(3);
        var results = await Task.WhenAll(
            AppendAsync(provider, Intent(workspace, "order_race_changed", "contact_race_A", time, "corr-race-A")),
            AppendAsync(provider, Intent(workspace, "order_race_changed", "contact_race_B", time, "corr-race-B")));
        Check("concurrent changed exactly one APPENDED", 1, results.Count(item => item.Outcome == PurchaseEvidenceAppendOutcome.Appended));
        Check("concurrent changed exactly one CONFLICT", 1, results.Count(item => item.Outcome == PurchaseEvidenceAppendOutcome.Conflict));
        Check("concurrent changed one row", 1L, await CountEvidenceAsync("workspace_race_changed", "order_race_changed"));
        Check("concurrent changed one audit", 1L, await ScalarLongAsync(
            "SELECT COUNT(*) FROM commercial_evidence.AuditRecords WHERE WorkspaceId=N'workspace_race_changed'"));
    }

    private async Task VerifyAggregateIdCollisionAsync(ServiceProvider provider, ControlledIdGenerator ids)
    {
        var workspace = Trusted("workspace_collision");
        ids.EnqueueEvidence("pe_forced_collision");
        var original = await AppendAsync(provider, Intent(workspace, "order_collision_original", "contact_collision", Utc(4), "corr-collision-1"));
        ids.EnqueueEvidence("pe_forced_collision");
        ids.EnqueueEvidence("pe_after_collision");
        var second = await AppendAsync(provider, Intent(workspace, "order_collision_second", "contact_collision", Utc(5), "corr-collision-2"));
        Check("forced aggregate collision first ID", "pe_forced_collision", original.EvidenceId);
        Check("aggregate collision is not replay", PurchaseEvidenceAppendOutcome.Appended, second.Outcome);
        Check("aggregate collision reallocated ID", "pe_after_collision", second.EvidenceId);
        Check("unrelated collided record unchanged", "order_collision_original", await EvidenceValueAsync("SourceId", original.EvidenceId, "workspace_collision"));
        Check("collision branch produced two rows", 2L, await ScalarLongAsync(
            "SELECT COUNT(*) FROM commercial_evidence.PurchaseEvidence WHERE WorkspaceId=N'workspace_collision'"));
    }

    private async Task VerifyWorkspaceQualifiedIdentityAsync(ServiceProvider provider, ControlledIdGenerator ids)
    {
        ids.EnqueueEvidence("pe_shared_workspace_local");
        var left = await AppendAsync(provider, Intent(Trusted("workspace_left"), "order_shared", "contact_shared", Utc(6), "corr-left"));
        ids.EnqueueEvidence("pe_shared_workspace_local");
        var right = await AppendAsync(provider, Intent(Trusted("workspace_right"), "order_shared", "contact_shared", Utc(6), "corr-right"));
        Check("same source across Workspaces appended left", PurchaseEvidenceAppendOutcome.Appended, left.Outcome);
        Check("same source across Workspaces appended right", PurchaseEvidenceAppendOutcome.Appended, right.Outcome);
        Check("same evidenceId across Workspaces allowed", left.EvidenceId, right.EvidenceId);
        Check("composite aggregate stores both", 2L, await ScalarLongAsync(
            "SELECT COUNT(*) FROM commercial_evidence.PurchaseEvidence WHERE EvidenceId=N'pe_shared_workspace_local'"));
    }

    private async Task VerifyAuditAtomicityAsync(ServiceProvider provider)
    {
        await ExecuteAsync("""
            CREATE TRIGGER commercial_evidence.TR_VerifierRejectAudit
            ON commercial_evidence.AuditRecords
            INSTEAD OF INSERT
            AS
            THROW 51000, 'Verifier-injected audit failure', 1;
            """);
        try
        {
            await ExpectFailureAsync(
                "audit failure closes append",
                () => AppendAsync(provider, Intent(Trusted("workspace_atomic"), "order_atomic", "contact_atomic", Utc(7), "corr-atomic")));
        }
        finally
        {
            await ExecuteAsync("DROP TRIGGER commercial_evidence.TR_VerifierRejectAudit");
        }

        Check("audit failure leaves no evidence", 0L, await ScalarLongAsync(
            "SELECT COUNT(*) FROM commercial_evidence.PurchaseEvidence WHERE WorkspaceId=N'workspace_atomic'"));
        Check("audit failure leaves no audit", 0L, await ScalarLongAsync(
            "SELECT COUNT(*) FROM commercial_evidence.AuditRecords WHERE WorkspaceId=N'workspace_atomic'"));
    }

    private void VerifyCallableSurface()
    {
        var assembly = typeof(IOrderCompletedPurchaseEvidenceAppender).Assembly;
        var publicTypes = assembly.GetExportedTypes();
        var intentProperties = typeof(AppendOrderCompletedPurchaseEvidenceIntent)
            .GetProperties()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);
        Check("only Order original append interface exported", 1,
            publicTypes.Count(type => type.IsInterface && type.Name.Contains("Appender", StringComparison.Ordinal)));
        Check("append intent has no evidenceId override", false, intentProperties.Contains("EvidenceId"));
        Check("append intent has no policyVersion override", false, intentProperties.Contains("PolicyVersion"));
        Check("append intent has no sourceType override", false, intentProperties.Contains("SourceType"));
        Check("append intent has no evidenceType override", false, intentProperties.Contains("EvidenceType"));
        Check("no external append surface", false,
            publicTypes.Any(type => type.Name.Contains("ExternalPurchase", StringComparison.Ordinal)));
        Check("no historical append surface", false,
            publicTypes.Any(type => type.Name.Contains("HistoricalPurchase", StringComparison.Ordinal)));
        Check("no reversal append surface", false,
            publicTypes.Any(type => type.Name.Contains("Reversal", StringComparison.Ordinal)));
        Check("effective reader is CommercialEvidence-specific", true,
            publicTypes.Any(type => type == typeof(IEffectivePurchaseEvidenceReader)));
        Check("effective snapshot is not persistence entity", false,
            typeof(EffectivePurchaseEvidenceSnapshot).GetProperties().Any(property =>
                typeof(DbContext).IsAssignableFrom(property.PropertyType)
                || typeof(IQueryable).IsAssignableFrom(property.PropertyType)));
        Check("original evidence has no status field", false,
            typeof(CommercialEvidenceDbContext).Assembly
                .GetType("UnicoreCRM.CommercialEvidence.CommercialEvidence.Domain.PurchaseEvidence")!
                .GetProperties()
                .Any(property => property.Name is "Status" or "Effective" or "Reversed"));
    }

    private async Task<AppendPurchaseEvidenceResult> AppendAsync(
        ServiceProvider provider,
        AppendOrderCompletedPurchaseEvidenceIntent intent)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<IOrderCompletedPurchaseEvidenceAppender>()
            .AppendAsync(intent, CancellationToken.None);
    }

    private static AppendOrderCompletedPurchaseEvidenceIntent Intent(
        TrustedWorkspaceContext workspace,
        string orderId,
        string buyerId,
        DateTimeOffset occurredAt,
        string correlationId) =>
        new(workspace, orderId, new(PurchaseEvidenceBuyerRefType.Contact, buyerId), occurredAt, correlationId);

    private static TrustedWorkspaceContext Trusted(string workspaceId) =>
        new(workspaceId, $"account_{workspaceId}", $"member_{workspaceId}", $"membership_{workspaceId}");

    private static DateTimeOffset Utc(int minute) =>
        new DateTimeOffset(2026, 8, 29, 12, minute, 0, TimeSpan.Zero).AddTicks(minute);

    private async Task RecreateDatabaseAsync()
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        var databaseName = builder.InitialCatalog;
        if (string.IsNullOrWhiteSpace(databaseName))
            throw new ArgumentException("The verifier connection string must name an isolated database.");
        builder.InitialCatalog = "master";
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"IF DB_ID(@database) IS NOT NULL BEGIN ALTER DATABASE [{EscapeIdentifier(databaseName)}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{EscapeIdentifier(databaseName)}]; END; CREATE DATABASE [{EscapeIdentifier(databaseName)}];";
        command.Parameters.AddWithValue("@database", databaseName);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<long> CountEvidenceAsync(string workspaceId, string orderId) =>
        await ScalarLongAsync(
            "SELECT COUNT(*) FROM commercial_evidence.PurchaseEvidence WHERE WorkspaceId=@workspace AND SourceType=N'ORDER' AND SourceSystem IS NULL AND SourceId=@source",
            ("@workspace", workspaceId), ("@source", orderId));

    private async Task<long> CountAuditAsync(string workspaceId, string evidenceId) =>
        await ScalarLongAsync(
            "SELECT COUNT(*) FROM commercial_evidence.AuditRecords WHERE WorkspaceId=@workspace AND EvidenceId=@evidence",
            ("@workspace", workspaceId), ("@evidence", evidenceId));

    private async Task<string?> EvidenceValueAsync(string column, string evidenceId, string workspaceId) =>
        await ScalarStringAsync(
            $"SELECT [{EscapeIdentifier(column)}] FROM commercial_evidence.PurchaseEvidence WHERE WorkspaceId=@workspace AND EvidenceId=@evidence",
            ("@workspace", workspaceId), ("@evidence", evidenceId));

    private async Task<DateTimeOffset> EvidenceDateAsync(string column, string evidenceId, string workspaceId) =>
        (DateTimeOffset)(await ScalarAsync(
            $"SELECT [{EscapeIdentifier(column)}] FROM commercial_evidence.PurchaseEvidence WHERE WorkspaceId=@workspace AND EvidenceId=@evidence",
            ("@workspace", workspaceId), ("@evidence", evidenceId))
            ?? throw new InvalidOperationException("Expected evidence timestamp."));

    private async Task<string?> AuditValueAsync(string column, string evidenceId, string workspaceId) =>
        await ScalarStringAsync(
            $"SELECT [{EscapeIdentifier(column)}] FROM commercial_evidence.AuditRecords WHERE WorkspaceId=@workspace AND EvidenceId=@evidence",
            ("@workspace", workspaceId), ("@evidence", evidenceId));

    private async Task<DateTimeOffset?> AuditDateAsync(string evidenceId, string workspaceId) =>
        (DateTimeOffset?)await ScalarAsync(
            "SELECT OccurredAt FROM commercial_evidence.AuditRecords WHERE WorkspaceId=@workspace AND EvidenceId=@evidence",
            ("@workspace", workspaceId), ("@evidence", evidenceId));

    private async Task<long> ScalarLongAsync(string sql, params (string Name, object Value)[] parameters) =>
        Convert.ToInt64(await ScalarAsync(sql, parameters));

    private async Task<string?> ScalarStringAsync(string sql, params (string Name, object Value)[] parameters)
    {
        var value = await ScalarAsync(sql, parameters);
        return value is null or DBNull ? null : Convert.ToString(value);
    }

    private async Task<object?> ScalarAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        return await command.ExecuteScalarAsync();
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private async Task ExpectSqlFailureAsync(string name, string sql)
    {
        try
        {
            await ExecuteAsync(sql);
            Check(name, true, false);
        }
        catch (SqlException)
        {
            Check(name, true, true);
        }
    }

    private async Task ExpectFailureAsync(string name, Func<Task> action)
    {
        try
        {
            await action();
            Check(name, true, false);
        }
        catch (DbUpdateException)
        {
            Check(name, true, true);
        }
    }

    private static string BaseRawInsert(
        string workspaceId,
        string evidenceId,
        string evidenceType,
        string sourceType,
        string buyerRefType,
        string sourceSystem) => $"""
        INSERT INTO commercial_evidence.PurchaseEvidence
        (WorkspaceId,EvidenceId,EvidenceType,BuyerRefType,BuyerRefId,SourceType,SourceSystem,SourceId,OccurredAt,PolicyVersion,CorrelationId)
        VALUES
        (N'{workspaceId}',N'{evidenceId}',N'{evidenceType}',N'{buyerRefType}',N'buyer_raw',N'{sourceType}',{sourceSystem},N'source_{evidenceId}',SYSUTCDATETIME(),N'policy-raw',N'corr-raw')
        """;

    private static string EscapeIdentifier(string value) => value.Replace("]", "]]", StringComparison.Ordinal);

    private void Check<T>(string name, T expected, T actual)
    {
        if (EqualityComparer<T>.Default.Equals(expected, actual))
        {
            passed++;
            results.Add($"PASS | {name} | {actual}");
        }
        else
        {
            failed++;
            results.Add($"FAIL | {name} | expected={expected} actual={actual}");
        }
    }
}

internal sealed class ControlledIdGenerator : IPurchaseEvidenceIdGenerator
{
    private readonly ConcurrentQueue<string> evidenceIds = new();

    internal void EnqueueEvidence(string evidenceId) => evidenceIds.Enqueue(evidenceId);

    public string NewEvidenceId() =>
        evidenceIds.TryDequeue(out var evidenceId) ? evidenceId : $"pe_{Guid.NewGuid():N}";

    public string NewAuditId() => $"ceaudit_{Guid.NewGuid():N}";
}

internal sealed class MutablePolicyVersionProvider(string current) : ICommercialEvidencePolicyVersionProvider
{
    public string Current { get; set; } = current;
}

internal sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;
}
