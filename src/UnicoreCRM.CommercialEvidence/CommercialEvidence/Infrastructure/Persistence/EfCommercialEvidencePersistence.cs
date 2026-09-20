using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
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
        var admittedJson = JsonSerializer.Serialize(buyerRefs.Select(reference => new
        {
            BuyerRefType = CommercialEvidenceValidation.PersistedBuyerRefType(reference.Type),
            BuyerRefId = reference.Id
        }));
        const string sql = """
            WITH admitted AS (
                SELECT BuyerRefType, BuyerRefId
                FROM OPENJSON(@buyerRefs)
                WITH (BuyerRefType nvarchar(40) '$.BuyerRefType', BuyerRefId nvarchar(128) '$.BuyerRefId')
            ), ranked AS (
                SELECT evidence.BuyerRefType, evidence.BuyerRefId, evidence.OccurredAt,
                       COUNT_BIG(*) OVER (PARTITION BY evidence.BuyerRefType, evidence.BuyerRefId) AS PurchaseCount,
                       MIN(evidence.OccurredAt) OVER (PARTITION BY evidence.BuyerRefType, evidence.BuyerRefId) AS FirstPurchaseAt,
                       MAX(evidence.OccurredAt) OVER (PARTITION BY evidence.BuyerRefType, evidence.BuyerRefId) AS LastPurchaseAt,
                       ROW_NUMBER() OVER (PARTITION BY evidence.BuyerRefType, evidence.BuyerRefId
                                          ORDER BY evidence.OccurredAt DESC, evidence.EvidenceId DESC) AS RecentRank
                FROM commercial_evidence.PurchaseEvidence AS evidence
                INNER JOIN admitted ON admitted.BuyerRefType = evidence.BuyerRefType
                                   AND admitted.BuyerRefId = evidence.BuyerRefId
                WHERE evidence.WorkspaceId = @workspaceId AND evidence.OccurredAt <= @asOf
            )
            SELECT BuyerRefType, BuyerRefId, PurchaseCount, FirstPurchaseAt, LastPurchaseAt, OccurredAt
            FROM ranked
            WHERE RecentRank <= 6
            """;
        return await dbContext.Database.SqlQueryRaw<PurchaseHealthSignalRow>(sql,
                new SqlParameter("@buyerRefs", admittedJson),
                new SqlParameter("@workspaceId", workspaceId),
                new SqlParameter("@asOf", asOf))
            .ToListAsync(cancellationToken);
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
