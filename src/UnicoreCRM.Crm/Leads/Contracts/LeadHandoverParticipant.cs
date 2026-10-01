using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Leads.Contracts;

public sealed record PrepareLeadHandoverCommand(string LeadId, string RequestId, string CorrelationId,
    long ExpectedVersion, string NewOwnerId, bool RequiresOwnerWrite = false);
public sealed record LeadHandoverPreparation(bool IsSuccess, TrustedWorkspaceContext? TrustedWorkspace,
    string? OwnerId, long? Version, string? ErrorCode = null, int? ErrorStatus = null);
public sealed record LeadHandoverParticipantCommand(TrustedWorkspaceContext TrustedWorkspace, string LeadId,
    string HandoverId, string PreviousOwnerId, string NewOwnerId, string Reason, string OpenTaskPolicy,
    long ExpectedLeadVersion, string ParticipantKey, string RequestId, string CorrelationId,
    string OriginalPrincipalId, string ExecutorPrincipalId);
public sealed record LeadHandoverParticipantResult(bool IsSuccess, LeadMutationResponse? Response,
    string? ErrorCode = null, int? ErrorStatus = null);
public interface ILeadHandoverParticipant
{
    Task<LeadDocument?> ProjectAsync(LeadDocument document, string requestId, string correlationId, CancellationToken cancellationToken);
    Task<LeadHandoverPreparation> AuthorizeAsync(PrepareLeadHandoverCommand command, CancellationToken cancellationToken);
    Task<LeadHandoverPreparation> PrepareAsync(PrepareLeadHandoverCommand command, CancellationToken cancellationToken);
    Task<LeadHandoverParticipantResult> ReserveAsync(LeadHandoverParticipantCommand command, CancellationToken cancellationToken);
    Task<LeadHandoverParticipantResult> ResolveReservationOrFenceAsync(LeadHandoverParticipantCommand command, CancellationToken cancellationToken);
    Task<LeadHandoverParticipantResult> CompleteAsync(LeadHandoverParticipantCommand command, CancellationToken cancellationToken);
    Task<LeadHandoverParticipantResult> ReleaseAsync(LeadHandoverParticipantCommand command, CancellationToken cancellationToken);
}
