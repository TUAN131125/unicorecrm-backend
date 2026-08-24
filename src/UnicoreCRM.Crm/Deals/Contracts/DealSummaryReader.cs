namespace UnicoreCRM.Crm.Deals.Contracts;

public enum DealSummaryReadStatus
{
    Succeeded,
    AccessDenied,
    WorkspaceMismatch,
    InvalidReference,
    NotFound
}

public sealed record DealSummaryProjection(
    string DealId,
    string? Name,
    string? StageCode,
    string? StageCategory,
    string? OpportunityScore,
    string? ExpectedCloseDate,
    string? NextActionAt,
    string? NextActionSummary);

public sealed record DealSummaryReadResult(
    DealSummaryReadStatus Status,
    DealSummaryProjection? Summary = null);

public interface IDealSummaryReader
{
    Task<DealSummaryReadResult> ReadAsync(
        string dealId,
        string requestId,
        string correlationId,
        CancellationToken cancellationToken);
}
