using System.Text.Json.Serialization;

namespace UnicoreCRM.Sales.Quotes.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RejectQuoteRequest(string Reason, string? Note = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RecordQuoteCustomerDecisionRequest(string DecisionId, string EvidenceType, string SourceChannel,
    string SourceDescription, string? ReportedCustomerDecisionAt = null, string? Note = null);

public sealed record QuoteCustomerDecisionReadModel(string Id, string WorkspaceId, string QuoteId, int QuoteRevision,
    long ResourceVersion, QuoteBuyerReference BuyerRef, string DecisionType, string EvidenceType, string VerificationStatus,
    string EvidenceSource, string SourceDescription, string RecordedAt, string RecordedBy, string CommercialContentFingerprint,
    string PolicyVersion)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ReportedCustomerDecisionAt { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Reason { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Note { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ApprovalPolicyVersion { get; init; }
}
