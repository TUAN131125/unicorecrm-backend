using System.Reflection;
using UnicoreCRM.Operations.Tasks.Application.Common;
using UnicoreCRM.Operations.Tasks.Application.LeadHandover;
using UnicoreCRM.Operations.Tasks.Contracts;
using UnicoreCRM.Operations.Tasks.Domain;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

var passed = 0;
void Check(bool value, string name)
{
    if (!value) throw new InvalidOperationException(name);
    passed++;
    Console.WriteLine($"PASS {name}");
}
var fixture = new Fixture();
fixture.Access.OwnOnly = true;
var open = fixture.Add();
var completed = fixture.Add();
completed.Complete("done", DateTimeOffset.UtcNow);
var cancelled = fixture.Add();
cancelled.Cancel("cancel", DateTimeOffset.UtcNow);
var archived = fixture.Add();
archived.Archive("archive", DateTimeOffset.UtcNow);
var otherLead = fixture.Add(lead: "lead_other");
var otherWorkspace = fixture.Add(workspace: "ws_other");
var move = fixture.Command();
var validation = await fixture.Participant.ValidateAsync(move, default);
Check(validation.IsSuccess && fixture.Persistence.Audits.Count == 0 && open.AssigneeId == fixture.Trusted.MemberId, "preflight writes no domain evidence or Task state");
fixture.Access.DeniedRecord = open.TaskId;
var denied = await fixture.Participant.ExecuteAsync(move, default);
Check(!denied.IsSuccess && fixture.Persistence.Items.Count == 6 && fixture.Persistence.Audits.Count == 0 && open.AssigneeId == fixture.Trusted.MemberId, "record denial fails complete set before mutation");
fixture.Access.DeniedRecord = null;
fixture.Access.DeniedCapability = "tasks.assign";
Check(!(await fixture.Participant.ExecuteAsync(move, default)).IsSuccess, "Handover requires tasks.assign even without visible task targets");
fixture.Access.DeniedCapability = "tasks.create";
Check(!(await fixture.Participant.ExecuteAsync(move, default)).IsSuccess, "Handover requires tasks.create");
fixture.Access.DeniedCapability = null;
var result = await fixture.Participant.ExecuteAsync(move, default);
Check(result.IsSuccess && result.ReassignedTaskIds.SequenceEqual([open.TaskId]) && open.AssigneeId == "member_new", "Handover transfers authoritative eligible set");
Check(new[] { completed, cancelled, archived, otherLead, otherWorkspace }.All(task => task.AssigneeId == fixture.Trusted.MemberId), "ineligible tasks untouched");
var takeover = fixture.Persistence.Items.Single(task => task.TaskId == result.HandoverTaskId);
Check(takeover.Status == UnicoreCRM.Operations.Tasks.Domain.TaskStatus.Open && takeover.Priority == TaskPriority.Normal &&
      takeover.AssigneeId == "member_new" && takeover.SourceType == "LEAD_HANDOVER" && takeover.SourceId == move.HandoverId &&
      takeover.SourceEvidence == move.Reason.Trim() && takeover.RecordModuleKey == "leads" && takeover.RecordId == move.LeadId &&
      takeover.DueAt == move.FrozenDueAt && takeover.Title == move.Reason.Trim() && takeover.Description is null,
      "mandatory takeover frozen semantics and canonical presentation");
Check((await fixture.Participant.AuthorizeReplayAsync(move, default)).IsSuccess && fixture.Access.RecordCalls == 3,
    "OWN admission succeeds and proof replay succeeds after all committed assignees move away");
var lateTask = fixture.Add();
var audits = fixture.Persistence.Audits.Count;
var events = fixture.Persistence.Events.Count;
Check(result.EmittedEventIds.SequenceEqual(fixture.Persistence.Events.Select(item => item.EventId)) &&
      result.AuditEvidenceIds.SequenceEqual(fixture.Persistence.Audits.Select(item => item.AuditId)), "participant exposes committed proof IDs");
fixture.Members.Active = false;
var replay = await fixture.Participant.ExecuteAsync(move, default);
Check(replay.IsSuccess && replay.Outcome == "REPLAYED" && replay.HandoverTaskId == result.HandoverTaskId &&
      replay.HandoverTaskDueAt == result.HandoverTaskDueAt && lateTask.AssigneeId == fixture.Trusted.MemberId &&
      fixture.Persistence.Audits.Count == audits && fixture.Persistence.Events.Count == events, "replay survives target deactivation without rediscovery or duplicate evidence");
Check(replay.EmittedEventIds.SequenceEqual(result.EmittedEventIds) && replay.AuditEvidenceIds.SequenceEqual(result.AuditEvidenceIds), "replay preserves proof IDs");
Check((await fixture.Participant.ExecuteAsync(move with { Reason = "changed" }, default)).ErrorCode == "IDEMPOTENCY_KEY_REUSED", "conflicting durable intent rejected");
fixture.Access.DeniedCapability = "tasks.create";
var recovery = move with { ExecutorServicePrincipalId = Participant.RecoveryPrincipal };
Check((await fixture.Participant.ExecuteAsync(recovery, default)).IsSuccess && fixture.Service.Calls == 1, "service-authorized recovery replays despite revoked human authority");
fixture.Service.Allowed = false;
Check(!(await fixture.Participant.ExecuteAsync(recovery, default)).IsSuccess, "recovery requires service authorization");
fixture.Service.Allowed = true;
Check((await fixture.Participant.ExecuteAsync(recovery with { HandoverId = "handover_uncommitted" }, default)).ErrorCode == "HANDOVER_TASKS_NOT_COMMITTED", "service recovery cannot admit uncommitted side effects");
var historical = new Fixture();
var historicalCommand = historical.Command();
await historical.Participant.ExecuteAsync(historicalCommand, default);
var historicalProof = historical.Persistence.Records.Single();
var legacyProof = new TaskIdempotencyRecord(historicalProof.ScopeKey, historicalProof.WorkspaceId,
    historicalProof.Operation, historicalProof.ActorId, historicalProof.TargetId, historicalProof.IdempotencyKey,
    "historical-wire-format-fingerprint", historicalProof.ResponseJson, historicalProof.CreatedAt);
historical.Persistence.ReplaceIdempotency(legacyProof);
Check((await historical.Participant.AuthorizeReplayAsync(historicalCommand, default)).IsSuccess,
    "historical committed proof disclosure does not depend on obsolete wire fingerprint");
Check((await historical.Participant.ExecuteAsync(historicalCommand, default)).ErrorCode == "IDEMPOTENCY_KEY_REUSED",
    "execution still enforces fingerprint despite historical disclosure repair");
foreach (var wrong in new[] {
    new TaskIdempotencyRecord(legacyProof.ScopeKey, legacyProof.WorkspaceId, legacyProof.Operation,
        "member_other", legacyProof.TargetId, legacyProof.IdempotencyKey, legacyProof.Fingerprint, legacyProof.ResponseJson, legacyProof.CreatedAt),
    new TaskIdempotencyRecord(legacyProof.ScopeKey, legacyProof.WorkspaceId, legacyProof.Operation,
        legacyProof.ActorId, "lead_other", legacyProof.IdempotencyKey, legacyProof.Fingerprint, legacyProof.ResponseJson, legacyProof.CreatedAt) })
{
    historical.Persistence.ReplaceIdempotency(wrong);
    Check((await historical.Participant.AuthorizeReplayAsync(historicalCommand, default)).ErrorCode == "ACCESS_DENIED",
        "historical proof disclosure rejects mismatched actor or target identity");
}
var invalid = new Fixture();
var command = invalid.Command();
foreach (var bad in new[] { command with { Reason = " " }, command with { Reason = new string('x', 1001) },
             command with { NextOwnerId = "" } })
    Check((await invalid.Participant.ExecuteAsync(bad, default)).ErrorCode == "VALIDATION_FAILED", "invalid frozen command rejected");
invalid.Members.Active = false;
Check((await invalid.Participant.ExecuteAsync(command, default)).ErrorCode == "VALIDATION_FAILED", "inactive target rejected before first commit");
Check((await invalid.Participant.ExecuteAsync(command with { TrustedWorkspace = command.TrustedWorkspace with { WorkspaceId = "ws_other" } }, default)).ErrorCode == "WORKSPACE_MISMATCH", "trusted workspace mismatch refused");
var fenceFixture = new Fixture();
var fenceCommand = fenceFixture.Command();
Check(!(await fenceFixture.Participant.ResolveOrFenceAsync(fenceCommand, default)).IsSuccess && fenceFixture.Persistence.Audits.Count == 0,
    "human cannot fence participant");
var serviceFenceCommand = fenceCommand with { ExecutorServicePrincipalId = Participant.RecoveryPrincipal };
fenceFixture.Service.Allowed = false;
Check(!(await fenceFixture.Participant.ResolveOrFenceAsync(serviceFenceCommand, default)).IsSuccess && fenceFixture.Persistence.Audits.Count == 0,
    "fence requires service grant");
fenceFixture.Service.Allowed = true;
var fence = await fenceFixture.Participant.ResolveOrFenceAsync(serviceFenceCommand, default);
Check(!fence.IsSuccess && fence.ErrorCode == "HANDOVER_TASKS_FENCED" && fence.ErrorStatus == 409 && fenceFixture.Persistence.Items.Count == 0,
    "durable fence permits release without Task side effects");
var repeatedFence = await fenceFixture.Participant.ResolveOrFenceAsync(serviceFenceCommand, default);
Check(repeatedFence.AuditEvidenceIds.SequenceEqual(fence.AuditEvidenceIds) && fenceFixture.Persistence.Audits.Count == 1,
    "repeated fence preserves evidence without duplication");
Check((await fenceFixture.Participant.ExecuteAsync(fenceCommand, default)).ErrorCode == "HANDOVER_TASKS_FENCED" && fenceFixture.Persistence.Items.Count == 0,
    "late human execute observes fence before null takeover traversal");
Check((await fenceFixture.Participant.ResolveOrFenceAsync(serviceFenceCommand with { NextOwnerId = "member_other" }, default)).ErrorCode == "IDEMPOTENCY_KEY_REUSED",
    "fence rejects changed intent");
fixture.Service.Allowed = true;
var resolved = await fixture.Participant.ResolveOrFenceAsync(recovery, default);
Check(resolved.IsSuccess && resolved.Outcome == "COMMITTED" && resolved.HandoverTaskId == result.HandoverTaskId &&
    resolved.EmittedEventIds.SequenceEqual(result.EmittedEventIds) && fixture.Persistence.Audits.Count == audits,
    "resolve committed proof bypasses revoked human authority without duplicate evidence");
fixture.Access.DeniedCapability = null;
fixture.Access.DeniedRecord = result.HandoverTaskId;
Check((await fixture.Participant.AuthorizeReplayAsync(move, default)).IsSuccess,
    "proof replay does not recheck post-transfer Task ownership");
fixture.Access.DeniedRecord = null;
foreach (var capability in new[] { "tasks.create", "tasks.assign" })
{
    fixture.Access.DeniedCapability = capability;
    Check((await fixture.Participant.AuthorizeReplayAsync(move, default)).ErrorCode == "ACCESS_DENIED",
        "later proof replay requires current " + capability);
}
fixture.Access.DeniedCapability = null;
foreach (var field in new[] { "id", "resourceVersion", "dueAt" })
{
    fixture.Access.HiddenField = field;
    Check((await fixture.Participant.AuthorizeReplayAsync(move, default)).ErrorCode == "ACCESS_DENIED",
        "later proof replay refuses hidden " + field);
}
fixture.Access.HiddenField = "title";
Check((await fixture.Participant.AuthorizeReplayAsync(move, default)).IsSuccess,
    "proof replay does not expose a currently hidden Task title");
fixture.Access.HiddenField = null;
Check((await fixture.Participant.AuthorizeReplayAsync(move, default)).IsSuccess && fixture.Persistence.Audits.Count == audits,
    "human disclosure guard reads existing proof without mutation");
Check((await fenceFixture.Participant.AuthorizeReplayAsync(fenceCommand with { HandoverId = "handover_absent" }, default)).ErrorCode == "HANDOVER_TASKS_NOT_COMMITTED" && fenceFixture.Persistence.Items.Count == 0,
    "disclosure guard never admits new Tasks");
var mixedScope = new Fixture();
mixedScope.Access.OwnOnly = true;
var admittedTask = mixedScope.Add();
var inaccessibleTask = mixedScope.Add();
inaccessibleTask.Assign("member_other", DateTimeOffset.UtcNow);
Check((await mixedScope.Participant.ExecuteAsync(mixedScope.Command(), default)).ErrorCode == "RESOURCE_NOT_FOUND" &&
    admittedTask.AssigneeId == mixedScope.Trusted.MemberId && mixedScope.Persistence.Items.Count == 2 &&
    mixedScope.Persistence.Audits.Count == 0 && mixedScope.Persistence.Events.Count == 0,
    "one inaccessible eligible Task rejects entire snapshot before any mutation");
var longReason = new Fixture();
var longResult = await longReason.Participant.ExecuteAsync(longReason.Command() with { Reason = "  " + new string('x', 1000) + "  " }, default);
var longTakeover = longReason.Persistence.Items.Single();
Check(longResult.IsSuccess && longTakeover.Title == new string('x', 300) && longTakeover.Description is null &&
    longTakeover.SourceEvidence == new string('x', 1000), "1000-character trimmed evidence retains canonical 300-character title");
var fieldAdmission = new Fixture();
foreach (var field in new[] { "title", "priority", "assigneeId", "dueAt", "recordRef", "sourceRef" })
{
    fieldAdmission.Access.HiddenField = field;
    Check((await fieldAdmission.Participant.ExecuteAsync(fieldAdmission.Command(), default)).ErrorCode == "ACCESS_DENIED" &&
        fieldAdmission.Persistence.Items.Count == 0 && fieldAdmission.Persistence.Audits.Count == 0,
        "initial admission requires write authority for " + field);
}
Console.WriteLine($"{passed} passed, 0 failed. In-memory participant verification; SQL range-lock concurrency requires integration verification.");
if (args.Contains("--sql")) await SqlVerifier.RunAsync();

sealed class Fixture
{
    internal readonly TrustedWorkspaceContext Trusted = new("ws_test", "account_test", "member_actor", "membership_test");
    internal readonly MemoryPersistence Persistence = new();
    internal readonly MemberValidator Members = new();
    internal readonly ServiceAuthorizer Service = new();
    internal readonly Evaluator Access;
    internal readonly Participant Participant;
    internal Fixture()
    {
        Access = new(Trusted);
        Participant = new(new TaskAuthorization(Access), new CurrentWorkspace(Trusted), Service, Members, Persistence, TimeProvider.System);
    }
    internal LeadHandoverTaskCommand Command() => new(Trusted, "lead_test", "handover_test", "member_new",
        "  takeover reason  ", new DateTimeOffset(2026, 10, 3, 4, 5, 6, TimeSpan.Zero), "request_test", "correlation_test", Trusted.MemberId);
    internal TaskItem Add(string lead = "lead_test", string workspace = "ws_test")
    {
        var task = new TaskItem(workspace, "task", null, TaskPriority.Normal, Trusted.MemberId, DateTimeOffset.UtcNow,
            new(null, null, "leads", lead, null, null, null, null), null, DateTimeOffset.UtcNow);
        Persistence.AddTask(task);
        return task;
    }
}
sealed class CurrentWorkspace(TrustedWorkspaceContext trusted) : ICurrentWorkspace
{
    public bool IsResolved => true;
    public TrustedWorkspaceContext Require() => trusted;
}
sealed class MemberValidator : IWorkspaceMemberReferenceValidator
{
    internal bool Active = true;
    public Task<bool> IsActiveMemberAsync(string workspaceId, string memberId, CancellationToken cancellationToken) => Task.FromResult(Active);
}
sealed class ServiceAuthorizer : IServiceAccessAuthorizer
{
    internal bool Allowed = true;
    internal int Calls;
    public Task<ServiceAccessAuthorizationDecision> AuthorizeAsync(string workspaceId, string servicePrincipalId,
        AccessRequirement requirement, string correlationId, CancellationToken cancellationToken)
    {
        Calls++;
        if (servicePrincipalId != Participant.RecoveryPrincipal || requirement.Capability != "leads.handover.recover") throw new InvalidOperationException("Wrong recovery authority");
        return Task.FromResult(new ServiceAccessAuthorizationDecision(Allowed, Allowed ? "AUTHORIZED" : "ACCESS_DENIED", "decision_test"));
    }
}
sealed class Evaluator(TrustedWorkspaceContext trusted) : IRecordAccessEvaluator
{
    internal string? DeniedCapability;
    internal string? DeniedRecord;
    internal string? HiddenField;
    internal bool OwnOnly;
    internal int RecordCalls;
    public Task<RecordAccessAuthorization> AuthorizeResourceAsync(string resourceKey, string requiredCapability,
        IReadOnlyList<string>? requestedFields, RecordAccessRepresentation representation, RecordAccessRequestContext requestContext, CancellationToken cancellationToken)
    {
        // The production constructor is internal to Platform; this isolated verifier supplies owner-boundary decisions.
        var constructor = typeof(RecordAccessAuthorization).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
        return Task.FromResult((RecordAccessAuthorization)constructor.Invoke([
            requiredCapability != DeniedCapability, "AUTHORIZED", trusted,
            OwnOnly ? RecordAccessScopeFilter.OwnedByMember : RecordAccessScopeFilter.Workspace,
            OwnOnly ? trusted.MemberId : null, OwnOnly ? "OWN" : "WORKSPACE",
            requestedFields!.ToDictionary(key => key, key => key == HiddenField ? RecordFieldEnforcement.Withheld : RecordFieldEnforcement.ReadWrite), Array.Empty<string>(),
            new[] { "tasks.create", "tasks.assign", "tasks.read" }, "policy_test", resourceKey, requiredCapability, true, false, false]));
    }
    public Task<RecordAccessRecordDecision> AuthorizeRecordAsync(RecordAccessAuthorization authorization, string recordId,
        RecordAccessFacts facts, string enforcementPoint, RecordAccessRequestContext requestContext, CancellationToken cancellationToken)
    {
        RecordCalls++;
        return Task.FromResult(new RecordAccessRecordDecision(recordId != DeniedRecord &&
            (!OwnOnly || facts.OwnerMemberId == trusted.MemberId), OwnOnly ? "OWN" : "WORKSPACE", true));
    }
}
sealed class MemoryPersistence : ITasksPersistence
{
    internal readonly List<TaskItem> Items = [];
    internal readonly List<TaskAuditRecord> Audits = [];
    internal readonly List<TaskOutboxMessage> Events = [];
    private readonly Dictionary<string, TaskIdempotencyRecord> records = [];
    internal IEnumerable<TaskIdempotencyRecord> Records => records.Values;
    internal void ReplaceIdempotency(TaskIdempotencyRecord record) => records[record.ScopeKey] = record;
    internal int Loads;
    public Task<ITasksTransaction> BeginSerializableAsync(CancellationToken cancellationToken) => Task.FromResult<ITasksTransaction>(new Transaction());
    public Task<TaskItem?> LoadTaskAsync(string workspaceId, string taskId, CancellationToken cancellationToken) => ReadTaskAsync(workspaceId, taskId, cancellationToken);
    public Task<TaskItem?> ReadTaskAsync(string workspaceId, string taskId, CancellationToken cancellationToken) => Task.FromResult(Items.SingleOrDefault(task => task.WorkspaceId == workspaceId && task.TaskId == taskId));
    public Task<TaskIdempotencyRecord?> FindIdempotencyAsync(string scopeKey, CancellationToken cancellationToken) => Task.FromResult(records.GetValueOrDefault(scopeKey));
    public Task<TaskIdempotencyRecord?> FindLeadHandoverIdempotencyForUpdateAsync(string scopeKey, CancellationToken cancellationToken) => FindIdempotencyAsync(scopeKey, cancellationToken);
    public Task<IReadOnlyList<TaskItem>> LoadEligibleLeadHandoverTasksForUpdateAsync(string workspaceId, string leadId, CancellationToken cancellationToken)
    {
        Loads++;
        return Task.FromResult<IReadOnlyList<TaskItem>>(Items.Where(task => task.WorkspaceId == workspaceId && task.RecordModuleKey == "leads" && task.RecordId == leadId && task.Status == UnicoreCRM.Operations.Tasks.Domain.TaskStatus.Open && task.ArchivedAt is null).OrderBy(task => task.TaskId).ToArray());
    }
    public void AddTask(TaskItem task) => Items.Add(task);
    public void AddIdempotency(TaskIdempotencyRecord record) => records.Add(record.ScopeKey, record);
    public void AddAudit(TaskAuditRecord audit) => Audits.Add(audit);
    public void AddOutbox(TaskOutboxMessage message) => Events.Add(message);
    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public void AddActivity(TaskActivity activity) => throw new NotSupportedException();
    public Task<TasksPage<TaskItem>> ListTasksAsync(string workspaceId, TaskListSpecification specification, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<TasksPage<TaskActivity>> ListActivitiesAsync(string workspaceId, ActivityListSpecification specification, CancellationToken cancellationToken) => throw new NotSupportedException();
    private sealed class Transaction : ITasksTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
