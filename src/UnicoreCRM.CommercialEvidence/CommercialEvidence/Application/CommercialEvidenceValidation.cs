using System.Text.RegularExpressions;
using UnicoreCRM.CommercialEvidence.CommercialEvidence.Contracts;
using UnicoreCRM.CommercialEvidence.CommercialEvidence.Domain;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.CommercialEvidence.CommercialEvidence.Application;

internal static partial class CommercialEvidenceValidation
{
    internal static void Validate(AppendOrderCompletedPurchaseEvidenceIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ValidateTrustedWorkspace(intent.TrustedWorkspace);
        RequireEntityId(intent.OrderId, nameof(intent.OrderId));
        ArgumentNullException.ThrowIfNull(intent.BuyerRef);
        if (!Enum.IsDefined(intent.BuyerRef.Type))
            throw new ArgumentOutOfRangeException(nameof(intent.BuyerRef), "BuyerRef type is not admitted.");
        RequireEntityId(intent.BuyerRef.Id, nameof(intent.BuyerRef));
        RequireText(intent.CorrelationId, nameof(intent.CorrelationId));
    }

    internal static void ValidateTrustedWorkspace(TrustedWorkspaceContext trustedWorkspace)
    {
        ArgumentNullException.ThrowIfNull(trustedWorkspace);
        RequireEntityId(trustedWorkspace.WorkspaceId, nameof(trustedWorkspace));
        RequireEntityId(trustedWorkspace.AccountId, nameof(trustedWorkspace));
        RequireEntityId(trustedWorkspace.MemberId, nameof(trustedWorkspace));
        RequireEntityId(trustedWorkspace.MembershipId, nameof(trustedWorkspace));
    }

    internal static void ValidateEvidenceId(string evidenceId) => RequireEntityId(evidenceId, nameof(evidenceId));

    internal static string PersistedBuyerRefType(PurchaseEvidenceBuyerRefType type) => type switch
    {
        PurchaseEvidenceBuyerRefType.Contact => CommercialEvidenceVocabulary.Contact,
        PurchaseEvidenceBuyerRefType.OrganizationAccount => CommercialEvidenceVocabulary.OrganizationAccount,
        _ => throw new ArgumentOutOfRangeException(nameof(type), "BuyerRef type is not admitted.")
    };

    internal static PurchaseEvidenceBuyerRefType ContractBuyerRefType(string type) => type switch
    {
        CommercialEvidenceVocabulary.Contact => PurchaseEvidenceBuyerRefType.Contact,
        CommercialEvidenceVocabulary.OrganizationAccount => PurchaseEvidenceBuyerRefType.OrganizationAccount,
        _ => throw new InvalidOperationException("Persisted BuyerRef type is invalid.")
    };

    private static void RequireEntityId(string? value, string parameterName)
    {
        if (value is not { Length: >= 1 and <= 128 } || !EntityIdPattern().IsMatch(value))
            throw new ArgumentException("Value must be a canonical EntityId.", parameterName);
    }

    private static void RequireText(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
            throw new ArgumentException("Value must contain between 1 and 128 non-whitespace characters.", parameterName);
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex EntityIdPattern();
}
