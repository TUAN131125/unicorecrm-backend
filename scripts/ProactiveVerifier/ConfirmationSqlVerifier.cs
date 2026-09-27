using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using UnicoreCRM.AI.Gateway;
using UnicoreCRM.Crm.Customers.Contracts;
using UnicoreCRM.Operations.Tasks.Application.Common;
using UnicoreCRM.Operations.Tasks.Application.CreateProactiveFollowUp;
using UnicoreCRM.Operations.Tasks.Contracts;
using UnicoreCRM.Operations.Tasks.Infrastructure.Persistence;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.PlatformOperations.AiExecution.Contracts;
using UnicoreCRM.PlatformOperations.AiExecution.Infrastructure;

internal static class ConfirmationSqlVerifier
{
    internal static async Task<int> RunAsync(DbContextOptions<AiExecutionDbContext> options, DateTimeOffset now)
    {
        var passed = 0;
        void Check(string name, bool condition) { if (!condition) throw new InvalidOperationException(name); passed++; }
        using var connectionContext = new AiExecutionDbContext(options);
        var taskOptions = new DbContextOptionsBuilder<TasksDbContext>().UseSqlServer(connectionContext.Database.GetConnectionString(),
            x => x.MigrationsHistoryTable("__EFMigrationsHistory", "tasks")).Options;
        await using (var db = new TasksDbContext(taskOptions)) await db.Database.MigrateAsync();
        var item = new ProactiveItemState("confirmation_sql", "confirmation_workspace", "member", "CUSTOMER_HEALTH_RISK", "CUSTOMER",
            "customer", "HIGH", "PURCHASE_OVER_EXPECTED_CADENCE", "fp", "cycle", "OPEN", now, now, null, null, null, null, "1", 3, now, now);
        await using (var seed = new AiExecutionDbContext(options))
            await new EfProactiveStore(seed).SaveItemAsync(item, new("confirmation_seed", item.WorkspaceId, "member", "ITEM_CREATED", item.ItemId,
                item.SubjectId, "{}", "correlation", now), default);
        var access = new TaskAccessEvaluator(item.WorkspaceId);
        var intent = new ProactiveTaskConfirmationRequest(" Follow up ", "member", "2026-09-30T03:00:00Z", " Discuss current needs ");
        var failAudit = new FailAcceptedAudit();
        var auditOptions = new DbContextOptionsBuilder<AiExecutionDbContext>(options).AddInterceptors(failAudit).Options;
        using var services = new ServiceCollection().AddScoped(_ => new AiExecutionDbContext(auditOptions))
            .AddScoped<IProactiveStore, EfProactiveStore>().BuildServiceProvider();

        async Task<AiOperationResult<ProactiveTaskConfirmationResponse>> Confirm(string key, ProactiveTaskConfirmationRequest? request = null, bool loseAck = false)
        {
            await using var taskDb = new TasksDbContext(taskOptions);
            await using var aiDb = new AiExecutionDbContext(options);
            var handler = new UnicoreCRM.Operations.Tasks.Application.CreateTask.Handler(new TaskAuthorization(access),
                new EfTasksPersistence(taskDb), new ActiveMembers(), new FixedClock(now));
            IProactiveTaskCreationParticipant tasks = new Participant(handler);
            if (loseAck) tasks = new LostAcknowledgement(tasks);
            var app = new ProactiveTaskConfirmationApplication(new FakeCurrentWorkspace(item.WorkspaceId, "member"),
                new FakeAccessAuthorizer(item.WorkspaceId, "member", true, false), new EfProactiveStore(aiDb),
                new FakeAttentionReader(true, new Dictionary<string, CustomerAttentionProjection> { ["customer"] = new("customer", "private-label") }),
                tasks, services.GetRequiredService<IServiceScopeFactory>(), new FixedClock(now), NullLogger<ProactiveTaskConfirmationApplication>.Instance);
            return await app.HandleAsync(item.ItemId, request ?? intent, key, "request-confirm", "correlation-confirm", default);
        }

        var first = await Confirm("confirmation-first");
        Check("commit", first.IsSuccess && first.Value!.Outcome == "COMMITTED" && first.Value.TaskVersion == 0);
        var replay = await Confirm("confirmation-first", intent with { Title = "Follow up", Description = "Discuss current needs", Priority = "NORMAL" });
        Check("normalized intent replay", replay.IsSuccess && replay.Value!.Outcome == "REPLAYED" && replay.Value.TaskId == first.Value!.TaskId);
        foreach (var changed in new[] { intent with { Title = "Changed" }, intent with { Description = "Changed" },
            intent with { AssigneeId = "member2" }, intent with { DueAt = "2026-10-01T03:00:00Z" }, intent with { Priority = "HIGH" } })
            Check("changed intent conflict", (await Confirm("confirmation-first", changed)).Error?.Code == "IDEMPOTENCY_KEY_REUSED");
        await using (var observed = new TasksDbContext(taskOptions))
        {
            var task = await observed.Tasks.SingleAsync();
            Check("one Task and server provenance", task.SourceType == "PROACTIVE_AI" && task.SourceId == item.ItemId && task.SourceEvidence is null);
            Check("no invented references", task.RecordModuleKey is null && task.RelationshipId is null && task.DedupeKey is null);
            Check("one audit", await observed.AuditRecords.CountAsync() == 1);
            Check("one outbox", await observed.OutboxMessages.CountAsync() == 1);
            Check("one idempotency", await observed.IdempotencyRecords.CountAsync() == 1);
        }
        await using (var observed = new AiExecutionDbContext(options))
            Check("one acceptance on replay", await observed.ProactiveAudits.CountAsync(x => x.Action == "TASK_SUGGESTION_ACCEPTED") == 1);
        var invalids = new[] { intent with { Title = "" }, intent with { Title = new string('x', 301) },
            intent with { Description = new string('x', 4001) }, intent with { AssigneeId = "inactive" },
            intent with { DueAt = "2026-09-30T03:00:00+00:00" }, intent with { DueAt = null }, intent with { Priority = "normal" } };
        foreach (var invalid in invalids) Check("Tasks validation preserved", (await Confirm("confirmation-invalid", invalid)).Error?.Code == "VALIDATION_FAILED");
        access.Allowed = false;
        Check("tasks.create denied", (await Confirm("confirmation-denied")).Error?.Code == "ACCESS_DENIED");
        access.Allowed = true;
        foreach (var field in new[] { "title", "description", "assigneeId", "priority", "dueAt", "sourceRef" })
        {
            access.ReadOnly = field;
            Check("Tasks field-write denied " + field, (await Confirm("confirmation-fields")).Error?.Code == "ACCESS_DENIED");
        }
        access.ReadOnly = null;
        foreach (var priority in new[] { "LOW", "NORMAL", "HIGH", "URGENT" })
            Check("Tasks priority " + priority, (await Confirm("priority-" + priority, intent with { Priority = priority })).IsSuccess);
        Check("authoritative max accepted", (await Confirm("confirmation-max", intent with { Title = new string('t', 300), Description = new string('d', 4000) })).IsSuccess);
        Check("optional description", (await Confirm("confirmation-optional", intent with { Description = null })).IsSuccess);

        failAudit.Reject = true;
        Check("post-commit audit failure safe retry", (await Confirm("confirmation-repair")).Error?.Code == "AI_PROACTIVE_ACCEPTANCE_AUDIT_UNAVAILABLE");
        failAudit.Reject = false;
        var repaired = await Confirm("confirmation-repair");
        Check("repair replays same Task", repaired.IsSuccess && repaired.Value!.Outcome == "REPLAYED");
        await Confirm("confirmation-repair");
        try { await Confirm("confirmation-lost-ack", loseAck: true); throw new InvalidOperationException("Lost ack not simulated."); }
        catch (OperationCanceledException) { passed++; }
        var recovered = await Confirm("confirmation-lost-ack");
        Check("lost ack converges", recovered.IsSuccess && recovered.Value!.Outcome == "REPLAYED");
        await using (var observed = new TasksDbContext(taskOptions))
        await using (var ai = new AiExecutionDbContext(options))
        {
            var count = await observed.Tasks.CountAsync();
            Check("distinct explicit keys create distinct tasks", count == 9);
            Check("one audit per Task", await observed.AuditRecords.CountAsync() == count);
            Check("one outbox per Task", await observed.OutboxMessages.CountAsync() == count);
            Check("one idempotency per Task", await observed.IdempotencyRecords.CountAsync() == count);
            var audits = await ai.ProactiveAudits.Where(x => x.Action == "TASK_SUGGESTION_ACCEPTED").ToArrayAsync();
            Check("one acceptance per Task incl repair", audits.Length == count);
            Check("safe evidence only", audits.All(x => JsonDocument.Parse(x.SafeSummaryJson).RootElement.EnumerateObject()
                .Select(p => p.Name).Order().SequenceEqual(new[] { "outcome", "taskId" })));
            Check("Attention unchanged", await new EfProactiveStore(ai).ReadItemAsync(item.WorkspaceId, item.ItemId, default) == item);
            Check("no provider executions", !await ai.Executions.AnyAsync(x => x.WorkspaceId == item.WorkspaceId) && !await ai.ProviderAttempts.AnyAsync(x => x.WorkspaceId == item.WorkspaceId));
        }
        // Concurrent retries may race to repair the same audit. Its primary key is the arbiter.
        var evidence = new ProactiveAuditEvidence("concurrent_acceptance", item.WorkspaceId, "member", "TASK_SUGGESTION_ACCEPTED", item.ItemId, item.SubjectId, "{}", "concurrent", now);
        async Task RecordOnce() { await using var scope = services.CreateAsyncScope(); await scope.ServiceProvider.GetRequiredService<IProactiveStore>().RecordAuditOnceAsync(evidence, default); }
        await Task.WhenAll(RecordOnce(), RecordOnce());
        await using (var observed = new AiExecutionDbContext(options)) Check("concurrent audit once", await observed.ProactiveAudits.CountAsync(x => x.AuditId == evidence.AuditId) == 1);
        Console.WriteLine($"PROACTIVE_CONFIRMATION_TASKS_SQL_PASS cases={passed}");
        return passed;
    }

    private sealed class ActiveMembers : IWorkspaceMemberReferenceValidator
    {
        public Task<bool> IsActiveMemberAsync(string workspaceId, string memberId, CancellationToken ct) => Task.FromResult(memberId is "member" or "member2");
    }
    private sealed class TaskAccessEvaluator(string workspace) : IRecordAccessEvaluator
    {
        internal bool Allowed { get; set; } = true;
        internal string? ReadOnly { get; set; }
        public Task<RecordAccessAuthorization> AuthorizeResourceAsync(string resource, string capability, IReadOnlyList<string>? fields,
            RecordAccessRepresentation representation, RecordAccessRequestContext context, CancellationToken ct)
        {
            if (capability != "tasks.create") throw new InvalidOperationException("Unexpected capability requirement.");
            return Task.FromResult(new RecordAccessAuthorization(Allowed, Allowed ? "AUTHORIZED" : "ACCESS_DENIED",
                new(workspace, "account", "member", "membership"), RecordAccessScopeFilter.Workspace, null, "WORKSPACE",
                fields!.ToDictionary(x => x, x => x == ReadOnly ? RecordFieldEnforcement.ReadOnly : RecordFieldEnforcement.ReadWrite),
                [], ["tasks.create"], "fixture", resource, capability, true));
        }
        public Task<RecordAccessRecordDecision> AuthorizeRecordAsync(RecordAccessAuthorization authorization, string id, RecordAccessFacts facts,
            string point, RecordAccessRequestContext context, CancellationToken ct) => throw new InvalidOperationException("Creation has no record scope.");
    }
    private sealed class LostAcknowledgement(IProactiveTaskCreationParticipant inner) : IProactiveTaskCreationParticipant
    {
        public async Task<ProactiveTaskCreationResult> CreateAsync(ProactiveTaskCreationCommand command, CancellationToken ct)
        { await inner.CreateAsync(command, ct); throw new OperationCanceledException("Simulated transport loss after durable commit."); }
    }
    private sealed class FailAcceptedAudit : SaveChangesInterceptor
    {
        internal bool Reject { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (Reject) throw new InvalidOperationException("Simulated audit storage outage.");
            return ValueTask.FromResult(result);
        }
    }
}
