using UnicoreCRM.Sales.Quotes.Domain;
using UnicoreCRM.Sales.Quotes.Contracts;
using System.Text.Json;
using Microsoft.Extensions.Options;
namespace UnicoreCRM.Sales.Quotes.Application.Common;

internal sealed class QuoteVerifiedAcceptanceOptions
{
    public Dictionary<string, QuoteVerifiedAcceptanceWorkspacePolicy> Workspaces { get; set; } = new(StringComparer.Ordinal);
}
internal sealed class QuoteVerifiedAcceptanceWorkspacePolicy
{
    public string Version { get; set; } = "";
    public bool Enabled { get; set; }
    public int AuthorityFreshnessHours { get; set; } = 24;
    public Dictionary<string, decimal> CurrencyLimits { get; set; } = new(StringComparer.Ordinal) { ["VND"] = 100000000m };
}
internal sealed record QuoteVerifiedAcceptancePolicy(string Version, int AuthorityFreshnessHours, IReadOnlyDictionary<string, decimal> CurrencyLimits);
internal interface IQuoteVerifiedAcceptancePolicyReader { QuoteVerifiedAcceptancePolicy? Read(string workspaceId); }
internal sealed class QuoteVerifiedAcceptancePolicyReader(IOptionsMonitor<QuoteVerifiedAcceptanceOptions> options) : IQuoteVerifiedAcceptancePolicyReader
{
    public QuoteVerifiedAcceptancePolicy? Read(string workspaceId)
    {
        QuoteVerifiedAcceptanceOptions value;
        try { value = options.CurrentValue; }
        catch (Exception e) when (e is InvalidOperationException or OptionsValidationException) { return null; }
        if (value.Workspaces is null || !value.Workspaces.TryGetValue(workspaceId, out var p) || p is null || !p.Enabled
            || string.IsNullOrWhiteSpace(p.Version) || p.Version.Length > 80 || p.Version.Any(char.IsControl)
            || p.AuthorityFreshnessHours is < 1 or > 24 || p.CurrencyLimits is null || p.CurrencyLimits.Count == 0
            || p.CurrencyLimits.Any(x => x.Key.Length != 3 || x.Key.Any(c => c is < 'A' or > 'Z') || x.Value <= 0)) return null;
        var limits = p.CurrencyLimits.OrderBy(x => x.Key, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        var hash = QuoteApprovalAssessment.Hash(JsonSerializer.Serialize(new { p.Version, p.AuthorityFreshnessHours, limits }, QuotePricing.Json));
        return new(p.Version + ":" + hash, p.AuthorityFreshnessHours, limits);
    }
}
internal static class QuoteVerifiedAcceptanceRules
{
    internal static bool HasCurrentManualDelivery(string? json, int revision, string fingerprint)
    {
        if (json is null) return false;
        try
        {
            var rows = JsonSerializer.Deserialize<QuoteDeliveryRecordReadModel[]>(json, QuotePricing.Json);
            return rows is not null && rows.Any(r => r is not null && r.QuoteRevision == revision
                && r.ContentFingerprint == fingerprint && r.EvidenceType == "USER_CONFIRMED_SENT");
        }
        catch (JsonException) { return false; }
    }

    internal static bool HasIndependentApproval(Quote quote, string? creator) => creator is not null
        && quote.ApprovalStatus == "APPROVED" && quote.ApprovalRequired == true && quote.ApprovedAt is not null
        && quote.ApprovedBy is not null && quote.ApprovalRequestedBy is not null
        && quote.ApprovedBy != creator && quote.ApprovedBy != quote.ApprovalRequestedBy;

    internal static bool WithinValueLimit(QuoteVerifiedAcceptancePolicy policy, string currency, decimal grandTotal) =>
        grandTotal >= 0 && policy.CurrencyLimits.TryGetValue(currency, out var limit) && grandTotal <= limit;

    // Pre-acceptance check only. Never apply this clock to historical committed acceptance.
    internal static bool AuthorityFreshBeforeAcceptance(QuoteVerifiedAcceptancePolicy policy, DateTimeOffset checkedAt,
        DateTimeOffset effectiveFrom, DateTimeOffset effectiveUntil, bool recordedRevoked, DateTimeOffset now) =>
        !recordedRevoked && checkedAt <= now && checkedAt >= now.AddHours(-policy.AuthorityFreshnessHours)
        && effectiveFrom <= now && now < effectiveUntil;
}
