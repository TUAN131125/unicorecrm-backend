using System.Reflection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Mono.Cecil;
using Xunit;
using static UnicoreCRM.ArchitectureTests.ArchitectureInventory;

namespace UnicoreCRM.ArchitectureTests;

public sealed class AtomicWorkflowAnchorStoreTests
{
    private const string Root = "UnicoreCRM.Workflows.Atomic.";
    private static readonly Assembly Workflows = Assembly.Load("UnicoreCRM.Workflows");
    private static Type WorkflowType(string name) => Workflows.GetType(Root + name, throwOnError: true)!;

    [Fact]
    public void Four_coordinators_and_storage_port_have_no_provider_or_infrastructure_dependency()
    {
        using var assembly = AssemblyDefinition.ReadAssembly(Workflows.Location);
        var namespaces = new[] { "QualifyLeadForOpportunity", "QualifyLeadForNurture", "HandoverLead", "ConvertLeadToCustomer", "Persistence" }
            .Select(x => Root + "Application." + x).ToArray();
        var types = assembly.MainModule.GetTypes().Where(t => namespaces.Contains(Namespace(t))).ToArray();
        Assert.NotEmpty(types);
        var invalid = types.SelectMany(References).SelectMany(Expand).Where(t =>
            Namespace(t).StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
            || Namespace(t).StartsWith("Microsoft.Data.SqlClient", StringComparison.Ordinal)
            || Namespace(t).StartsWith(Root + "Infrastructure", StringComparison.Ordinal)).Select(t => t.FullName);
        Assert.Empty(invalid);
        var port = WorkflowType("Application.Persistence.IAtomicWorkflowAnchorStore");
        Assert.False(port.IsPublic);
        Assert.DoesNotContain(port.GetMethods(), m => m.IsGenericMethod || m.ReturnType.IsGenericType
            && m.ReturnType.GetGenericTypeDefinition() == typeof(IQueryable<>));
    }

    [Theory]
    [InlineData("Qualification")]
    [InlineData("Handover")]
    [InlineData("Conversion")]
    public async Task Create_and_progress_keep_the_same_tracked_anchor_and_each_save_boundary(string kind)
    {
        using var session = new Session();
        var anchor = NewAnchor(kind);
        await session.CallAsync("Create" + kind + "Async", anchor, CancellationToken.None);
        Assert.Equal(1, session.Writes.Calls);
        Assert.Same(anchor, Assert.Single(session.Writes.Entries).Entity);

        session.Context.Entry(anchor).State = EntityState.Unchanged;
        session.Context.Entry(anchor).Property("UpdatedAt").CurrentValue = DateTimeOffset.UtcNow;
        await session.CallAsync("Persist" + kind + "Async", anchor, CancellationToken.None);
        Assert.Equal(2, session.Writes.Calls);
        var progress = Assert.Single(session.Writes.Entries);
        Assert.Same(anchor, progress.Entity);
        Assert.Equal(EntityState.Modified, progress.State);
        Assert.Same(anchor, Assert.Single(session.Context.ChangeTracker.Entries()).Entity);
    }

    [Theory]
    [InlineData("Handover")]
    [InlineData("Conversion")]
    public async Task Completion_saves_tracked_anchor_and_outbox_together_without_intermediate_commit(string kind)
    {
        using var session = new Session();
        var anchor = NewAnchor(kind);
        session.Context.Attach(anchor);
        var stage = session.Context.Entry(anchor).Property("Stage");
        stage.CurrentValue = Enum.Parse(stage.Metadata.ClrType, "Completed");
        var message = NewMessage();

        await session.CallAsync("Persist" + kind + "CompletionAsync", anchor, message, CancellationToken.None);

        Assert.Equal(1, session.Writes.Calls);
        Assert.Equal(2, session.Writes.Entries.Length);
        Assert.Contains(session.Writes.Entries, x => ReferenceEquals(x.Entity, anchor) && x.State == EntityState.Modified);
        Assert.Contains(session.Writes.Entries, x => ReferenceEquals(x.Entity, message) && x.State == EntityState.Added);
    }

    [Theory]
    [InlineData("Handover")]
    [InlineData("Conversion")]
    public async Task Failed_completion_preserves_original_conflict_and_never_attempts_a_second_save(string kind)
    {
        var conflict = new DbUpdateConcurrencyException("Synthetic conflict");
        using var session = new Session(conflict);
        var anchor = NewAnchor(kind);
        session.Context.Attach(anchor);
        session.Context.Entry(anchor).Property("UpdatedAt").CurrentValue = DateTimeOffset.UtcNow;
        var message = NewMessage();

        var thrown = await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            session.CallAsync("Persist" + kind + "CompletionAsync", anchor, message, CancellationToken.None));

        Assert.Same(conflict, thrown);
        Assert.Equal(1, session.Writes.Calls);
        Assert.Equal(2, session.Context.ChangeTracker.Entries().Count());
        session.Call("DiscardPendingAnchorChanges");
        Assert.Empty(session.Context.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData("Qualification")]
    [InlineData("Handover")]
    [InlineData("Conversion")]
    public async Task Detached_progress_cannot_silently_update_a_different_session(string kind)
    {
        using var session = new Session();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.CallAsync("Persist" + kind + "Async", NewAnchor(kind), CancellationToken.None));
        Assert.Equal(0, session.Writes.Calls);
        Assert.Empty(session.Context.ChangeTracker.Entries());
    }

    [Fact]
    public void Provider_filters_preserve_exact_update_concurrency_and_nested_contention_semantics()
    {
        using var session = new Session();
        var concurrent = new DbUpdateConcurrencyException("Synthetic conflict");
        Assert.True(session.Check("IsUpdateFailure", concurrent));
        Assert.True(session.Check("IsConcurrencyConflict", concurrent));
        Assert.True(session.Check("IsProviderContention", new Exception("wrapper", concurrent)));
        Assert.False(session.Check("IsConcurrencyConflict", new Exception("wrapper", concurrent)));
        Assert.False(session.Check("IsUpdateFailure", new Exception("wrapper", concurrent)));
        Assert.False(session.Check("IsProviderContention", new OperationCanceledException()));
        Assert.False(session.Check("IsProviderContention", new InvalidOperationException()));
    }

    [Theory]
    [InlineData(1205, true, false)]
    [InlineData(1222, true, false)]
    [InlineData(2601, false, true)]
    [InlineData(2627, false, true)]
    [InlineData(208, false, false)]
    public void Sql_number_classification_preserves_direct_unique_race_and_recursive_contention(int number, bool contention, bool unique)
    {
        using var session = new Session();
        var sql = SqlFailure(number);
        var update = new DbUpdateException("Synthetic update failure", sql);
        Assert.Equal(contention, session.Check("IsProviderContention", sql));
        Assert.Equal(contention, session.Check("IsProviderContention", new Exception("participant wrapper", update)));
        Assert.Equal(unique, session.Check("IsUniqueInsertRace", update));
        Assert.False(session.Check("IsUniqueInsertRace", sql));
        Assert.False(session.Check("IsUniqueInsertRace", new DbUpdateException("nested", new Exception("wrapper", sql))));
        Assert.True(session.Check("IsUpdateFailure", update));
        Assert.False(session.Check("IsConcurrencyConflict", update));
    }

    private static object NewAnchor(string kind)
    {
        var name = kind == "Conversion" ? "LeadCustomerConversionAnchor" : "Lead" + kind + "Anchor";
        var anchor = Activator.CreateInstance(WorkflowType("Domain." + name), nonPublic: true)!;
        Set(anchor, "ScopeKey", Guid.NewGuid().ToString("N"));
        Set(anchor, "WorkspaceId", "synthetic-workflow-storage-test");
        return anchor;
    }

    private static object NewMessage()
    {
        var message = Activator.CreateInstance(WorkflowType("Domain.WorkflowIntegrationOutboxMessage"), nonPublic: true)!;
        Set(message, "EventId", Guid.NewGuid().ToString("N"));
        Set(message, "WorkspaceId", "synthetic-workflow-storage-test");
        return message;
    }

    private static void Set(object instance, string property, object value) => instance.GetType()
        .GetProperty(property, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(instance, value);

    private static SqlException SqlFailure(int number)
    {
        var errors = (SqlErrorCollection)Activator.CreateInstance(typeof(SqlErrorCollection), nonPublic: true)!;
        var constructor = typeof(SqlError).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .OrderBy(c => c.GetParameters().Length).First();
        var args = constructor.GetParameters().Select((p, i) => i == 0 ? (object)number
            : p.ParameterType == typeof(string) ? "Synthetic provider failure"
            : p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null).ToArray();
        var error = (SqlError)constructor.Invoke(args);
        typeof(SqlErrorCollection).GetMethod("Add", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(errors, [error]);
        return (SqlException)typeof(SqlException).GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Single(m => m.Name == "CreateException" && m.GetParameters().Length == 2).Invoke(null, [errors, "test"] )!;
    }

    // Reflection keeps the production port internal without granting tests a new assembly friendship.
    private sealed class Session : IDisposable
    {
        internal DbContext Context { get; }
        internal WritesInterceptor Writes { get; }
        private object Store { get; }

        internal Session(Exception? failure = null)
        {
            Writes = new(failure);
            var contextType = WorkflowType("Infrastructure.Persistence.WorkflowsDbContext");
            var builder = (DbContextOptionsBuilder)Activator.CreateInstance(typeof(DbContextOptionsBuilder<>).MakeGenericType(contextType))!;
            builder.UseSqlServer("Server=localhost;Database=UnusedWorkflowStorageTest;Integrated Security=true;TrustServerCertificate=true");
            builder.AddInterceptors(Writes);
            Context = (DbContext)Activator.CreateInstance(contextType, builder.Options)!;
            Store = Activator.CreateInstance(WorkflowType("Infrastructure.Persistence.EfAtomicWorkflowAnchorStore"), Context)!;
        }

        internal object? Call(string name, params object[] args) => Store.GetType().GetMethod(name)!.Invoke(Store, args);
        internal Task CallAsync(string name, params object[] args) => (Task)Call(name, args)!;
        internal bool Check(string name, Exception exception) => (bool)Call(name, exception)!;
        public void Dispose() => Context.Dispose();
    }

    private sealed class WritesInterceptor(Exception? failure) : SaveChangesInterceptor
    {
        internal int Calls { get; private set; }
        internal (object Entity, EntityState State)[] Entries { get; private set; } = [];
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Calls++;
            Entries = eventData.Context!.ChangeTracker.Entries().Select(e => (e.Entity, e.State)).ToArray();
            if (failure is not null) throw failure;
            // No database connection or synthetic claim of SQL transaction success.
            return ValueTask.FromResult(InterceptionResult<int>.SuppressWithResult(0));
        }
    }
}
