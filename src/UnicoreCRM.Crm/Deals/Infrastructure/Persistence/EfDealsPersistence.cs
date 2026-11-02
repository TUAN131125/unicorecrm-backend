using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using UnicoreCRM.Crm.Deals.Application.Common;
using UnicoreCRM.Crm.Deals.Domain;

namespace UnicoreCRM.Crm.Deals.Infrastructure.Persistence;

internal sealed class EfDealsPersistence(DealsDbContext dbContext) : IDealsPersistence, IDealPipelinePersistence
{
    public async Task LockCommandAsync(string scopeKey, CancellationToken cancellationToken)
    {
        var resource = "deal-command:" + scopeKey;
        await RunAsync(() => dbContext.Database.ExecuteSqlInterpolatedAsync($"DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=15000; IF @result < 0 THROW 51001, 'Deal command lock unavailable', 1;", cancellationToken));
    }
    public async Task LockRegistryAsync(string workspaceId, CancellationToken cancellationToken, bool exclusive = false)
    {
        var resource = "deal-pipeline:" + workspaceId;
        var mode = exclusive ? "Exclusive" : "Shared";
        await RunAsync(() => dbContext.Database.ExecuteSqlInterpolatedAsync($"DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource={resource}, @LockMode={mode}, @LockOwner='Transaction', @LockTimeout=15000; IF @result < 0 THROW 51001, 'Pipeline registry lock unavailable', 1;", cancellationToken));
    }
    public async Task<IReadOnlyList<DealPipeline>> LoadPipelinesAsync(string workspaceId, CancellationToken cancellationToken, bool tracking = false)
    {
        var query = dbContext.Pipelines.Where(item => item.WorkspaceId == workspaceId).Include(item => item.Stages);
        return await RunAsync(() => (tracking ? query : query.AsNoTracking()).ToArrayAsync(cancellationToken));
    }
    public void AddPipeline(DealPipeline pipeline) => dbContext.Pipelines.Add(pipeline);
    public Task<int> CountStageUsageAsync(string workspaceId, string pipelineId, string? stageCode, CancellationToken cancellationToken) =>
        RunAsync(() => dbContext.Deals.CountAsync(item => item.WorkspaceId == workspaceId && item.PipelineId == pipelineId && (stageCode == null || item.StageCode == stageCode), cancellationToken));
    public async Task<IDealsTransaction> BeginSerializableAsync(CancellationToken cancellationToken) =>
        new DealsTransaction(await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken));

    public Task<Deal?> LoadDealAsync(string workspaceId, string dealId, CancellationToken cancellationToken) =>
        RunAsync(() => dbContext.Deals.SingleOrDefaultAsync(item => item.WorkspaceId == workspaceId && item.DealId == dealId, cancellationToken));

    public Task<Deal?> ReadDealAsync(string workspaceId, string dealId, CancellationToken cancellationToken) =>
        RunAsync(() => dbContext.Deals.AsNoTracking().SingleOrDefaultAsync(item => item.WorkspaceId == workspaceId && item.DealId == dealId, cancellationToken));

    public Task<Deal?> ReadQualificationDealBySourceLeadAsync(
        string workspaceId,
        string sourceLeadId,
        CancellationToken cancellationToken) =>
        RunAsync(() => dbContext.Deals.SingleOrDefaultAsync(
            item => item.WorkspaceId == workspaceId && item.QualificationSourceLeadId == sourceLeadId,
            cancellationToken));

    public async Task<IReadOnlyList<Deal>> ReadDealsAsync(
        string workspaceId,
        string? scopeOwnerMemberId,
        CancellationToken cancellationToken, string? pipelineId = null, string? stageCode = null)
    {
        IQueryable<Deal> query = dbContext.Deals.AsNoTracking().Where(item => item.WorkspaceId == workspaceId);
        // The AccessControl record scope is part of the SQL query, so a hidden deal is never
        // materialised and cannot reach the count, the ordering or the page.
        if (scopeOwnerMemberId is not null)
            query = query.Where(item => item.ScopeOwnerId == scopeOwnerMemberId);
        if (pipelineId is not null) query = query.Where(item => item.PipelineId == pipelineId);
        if (stageCode is not null) query = query.Where(item => item.StageCode == stageCode);
        return await RunAsync(() => query.ToArrayAsync(cancellationToken));
    }

    public async Task<IReadOnlyList<Deal>> LoadDealsAsync(
        string workspaceId,
        IReadOnlyCollection<string> dealIds,
        CancellationToken cancellationToken) =>
        await RunAsync(() => dbContext.Deals
            .Where(item => item.WorkspaceId == workspaceId && dealIds.Contains(item.DealId))
            .ToArrayAsync(cancellationToken));

    public Task<DealIdempotencyRecord?> FindIdempotencyAsync(string scopeKey, CancellationToken cancellationToken) =>
        RunAsync(() => dbContext.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(item => item.ScopeKey == scopeKey, cancellationToken));

    public void AddDeal(Deal deal) => dbContext.Deals.Add(deal);
    public void AddIdempotency(DealIdempotencyRecord record) => dbContext.IdempotencyRecords.Add(record);
    public void AddAudit(DealAuditRecord audit) => dbContext.AuditRecords.Add(audit);
    public void AddOutbox(DealOutboxMessage message) => dbContext.OutboxMessages.Add(message);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException sql && IsBusy(sql))
        {
            throw new DealsPersistenceBusyException(exception);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new DealsPersistenceConcurrencyException(exception);
        }
    }

    private static bool IsBusy(SqlException error) => error.Number is 1205 or 1222 or 51001;
    private static async Task<T> RunAsync<T>(Func<Task<T>> action)
    {
        try { return await action(); }
        catch (SqlException error) when (IsBusy(error)) { throw new DealsPersistenceBusyException(error); }
    }

    private sealed class DealsTransaction(IDbContextTransaction transaction) : IDealsTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) => RunAsync(async () => { await transaction.CommitAsync(cancellationToken); return true; });
        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
