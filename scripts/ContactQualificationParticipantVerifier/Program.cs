using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using UnicoreCRM.Crm;
using UnicoreCRM.Crm.Contacts.Contracts;
using UnicoreCRM.Platform;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
    throw new ArgumentException("Pass exactly one isolated SQL Server connection string.");

var verifier = new ContactQualificationParticipantVerifier(args[0]);
await verifier.RunAsync();

/// <summary>
/// Reproducible owner-local verification of the Contacts Lead qualification participant. It applies
/// the real Contacts and AccessControl migrations to an isolated database, provisions real
/// AccessControl state through the production contract, and drives the internal boundary through
/// production DI. No HTTP route exists for this boundary and none is created.
/// </summary>
internal sealed class ContactQualificationParticipantVerifier(string connectionString)
{
    private const string WorkspaceA = "ws_participant_a";
    private const string WorkspaceB = "ws_participant_b";
    private const string MemberA = "member_a";
    private const string MemberB = "member_b";
    private const string MembershipA = "membership_a";

    private readonly List<string> results = [];
    private int passed;
    private int failed;

    internal async Task RunAsync()
    {
        await RecreateDatabaseAsync();

        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:UnicoreCRM"] = connectionString
            })
            .Build();
        services.AddLogging();
        services.AddPlatformModule(configuration);
        services.AddCrmModule(configuration);

        // The ambient trusted-workspace accessor is normally populated by the HTTP middleware. The
        // participant has no route, so the verifier supplies the same ambient context the coordinator
        // would be running inside. Everything else - the canonical record-access evaluator, the
        // Contacts fact provider, persistence - is the production registration.
        services.RemoveAll<ICurrentWorkspace>();
        services.AddScoped<ControlledWorkspace>();
        services.AddScoped<ICurrentWorkspace>(provider => provider.GetRequiredService<ControlledWorkspace>());

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = false,
            ValidateScopes = true
        });

        try
        {
            await MigrateAsync(provider);
            await ProvisionAccessAsync(provider);

            await VerifyPhysicalModelAsync();
            await VerifyExistingModeAsync(provider);
            await VerifyNewModeCreatesAsync(provider);
            await VerifyDuplicateGuardAsync(provider);
            await VerifyWorkspaceIsolationAsync(provider);
            await VerifyReplayAsync(provider);
            await VerifyConcurrentCreateAsync(provider);
            VerifyCallableSurface();
        }
        finally
        {
            foreach (var result in results)
                Console.WriteLine(result);
            Console.WriteLine($"Contact qualification participant verification: PASS={passed} FAIL={failed}");
        }

        if (failed != 0)
            throw new InvalidOperationException("Contact qualification participant verification failed.");
    }

    // ---------------------------------------------------------------- physical model

    private async Task VerifyPhysicalModelAsync()
    {
        Check("contacts schema exists", 1L, await ScalarLongAsync(
            "SELECT COUNT(*) FROM sys.schemas WHERE name = N'contacts'"));
        Check("NormalizedWorkEmail column exists", 1L, await ScalarLongAsync(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA=N'contacts' AND TABLE_NAME=N'Contacts' AND COLUMN_NAME=N'NormalizedWorkEmail'"));
        Check("NormalizedPersonalEmail column exists", 1L, await ScalarLongAsync(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA=N'contacts' AND TABLE_NAME=N'Contacts' AND COLUMN_NAME=N'NormalizedPersonalEmail'"));
        Check("work-email detection index exists", 1L, await ScalarLongAsync(
            "SELECT COUNT(*) FROM sys.indexes WHERE name = N'IX_Contacts_WorkspaceId_NormalizedWorkEmail'"));
        Check("personal-email detection index exists", 1L, await ScalarLongAsync(
            "SELECT COUNT(*) FROM sys.indexes WHERE name = N'IX_Contacts_WorkspaceId_NormalizedPersonalEmail'"));
        Check("no UNIQUE constraint on any Contacts index", 0L, await ScalarLongAsync("""
            SELECT COUNT(*) FROM sys.indexes i
            JOIN sys.objects o ON o.object_id = i.object_id
            JOIN sys.schemas s ON s.schema_id = o.schema_id
            WHERE s.name = N'contacts' AND o.name = N'Contacts'
              AND i.is_unique = 1 AND i.is_primary_key = 0
            """));
        Check("Contacts AuditRecords table exists", 1L, await ScalarLongAsync(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA=N'contacts' AND TABLE_NAME=N'AuditRecords'"));
        Check("Contacts OutboxMessages table exists", 1L, await ScalarLongAsync(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA=N'contacts' AND TABLE_NAME=N'OutboxMessages'"));
        Check("Contacts ConversionRecords table exists", 1L, await ScalarLongAsync(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA=N'contacts' AND TABLE_NAME=N'ConversionRecords'"));
    }

    // ---------------------------------------------------------------- EXISTING

    private async Task VerifyExistingModeAsync(ServiceProvider provider)
    {
        var seeded = await SeedContactAsync(WorkspaceA, MemberA, "Seeded Person", "seed@example.com");

        var linked = await ResolveAsync(provider, WorkspaceA, MemberA, new(
            Trusted(WorkspaceA, MemberA),
            ContactQualificationMode.Existing,
            seeded,
            new ContactQualificationInput("Ignored Name", "ignored@example.com", "999", "Ignored"),
            MemberA,
            NewKey(),
            "req_existing_ok",
            "corr_existing_ok"));
        Check("EXISTING links the Workspace Contact", ContactQualificationDecision.Linked, linked.Decision);
        Check("EXISTING returns the selected identity", seeded, linked.ContactId);

        var storedName = await ScalarStringAsync(
            $"SELECT FullName FROM contacts.Contacts WHERE ContactId = N'{seeded}'");
        Check("EXISTING does not mutate the Contact name", "Seeded Person", storedName);
        Check("EXISTING does not advance the Contact version", 0L, await ScalarLongAsync(
            $"SELECT [Version] FROM contacts.Contacts WHERE ContactId = N'{seeded}'"));
        Check("EXISTING writes no command audit", 0L, await ScalarLongAsync(
            "SELECT COUNT(*) FROM contacts.AuditRecords"));
        Check("EXISTING writes no outbox message", 0L, await ScalarLongAsync(
            "SELECT COUNT(*) FROM contacts.OutboxMessages"));

        var unknown = await ResolveAsync(provider, WorkspaceA, MemberA, new(
            Trusted(WorkspaceA, MemberA),
            ContactQualificationMode.Existing,
            "contact_does_not_exist",
            null,
            MemberA,
            NewKey(),
            "req_existing_unknown",
            "corr_existing_unknown"));
        Check("unknown EXISTING is rejected", ContactQualificationDecision.Rejected, unknown.Decision);
        Check("unknown EXISTING returns no identity", null, unknown.ContactId);

        // A Contact that exists, but in another Workspace, must be byte-identical to an unknown one.
        var foreign = await SeedContactAsync(WorkspaceB, MemberB, "Foreign Person", "foreign@example.com");
        var foreignAttempt = await ResolveAsync(provider, WorkspaceA, MemberA, new(
            Trusted(WorkspaceA, MemberA),
            ContactQualificationMode.Existing,
            foreign,
            null,
            MemberA,
            NewKey(),
            "req_existing_foreign",
            "corr_existing_foreign"));
        Check("foreign-Workspace EXISTING is rejected", ContactQualificationDecision.Rejected, foreignAttempt.Decision);
        Check(
            "foreign and unknown EXISTING are indistinguishable",
            $"{unknown.Decision}|{unknown.Rejection}|{unknown.ContactId}|{unknown.ContactVersion}",
            $"{foreignAttempt.Decision}|{foreignAttempt.Rejection}|{foreignAttempt.ContactId}|{foreignAttempt.ContactVersion}");
    }

    // ---------------------------------------------------------------- NEW

    private async Task VerifyNewModeCreatesAsync(ServiceProvider provider)
    {
        var before = await ScalarLongAsync($"SELECT COUNT(*) FROM contacts.Contacts WHERE WorkspaceId = N'{WorkspaceA}'");

        var created = await ResolveAsync(provider, WorkspaceA, MemberA, new(
            Trusted(WorkspaceA, MemberA),
            ContactQualificationMode.New,
            null,
            new ContactQualificationInput("  Nguyen Van A  ", " New.Person@Example.COM ", " 0912345678 ", " Director "),
            MemberA,
            NewKey(),
            "req_new_ok",
            "corr_new_ok"));
        Check("NEW creates a Contact", ContactQualificationDecision.Created, created.Decision);
        Check("NEW returns an owner-assigned identity", true, created.ContactId?.StartsWith("contact_", StringComparison.Ordinal));
        Check("NEW returns the initial version", 0L, created.ContactVersion);

        Check("NEW created exactly one Contact", before + 1, await ScalarLongAsync(
            $"SELECT COUNT(*) FROM contacts.Contacts WHERE WorkspaceId = N'{WorkspaceA}'"));

        var id = created.ContactId!;
        Check("fullName comes from displayName, trimmed", "Nguyen Van A", await ScalarStringAsync(
            $"SELECT FullName FROM contacts.Contacts WHERE ContactId = N'{id}'"));
        Check("status is active", "active", await ScalarStringAsync(
            $"SELECT [Status] FROM contacts.Contacts WHERE ContactId = N'{id}'"));
        Check("ownerId is the Lead owner", MemberA, await ScalarStringAsync(
            $"SELECT OwnerId FROM contacts.Contacts WHERE ContactId = N'{id}'"));
        Check("email lands in workEmail", "New.Person@Example.COM", await ScalarStringAsync(
            $"SELECT JSON_VALUE(Profile, '$.workEmail') FROM contacts.Contacts WHERE ContactId = N'{id}'"));
        Check("phone lands in mobilePhone", "0912345678", await ScalarStringAsync(
            $"SELECT JSON_VALUE(Profile, '$.mobilePhone') FROM contacts.Contacts WHERE ContactId = N'{id}'"));
        Check("title lands in jobTitle", "Director", await ScalarStringAsync(
            $"SELECT JSON_VALUE(Profile, '$.jobTitle') FROM contacts.Contacts WHERE ContactId = N'{id}'"));
        Check("normalized projection uses the frozen rule", "NEW.PERSON@EXAMPLE.COM", await ScalarStringAsync(
            $"SELECT NormalizedWorkEmail FROM contacts.Contacts WHERE ContactId = N'{id}'"));
        Check("no personal email is invented", null, await ScalarStringAsync(
            $"SELECT NormalizedPersonalEmail FROM contacts.Contacts WHERE ContactId = N'{id}'"));
        Check("consent is not transferred", null, await ScalarStringAsync(
            $"SELECT JSON_VALUE(Profile, '$.consent') FROM contacts.Contacts WHERE ContactId = N'{id}'"));
        Check("do-not-email is not transferred", null, await ScalarStringAsync(
            $"SELECT JSON_VALUE(Profile, '$.doNotEmail') FROM contacts.Contacts WHERE ContactId = N'{id}'"));
        Check("source is not copied from the Lead", null, await ScalarStringAsync(
            $"SELECT JSON_VALUE(Profile, '$.source') FROM contacts.Contacts WHERE ContactId = N'{id}'"));

        Check("NEW writes exactly one command audit", 1L, await ScalarLongAsync(
            $"SELECT COUNT(*) FROM contacts.AuditRecords WHERE AggregateId = N'{id}'"));
        Check("NEW stages exactly one outbox message", 1L, await ScalarLongAsync(
            $"SELECT COUNT(*) FROM contacts.OutboxMessages WHERE AggregateId = N'{id}'"));
        Check("outbox event type", "CONTACT_CREATED", await ScalarStringAsync(
            $"SELECT EventType FROM contacts.OutboxMessages WHERE AggregateId = N'{id}'"));
    }

    private async Task VerifyDuplicateGuardAsync(ServiceProvider provider)
    {
        var before = await ScalarLongAsync($"SELECT COUNT(*) FROM contacts.Contacts WHERE WorkspaceId = N'{WorkspaceA}'");

        // Different spelling, same normalized identity under the frozen rule.
        var duplicate = await ResolveAsync(provider, WorkspaceA, MemberA, new(
            Trusted(WorkspaceA, MemberA),
            ContactQualificationMode.New,
            null,
            new ContactQualificationInput("Someone Else", "new.person@EXAMPLE.com", null, null),
            MemberA,
            NewKey(),
            "req_new_dup",
            "corr_new_dup"));
        Check("exact normalized duplicate blocks NEW", ContactQualificationDecision.Rejected, duplicate.Decision);
        Check("duplicate rejection reveals no identity", null, duplicate.ContactId);
        Check("duplicate rejection reveals no version", null, duplicate.ContactVersion);
        Check("duplicate committed nothing", before, await ScalarLongAsync(
            $"SELECT COUNT(*) FROM contacts.Contacts WHERE WorkspaceId = N'{WorkspaceA}'"));

        // The guard must ignore record scope: a Contact owned by another member still blocks.
        await SeedContactAsync(WorkspaceA, MemberB, "Other Member Contact", "othermember@example.com");
        var foreignOwned = await ResolveAsync(provider, WorkspaceA, MemberA, new(
            Trusted(WorkspaceA, MemberA),
            ContactQualificationMode.New,
            null,
            new ContactQualificationInput("Duplicate Of Other", "OTHERMEMBER@example.com", null, null),
            MemberA,
            NewKey(),
            "req_new_dup_other",
            "corr_new_dup_other"));
        Check("duplicate owned by another member also blocks", ContactQualificationDecision.Rejected, foreignOwned.Decision);
        Check(
            "same-owner and other-owner duplicates are indistinguishable",
            $"{duplicate.Decision}|{duplicate.Rejection}|{duplicate.ContactId}|{duplicate.ContactVersion}",
            $"{foreignOwned.Decision}|{foreignOwned.Rejection}|{foreignOwned.ContactId}|{foreignOwned.ContactVersion}");

        // A personalEmail hit must block a workEmail candidate: it is the same person.
        await SeedPersonalEmailContactAsync(WorkspaceA, MemberA, "Personal Only", "personal.only@example.com");
        var crossField = await ResolveAsync(provider, WorkspaceA, MemberA, new(
            Trusted(WorkspaceA, MemberA),
            ContactQualificationMode.New,
            null,
            new ContactQualificationInput("Personal Duplicate", "Personal.Only@Example.com", null, null),
            MemberA,
            NewKey(),
            "req_new_dup_personal",
            "corr_new_dup_personal"));
        Check("personalEmail match blocks a workEmail candidate", ContactQualificationDecision.Rejected, crossField.Decision);

        // A near-miss must NOT block: no fuzzy matching, no plus-address stripping, no dot removal.
        var plusAddress = await ResolveAsync(provider, WorkspaceA, MemberA, new(
            Trusted(WorkspaceA, MemberA),
            ContactQualificationMode.New,
            null,
            new ContactQualificationInput("Plus Address", "new.person+tag@example.com", null, null),
            MemberA,
            NewKey(),
            "req_new_plus",
            "corr_new_plus"));
        Check("plus-addressed variant is NOT treated as a duplicate", ContactQualificationDecision.Created, plusAddress.Decision);

        // A Contact with no email must not collide with another Contact with no email.
        var firstKeyless = await ResolveAsync(provider, WorkspaceA, MemberA, new(
            Trusted(WorkspaceA, MemberA),
            ContactQualificationMode.New, null,
            new ContactQualificationInput("Keyless One", null, null, null),
            MemberA, NewKey(), "req_keyless_1", "corr_keyless_1"));
        var secondKeyless = await ResolveAsync(provider, WorkspaceA, MemberA, new(
            Trusted(WorkspaceA, MemberA),
            ContactQualificationMode.New, null,
            new ContactQualificationInput("Keyless Two", "   ", null, null),
            MemberA, NewKey(), "req_keyless_2", "corr_keyless_2"));
        Check("first Contact without an email is created", ContactQualificationDecision.Created, firstKeyless.Decision);
        Check("second Contact without an email is not blocked", ContactQualificationDecision.Created, secondKeyless.Decision);
    }

    private async Task VerifyWorkspaceIsolationAsync(ServiceProvider provider)
    {
        var created = await ResolveAsync(provider, WorkspaceB, MemberB, new(
            Trusted(WorkspaceB, MemberB),
            ContactQualificationMode.New,
            null,
            new ContactQualificationInput("Same Email Other Workspace", "new.person@example.com", null, null),
            MemberB,
            NewKey(),
            "req_new_other_ws",
            "corr_new_other_ws"));
        Check("the same email in another Workspace does not block", ContactQualificationDecision.Created, created.Decision);
        Check("the new Contact belongs to its own Workspace", WorkspaceB, await ScalarStringAsync(
            $"SELECT WorkspaceId FROM contacts.Contacts WHERE ContactId = N'{created.ContactId}'"));
    }

    private async Task VerifyReplayAsync(ServiceProvider provider)
    {
        var key = NewKey();
        var command = new ResolveQualificationContactCommand(
            Trusted(WorkspaceA, MemberA),
            ContactQualificationMode.New,
            null,
            new ContactQualificationInput("Replay Person", "replay@example.com", null, null),
            MemberA,
            key,
            "req_replay",
            "corr_replay");

        var first = await ResolveAsync(provider, WorkspaceA, MemberA, command);
        var second = await ResolveAsync(provider, WorkspaceA, MemberA, command);
        var third = await ResolveAsync(provider, WorkspaceA, MemberA, command);

        Check("first conversion creates", ContactQualificationDecision.Created, first.Decision);
        Check("replay reports REPLAYED", ContactQualificationDecision.Replayed, second.Decision);
        Check("second replay also reports REPLAYED", ContactQualificationDecision.Replayed, third.Decision);
        Check("replay returns the same identity", first.ContactId, second.ContactId);
        Check("repeated replay stays deterministic", first.ContactId, third.ContactId);
        Check("replay creates no second Contact", 1L, await ScalarLongAsync(
            $"SELECT COUNT(*) FROM contacts.Contacts WHERE NormalizedWorkEmail = N'REPLAY@EXAMPLE.COM' AND WorkspaceId = N'{WorkspaceA}'"));
        Check("replay writes no second audit", 1L, await ScalarLongAsync(
            $"SELECT COUNT(*) FROM contacts.AuditRecords WHERE AggregateId = N'{first.ContactId}'"));
        Check("replay stages no second outbox message", 1L, await ScalarLongAsync(
            $"SELECT COUNT(*) FROM contacts.OutboxMessages WHERE AggregateId = N'{first.ContactId}'"));

        // A different conversion key for the same person is a genuinely new intent, and must hit the
        // duplicate guard rather than silently replaying someone else's conversion.
        var otherKey = await ResolveAsync(provider, WorkspaceA, MemberA, command with
        {
            ConversionKey = NewKey(),
            RequestId = "req_replay_other_key"
        });
        Check("a different conversion key does not replay", ContactQualificationDecision.Rejected, otherKey.Decision);
    }

    private async Task VerifyConcurrentCreateAsync(ServiceProvider provider)
    {
        const string email = "race@example.com";
        var commands = Enumerable.Range(0, 2).Select(index => new ResolveQualificationContactCommand(
            Trusted(WorkspaceA, MemberA),
            ContactQualificationMode.New,
            null,
            new ContactQualificationInput($"Race {index}", email, null, null),
            MemberA,
            NewKey(),
            $"req_race_{index}",
            $"corr_race_{index}")).ToArray();

        var outcomes = await Task.WhenAll(commands.Select(command =>
            ResolveAsync(provider, WorkspaceA, MemberA, command)));

        var created = outcomes.Count(item => item.Decision == ContactQualificationDecision.Created);
        var rejected = outcomes.Count(item => item.Decision == ContactQualificationDecision.Rejected);
        Check("exactly one concurrent NEW commits", 1, created);
        Check("the other concurrent NEW is rejected", 1, rejected);
        Check("concurrency leaves exactly one Contact", 1L, await ScalarLongAsync(
            $"SELECT COUNT(*) FROM contacts.Contacts WHERE WorkspaceId = N'{WorkspaceA}' AND NormalizedWorkEmail = N'RACE@EXAMPLE.COM'"));
        Check("no unhandled concurrency failure escaped", 0, outcomes.Count(item =>
            item.Rejection == ContactQualificationRejection.ConcurrentConflict));
    }

    /// <summary>
    /// The boundary must stay internal. Any HTTP verb mapping or foreign persistence type inside the
    /// Contacts owner would mean a public mutation surface or a broken ownership boundary.
    /// </summary>
    private void VerifyCallableSurface()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/UnicoreCRM.Crm/Contacts"));
        var source = string.Join(
            "\n",
            Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Select(File.ReadAllText));

        foreach (var forbidden in new[]
                 {
                     "MapPost(", "MapPut(", "MapPatch(", "MapDelete(",
                     "LeadsDbContext", "CustomersDbContext", "OrganizationsDbContext", "DealsDbContext",
                     "Leads.Infrastructure", "Customers.Infrastructure", "Organizations.Infrastructure",
                     "UnicoreCRM.Workflows"
                 })
        {
            Check($"no forbidden surface: {forbidden}", false, source.Contains(forbidden, StringComparison.Ordinal));
        }

        var mappedGets = source.Split("MapGet(").Length - 1;
        Check("Contacts still maps exactly the two admitted reads", 2, mappedGets);
    }

    // ---------------------------------------------------------------- harness

    private static TrustedWorkspaceContext Trusted(string workspaceId, string memberId) =>
        new(workspaceId, $"account_{memberId}", memberId, MembershipId(memberId));

    // AccessControl assignments are keyed by membership, so the identifier the verifier provisions
    // must be the identifier the trusted context carries.
    private static string MembershipId(string memberId) => $"{MembershipA}_{memberId}";

    private static string NewKey() => $"conv_{Guid.NewGuid():N}";

    private static async Task<ResolveQualificationContactResult> ResolveAsync(
        ServiceProvider provider,
        string workspaceId,
        string memberId,
        ResolveQualificationContactCommand command)
    {
        await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ControlledWorkspace>().Set(Trusted(workspaceId, memberId));
        var participant = scope.ServiceProvider.GetRequiredService<IContactQualificationParticipant>();
        return await participant.ResolveAsync(command, CancellationToken.None);
    }

    private static async Task MigrateAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        foreach (var context in new DbContext[]
                 {
                     Resolve(scope, "UnicoreCRM.Crm.Contacts.Infrastructure.Persistence.ContactsDbContext"),
                     Resolve(scope, "UnicoreCRM.Platform.AccessControl.Infrastructure.Persistence.AccessControlDbContext")
                 })
        {
            await context.Database.MigrateAsync();
        }
    }

    private static DbContext Resolve(IServiceScope scope, string typeName)
    {
        var type = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(typeName))
            .FirstOrDefault(found => found is not null)
            ?? throw new InvalidOperationException($"{typeName} was not found.");
        return (DbContext)scope.ServiceProvider.GetRequiredService(type);
    }

    /// <summary>
    /// Provisions real AccessControl state through the production contract, so the EXISTING path is
    /// authorized by the canonical evaluator against the frozen server-owned capability set rather
    /// than a stub.
    /// </summary>
    private static async Task ProvisionAccessAsync(ServiceProvider provider)
    {
        foreach (var (workspaceId, memberId) in new[] { (WorkspaceA, MemberA), (WorkspaceB, MemberB) })
        {
            await using var scope = provider.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ControlledWorkspace>().Set(Trusted(workspaceId, memberId));
            var access = scope.ServiceProvider.GetRequiredService<IInitialWorkspaceAccessProvisioning>();
            var provisioned = await access.EnsureInitialWorkspaceAccessAsync(
                workspaceId,
                MembershipId(memberId),
                CancellationToken.None);
            if (!provisioned.Capabilities.Contains("contacts.read", StringComparer.Ordinal))
                throw new InvalidOperationException($"contacts.read was not provisioned for {workspaceId}.");
        }
    }

    private async Task<string> SeedContactAsync(string workspaceId, string ownerId, string fullName, string email)
    {
        var id = $"contact_seed_{Guid.NewGuid():N}";
        var profile = $$"""{"workEmail":"{{email}}","displayName":"{{fullName}}"}""";
        await ExecuteAsync($"""
            INSERT INTO contacts.Contacts
                (ContactId, WorkspaceId, OwnerId, FullName, [Status], [Version], CreatedAt, UpdatedAt, Profile,
                 NormalizedWorkEmail, NormalizedPersonalEmail)
            VALUES
                (N'{id}', N'{workspaceId}', N'{ownerId}', N'{fullName}', N'active', 0,
                 SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'{profile}',
                 N'{email.ToUpperInvariant()}', NULL)
            """);
        return id;
    }

    private async Task SeedPersonalEmailContactAsync(string workspaceId, string ownerId, string fullName, string email)
    {
        var id = $"contact_seed_{Guid.NewGuid():N}";
        var profile = $$"""{"personalEmail":"{{email}}","displayName":"{{fullName}}"}""";
        await ExecuteAsync($"""
            INSERT INTO contacts.Contacts
                (ContactId, WorkspaceId, OwnerId, FullName, [Status], [Version], CreatedAt, UpdatedAt, Profile,
                 NormalizedWorkEmail, NormalizedPersonalEmail)
            VALUES
                (N'{id}', N'{workspaceId}', N'{ownerId}', N'{fullName}', N'active', 0,
                 SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'{profile}',
                 NULL, N'{email.ToUpperInvariant()}')
            """);
    }

    private async Task RecreateDatabaseAsync()
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        var database = builder.InitialCatalog;
        builder.InitialCatalog = "master";
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            IF DB_ID('{database}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{database}];
            END
            CREATE DATABASE [{database}];
            """;
        await command.ExecuteNonQueryAsync();
        // READ_COMMITTED_SNAPSHOT stays off so SERIALIZABLE takes real key-range locks, which is what
        // the concurrency guarantee actually depends on.
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private async Task<long> ScalarLongAsync(string sql)
    {
        var value = await ScalarAsync(sql);
        return value is null or DBNull ? 0L : Convert.ToInt64(value);
    }

    private async Task<string?> ScalarStringAsync(string sql)
    {
        var value = await ScalarAsync(sql);
        return value is null or DBNull ? null : Convert.ToString(value);
    }

    private async Task<object?> ScalarAsync(string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }

    private void Check<T>(string name, T expected, T actual)
    {
        if (EqualityComparer<T>.Default.Equals(expected, actual))
        {
            passed++;
            results.Add($"PASS | {name}");
        }
        else
        {
            failed++;
            results.Add($"FAIL | {name} | expected='{expected}' actual='{actual}'");
        }
    }
}

internal sealed class ControlledWorkspace : ICurrentWorkspace
{
    private TrustedWorkspaceContext? current;

    public bool IsResolved => current is not null;

    public TrustedWorkspaceContext Require() =>
        current ?? throw new InvalidOperationException("No trusted workspace was set for this verifier scope.");

    public void Set(TrustedWorkspaceContext context) => current = context;
}
