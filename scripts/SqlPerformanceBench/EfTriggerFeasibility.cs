using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using UnicoreCRM.Crm.Contacts.Domain;
using UnicoreCRM.Crm.Contacts.Infrastructure.Persistence;

// Isolated feasibility only. Never registered in production DI or migrations.
internal static class EfTriggerFeasibility
{
    private const string Table = "contacts.BenchmarkEfSearchProjection";
    private const string Trigger = "contacts.BenchmarkEfSearchSync";
    private static readonly string[] Fields = ["fullName", "displayName", "workEmail", "personalEmail", "mobilePhone", "workPhone", "otherPhone"];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static string Columns => string.Join(",", Fields.Select(f => $"[{f}]"));
    private static string Value(string alias, string field) => field == "fullName" ? $"UPPER({alias}.FullName)" : $"UPPER(JSON_VALUE({alias}.Profile,'$.{field}'))";
    private static string Values(string alias) => string.Join(",", Fields.Select(f => Value(alias, f)));

    internal static async Task Run(ContactsDbContext canonical, SqlConnection fixture, Func<string, object, Task> save, Func<Task> guard)
    {
        using var ownership = new SqlCommand("SELECT DB_NAME(),CONVERT(nvarchar(128),value) FROM sys.extended_properties WHERE class=0 AND name=N'UniCoreCRM_PerfBench_RunId'", fixture);
        string run;
        await using (var reader = await ownership.ExecuteReaderAsync())
        {
            if (!await reader.ReadAsync() || !Guid.TryParseExact(reader.GetString(1), "N", out _)
                || reader.GetString(0) != "UniCoreCRM_PerfBench_" + reader.GetString(1)) throw new InvalidOperationException("Owned GUID fixture required");
            run = reader.GetString(1);
        }
        if (Convert.ToInt32(await Scalar(fixture, $"SELECT CASE WHEN OBJECT_ID('{Table}') IS NOT NULL OR OBJECT_ID('{Trigger}') IS NOT NULL THEN 1 ELSE 0 END")) != 0)
            throw new InvalidOperationException("Existing prototype object: refuse mutation or cleanup");
        var workspace = "bench_ef_" + run;
        var commands = new SqlCapture();
        var lifecycle = new List<object>();
        var writes = new List<object>();
        var installedTable = false;
        var installedTrigger = false;
        DbContext Context(bool compatible) => compatible
            ? new CompatibleContext(new DbContextOptionsBuilder<CompatibleContext>().UseSqlServer(fixture.ConnectionString).AddInterceptors(commands).Options, canonical)
            : new ContactsDbContext(new DbContextOptionsBuilder<ContactsDbContext>().UseSqlServer(fixture.ConnectionString).AddInterceptors(commands).Options);
        try
        {
            await guard();
            // First establish canonical baseline before any trigger exists.
            commands.Label = "canonical-without-trigger";
            await Lifecycle(Context, false, false, workspace, fixture, lifecycle);
            await save("ef-trigger-lifecycle.json", lifecycle);
            await WriteBench(Context, "canonical-without-trigger", false, false, false, workspace, fixture, writes, guard, save);
            await WriteBench(Context, "raw-without-trigger", false, true, false, workspace, fixture, writes, guard, save);
            await guard();
            var collations = new Dictionary<string, string>();
            using (var command = new SqlCommand("SELECT name,collation_name FROM sys.columns WHERE object_id=OBJECT_ID('contacts.Contacts') AND name IN ('WorkspaceId','ContactId','FullName','Profile')", fixture))
            await using (var reader = await command.ExecuteReaderAsync())
                while (await reader.ReadAsync())
                {
                    var collation = reader.GetString(1);
                    if (!Regex.IsMatch(collation, @"\A[A-Za-z0-9_]+\z")) throw new InvalidOperationException("Unsafe collation identifier");
                    collations.Add(reader.GetString(0), collation);
                }
            await Exec(fixture, $"CREATE TABLE {Table}(WorkspaceId nvarchar(128) COLLATE {collations["WorkspaceId"]} NOT NULL,ContactId nvarchar(128) COLLATE {collations["ContactId"]} NOT NULL,SourceVersion bigint NOT NULL," +
                string.Join(",", Fields.Select(f => $"[{f}] nvarchar(4000) COLLATE {collations[f == "fullName" ? "FullName" : "Profile"]} NULL")) +
                ",CONSTRAINT PK_BenchmarkEfSearchProjection PRIMARY KEY(WorkspaceId,ContactId),CONSTRAINT FK_BenchmarkEfSearchProjection FOREIGN KEY(WorkspaceId,ContactId) REFERENCES contacts.Contacts(WorkspaceId,ContactId) ON DELETE CASCADE)");
            installedTable = true;
            await Exec(fixture, TriggerSql(workspace, false));
            installedTrigger = true;
            await save("ef-trigger-prototype-schema.json", new { createScope = "Only synthetic workspace; seven SQL UPPER/JSON_VALUE values, original column collations, SourceVersion, FK cascade", collations, trigger = TriggerSql(workspace, false), productionChanged = false });
            await guard();
            commands.Label = "canonical-with-trigger";
            await CanonicalError334(Context, workspace, fixture, lifecycle);
            await save("ef-trigger-lifecycle.json", lifecycle);
            commands.Label = "compatible-with-trigger";
            await Lifecycle(Context, true, true, workspace, fixture, lifecycle);
            await save("ef-trigger-lifecycle.json", lifecycle);
            await WriteBench(Context, "raw-with-trigger", true, true, false, workspace, fixture, writes, guard, save);
            await WriteBench(Context, "compatible-with-trigger", true, false, false, workspace, fixture, writes, guard, save);
            await guard();
            await Exec(fixture, "DROP TRIGGER " + Trigger);
            installedTrigger = false;
            await Exec(fixture, TriggerSql(workspace, true));
            installedTrigger = true;
            commands.Label = "compatible-with-conditional-trigger";
            await Lifecycle(Context, true, true, workspace, fixture, lifecycle, "compatible-with-conditional-trigger");
            await WriteBench(Context, "compatible-with-conditional-trigger", true, false, true, workspace, fixture, writes, guard, save);
            await save("ef-trigger-generated-sql.json", commands.Statements.Select(x => new { mode = x.Key, sql = x.Value,
                containsOutput = x.Value.Contains("OUTPUT", StringComparison.OrdinalIgnoreCase), containsRowCount = x.Value.Contains("@@ROWCOUNT", StringComparison.OrdinalIgnoreCase) }).ToArray());
            await save("ef-trigger-result.json", new { status = "FEASIBLE_IN_ISOLATED_PERSISTENCE", publicWriterCompatibility = "PARTIAL_NOT_EXECUTED", productionPromotion = "NOT_ADMITTED",
                provider = typeof(SqlServerDbContextOptionsExtensions).Assembly.GetName().Version?.ToString(), modelConfiguration = "Canonical OnModelCreating invoked; only isolated context sets Contact UseSqlOutputClause(false)",
                lifecycle, writes, validationScope = "Domain Contact.ApplyPatch rejection, not public validation/authentication pipeline", transactionScope = "SERIALIZABLE Contact/projection rollback; public audit/outbox/idempotency not executed",
                publicWriterInventory = new[] { "CreateContact: aggregate constructor, SERIALIZABLE transaction, audit/outbox/idempotency", "UpdateContact: ApplyPatch, Version concurrency token, audit/outbox/idempotency", "ArchiveContact: Archive, Version concurrency token, audit/outbox/idempotency", "ResolveQualificationContact: Contact create/normalized email range locks/conversion/audit/outbox; called by Lead qualification", "Relationships handlers: RecordRelationshipMutation advances Contact Version alongside relationship changes" },
                remainingRisks = new[] { "Public handlers and Lead distributed participant transaction paths are not run", "Compatible context copies canonical mapping by reflection for fixture only; production requires deliberate canonical model strategy", "No migration/backfill/deployment proof", "EF compatibility does not resolve previous read regressions or admit search projection" } });
        }
        catch (Exception error)
        {
            await save("ef-trigger-result.json", new { status = "PARTIAL_OR_FAILED", exceptionType = error.GetType().Name, sqlError = SqlError(error), lifecycle, writes,
                productionPromotion = "NOT_ADMITTED", publicWriterCompatibility = "NOT_EXECUTED" });
            throw;
        }
        finally
        {
            await save("ef-trigger-generated-sql.json", commands.Statements.Select(x => new { mode = x.Key, sql = x.Value,
                containsOutput = x.Value.Contains("OUTPUT", StringComparison.OrdinalIgnoreCase), containsRowCount = x.Value.Contains("@@ROWCOUNT", StringComparison.OrdinalIgnoreCase) }).ToArray());
            // All names were checked absent before this task created them. Do not drop any other object.
            canonical.ChangeTracker.Clear();
            await Exec(fixture, "DELETE contacts.Contacts WHERE WorkspaceId=@workspace", null, new SqlParameter("@workspace", workspace));
            if (installedTrigger) await Exec(fixture, "DROP TRIGGER " + Trigger);
            if (installedTable) await Exec(fixture, "DROP TABLE " + Table);
            await save("ef-trigger-teardown.json", new { status = "REMOVED_OWN_SYNTHETIC_ROWS_AND_OBJECTS", databaseCreatedOrDropped = false, productionChanged = false });
        }
    }

    private static async Task Lifecycle(Func<bool, DbContext> context, bool compatible, bool projection, string workspace, SqlConnection fixture, List<object> results, string? label = null)
    {
        var checks = new List<string>();
        await using var db = context(compatible);
        var entity = NewContact(workspace);
        db.Add(entity);
        var rows = await db.SaveChangesAsync();
        Assert(rows == 1, "Create affected rows");
        await Verify(fixture, entity, projection); checks.Add("create");
        foreach (var operation in new[] { "name", "profile", "work-email", "personal-email", "phone", "clear", "owner", "version" })
        {
            var oldVersion = entity.Version;
            Mutate(entity, operation, 1);
            Assert(entity.Version == oldVersion + 1, "Canonical mutation increments Version exactly once");
            rows = await db.SaveChangesAsync(); Assert(rows == 1, "Update affected rows");
            await Verify(fixture, entity, projection); checks.Add(operation);
        }
        var beforeValidation = entity.Version;
        try { entity.ApplyPatch(new ContactPatch(new HashSet<string> { "fullName" }, " ", null, new()), DateTimeOffset.UtcNow); throw new InvalidOperationException("Invalid patch accepted"); }
        catch (InvalidOperationException error) when (error.Message == "A supplied Contact name must be nonblank.") { }
        Assert(entity.Version == beforeValidation && await db.SaveChangesAsync() == 0, "Failed domain validation writes nothing");
        await Verify(fixture, entity, projection); checks.Add("failed-domain-validation");
        await using (var first = context(compatible))
        await using (var stale = context(compatible))
        {
            var id = entity.ContactId;
            var winner = await first.Set<Contact>().SingleAsync(c => c.ContactId == id);
            var loser = await stale.Set<Contact>().SingleAsync(c => c.ContactId == id);
            Mutate(winner, "name", 20); await first.SaveChangesAsync();
            Mutate(loser, "name", 21);
            var rejected = false;
            try { await stale.SaveChangesAsync(); } catch (DbUpdateConcurrencyException) { rejected = true; }
            Assert(rejected, "Stale EF version must fail");
            await Verify(fixture, winner, projection); checks.Add("stale-EF-concurrency");
        }
        db.ChangeTracker.Clear();
        var loadedId = entity.ContactId;
        entity = await db.Set<Contact>().SingleAsync(c => c.ContactId == loadedId);
        var rollbackVersion = entity.Version;
        var rollbackName = entity.FullName;
        var rollbackProfile = JsonSerializer.Serialize(entity.Profile, Json);
        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            Mutate(entity, "name", 100); await db.SaveChangesAsync();
            await tx.RollbackAsync();
        }
        db.ChangeTracker.Clear();
        entity = await db.Set<Contact>().SingleAsync(c => c.ContactId == loadedId);
        Assert(entity.Version == rollbackVersion && entity.FullName == rollbackName
            && JsonSerializer.Serialize(entity.Profile, Json) == rollbackProfile, "Rollback restores Version and authoritative facts");
        await Verify(fixture, entity, projection); checks.Add("serializable-transaction-rollback");
        var archiveVersion = entity.Version;
        entity.Archive(DateTimeOffset.UtcNow); await db.SaveChangesAsync();
        Assert(entity.Version == archiveVersion + 1 && entity.ArchivedAt != null && entity.Status == "archived", "Archive invariants");
        await Verify(fixture, entity, projection); checks.Add("archive");
        results.Add(new { mode = label ?? (compatible ? "compatible-with-trigger" : "canonical-without-trigger"), status = "PASS", checks,
            projection, finalVersion = entity.Version, scope = "Real aggregate methods and EF SaveChanges, not public commands" });
        await Exec(fixture, "DELETE contacts.Contacts WHERE ContactId=@id AND WorkspaceId=@workspace", null, new SqlParameter("@id", entity.ContactId), new SqlParameter("@workspace", workspace));
        if (projection) Assert(Convert.ToInt64(await Scalar(fixture, $"SELECT COUNT_BIG(*) FROM {Table} WHERE ContactId=@id AND WorkspaceId=@workspace", null,
            new SqlParameter("@id", entity.ContactId), new SqlParameter("@workspace", workspace))) == 0, "Deleted Contact must have no projection row");
    }

    private static async Task CanonicalError334(Func<bool, DbContext> context, string workspace, SqlConnection fixture, List<object> results)
    {
        await using var db = context(false);
        var entity = NewContact(workspace);
        db.Add(entity);
        int? createError = null;
        try { await db.SaveChangesAsync(); } catch (DbUpdateException error) { createError = SqlError(error); }
        if (createError != null)
        {
            Assert(createError == 334, "Canonical create failed for unexpected reason");
            results.Add(new { mode = "canonical-with-trigger", create = "FAIL", createError, update = "NOT_RUN", archive = "NOT_RUN" });
            db.ChangeTracker.Clear();
            return;
        }
        await Verify(fixture, entity, true);
        Mutate(entity, "name", 1);
        int? updateError = null;
        try { await db.SaveChangesAsync(); } catch (DbUpdateException error) { updateError = SqlError(error); }
        Assert(updateError == 334, "Expected canonical SQL OUTPUT/trigger error 334 was not reproduced");
        db.ChangeTracker.Clear();
        var id = entity.ContactId;
        var unchanged = await db.Set<Contact>().SingleAsync(c => c.ContactId == id);
        Assert(unchanged.Version == 0, "334 must leave authoritative row unchanged");
        await Verify(fixture, unchanged, true);
        unchanged.Archive(DateTimeOffset.UtcNow);
        int? archiveError = null;
        try { await db.SaveChangesAsync(); } catch (DbUpdateException error) { archiveError = SqlError(error); }
        Assert(archiveError == 334, "Expected canonical archive error 334");
        db.ChangeTracker.Clear();
        var afterArchiveFailure = await db.Set<Contact>().SingleAsync(c => c.ContactId == id);
        Assert(afterArchiveFailure.Version == 0 && afterArchiveFailure.ArchivedAt == null && afterArchiveFailure.Status == "active", "334 archive must leave row unchanged");
        await Verify(fixture, afterArchiveFailure, true);
        results.Add(new { mode = "canonical-with-trigger", create = "PASS", update = "FAIL_EXPECTED_334", updateError,
            archive = "FAIL_EXPECTED_334", archiveError, failedMutationRollback = "PASS" });
        db.ChangeTracker.Clear();
        await Exec(fixture, "DELETE contacts.Contacts WHERE ContactId=@id AND WorkspaceId=@workspace", null, new SqlParameter("@id", id), new SqlParameter("@workspace", workspace));
    }

    private static Contact NewContact(string workspace) => new(workspace, "member_1", "Synthetic EF original", "active",
        new ContactProfile { DisplayName = "Synthetic display", WorkEmail = "Initial@example.invalid", PersonalEmail = "Personal@example.invalid", MobilePhone = "000-original", WorkPhone = "111-original", OtherPhone = "222-original" }, DateTimeOffset.Parse("2026-01-01T00:00:00Z"));

    private static void Mutate(Contact entity, string operation, int sequence)
    {
        var now = DateTimeOffset.Parse("2026-10-09T00:00:00Z").AddSeconds(sequence);
        var fields = operation switch
        {
            "name" => new[] { "fullName" }, "profile" => ["displayName", "jobTitle"], "work-email" => ["workEmail"], "personal-email" => ["personalEmail"],
            "phone" => ["mobilePhone", "workPhone", "otherPhone"], "clear" => ["displayName", "workEmail", "personalEmail", "mobilePhone", "workPhone", "otherPhone"],
            "owner" => ["ownerId"], _ => Array.Empty<string>()
        };
        if (operation == "archive") { entity.Archive(now); return; }
        if (operation == "version") { entity.RecordRelationshipMutation(now); return; }
        if (operation == "status") throw new InvalidOperationException("Status-only has no admitted domain mutator; raw SQL experiment only");
        var profile = operation == "clear" ? new ContactProfile() : entity.Profile with
        {
            DisplayName = "Synthetic display " + sequence, JobTitle = "Synthetic job " + sequence,
            WorkEmail = $"Work{sequence}@example.invalid", PersonalEmail = $"Personal{sequence}@example.invalid",
            MobilePhone = "000-" + sequence, WorkPhone = "111-" + sequence, OtherPhone = "222-" + sequence
        };
        entity.ApplyPatch(new ContactPatch(new HashSet<string>(fields), "Synthetic name " + sequence, "member_2", profile), now);
    }

    private static async Task WriteBench(Func<bool, DbContext> context, string mode, bool projection, bool raw, bool conditional,
        string workspace, SqlConnection fixture, List<object> output, Func<Task> guard, Func<string, object, Task> save)
    {
        foreach (var operation in new[] { "create", "name", "work-email", "phone", "owner", "status-fixture-only", "version", "archive" })
        {
            await guard();
            var samples = new List<WriteSample>();
            for (var iteration = 0; iteration <= 20; iteration++)
            {
                await using var db = context(projection && !raw);
                var entity = NewContact(workspace);
                if (operation != "create") { db.Add(entity); await db.SaveChangesAsync(); }
                if (operation == "status-fixture-only")
                {
                    // No admitted standalone status mutator exists. This measures only
                    // an isolated EF property write, never a new business command.
                    db.Entry(entity).Property(nameof(Contact.Status)).CurrentValue = "inactive";
                    db.Entry(entity).Property(nameof(Contact.Version)).CurrentValue = entity.Version + 1;
                    db.Entry(entity).Property(nameof(Contact.UpdatedAt)).CurrentValue = DateTimeOffset.Parse("2026-10-09T00:00:00Z");
                }
                else if (operation != "create") Mutate(entity, operation, iteration + 1);
                var watch = Stopwatch.StartNew();
                long? bytes;
                int rows;
                if (raw)
                {
                    await using var tx = (SqlTransaction)await fixture.BeginTransactionAsync();
                    rows = await RawWrite(fixture, tx, entity, operation);
                    bytes = await LogBytes(fixture, tx);
                    await tx.CommitAsync();
                }
                else
                {
                    if (operation == "create") db.Add(entity);
                    await using var tx = await db.Database.BeginTransactionAsync();
                    rows = await db.SaveChangesAsync();
                    bytes = await LogBytes((SqlConnection)db.Database.GetDbConnection(), (SqlTransaction)tx.GetDbTransaction());
                    await tx.CommitAsync();
                }
                watch.Stop();
                Assert(rows == 1, "Measured write affected rows");
                await Verify(fixture, entity, projection);
                samples.Add(new WriteSample(iteration == 0, watch.Elapsed.TotalMilliseconds, bytes, rows));
                db.ChangeTracker.Clear();
                await Exec(fixture, "DELETE contacts.Contacts WHERE ContactId=@id AND WorkspaceId=@workspace", null,
                    new SqlParameter("@id", entity.ContactId), new SqlParameter("@workspace", workspace));
            }
            var measured = samples.Where(s => !s.Warmup).Select(s => s.TransactionMs).Order().ToArray();
            output.Add(new { mode, operation, conditional, p50 = measured[9], p95 = measured[18], p99 = measured[19], samples,
                scope = "Single Contact mutation including normalized email maintenance and explicit transaction commit; excludes public authorization/audit/outbox/idempotency. Includes log-byte instrumentation roundtrip. Log bytes measured before commit record, not total durable log or instance counters." });
            await save("ef-trigger-write-bench.json", output);
        }
    }
    private sealed record WriteSample(bool Warmup, double TransactionMs, long? LogBytesBeforeCommit, int AffectedRows);

    private static async Task<int> RawWrite(SqlConnection connection, SqlTransaction transaction, Contact entity, string operation)
    {
        var profile = JsonSerializer.Serialize(entity.Profile, Json);
        var set = operation switch
        {
            "name" => "FullName=@name", "work-email" => "Profile=@profile,NormalizedWorkEmail=@work", "phone" => "Profile=@profile",
            "owner" => "OwnerId=@owner", "status-fixture-only" => "Status=@status", "archive" => "Status=@status,ArchivedAt=@archived", "version" => "UpdatedAt=@updated", _ => ""
        };
        var sql = operation == "create"
            ? "INSERT contacts.Contacts(ContactId,WorkspaceId,OwnerId,FullName,Status,Profile,NormalizedWorkEmail,NormalizedPersonalEmail,Version,CreatedAt,UpdatedAt) VALUES(@id,@workspace,@owner,@name,@status,@profile,@work,@personal,@version,@created,@updated)"
            : $"UPDATE contacts.Contacts SET {set},Version=@version{(operation == "version" ? "" : ",UpdatedAt=@updated")} WHERE ContactId=@id AND WorkspaceId=@workspace AND Version=@oldVersion";
        using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddRange(new[] {
            new SqlParameter("@id",entity.ContactId),new SqlParameter("@workspace",entity.WorkspaceId),new SqlParameter("@owner",(object?)entity.OwnerId??DBNull.Value),
            new SqlParameter("@name",entity.FullName),new SqlParameter("@status",entity.Status),new SqlParameter("@profile",SqlDbType.NVarChar,-1){Value=profile},
            new SqlParameter("@work",(object?)entity.NormalizedWorkEmail??DBNull.Value),new SqlParameter("@personal",(object?)entity.NormalizedPersonalEmail??DBNull.Value),
            new SqlParameter("@version",entity.Version),new SqlParameter("@oldVersion",entity.Version-1),new SqlParameter("@created",entity.CreatedAt),
            new SqlParameter("@updated",entity.UpdatedAt),new SqlParameter("@archived",(object?)entity.ArchivedAt??DBNull.Value) });
        return await command.ExecuteNonQueryAsync();
    }

    private static async Task Verify(SqlConnection fixture, Contact expected, bool projection)
    {
        using var command = new SqlCommand("SELECT Version,NormalizedWorkEmail,NormalizedPersonalEmail,Status,ArchivedAt,FullName,OwnerId,Profile,CreatedAt,UpdatedAt FROM contacts.Contacts WHERE ContactId=@id AND WorkspaceId=@workspace", fixture);
        command.Parameters.AddWithValue("@id", expected.ContactId); command.Parameters.AddWithValue("@workspace", expected.WorkspaceId);
        await using (var reader = await command.ExecuteReaderAsync())
        {
            Assert(await reader.ReadAsync(), "Contact missing");
            Assert(reader.GetInt64(0) == expected.Version && Equals(reader.IsDBNull(1) ? null : reader.GetString(1), expected.NormalizedWorkEmail)
                && Equals(reader.IsDBNull(2) ? null : reader.GetString(2), expected.NormalizedPersonalEmail) && reader.GetString(3) == expected.Status
                && reader.IsDBNull(4) == (expected.ArchivedAt == null)
                && (reader.IsDBNull(4) || reader.GetFieldValue<DateTimeOffset>(4)==expected.ArchivedAt)
                && reader.GetFieldValue<DateTimeOffset>(8)==expected.CreatedAt && reader.GetFieldValue<DateTimeOffset>(9)==expected.UpdatedAt
                && reader.GetString(5) == expected.FullName && Equals(reader.IsDBNull(6) ? null : reader.GetString(6), expected.OwnerId)
                && JsonSerializer.Serialize(JsonSerializer.Deserialize<ContactProfile>(reader.GetString(7), Json), Json) == JsonSerializer.Serialize(expected.Profile, Json),
                "Version/lifecycle/normalized email identity/authoritative facts mismatch");
        }
        if (!projection) return;
        var mismatches = string.Join(" OR ", Fields.Select(f => Different($"p.[{f}]", Value("c", f))));
        var sql = $"SELECT COUNT_BIG(*) FROM contacts.Contacts c LEFT JOIN {Table} p ON p.WorkspaceId=c.WorkspaceId AND p.ContactId=c.ContactId WHERE c.WorkspaceId=@workspace AND (p.ContactId IS NULL OR p.SourceVersion<>c.Version OR {mismatches})";
        Assert(Convert.ToInt64(await Scalar(fixture, sql, null, new SqlParameter("@workspace", expected.WorkspaceId))) == 0, "Projection differs from authoritative SQL scalar facts or Version");
    }
    private static string Different(string left, string right) => $"({left} COLLATE Latin1_General_100_BIN2 <> {right} COLLATE Latin1_General_100_BIN2 OR ({left} IS NULL AND {right} IS NOT NULL) OR ({left} IS NOT NULL AND {right} IS NULL) OR ISNULL(DATALENGTH({left}),-1)<>ISNULL(DATALENGTH({right}),-1))";
    private static string TriggerSql(string workspace, bool conditional)
    {
        if (!Regex.IsMatch(workspace, @"\Abench_ef_[a-f0-9]{32}\z")) throw new InvalidOperationException("Unsafe workspace fixture identifier");
        var replace = $"DELETE p FROM {Table} p JOIN deleted d ON d.WorkspaceId=p.WorkspaceId AND d.ContactId=p.ContactId; INSERT {Table}(WorkspaceId,ContactId,SourceVersion,{Columns}) SELECT i.WorkspaceId,i.ContactId,i.Version,{Values("i")} FROM inserted i WHERE i.WorkspaceId=N'{workspace}';";
        if (conditional)
        {
            var changed = string.Join(" OR ", Fields.Select(f => Different($"p.[{f}]", Value("i", f))));
            replace = $"DELETE p FROM {Table} p JOIN deleted d ON d.WorkspaceId=p.WorkspaceId AND d.ContactId=p.ContactId WHERE NOT EXISTS(SELECT 1 FROM inserted i WHERE i.WorkspaceId=p.WorkspaceId AND i.ContactId=p.ContactId) OR EXISTS(SELECT 1 FROM inserted i WHERE i.WorkspaceId=p.WorkspaceId AND i.ContactId=p.ContactId AND ({changed})); " +
                $"UPDATE p SET SourceVersion=i.Version FROM {Table} p JOIN inserted i ON i.WorkspaceId=p.WorkspaceId AND i.ContactId=p.ContactId; " +
                $"INSERT {Table}(WorkspaceId,ContactId,SourceVersion,{Columns}) SELECT i.WorkspaceId,i.ContactId,i.Version,{Values("i")} FROM inserted i WHERE i.WorkspaceId=N'{workspace}' AND NOT EXISTS(SELECT 1 FROM {Table} p WHERE p.WorkspaceId=i.WorkspaceId AND p.ContactId=i.ContactId);";
        }
        return $"CREATE TRIGGER {Trigger} ON contacts.Contacts AFTER INSERT,UPDATE,DELETE AS BEGIN SET NOCOUNT ON; {replace} END";
    }

    private sealed class CompatibleContext(DbContextOptions<CompatibleContext> options, ContactsDbContext canonical) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            var mapping = typeof(ContactsDbContext).GetMethod("OnModelCreating", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Canonical mapping unavailable");
            mapping.Invoke(canonical, [builder]);
            builder.Entity<Contact>().ToTable("Contacts", "contacts", table => table.UseSqlOutputClause(false));
        }
    }
    private sealed class SqlCapture : DbCommandInterceptor
    {
        internal string Label { get; set; } = "baseline";
        internal Dictionary<string, string> Statements { get; } = new();
        private void Record(DbCommand command)
        {
            if (!command.CommandText.Contains("[contacts].[Contacts]", StringComparison.Ordinal) || !Regex.IsMatch(command.CommandText, @"\b(INSERT|UPDATE|DELETE)\b", RegexOptions.IgnoreCase)) return;
            var sql = Regex.Replace(command.CommandText, @"N?'(?:''|[^'])*'", "'<literal-redacted>'");
            // Retain each parameterized SQL shape once per mode, including archive,
            // profile and owner updates; never retain parameter values.
            if (!Statements.Any(x => x.Key.StartsWith(Label + ":", StringComparison.Ordinal) && x.Value == sql))
                Statements.Add(Label + ":" + (sql.Contains("INSERT", StringComparison.OrdinalIgnoreCase) ? "insert" : "update") + ":" + Statements.Count, sql);
        }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Record(command); return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { Record(command); return ValueTask.FromResult(result); }
    }
    private static async Task<long?> LogBytes(SqlConnection connection, SqlTransaction tx)
    {
        var value = await Scalar(connection, "SELECT dt.database_transaction_log_bytes_used FROM sys.dm_tran_session_transactions st JOIN sys.dm_tran_database_transactions dt ON st.transaction_id=dt.transaction_id WHERE st.session_id=@@SPID AND dt.database_id=DB_ID()", tx);
        return value == null || value == DBNull.Value ? null : Convert.ToInt64(value);
    }
    private static async Task<object?> Scalar(SqlConnection connection, string sql, SqlTransaction? tx = null, params SqlParameter[] parameters)
    { using var command = new SqlCommand(sql, connection, tx) { CommandTimeout = 30 }; command.Parameters.AddRange(parameters); return await command.ExecuteScalarAsync(); }
    private static async Task Exec(SqlConnection connection, string sql, SqlTransaction? tx = null, params SqlParameter[] parameters)
    { using var command = new SqlCommand(sql, connection, tx) { CommandTimeout = 30 }; command.Parameters.AddRange(parameters); await command.ExecuteNonQueryAsync(); }
    private static int? SqlError(Exception error)
    { for (Exception? current = error; current != null; current = current.InnerException) if (current is SqlException sql) return sql.Number; return null; }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
