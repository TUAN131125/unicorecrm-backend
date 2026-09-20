namespace UnicoreCRM.Crm.Customers.Contracts;

public enum CustomerSummaryReadStatus { Succeeded, AccessDenied, WorkspaceMismatch, InvalidReference, NotFound }
public sealed record CustomerSummaryProjection(string CustomerId, string? CustomerCode, string? Status, string? Health, string? Tier, string? Segment, string? NextCareAt, long Version)
{
    public CustomerHealthAssessment? HealthAssessment { get; init; }
}
public sealed record CustomerSummaryReadResult(CustomerSummaryReadStatus Status, CustomerSummaryProjection? Summary = null);
public interface ICustomerSummaryReader
{
    Task<CustomerSummaryReadResult> ReadAsync(string customerId, string requestId, string correlationId, CancellationToken cancellationToken);
}
