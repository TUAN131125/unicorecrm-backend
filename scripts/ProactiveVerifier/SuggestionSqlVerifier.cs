using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using UnicoreCRM.AI.Gateway;
using UnicoreCRM.AI.Providers;
using UnicoreCRM.AI.Usage;
using UnicoreCRM.Crm.Customers.Contracts;
using UnicoreCRM.PlatformOperations.AiExecution.Contracts;
using UnicoreCRM.PlatformOperations.AiExecution.Infrastructure;

internal static class SuggestionSqlVerifier
{
    internal static async Task<int> RunAsync(DbContextOptions<AiExecutionDbContext> options, DateTimeOffset now)
    {
        var passed = 0;
        void Check(string name, bool condition) { if (!condition) throw new InvalidOperationException(name); passed++; }
        var protection = new EphemeralDataProtectionProvider();
        var configuration = new ConfigurationBuilder().Build();
        var workspace = new FakeCurrentWorkspace("suggestion_sql", "member");
        var item = new ProactiveItemState("suggestion_item", "suggestion_sql", "member", ProactiveValues.CustomerHealthRisk,
            ProactiveValues.Customer, "customer", ProactiveValues.High, "PURCHASE_OVER_EXPECTED_CADENCE", "fp", "cycle", ProactiveValues.Open,
            now, now, null, null, null, null, "1", 8, now, now);
        await using (var seed = new AiExecutionDbContext(options))
        {
            await new EfProactiveStore(seed).SaveItemAsync(item, new("suggestion_seed", item.WorkspaceId, "member", "ITEM_CREATED", item.ItemId,
                item.SubjectId, "{}", "correlation", now), default);
            seed.Configurations.Add(new WorkspaceAiConfigurationRow
            {
                WorkspaceId = item.WorkspaceId, PrimaryProvider = "OPENAI", PrimaryModel = "gpt-5-mini",
                PrimaryCredentialSource = "WORKSPACE", Status = "ACTIVE", CreatedAt = now, UpdatedAt = now,
                ActivePolicyJson = JsonSerializer.Serialize(new WorkspaceAiConfigurationDraft("OPENAI", "gpt-5-mini", "WORKSPACE", false, null, null, null, false)),
                ActivePrimaryProtectedCredential = protection.CreateProtector("UnicoreCRM.WorkspaceAiCredential.v1").Protect("sql-test-credential")
            });
            await seed.SaveChangesAsync();
        }
        using var services = new ServiceCollection()
            .AddScoped(_ => new AiExecutionDbContext(options))
            .AddScoped<IProactiveStore, EfProactiveStore>().BuildServiceProvider();
        foreach (var mode in new[] { "success", "invalid", "unavailable", "dirty-ledger" })
        {
            await using var db = new AiExecutionDbContext(options);
            var ledger = new EfAiExecutionLedger(db);
            var adapter = new SqlAdapter(options, mode);
            var resolver = new WorkspaceAiProviderResolver(workspace, new EfWorkspaceAiConfigurationStore(db), protection, configuration, new AiProviderCatalog(configuration));
            var provider = new WorkspaceProductionAiProvider(resolver, [adapter], workspace, ledger, new AiProviderCircuitBreaker(TimeProvider.System),
                new AiWorkspaceGuardrails(TimeProvider.System, 2, 100), new AiProviderRuntimeOptions(TimeSpan.FromSeconds(5)), TimeProvider.System);
            IAiUsageRecorder recorder = mode == "dirty-ledger" ? new DirtyUsage(db) : new DurableAiUsageRecorder(NullLogger<DurableAiUsageRecorder>.Instance, ledger, TimeProvider.System);
            var app = new ProactiveSuggestionApplication(workspace, new FakeAccessAuthorizer(item.WorkspaceId, "member", true, false),
                new EfProactiveStore(db), new SqlContext(), provider, recorder, services.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System);
            try
            {
                var result = await app.HandleAsync(item.ItemId, new("en"), "request", "correlation", default);
                Check(mode + " result", mode == "success" ? result.IsSuccess : !result.IsSuccess);
            }
            catch (DbUpdateException) when (mode == "dirty-ledger") { passed++; }
            await using var observed = new AiExecutionDbContext(options);
            Check("business item unchanged", await new EfProactiveStore(observed).ReadItemAsync(item.WorkspaceId, item.ItemId, default) == item);
            Check("requested durably observed", adapter.Calls == (mode == "unavailable" ? 2 : 1));
            Check("attempt evidence", await observed.ProviderAttempts.CountAsync(x => x.ExecutionId == adapter.ExecutionId) == adapter.Calls);
            Check("Workspace selects provider and model", await observed.ProviderAttempts.AllAsync(x => x.ExecutionId != adapter.ExecutionId || x.Provider == "OPENAI" && x.Model == "gpt-5-mini"));
            Check("terminal audit clean", await observed.ProactiveAudits.CountAsync(x => x.ItemId == item.ItemId && x.SafeSummaryJson.Contains(adapter.ExecutionId!) && x.Action != "AI_SUGGESTION_REQUESTED") == 1);
            if (mode == "dirty-ledger") Check("ledger failure is terminal failure", await observed.ProactiveAudits.AnyAsync(x => x.Action == "AI_SUGGESTION_FAILED" && x.SafeSummaryJson.Contains(adapter.ExecutionId!)));
            Check("dirty changes never flushed", !await observed.ProactiveAudits.AnyAsync(x => x.AuditId == "stale_failed_audit"));
            if (mode != "dirty-ledger") Check("durable ledger", await observed.Executions.AnyAsync(x => x.ExecutionId == adapter.ExecutionId && x.Operation == "requestProactiveSuggestion"));
            var audits = await observed.ProactiveAudits.Where(x => x.ItemId == item.ItemId).Select(x => x.SafeSummaryJson).ToArrayAsync();
            Check("no context, output or credentials", audits.All(x => !x.Contains("sensitive-label", StringComparison.Ordinal) && !x.Contains("provider-output", StringComparison.Ordinal) && !x.Contains("sql-test-credential", StringComparison.Ordinal)));
            var ledgerJson = JsonSerializer.Serialize(new
            {
                executions = await observed.Executions.Where(x => x.ExecutionId == adapter.ExecutionId).ToArrayAsync(),
                attempts = await observed.ProviderAttempts.Where(x => x.ExecutionId == adapter.ExecutionId).ToArrayAsync()
            });
            Check("safe ledger", !ledgerJson.Contains("sensitive-label", StringComparison.Ordinal)
                && !ledgerJson.Contains("provider-output", StringComparison.Ordinal) && !ledgerJson.Contains("sql-test-credential", StringComparison.Ordinal));
        }
        // Fail the required audit in a fresh scoped context. Provider must remain untouched.
        var rejectedOptions = new DbContextOptionsBuilder<AiExecutionDbContext>(options).AddInterceptors(new RejectRequestedAudit()).Options;
        using var failingServices = new ServiceCollection().AddScoped(_ => new AiExecutionDbContext(rejectedOptions))
            .AddScoped<IProactiveStore, EfProactiveStore>().BuildServiceProvider();
        await using (var db = new AiExecutionDbContext(options))
        {
            var never = new NeverProvider();
            var app = new ProactiveSuggestionApplication(workspace, new FakeAccessAuthorizer(item.WorkspaceId, "member", true, false),
                new EfProactiveStore(db), new SqlContext(), never, new DirtyUsage(db), failingServices.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System);
            try { await app.HandleAsync(item.ItemId, new("en"), "request", "correlation", default); throw new InvalidOperationException("audit failure ignored"); }
            catch (AuditRejectedException) { Check("no provider on failed required audit", never.Calls == 0); }
        }
        Console.WriteLine($"PROACTIVE_SUGGESTION_SQL_PASS cases={passed}");
        return passed;
    }

    private sealed class SqlContext : IProactiveCustomerAiContextReader
    {
        public Task<ProactiveCustomerAiContextResult> ReadAsync(string id, CustomerAttentionRequestContext context, CancellationToken ct) =>
            Task.FromResult(new ProactiveCustomerAiContextResult(true, new(id, "sensitive-label", "ACTIVE", "AT_RISK", "HIGH", "PURCHASE_OVER_EXPECTED_CADENCE", "V1")));
    }
    private sealed class SqlAdapter(DbContextOptions<AiExecutionDbContext> options, string mode) : IProductionAiProviderAdapter
    {
        public string ProviderId => "OPENAI";
        internal int Calls { get; private set; }
        internal string? ExecutionId { get; private set; }
        public async Task<AiProviderAdapterResult> CompleteAsync(string model, string credential, AiProviderRequest request, CancellationToken ct)
        {
            Calls++; ExecutionId = request.ExecutionId;
            await using var observed = new AiExecutionDbContext(options);
            if (!await observed.ProactiveAudits.AnyAsync(x => x.Action == "AI_SUGGESTION_REQUESTED" && x.SafeSummaryJson.Contains(request.ExecutionId), ct))
                throw new InvalidOperationException("required audit not durable before provider");
            if (request.OutputContract != AiProviderOutputContract.ProactiveSuggestion || model != "gpt-5-mini" || credential != "sql-test-credential")
                throw new InvalidOperationException("incorrect workspace contract");
            if (mode == "unavailable") throw new AiProviderExecutionException(AiProviderFailure.Unavailable, "Unavailable.");
            return new("OPENAI", model, mode == "invalid" ? "{" : """{"summary":"provider-output","suggestedNextStep":"Review","taskDraft":{"title":"Follow up","description":"Discuss current needs"}}""", TimeSpan.FromMilliseconds(1));
        }
    }
    private sealed class DirtyUsage(AiExecutionDbContext db) : IAiUsageRecorder
    {
        public async Task RecordAsync(AiUsageEvent value, CancellationToken ct)
        {
            db.ProactiveAudits.Add(new ProactiveAuditRow { AuditId = "stale_failed_audit", WorkspaceId = value.WorkspaceId, Action = new string('x', 81), CorrelationId = "test", OccurredAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(ct);
        }
    }
    private sealed class NeverProvider : IAiProvider
    {
        internal int Calls { get; private set; }
        public AiProviderDescriptor Descriptor => new("unused", "unused");
        public Task<AiProviderResponse> CompleteAsync(AiProviderRequest request, CancellationToken ct) { Calls++; throw new InvalidOperationException("unexpected provider"); }
    }
    private sealed class AuditRejectedException : Exception;
    private sealed class RejectRequestedAudit : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) => throw new AuditRejectedException();
    }
}
