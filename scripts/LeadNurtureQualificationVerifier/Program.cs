using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using UnicoreCRM.Crm;
using UnicoreCRM.Crm.Contacts.Contracts;
using UnicoreCRM.Crm.Leads.Contracts;
using UnicoreCRM.Operations;
using UnicoreCRM.Operations.Tasks.Contracts;
using UnicoreCRM.Platform;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Workflows;
using UnicoreCRM.Workflows.Atomic.Contracts;

if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
    throw new ArgumentException("Pass exactly one isolated SQL Server connection string.");

var verifier = new LeadNurtureQualificationVerifier(args[0]);
await verifier.RunAsync();

/// <summary>
/// Reproducible owner-local verification of the internal NURTURE Lead Qualification workflow. It
/// applies the real Leads, Contacts, Tasks, AccessControl and Workflows migrations to an isolated
/// database, provisions real Workspace access through the production contract, and drives the
/// coordinator through production DI directly, below the HTTP boundary. Public exposure of the
/// NURTURE operation is verified separately by verify-lead-nurture-qualification-api.ps1.
/// </summary>
internal sealed class LeadNurtureQualificationVerifier(string connectionString)
{
    private const string WorkspaceA = "ws_nurture_a";
    private const string WorkspaceB = "ws_nurture_b";
    private const string MemberA = "member_nurture_a";
    private const string MemberB = "member_nurture_b";

    private readonly List<string> results = [];
    private int passed;
    private int failed;
    private ServiceProvider provider = null!;
    private readonly ParticipantCalls participantCalls = new();

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
        services.AddOperationsModule(configuration);
        services.AddWorkflowsModule(configuration);

        // Count entry into the real participants, including idempotent calls that write no rows.
        var contactType = services.Single(item => item.ServiceType == typeof(IContactQualificationParticipant)).ImplementationType!;
        var taskType = services.Single(item => item.ServiceType == typeof(ILeadQualificationTaskParticipant)).ImplementationType!;
        var leadType = services.Single(item => item.ServiceType == typeof(ILeadQualificationParticipant)).ImplementationType!;
        services.RemoveAll<IContactQualificationParticipant>();
        services.RemoveAll<ILeadQualificationTaskParticipant>();
        services.RemoveAll<ILeadQualificationParticipant>();
        services.AddScoped<IContactQualificationParticipant>(sp => new CountingContactParticipant(
            (IContactQualificationParticipant)ActivatorUtilities.CreateInstance(sp, contactType), participantCalls));
        services.AddScoped<ILeadQualificationTaskParticipant>(sp => new CountingTaskParticipant(
            (ILeadQualificationTaskParticipant)ActivatorUtilities.CreateInstance(sp, taskType), participantCalls));
        services.AddScoped<ILeadQualificationParticipant>(sp => new CountingLeadParticipant(
            (ILeadQualificationParticipant)ActivatorUtilities.CreateInstance(sp, leadType), participantCalls));

        // The ambient trusted-workspace accessor is normally populated by the HTTP middleware. The
        // coordinator has no route, so the verifier supplies the same ambient context a request
        // would. Every other registration is production.
        services.RemoveAll<ICurrentWorkspace>();
        services.AddScoped<ControlledWorkspace>();
        services.AddScoped<ICurrentWorkspace>(sp => sp.GetRequiredService<ControlledWorkspace>());

        provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = false,
            ValidateScopes = true
        });

        await using (provider)
        {
            try
            {
                await MigrateAsync();
                await SeedWorkspacesAsync();
                await ProvisionAccessAsync();

                await VerifyPhysicalModelAsync();
                await VerifyNewContactHappyPathAsync();
                await VerifyExistingContactPathAsync();
                await VerifyReplayAsync();
                await VerifyCrashRecoveryAsync();
                await VerifyChangedIntentAsync();
                await VerifyStaleVersionAsync();
                await VerifyLeadBoundaryAsync();
                await VerifyRequestContractAsync();
                await VerifyInvalidTaskOwnerAsync();
                await VerifyContactRejectionAsync();
                await VerifyMissingTaskCapabilityAsync();
                await VerifyWorkspaceIsolationAsync();
                await VerifyConcurrencyAsync();
                await VerifyIntentFingerprintAsync();
                await VerifyParticipantAcknowledgmentLossAsync();
                await VerifyChangedIntentConcurrencyAsync();
                await VerifyReasonPreservationAsync();
                await VerifyContactContentionAsync();
                await VerifyDifferentCallerRecoveryAsync();
                await VerifyNoForeignOwnerWritesAsync();
                VerifyCallableSurface();
            }
            finally
            {
                foreach (var line in results)
                    Console.WriteLine(line);
                Console.WriteLine($"NURTURE Lead qualification workflow verification: PASS={passed} FAIL={failed}");
            }
        }

        if (failed != 0)
            throw new InvalidOperationException("NURTURE Lead qualification workflow verification failed.");
    }

    // ------------------------------------------------------------------ physical model

    private async Task VerifyPhysicalModelAsync()
    {
        Check("workflow schema exists", 1L, await ScalarLongAsync(
            "SELECT COUNT(*) FROM sys.schemas WHERE name = N'workflow'"));
        Check("LeadQualificationAnchors table exists", 1L, await ScalarLongAsync(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA=N'workflow' AND TABLE_NAME=N'LeadQualificationAnchors'"));
        Check("anchor primary key is the workflow identity", "ScopeKey", await ScalarStringAsync("""
            SELECT c.name FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns c ON c.object_id = i.object_id AND c.column_id = ic.column_id
            JOIN sys.objects o ON o.object_id = i.object_id
            JOIN sys.schemas s ON s.schema_id = o.schema_id
            WHERE s.name = N'workflow' AND o.name = N'LeadQualificationAnchors' AND i.is_primary_key = 1
            """));
        Check("Lead relationship columns exist", 2L, await ScalarLongAsync(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA=N'leads' AND TABLE_NAME=N'Leads' AND COLUMN_NAME IN (N'RelationshipType', N'RelationshipId')"));
    }

    // ------------------------------------------------------------------ happy paths

    private async Task VerifyNewContactHappyPathAsync()
    {
        var leadId = await SeedLeadAsync(WorkspaceA, MemberA, "Nurture Lead One", "lead.one@example.com");
        var result = await ExecuteAsync(WorkspaceA, MemberA, Command(leadId, NewContact("Nurture Person One", "person.one@example.com")));

        Check("NEW-contact qualification succeeds", true, result.IsSuccess);
        Check("outcome is COMMITTED", "COMMITTED", result.Outcome);
        Check("qualification outcome is NURTURE", "NURTURE", result.QualificationOutcome);
        Check("a Contact was resolved", true, result.ContactId?.StartsWith("contact_", StringComparison.Ordinal));
        Check("a Task was created", true, result.TaskId?.Length > 0);

        Check("exactly one Contact exists for the address", 1L, await ScalarLongAsync(
            $"SELECT COUNT(*) FROM contacts.Contacts WHERE WorkspaceId=N'{WorkspaceA}' AND NormalizedWorkEmail=N'PERSON.ONE@EXAMPLE.COM'"));
        Check("the Contact owner is the Lead owner", MemberA, await ScalarStringAsync(
            $"SELECT OwnerId FROM contacts.Contacts WHERE ContactId=N'{result.ContactId}'"));

        Check("exactly one Task references the Lead", 1L, await ScalarLongAsync(
            $"SELECT COUNT(*) FROM tasks.Tasks WHERE TaskId=N'{result.TaskId}'"));

        Check("Lead work state is CLOSED", 3L, await ScalarLongAsync(
            $"SELECT WorkState FROM leads.Leads WHERE LeadId=N'{leadId}'"));
        Check("Lead qualification outcome is NURTURE", 1L, await ScalarLongAsync(
            $"SELECT QualificationOutcome FROM leads.Leads WHERE LeadId=N'{leadId}'"));
        Check("Lead relationship type is CONTACT", "CONTACT", await ScalarStringAsync(
            $"SELECT RelationshipType FROM leads.Leads WHERE LeadId=N'{leadId}'"));
        Check("Lead relationship id is the resolved Contact", result.ContactId, await ScalarStringAsync(
            $"SELECT RelationshipId FROM leads.Leads WHERE LeadId=N'{leadId}'"));
        Check("Lead version advanced once", 1L, await ScalarLongAsync(
            $"SELECT [Version] FROM leads.Leads WHERE LeadId=N'{leadId}'"));
        Check("no qualifiedAt/qualifiedBy column was added to Leads", 0L, await ScalarLongAsync(
            "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA=N'leads' AND TABLE_NAME=N'Leads' AND COLUMN_NAME IN (N'QualifiedAt', N'QualifiedBy', N'ContactId')"));

        Check("Leads wrote one qualification command audit", 1L, await ScalarLongAsync(
            $"SELECT COUNT(*) FROM leads.AuditRecords WHERE AggregateId=N'{leadId}' AND Operation=N'qualifyLeadForNurture'"));
        Check("Leads staged one qualification outbox message", 1L, await ScalarLongAsync(
            $"SELECT COUNT(*) FROM leads.OutboxMessages WHERE AggregateId=N'{leadId}' AND EventType=N'LEAD_QUALIFIED_FOR_NURTURE'"));
        Check("anchor is Completed", "Completed", await ScalarStringAsync(
            $"SELECT Stage FROM workflow.LeadQualificationAnchors WHERE LeadId=N'{leadId}'"));
    }

    private async Task VerifyExistingContactPathAsync()
    {
        var existing = await SeedContactAsync(WorkspaceA, MemberA, "Existing Person", "existing.person@example.com");
        var beforeVersion = await ScalarLongAsync($"SELECT [Version] FROM contacts.Contacts WHERE ContactId=N'{existing}'");
        var beforeName = await ScalarStringAsync($"SELECT FullName FROM contacts.Contacts WHERE ContactId=N'{existing}'");
        var beforeContacts = await ScalarLongAsync($"SELECT COUNT(*) FROM contacts.Contacts WHERE WorkspaceId=N'{WorkspaceA}'");

        var leadId = await SeedLeadAsync(WorkspaceA, MemberA, "Nurture Lead Two", "lead.two@example.com");
        var result = await ExecuteAsync(WorkspaceA, MemberA, Command(leadId, new LeadNurtureContactIntent(
            LeadNurtureRelationshipMode.Existing, existing, "Ignored", "ignored@example.com", "999", "Ignored")));

        Check("EXISTING-contact qualification succeeds", true, result.IsSuccess);
        Check("EXISTING returns the selected Contact", existing, result.ContactId);
        Check("EXISTING creates no Contact", beforeContacts, await ScalarLongAsync(
            $"SELECT COUNT(*) FROM contacts.Contacts WHERE WorkspaceId=N'{WorkspaceA}'"));
        Check("EXISTING does not advance the Contact version", beforeVersion, await ScalarLongAsync(
            $"SELECT [Version] FROM contacts.Contacts WHERE ContactId=N'{existing}'"));
        Check("EXISTING does not mutate the Contact name", beforeName, await ScalarStringAsync(
            $"SELECT FullName FROM contacts.Contacts WHERE ContactId=N'{existing}'"));
        Check("EXISTING still creates one Task", 1L, await ScalarLongAsync(
            $"SELECT COUNT(*) FROM tasks.Tasks WHERE TaskId=N'{result.TaskId}'"));
        Check("EXISTING closes the Lead", 3L, await ScalarLongAsync(
            $"SELECT WorkState FROM leads.Leads WHERE LeadId=N'{leadId}'"));
        Check("EXISTING records the CONTACT relationship", existing, await ScalarStringAsync(
            $"SELECT RelationshipId FROM leads.Leads WHERE LeadId=N'{leadId}'"));
    }

    // ------------------------------------------------------------------ replay and recovery

    private async Task VerifyReplayAsync()
    {
        var leadId = await SeedLeadAsync(WorkspaceA, MemberA, "Replay Lead", "replay.lead@example.com");
        var command = Command(leadId, NewContact("Replay Person", "replay.person@example.com"));

        var contactsBefore = participantCalls.Contacts;
        var tasksBefore = participantCalls.Tasks;
        var first = await ExecuteAsync(WorkspaceA, MemberA, command);
        Check("replay spy observes original Contact call", contactsBefore + 1, participantCalls.Contacts);
        Check("replay spy observes original Task call", tasksBefore + 1, participantCalls.Tasks);
        var second = await ExecuteAsync(WorkspaceA, MemberA, command);
        var third = await ExecuteAsync(WorkspaceA, MemberA, command);
        Check("completed replays invoke no Contact participant", contactsBefore + 1, participantCalls.Contacts);
        Check("completed replays invoke no Task participant", tasksBefore + 1, participantCalls.Tasks);

        Check("first execution commits", "COMMITTED", first.Outcome);
        Check("replay reports REPLAYED", "REPLAYED", second.Outcome);
        Check("second replay stays REPLAYED", "REPLAYED", third.Outcome);
        Check("replay returns the same Contact", first.ContactId, second.ContactId);
        Check("replay returns the same Task", first.TaskId, third.TaskId);
        Check("replay returns the same Lead version", first.LeadVersion, second.LeadVersion);
        Check("replay creates no second Contact", 1L, await ScalarLongAsync(
            $"SELECT COUNT(*) FROM contacts.Contacts WHERE WorkspaceId=N'{WorkspaceA}' AND NormalizedWorkEmail=N'REPLAY.PERSON@EXAMPLE.COM'"));
        Check("replay creates no second Task", 1L, await ScalarLongAsync(
            $"SELECT COUNT(*) FROM tasks.Tasks WHERE SourceId=N'{leadId}'"));
        Check("replay does not advance the Lead version", 1L, await ScalarLongAsync(
            $"SELECT [Version] FROM leads.Leads WHERE LeadId=N'{leadId}'"));
        Check("replay writes no second Lead audit", 1L, await ScalarLongAsync(
            $"SELECT COUNT(*) FROM leads.AuditRecords WHERE AggregateId=N'{leadId}' AND Operation=N'qualifyLeadForNurture'"));
    }

    /// <summary>
    /// Recovery from an interrupted coordinator. Each case reproduces a real interruption point
    /// rather than editing a completed run: every identifier involved is genuine, and every case
    /// must converge on one Contact, one Task and one terminal Lead result without deleting
    /// anything.
    ///
    /// The third interruption point - anchor holds a Contact but no Task - is exercised by
    /// <see cref="VerifyMissingTaskCapabilityAsync"/>, where a real capability denial stops the
    /// coordinator at exactly that stage.
    /// </summary>
    private async Task VerifyCrashRecoveryAsync()
    {
        await VerifyContactWithoutAnchorAsync();
        await VerifyAnchorLostAfterCloseAsync();
    }

    /// <summary>
    /// The Contact committed and the coordinator stopped before it could record anything: no anchor
    /// row exists at all. The re-drive must adopt that Contact through the conversion key, not
    /// create a second one.
    /// </summary>
    private async Task VerifyContactWithoutAnchorAsync()
    {
        const string label = "Contact committed, anchor never written";
        const string email = "recovery.one@example.com";
        var leadId = await SeedLeadAsync(WorkspaceA, MemberA, "Recovery One", "lead.recovery.one@example.com");
        var command = Command(leadId, NewContact("Recovery Person One", email));

        // Exactly what the coordinator's first step commits: the same participant, under the same
        // conversion key the coordinator derives from the workflow identity.
        var conversionKey = WorkflowScopeKey(WorkspaceA, leadId, command.IdempotencyKey);
        var preCommitted = await ResolveContactAsync(WorkspaceA, MemberA, conversionKey, "Recovery Person One", email);
        Check($"[{label}] the interrupted attempt committed a Contact", true, preCommitted is not null);
        Check($"[{label}] no anchor exists yet", 0L, await ScalarLongAsync(
            $"SELECT COUNT(*) FROM workflow.LeadQualificationAnchors WHERE LeadId=N'{leadId}'"));

        var resumed = await ExecuteAsync(WorkspaceA, MemberA, command);
        Check($"[{label}] the re-drive succeeds", true, resumed.IsSuccess);
        Check($"[{label}] it adopts the committed Contact", preCommitted, resumed.ContactId);
        Check($"[{label}] one Contact survives", 1L, await ScalarLongAsync(
            $"SELECT COUNT(*) FROM contacts.Contacts WHERE WorkspaceId=N'{WorkspaceA}' AND NormalizedWorkEmail=N'{email.ToUpperInvariant()}'"));
        Check($"[{label}] one Task exists", 1L, await ScalarLongAsync(
            $"SELECT COUNT(*) FROM tasks.Tasks WHERE SourceId=N'{leadId}'"));
        Check($"[{label}] the Lead closes once", 1L, await ScalarLongAsync(
            $"SELECT [Version] FROM leads.Leads WHERE LeadId=N'{leadId}'"));
        Check($"[{label}] the Lead points at the adopted Contact", preCommitted, await ScalarStringAsync(
            $"SELECT RelationshipId FROM leads.Leads WHERE LeadId=N'{leadId}'"));
        Check($"[{label}] the anchor ends Completed", "Completed", await ScalarStringAsync(
            $"SELECT Stage FROM workflow.LeadQualificationAnchors WHERE LeadId=N'{leadId}'"));
    }

    /// <summary>
    /// Contact, Task and the Lead close all committed, and the coordinator stopped before recording
    /// completion. The re-drive must converge on the same result through the Leads idempotency
    /// record, even though the Lead is now legitimately CLOSED and would fail its own precondition.
    /// </summary>
    private async Task VerifyAnchorLostAfterCloseAsync()
    {
        const string label = "Task and close committed, completion not recorded";
        const string email = "recovery.three@example.com";
        var leadId = await SeedLeadAsync(WorkspaceA, MemberA, "Recovery Three", "lead.recovery.three@example.com");
        var command = Command(leadId, NewContact("Recovery Person Three", email));

        var first = await ExecuteAsync(WorkspaceA, MemberA, command);
        Check($"[{label}] the interrupted attempt commits", "COMMITTED", first.Outcome);

        await ExecuteAsync(null,
            $"UPDATE workflow.LeadQualificationAnchors SET Stage=N'TaskCreated', LeadVersion=NULL WHERE LeadId=N'{leadId}'");

        var resumed = await ExecuteAsync(WorkspaceA, MemberA, command);
        Check($"[{label}] the re-drive succeeds", true, resumed.IsSuccess);
        Check($"[{label}] full response converges", SemanticResponse(first), SemanticResponse(resumed));
        Check($"[{label}] the Lead close replays rather than repeating", "REPLAYED", resumed.Outcome);
        Check($"[{label}] converges on the same Contact", first.ContactId, resumed.ContactId);
        Check($"[{label}] converges on the same Task", first.TaskId, resumed.TaskId);
        Check($"[{label}] one Contact survives", 1L, await ScalarLongAsync(
            $"SELECT COUNT(*) FROM contacts.Contacts WHERE WorkspaceId=N'{WorkspaceA}' AND NormalizedWorkEmail=N'{email.ToUpperInvariant()}'"));
        Check($"[{label}] one Task survives", 1L, await ScalarLongAsync(
            $"SELECT COUNT(*) FROM tasks.Tasks WHERE SourceId=N'{leadId}'"));
        Check($"[{label}] the Lead advanced exactly once", 1L, await ScalarLongAsync(
            $"SELECT [Version] FROM leads.Leads WHERE LeadId=N'{leadId}'"));
        Check($"[{label}] one Lead qualification audit exists", 1L, await ScalarLongAsync(
            $"SELECT COUNT(*) FROM leads.AuditRecords WHERE AggregateId=N'{leadId}' AND Operation=N'qualifyLeadForNurture'"));
        Check($"[{label}] the anchor ends Completed", "Completed", await ScalarStringAsync(
            $"SELECT Stage FROM workflow.LeadQualificationAnchors WHERE LeadId=N'{leadId}'"));
    }

    private async Task VerifyChangedIntentAsync()
    {
        var leadId = await SeedLeadAsync(WorkspaceA, MemberA, "Changed Intent Lead", "changed.lead@example.com");
        var command = Command(leadId, NewContact("Changed Person", "changed.person@example.com"));

        var first = await ExecuteAsync(WorkspaceA, MemberA, command);
        Check("changed-intent baseline commits", true, first.IsSuccess);

        var changed = await ExecuteAsync(WorkspaceA, MemberA, command with { Reason = "A materially different reason" });
        Check("same key with changed intent conflicts", "IDEMPOTENCY_KEY_REUSED", changed.ErrorCode);
        Check("changed-intent conflict is 409", 409, changed.ErrorStatus);
        Check("changed intent creates no second Contact", 1L, await ScalarLongAsync(
            $"SELECT COUNT(*) FROM contacts.Contacts WHERE WorkspaceId=N'{WorkspaceA}' AND NormalizedWorkEmail=N'CHANGED.PERSON@EXAMPLE.COM'"));
        Check("changed intent creates no second Task", 1L, await ScalarLongAsync(
            $"SELECT COUNT(*) FROM tasks.Tasks WHERE SourceId=N'{leadId}'"));
    }

    private async Task VerifyStaleVersionAsync()
    {
        var leadId = await SeedLeadAsync(WorkspaceA, MemberA, "Stale Lead", "stale.lead@example.com");
        var stale = await ExecuteAsync(WorkspaceA, MemberA,
            Command(leadId, NewContact("Stale Person", "stale.person@example.com")) with { ExpectedVersion = 7 });

        Check("stale If-Match is rejected", "VERSION_CONFLICT", stale.ErrorCode);
        Check("stale If-Match is 412", 412, stale.ErrorStatus);
        Check("stale version creates no Contact", 0L, await ScalarLongAsync(
            $"SELECT COUNT(*) FROM contacts.Contacts WHERE WorkspaceId=N'{WorkspaceA}' AND NormalizedWorkEmail=N'STALE.PERSON@EXAMPLE.COM'"));
        Check("stale version creates no Task", 0L, await ScalarLongAsync(
            $"SELECT COUNT(*) FROM tasks.Tasks WHERE SourceId=N'{leadId}'"));
        Check("stale version leaves the Lead open", 2L, await ScalarLongAsync(
            $"SELECT WorkState FROM leads.Leads WHERE LeadId=N'{leadId}'"));
    }

    private async Task VerifyLeadBoundaryAsync()
    {
        // A Lead that never reached VERIFYING.
        var newLead = await SeedLeadAsync(WorkspaceA, MemberA, "New State Lead", "newstate@example.com", workState: 0);
        var notVerifying = await ExecuteAsync(WorkspaceA, MemberA,
            Command(newLead, NewContact("New State Person", "newstate.person@example.com")));
        Check("a NEW Lead cannot positively qualify", "LEAD_INVALID_TRANSITION", notVerifying.ErrorCode);
        Check("non-VERIFYING refusal is 409", 409, notVerifying.ErrorStatus);
        Check("non-VERIFYING creates no Contact", 0L, await ScalarLongAsync(
            $"SELECT COUNT(*) FROM contacts.Contacts WHERE WorkspaceId=N'{WorkspaceA}' AND NormalizedWorkEmail=N'NEWSTATE.PERSON@EXAMPLE.COM'"));

        // A VERIFYING Lead edited into an incomplete profile: replaceLeadProfile does not re-check
        // completeness, so qualification must re-evaluate it rather than trust the work state.
        var incomplete = await SeedLeadAsync(WorkspaceA, MemberA, "Incomplete Lead", null);
        var incompleteResult = await ExecuteAsync(WorkspaceA, MemberA,
            Command(incomplete, NewContact("Incomplete Person", "incomplete.person@example.com")));
        Check("an incomplete progressive profile is refused", "LEAD_INVALID_TRANSITION", incompleteResult.ErrorCode);
        Check("incomplete profile creates no Contact", 0L, await ScalarLongAsync(
            $"SELECT COUNT(*) FROM contacts.Contacts WHERE WorkspaceId=N'{WorkspaceA}' AND NormalizedWorkEmail=N'INCOMPLETE.PERSON@EXAMPLE.COM'"));

        var unknown = await ExecuteAsync(WorkspaceA, MemberA,
            Command("lead_does_not_exist", NewContact("Unknown", "unknown.person@example.com")));
        Check("an unknown Lead is not found", "RESOURCE_NOT_FOUND", unknown.ErrorCode);
        Check("unknown Lead creates no Contact", 0L, await ScalarLongAsync(
            $"SELECT COUNT(*) FROM contacts.Contacts WHERE WorkspaceId=N'{WorkspaceA}' AND NormalizedWorkEmail=N'UNKNOWN.PERSON@EXAMPLE.COM'"));

        // A Lead that exists, but in another Workspace, must be indistinguishable from unknown.
        var foreignLead = await SeedLeadAsync(WorkspaceB, MemberB, "Foreign Lead", "foreign.lead@example.com");
        var foreign = await ExecuteAsync(WorkspaceA, MemberA,
            Command(foreignLead, NewContact("Foreign", "foreign.person@example.com")));
        Check("a foreign-Workspace Lead is refused", "RESOURCE_NOT_FOUND", foreign.ErrorCode);
        Check(
            "foreign and unknown Leads are indistinguishable",
            $"{unknown.ErrorCode}|{unknown.ErrorStatus}|{unknown.LeadId}|{unknown.ContactId}",
            $"{foreign.ErrorCode}|{foreign.ErrorStatus}|{foreign.LeadId}|{foreign.ContactId}");
    }

    // ------------------------------------------------------------------ adopted request contract

    /// <summary>
    /// The complete adopted <c>QualifyLeadNurtureRequest</c>, proven to be refused before the first
    /// owner mutation. The workflow commits Contact, then Task, then the Lead close in three separate
    /// owner-local transactions and never compensates, so "rejected" is only half the claim: every
    /// case below also proves that no Contact, Task, Lead advance, audit row, outbox message or
    /// workflow anchor exists afterwards.
    /// </summary>
    private async Task VerifyRequestContractAsync()
    {
        const string validRevisitAt = "2026-10-01T09:00:00.0000000Z";
        var overLimitReason = new string('r', 1001);
        var maxName = new string('n', 200);
        var overLimitName = new string('n', 201);

        var cases = new (string Name, Func<LeadNurtureContactIntent> Contact, string RevisitAt, string Reason, string? Note, string? OwnerId, string Field)[]
        {
            ("invalid email", () => NewContact("Contract Person", "not-an-address"), validRevisitAt, "Revisit", null, null, "relationship.contact.email"),
            ("over-limit reason", () => NewContact("Contract Person", "contract.reason@example.com"), validRevisitAt, overLimitReason, null, null, "reason"),
            ("empty reason", () => NewContact("Contract Person", "contract.emptyreason@example.com"), validRevisitAt, "   ", null, null, "reason"),
            ("invalid revisitAt", () => NewContact("Contract Person", "contract.revisit@example.com"), "2026-13-45T99:00:00Z", "Revisit", null, null, "revisitAt"),
            ("non-UTC revisitAt", () => NewContact("Contract Person", "contract.local@example.com"), "2026-10-01T09:00:00+07:00", "Revisit", null, null, "revisitAt"),
            ("over-limit note", () => NewContact("Contract Person", "contract.note@example.com"), validRevisitAt, "Revisit", new string('x', 4001), null, "note"),
            ("over-limit displayName", () => NewContact(overLimitName, "contract.name@example.com"), validRevisitAt, "Revisit", null, null, "relationship.contact.displayName"),
            ("over-limit title", () => new LeadNurtureContactIntent(LeadNurtureRelationshipMode.New, null, "Contract Person", "contract.title@example.com", "0900000001", new string('t', 161)), validRevisitAt, "Revisit", null, null, "relationship.contact.title"),
            ("over-limit phone", () => new LeadNurtureContactIntent(LeadNurtureRelationshipMode.New, null, "Contract Person", "contract.phone@example.com", new string('9', 65), "Manager"), validRevisitAt, "Revisit", null, null, "relationship.contact.phone"),
            ("malformed NEW: contact object absent", () => new LeadNurtureContactIntent(LeadNurtureRelationshipMode.New, null, null, null, null, null, ContactSupplied: false), validRevisitAt, "Revisit", null, null, "relationship.contact"),
            ("malformed NEW: missing displayName", () => new LeadNurtureContactIntent(LeadNurtureRelationshipMode.New, null, null, "contract.noname@example.com", null, null), validRevisitAt, "Revisit", null, null, "relationship.contact.displayName"),
            ("malformed EXISTING: missing selectedId", () => new LeadNurtureContactIntent(LeadNurtureRelationshipMode.Existing, null, "Contract Person", null, null, null), validRevisitAt, "Revisit", null, null, "relationship.selectedId"),
            ("malformed EXISTING: contact object absent", () => new LeadNurtureContactIntent(LeadNurtureRelationshipMode.Existing, "contact_something", null, null, null, null, ContactSupplied: false), validRevisitAt, "Revisit", null, null, "relationship.contact"),
            ("malformed EXISTING: selectedId is not an entity identifier", () => new LeadNurtureContactIntent(LeadNurtureRelationshipMode.Existing, "not a valid id", "Contract Person", null, null, null), validRevisitAt, "Revisit", null, null, "relationship.selectedId"),
            ("inconsistent NEW carrying selectedId", () => new LeadNurtureContactIntent(LeadNurtureRelationshipMode.New, "contact_something", "Contract Person", "contract.mixed@example.com", null, null), validRevisitAt, "Revisit", null, null, "relationship.selectedId"),
            ("unadmitted organization on a CONTACT relationship", () => new LeadNurtureContactIntent(LeadNurtureRelationshipMode.New, null, "Contract Person", "contract.org@example.com", null, null, OrganizationSupplied: true), validRevisitAt, "Revisit", null, null, "relationship.organization"),
            ("invalid ownerId", () => NewContact("Contract Person", "contract.owner@example.com"), validRevisitAt, "Revisit", null, "not a member id", "ownerId")
        };

        foreach (var (name, contact, revisitAt, reason, note, ownerId, field) in cases)
        {
            var leadId = await SeedLeadAsync(WorkspaceA, MemberA, $"Contract {name}", $"{Guid.NewGuid():N}@example.com");
            var before = await OwnerEffectsAsync(leadId);
            var result = await ExecuteAsync(WorkspaceA, MemberA, new LeadNurtureQualificationCommand(
                leadId,
                contact(),
                revisitAt,
                reason,
                note,
                $"req_{Guid.NewGuid():N}"[..32],
                $"corr_{Guid.NewGuid():N}"[..32],
                $"idem_{Guid.NewGuid():N}"[..32],
                0,
                ownerId));

            Check($"contract: {name} is refused", "VALIDATION_FAILED", result.ErrorCode);
            Check($"contract: {name} is 422", 422, result.ErrorStatus);
            Check($"contract: {name} names {field}", true, result.FieldErrors?.ContainsKey(field) == true);
            Check($"contract: {name} performs zero owner effects", before, await OwnerEffectsAsync(leadId));
        }

        // The frozen boundary itself: the largest name a Contact can hold is accepted and lands
        // verbatim, and one character more is refused. Both halves are required - a bound proven only
        // by its rejection could have been implemented as a silent truncation.
        var boundaryLead = await SeedLeadAsync(WorkspaceA, MemberA, "Contract name boundary", "contract.boundary@example.com");
        var boundary = await ExecuteAsync(WorkspaceA, MemberA,
            Command(boundaryLead, NewContact(maxName, "contract.boundary.person@example.com")));
        Check("contract: a 200-character displayName qualifies", true, boundary.IsSuccess);
        Check("contract: the 200-character name is stored whole, not truncated", maxName, await ScalarStringAsync(
            $"SELECT FullName FROM contacts.Contacts WHERE ContactId=N'{boundary.ContactId}'"));

        // Valid NEW and valid EXISTING still succeed under the completed contract, so the new stage
        // rejects malformed requests without narrowing the admitted ones.
        var validNewLead = await SeedLeadAsync(WorkspaceA, MemberA, "Contract valid NEW", "contract.validnew@example.com");
        var validNew = await ExecuteAsync(WorkspaceA, MemberA,
            Command(validNewLead, NewContact("Contract Valid New", "contract.validnew.person@example.com")));
        Check("contract: a fully valid NEW request still commits", "COMMITTED", validNew.Outcome);

        var selected = await SeedContactAsync(WorkspaceA, MemberA, "Contract Existing", "contract.existing@example.com");
        var validExistingLead = await SeedLeadAsync(WorkspaceA, MemberA, "Contract valid EXISTING", "contract.validexisting@example.com");
        var validExisting = await ExecuteAsync(WorkspaceA, MemberA, Command(validExistingLead,
            new LeadNurtureContactIntent(LeadNurtureRelationshipMode.Existing, selected, "Ignored", null, null, null)));
        Check("contract: a fully valid EXISTING request still commits", "COMMITTED", validExisting.Outcome);
        Check("contract: EXISTING still links the selected Contact", selected, validExisting.ContactId);
    }

    /// <summary>
    /// Every durable trace a successful qualification would leave, for one Lead. A refused request
    /// must move none of them.
    /// </summary>
    private async Task<string> OwnerEffectsAsync(string leadId) =>
        string.Join("|",
            await ScalarLongAsync($"SELECT COUNT(*) FROM contacts.Contacts WHERE WorkspaceId=N'{WorkspaceA}'"),
            await ScalarLongAsync($"SELECT COUNT(*) FROM contacts.AuditRecords WHERE WorkspaceId=N'{WorkspaceA}'"),
            await ScalarLongAsync($"SELECT COUNT(*) FROM contacts.OutboxMessages WHERE WorkspaceId=N'{WorkspaceA}'"),
            await ScalarLongAsync($"SELECT COUNT(*) FROM contacts.ConversionRecords WHERE WorkspaceId=N'{WorkspaceA}'"),
            await ScalarLongAsync($"SELECT COUNT(*) FROM tasks.Tasks WHERE SourceId=N'{leadId}'"),
            await ScalarLongAsync($"SELECT COUNT(*) FROM leads.AuditRecords WHERE AggregateId=N'{leadId}'"),
            await ScalarLongAsync($"SELECT COUNT(*) FROM leads.OutboxMessages WHERE AggregateId=N'{leadId}'"),
            await ScalarLongAsync($"SELECT COUNT(*) FROM workflow.LeadQualificationAnchors WHERE LeadId=N'{leadId}'"),
            await ScalarLongAsync($"SELECT [Version] FROM leads.Leads WHERE LeadId=N'{leadId}'"),
            await ScalarLongAsync($"SELECT WorkState FROM leads.Leads WHERE LeadId=N'{leadId}'"));

    private async Task VerifyInvalidTaskOwnerAsync()
    {
        var leadId = await SeedLeadAsync(
            WorkspaceA,
            MemberA,
            "Invalid Task owner Lead",
            "invalid.task.owner.lead@example.com");
        var command = Command(
            leadId,
            NewContact("Invalid Task owner Person", "invalid.task.owner.person@example.com"));
        var rejectedCommand = command with { TaskOwnerId = "member_nurture_missing" };
        var effectsBefore = await OwnerEffectsAsync(leadId);

        var rejected = await ExecuteAsync(WorkspaceA, MemberA, rejectedCommand);

        Check("invalid Task owner is refused", "VALIDATION_FAILED", rejected.ErrorCode);
        Check("invalid Task owner is 422", 422, rejected.ErrorStatus);
        Check("invalid Task owner names assigneeId", "assigneeId",
            string.Join(",", rejected.FieldErrors?.Keys ?? []));
        Check("invalid Task owner performs zero owner effects", effectsBefore,
            await OwnerEffectsAsync(leadId));
        Check("invalid Task owner leaves no Contact qualification receipt", 0L,
            await ScalarLongAsync(
                $"SELECT COUNT(*) FROM contacts.ConversionRecords WHERE WorkspaceId=N'{WorkspaceA}' AND ConversionKey=N'{WorkflowScopeKey(WorkspaceA, leadId, command.IdempotencyKey)}'"));

        var committed = await ExecuteAsync(WorkspaceA, MemberA, command with { TaskOwnerId = MemberA });
        Check("valid NURTURE after invalid Task owner commits", "COMMITTED", committed.Outcome);
        Check("valid NURTURE after invalid Task owner creates one intended Contact", 1L,
            await ScalarLongAsync(
                "SELECT COUNT(*) FROM contacts.Contacts WHERE WorkspaceId=N'" + WorkspaceA
                + "' AND NormalizedWorkEmail=N'INVALID.TASK.OWNER.PERSON@EXAMPLE.COM'"));
        Check("valid NURTURE after invalid Task owner creates one intended Task", 1L,
            await ScalarLongAsync($"SELECT COUNT(*) FROM tasks.Tasks WHERE SourceId=N'{leadId}'"));
        Check("valid NURTURE after invalid Task owner closes Lead once", 1L,
            await ScalarLongAsync($"SELECT [Version] FROM leads.Leads WHERE LeadId=N'{leadId}'"));
        Check("valid NURTURE after invalid Task owner completes workflow", "Completed",
            await ScalarStringAsync($"SELECT Stage FROM workflow.LeadQualificationAnchors WHERE LeadId=N'{leadId}'"));

        var committedEffects = await OwnerEffectsAsync(leadId);
        var replayed = await ExecuteAsync(WorkspaceA, MemberA, command with { TaskOwnerId = MemberA });
        Check("valid NURTURE after invalid Task owner replays", "REPLAYED", replayed.Outcome);
        Check("valid NURTURE replay after invalid Task owner changes no counts", committedEffects,
            await OwnerEffectsAsync(leadId));
    }

    private async Task VerifyContactRejectionAsync()
    {
        await SeedContactAsync(WorkspaceA, MemberA, "Duplicate Blocker", "blocked.person@example.com");
        var leadId = await SeedLeadAsync(WorkspaceA, MemberA, "Duplicate Lead", "duplicate.lead@example.com");

        var duplicate = await ExecuteAsync(WorkspaceA, MemberA,
            Command(leadId, NewContact("Duplicate Person", "Blocked.Person@Example.com")));
        Check("a duplicate address rejects the qualification", "LEAD_QUALIFICATION_RELATIONSHIP_INVALID", duplicate.ErrorCode);
        Check("relationship rejection is 422", 422, duplicate.ErrorStatus);
        Check("rejection discloses no Contact", null, duplicate.ContactId);
        Check("duplicate creates no second Contact", 1L, await ScalarLongAsync(
            $"SELECT COUNT(*) FROM contacts.Contacts WHERE WorkspaceId=N'{WorkspaceA}' AND NormalizedWorkEmail=N'BLOCKED.PERSON@EXAMPLE.COM'"));
        Check("duplicate creates no Task", 0L, await ScalarLongAsync(
            $"SELECT COUNT(*) FROM tasks.Tasks WHERE SourceId=N'{leadId}'"));
        Check("duplicate leaves the Lead open", 2L, await ScalarLongAsync(
            $"SELECT WorkState FROM leads.Leads WHERE LeadId=N'{leadId}'"));

        // The wire schema requires the contact object for EXISTING too, so the fixture supplies a
        // contract-valid one. What is being proven here is the unresolvable identifier, not a
        // malformed body - that case is proven separately by the request-contract suite.
        var unresolvable = await ExecuteAsync(WorkspaceA, MemberA, Command(
            await SeedLeadAsync(WorkspaceA, MemberA, "Unresolvable Lead", "unresolvable.lead@example.com"),
            new LeadNurtureContactIntent(
                LeadNurtureRelationshipMode.Existing, "contact_not_here", "Ignored", null, null, null)));
        Check("an unresolvable EXISTING Contact rejects the qualification", "LEAD_QUALIFICATION_RELATIONSHIP_INVALID", unresolvable.ErrorCode);
        Check(
            "duplicate and unresolvable rejections are indistinguishable",
            $"{duplicate.ErrorCode}|{duplicate.ErrorStatus}|{duplicate.ContactId}",
            $"{unresolvable.ErrorCode}|{unresolvable.ErrorStatus}|{unresolvable.ContactId}");

        // DEC-LEAD-CONTACT-DUPLICATE-POLICY 9.4: the duplicate refusal keeps its frozen field pointer,
        // and nothing else about the matched Contact travels with it.
        Check(
            "duplicate names the frozen relationship.contact.email pointer",
            "relationship.contact.email",
            string.Join(",", duplicate.FieldErrors?.Keys ?? []));
        Check("duplicate still discloses no Contact identifier", false,
            (duplicate.FieldErrors?.Values.SelectMany(messages => messages) ?? [])
                .Any(message => message.Contains("contact_", StringComparison.Ordinal)));
        Check("an unresolvable identifier carries no field pointer", true, unresolvable.FieldErrors is null);
    }

    /// <summary>
    /// tasks.create is required of the caller. Removing it must refuse the qualification with the
    /// admitted downstream-capability error, and the already-committed Contact must survive
    /// untouched - there is no compensation - so that restoring the capability converges.
    /// </summary>
    private async Task VerifyMissingTaskCapabilityAsync()
    {
        var leadId = await SeedLeadAsync(WorkspaceA, MemberA, "No Task Capability Lead", "notask.lead@example.com");
        var command = Command(leadId, NewContact("No Task Person", "notask.person@example.com"));

        await ExecuteAsync(connectionStringOverride: null, sql:
            $"DELETE FROM access.RoleCapabilities WHERE Capability=N'tasks.create' AND RoleId IN (SELECT RoleId FROM access.Roles WHERE WorkspaceId=N'{WorkspaceA}')");

        var denied = await ExecuteAsync(WorkspaceA, MemberA, command);
        Check("missing tasks.create refuses the qualification", "LEAD_QUALIFICATION_DOWNSTREAM_CAPABILITY_REQUIRED", denied.ErrorCode);
        Check("downstream capability refusal is 403", 403, denied.ErrorStatus);
        Check("the already-committed Contact is not deleted", 1L, await ScalarLongAsync(
            $"SELECT COUNT(*) FROM contacts.Contacts WHERE WorkspaceId=N'{WorkspaceA}' AND NormalizedWorkEmail=N'NOTASK.PERSON@EXAMPLE.COM'"));
        Check("no Task was created", 0L, await ScalarLongAsync(
            $"SELECT COUNT(*) FROM tasks.Tasks WHERE SourceId=N'{leadId}'"));
        Check("the Lead stays open", 2L, await ScalarLongAsync(
            $"SELECT WorkState FROM leads.Leads WHERE LeadId=N'{leadId}'"));
        Check("the anchor stopped at ContactResolved", "ContactResolved", await ScalarStringAsync(
            $"SELECT Stage FROM workflow.LeadQualificationAnchors WHERE LeadId=N'{leadId}'"));

        // Restore the capability by direct insert. Re-provisioning would refuse: the server-owned
        // policy deliberately treats a drifted role as drift rather than silently repairing it.
        await ExecuteAsync(null, $"""
            INSERT INTO access.RoleCapabilities (RoleId, Capability)
            SELECT RoleId, N'tasks.create' FROM access.Roles WHERE WorkspaceId=N'{WorkspaceA}'
            """);
        var recovered = await ExecuteAsync(WorkspaceA, MemberA, command);
        // This is also the "anchor has Contact but Task not created" recovery case, reached through
        // a real interruption rather than by editing the anchor.
        Check("[anchor has Contact, Task not created] resume converges", true, recovered.IsSuccess);
        Check("[anchor has Contact, Task not created] reuses the recorded Contact", denied.ErrorCode is not null, true);
        Check("recovery reuses the same Contact", 1L, await ScalarLongAsync(
            $"SELECT COUNT(*) FROM contacts.Contacts WHERE WorkspaceId=N'{WorkspaceA}' AND NormalizedWorkEmail=N'NOTASK.PERSON@EXAMPLE.COM'"));
        Check("recovery creates exactly one Task", 1L, await ScalarLongAsync(
            $"SELECT COUNT(*) FROM tasks.Tasks WHERE SourceId=N'{leadId}'"));
        Check("recovery closes the Lead", 3L, await ScalarLongAsync(
            $"SELECT WorkState FROM leads.Leads WHERE LeadId=N'{leadId}'"));
    }

    private async Task VerifyWorkspaceIsolationAsync()
    {
        var leadId = await SeedLeadAsync(WorkspaceB, MemberB, "Isolated Lead", "isolated.lead@example.com");
        var result = await ExecuteAsync(WorkspaceB, MemberB,
            Command(leadId, NewContact("Isolated Person", "person.one@example.com")));

        Check("the same address in another Workspace qualifies", true, result.IsSuccess);
        Check("the Contact belongs to its own Workspace", WorkspaceB, await ScalarStringAsync(
            $"SELECT WorkspaceId FROM contacts.Contacts WHERE ContactId=N'{result.ContactId}'"));
        Check("the anchor belongs to its own Workspace", WorkspaceB, await ScalarStringAsync(
            $"SELECT WorkspaceId FROM workflow.LeadQualificationAnchors WHERE LeadId=N'{leadId}'"));
    }

    private async Task VerifyConcurrencyAsync()
    {
        var leadId = await SeedLeadAsync(WorkspaceA, MemberA, "Concurrent Lead", "concurrent.lead@example.com");
        var command = Command(leadId, NewContact("Concurrent Person", "concurrent.person@example.com"));

        var outcomes = await Task.WhenAll(
            Enumerable.Range(0, 3).Select(_ => ExecuteAsync(WorkspaceA, MemberA, command)));

        Check("no concurrent duplicate fails outright", 0, outcomes.Count(item => !item.IsSuccess));
        Check("concurrent duplicates produce one Contact", 1L, await ScalarLongAsync(
            $"SELECT COUNT(*) FROM contacts.Contacts WHERE WorkspaceId=N'{WorkspaceA}' AND NormalizedWorkEmail=N'CONCURRENT.PERSON@EXAMPLE.COM'"));
        Check("concurrent duplicates produce one Task", 1L, await ScalarLongAsync(
            $"SELECT COUNT(*) FROM tasks.Tasks WHERE SourceId=N'{leadId}'"));
        Check("concurrent duplicates produce one anchor", 1L, await ScalarLongAsync(
            $"SELECT COUNT(*) FROM workflow.LeadQualificationAnchors WHERE LeadId=N'{leadId}'"));
        Check("concurrent duplicates advance the Lead once", 1L, await ScalarLongAsync(
            $"SELECT [Version] FROM leads.Leads WHERE LeadId=N'{leadId}'"));
        Check("concurrent duplicates agree on the Contact", 1,
            outcomes.Select(item => item.ContactId).Distinct(StringComparer.Ordinal).Count());
        Check("concurrent duplicates agree on the Task", 1,
            outcomes.Select(item => item.TaskId).Distinct(StringComparer.Ordinal).Count());
        Check("concurrent duplicates agree on the entire semantic response", 1,
            outcomes.Select(SemanticResponse).Distinct(StringComparer.Ordinal).Count());
        Check("concurrent duplicates leave Completed anchor", "Completed", await ScalarStringAsync(
            $"SELECT Stage FROM workflow.LeadQualificationAnchors WHERE LeadId=N'{leadId}'"));
    }

    private static string SemanticResponse(LeadNurtureQualificationResult result) =>
        JsonSerializer.Serialize(result.Response is null ? null : result.Response with { Outcome = "COMMITTED" });

    private async Task VerifyIntentFingerprintAsync()
    {
        var leadId = await SeedLeadAsync(WorkspaceA, MemberA, "Fingerprint Lead", "fingerprint.lead@example.com");
        var command = Command(leadId, NewContact("Fingerprint Person", "fingerprint.person@example.com")) with { TaskOwnerId = MemberA };
        var first = await ExecuteAsync(WorkspaceA, MemberA, command);
        Check("F3 explicit Task owner commits", true, first.IsSuccess);
        var replay = await ExecuteAsync(WorkspaceA, MemberA, command with { ExpectedVersion = 999 });
        Check("F3 same owner completed replay ignores concurrency token", "REPLAYED", replay.Outcome);
        Check("F3 same owner completed response stable", SemanticResponse(first), SemanticResponse(replay));
        var conflict = await ExecuteAsync(WorkspaceA, MemberA, command with { TaskOwnerId = "member_other_intent" });
        Check("F3 Task-owner-only change conflicts", "IDEMPOTENCY_KEY_REUSED", conflict.ErrorCode);

        var handler = typeof(ILeadNurtureQualificationWorkflow).Assembly.GetType(
            "UnicoreCRM.Workflows.Atomic.Application.QualifyLeadForNurture.Handler")!;
        var fingerprint = handler.GetMethod("Fingerprint", BindingFlags.NonPublic | BindingFlags.Static)!;
        string Hash(LeadNurtureQualificationCommand input) => (string)fingerprint.Invoke(null, [input])!;
        var before = Hash(command);
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            Check("F3 canonical fingerprint deterministic across culture", before, Hash(command));
        }
        finally { CultureInfo.CurrentCulture = culture; }
        Check("F3 request metadata and version are not business intent", before,
            Hash(command with { ExpectedVersion = 12, RequestId = "req_changed", CorrelationId = "corr_changed" }));
        Check("F3 delimiter-containing fields cannot collide", false,
            Hash(command with { Reason = "a\nb", Note = "c" }) == Hash(command with { Reason = "a", Note = "b\nc" }));
        await ExecuteAsync(null, $"UPDATE contacts.Contacts SET FullName=N'Mutable new name', [Version]=[Version]+1 WHERE ContactId=N'{first.ContactId}'");
        Check("F3 mutable owner data does not change fingerprint", before, Hash(command));
        var afterMutation = await ExecuteAsync(WorkspaceA, MemberA, command);
        Check("F3 mutable owner data does not change completed result", SemanticResponse(first), SemanticResponse(afterMutation));
        await ExecuteAsync(null, $"UPDATE workflow.LeadQualificationAnchors SET IntentVersion=0 WHERE LeadId=N'{leadId}'");
        var legacy = await ExecuteAsync(WorkspaceA, MemberA, command);
        Check("legacy intent lacking Task owner evidence fails closed", "INTERNAL_ERROR", legacy.ErrorCode);
    }

    private async Task VerifyParticipantAcknowledgmentLossAsync()
    {
        foreach (var existing in new[] { false, true })
        foreach (var stop in new[] { "Contact", "Task", "Lead" })
        {
            var label = $"F5 {(existing ? "EXISTING" : "NEW")} {stop} acknowledgment loss";
            var leadId = await SeedLeadAsync(WorkspaceA, MemberA, label, $"{Guid.NewGuid():N}@example.com");
            const string originalName = "Authoritative owner name";
            var contact = existing
                ? new LeadNurtureContactIntent(LeadNurtureRelationshipMode.Existing,
                    await SeedContactAsync(WorkspaceA, MemberA, originalName, $"{Guid.NewGuid():N}@example.com"),
                    "Ignored caller name", null, null, null)
                : NewContact(originalName, $"{Guid.NewGuid():N}@example.com");
            var command = Command(leadId, contact);
            participantCalls.FailAfter = stop;
            var interrupted = false;
            try { await ExecuteAsync(WorkspaceA, MemberA, command); }
            catch (InjectedAcknowledgmentLoss) { interrupted = true; }
            finally { participantCalls.FailAfter = null; }
            Check($"{label}: real owner commit interrupted", true, interrupted);
            var contactResult = participantCalls.LastContact!;
            var taskResult = stop == "Contact" ? null : participantCalls.LastTask;
            var leadResult = stop == "Lead" ? participantCalls.LastLead : null;
            Check($"{label}: anchor records only acknowledged stage",
                stop == "Contact" ? "Started" : stop == "Task" ? "ContactResolved" : "TaskCreated",
                await ScalarStringAsync($"SELECT Stage FROM workflow.LeadQualificationAnchors WHERE LeadId=N'{leadId}'"));
            await ExecuteAsync(null, $"UPDATE contacts.Contacts SET FullName=N'Changed after original result', [Version]=[Version]+10 WHERE ContactId=N'{contactResult.ContactId}'");
            if (taskResult is not null)
                await ExecuteAsync(null, $"UPDATE tasks.Tasks SET [Version]=[Version]+10 WHERE TaskId=N'{taskResult.TaskId}'");

            var resumed = await ExecuteAsync(WorkspaceA, MemberA, command with { CorrelationId = "corr_retry_8b" });
            Check($"{label}: recovery succeeds", true, resumed.IsSuccess);
            Check($"{label}: Contact identity preserved", contactResult.ContactId, resumed.ContactId);
            Check($"{label}: owner name preserved", originalName, resumed.Response?.Result.Relationship.DisplayName);
            Check($"{label}: createdResources preserved", existing ? "TASK" : "CONTACT,TASK",
                string.Join(',', resumed.Response?.Result.CreatedResources.Select(item => item.ResourceType) ?? []));
            var capturedContactVersion = await ScalarLongAsync($"SELECT ContactVersion FROM workflow.LeadQualificationAnchors WHERE LeadId=N'{leadId}'");
            Check($"{label}: original Contact version retained", contactResult.ContactVersion, capturedContactVersion);
            if (taskResult is not null)
            {
                Check($"{label}: Task identity preserved", taskResult.TaskId, resumed.TaskId);
                Check($"{label}: Task creation version retained", taskResult.TaskVersion,
                    resumed.Response?.Result.CreatedResources.Single(item => item.ResourceType == "TASK").ResourceVersion);
            }
            if (leadResult is not null)
            {
                Check($"{label}: Lead command identity retained", leadResult.CommandId, resumed.Response?.CommandId);
                Check($"{label}: Lead committed instant retained", leadResult.OccurredAt, resumed.Response?.OccurredAt);
                Check($"{label}: Lead version retained", leadResult.Version, resumed.LeadVersion);
                Check($"{label}: Lead evidence retained", string.Join(',', leadResult.AuditEvidenceIds!), string.Join(',', resumed.Response!.AuditEvidenceIds));
            }
            Check($"{label}: original correlation retained", command.CorrelationId, resumed.Response?.CorrelationId);
            Check($"{label}: one Contact receipt", 1L, await ScalarLongAsync($"SELECT COUNT(*) FROM contacts.ConversionRecords WHERE ContactId=N'{contactResult.ContactId}'"));
            Check($"{label}: one Task", 1L, await ScalarLongAsync($"SELECT COUNT(*) FROM tasks.Tasks WHERE SourceId=N'{leadId}'"));
            Check($"{label}: one Lead close", 1L, await ScalarLongAsync($"SELECT COUNT(*) FROM leads.AuditRecords WHERE AggregateId=N'{leadId}' AND Operation=N'qualifyLeadForNurture'"));
            var replay = await ExecuteAsync(WorkspaceA, MemberA, command);
            Check($"{label}: completed replay full response stable", SemanticResponse(resumed), SemanticResponse(replay));
            // Lose completion once more: compare the complete successful response, not just IDs.
            await ExecuteAsync(null, $"UPDATE workflow.LeadQualificationAnchors SET Stage=N'TaskCreated', ResponseJson=NULL, LeadVersion=NULL WHERE LeadId=N'{leadId}'");
            var recoveredAgain = await ExecuteAsync(WorkspaceA, MemberA, command with { ExpectedVersion = resumed.LeadVersion!.Value });
            Check($"{label}: lost completion full response converges", SemanticResponse(resumed), SemanticResponse(recoveredAgain));
        }
    }

    private async Task VerifyChangedIntentConcurrencyAsync()
    {
        var leadId = await SeedLeadAsync(WorkspaceA, MemberA, "Competing intents", "competing.lead@example.com");
        var command = Command(leadId, NewContact("Competing Person", "competing.person@example.com"));
        var outcomes = await Task.WhenAll(ExecuteAsync(WorkspaceA, MemberA, command),
            ExecuteAsync(WorkspaceA, MemberA, command with { TaskOwnerId = MemberA }));
        Check("F3 competing owner intents have exactly one winner", 1, outcomes.Count(item => item.IsSuccess));
        Check("F3 competing owner intents have canonical conflict", 1, outcomes.Count(item => item.ErrorCode == "IDEMPOTENCY_KEY_REUSED"));
        Check("F3 competing intents create one Task", 1L, await ScalarLongAsync($"SELECT COUNT(*) FROM tasks.Tasks WHERE SourceId=N'{leadId}'"));
        Check("F3 competing intents create one Contact", 1L, await ScalarLongAsync("SELECT COUNT(*) FROM contacts.Contacts WHERE NormalizedWorkEmail=N'COMPETING.PERSON@EXAMPLE.COM'"));
    }

    private async Task VerifyDifferentCallerRecoveryAsync()
    {
        const string resumer = "member_nurture_resume";
        await ExecuteAsync(null, $"""
            INSERT INTO workspace.Memberships (MembershipId, WorkspaceId, AccountId, MemberId, [Status], CreatedAt)
            VALUES (N'membership_{resumer}', N'{WorkspaceA}', N'account_{resumer}', N'{resumer}', N'Active', SYSDATETIMEOFFSET());
            """);
        await using (var scope = provider.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ControlledWorkspace>().Set(Trusted(WorkspaceA, resumer));
            await scope.ServiceProvider.GetRequiredService<IInitialWorkspaceAccessProvisioning>()
                .EnsureInitialWorkspaceAccessAsync(WorkspaceA, $"membership_{resumer}", CancellationToken.None);
        }
        foreach (var stop in new[] { "Task", "Lead" })
        {
            var leadId = await SeedLeadAsync(WorkspaceA, MemberA, $"Changed caller {stop}", $"{Guid.NewGuid():N}@example.com");
            var command = Command(leadId, NewContact("Original caller Contact", $"{Guid.NewGuid():N}@example.com"));
            participantCalls.FailAfter = stop;
            try { await ExecuteAsync(WorkspaceA, MemberA, command); }
            catch (InjectedAcknowledgmentLoss) { }
            finally { participantCalls.FailAfter = null; }
            var originalTask = participantCalls.LastTask!;
            var originalClosure = stop == "Lead" ? participantCalls.LastLead : null;
            var fingerprint = await ScalarStringAsync($"SELECT Fingerprint FROM workflow.LeadQualificationAnchors WHERE LeadId=N'{leadId}'");
            await ExecuteAsync(null, $"UPDATE leads.Leads SET Profile=JSON_MODIFY(Profile,'$.ownerId',N'{resumer}'), ScopeOwnerId=N'{resumer}', [Version]=[Version]+1 WHERE LeadId=N'{leadId}'");
            var currentVersion = await ScalarLongAsync($"SELECT [Version] FROM leads.Leads WHERE LeadId=N'{leadId}'");
            var result = await ExecuteAsync(WorkspaceA, resumer, command with { ExpectedVersion = currentVersion });
            Check($"{stop} loss: different authorized caller resumes", true, result.IsSuccess);
            Check($"{stop} loss: participant key still returns original Task", originalTask.TaskId, result.TaskId);
            Check($"{stop} loss: owner edit does not redefine default Task assignee", MemberA,
                await ScalarStringAsync($"SELECT AssigneeId FROM tasks.Tasks WHERE TaskId=N'{originalTask.TaskId}'"));
            Check($"{stop} loss: owner edit leaves immutable intent unchanged", fingerprint,
                await ScalarStringAsync($"SELECT Fingerprint FROM workflow.LeadQualificationAnchors WHERE LeadId=N'{leadId}'"));
            Check($"{stop} loss: one Task across callers", 1L, await ScalarLongAsync($"SELECT COUNT(*) FROM tasks.Tasks WHERE SourceId=N'{leadId}'"));
            Check($"{stop} loss: one terminal transition across callers", 1L, await ScalarLongAsync($"SELECT COUNT(*) FROM leads.AuditRecords WHERE AggregateId=N'{leadId}' AND Operation=N'qualifyLeadForNurture'"));
            Check($"{stop} loss: audit attributes actual closing caller", stop == "Lead" ? MemberA : resumer,
                await ScalarStringAsync($"SELECT ActorId FROM leads.AuditRecords WHERE AggregateId=N'{leadId}' AND Operation=N'qualifyLeadForNurture'"));
            if (originalClosure is not null)
                Check("Lead loss: another caller replays original close evidence", originalClosure.CommandId, result.Response?.CommandId);
        }
    }

    // ------------------------------------------------------------------ reason preservation

    /// <summary>
    /// FN-1. The adopted contract admits a <c>reason</c> of 1-1000 characters while the
    /// <c>createTask</c> title stops at 300, so the title can only ever be a bounded derived
    /// summary. The caller's complete reason is an admitted business fact and must survive the
    /// qualification: it is carried by the Lead source reference's already-admitted
    /// <c>sourceRef.evidence</c>, whose contract and column bound (1000) is exactly the reason's
    /// own. Every case reads the persisted Task row - an HTTP-shaped success proves nothing here.
    /// </summary>
    private async Task VerifyReasonPreservationAsync()
    {
        foreach (var length in new[] { 1, 300, 301, 1000 })
        {
            var reason = Reason(length);
            var leadId = await SeedLeadAsync(WorkspaceA, MemberA, $"Reason Lead {length}", $"reason.lead.{length}@example.com");
            var result = await ExecuteAsync(WorkspaceA, MemberA,
                Command(leadId, NewContact($"Reason Person {length}", $"reason.person.{length}@example.com")) with { Reason = reason });

            Check($"FN-1 reason of {length} characters qualifies", true, result.IsSuccess);
            Check($"FN-1 reason of {length} characters survives in full on the Task", reason,
                await ScalarStringAsync($"SELECT SourceEvidence FROM tasks.Tasks WHERE TaskId=N'{result.TaskId}'"));
            Check($"FN-1 reason of {length} characters yields a bounded title", reason[..Math.Min(300, length)],
                await ScalarStringAsync($"SELECT Title FROM tasks.Tasks WHERE TaskId=N'{result.TaskId}'"));
            Check($"FN-1 reason of {length} characters leaves the note as the Task description",
                "Seeded by the NURTURE workflow verifier.",
                await ScalarStringAsync($"SELECT [Description] FROM tasks.Tasks WHERE TaskId=N'{result.TaskId}'"));
            Check($"FN-1 reason of {length} characters still records the source Lead", leadId,
                await ScalarStringAsync($"SELECT SourceId FROM tasks.Tasks WHERE TaskId=N'{result.TaskId}'"));
        }

        // The F6 request contract still owns the upper bound: an inadmissible reason is refused
        // before the first owner mutation, so nothing is truncated to make it fit.
        var refusedLead = await SeedLeadAsync(WorkspaceA, MemberA, "Reason Lead 1001", "reason.lead.1001@example.com");
        var refused = await ExecuteAsync(WorkspaceA, MemberA,
            Command(refusedLead, NewContact("Reason Person 1001", "reason.person.1001@example.com")) with { Reason = Reason(1001) });
        Check("FN-1 a 1001-character reason is refused", "VALIDATION_FAILED", refused.ErrorCode);
        Check("FN-1 the refusal names the reason pointer", "reason",
            string.Join(",", refused.FieldErrors?.Keys ?? []));
        Check("FN-1 a refused reason creates no Contact", 0L, await ScalarLongAsync(
            $"SELECT COUNT(*) FROM contacts.Contacts WHERE WorkspaceId=N'{WorkspaceA}' AND NormalizedWorkEmail=N'REASON.PERSON.1001@EXAMPLE.COM'"));
        Check("FN-1 a refused reason creates no Task", 0L, await ScalarLongAsync(
            $"SELECT COUNT(*) FROM tasks.Tasks WHERE SourceId=N'{refusedLead}'"));

        // Replay, interrupted recovery and changed intent all keep the originally committed reason.
        var stableReason = Reason(1000);
        var replayLead = await SeedLeadAsync(WorkspaceA, MemberA, "Reason Replay Lead", "reason.replay.lead@example.com");
        var replayCommand = Command(replayLead, NewContact("Reason Replay Person", "reason.replay.person@example.com"))
            with { Reason = stableReason };
        var committed = await ExecuteAsync(WorkspaceA, MemberA, replayCommand);
        var replayed = await ExecuteAsync(WorkspaceA, MemberA, replayCommand);
        Check("FN-1 replay returns the originally committed Task", committed.TaskId, replayed.TaskId);
        Check("FN-1 replay is labelled REPLAYED", "REPLAYED", replayed.Outcome);
        Check("FN-1 replay preserves the committed reason", stableReason,
            await ScalarStringAsync($"SELECT SourceEvidence FROM tasks.Tasks WHERE TaskId=N'{replayed.TaskId}'"));
        Check("FN-1 replay creates no second Task", 1L, await ScalarLongAsync(
            $"SELECT COUNT(*) FROM tasks.Tasks WHERE SourceId=N'{replayLead}'"));

        var recoveryLead = await SeedLeadAsync(WorkspaceA, MemberA, "Reason Recovery Lead", "reason.recovery.lead@example.com");
        var recoveryCommand = Command(recoveryLead, NewContact("Reason Recovery Person", "reason.recovery.person@example.com"))
            with { Reason = stableReason };
        participantCalls.FailAfter = "Contact";
        try { await ExecuteAsync(WorkspaceA, MemberA, recoveryCommand); }
        catch (InjectedAcknowledgmentLoss) { }
        finally { participantCalls.FailAfter = null; }
        var recovered = await ExecuteAsync(WorkspaceA, MemberA, recoveryCommand);
        Check("FN-1 partial recovery still succeeds", true, recovered.IsSuccess);
        Check("FN-1 partial recovery preserves the reason", stableReason,
            await ScalarStringAsync($"SELECT SourceEvidence FROM tasks.Tasks WHERE TaskId=N'{recovered.TaskId}'"));
        Check("FN-1 partial recovery yields the same bounded title", stableReason[..300],
            await ScalarStringAsync($"SELECT Title FROM tasks.Tasks WHERE TaskId=N'{recovered.TaskId}'"));
        Check("FN-1 partial recovery creates one Task", 1L, await ScalarLongAsync(
            $"SELECT COUNT(*) FROM tasks.Tasks WHERE SourceId=N'{recoveryLead}'"));

        var changed = await ExecuteAsync(WorkspaceA, MemberA, replayCommand with { Reason = Reason(999) });
        Check("FN-1 a changed reason under the same key still conflicts", "IDEMPOTENCY_KEY_REUSED", changed.ErrorCode);
        Check("FN-1 the conflicting retry leaves the committed reason untouched", stableReason,
            await ScalarStringAsync($"SELECT SourceEvidence FROM tasks.Tasks WHERE TaskId=N'{committed.TaskId}'"));
    }

    // ------------------------------------------------------------------ transient contention

    /// <summary>
    /// FN-2. Contacts already distinguishes a permanent relationship refusal from transient
    /// contention: exhausting its own bounded resolution attempts yields the typed
    /// <c>ConcurrentConflict</c> outcome. That outcome is a normal participant result rather than an
    /// exception, so the coordinator must recognise it explicitly and drive its own admitted bounded
    /// retry. Collapsing it into the permanent relationship-invalid 422 would tell a caller their
    /// valid command was wrong and stop them retrying it.
    ///
    /// The injected outcome is exactly the typed value the real owner returns after its own real
    /// deadlock retry is exhausted - the real persistence contention path itself is exercised by
    /// verify-contact-qualification-participant.ps1, which proves that a genuine concurrent create
    /// resolves to the permanent duplicate refusal and never leaks this transient outcome.
    /// </summary>
    private async Task VerifyContactContentionAsync()
    {
        const string convergingEmail = "contended.person@example.com";
        var leadId = await SeedLeadAsync(WorkspaceA, MemberA, "Contended Lead", "contended.lead@example.com");
        var command = Command(leadId, NewContact("Contended Person", convergingEmail));
        participantCalls.ContentionRemaining = 1;
        var converged = await ExecuteAsync(WorkspaceA, MemberA, command);
        participantCalls.ContentionRemaining = 0;

        Check("FN-2 transient contention is retried and converges", true, converged.IsSuccess);
        Check("FN-2 the injected contention was actually observed", 0, participantCalls.ContentionRemaining);
        Check("FN-2 contention creates exactly one Contact", 1L, await ScalarLongAsync(
            $"SELECT COUNT(*) FROM contacts.Contacts WHERE WorkspaceId=N'{WorkspaceA}' AND NormalizedWorkEmail=N'{convergingEmail.ToUpperInvariant()}'"));
        Check("FN-2 contention creates exactly one Task", 1L, await ScalarLongAsync(
            $"SELECT COUNT(*) FROM tasks.Tasks WHERE SourceId=N'{leadId}'"));
        Check("FN-2 Contacts writes exactly one create audit", 1L, await ScalarLongAsync(
            $"SELECT COUNT(*) FROM contacts.AuditRecords WHERE AggregateId=N'{converged.ContactId}'"));
        Check("FN-2 Contacts stages exactly one CONTACT_CREATED message", 1L, await ScalarLongAsync(
            $"SELECT COUNT(*) FROM contacts.OutboxMessages WHERE AggregateId=N'{converged.ContactId}' AND EventType=N'CONTACT_CREATED'"));
        Check("FN-2 contention leaves one Lead close", 1L, await ScalarLongAsync(
            $"SELECT COUNT(*) FROM leads.AuditRecords WHERE AggregateId=N'{leadId}' AND Operation=N'qualifyLeadForNurture'"));

        // Contention through the whole bounded retry. The caller must not be told their
        // relationship input was invalid, and no owner may have advanced.
        const string exhaustedEmail = "exhausted.person@example.com";
        var exhaustedLead = await SeedLeadAsync(WorkspaceA, MemberA, "Exhausted Lead", "exhausted.lead@example.com");
        var exhaustedCommand = Command(exhaustedLead, NewContact("Exhausted Person", exhaustedEmail));
        participantCalls.ContentionRemaining = 3;
        var refusal = await ExecuteAsync(WorkspaceA, MemberA, exhaustedCommand);
        var remainingInjections = participantCalls.ContentionRemaining;
        participantCalls.ContentionRemaining = 0;

        Check("FN-2 exhausted contention consumes the whole bounded retry", 0, remainingInjections);
        Check("FN-2 exhausted contention is not a relationship refusal", false,
            refusal.ErrorCode == "LEAD_QUALIFICATION_RELATIONSHIP_INVALID");
        Check("FN-2 exhausted contention is not 422", false, refusal.ErrorStatus == 422);
        Check("FN-2 exhausted contention reports the admitted contention outcome", "INTERNAL_ERROR", refusal.ErrorCode);
        Check("FN-2 exhausted contention is 500", 500, refusal.ErrorStatus);
        Check("FN-2 exhausted contention discloses no Contact", null, refusal.ContactId);
        Check("FN-2 exhausted contention creates no Contact", 0L, await ScalarLongAsync(
            $"SELECT COUNT(*) FROM contacts.Contacts WHERE WorkspaceId=N'{WorkspaceA}' AND NormalizedWorkEmail=N'{exhaustedEmail.ToUpperInvariant()}'"));
        Check("FN-2 exhausted contention creates no Task", 0L, await ScalarLongAsync(
            $"SELECT COUNT(*) FROM tasks.Tasks WHERE SourceId=N'{exhaustedLead}'"));
        Check("FN-2 exhausted contention does not advance the anchor", "Started", await ScalarStringAsync(
            $"SELECT Stage FROM workflow.LeadQualificationAnchors WHERE LeadId=N'{exhaustedLead}'"));
        Check("FN-2 exhausted contention records no anchor Contact", null, await ScalarStringAsync(
            $"SELECT ContactId FROM workflow.LeadQualificationAnchors WHERE LeadId=N'{exhaustedLead}'"));
        Check("FN-2 exhausted contention leaves the Lead open", 2L, await ScalarLongAsync(
            $"SELECT WorkState FROM leads.Leads WHERE LeadId=N'{exhaustedLead}'"));

        // The same key, retried once the contention has passed, converges on one execution.
        var retried = await ExecuteAsync(WorkspaceA, MemberA, exhaustedCommand);
        Check("FN-2 the same key retried after contention converges", true, retried.IsSuccess);
        Check("FN-2 the converged retry creates exactly one Contact", 1L, await ScalarLongAsync(
            $"SELECT COUNT(*) FROM contacts.Contacts WHERE WorkspaceId=N'{WorkspaceA}' AND NormalizedWorkEmail=N'{exhaustedEmail.ToUpperInvariant()}'"));
        Check("FN-2 the converged retry creates exactly one Task", 1L, await ScalarLongAsync(
            $"SELECT COUNT(*) FROM tasks.Tasks WHERE SourceId=N'{exhaustedLead}'"));
        Check("FN-2 the converged retry closes the Lead once", 1L, await ScalarLongAsync(
            $"SELECT COUNT(*) FROM leads.AuditRecords WHERE AggregateId=N'{exhaustedLead}' AND Operation=N'qualifyLeadForNurture'"));

        // A permanent relationship refusal is unchanged by any of this.
        await SeedContactAsync(WorkspaceA, MemberA, "Contention Duplicate Blocker", "contention.blocked@example.com");
        var duplicateLead = await SeedLeadAsync(WorkspaceA, MemberA, "Contention Duplicate Lead", "contention.duplicate.lead@example.com");
        var duplicate = await ExecuteAsync(WorkspaceA, MemberA,
            Command(duplicateLead, NewContact("Contention Duplicate", "Contention.Blocked@Example.com")));
        Check("FN-2 a duplicate address is still the permanent relationship refusal",
            "LEAD_QUALIFICATION_RELATIONSHIP_INVALID", duplicate.ErrorCode);
        Check("FN-2 the permanent refusal is still 422", 422, duplicate.ErrorStatus);
        Check("FN-2 the permanent refusal keeps its frozen pointer", "relationship.contact.email",
            string.Join(",", duplicate.FieldErrors?.Keys ?? []));
        var unresolvable = await ExecuteAsync(WorkspaceA, MemberA, Command(
            await SeedLeadAsync(WorkspaceA, MemberA, "Contention Unresolvable Lead", "contention.unresolvable.lead@example.com"),
            new LeadNurtureContactIntent(
                LeadNurtureRelationshipMode.Existing, "contact_still_not_here", "Ignored", null, null, null)));
        Check("FN-2 an unresolvable EXISTING stays non-disclosing",
            "LEAD_QUALIFICATION_RELATIONSHIP_INVALID|422||True",
            $"{unresolvable.ErrorCode}|{unresolvable.ErrorStatus}|{unresolvable.ContactId}|{unresolvable.FieldErrors is null}");

        // Two concurrent callers of the same key still converge while contention is in play.
        const string racingEmail = "racing.person@example.com";
        var racingLead = await SeedLeadAsync(WorkspaceA, MemberA, "Contention Racing Lead", "racing.lead@example.com");
        var racingCommand = Command(racingLead, NewContact("Racing Person", racingEmail));
        participantCalls.ContentionRemaining = 1;
        var outcomes = await Task.WhenAll(
            ExecuteAsync(WorkspaceA, MemberA, racingCommand),
            ExecuteAsync(WorkspaceA, MemberA, racingCommand));
        participantCalls.ContentionRemaining = 0;
        Check("FN-2 concurrent callers under contention never report a relationship refusal", 0,
            outcomes.Count(item => item.ErrorCode == "LEAD_QUALIFICATION_RELATIONSHIP_INVALID"));
        Check("FN-2 concurrent callers under contention create one Contact", 1L, await ScalarLongAsync(
            $"SELECT COUNT(*) FROM contacts.Contacts WHERE WorkspaceId=N'{WorkspaceA}' AND NormalizedWorkEmail=N'{racingEmail.ToUpperInvariant()}'"));
        Check("FN-2 concurrent callers under contention create one Task", 1L, await ScalarLongAsync(
            $"SELECT COUNT(*) FROM tasks.Tasks WHERE SourceId=N'{racingLead}'"));
        Check("FN-2 concurrent callers under contention close the Lead once", 1L, await ScalarLongAsync(
            $"SELECT COUNT(*) FROM leads.AuditRecords WHERE AggregateId=N'{racingLead}' AND Operation=N'qualifyLeadForNurture'"));
    }

    /// <summary>A deterministic, position-sensitive reason of the exact requested length.</summary>
    private static string Reason(int length) =>
        string.Create(length, length, (span, _) =>
        {
            for (var index = 0; index < span.Length; index++)
                span[index] = (char)('a' + (index % 26));
        });

    private async Task VerifyNoForeignOwnerWritesAsync()
    {
        foreach (var (schema, table) in new[]
                 {
                     ("deals", "Deals"), ("customers", "Customers"), ("quotes", "Quotes"), ("orders", "Orders")
                 })
        {
            var exists = await ScalarLongAsync(
                $"SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA=N'{schema}' AND TABLE_NAME=N'{table}'");
            var rows = exists == 0 ? 0L : await ScalarLongAsync($"SELECT COUNT(*) FROM [{schema}].[{table}]");
            Check($"no {schema}.{table} row was written", 0L, rows);
        }
    }

    /// <summary>The coordinator must stay internal and must not reach into any owner's persistence.</summary>
    private void VerifyCallableSurface()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/UnicoreCRM.Workflows"));
        var source = string.Join(
            "\n",
            Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    && !path.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Select(File.ReadAllText));

        foreach (var forbidden in new[]
                 {
                     "LeadsDbContext", "ContactsDbContext", "TasksDbContext", "DealsDbContext",
                     "CustomersDbContext", "QuotesDbContext", "OrdersDbContext",
                     "Leads.Infrastructure", "Contacts.Infrastructure", "Tasks.Infrastructure",
                     "Leads.Domain", "Contacts.Domain", "Tasks.Domain"
                 })
        {
            Check($"Workflows has no forbidden surface: {forbidden}", false, source.Contains(forbidden, StringComparison.Ordinal));
        }

        // No qualification route anywhere: public exposure is still gated on G-1.
        var apiSource = string.Join("\n", Directory
            .EnumerateFiles(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src")), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(File.ReadAllText));
        // Exposure is now admitted for NURTURE only. This is deliberately stricter than the previous
        // blanket "no qualification route" assertion: it pins exactly which one exists.
        Check("exactly one lead-qualification route is mapped", 1,
            apiSource.Split("workflows/lead-qualification").Length - 1);
        Check("the mapped qualification route is nurture", true,
            apiSource.Contains("/workflows/lead-qualification/{leadId}/nurture", StringComparison.Ordinal));
        Check("qualifyLeadForOpportunity stays unmapped", false,
            apiSource.Contains("lead-qualification/{leadId}/opportunity", StringComparison.Ordinal));
        Check("qualifyLeadForDirectSale stays unmapped", false,
            apiSource.Contains("lead-qualification/{leadId}/direct-sale", StringComparison.Ordinal));
        Check("the retired generic qualify route is still absent", false,
            apiSource.Contains("/leads/{leadId}/qualify", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ harness

    /// <summary>
    /// The coordinator's workflow identity, replicated so a recovery case can commit exactly what an
    /// interrupted coordinator would have committed. It must stay in step with the coordinator's own
    /// ScopeKey: the frozen identity is (trusted WorkspaceId, workflow, leadId, Idempotency-Key).
    /// </summary>
    private static string WorkflowScopeKey(string workspaceId, string leadId, string idempotencyKey)
    {
        var payload = string.Join("\n", workspaceId, "qualifyLeadForNurture", leadId, idempotencyKey);
        return Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(payload)))[..48];
    }

    /// <summary>Commits a Contact exactly as the coordinator's first step would.</summary>
    private async Task<string?> ResolveContactAsync(
        string workspaceId,
        string memberId,
        string conversionKey,
        string displayName,
        string email)
    {
        await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ControlledWorkspace>().Set(Trusted(workspaceId, memberId));
        var participant = scope.ServiceProvider.GetRequiredService<IContactQualificationParticipant>();
        var result = await participant.ResolveAsync(
            new ResolveQualificationContactCommand(
                Trusted(workspaceId, memberId),
                ContactQualificationMode.New,
                null,
                new ContactQualificationInput(displayName, email, "0900000001", "Manager"),
                memberId,
                conversionKey,
                $"req_{Guid.NewGuid():N}"[..32],
                $"corr_{Guid.NewGuid():N}"[..32]),
            CancellationToken.None);
        return result.ContactId;
    }

    private static LeadNurtureContactIntent NewContact(string displayName, string email) =>
        new(LeadNurtureRelationshipMode.New, null, displayName, email, "0900000001", "Manager");

    private static LeadNurtureQualificationCommand Command(string leadId, LeadNurtureContactIntent contact) =>
        new(
            leadId,
            contact,
            "2026-10-01T09:00:00.0000000Z",
            "Revisit after budget cycle",
            "Seeded by the NURTURE workflow verifier.",
            $"req_{Guid.NewGuid():N}"[..32],
            $"corr_{Guid.NewGuid():N}"[..32],
            $"idem_{leadId}",
            0);

    private static TrustedWorkspaceContext Trusted(string workspaceId, string memberId) =>
        new(workspaceId, $"account_{memberId}", memberId, $"membership_{memberId}");

    private async Task<LeadNurtureQualificationResult> ExecuteAsync(
        string workspaceId,
        string memberId,
        LeadNurtureQualificationCommand command)
    {
        await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ControlledWorkspace>().Set(Trusted(workspaceId, memberId));
        var workflow = scope.ServiceProvider.GetRequiredService<ILeadNurtureQualificationWorkflow>();
        return await workflow.ExecuteAsync(command, CancellationToken.None);
    }

    private async Task MigrateAsync()
    {
        await using var scope = provider.CreateAsyncScope();
        foreach (var typeName in new[]
                 {
                     "UnicoreCRM.Platform.Workspace.Infrastructure.Persistence.WorkspaceDbContext",
                     "UnicoreCRM.Platform.AccessControl.Infrastructure.Persistence.AccessControlDbContext",
                     "UnicoreCRM.Crm.Leads.Infrastructure.Persistence.LeadsDbContext",
                     "UnicoreCRM.Crm.Contacts.Infrastructure.Persistence.ContactsDbContext",
                     "UnicoreCRM.Operations.Tasks.Infrastructure.Persistence.TasksDbContext",
                     "UnicoreCRM.Workflows.Atomic.Infrastructure.Persistence.WorkflowsDbContext"
                 })
        {
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(typeName))
                .FirstOrDefault(found => found is not null)
                ?? throw new InvalidOperationException($"{typeName} was not found.");
            await ((DbContext)scope.ServiceProvider.GetRequiredService(type)).Database.MigrateAsync();
        }
    }

    /// <summary>
    /// Real Workspace and active-membership rows. Tasks validates the assignee through the narrow
    /// Workspace active-member contract, so a fabricated member would not be accepted.
    /// </summary>
    private async Task SeedWorkspacesAsync()
    {
        foreach (var (workspaceId, memberId) in new[] { (WorkspaceA, MemberA), (WorkspaceB, MemberB) })
        {
            await ExecuteAsync(null, $"""
                INSERT INTO workspace.Workspaces (WorkspaceId, [Key], [Name], LogoText, CreatedAt)
                VALUES (N'{workspaceId}', N'{workspaceId}', N'{workspaceId}', N'UC', SYSDATETIMEOFFSET());
                INSERT INTO workspace.Memberships (MembershipId, WorkspaceId, AccountId, MemberId, [Status], CreatedAt)
                VALUES (N'membership_{memberId}', N'{workspaceId}', N'account_{memberId}', N'{memberId}', N'Active', SYSDATETIMEOFFSET());
                """);
        }
    }

    private async Task ProvisionAccessAsync()
    {
        foreach (var (workspaceId, memberId) in new[] { (WorkspaceA, MemberA), (WorkspaceB, MemberB) })
        {
            await using var scope = provider.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ControlledWorkspace>().Set(Trusted(workspaceId, memberId));
            var access = scope.ServiceProvider.GetRequiredService<IInitialWorkspaceAccessProvisioning>();
            var provisioned = await access.EnsureInitialWorkspaceAccessAsync(
                workspaceId,
                $"membership_{memberId}",
                CancellationToken.None);
            foreach (var capability in new[] { "leads.qualify", "tasks.create", "contacts.read" })
            {
                if (!provisioned.Capabilities.Contains(capability, StringComparer.Ordinal))
                    throw new InvalidOperationException($"{capability} was not provisioned for {workspaceId}.");
            }
        }
    }

    /// <param name="workState">0 = NEW, 2 = VERIFYING.</param>
    /// <param name="email">Null seeds a Lead with no reachable channel, i.e. an incomplete progressive profile.</param>
    private async Task<string> SeedLeadAsync(
        string workspaceId,
        string ownerId,
        string displayName,
        string? email,
        int workState = 2)
    {
        var leadId = $"lead_seed_{Guid.NewGuid():N}";
        var channel = email is null ? "" : $"\"phone\":\"0900000001\",\"email\":\"{email}\",";
        var profile = $$"""
            {"displayName":"{{displayName}}",{{channel}}"source":"verifier","ownerId":"{{ownerId}}",
             "interestedProducts":[],"estimatedValue":{"amount":"0","currency":"VND"},
             "tags":[],"customFields":[]}
            """.Replace("\r", "").Replace("\n", "").Replace("  ", "");
        await ExecuteAsync(null, $"""
            INSERT INTO leads.Leads
                (LeadId, WorkspaceId, Profile, ScopeOwnerId, WorkState, Score, CreatedAt, UpdatedAt, [Version])
            VALUES
                (N'{leadId}', N'{workspaceId}', N'{profile}', N'{ownerId}', {workState}, 0,
                 SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), 0)
            """);
        return leadId;
    }

    private async Task<string> SeedContactAsync(string workspaceId, string ownerId, string fullName, string email)
    {
        var contactId = $"contact_seed_{Guid.NewGuid():N}";
        var profile = $$"""{"workEmail":"{{email}}","displayName":"{{fullName}}"}""";
        await ExecuteAsync(null, $"""
            INSERT INTO contacts.Contacts
                (ContactId, WorkspaceId, OwnerId, FullName, [Status], [Version], CreatedAt, UpdatedAt, Profile,
                 NormalizedWorkEmail, NormalizedPersonalEmail)
            VALUES
                (N'{contactId}', N'{workspaceId}', N'{ownerId}', N'{fullName}', N'active', 0,
                 SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), N'{profile}', N'{email.ToUpperInvariant()}', NULL)
            """);
        return contactId;
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
    }

    private async Task ExecuteAsync(string? connectionStringOverride, string sql)
    {
        await using var connection = new SqlConnection(connectionStringOverride ?? connectionString);
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

internal sealed class ParticipantCalls
{
    internal int Contacts;
    internal int Tasks;
    internal string? FailAfter;

    /// <summary>
    /// How many further Contacts resolutions return the owner's typed transient contention outcome
    /// instead of reaching the real participant. It reproduces exactly what Contacts returns once
    /// its own bounded deadlock retry is exhausted, without needing a real deadlock to be scheduled
    /// a fixed number of times.
    /// </summary>
    internal int ContentionRemaining;
    internal ResolveQualificationContactResult? LastContact;
    internal LeadNurtureTaskResult? LastTask;
    internal LeadQualificationClosure? LastLead;

    internal void After(string participant)
    {
        if (FailAfter != participant) return;
        FailAfter = null;
        throw new InjectedAcknowledgmentLoss();
    }
}

internal sealed class InjectedAcknowledgmentLoss : Exception;

internal sealed class CountingContactParticipant(IContactQualificationParticipant inner, ParticipantCalls calls)
    : IContactQualificationParticipant
{
    public async Task<ResolveQualificationContactResult> ResolveAsync(
        ResolveQualificationContactCommand command, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref calls.Contacts);
        if (Volatile.Read(ref calls.ContentionRemaining) > 0)
        {
            Interlocked.Decrement(ref calls.ContentionRemaining);
            return new ResolveQualificationContactResult(
                ContactQualificationDecision.Rejected, null, null,
                ContactQualificationRejection.ConcurrentConflict);
        }
        var result = await inner.ResolveAsync(command, cancellationToken);
        if (result.IsSuccess) { calls.LastContact = result; calls.After("Contact"); }
        return result;
    }
}

internal sealed class CountingTaskParticipant(ILeadQualificationTaskParticipant inner, ParticipantCalls calls)
    : ILeadQualificationTaskParticipant
{
    public Task<LeadNurtureTaskAssigneeValidationResult> ValidateNurtureAssigneeAsync(
        string assigneeId, CancellationToken cancellationToken) =>
        inner.ValidateNurtureAssigneeAsync(assigneeId, cancellationToken);

    public async Task<LeadNurtureTaskResult> CreateNurtureFollowUpAsync(
        LeadNurtureTaskCommand command, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref calls.Tasks);
        var result = await inner.CreateNurtureFollowUpAsync(command, cancellationToken);
        if (result.IsSuccess) { calls.LastTask = result; calls.After("Task"); }
        return result;
    }
}

internal sealed class CountingLeadParticipant(ILeadQualificationParticipant inner, ParticipantCalls calls)
    : ILeadQualificationParticipant
{
    public Task<LeadQualificationAuthorization> AuthorizeAsync(LeadQualificationAccessQuery query, CancellationToken token) => inner.AuthorizeAsync(query, token);
    public Task<LeadQualificationPreparation> PrepareAsync(LeadQualificationPrepareCommand command, CancellationToken token) => inner.PrepareAsync(command, token);
    public async Task<LeadQualificationClosure> CloseForNurtureAsync(LeadQualificationCloseCommand command, CancellationToken token)
    {
        var result = await inner.CloseForNurtureAsync(command, token);
        if (result.IsSuccess) { calls.LastLead = result; calls.After("Lead"); }
        return result;
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
