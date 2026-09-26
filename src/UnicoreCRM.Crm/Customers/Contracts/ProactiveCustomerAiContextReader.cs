namespace UnicoreCRM.Crm.Customers.Contracts;

public sealed record ProactiveCustomerAiContext(
    string CustomerId, string DisplayLabel, string? CustomerStatus,
    string HealthBand, string ChurnRisk, string HealthReasonCode, string HealthAlgorithmVersion);

public sealed record ProactiveCustomerAiContextResult(bool IsAuthorized, ProactiveCustomerAiContext? Context = null);

public interface IProactiveCustomerAiContextReader
{
    Task<ProactiveCustomerAiContextResult> ReadAsync(string customerId,
        CustomerAttentionRequestContext requestContext, CancellationToken cancellationToken);
}
