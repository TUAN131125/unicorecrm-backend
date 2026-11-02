namespace UnicoreCRM.Platform.Workspace.Contracts;

/// <summary>Workspace-owned deployment policy. No client or implicit currency conversion.</summary>
public sealed record QuoteApprovalPolicy(string Version, decimal LineDiscountPercent,
    decimal TotalDiscountPercent, int PostpaidDays, bool AdvancedPaymentRequiresApproval,
    bool AlwaysRequireApproval, IReadOnlyDictionary<string, decimal> GrandTotalLimits);

public interface IQuoteApprovalPolicyReader
{
    Task<QuoteApprovalPolicy?> ReadAsync(string workspaceId, CancellationToken cancellationToken);
}
