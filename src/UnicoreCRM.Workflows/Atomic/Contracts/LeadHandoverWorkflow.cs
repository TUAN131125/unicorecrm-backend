using System.Text.Json.Serialization;
using UnicoreCRM.Crm.Leads.Contracts;

namespace UnicoreCRM.Workflows.Atomic.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LeadHandoverRequest(string? NewOwnerId, string? Reason, string? OpenTaskPolicy);
public sealed record LeadHandoverCommand(string LeadId, LeadHandoverRequest Request, string RequestId,
    string CorrelationId, string IdempotencyKey, long ExpectedVersion);
public sealed record LeadHandoverResult(LeadDocument Lead, string OpenTaskPolicy, IReadOnlyList<string> ReassignedTaskIds,
    string HandoverTaskId, long HandoverTaskVersion, string HandoverTaskDueAt, int ResolvedHandoverAcceptanceSlaHours);
public sealed record LeadHandoverResponse(string CommandId, string CorrelationId, string AggregateId, string AggregateType,
    long Version, string OccurredAt, string Outcome, LeadHandoverResult Result, IReadOnlyList<string> Warnings,
    IReadOnlyList<string> EmittedEventIds, IReadOnlyList<string> AuditEvidenceIds);
public sealed record LeadHandoverOperationResult(bool IsSuccess, LeadHandoverResponse? Response,
    string? ErrorCode = null, int? ErrorStatus = null, IReadOnlyDictionary<string, string[]>? FieldErrors = null,
    long? ExpectedVersion = null, long? CurrentVersion = null, string? IdempotencyKey = null);
public interface ILeadHandoverWorkflow
{
    Task<LeadHandoverOperationResult> ExecuteAsync(LeadHandoverCommand command, CancellationToken cancellationToken);
}
