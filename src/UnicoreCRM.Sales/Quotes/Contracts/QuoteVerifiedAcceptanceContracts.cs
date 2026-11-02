using System.Text.Json.Serialization;
namespace UnicoreCRM.Sales.Quotes.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EvaluateQuoteCustomerAcceptanceRequest(string Intent, string EvidenceId, string DocumentId,
    int QuoteRevision, string PdfSha256, string CommercialContentFingerprint);
public sealed record QuoteAcceptanceDocumentBinding(string DocumentId, string QuoteId, string RootQuoteId, int QuoteRevision,
    long SourceResourceVersion, long ResultResourceVersion, string CommercialContentFingerprint, string PdfSha256, QuoteBuyerReference BuyerRef);
public sealed record QuoteCustomerAcceptanceReadiness(string QuoteId, long ResourceVersion, string QuoteStatus,
    string Status, string VerificationStatus, bool LifecycleCommitted, IReadOnlyList<string> BlockerCodes)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public QuoteIdentityAssessment? IdentityAuthority { get; init; }
    public bool ProofCollectionAllowed { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? PolicyVersion { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? AuthorityFreshnessHours { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public QuoteAcceptanceDocumentBinding? Binding { get; init; }
}
