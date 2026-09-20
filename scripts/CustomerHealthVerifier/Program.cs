using UnicoreCRM.Crm.Customers.Domain.Health;
using Microsoft.Extensions.Logging.Abstractions;
using UnicoreCRM.CommercialEvidence.CommercialEvidence.Contracts;
using UnicoreCRM.Crm.Customers.Application.Health;
using UnicoreCRM.Crm.Customers.Domain;
using UnicoreCRM.Platform.Workspace.Contracts;

var now = new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);
var passed = 0;

void Check(string name, object? expected, object? actual)
{
    if (!Equals(expected, actual)) throw new InvalidOperationException($"{name}: expected={expected}, actual={actual}");
    passed++;
}

CustomerHealthResult Assess(int count, params DateTimeOffset[] purchases) =>
    CustomerHealthCalculator.Calculate(new(count, purchases), now);

var unknown = Assess(0);
Check("zero score", null, unknown.Score);
Check("zero band", "UNKNOWN", unknown.HealthBand);
Check("zero risk", "UNKNOWN", unknown.ChurnRisk);
Check("zero confidence", "NONE", unknown.Confidence);

var recent = Assess(1, now.AddDays(-1));
Check("recent score", 100, recent.Score);
Check("recent band", "HEALTHY", recent.HealthBand);
Check("one confidence", "LOW", recent.Confidence);
Check("two confidence", "MEDIUM", Assess(2, now.AddDays(-60), now.AddDays(-1)).Confidence);

var regular = Assess(4, now.AddDays(-220), now.AddDays(-190), now.AddDays(-160), now.AddDays(-130));
Check("derived cadence", 30, regular.ExpectedPurchaseCadenceDays);
Check("three confidence", "HIGH", regular.Confidence);
Check("same-day fallback", 90, Assess(3, now.AddDays(-10), now.AddDays(-10), now.AddDays(-10)).ExpectedPurchaseCadenceDays);
Check("lower cadence clamp", 7, Assess(3, now.AddDays(-11), now.AddDays(-6), now.AddDays(-1)).ExpectedPurchaseCadenceDays);
Check("upper cadence clamp", 365, Assess(3, now.AddDays(-1000), now.AddDays(-500), now).ExpectedPurchaseCadenceDays);
Check("default cadence one", 90, recent.ExpectedPurchaseCadenceDays);
Check("default cadence two", 90, Assess(2, now.AddDays(-30), now.AddDays(-1)).ExpectedPurchaseCadenceDays);

CustomerHealthResult Boundary(int days) => Assess(3, now.AddDays(-days - 240), now.AddDays(-days - 120), now.AddDays(-days));
Check("score boundary 80", 80, Boundary(130).Score);
Check("band boundary 80", "HEALTHY", Boundary(130).HealthBand);
Check("score boundary 60", 60, Boundary(165).Score);
Check("band boundary 60", "WATCH", Boundary(165).HealthBand);
Check("score boundary 30", 30, Boundary(228).Score);
Check("band boundary 30", "AT_RISK", Boundary(228).HealthBand);
Check("below 30", "CRITICAL", Boundary(230).HealthBand);

var prior = 101;
for (var day = 0; day <= 400; day += 5)
{
    var score = Assess(1, now.AddDays(-day)).Score!.Value;
    if (score > prior) throw new InvalidOperationException("Monotonic decay violated.");
    prior = score;
}
passed++;

var futureExcluded = Assess(2, now.AddDays(-2), now.AddDays(4));
Check("future purchase excluded count", 1, futureExcluded.PurchaseCount);
Check("future purchase cannot improve last purchase", now.AddDays(-2), futureExcluded.LastPurchaseAt);
Check("deterministic replay", recent, Assess(1, now.AddDays(-1)));
Check("algorithm version", "CUSTOMER_HEALTH_PURCHASE_RECENCY_V1", recent.AlgorithmVersion);

var signalReader = new CountingSignalReader(now);
var assessmentService = new CustomerHealthAssessmentService(signalReader,
    new FixedTimeProvider(now), NullLogger<CustomerHealthAssessmentService>.Instance);
var trusted = new TrustedWorkspaceContext("workspace_health", "account_health", "member_health", "membership_health");
var customers = new[]
{
    new Customer(trusted.WorkspaceId, trusted.MemberId, "CONTACT", "contact_health_a", new CustomerProfile(), now),
    new Customer(trusted.WorkspaceId, trusted.MemberId, "ORGANIZATION_ACCOUNT", "organization_health_b", new CustomerProfile(), now)
};
var assessed = await assessmentService.AssessBatchAsync(trusted, customers, CancellationToken.None);
Check("one batch signal read for page", 1, signalReader.BatchCalls);
Check("batch enriches every visible customer", 2, assessed.Count);

Console.WriteLine($"CUSTOMER_HEALTH_SCENARIO_CORPUS_PASS cases={passed}");

sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

sealed class CountingSignalReader(DateTimeOffset now) : ICustomerPurchaseHealthSignalReader
{
    internal int BatchCalls { get; private set; }
    public Task<CustomerPurchaseHealthSignalSnapshot?> ReadAsync(TrustedWorkspaceContext trustedWorkspace,
        CustomerPurchaseHealthBuyerRef buyerRef, DateTimeOffset asOf, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Batch enrichment must not call the single-reader path.");
    public Task<IReadOnlyList<CustomerPurchaseHealthSignalSnapshot>> ReadBatchAsync(TrustedWorkspaceContext trustedWorkspace,
        IReadOnlyCollection<CustomerPurchaseHealthBuyerRef> buyerRefs, DateTimeOffset asOf, CancellationToken cancellationToken)
    {
        BatchCalls++;
        return Task.FromResult<IReadOnlyList<CustomerPurchaseHealthSignalSnapshot>>(buyerRefs
            .Select(reference => new CustomerPurchaseHealthSignalSnapshot(reference, 1, now.AddDays(-1), now.AddDays(-1), [now.AddDays(-1)]))
            .ToArray());
    }
}
