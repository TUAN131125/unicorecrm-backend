namespace UnicoreCRM.Crm.Leads.Contracts;

public enum LeadSummaryReadStatus
{
    Succeeded,
    AccessDenied,
    WorkspaceMismatch,
    InvalidReference,
    NotFound
}

public sealed record LeadSummaryProjection(
    string LeadId,
    string? DisplayName,
    string? WorkState,
    int? Score,
    string? Priority,
    string? NextFollowUpAt);

public sealed record LeadSummaryReadResult(
    LeadSummaryReadStatus Status,
    LeadSummaryProjection? Summary = null);

public interface ILeadSummaryReader
{
    Task<LeadSummaryReadResult> ReadAsync(
        string leadId,
        string requestId,
        string correlationId,
        CancellationToken cancellationToken);
}
