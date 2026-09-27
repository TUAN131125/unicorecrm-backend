using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using UnicoreCRM.AI.Gateway;
using UnicoreCRM.Crm.Customers.Contracts;
using UnicoreCRM.Operations.Tasks.Contracts;
using UnicoreCRM.PlatformOperations.AiExecution.Contracts;

internal static class ConfirmationVerifier
{
    internal static async Task<int> RunAsync(DateTimeOffset now)
    {
        var passed = 0;
        var original = new ProactiveItemState("confirm_item", "workspace", "member", ProactiveValues.CustomerHealthRisk,
            ProactiveValues.Customer, "customer", "HIGH", "PURCHASE_OVER_EXPECTED_CADENCE", "fp", "cycle", "OPEN",
            now, now, null, null, null, null, "1", 4, now, now);
        foreach (var (item, use, allowed, visible, key) in new[]
        {
            (original with { OwnerMemberId = "other" }, true, true, true, "confirm-key"),
            (original with { WorkspaceId = "foreign" }, true, true, true, "confirm-key"),
            (original with { Status = "SNOOZED" }, true, true, true, "confirm-key"),
            (original with { Status = "DISMISSED" }, true, true, true, "confirm-key"),
            (original with { Status = "RESOLVED" }, true, true, true, "confirm-key"),
            (original with { SubjectType = "LEAD" }, true, true, true, "confirm-key"),
            (original with { TriggerType = "OTHER" }, true, true, true, "confirm-key"),
            (original, false, true, true, "confirm-key"),
            (original, true, false, true, "confirm-key"),
            (original, true, true, false, "confirm-key"),
            (original, true, true, true, "")
        })
        {
            var store = new MemoryStore(); store.Items.Add(item);
            var tasks = new ProbeTasks();
            using var services = new ServiceCollection().AddSingleton<IProactiveStore>(store).BuildServiceProvider();
            var app = new ProactiveTaskConfirmationApplication(new FakeCurrentWorkspace("workspace", "member"),
                new FakeAccessAuthorizer("workspace", "member", use, false), store,
                new FakeAttentionReader(allowed, visible ? new Dictionary<string, CustomerAttentionProjection> { ["customer"] = new("customer", "label") } : new()),
                tasks, services.GetRequiredService<IServiceScopeFactory>(), new FixedClock(now), NullLogger<ProactiveTaskConfirmationApplication>.Instance);
            var result = await app.HandleAsync(item.ItemId, new("Title", "member", "2026-09-30T03:00:00Z"), key, "request", "correlation", default);
            if (result.IsSuccess || tasks.Calls != 0 || store.Audits.Count != 0 || store.Items.Single() != item)
                throw new InvalidOperationException("Confirmation authorization must precede Tasks and preserve item state.");
            passed++;
        }
        foreach (var field in new[] { "taskId", "customerId", "ownerId", "sourceRef", "recordRef", "relationshipRef", "dedupeKey",
            "healthBand", "severity", "reasonCode", "provider", "model", "credential", "credentialRef", "suggestionExecutionId" })
        {
            try { JsonSerializer.Deserialize<ProactiveTaskConfirmationRequest>($"{{\"{field}\":\"override\"}}", new JsonSerializerOptions(JsonSerializerDefaults.Web)); throw new InvalidOperationException("Client authority override accepted."); }
            catch (JsonException) { passed++; }
        }
        Console.WriteLine($"PROACTIVE_CONFIRMATION_SCENARIO_PASS cases={passed}");
        return passed;
    }

    private sealed class ProbeTasks : IProactiveTaskCreationParticipant
    {
        internal int Calls { get; private set; }
        public Task<ProactiveTaskCreationResult> CreateAsync(ProactiveTaskCreationCommand command, CancellationToken ct)
        { Calls++; throw new InvalidOperationException("Tasks must not be called."); }
    }
}
