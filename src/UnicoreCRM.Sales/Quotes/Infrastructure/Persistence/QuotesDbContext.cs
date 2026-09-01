using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Sales.Quotes.Domain;

namespace UnicoreCRM.Sales.Quotes.Infrastructure.Persistence;

internal sealed class QuotesDbContext(DbContextOptions<QuotesDbContext> options) : DbContext(options)
{
    internal DbSet<Quote> Quotes => Set<Quote>();
    internal DbSet<QuoteReadAuditRecord> ReadAuditRecords => Set<QuoteReadAuditRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("quotes");
        modelBuilder.Entity<Quote>(entity =>
        {
            entity.ToTable("Quotes", table =>
            {
                table.HasCheckConstraint("CK_Quotes_QuoteRevision", "[QuoteRevision] >= 1");
                table.HasCheckConstraint("CK_Quotes_ResourceVersion", "[ResourceVersion] >= 0");
                table.HasCheckConstraint("CK_Quotes_BuyerType", ExactValues("BuyerType", "CONTACT", "ORGANIZATION_ACCOUNT"));
                table.HasCheckConstraint("CK_Quotes_SourcePath", ExactValues("SourcePath", "DEAL", "DIRECT_SALE"));
                table.HasCheckConstraint("CK_Quotes_Status", ExactValues("Status", "DRAFT", "REVIEW", "SENT", "ACCEPTED", "REJECTED", "EXPIRED"));
                table.HasCheckConstraint("CK_Quotes_ApprovalStatus", $"[ApprovalStatus] IS NULL OR ({ExactValues("ApprovalStatus", "NOT_REQUIRED", "PENDING", "APPROVED", "CHANGES_REQUESTED")})");
                table.HasCheckConstraint("CK_Quotes_LineItemsJson", "ISJSON([LineItemsJson]) = 1");
                table.HasCheckConstraint("CK_Quotes_ActionsJson", "ISJSON([ActionsJson]) = 1");
                table.HasCheckConstraint("CK_Quotes_AdjustmentsJson", "[AdjustmentsJson] IS NULL OR ISJSON([AdjustmentsJson]) = 1");
                table.HasCheckConstraint("CK_Quotes_ApprovalReasonsJson", "[ApprovalReasonsJson] IS NULL OR ISJSON([ApprovalReasonsJson]) = 1");
                table.HasCheckConstraint("CK_Quotes_PaymentAgreementJson", "[PaymentAgreementJson] IS NULL OR ISJSON([PaymentAgreementJson]) = 1");
                table.HasCheckConstraint("CK_Quotes_DeliveryHistoryJson", "[DeliveryHistoryJson] IS NULL OR ISJSON([DeliveryHistoryJson]) = 1");
            });
            entity.HasKey(item => new { item.WorkspaceId, item.QuoteId });
            entity.Property(item => item.WorkspaceId).HasMaxLength(128);
            entity.Property(item => item.QuoteId).HasMaxLength(128);
            entity.Property(item => item.QuoteNumber).HasMaxLength(120).IsRequired();
            entity.Property(item => item.QuoteRevision);
            entity.Property(item => item.RootQuoteId).HasMaxLength(128).IsRequired();
            entity.Property(item => item.RevisionOfQuoteId).HasMaxLength(128);
            entity.Property(item => item.BuyerType).HasMaxLength(32).IsRequired();
            entity.Property(item => item.BuyerId).HasMaxLength(128).IsRequired();
            entity.Property(item => item.SourcePath).HasMaxLength(16).IsRequired();
            entity.Property(item => item.SourceDealId).HasMaxLength(128);
            entity.Property(item => item.ContactId).HasMaxLength(128);
            entity.Property(item => item.SourceLeadId).HasMaxLength(128);
            entity.Property(item => item.Status).HasMaxLength(16).IsRequired();
            entity.Property(item => item.Title).HasMaxLength(300).IsRequired();
            entity.Property(item => item.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
            entity.Property(item => item.OwnerId).HasMaxLength(128);
            entity.Property(item => item.RecipientEmail).HasMaxLength(320);
            entity.Property(item => item.LineItemsJson).HasColumnType("nvarchar(max)").IsRequired();
            entity.Property(item => item.AdjustmentsJson).HasColumnType("nvarchar(max)");
            Money(entity.Property(item => item.SubtotalAmount));
            entity.Property(item => item.SubtotalCurrency).HasMaxLength(3).IsFixedLength().IsRequired();
            Money(entity.Property(item => item.DiscountTotalAmount));
            entity.Property(item => item.DiscountTotalCurrency).HasMaxLength(3).IsFixedLength().IsRequired();
            Money(entity.Property(item => item.TaxTotalAmount));
            entity.Property(item => item.TaxTotalCurrency).HasMaxLength(3).IsFixedLength().IsRequired();
            Money(entity.Property(item => item.GrandTotalAmount));
            entity.Property(item => item.GrandTotalCurrency).HasMaxLength(3).IsFixedLength().IsRequired();
            entity.Property(item => item.ValidUntil).HasColumnType("date");
            Timestamp(entity.Property(item => item.ReviewRequestedAt));
            Timestamp(entity.Property(item => item.SentAt));
            Timestamp(entity.Property(item => item.AcceptedAt));
            Timestamp(entity.Property(item => item.RejectedAt));
            Timestamp(entity.Property(item => item.ExpiredAt));
            entity.Property(item => item.Notes).HasMaxLength(4000);
            Timestamp(entity.Property(item => item.ArchivedAt));
            entity.Property(item => item.ArchiveReason).HasMaxLength(500);
            entity.Property(item => item.ActionsJson).HasColumnType("nvarchar(max)").IsRequired();
            entity.Property(item => item.ApprovalStatus).HasMaxLength(32);
            entity.Property(item => item.ApprovalRequired);
            entity.Property(item => item.ApprovalReasonsJson).HasColumnType("nvarchar(max)");
            Timestamp(entity.Property(item => item.ApprovalRequestedAt));
            entity.Property(item => item.ApprovalRequestedBy).HasMaxLength(128);
            Timestamp(entity.Property(item => item.ApprovedAt));
            entity.Property(item => item.ApprovedBy).HasMaxLength(128);
            entity.Property(item => item.ApprovalDecisionNote).HasMaxLength(2000);
            entity.Property(item => item.ApprovalContentFingerprint).HasMaxLength(256);
            entity.Property(item => item.ApprovalPolicyVersion).HasMaxLength(160);
            entity.Property(item => item.PaymentAgreementJson).HasColumnType("nvarchar(max)");
            entity.Property(item => item.DeliveryHistoryJson).HasColumnType("nvarchar(max)");
            entity.Property(item => item.SenderName).HasMaxLength(300);
            entity.Property(item => item.SenderAddress).HasMaxLength(1000);
            entity.Property(item => item.SenderEmail).HasMaxLength(320);
            entity.Property(item => item.SenderTaxId).HasMaxLength(120);
            entity.Property(item => item.ResourceVersion);
            Timestamp(entity.Property(item => item.CreatedAt));
            Timestamp(entity.Property(item => item.UpdatedAt));

            entity.HasIndex(item => new { item.WorkspaceId, item.UpdatedAt, item.QuoteId })
                .IsDescending(false, true, false);
            entity.HasIndex(item => new { item.WorkspaceId, item.CreatedAt, item.QuoteId })
                .IsDescending(false, true, false);
            entity.HasIndex(item => new { item.WorkspaceId, item.ValidUntil, item.QuoteId });
            entity.HasIndex(item => new { item.WorkspaceId, item.GrandTotalAmount, item.QuoteId });
            entity.HasIndex(item => new { item.WorkspaceId, item.QuoteNumber, item.QuoteId });
            entity.HasIndex(item => new { item.WorkspaceId, item.Status });
            entity.HasIndex(item => new { item.WorkspaceId, item.SourceDealId });
            entity.HasIndex(item => new { item.WorkspaceId, item.BuyerType, item.BuyerId });
        });

        modelBuilder.Entity<QuoteReadAuditRecord>(entity =>
        {
            entity.ToTable("ReadAuditRecords", table =>
            {
                table.HasCheckConstraint("CK_ReadAuditRecords_Outcome", ExactValues("Outcome", "READ"));
                table.HasCheckConstraint(
                    "CK_ReadAuditRecords_ResourceVersion",
                    "[ResourceVersion] IS NULL OR [ResourceVersion] >= 0");
            });
            entity.HasKey(item => item.AuditId);
            EntityId(entity.Property(item => item.AuditId));
            entity.Property(item => item.Operation).HasMaxLength(128).IsRequired();
            EntityId(entity.Property(item => item.WorkspaceId));
            EntityId(entity.Property(item => item.ActorId));
            entity.Property(item => item.RecordId).HasMaxLength(128);
            EntityId(entity.Property(item => item.RequestId));
            EntityId(entity.Property(item => item.CorrelationId));
            entity.Property(item => item.Outcome).HasMaxLength(16).IsRequired();
            entity.Property(item => item.ResourceVersion);
            Timestamp(entity.Property(item => item.OccurredAt));
            entity.HasIndex(item => new { item.WorkspaceId, item.OccurredAt });
        });
    }

    private static void EntityId(Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<string> property) =>
        property.HasMaxLength(128).IsRequired();

    private static void Money(Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<decimal> property) =>
        property.HasPrecision(38, 6);

    private static void Timestamp(Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<DateTimeOffset> property) =>
        property.HasPrecision(7);

    private static void Timestamp(Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<DateTimeOffset?> property) =>
        property.HasPrecision(7);

    private static string ExactValues(string column, params string[] values) =>
        "(" + string.Join(" OR ", values.Select(value =>
            $"([{column}] COLLATE Latin1_General_100_BIN2 = N'{value}' AND DATALENGTH([{column}]) = DATALENGTH(N'{value}'))")) + ")";
}
