namespace UnicoreCRM.CommercialEvidence.CommercialEvidence.Application;

internal interface IPurchaseEvidenceIdGenerator
{
    string NewEvidenceId();
    string NewAuditId();
}

internal sealed class OpaquePurchaseEvidenceIdGenerator : IPurchaseEvidenceIdGenerator
{
    public string NewEvidenceId() => $"pe_{Guid.NewGuid():N}";
    public string NewAuditId() => $"ceaudit_{Guid.NewGuid():N}";
}

internal interface ICommercialEvidencePolicyVersionProvider
{
    string Current { get; }
}

internal sealed class CommercialEvidencePolicyVersionProvider : ICommercialEvidencePolicyVersionProvider
{
    public string Current => "commercial-evidence-policy-v1";
}
