using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Leads.Contracts;

public sealed record PrepareLeadCustomerConversionCommand(string LeadId, string RequestId, string CorrelationId, long ExpectedVersion);
public sealed record LeadCustomerConversionPreparation(bool IsSuccess, TrustedWorkspaceContext? TrustedWorkspace, string? OwnerId,
    long? Version, bool? DoNotCall, bool? DoNotEmail, string? ErrorCode, int? ErrorStatus, long? CurrentVersion = null);
public sealed record RecordLeadCustomerConversionCommand(TrustedWorkspaceContext TrustedWorkspace, string LeadId, string CustomerId,
    string WorkflowId, string ParticipantKey, string RequestId, string CorrelationId, string OriginalActorId, string RecoveryExecutorId);
public sealed record LeadCustomerConversionRecord(bool IsSuccess, bool Replayed, long? LeadVersion, string? CommandId,
    IReadOnlyList<string> EmittedEventIds, IReadOnlyList<string> AuditEvidenceIds, string? ErrorCode, int? ErrorStatus);

public interface ILeadCustomerConversionParticipant
{
    Task<LeadCustomerConversionPreparation> AuthorizeAsync(PrepareLeadCustomerConversionCommand command, CancellationToken cancellationToken);
    Task<LeadCustomerConversionPreparation> PrepareAsync(PrepareLeadCustomerConversionCommand command, CancellationToken cancellationToken);
    Task<LeadCustomerConversionRecord> RecordAsync(RecordLeadCustomerConversionCommand command, CancellationToken cancellationToken);
}
