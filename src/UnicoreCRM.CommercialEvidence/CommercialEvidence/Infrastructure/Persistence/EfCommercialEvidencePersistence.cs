using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using UnicoreCRM.CommercialEvidence.CommercialEvidence.Application;
using UnicoreCRM.CommercialEvidence.CommercialEvidence.Domain;

namespace UnicoreCRM.CommercialEvidence.CommercialEvidence.Infrastructure.Persistence;

internal sealed class EfCommercialEvidencePersistence(CommercialEvidenceDbContext dbContext)
    : ICommercialEvidencePersistence
{
    public async Task<IReadOnlyList<PurchaseHealthSignalRow>> ReadPurchaseHealthSignalsAsync(
        string workspaceId,
        IReadOnlyCollection<Contracts.CustomerPurchaseHealthBuyerRef> buyerRefs,
        DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        var buyerIds = buyerRefs.Select(reference => reference.Id).Distinct().ToArray();
        var admitted = buyerRefs.Select(reference =>
            $"{CommercialEvidenceValidation.PersistedBuyerRefType(reference.Type)}\n{reference.Id}").ToHashSet(StringComparer.Ordinal);
        var rows = await dbContext.PurchaseEvidence.AsNoTracking()
            .Where(item => item.WorkspaceId == workspaceId && item.OccurredAt <= asOf && buyerIds.Contains(item.BuyerRefId))
            .Select(item => new PurchaseHealthSignalRow(item.BuyerRefType, item.BuyerRefId, item.OccurredAt))
            .ToListAsync(cancellationToken);
        return rows.Where(row => admitted.Contains($"{row.BuyerRefType}\n{row.BuyerRefId}")).ToArray();
    }

    public Task<PurchaseEvidence?> FindOriginalByOrderSourceAsync(
        string workspaceId,
        string orderId,
        CancellationToken cancellationToken) =>
        dbContext.PurchaseEvidence
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.WorkspaceId == workspaceId
                    && item.SourceType == CommercialEvidenceVocabulary.Order
                    && item.SourceSystem == null
                    && item.SourceId == orderId,
                cancellationToken);

    public Task<PurchaseEvidence?> ReadOriginalByIdAsync(
        string workspaceId,
        string evidenceId,
        CancellationToken cancellationToken) =>
        dbContext.PurchaseEvidence
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.WorkspaceId == workspaceId && item.EvidenceId == evidenceId,
                cancellationToken);

    public void Add(PurchaseEvidence evidence, CommercialEvidenceAuditRecord audit)
    {
        dbContext.PurchaseEvidence.Add(evidence);
        dbContext.AuditRecords.Add(audit);
    }

    public void ClearTrackedChanges() => dbContext.ChangeTracker.Clear();

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsNamedUniqueFailure(exception, CommercialEvidenceDbContext.SourceUniqueIndexName))
        {
            throw new CommercialEvidenceUniqueConflictException(
                CommercialEvidenceUniqueConflict.SourceIdentity,
                exception);
        }
        catch (DbUpdateException exception) when (IsNamedUniqueFailure(exception, CommercialEvidenceDbContext.AggregatePrimaryKeyName))
        {
            throw new CommercialEvidenceUniqueConflictException(
                CommercialEvidenceUniqueConflict.AggregateIdentity,
                exception);
        }
    }

    private static bool IsNamedUniqueFailure(DbUpdateException exception, string databaseObjectName) =>
        FindSqlException(exception) is { Number: 2601 or 2627 } sqlException
        && sqlException.Message.Contains(databaseObjectName, StringComparison.Ordinal);

    private static SqlException? FindSqlException(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException!)
        {
            if (current is SqlException sqlException)
                return sqlException;
        }

        return null;
    }
}
