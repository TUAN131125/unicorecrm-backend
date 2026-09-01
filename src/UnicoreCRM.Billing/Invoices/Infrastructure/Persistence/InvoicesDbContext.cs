using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UnicoreCRM.Billing.Invoices.Domain;

namespace UnicoreCRM.Billing.Invoices.Infrastructure.Persistence;

/// <summary>
/// The Invoices-owned persistence context. It maps the single Invoices-owned table in the
/// Invoices-owned schema, carries no foreign key to any other owner and holds nothing beyond the
/// durable state the admitted Invoice read contract returns.
/// </summary>
internal sealed class InvoicesDbContext(DbContextOptions<InvoicesDbContext> options) : DbContext(options)
{
    internal DbSet<Invoice> Invoices => Set<Invoice>();
    internal DbSet<InvoiceReadAuditRecord> ReadAuditRecords => Set<InvoiceReadAuditRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("invoices");
        modelBuilder.Entity<Invoice>(entity =>
        {
            entity.ToTable("Invoices", table =>
            {
                table.HasCheckConstraint("CK_Invoices_BuyerType", ExactValues("BuyerType", "CONTACT", "ORGANIZATION_ACCOUNT"));
                table.HasCheckConstraint("CK_Invoices_LifecycleState", ExactValues("LifecycleState", "DRAFT", "ISSUING", "ISSUED", "ISSUE_FAILED", "DISCARDED", "VOIDED"));
                table.HasCheckConstraint("CK_Invoices_DeliveryState", ExactValues("DeliveryState", "NOT_SENT", "SENDING", "SENT", "DELIVERY_FAILED"));
                table.HasCheckConstraint("CK_Invoices_Currency", CurrencyCheck("Currency"));
                table.HasCheckConstraint("CK_Invoices_SellerSnapshotJson", "ISJSON([SellerSnapshotJson]) = 1");
                table.HasCheckConstraint("CK_Invoices_BuyerSnapshotJson", "ISJSON([BuyerSnapshotJson]) = 1");
                table.HasCheckConstraint("CK_Invoices_LinesJson", "ISJSON([LinesJson]) = 1");
                table.HasCheckConstraint("CK_Invoices_TotalsJson", "ISJSON([TotalsJson]) = 1");
                table.HasCheckConstraint("CK_Invoices_SourceLinksJson", "ISJSON([SourceLinksJson]) = 1");
                table.HasCheckConstraint("CK_Invoices_ExchangeRateSnapshotJson", "[ExchangeRateSnapshotJson] IS NULL OR ISJSON([ExchangeRateSnapshotJson]) = 1");
                table.HasCheckConstraint("CK_Invoices_IssueEvidenceJson", "[IssueEvidenceJson] IS NULL OR ISJSON([IssueEvidenceJson]) = 1");
                table.HasCheckConstraint("CK_Invoices_ResourceVersion", "[ResourceVersion] >= 0");
                table.HasCheckConstraint("CK_Invoices_IdempotencyKey", "LEN([IdempotencyKey]) >= 8");
            });

            entity.HasKey(item => new { item.WorkspaceId, item.InvoiceId });
            Id(entity.Property(item => item.WorkspaceId));
            Id(entity.Property(item => item.InvoiceId));
            entity.Property(item => item.InvoiceNumber).HasMaxLength(160);
            entity.Property(item => item.BuyerType).HasMaxLength(32).IsRequired();
            Id(entity.Property(item => item.BuyerId));
            Json(entity.Property(item => item.SellerSnapshotJson));
            Json(entity.Property(item => item.BuyerSnapshotJson));
            entity.Property(item => item.LifecycleState).HasMaxLength(16).IsRequired();
            entity.Property(item => item.DeliveryState).HasMaxLength(16).IsRequired();
            entity.Property(item => item.IssueDate).HasColumnType("date");
            entity.Property(item => item.DueDate).HasColumnType("date");
            Currency(entity.Property(item => item.Currency));
            OptionalJson(entity.Property(item => item.ExchangeRateSnapshotJson));
            entity.Property(item => item.PaymentTerms).HasMaxLength(500);
            entity.Property(item => item.CreationIntentId).HasMaxLength(160);
            Json(entity.Property(item => item.LinesJson));
            Json(entity.Property(item => item.TotalsJson));
            Json(entity.Property(item => item.SourceLinksJson));
            entity.Property(item => item.IdempotencyKey).HasMaxLength(128).IsRequired();
            Timestamp(entity.Property(item => item.CreatedAt));
            Timestamp(entity.Property(item => item.UpdatedAt));
            Timestamp(entity.Property(item => item.IssuedAt));
            entity.Property(item => item.IssueFailureCode).HasMaxLength(160);
            OptionalJson(entity.Property(item => item.IssueEvidenceJson));
            Timestamp(entity.Property(item => item.DiscardedAt));
            Timestamp(entity.Property(item => item.VoidedAt));
            entity.Property(item => item.VoidReason).HasMaxLength(1000);
        });

        // Invoices-owned proof of successful disclosure. Separate from the AccessControl-owned
        // authorization and record decisions, which prove evaluation rather than disclosure.
        modelBuilder.Entity<InvoiceReadAuditRecord>(entity =>
        {
            entity.ToTable("ReadAuditRecords", table =>
            {
                table.HasCheckConstraint("CK_ReadAuditRecords_Outcome", ExactValues("Outcome", "READ"));
                table.HasCheckConstraint("CK_ReadAuditRecords_ResourceVersion", "[ResourceVersion] IS NULL OR [ResourceVersion] >= 0");
            });
            entity.HasKey(item => item.AuditId);
            Id(entity.Property(item => item.AuditId));
            entity.Property(item => item.Operation).HasMaxLength(128).IsRequired();
            Id(entity.Property(item => item.WorkspaceId));
            Id(entity.Property(item => item.ActorId));
            entity.Property(item => item.RecordId).HasMaxLength(128);
            Id(entity.Property(item => item.RequestId));
            Id(entity.Property(item => item.CorrelationId));
            entity.Property(item => item.Outcome).HasMaxLength(16).IsRequired();
            // Non-public properties are not mapped by convention, so the nullable version column
            // must be configured explicitly or its check constraint would reference a missing column.
            entity.Property(item => item.ResourceVersion);
            Timestamp(entity.Property(item => item.OccurredAt));
            entity.HasIndex(item => new { item.WorkspaceId, item.OccurredAt });
        });
    }

    private static void Id(PropertyBuilder<string> property) => property.HasMaxLength(128).IsRequired();
    private static void Currency(PropertyBuilder<string> property) => property.HasMaxLength(3).IsFixedLength().IsRequired();
    private static void Json(PropertyBuilder<string> property) => property.HasColumnType("nvarchar(max)").IsRequired();
    private static void OptionalJson(PropertyBuilder<string?> property) => property.HasColumnType("nvarchar(max)");
    private static void Timestamp(PropertyBuilder<DateTimeOffset> property) => property.HasPrecision(7);
    private static void Timestamp(PropertyBuilder<DateTimeOffset?> property) => property.HasPrecision(7);

    private static string CurrencyCheck(string column) =>
        $"[{column}] LIKE '[A-Z][A-Z][A-Z]' COLLATE Latin1_General_100_BIN2 AND DATALENGTH([{column}]) = 6";

    private static string ExactValues(string column, params string[] values) =>
        "(" + string.Join(" OR ", values.Select(value => $"([{column}] COLLATE Latin1_General_100_BIN2 = N'{value}' AND DATALENGTH([{column}]) = DATALENGTH(N'{value}'))")) + ")";
}
