namespace UnicoreCRM.Sales.Quotes.Application.Common;

internal sealed record QuoteCustomerDecisionPolicy(string Version, bool AllowReportedAcceptance, bool AllowRejection);
internal interface IQuoteCustomerDecisionPolicyReader
{
    QuoteCustomerDecisionPolicy? Read(string workspaceId);
}
