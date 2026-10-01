using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using UnicoreCRM.Operations.Tasks.Application.Common;
using UnicoreCRM.Operations.Tasks.Application.LeadHandover;
using UnicoreCRM.Operations.Tasks.Contracts;
using UnicoreCRM.Operations.Tasks.Infrastructure.Persistence;

static partial class SqlVerifier
{
    private static async Task RunFenceChecksAsync(DbContextOptions<TasksDbContext> options)
    {
        var fixture = new Fixture();
        Participant Create(TasksDbContext context) => new(new TaskAuthorization(fixture.Access),
            new CurrentWorkspace(fixture.Trusted), fixture.Service, fixture.Members, new EfTasksPersistence(context), TimeProvider.System);
        var command = fixture.Command(LeadHandoverTaskPolicies.Move) with { HandoverId = "handover_fence_wins", LeadId = "lead_fence_wins" };
        var pauseBeforeKey = new PauseBeforeKeyRead();
        var pausedOptions = new DbContextOptionsBuilder<TasksDbContext>(options).AddInterceptors(pauseBeforeKey).Options;
        await using var pausedContext = new TasksDbContext(pausedOptions);
        var pausedExecution = Create(pausedContext).ExecuteAsync(command, default);
        await pauseBeforeKey.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        LeadHandoverTaskResult fence;
        try
        {
            await using var resolving = new TasksDbContext(options);
            fence = await Create(resolving).ResolveOrFenceAsync(command with { ExecutorServicePrincipalId = Participant.RecoveryPrincipal }, default);
            if (fence.ErrorCode != "HANDOVER_TASKS_FENCED" || fence.ErrorStatus != 409 || fence.IsSuccess)
                throw new InvalidOperationException("Fence did not commit");
        }
        finally { pauseBeforeKey.Resume.TrySetResult(); }
        var lateResult = await pausedExecution;
        await using var read = new TasksDbContext(options);
        if (lateResult.ErrorCode != "HANDOVER_TASKS_FENCED" || await read.Tasks.AnyAsync(task => task.RecordId == command.LeadId))
            throw new InvalidOperationException("Late worker committed after durable fence");
        var fenceAgain = await Create(read).ResolveOrFenceAsync(command with { ExecutorServicePrincipalId = Participant.RecoveryPrincipal }, default);
        if (!fenceAgain.AuditEvidenceIds.SequenceEqual(fence.AuditEvidenceIds) ||
            await read.AuditRecords.CountAsync(audit => audit.AggregateId == command.LeadId) != 1)
            throw new InvalidOperationException("Fence replay duplicated evidence");
        Console.WriteLine("PASS SQL fence wins against authorized human worker paused before key lock; late worker creates no Tasks");

        // If Execute already owns the key range, reconciliation must wait for its commit and
        // return the exact proof instead of deciding an absent record permits reservation release.
        command = command with { HandoverId = "handover_commit_wins", LeadId = "lead_commit_wins" };
        var pauseBeforeCommit = new PauseAfterSave();
        var savingOptions = new DbContextOptionsBuilder<TasksDbContext>(options).AddInterceptors(pauseBeforeCommit).Options;
        await using var executing = new TasksDbContext(savingOptions);
        var committing = Create(executing).ExecuteAsync(command, default);
        await pauseBeforeCommit.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await using var reconcileContext = new TasksDbContext(options);
        var reconciling = Create(reconcileContext).ResolveOrFenceAsync(command with { ExecutorServicePrincipalId = Participant.RecoveryPrincipal }, default);
        try
        {
            await Task.Delay(250);
            if (reconciling.IsCompleted) throw new InvalidOperationException("Reconcile bypassed uncommitted participant key lock");
        }
        finally { pauseBeforeCommit.Resume.TrySetResult(); }
        var committed = await committing;
        var reconciled = await reconciling;
        if (!committed.IsSuccess || !reconciled.IsSuccess || reconciled.Outcome != "COMMITTED" ||
            reconciled.HandoverTaskId != committed.HandoverTaskId || reconciled.HandoverTaskVersion != committed.HandoverTaskVersion ||
            reconciled.HandoverTaskDueAt != committed.HandoverTaskDueAt ||
            !reconciled.ReassignedTaskIds.SequenceEqual(committed.ReassignedTaskIds) ||
            !reconciled.EmittedEventIds.SequenceEqual(committed.EmittedEventIds) ||
            !reconciled.AuditEvidenceIds.SequenceEqual(committed.AuditEvidenceIds))
            throw new InvalidOperationException("Reconcile failed to return exact committed proof");
        if (await read.Tasks.CountAsync(task => task.RecordId == command.LeadId) != 1 ||
            await read.AuditRecords.CountAsync(audit => audit.AggregateId == committed.HandoverTaskId) != 1)
            throw new InvalidOperationException("Committed reconciliation duplicated effects");
        Console.WriteLine("PASS SQL commit wins; concurrent reconcile waits and returns exact proof without fence or duplicate evidence");
    }

    private sealed class PauseBeforeKeyRead : DbCommandInterceptor
    {
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("[IdempotencyRecords]", StringComparison.Ordinal))
            {
                Entered.TrySetResult();
                await Resume.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }
    private sealed class PauseAfterSave : SaveChangesInterceptor
    {
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await Resume.Task.WaitAsync(cancellationToken);
            return result;
        }
    }
}
