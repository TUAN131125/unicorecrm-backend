using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Platform.Workspace.Infrastructure;

internal sealed class QuoteApprovalPolicyOptions
{
    public Dictionary<string, QuoteApprovalPolicyConfiguration> Workspaces { get; set; } = new(StringComparer.Ordinal);
}

internal sealed class QuoteApprovalPolicyConfiguration
{
    public string Version { get; set; } = "";
    public decimal? LineDiscountPercent { get; set; }
    public decimal? TotalDiscountPercent { get; set; }
    public int? PostpaidDays { get; set; }
    public bool? AdvancedPaymentRequiresApproval { get; set; }
    public bool? AlwaysRequireApproval { get; set; }
    public Dictionary<string, decimal> GrandTotalLimits { get; set; } = new(StringComparer.Ordinal);
}

internal sealed class ConfiguredQuoteApprovalPolicyReader(IOptionsMonitor<QuoteApprovalPolicyOptions> options)
    : IQuoteApprovalPolicyReader
{
    public Task<QuoteApprovalPolicy?> ReadAsync(string workspaceId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        QuoteApprovalPolicyOptions configured;
        try { configured = options.CurrentValue; }
        catch (Exception error) when (error is OptionsValidationException or InvalidOperationException)
        { return Task.FromResult<QuoteApprovalPolicy?>(null); }
        if (!configured.Workspaces.TryGetValue(workspaceId, out var value)
            || string.IsNullOrWhiteSpace(value.Version) || value.Version.Length > 80
            || value.LineDiscountPercent is null or < 0 or > 100
            || value.TotalDiscountPercent is null or < 0 or > 100 || value.PostpaidDays is null or < 0
            || value.AdvancedPaymentRequiresApproval is null || value.AlwaysRequireApproval is null
            || value.GrandTotalLimits.Any(x => x.Key.Length != 3 || x.Key.Any(c => c is < 'A' or > 'Z') || x.Value < 0))
            return Task.FromResult<QuoteApprovalPolicy?>(null);
        var limits = value.GrandTotalLimits.OrderBy(x => x.Key, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        // Bind the effective version to content even if an operator accidentally reuses a label.
        var canonical = string.Join("|", value.Version, Number(value.LineDiscountPercent.Value),
            Number(value.TotalDiscountPercent.Value), value.PostpaidDays.Value.ToString(CultureInfo.InvariantCulture),
            value.AdvancedPaymentRequiresApproval.Value, value.AlwaysRequireApproval.Value,
            string.Join(";", limits.Select(x => x.Key + "=" + Number(x.Value))));
        var version = value.Version + ":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        return Task.FromResult<QuoteApprovalPolicy?>(new(version, value.LineDiscountPercent.Value,
            value.TotalDiscountPercent.Value, value.PostpaidDays.Value, value.AdvancedPaymentRequiresApproval.Value,
            value.AlwaysRequireApproval.Value, limits));
    }
    private static string Number(decimal value) => value.ToString("G29", CultureInfo.InvariantCulture);
}
