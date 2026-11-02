using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Workflows.Atomic.Application.Persistence;
using UnicoreCRM.Workflows.Atomic.Domain;

namespace UnicoreCRM.Workflows.Atomic.Infrastructure.Persistence;

internal sealed class EfAtomicWorkflowAnchorStore(WorkflowsDbContext db) : IAtomicWorkflowAnchorStore
{
    private const int HandoverRecoveryCandidateLimit = 100;

    public async Task<IAsyncDisposable> LockQualificationAsync(string workspaceId, string leadId, CancellationToken ct)
    {
        // A dedicated session lock serializes coordinators without holding owner transactions open.
        var settings = new SqlConnectionStringBuilder(db.Database.GetConnectionString()) { Pooling = false };
        var connection = new SqlConnection(settings.ConnectionString);
        try
        {
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = "DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=@key,@LockMode='Exclusive',@LockOwner='Session',@LockTimeout=15000; IF @r<0 THROW 51001,'Qualification lock unavailable',1;";
            command.Parameters.AddWithValue("@key", "lead-opportunity:" + workspaceId.ToLowerInvariant() + ":" + leadId.ToLowerInvariant());
            await command.ExecuteNonQueryAsync(ct);
            return new QualificationLock(connection);
        }
        catch { await connection.DisposeAsync(); throw; }
    }

    public Task<bool> HasOtherOpportunityIntentAsync(string workspaceId, string leadId, string scopeKey, CancellationToken ct) =>
        db.LeadQualificationAnchors.AsNoTracking().AnyAsync(x => x.WorkspaceId == workspaceId
            && x.LeadId == leadId && x.Workflow == "qualifyLeadForOpportunity" && x.ScopeKey != scopeKey, ct);

    private sealed class QualificationLock(SqlConnection connection) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }

    public Task<LeadQualificationAnchor?> FindQualificationAsync(string scopeKey, CancellationToken ct) =>
        db.LeadQualificationAnchors.SingleOrDefaultAsync(x => x.ScopeKey == scopeKey, ct);

    public Task<LeadQualificationAnchor> GetQualificationAsync(string scopeKey, CancellationToken ct) =>
        db.LeadQualificationAnchors.SingleAsync(x => x.ScopeKey == scopeKey, ct);

    public async Task CreateQualificationAsync(LeadQualificationAnchor anchor, CancellationToken ct)
    {
        db.LeadQualificationAnchors.Add(anchor);
        await db.SaveChangesAsync(ct);
    }

    public Task PersistQualificationAsync(LeadQualificationAnchor anchor, CancellationToken ct) => PersistTrackedAsync(anchor, ct);

    public Task<LeadHandoverAnchor?> FindHandoverReceiptAsync(string scopeKey, CancellationToken ct) =>
        db.LeadHandoverAnchors.AsNoTracking().SingleOrDefaultAsync(x => x.ScopeKey == scopeKey, ct);

    public Task<LeadHandoverAnchor> GetHandoverReceiptAsync(string commandId, string leadId, CancellationToken ct) =>
        db.LeadHandoverAnchors.AsNoTracking().SingleAsync(x => x.HandoverId == commandId && x.LeadId == leadId, ct);

    public Task<LeadHandoverAnchor?> FindHandoverForUpdateAsync(string scopeKey, CancellationToken ct) =>
        db.LeadHandoverAnchors.SingleOrDefaultAsync(x => x.ScopeKey == scopeKey, ct);

    public Task<LeadHandoverAnchor> GetHandoverForUpdateAsync(string scopeKey, CancellationToken ct) =>
        db.LeadHandoverAnchors.SingleAsync(x => x.ScopeKey == scopeKey, ct);

    public async Task<IReadOnlyList<WorkflowRecoveryCandidate>> FindDueHandoversAsync(DateTimeOffset now, CancellationToken ct) =>
        await db.LeadHandoverAnchors.AsNoTracking()
            .Where(x => x.Stage != LeadHandoverStage.Completed && x.Stage != LeadHandoverStage.ManualReview
                && (x.NextRetryAt == null || x.NextRetryAt <= now)
                && (x.ExecutionAttemptId == null || x.ExecutionLeaseExpiresAt == null || x.ExecutionLeaseExpiresAt <= now))
            .OrderBy(x => x.UpdatedAt).ThenBy(x => x.ScopeKey)
            .Select(x => new WorkflowRecoveryCandidate(x.ScopeKey, x.WorkspaceId, x.CorrelationId))
            .Take(HandoverRecoveryCandidateLimit).ToArrayAsync(ct);

    public async Task CreateHandoverAsync(LeadHandoverAnchor anchor, CancellationToken ct)
    {
        db.LeadHandoverAnchors.Add(anchor);
        await db.SaveChangesAsync(ct);
    }

    public Task PersistHandoverAsync(LeadHandoverAnchor anchor, CancellationToken ct) => PersistTrackedAsync(anchor, ct);

    public async Task PersistHandoverCompletionAsync(LeadHandoverAnchor anchor, WorkflowIntegrationOutboxMessage message, CancellationToken ct)
    {
        RequireTracked(anchor);
        db.IntegrationOutboxMessages.Add(message);
        await db.SaveChangesAsync(ct);
    }

    public Task<LeadCustomerConversionAnchor?> FindConversionReceiptAsync(string scopeKey, CancellationToken ct) =>
        db.LeadCustomerConversionAnchors.AsNoTracking().SingleOrDefaultAsync(x => x.ScopeKey == scopeKey, ct);

    public Task<LeadCustomerConversionAnchor?> FindConversionWinnerAsync(string workspaceId, string leadId, CancellationToken ct) =>
        db.LeadCustomerConversionAnchors.AsNoTracking().SingleOrDefaultAsync(
            x => x.WorkspaceId == workspaceId && x.LeadId == leadId && x.ConversionType == "LEAD_TO_CUSTOMER", ct);

    public Task<LeadCustomerConversionAnchor?> FindConversionForUpdateAsync(string scopeKey, CancellationToken ct) =>
        db.LeadCustomerConversionAnchors.SingleOrDefaultAsync(x => x.ScopeKey == scopeKey, ct);

    public async Task<IReadOnlyList<WorkflowRecoveryCandidate>> FindDueConversionsAsync(DateTimeOffset now, CancellationToken ct) =>
        await db.LeadCustomerConversionAnchors.AsNoTracking()
            .Where(x => x.Stage != LeadCustomerConversionStage.Completed && x.Stage != LeadCustomerConversionStage.ManualReview
                && (x.NextRetryAt == null || x.NextRetryAt <= now))
            .OrderBy(x => x.UpdatedAt)
            .Select(x => new WorkflowRecoveryCandidate(x.ScopeKey, x.WorkspaceId, x.CorrelationId)).Take(10).ToArrayAsync(ct);

    public async Task CreateConversionAsync(LeadCustomerConversionAnchor anchor, CancellationToken ct)
    {
        db.LeadCustomerConversionAnchors.Add(anchor);
        await db.SaveChangesAsync(ct);
    }

    public Task PersistConversionAsync(LeadCustomerConversionAnchor anchor, CancellationToken ct) => PersistTrackedAsync(anchor, ct);

    public async Task PersistConversionCompletionAsync(LeadCustomerConversionAnchor anchor, WorkflowIntegrationOutboxMessage message, CancellationToken ct)
    {
        RequireTracked(anchor);
        db.IntegrationOutboxMessages.Add(message);
        await db.SaveChangesAsync(ct);
    }

    public void DiscardPendingAnchorChanges() => db.ChangeTracker.Clear();
    public bool IsUpdateFailure(Exception exception) => exception is DbUpdateException;
    public bool IsConcurrencyConflict(Exception exception) => exception is DbUpdateConcurrencyException;
    public bool IsUniqueInsertRace(Exception exception) =>
        exception is DbUpdateException { InnerException: SqlException { Number: 2601 or 2627 } };

    public bool IsProviderContention(Exception exception) => exception switch
    {
        DbUpdateConcurrencyException => true,
        SqlException sql => sql.Number is 1205 or 1222,
        _ => exception.InnerException is not null && IsProviderContention(exception.InnerException)
    };

    private async Task PersistTrackedAsync(object anchor, CancellationToken ct)
    {
        RequireTracked(anchor);
        await db.SaveChangesAsync(ct);
    }

    private void RequireTracked(object anchor)
    {
        if (db.Entry(anchor).State == EntityState.Detached)
            throw new InvalidOperationException("Workflow progress requires the anchor loaded by this scoped store.");
    }
}
