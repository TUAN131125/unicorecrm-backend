using Microsoft.Extensions.Options;
using UnicoreCRM.Sales.Quotes.Application.Common;
namespace UnicoreCRM.Sales.Quotes.Infrastructure.Persistence;

internal sealed class QuoteLifecycleOptions
{
    public Dictionary<string, QuoteLifecycleConfiguration> Workspaces { get; set; } = new(StringComparer.Ordinal);
}
internal sealed class QuoteLifecycleConfiguration
{
    public string Version { get; set; } = "";
    public bool? AllowRevision { get; set; }
    public bool? AllowExpiration { get; set; }
    public bool? AllowArchive { get; set; }
}
internal sealed class ConfiguredQuoteLifecyclePolicyReader(IOptionsMonitor<QuoteLifecycleOptions> options) : IQuoteLifecyclePolicyReader
{
    public QuoteLifecyclePolicy? Read(string workspaceId)
    {
        QuoteLifecycleOptions value;
        try { value = options.CurrentValue; }
        catch (Exception error) when (error is OptionsValidationException or InvalidOperationException) { return null; }
        if (value.Workspaces is null || !value.Workspaces.TryGetValue(workspaceId, out var p) || p is null
            || string.IsNullOrWhiteSpace(p.Version) || p.Version.Length > 80
            || p.AllowRevision is null || p.AllowExpiration is null || p.AllowArchive is null) return null;
        var canonical = $"b4-package-a-v1|{p.Version}|{p.AllowRevision}|{p.AllowExpiration}|{p.AllowArchive}|ONE_WORKING|NEW_NUMBER|SENT_REJECTED_EXPIRED_SOURCE|OPEN_DEAL|EXPLICIT_EXPIRY|ARCHIVE_DRAFT_REVIEW_REJECTED_EXPIRED|NO_RESTORE";
        return new(p.Version + ":" + QuoteApprovalAssessment.Hash(canonical), p.AllowRevision.Value, p.AllowExpiration.Value, p.AllowArchive.Value);
    }
}
