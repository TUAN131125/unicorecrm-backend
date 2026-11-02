using System.Text.Json.Serialization;

namespace UnicoreCRM.Sales.Quotes.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StaffQuoteAcceptanceRequest(string DocumentId, string Channel, DateTimeOffset CustomerAgreedAt,
    string Evidence, bool ResponsibilityConfirmed, string? AttachmentEvidenceId = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QuoteInvitationRequest(string DocumentId, bool SendGmail = true);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CustomerQuoteConsentRequest(string Token, bool Agree, string TermsVersion);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QuoteInvitationPreviewRequest(string Token);

public sealed record QuoteSalesAcceptanceView(string AcceptanceId, string QuoteId, string Source, string AssuranceLevel,
    string AcceptedAt, string RecordedBy, string Channel, string Evidence, string DocumentId, string CommercialFingerprint,
    string ConversionStatus, string? OrderId, string? LastErrorCode, bool OperationalApprovalRequired, bool AutoConfirmRequested=false);
public sealed record QuoteInvitationView(string InvitationId, string QuoteId, string DeliveryStatus, string? DeliveryErrorCode,
    string ExpiresAt, string? AcceptanceUrl);
public sealed record CustomerQuotePreview(string QuoteNumber, string BuyerName, int QuoteRevision, int DocumentVersion,
    QuoteMoney GrandTotal, IReadOnlyList<QuoteLineReadModel> LineItems, PaymentAgreementSnapshotDocument? PaymentAgreement,
    string? ValidUntil, string TermsVersion, string Terms, string DeliveryStatus);

// Owner-approved durable handoff: consumers receive committed snapshots, never a foreign DbContext.
public sealed record AcceptedQuoteOrderSource(string AcceptanceId, string WorkspaceId, string QuoteId,
    string SnapshotJson, string CommercialFingerprint, string AcceptanceJson, DateTimeOffset AcceptedAt, bool AutoConfirmRequested=false);
public interface IAcceptedQuoteOrderQueue
{
    Task<IReadOnlyList<AcceptedQuoteOrderSource>> PendingAsync(int limit, CancellationToken ct);
    Task CompleteAsync(string workspaceId, string acceptanceId, string orderId, CancellationToken ct);
    Task RetryAsync(string workspaceId, string acceptanceId, string errorCode, CancellationToken ct);
}
