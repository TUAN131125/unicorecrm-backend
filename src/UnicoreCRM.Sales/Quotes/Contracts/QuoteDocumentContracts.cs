using System.Text.Json.Serialization;
using UnicoreCRM.Platform.Workspace.Contracts;
namespace UnicoreCRM.Sales.Quotes.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record IssueQuoteDocumentRequest(string? Reason = null);
public sealed record QuoteIssuedDocument(string DocumentId, string QuoteId, string RootQuoteId, int QuoteRevision,
    int DocumentVersion, long SourceResourceVersion, long ResultResourceVersion, string CommercialContentFingerprint,
    long SellerConfigurationVersion, string ApprovalPolicyVersion, string DocumentPolicyVersion, string DocumentTemplateVersion,
    string IssuedAt, string IssuedBy, string DocumentStatus, int FileSize, int PageCount, string MediaType, string PdfSha256, string FileName);
public sealed record QuoteDocumentIssueResult(QuoteReadModel Quote, QuoteIssuedDocument Document);
public sealed record QuoteDocumentIssueResponse(string CommandId, string CorrelationId, string AggregateId, string AggregateType,
    long Version, string OccurredAt, string Outcome, QuoteDocumentIssueResult Result, IReadOnlyList<string> EventIds, IReadOnlyList<string> AuditIds);
public sealed record QuoteDocumentList(IReadOnlyList<QuoteIssuedDocument> Items, QuotePageInfo PageInfo);
public sealed record QuoteDocumentSeller(bool Ready, IReadOnlyList<string> BlockerCodes,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] QuoteSellerSnapshot? Seller);
