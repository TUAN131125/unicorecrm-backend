using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.CommercialEvidence.CommercialEvidence.Contracts;

public enum PurchaseEvidenceBuyerRefType
{
    Contact,
    OrganizationAccount
}

public sealed record PurchaseEvidenceBuyerRef(PurchaseEvidenceBuyerRefType Type, string Id);

public sealed record AppendOrderCompletedPurchaseEvidenceIntent(
    TrustedWorkspaceContext TrustedWorkspace,
    string OrderId,
    PurchaseEvidenceBuyerRef BuyerRef,
    DateTimeOffset OccurredAt,
    string CorrelationId);

public enum PurchaseEvidenceAppendOutcome
{
    Appended,
    Replayed,
    Conflict
}

public sealed record AppendPurchaseEvidenceResult(
    PurchaseEvidenceAppendOutcome Outcome,
    string EvidenceId);

public interface IOrderCompletedPurchaseEvidenceAppender
{
    Task<AppendPurchaseEvidenceResult> AppendAsync(
        AppendOrderCompletedPurchaseEvidenceIntent intent,
        CancellationToken cancellationToken);
}

public sealed record EffectivePurchaseEvidenceSnapshot(
    string WorkspaceId,
    string EvidenceId,
    string EvidenceType,
    PurchaseEvidenceBuyerRef BuyerRef,
    DateTimeOffset OccurredAt,
    string PolicyVersion);

public interface IEffectivePurchaseEvidenceReader
{
    Task<EffectivePurchaseEvidenceSnapshot?> GetByIdAsync(
        TrustedWorkspaceContext trustedWorkspace,
        string evidenceId,
        CancellationToken cancellationToken);
}
