namespace UnicoreCRM.Crm.Customers.Contracts;

public sealed record ProactiveCustomerHealthFact(
    string CustomerId,
    string? OwnerMemberId,
    string CustomerStatus,
    string? HealthBand,
    string? ChurnRisk,
    string? ReasonCode,
    string? AlgorithmVersion,
    string SourceVersion);

public sealed record ProactiveCustomerHealthPage(
    IReadOnlyList<ProactiveCustomerHealthFact> Items,
    string? NextCursor);

/// <summary>Customers-owned, system-only bounded projection for deterministic proactive evaluation.</summary>
public interface IProactiveCustomerHealthReader
{
    Task<ProactiveCustomerHealthPage> ReadPageAsync(
        string workspaceId,
        string? cursor,
        int limit,
        CancellationToken cancellationToken);
}
