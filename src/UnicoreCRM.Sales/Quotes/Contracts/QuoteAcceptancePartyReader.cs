namespace UnicoreCRM.Sales.Quotes.Contracts;

/// <summary>CRM assertions and held versions only; never identity verification or signing authority.</summary>
public interface IQuoteAcceptancePartyReader
{
    Task<IQuoteAcceptancePartyLease> AcquireAsync(string buyerType,string buyerId,string contactId,string? relationshipId,
        string requestId,string correlationId,CancellationToken ct);
}
public interface IQuoteAcceptancePartyLease:IAsyncDisposable
{
    QuoteAcceptancePartySnapshot? Snapshot { get; }
}
public sealed record QuoteAcceptancePartySnapshot(string WorkspaceId,string BuyerType,string BuyerId,long BuyerVersion,
    string ContactId,long ContactVersion,string DisplayName,string? RelationshipId,string? RelationshipRole,bool? PrimaryAffiliation,
    string? EffectiveFrom,string? EffectiveTo,string ObservedAt,bool Eligible);
