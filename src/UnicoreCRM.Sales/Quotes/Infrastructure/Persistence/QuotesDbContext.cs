using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Sales.Quotes.Domain;

namespace UnicoreCRM.Sales.Quotes.Infrastructure.Persistence;

internal sealed class QuotesDbContext(DbContextOptions<QuotesDbContext> options) : DbContext(options)
{
    internal DbSet<Quote> Quotes => Set<Quote>();
    internal DbSet<QuoteReadAuditRecord> ReadAuditRecords => Set<QuoteReadAuditRecord>();
    internal DbSet<QuoteCommandRecord> CommandRecords => Set<QuoteCommandRecord>();
    internal DbSet<QuoteOutboxMessage> OutboxMessages => Set<QuoteOutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("quotes");
        modelBuilder.MapSalesAcceptance();
        QuoteProofModel.Configure(modelBuilder);
        QuoteProofScanModel.Configure(modelBuilder);
        QuoteIdentityModel.Configure(modelBuilder);
        modelBuilder.HasSequence<long>("QuoteNumbers", "quotes");
        modelBuilder.Entity<QuoteCustomerDecisionRecord>(entity =>
        {
            entity.ToTable("CustomerDecisionRecords", table =>
            {
                table.HasCheckConstraint("CK_CustomerDecisionRecords_PayloadJson", "ISJSON([PayloadJson]) = 1");
                table.HasCheckConstraint("CK_CustomerDecisionRecords_Type", "[DecisionType] COLLATE Latin1_General_100_BIN2 IN ('ACCEPTANCE','REJECTION')");
                table.HasCheckConstraint("CK_CustomerDecisionRecords_Trust", "[VerificationStatus] COLLATE Latin1_General_100_BIN2 = 'UNVERIFIED'");
                table.HasCheckConstraint("CK_CustomerDecisionRecords_Version", "[QuoteRevision] >= 1 AND [ContentResourceVersion] >= 1 AND [ResultResourceVersion] = [ContentResourceVersion] + 1");
            });
            entity.HasKey(x => new { x.WorkspaceId, x.DecisionId });
            entity.Property(x => x.WorkspaceId).HasMaxLength(128);
            entity.Property(x => x.DecisionId).HasMaxLength(128).UseCollation("Latin1_General_100_BIN2");
            entity.Property(x => x.QuoteId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.ActorId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.DecisionType).HasMaxLength(32).IsRequired();
            entity.Property(x => x.VerificationStatus).HasMaxLength(32).IsRequired();
            entity.Property(x => x.ContentFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(x => x.RequestFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(x => x.PolicyVersion).HasMaxLength(160).IsRequired();
            entity.Property(x => x.PayloadJson).IsRequired();
            entity.Property(x => x.CommandId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.AuditId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.EventId).HasMaxLength(128).IsRequired();
            entity.HasIndex(x => new { x.WorkspaceId, x.QuoteId, x.ResultResourceVersion }).IsUnique();
        });
        modelBuilder.Entity<QuoteDocument>(entity =>
        {
            entity.ToTable("Documents", table => {
                table.HasCheckConstraint("CK_QuoteDocument_Bounds", "[FileSize] BETWEEN 1 AND 10485760 AND [PageCount] BETWEEN 1 AND 100 AND [DocumentVersion] >= 1 AND [QuoteRevision] >= 1 AND [SourceResourceVersion] >= 1 AND [ResultResourceVersion] = [SourceResourceVersion] + 1");
            });
            entity.HasKey(x => new { x.WorkspaceId, x.DocumentId });
            foreach (var name in new[] { "WorkspaceId", "DocumentId", "QuoteId", "RootQuoteId", "IssuedBy" }) entity.Property<string>(name).HasMaxLength(128);
            foreach (var name in new[] { "BindingHash", "CommercialFingerprint", "PdfSha256" }) entity.Property<string>(name).HasMaxLength(64);
            foreach (var name in new[] { "ApprovalPolicyVersion", "PolicyVersion", "TemplateVersion" }) entity.Property<string>(name).HasMaxLength(160);
            entity.Property(x => x.SnapshotJson).IsRequired(); entity.Property(x => x.FieldManifestJson).IsRequired();
            entity.HasOne<Quote>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.QuoteId }).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.WorkspaceId, x.QuoteId, x.DocumentVersion }).IsUnique();
            entity.HasIndex(x => new { x.WorkspaceId, x.QuoteId, x.BindingHash }).IsUnique();
        });
        modelBuilder.Entity<QuoteDocumentContent>(entity =>
        {
            entity.ToTable("DocumentContents"); entity.HasKey(x => new { x.WorkspaceId, x.DocumentId });
            entity.Property(x => x.WorkspaceId).HasMaxLength(128); entity.Property(x => x.DocumentId).HasMaxLength(128);
            entity.Property(x => x.Bytes).HasColumnType("varbinary(max)").IsRequired();
            entity.HasOne<QuoteDocument>().WithOne().HasForeignKey<QuoteDocumentContent>(x => new { x.WorkspaceId, x.DocumentId }).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<QuoteCommandRecord>(entity =>
        {
            entity.ToTable("CommandRecords"); entity.HasKey(x => x.ScopeKey);
            entity.Property(x => x.ScopeKey).HasMaxLength(64);
            entity.Property(x => x.Fingerprint).HasMaxLength(64).IsRequired();
            entity.Property(x => x.WorkspaceId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.ActorId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Operation).HasMaxLength(128).IsRequired();
            entity.Property(x => x.QuoteId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.AuditId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.RequestId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.ResponseJson).IsRequired();
            entity.HasIndex(x => x.AuditId).IsUnique();
            entity.HasIndex(x => new { x.WorkspaceId, x.QuoteId, x.OccurredAt });
        });
        modelBuilder.Entity<QuoteOutboxMessage>(entity =>
        {
            entity.ToTable("OutboxMessages"); entity.HasKey(x => x.EventId);
            entity.Property(x => x.EventId).HasMaxLength(128);
            entity.Property(x => x.WorkspaceId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.QuoteId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.EventType).HasMaxLength(128).IsRequired();
            entity.Property(x => x.CorrelationId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.PayloadJson).IsRequired();
            entity.HasIndex(x => new { x.WorkspaceId, x.OccurredAt });
        });
        modelBuilder.Entity<Quote>(entity =>
        {
            entity.HasMany(x => x.CustomerDecisions).WithOne().HasForeignKey(x => new { x.WorkspaceId, x.QuoteId }).OnDelete(DeleteBehavior.Restrict);
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
            entity.Property(item => item.ProductSnapshotsJson).HasColumnType("nvarchar(max)");
            entity.Property(item => item.DeliveryHistoryJson).HasColumnType("nvarchar(max)");
            entity.Property(item => item.SenderName).HasMaxLength(300);
            entity.Property(item => item.SenderAddress).HasMaxLength(1000);
            entity.Property(item => item.SenderEmail).HasMaxLength(320);
            entity.Property(item => item.SenderTaxId).HasMaxLength(120);
            entity.Property(item => item.ResourceVersion).IsConcurrencyToken();
            Timestamp(entity.Property(item => item.CreatedAt));
            Timestamp(entity.Property(item => item.UpdatedAt));

            entity.HasIndex(item => new { item.WorkspaceId, item.UpdatedAt, item.QuoteId })
                .IsDescending(false, true, false);
            entity.HasIndex(item => new { item.WorkspaceId, item.CreatedAt, item.QuoteId })
                .IsDescending(false, true, false);
            entity.HasIndex(item => new { item.WorkspaceId, item.ValidUntil, item.QuoteId });
            entity.HasIndex(item => new { item.WorkspaceId, item.GrandTotalAmount, item.QuoteId });
            entity.HasIndex(item => new { item.WorkspaceId, item.QuoteNumber, item.QuoteId });
            entity.HasIndex(item => new { item.WorkspaceId, item.QuoteNumber }).IsUnique();
            entity.HasIndex(item => new { item.WorkspaceId, item.RootQuoteId, item.QuoteRevision }).IsUnique();
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

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => SaveChangesAsync(true, cancellationToken);

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        GuardImmutableEvidence();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        GuardImmutableEvidence();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    private void GuardImmutableEvidence()
    {
        QuoteProofModel.Guard(this);
        QuoteProofScanModel.Guard(this);
        if (ChangeTracker.Entries().Any(entry => entry.Entity is QuoteIdentityVersion or QuoteEvidenceSourceVersion or QuoteProofScanAttempt or QuoteProofScanResult or QuoteCommandRecord or QuoteOutboxMessage or QuoteCustomerDecisionRecord or QuoteDocument or QuoteDocumentContent or QuoteAcceptanceProof or QuoteAcceptanceProofContent
            && entry.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Quote command evidence is immutable.");
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
