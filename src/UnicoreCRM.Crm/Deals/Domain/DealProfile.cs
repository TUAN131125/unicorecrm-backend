namespace UnicoreCRM.Crm.Deals.Domain;

internal sealed record DealProfile(
    string Name,
    DealBuyer BuyerRef,
    DealMoneyValue Amount,
    string OpportunityScore,
    string OwnerId,
    DateOnly ExpectedCloseDate,
    string? ContactId,
    string? SourceLeadId,
    IReadOnlyList<string> InterestedProductIds,
    string? Notes);

internal sealed record DealBuyer(string Type, string Id);
internal sealed record DealMoneyValue(string Amount, string Currency);
