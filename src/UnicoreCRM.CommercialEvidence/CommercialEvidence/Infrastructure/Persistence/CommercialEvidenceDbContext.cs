using Microsoft.EntityFrameworkCore;
using UnicoreCRM.CommercialEvidence.CommercialEvidence.Domain;

namespace UnicoreCRM.CommercialEvidence.CommercialEvidence.Infrastructure.Persistence;

internal sealed class CommercialEvidenceDbContext(DbContextOptions<CommercialEvidenceDbContext> options)
    : DbContext(options)
{
    internal const string Schema = "commercial_evidence";
    internal const string AggregatePrimaryKeyName = "PK_PurchaseEvidence";
    internal const string SourceUniqueIndexName = "UX_PurchaseEvidence_Workspace_Source";

    internal DbSet<PurchaseEvidence> PurchaseEvidence => Set<PurchaseEvidence>();
    internal DbSet<CommercialEvidenceAuditRecord> AuditRecords => Set<CommercialEvidenceAuditRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<PurchaseEvidence>(entity =>
        {
            entity.ToTable("PurchaseEvidence", table =>
            {
                table.HasCheckConstraint(
                    "CK_PurchaseEvidence_EvidenceType",
                    ExactVocabulary("EvidenceType", [
                        CommercialEvidenceVocabulary.OrderCompleted,
                        CommercialEvidenceVocabulary.ExternalPurchaseConfirmed,
                        CommercialEvidenceVocabulary.HistoricalPurchaseImported]));
                table.HasCheckConstraint(
                    "CK_PurchaseEvidence_SourceType",
                    ExactVocabulary("SourceType", [
                        CommercialEvidenceVocabulary.Order,
                        CommercialEvidenceVocabulary.ExternalPurchase,
                        CommercialEvidenceVocabulary.HistoricalImport]));
                table.HasCheckConstraint(
                    "CK_PurchaseEvidence_BuyerRefType",
                    ExactVocabulary("BuyerRefType", [
                        CommercialEvidenceVocabulary.Contact,
                        CommercialEvidenceVocabulary.OrganizationAccount]));
                table.HasCheckConstraint(
                    "CK_PurchaseEvidence_SourceMapping",
                    $"(([SourceType] = N'{CommercialEvidenceVocabulary.Order}' AND " +
                    $"[EvidenceType] = N'{CommercialEvidenceVocabulary.OrderCompleted}' AND [SourceSystem] IS NULL) OR " +
                    $"([SourceType] = N'{CommercialEvidenceVocabulary.ExternalPurchase}' AND " +
                    $"[EvidenceType] = N'{CommercialEvidenceVocabulary.ExternalPurchaseConfirmed}' AND " +
                    "[SourceSystem] IS NOT NULL AND DATALENGTH([SourceSystem]) > 0) OR " +
                    $"([SourceType] = N'{CommercialEvidenceVocabulary.HistoricalImport}' AND " +
                    $"[EvidenceType] = N'{CommercialEvidenceVocabulary.HistoricalPurchaseImported}' AND " +
                    "[SourceSystem] IS NOT NULL AND DATALENGTH([SourceSystem]) > 0))");
                table.HasCheckConstraint("CK_PurchaseEvidence_SourceId", "DATALENGTH([SourceId]) > 0");
                table.HasCheckConstraint("CK_PurchaseEvidence_PolicyVersion", "DATALENGTH([PolicyVersion]) > 0");
                table.HasCheckConstraint("CK_PurchaseEvidence_CorrelationId", "DATALENGTH([CorrelationId]) > 0");
            });
            entity.HasKey(item => new { item.WorkspaceId, item.EvidenceId })
                .HasName(AggregatePrimaryKeyName);
            entity.Property(item => item.WorkspaceId).HasMaxLength(128).UseCollation("Latin1_General_100_BIN2");
            entity.Property(item => item.EvidenceId).HasMaxLength(128).UseCollation("Latin1_General_100_BIN2");
            entity.Property(item => item.EvidenceType).HasMaxLength(40).UseCollation("Latin1_General_100_BIN2");
            entity.Property(item => item.BuyerRefType).HasMaxLength(40).UseCollation("Latin1_General_100_BIN2");
            entity.Property(item => item.BuyerRefId).HasMaxLength(128).UseCollation("Latin1_General_100_BIN2");
            entity.Property(item => item.SourceType).HasMaxLength(32).UseCollation("Latin1_General_100_BIN2");
            entity.Property(item => item.SourceSystem).HasMaxLength(128).UseCollation("Latin1_General_100_BIN2");
            entity.Property(item => item.SourceId).HasMaxLength(128).UseCollation("Latin1_General_100_BIN2");
            entity.Property(item => item.OccurredAt).HasPrecision(7);
            entity.Property(item => item.PolicyVersion).HasMaxLength(128).UseCollation("Latin1_General_100_BIN2");
            entity.Property(item => item.CorrelationId).HasMaxLength(128).UseCollation("Latin1_General_100_BIN2");
            entity.HasIndex(item => new { item.WorkspaceId, item.SourceType, item.SourceSystem, item.SourceId })
                .IsUnique()
                .HasFilter(null)
                .HasDatabaseName(SourceUniqueIndexName);
        });

        modelBuilder.Entity<CommercialEvidenceAuditRecord>(entity =>
        {
            entity.ToTable("AuditRecords", table =>
            {
                table.HasCheckConstraint(
                    "CK_CommercialEvidenceAudit_Operation",
                    ExactVocabulary("Operation", [CommercialEvidenceVocabulary.OriginalAppend]));
            });
            entity.HasKey(item => item.AuditId).HasName("PK_CommercialEvidenceAudit");
            entity.Property(item => item.AuditId).HasMaxLength(128).UseCollation("Latin1_General_100_BIN2");
            entity.Property(item => item.WorkspaceId).HasMaxLength(128).UseCollation("Latin1_General_100_BIN2");
            entity.Property(item => item.EvidenceId).HasMaxLength(128).UseCollation("Latin1_General_100_BIN2");
            entity.Property(item => item.Operation).HasMaxLength(40).UseCollation("Latin1_General_100_BIN2");
            entity.Property(item => item.CorrelationId).HasMaxLength(128).UseCollation("Latin1_General_100_BIN2");
            entity.Property(item => item.OccurredAt).HasPrecision(7);
            entity.Property(item => item.PolicyVersion).HasMaxLength(128).UseCollation("Latin1_General_100_BIN2");
            entity.HasIndex(item => new { item.WorkspaceId, item.EvidenceId })
                .IsUnique()
                .HasDatabaseName("UX_CommercialEvidenceAudit_Workspace_Evidence");
            entity.HasIndex(item => new { item.WorkspaceId, item.OccurredAt })
                .HasDatabaseName("IX_CommercialEvidenceAudit_Workspace_OccurredAt");
            entity.HasOne<PurchaseEvidence>()
                .WithOne()
                .HasForeignKey<CommercialEvidenceAuditRecord>(item => new { item.WorkspaceId, item.EvidenceId })
                .HasPrincipalKey<PurchaseEvidence>(item => new { item.WorkspaceId, item.EvidenceId })
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_CommercialEvidenceAudit_PurchaseEvidence");
        });
    }

    private static string ExactVocabulary(string column, IReadOnlyList<string> values) =>
        "(" + string.Join(
            " OR ",
            values.Select(value =>
                $"([{column}] COLLATE Latin1_General_100_BIN2 = N'{value}' AND " +
                $"DATALENGTH([{column}]) = DATALENGTH(N'{value}'))")) + ")";
}
