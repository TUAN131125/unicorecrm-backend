namespace UnicoreCRM.Crm.Deals.Contracts;

/// <summary>
/// Narrow Deals-owned participant for WF-10 OPPORTUNITY qualification. It exposes no persistence
/// and is not a generic cross-module Deal gateway; Deals retains validation, identity, lifecycle,
/// authorization, idempotency, audit and outbox ownership.
/// </summary>
public interface ILeadQualificationDealParticipant
{
    Task<LeadOpportunityDealResult> ValidateOpportunityAsync(
        LeadOpportunityDealCommand command,
        CancellationToken cancellationToken);

    Task<LeadOpportunityDealResult> CreateOpportunityAsync(
        LeadOpportunityDealCommand command,
        CancellationToken cancellationToken);
}

public sealed record LeadOpportunityDealCommand(
    string LeadId,
    string? ContactId,
    string Name,
    string OwnerId,
    string ExpectedCloseDate,
    IReadOnlyList<string> InterestedProductIds,
    DealMoney EstimatedValue,
    string? NeedSummary,
    string? NextActionAt,
    string? NextActionSummary,
    string? NextActionTaskId,
    string RequestId,
    string CorrelationId,
    string IdempotencyKey,
    string? IdempotencyScopeActorId = null);

public sealed record LeadOpportunityDealResult(
    bool IsSuccess,
    string? DealId,
    long? DealVersion,
    string? Outcome,
    string? ErrorCode,
    int? ErrorStatus,
    IReadOnlyDictionary<string, string[]>? FieldErrors);
