using System.Text.Json.Serialization;

namespace UnicoreCRM.Workflows.Atomic.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConvertLeadToCustomerRequest(LeadCustomerConversionSubject? AccountSubject,
    LeadCustomerConversionStakeholder? Stakeholder = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LeadCustomerConversionSubject(string? Type, string? Mode, string? Id = null,
    LeadQualificationContactInput? Contact = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LeadCustomerConversionStakeholder(string? ContactId, string? Role);

public sealed record ConvertLeadToCustomerCommand(string LeadId, ConvertLeadToCustomerRequest Request,
    string RequestId, string CorrelationId, string IdempotencyKey, long ExpectedVersion);
public sealed record LeadCustomerConversionResponse(string CommandId, string CorrelationId, string AggregateId,
    string AggregateType, long Version, string OccurredAt, string Outcome, LeadCustomerConversionResult Result,
    IReadOnlyList<string> Warnings, IReadOnlyList<string> EmittedEventIds, IReadOnlyList<string> AuditEvidenceIds);
public sealed record LeadCustomerConversionResult(string ConversionId, string LeadId, string CustomerId,
    string CustomerResolution, QualificationRelationshipRef AccountSubject, long LeadVersion, long CustomerVersion)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ContactId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? OrganizationId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? StakeholderRelationshipId { get; init; }
}
public sealed record ConvertLeadToCustomerResult(bool IsSuccess, LeadCustomerConversionResponse? Response,
    string? ErrorCode = null, int? ErrorStatus = null, IReadOnlyDictionary<string, string[]>? FieldErrors = null,
    long? ExpectedVersion = null, long? CurrentVersion = null, string? IdempotencyKey = null);
public interface ILeadCustomerConversionWorkflow
{
    Task<ConvertLeadToCustomerResult> ExecuteAsync(ConvertLeadToCustomerCommand command, CancellationToken cancellationToken);
}
