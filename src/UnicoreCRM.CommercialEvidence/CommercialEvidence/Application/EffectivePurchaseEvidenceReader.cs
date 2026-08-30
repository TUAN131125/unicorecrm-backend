using UnicoreCRM.CommercialEvidence.CommercialEvidence.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.CommercialEvidence.CommercialEvidence.Application;

internal sealed class EffectivePurchaseEvidenceReader(ICommercialEvidencePersistence persistence)
    : IEffectivePurchaseEvidenceReader
{
    public async Task<EffectivePurchaseEvidenceSnapshot?> GetByIdAsync(
        TrustedWorkspaceContext trustedWorkspace,
        string evidenceId,
        CancellationToken cancellationToken)
    {
        CommercialEvidenceValidation.ValidateTrustedWorkspace(trustedWorkspace);
        CommercialEvidenceValidation.ValidateEvidenceId(evidenceId);
        var evidence = await persistence.ReadOriginalByIdAsync(
            trustedWorkspace.WorkspaceId,
            evidenceId,
            cancellationToken);
        return evidence is null
            ? null
            : new(
                evidence.WorkspaceId,
                evidence.EvidenceId,
                evidence.EvidenceType,
                new(CommercialEvidenceValidation.ContractBuyerRefType(evidence.BuyerRefType), evidence.BuyerRefId),
                evidence.OccurredAt,
                evidence.PolicyVersion);
    }
}
