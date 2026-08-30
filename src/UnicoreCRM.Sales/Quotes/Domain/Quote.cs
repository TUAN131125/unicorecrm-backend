namespace UnicoreCRM.Sales.Quotes.Domain;

/// <summary>
/// Quotes-owned durable read state. This slice exposes no Quote creation or mutation path;
/// controlled verifier fixtures and future separately admitted Quotes commands are the only
/// current ways rows can be written.
/// </summary>
internal sealed class Quote
{
    private Quote() { }

    internal string WorkspaceId { get; private set; } = null!;
    internal string QuoteId { get; private set; } = null!;
    internal string QuoteNumber { get; private set; } = null!;
    internal int QuoteRevision { get; private set; }
    internal string RootQuoteId { get; private set; } = null!;
    internal string? RevisionOfQuoteId { get; private set; }
    internal string BuyerType { get; private set; } = null!;
    internal string BuyerId { get; private set; } = null!;
    internal string SourcePath { get; private set; } = null!;
    internal string? SourceDealId { get; private set; }
    internal string? ContactId { get; private set; }
    internal string? SourceLeadId { get; private set; }
    internal string Status { get; private set; } = null!;
    internal string Title { get; private set; } = null!;
    internal string Currency { get; private set; } = null!;
    internal string? OwnerId { get; private set; }
    internal string? RecipientEmail { get; private set; }
    internal string LineItemsJson { get; private set; } = null!;
    internal string? AdjustmentsJson { get; private set; }
    internal decimal SubtotalAmount { get; private set; }
    internal string SubtotalCurrency { get; private set; } = null!;
    internal decimal DiscountTotalAmount { get; private set; }
    internal string DiscountTotalCurrency { get; private set; } = null!;
    internal decimal TaxTotalAmount { get; private set; }
    internal string TaxTotalCurrency { get; private set; } = null!;
    internal decimal GrandTotalAmount { get; private set; }
    internal string GrandTotalCurrency { get; private set; } = null!;
    internal DateOnly? ValidUntil { get; private set; }
    internal DateTimeOffset? ReviewRequestedAt { get; private set; }
    internal DateTimeOffset? SentAt { get; private set; }
    internal DateTimeOffset? AcceptedAt { get; private set; }
    internal DateTimeOffset? RejectedAt { get; private set; }
    internal DateTimeOffset? ExpiredAt { get; private set; }
    internal string? Notes { get; private set; }
    internal DateTimeOffset? ArchivedAt { get; private set; }
    internal string? ArchiveReason { get; private set; }
    internal string ActionsJson { get; private set; } = null!;
    internal string? ApprovalStatus { get; private set; }
    internal bool? ApprovalRequired { get; private set; }
    internal string? ApprovalReasonsJson { get; private set; }
    internal DateTimeOffset? ApprovalRequestedAt { get; private set; }
    internal string? ApprovalRequestedBy { get; private set; }
    internal DateTimeOffset? ApprovedAt { get; private set; }
    internal string? ApprovedBy { get; private set; }
    internal string? ApprovalDecisionNote { get; private set; }
    internal string? ApprovalContentFingerprint { get; private set; }
    internal string? ApprovalPolicyVersion { get; private set; }
    internal string? PaymentAgreementJson { get; private set; }
    internal string? DeliveryHistoryJson { get; private set; }
    internal string? SenderName { get; private set; }
    internal string? SenderAddress { get; private set; }
    internal string? SenderEmail { get; private set; }
    internal string? SenderTaxId { get; private set; }
    internal long ResourceVersion { get; private set; }
    internal DateTimeOffset CreatedAt { get; private set; }
    internal DateTimeOffset UpdatedAt { get; private set; }
}
