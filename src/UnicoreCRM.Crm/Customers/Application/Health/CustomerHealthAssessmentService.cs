using System.Diagnostics;
using Microsoft.Extensions.Logging;
using UnicoreCRM.CommercialEvidence.CommercialEvidence.Contracts;
using UnicoreCRM.Crm.Customers.Contracts;
using UnicoreCRM.Crm.Customers.Domain;
using UnicoreCRM.Crm.Customers.Domain.Health;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Customers.Application.Health;

internal sealed class CustomerHealthAssessmentService(
    ICustomerPurchaseHealthSignalReader signalReader,
    ISystemCustomerPurchaseHealthSignalReader systemSignalReader,
    TimeProvider timeProvider,
    ILogger<CustomerHealthAssessmentService> logger)
{
    internal async Task<CustomerHealthAssessment?> AssessAsync(
        TrustedWorkspaceContext trustedWorkspace,
        Customer customer,
        CancellationToken cancellationToken) =>
        (await AssessBatchAsync(trustedWorkspace, [customer], cancellationToken))[customer.CustomerId];

    internal async Task<IReadOnlyDictionary<string, CustomerHealthAssessment?>> AssessBatchAsync(
        TrustedWorkspaceContext trustedWorkspace,
        IReadOnlyCollection<Customer> customers,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var asOf = timeProvider.GetUtcNow();
        var assessable = customers.Where(customer => customer.Status != "ARCHIVED").ToArray();
        var references = assessable.Select(BuyerRef).Distinct().ToArray();
        var signals = references.Length == 0
            ? []
            : await signalReader.ReadBatchAsync(trustedWorkspace, references, asOf, cancellationToken);
        var byBuyer = signals.ToDictionary(signal => signal.BuyerRef);
        var result = new Dictionary<string, CustomerHealthAssessment?>();
        foreach (var customer in customers)
        {
            if (customer.Status == "ARCHIVED")
            {
                result[customer.CustomerId] = null;
                continue;
            }
            byBuyer.TryGetValue(BuyerRef(customer), out var signal);
            var calculated = CustomerHealthCalculator.Calculate(
                new(signal?.PurchaseCount ?? 0, signal?.RecentPurchaseTimestamps ?? []), asOf);
            result[customer.CustomerId] = Contract(calculated);
        }
        logger.LogInformation(
            "Customer Health calculated Count={Count} Unknown={Unknown} DurationMs={DurationMs} Algorithm={Algorithm}",
            assessable.Length,
            result.Values.Count(value => value?.HealthBand == "UNKNOWN"),
            Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            CustomerHealthVocabulary.AlgorithmVersion);
        return result;
    }

    internal async Task<IReadOnlyDictionary<string, CustomerHealthAssessment?>> AssessSystemBatchAsync(
        string workspaceId, IReadOnlyCollection<Customer> customers, CancellationToken cancellationToken)
    {
        var asOf = timeProvider.GetUtcNow();
        var assessable = customers.Where(customer => customer.Status != "ARCHIVED").ToArray();
        var references = assessable.Select(BuyerRef).Distinct().ToArray();
        var signals = references.Length == 0 ? [] : await systemSignalReader.ReadBatchAsync(workspaceId, references, asOf, cancellationToken);
        var byBuyer = signals.ToDictionary(signal => signal.BuyerRef);
        return customers.ToDictionary(customer => customer.CustomerId, customer =>
        {
            if (customer.Status == "ARCHIVED") return null;
            byBuyer.TryGetValue(BuyerRef(customer), out var signal);
            return Contract(CustomerHealthCalculator.Calculate(new(signal?.PurchaseCount ?? 0, signal?.RecentPurchaseTimestamps ?? []), asOf));
        });
    }

    private static CustomerPurchaseHealthBuyerRef BuyerRef(Customer customer) =>
        new(customer.RelationshipType switch
        {
            "CONTACT" => PurchaseEvidenceBuyerRefType.Contact,
            "ORGANIZATION_ACCOUNT" => PurchaseEvidenceBuyerRefType.OrganizationAccount,
            _ => throw new InvalidOperationException("Customer relationship type cannot be used for purchase health.")
        }, customer.RelationshipId);

    private static CustomerHealthAssessment Contract(CustomerHealthResult value) => new(
        value.Score,
        value.HealthBand,
        value.ChurnRisk,
        value.Confidence,
        value.PurchaseCount,
        value.LastPurchaseAt is null ? null : Common.CustomerProjection.TimestampValue(value.LastPurchaseAt.Value),
        value.ExpectedPurchaseCadenceDays,
        value.DaysSinceLastPurchase,
        value.ReasonCode,
        value.AlgorithmVersion,
        Common.CustomerProjection.TimestampValue(value.EvaluatedAt));
}
