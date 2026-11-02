using System.Text.Json.Serialization;

namespace UnicoreCRM.Sales.Quotes.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QuoteDraftLineInput(string? ProductNameSnapshot, string? Quantity, QuoteMoney? UnitPrice,
    string? DiscountRate, string? TaxMode, string? ProductId = null, string? SkuSnapshot = null,
    string? ProductTypeSnapshot = null, string? DescriptionSnapshot = null, string? TaxRate = null, string? BillingCycleSnapshot = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QuoteAdjustmentInput(string? Id, string? Label, string? Type, string? Calculation, string? Value);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QuoteDraftRequest(string? Title, string? Currency, IReadOnlyList<QuoteDraftLineInput?>? LineItems,
    QuoteBuyerReference? BuyerRef = null, string? SourcePath = null, string? SourceDealId = null,
    string? ContactId = null, string? SourceLeadId = null, string? OwnerId = null, string? RecipientEmail = null,
    IReadOnlyList<QuoteAdjustmentInput?>? Adjustments = null, string? ValidUntil = null, string? Notes = null,
    QuotePaymentAgreementInput? PaymentAgreement = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QuotePaymentAgreementInput(string Kind, string Currency,
    IReadOnlyList<QuotePaymentAgreementLineInput> Lines, string? PolicyVersion = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QuotePaymentAgreementLineInput(int Sequence, string Label, string Purpose,
    PaymentAmountRuleDocument AmountRule, PaymentDueRuleDocument DueRule, IReadOnlyList<string> AllowedMethodCodes,
    string FulfillmentGate, string? PreferredMethodCode = null, string? Channel = null, string? InvoicePolicyCode = null);

public sealed record QuoteMutationResult(QuoteReadModel Quote);
public sealed record QuoteMutationResponse(string CommandId, string CorrelationId, string AggregateId,
    string AggregateType, long Version, string OccurredAt, string Outcome, QuoteMutationResult Result,
    IReadOnlyList<string> Warnings, IReadOnlyList<string> EmittedEventIds, IReadOnlyList<string> AuditEvidenceIds);
