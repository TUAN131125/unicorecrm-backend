using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Leads.Contracts;

public sealed record LeadExecutionProvenance(
    string ActorType,
    string ActorId,
    string? DelegatedSubjectId,
    string? SourceReference);

public sealed record InboundLeadCreateCommand(
    TrustedWorkspaceContext TrustedWorkspace,
    CreateLeadRequest Request,
    LeadExecutionProvenance Provenance,
    string RequestId,
    string CorrelationId,
    string IdempotencyKey);

public sealed record InboundLeadCreateResult(
    bool IsSuccess,
    string? LeadId,
    string? Outcome,
    string? ErrorCode,
    int? ErrorStatus,
    IReadOnlyDictionary<string, string[]>? FieldErrors);

public interface IInboundLeadIngress
{
    Task<InboundLeadCreateResult> CreateAsync(
        InboundLeadCreateCommand command,
        CancellationToken cancellationToken);
}
