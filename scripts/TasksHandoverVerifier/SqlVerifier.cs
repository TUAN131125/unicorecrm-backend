using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using UnicoreCRM.Operations.Tasks.Application.Common;
using UnicoreCRM.Operations.Tasks.Application.LeadHandover;
using UnicoreCRM.Operations.Tasks.Contracts;
using UnicoreCRM.Operations.Tasks.Domain;
using UnicoreCRM.Operations.Tasks.Infrastructure.Persistence;

static partial class SqlVerifier
{
    internal static async Task RunAsync()
    {
        var database = $"UnicoreCRM_TasksHandoverVerifier_{Guid.NewGuid():N}";
        var connection = $"Server=(localdb)\\MSSQLLocalDB;Database={database};Trusted_Connection=True;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<TasksDbContext>().UseSqlServer(connection,
            sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "tasks")).Options;
        var fixture = new Fixture();
        await using var setup = new TasksDbContext(options);
        try
        {
            await setup.Database.MigrateAsync();
            if (setup.Database.HasPendingModelChanges()) throw new InvalidOperationException("Migration snapshot mismatch");
            Console.WriteLine("PASS SQL migrations and model snapshot");
            var seed = fixture.Add();
            setup.Tasks.Add(seed);
            await setup.SaveChangesAsync();
            var command = fixture.Command();
            Participant Create(TasksDbContext context) => new(new TaskAuthorization(fixture.Access),
                new CurrentWorkspace(fixture.Trusted), fixture.Service, fixture.Members, new EfTasksPersistence(context), TimeProvider.System);
            await using (var preflight = new TasksDbContext(options))
            {
                var participant = Create(preflight);
                if (!(await participant.ValidateAsync(command, default)).IsSuccess) throw new InvalidOperationException("SQL preflight failed");
                await using (var external = new TasksDbContext(options))
                {
                    await external.Tasks.Where(task => task.TaskId == seed.TaskId)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(task => task.Version, 7L));
                }
                if (!(await participant.ExecuteAsync(command, default)).IsSuccess) throw new InvalidOperationException("Commit used stale preflight state");
                await using var fresh = new TasksDbContext(options);
                if ((await fresh.Tasks.SingleAsync(task => task.TaskId == seed.TaskId)).Version != 8)
                    throw new InvalidOperationException("Commit failed to use fresh authoritative version");
                Console.WriteLine("PASS SQL commit refreshes tasks tracked by preflight");
            }
            command = command with { HandoverId = "handover_concurrent" };
            async Task<LeadHandoverTaskResult> Execute()
            {
                await using var context = new TasksDbContext(options);
                return await Create(context).ExecuteAsync(command, default);
            }
            var results = await Task.WhenAll(Execute(), Execute());
            if (results.Any(result => !result.IsSuccess) || results[0].HandoverTaskId != results[1].HandoverTaskId)
                throw new InvalidOperationException("Concurrent duplicate commit");
            await using var read = new TasksDbContext(options);
            if (await read.Tasks.CountAsync() != 3 || await read.AuditRecords.CountAsync() != 5 ||
                await read.OutboxMessages.CountAsync() != 5 || await read.IdempotencyRecords.CountAsync() != 2)
                throw new InvalidOperationException("Duplicate SQL evidence");
            Console.WriteLine("PASS SQL concurrent participant executions converge on one takeover and one evidence set");

            // A write failure leaves both reassignment and takeover/evidence uncommitted.
            var failedCommand = command with { HandoverId = "handover_failed" };
            var failedOptions = new DbContextOptionsBuilder<TasksDbContext>().UseSqlServer(connection)
                .AddInterceptors(new FailSave()).Options;
            await using (var failing = new TasksDbContext(failedOptions))
            {
                try
                {
                    await Create(failing).ExecuteAsync(failedCommand, default);
                    throw new InvalidOperationException("Injected failure was not reached");
                }
                catch (InjectedFailure) { }
            }
            await using var afterFailure = new TasksDbContext(options);
            if (await afterFailure.Tasks.CountAsync() != 3 || await afterFailure.AuditRecords.CountAsync() != 5 ||
                await afterFailure.OutboxMessages.CountAsync() != 5 || await afterFailure.IdempotencyRecords.CountAsync() != 2)
                throw new InvalidOperationException("Failed transaction committed effects");
            Console.WriteLine("PASS SQL participant write failure rolls back all Task/evidence effects");

            // The authoritative locked range prevents a matching OPEN insertion before commit.
            await using var locking = new TasksDbContext(options);
            var persistence = new EfTasksPersistence(locking);
            await using var transaction = await persistence.BeginSerializableAsync(default);
            await persistence.LoadEligibleLeadHandoverTasksForUpdateAsync("ws_test", "lead_test", default);
            await using var inserting = new TasksDbContext(options);
            var late = new TaskItem("ws_test", "late", null, TaskPriority.Normal, "member_old", command.FrozenDueAt,
                new(null, null, "leads", "lead_test", null, null, null, null), null, DateTimeOffset.UtcNow);
            inserting.Tasks.Add(late);
            var insert = inserting.SaveChangesAsync();
            await Task.Delay(500);
            if (insert.IsCompleted) throw new InvalidOperationException("Eligible insertion bypassed locked range");
            await transaction.CommitAsync(default);
            await insert;
            Console.WriteLine("PASS SQL authoritative range locks matching insertions until commit");
            await using var recovering = new TasksDbContext(options);
            var replay = await Create(recovering).ExecuteAsync(command with { ExecutorServicePrincipalId = Participant.RecoveryPrincipal }, default);
            if (!replay.IsSuccess || replay.HandoverTaskId != results[0].HandoverTaskId ||
                replay.ReassignedTaskIds.Contains(late.TaskId)) throw new InvalidOperationException("Replay widened snapshot");
            Console.WriteLine("PASS SQL recovery replays frozen snapshot after late insertion");
            await RunFenceChecksAsync(options);
        }
        finally
        {
            // Only the generated verifier database is removed.
            await setup.Database.EnsureDeletedAsync();
        }
    }
    private sealed class InjectedFailure : Exception;
    private sealed class FailSave : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default) => throw new InjectedFailure();
    }
}
