using System.Text.Json.Serialization;

namespace UnicoreCRM.Crm.Deals.Contracts;

public sealed record DealPipelineStageDocument(string Id, string Code, string LabelVi, string LabelEn,
    int Order, string Category, int DefaultProbability, string Color, bool IsActive, bool IsSystem,
    string ForecastFloor, bool RequiresPositiveAmount);
public sealed record DealPipelineDocument(string Id, string WorkspaceId, string NameVi, string NameEn,
    string Description, bool IsDefault, bool IsActive, long ResourceVersion, string CreatedAt,
    string UpdatedAt, string UpdatedBy, IReadOnlyList<DealPipelineStageDocument> Stages);
public sealed record DealPipelineListResponse(IReadOnlyList<DealPipelineDocument> Items);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DealPipelineStageInput(string? Id, string? Code, string? LabelVi, string? LabelEn,
    int Order, string? Category, int DefaultProbability, string? Color, bool IsActive);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SaveDealPipelineRequest(string? NameVi, string? NameEn, string? Description,
    bool IsActive, bool IsDefault, IReadOnlyList<DealPipelineStageInput>? Stages);
public sealed record DealPipelineMutationResponse(string Outcome, DealPipelineDocument Pipeline,
    IReadOnlyList<string> AuditEvidenceIds);

/// <summary>Deals-owned idempotent participant; only trusted workspace provisioning calls it.</summary>
public interface IDealPipelineProvisioning
{
    Task EnsureDefaultAsync(string workspaceId, string actorMemberId, CancellationToken cancellationToken);
}
