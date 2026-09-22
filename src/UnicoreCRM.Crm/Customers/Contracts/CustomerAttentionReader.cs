namespace UnicoreCRM.Crm.Customers.Contracts;

public sealed record CustomerAttentionRequestContext(string RequestId, string CorrelationId);

public sealed record CustomerAttentionProjection(
    string CustomerId,
    string DisplayLabel);

public sealed record CustomerAttentionReadResult(
    bool IsAuthorized,
    IReadOnlyDictionary<string, CustomerAttentionProjection> Items);

/// <summary>Customers-owned, caller-authorized bounded projection for Attention reads.</summary>
public interface ICustomerAttentionReader
{
    Task<CustomerAttentionReadResult> ReadAuthorizedAsync(
        IReadOnlyCollection<string> customerIds,
        CustomerAttentionRequestContext requestContext,
        CancellationToken cancellationToken);
}
