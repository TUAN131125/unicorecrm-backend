using Microsoft.Extensions.Options;
using UnicoreCRM.Sales.Quotes.Application.Common;

namespace UnicoreCRM.Sales.Quotes.Infrastructure.Persistence;

internal sealed class QuoteCustomerDecisionOptions
{
    public Dictionary<string, QuoteCustomerDecisionConfiguration> Workspaces { get; set; } = new(StringComparer.Ordinal);
}
internal sealed class QuoteCustomerDecisionConfiguration
{
    public string Version { get; set; } = "";
    public bool? AllowReportedAcceptance { get; set; }
    public bool? AllowRejection { get; set; }
}
internal sealed class ConfiguredQuoteCustomerDecisionPolicyReader(IOptionsMonitor<QuoteCustomerDecisionOptions> options) : IQuoteCustomerDecisionPolicyReader
{
    public QuoteCustomerDecisionPolicy? Read(string workspaceId)
    {
        QuoteCustomerDecisionOptions configured;
        try { configured = options.CurrentValue; }
        catch (Exception error) when (error is OptionsValidationException or InvalidOperationException) { return null; }
        if (!configured.Workspaces.TryGetValue(workspaceId, out var policy) || string.IsNullOrWhiteSpace(policy.Version)
            || policy.Version.Length > 80 || policy.AllowReportedAcceptance is null || policy.AllowRejection is null) return null;
        var canonical = $"customer-decision-v1|{policy.Version}|{policy.AllowReportedAcceptance}|{policy.AllowRejection}|SENT|UNVERIFIED|APPEND_ONLY";
        return new(policy.Version + ":" + QuoteApprovalAssessment.Hash(canonical), policy.AllowReportedAcceptance.Value, policy.AllowRejection.Value);
    }
}
