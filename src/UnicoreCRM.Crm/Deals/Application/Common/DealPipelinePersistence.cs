using UnicoreCRM.Crm.Deals.Domain;

namespace UnicoreCRM.Crm.Deals.Application.Common;

internal interface IDealPipelinePersistence
{
    Task LockRegistryAsync(string workspaceId, CancellationToken cancellationToken, bool exclusive = false);
    Task LockCommandAsync(string scopeKey, CancellationToken cancellationToken);
    Task<IReadOnlyList<DealPipeline>> LoadPipelinesAsync(string workspaceId, CancellationToken cancellationToken, bool tracking = false);
    void AddPipeline(DealPipeline pipeline);
    Task<int> CountStageUsageAsync(string workspaceId, string pipelineId, string? stageCode, CancellationToken cancellationToken);
}
