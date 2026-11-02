using Microsoft.Extensions.Options;
namespace UnicoreCRM.Sales.Quotes.Application.Common;

internal sealed class QuoteDocumentOptions
{
    public Dictionary<string, QuoteDocumentWorkspacePolicy> Workspaces { get; set; } = new(StringComparer.Ordinal);
}
internal sealed class QuoteDocumentWorkspacePolicy
{
    public string Version { get; set; } = "";
    public bool? Enabled { get; set; }
}
internal interface IQuoteDocumentPolicyReader { string? EnabledVersion(string workspaceId); }
internal sealed class QuoteDocumentPolicyReader(IOptionsMonitor<QuoteDocumentOptions> options) : IQuoteDocumentPolicyReader
{
    public string? EnabledVersion(string workspaceId)
    {
        Dictionary<string, QuoteDocumentWorkspacePolicy>? policies;
        try { policies = options.CurrentValue.Workspaces; }
        catch (Exception error) when (error is OptionsValidationException or InvalidOperationException) { return null; }
        if (policies is null || !policies.TryGetValue(workspaceId, out var policy) || policy.Enabled != true
            || string.IsNullOrWhiteSpace(policy.Version) || policy.Version.Length > 80) return null;
        return policy.Version + ":" + QuoteApprovalAssessment.Hash("b5-approved-v1|" + policy.Version + "|SQL|IMMUTABLE|CURRENT_B1|ONE_BINDING|NO_LIFECYCLE_EFFECT");
    }
}
