namespace UnicoreCRM.Sales.Quotes.Domain;

internal sealed class QuoteSalesAcceptance
{
    private QuoteSalesAcceptance() { }
    internal QuoteSalesAcceptance(string id, string workspace, string quote, string source, string actor, string channel,
        string evidence, string document, string fingerprint, string snapshot, string policy, bool operationalApproval,
        DateTimeOffset agreedAt, DateTimeOffset now, bool autoConfirmRequested=false)
    {
        AcceptanceId=id; WorkspaceId=workspace; QuoteId=quote; Source=source; RecordedBy=actor; Channel=channel;
        Evidence=evidence; DocumentId=document; CommercialFingerprint=fingerprint; SnapshotJson=snapshot;
        AutoConfirmRequested=autoConfirmRequested; PolicyVersion=policy; OperationalApprovalRequired=operationalApproval; CustomerAgreedAt=agreedAt; AcceptedAt=now;
    }
    public string AcceptanceId { get; private set; } = null!;
    public string WorkspaceId { get; private set; } = null!;
    public string QuoteId { get; private set; } = null!;
    public string Source { get; private set; } = null!;
    public string AssuranceLevel { get; private set; } = "ORDINARY_SALES_UNVERIFIED";
    public string RecordedBy { get; private set; } = null!;
    public string Channel { get; private set; } = null!;
    public string Evidence { get; private set; } = null!;
    public string DocumentId { get; private set; } = null!;
    public string CommercialFingerprint { get; private set; } = null!;
    public string SnapshotJson { get; private set; } = null!;
    public string PolicyVersion { get; private set; } = null!;
    public bool AutoConfirmRequested { get; private set; }
    public bool OperationalApprovalRequired { get; private set; }
    public DateTimeOffset CustomerAgreedAt { get; private set; }
    public DateTimeOffset AcceptedAt { get; private set; }
    public string ConversionStatus { get; private set; } = "PENDING";
    public string? OrderId { get; private set; }
    public string? LastErrorCode { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset? NextAttemptAt { get; private set; }
    internal void Complete(string orderId) { OrderId=orderId; ConversionStatus="COMPLETED"; LastErrorCode=null; }
    internal void Retry(string code, DateTimeOffset now)
    { if(OrderId is not null)return; LastErrorCode=code; Attempts++; NextAttemptAt=now.AddSeconds(Math.Min(300,5*Attempts)); }
}

internal sealed class QuoteAcceptanceInvitation
{
    private QuoteAcceptanceInvitation() { }
    internal QuoteAcceptanceInvitation(string id,string workspace,string quote,string document,long version,string fingerprint,
        string hash,string protectedPayload,string actor,string recipient,DateTimeOffset expires,DateTimeOffset now)
    { InvitationId=id; WorkspaceId=workspace; QuoteId=quote; DocumentId=document; QuoteVersion=version;
      CommercialFingerprint=fingerprint; TokenHash=hash; ProtectedDeliveryPayload=protectedPayload; CreatedBy=actor;
      RecipientEmail=recipient; ExpiresAt=expires; CreatedAt=now; }
    public string InvitationId { get; private set; }=null!;
    public string WorkspaceId { get; private set; }=null!;
    public string QuoteId { get; private set; }=null!;
    public string DocumentId { get; private set; }=null!;
    public long QuoteVersion { get; private set; }
    public string CommercialFingerprint { get; private set; }=null!;
    public string TokenHash { get; private set; }=null!;
    public string ProtectedDeliveryPayload { get; private set; }=null!;
    public string CreatedBy { get; private set; }=null!;
    public string RecipientEmail { get; private set; }=null!;
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ConsentedAt { get; private set; }
    public string DeliveryStatus { get; private set; }="NOT_SENT";
    public string? DeliveryErrorCode { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }
    internal void Consent(DateTimeOffset now) { ConsentedAt=now; }
    internal void BindSentVersion(long version) { QuoteVersion=version; }
    internal void Delivery(string status,string? code,DateTimeOffset now)
    { DeliveryStatus=status; DeliveryErrorCode=code; if(status=="SENT")DeliveredAt=now; }
}
