using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Customers.Contracts;

public sealed record ResolveLeadConversionCustomerCommand(TrustedWorkspaceContext TrustedWorkspace, string SubjectType,
    string SubjectId, string SourceLeadId, string WorkflowId, string ParticipantKey, string RequestId,
    string CorrelationId, string OriginalPrincipalId, string LeadOwnerId, string ExecutorPrincipalId);
public sealed record ResolveLeadConversionCustomerResult(bool IsSuccess, string? CustomerId, long? CustomerVersion,
    string? Resolution, IReadOnlyList<string> EmittedEventIds, IReadOnlyList<string> AuditEvidenceIds,
    string? ErrorCode = null, int? ErrorStatus = null);
public sealed record FinalizeLeadConversionCustomerCommand(TrustedWorkspaceContext TrustedWorkspace,string CustomerId,
    string SourceLeadId,string WorkflowId,string ParticipantKey,string RequestId,string CorrelationId,string OriginalPrincipalId,
    string ExecutorPrincipalId,string Resolution);
public interface ILeadCustomerConversionParticipant
{
    Task<ResolveLeadConversionCustomerResult> ResolveOrCreateAsync(ResolveLeadConversionCustomerCommand command,
        CancellationToken cancellationToken);
    Task<ResolveLeadConversionCustomerResult> FinalizeAsync(FinalizeLeadConversionCustomerCommand command,CancellationToken cancellationToken);
}
