using System.Globalization;
using System.Text.Json;
using UnicoreCRM.Sales.Quotes.Contracts;
using UnicoreCRM.Sales.Quotes.Domain;

namespace UnicoreCRM.Sales.Quotes.Application.Common;

internal static class QuoteCustomerDecisionRecords
{
    internal static QuoteCustomerDecisionRecord Create(Quote quote, string decisionId, string actor, string type,
        string source, string description, string? reportedAt, string? reason, string? note,
        QuoteCustomerDecisionPolicy policy, string requestFingerprint, string commandId, string auditId, string eventId, DateTimeOffset now)
    {
        var fingerprint = QuoteApprovalAssessment.ContentFingerprint(quote);
        var model = new QuoteCustomerDecisionReadModel(decisionId, quote.WorkspaceId, quote.QuoteId, quote.QuoteRevision,
            quote.ResourceVersion, new(quote.BuyerType, quote.BuyerId), type,
            type == "REJECTION" ? "USER_REPORTED_REJECTION" : "USER_REPORTED_ACCEPTANCE", "UNVERIFIED", source,
            description, now.UtcDateTime.ToString("O", CultureInfo.InvariantCulture), actor, fingerprint, policy.Version)
        { Reason = reason, Note = note, ReportedCustomerDecisionAt = reportedAt, ApprovalPolicyVersion = quote.ApprovalPolicyVersion };
        return new(quote.WorkspaceId, decisionId, quote.QuoteId, quote.QuoteRevision, quote.ResourceVersion, actor, type,
            fingerprint, policy.Version, requestFingerprint, JsonSerializer.Serialize(model, QuotePricing.Json), commandId, auditId, eventId, now);
    }
}
