namespace UnicoreCRM.Sales.Quotes.Domain;

/// <summary>Append-only employee reports. This type cannot represent verified customer consent.</summary>
internal sealed class QuoteCustomerDecisionRecord
{
    private QuoteCustomerDecisionRecord() { }
    internal QuoteCustomerDecisionRecord(string workspaceId, string decisionId, string quoteId, int quoteRevision,
        long contentVersion, string actorId, string decisionType, string contentFingerprint, string policyVersion,
        string requestFingerprint, string payloadJson, string commandId, string auditId, string eventId, DateTimeOffset now)
    {
        WorkspaceId = workspaceId; DecisionId = decisionId; QuoteId = quoteId; QuoteRevision = quoteRevision;
        ContentResourceVersion = contentVersion; ResultResourceVersion = contentVersion + 1;
        ActorId = actorId; DecisionType = decisionType; ContentFingerprint = contentFingerprint; PolicyVersion = policyVersion;
        RequestFingerprint = requestFingerprint; PayloadJson = payloadJson; CommandId = commandId; AuditId = auditId;
        EventId = eventId; RecordedAt = now;
    }
    public string WorkspaceId { get; private set; } = null!;
    public string DecisionId { get; private set; } = null!;
    public string QuoteId { get; private set; } = null!;
    public int QuoteRevision { get; private set; }
    public long ContentResourceVersion { get; private set; }
    public long ResultResourceVersion { get; private set; }
    public string ActorId { get; private set; } = null!;
    public string DecisionType { get; private set; } = null!;
    public string VerificationStatus { get; private set; } = "UNVERIFIED";
    public string ContentFingerprint { get; private set; } = null!;
    public string PolicyVersion { get; private set; } = null!;
    public string RequestFingerprint { get; private set; } = null!;
    public string PayloadJson { get; private set; } = null!;
    public string CommandId { get; private set; } = null!;
    public string AuditId { get; private set; } = null!;
    public string EventId { get; private set; } = null!;
    public DateTimeOffset RecordedAt { get; private set; }
}
