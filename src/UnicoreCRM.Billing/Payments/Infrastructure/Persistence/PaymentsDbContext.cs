using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Billing.Payments.Domain;

namespace UnicoreCRM.Billing.Payments.Infrastructure.Persistence;

internal sealed class PaymentsDbContext(DbContextOptions<PaymentsDbContext> options) : DbContext(options)
{
    internal DbSet<PaymentPlan> PaymentPlans => Set<PaymentPlan>();
    internal DbSet<PaymentScheduleLine> PaymentScheduleLines => Set<PaymentScheduleLine>();
    internal DbSet<PaymentIntent> PaymentIntents => Set<PaymentIntent>();
    internal DbSet<PaymentRecord> PaymentRecords => Set<PaymentRecord>();
    internal DbSet<PaymentReadAuditRecord> ReadAuditRecords => Set<PaymentReadAuditRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("payments");
        modelBuilder.Entity<PaymentPlan>(entity =>
        {
            entity.ToTable("PaymentPlans", table =>
            {
                table.HasCheckConstraint("CK_PaymentPlans_BuyerType", ExactValues("BuyerType", "CONTACT", "ORGANIZATION_ACCOUNT"));
                table.HasCheckConstraint("CK_PaymentPlans_Kind", ExactValues("Kind", "FULL_PAYMENT", "DEPOSIT_AND_BALANCE", "INSTALLMENT", "MILESTONE", "CUSTOM"));
                table.HasCheckConstraint("CK_PaymentPlans_State", ExactValues("State", "DRAFT", "ACTIVE", "SUPERSEDED", "CANCELLED", "COMPLETED"));
                table.HasCheckConstraint("CK_PaymentPlans_Currency", "[Currency] LIKE '[A-Z][A-Z][A-Z]' COLLATE Latin1_General_100_BIN2 AND DATALENGTH([Currency]) = 6");
                table.HasCheckConstraint("CK_PaymentPlans_AgreementSnapshotJson", "ISJSON([AgreementSnapshotJson]) = 1");
                table.HasCheckConstraint("CK_PaymentPlans_ScheduleLineIdsJson", "ISJSON([ScheduleLineIdsJson]) = 1");
                table.HasCheckConstraint("CK_PaymentPlans_EvidenceCount", "[EvidenceCount] >= 0");
                table.HasCheckConstraint("CK_PaymentPlans_ResourceVersion", "[ResourceVersion] >= 0");
            });
            entity.HasKey(item => new { item.WorkspaceId, item.PaymentPlanId });
            Id(entity.Property(item => item.WorkspaceId));
            Id(entity.Property(item => item.PaymentPlanId));
            Id(entity.Property(item => item.OrderId));
            entity.Property(item => item.BuyerType).HasMaxLength(32).IsRequired();
            Id(entity.Property(item => item.BuyerId));
            entity.Property(item => item.Kind).HasMaxLength(32).IsRequired();
            entity.Property(item => item.State).HasMaxLength(16).IsRequired();
            Currency(entity.Property(item => item.Currency));
            Json(entity.Property(item => item.AgreementSnapshotJson));
            Json(entity.Property(item => item.ScheduleLineIdsJson));
            OptionalId(entity.Property(item => item.SupersedesPlanId));
            OptionalId(entity.Property(item => item.SupersededByPlanId));
            Timestamp(entity.Property(item => item.CreatedAt));
            Timestamp(entity.Property(item => item.UpdatedAt));
            Timestamp(entity.Property(item => item.ActivatedAt));
            Timestamp(entity.Property(item => item.CompletedAt));
            Timestamp(entity.Property(item => item.CancelledAt));
            entity.HasIndex(item => new { item.WorkspaceId, item.OrderId });
        });

        modelBuilder.Entity<PaymentScheduleLine>(entity =>
        {
            entity.ToTable("PaymentScheduleLines", table =>
            {
                table.HasCheckConstraint("CK_PaymentScheduleLines_BuyerType", ExactValues("BuyerType", "CONTACT", "ORGANIZATION_ACCOUNT"));
                table.HasCheckConstraint("CK_PaymentScheduleLines_PlanVersion", "[PaymentPlanVersion] >= 0");
                table.HasCheckConstraint("CK_PaymentScheduleLines_Sequence", "[Sequence] >= 1");
                table.HasCheckConstraint("CK_PaymentScheduleLines_Purpose", ExactValues("Purpose", "FULL", "DEPOSIT", "BALANCE", "INSTALLMENT", "MILESTONE", "OTHER"));
                table.HasCheckConstraint("CK_PaymentScheduleLines_AmountRuleJson", "ISJSON([AmountRuleJson]) = 1");
                table.HasCheckConstraint("CK_PaymentScheduleLines_DueRuleJson", "ISJSON([DueRuleJson]) = 1");
                table.HasCheckConstraint("CK_PaymentScheduleLines_AllowedMethodCodesJson", "ISJSON([AllowedMethodCodesJson]) = 1");
                table.HasCheckConstraint("CK_PaymentScheduleLines_FulfillmentGate", ExactValues("FulfillmentGate", "NONE", "BEFORE_BOOKING", "BEFORE_DISPATCH", "BEFORE_COMPLETION"));
                table.HasCheckConstraint("CK_PaymentScheduleLines_Channel", $"[Channel] IS NULL OR ({ExactValues("Channel", "BANK", "ONLINE_GATEWAY", "POS", "CARRIER", "OFFLINE", "EXTERNAL")})");
                table.HasCheckConstraint("CK_PaymentScheduleLines_State", ExactValues("State", "SCHEDULED", "NOT_DUE", "DUE", "PARTIAL", "SATISFIED", "OVERDUE", "VOIDED"));
                table.HasCheckConstraint("CK_PaymentScheduleLines_ResourceVersion", "[ResourceVersion] >= 0");
            });
            entity.HasKey(item => new { item.WorkspaceId, item.PaymentScheduleLineId });
            Id(entity.Property(item => item.WorkspaceId));
            Id(entity.Property(item => item.PaymentScheduleLineId));
            Id(entity.Property(item => item.PaymentPlanId));
            Id(entity.Property(item => item.OrderId));
            entity.Property(item => item.BuyerType).HasMaxLength(32).IsRequired();
            Id(entity.Property(item => item.BuyerId));
            entity.Property(item => item.Label).HasMaxLength(240).IsRequired();
            entity.Property(item => item.Purpose).HasMaxLength(16).IsRequired();
            Json(entity.Property(item => item.AmountRuleJson));
            Money(entity.Property(item => item.Amount));
            Currency(entity.Property(item => item.AmountCurrency));
            Json(entity.Property(item => item.DueRuleJson));
            entity.Property(item => item.ResolvedDueDate).HasColumnType("date");
            Json(entity.Property(item => item.AllowedMethodCodesJson));
            entity.Property(item => item.PreferredMethodCode).HasMaxLength(100);
            entity.Property(item => item.Channel).HasMaxLength(32);
            entity.Property(item => item.FulfillmentGate).HasMaxLength(32).IsRequired();
            entity.Property(item => item.InvoicePolicyCode).HasMaxLength(100);
            entity.Property(item => item.State).HasMaxLength(16).IsRequired();
            Money(entity.Property(item => item.SatisfiedAmount));
            Currency(entity.Property(item => item.SatisfiedCurrency));
            Money(entity.Property(item => item.OutstandingAmount));
            Currency(entity.Property(item => item.OutstandingCurrency));
            Timestamp(entity.Property(item => item.CreatedAt));
            Timestamp(entity.Property(item => item.UpdatedAt));
            entity.HasIndex(item => new { item.WorkspaceId, item.PaymentPlanId });
        });

        modelBuilder.Entity<PaymentIntent>(entity =>
        {
            entity.ToTable("PaymentIntents", table =>
            {
                table.HasCheckConstraint("CK_PaymentIntents_BuyerType", ExactValues("BuyerType", "CONTACT", "ORGANIZATION_ACCOUNT"));
                table.HasCheckConstraint("CK_PaymentIntents_InvoiceIdsJson", "ISJSON([InvoiceIdsJson]) = 1");
                table.HasCheckConstraint("CK_PaymentIntents_ScheduleLineIdsJson", "ISJSON([ScheduleLineIdsJson]) = 1");
                table.HasCheckConstraint("CK_PaymentIntents_Currency", "[Currency] LIKE '[A-Z][A-Z][A-Z]' COLLATE Latin1_General_100_BIN2 AND DATALENGTH([Currency]) = 6");
                table.HasCheckConstraint("CK_PaymentIntents_State", ExactValues("State", "CREATED", "REQUIRES_ACTION", "PROCESSING", "SUCCEEDED", "FAILED", "CANCELLED", "EXPIRED"));
                table.HasCheckConstraint("CK_PaymentIntents_Purpose", $"[Purpose] IS NULL OR ({ExactValues("Purpose", "DEPOSIT", "FULL_PAYMENT", "INSTALLMENT", "OVERDUE_REMINDER", "OTHER")})");
                table.HasCheckConstraint("CK_PaymentIntents_ResourceVersion", "[ResourceVersion] >= 0");
            });
            entity.HasKey(item => new { item.WorkspaceId, item.PaymentIntentId });
            Id(entity.Property(item => item.WorkspaceId));
            Id(entity.Property(item => item.PaymentIntentId));
            entity.Property(item => item.BuyerType).HasMaxLength(32).IsRequired();
            Id(entity.Property(item => item.BuyerId));
            OptionalId(entity.Property(item => item.OrderId));
            Json(entity.Property(item => item.InvoiceIdsJson));
            Json(entity.Property(item => item.ScheduleLineIdsJson));
            Money(entity.Property(item => item.Amount));
            Currency(entity.Property(item => item.Currency));
            entity.Property(item => item.MethodCode).HasMaxLength(100).IsRequired();
            entity.Property(item => item.ProviderCode).HasMaxLength(100).IsRequired();
            entity.Property(item => item.State).HasMaxLength(16).IsRequired();
            entity.Property(item => item.CheckoutUrl).HasMaxLength(2000);
            Timestamp(entity.Property(item => item.ExpiresAt));
            entity.Property(item => item.FailureCode).HasMaxLength(160);
            entity.Property(item => item.Purpose).HasMaxLength(32);
            Timestamp(entity.Property(item => item.CreatedAt));
            Timestamp(entity.Property(item => item.UpdatedAt));
            entity.HasIndex(item => new { item.WorkspaceId, item.OrderId });
        });

        modelBuilder.Entity<PaymentRecord>(entity =>
        {
            entity.ToTable("PaymentRecords", table =>
            {
                table.HasCheckConstraint("CK_PaymentRecords_BuyerType", ExactValues("BuyerType", "CONTACT", "ORGANIZATION_ACCOUNT"));
                table.HasCheckConstraint("CK_PaymentRecords_Kind", ExactValues("Kind", "PAYMENT", "REFUND"));
                table.HasCheckConstraint("CK_PaymentRecords_State", ExactValues("State", "CREATED", "PENDING", "PROCESSING", "SUCCEEDED", "FAILED", "CANCELLED", "EXPIRED", "REVERSED"));
                table.HasCheckConstraint("CK_PaymentRecords_Currency", CurrencyCheck("Currency"));
                table.HasCheckConstraint("CK_PaymentRecords_Channel", ExactValues("Channel", "BANK", "ONLINE_GATEWAY", "POS", "CARRIER", "OFFLINE", "EXTERNAL"));
                table.HasCheckConstraint("CK_PaymentRecords_EvidenceJson", "[EvidenceJson] IS NULL OR ISJSON([EvidenceJson]) = 1");
                table.HasCheckConstraint("CK_PaymentRecords_ReconciliationState", ExactValues("ReconciliationState", "UNRECONCILED", "MATCHED", "MISMATCH"));
                table.HasCheckConstraint("CK_PaymentRecords_CodCustomerCollectionState", $"[CodCustomerCollectionState] IS NULL OR ({ExactValues("CodCustomerCollectionState", "NOT_REQUESTED", "REQUESTED", "COLLECTED", "FAILED")})");
                table.HasCheckConstraint("CK_PaymentRecords_CodMerchantRemittanceState", $"[CodMerchantRemittanceState] IS NULL OR ({ExactValues("CodMerchantRemittanceState", "NOT_APPLICABLE", "PENDING", "REMITTED", "FAILED")})");
                table.HasCheckConstraint("CK_PaymentRecords_ResourceVersion", "[ResourceVersion] >= 0");
                table.HasCheckConstraint("CK_PaymentRecords_AllocationsJson", "ISJSON([AllocationsJson]) = 1");
                table.HasCheckConstraint("CK_PaymentRecords_RefundsJson", "ISJSON([RefundsJson]) = 1");
                table.HasCheckConstraint("CK_PaymentRecords_CustomerCreditsJson", "ISJSON([CustomerCreditsJson]) = 1");
                table.HasCheckConstraint("CK_PaymentRecords_UnallocatedCurrency", CurrencyCheck("UnallocatedCurrency"));
                table.HasCheckConstraint("CK_PaymentRecords_RefundableCurrency", CurrencyCheck("RefundableCurrency"));
            });
            entity.HasKey(item => new { item.WorkspaceId, item.PaymentRecordId });
            Id(entity.Property(item => item.WorkspaceId));
            Id(entity.Property(item => item.PaymentRecordId));
            entity.Property(item => item.BuyerType).HasMaxLength(32).IsRequired();
            Id(entity.Property(item => item.BuyerId));
            OptionalId(entity.Property(item => item.OrderId));
            OptionalId(entity.Property(item => item.PaymentIntentId));
            entity.Property(item => item.Kind).HasMaxLength(16).IsRequired();
            entity.Property(item => item.State).HasMaxLength(16).IsRequired();
            Money(entity.Property(item => item.Amount));
            Currency(entity.Property(item => item.Currency));
            entity.Property(item => item.MethodCode).HasMaxLength(100).IsRequired();
            entity.Property(item => item.Channel).HasMaxLength(32).IsRequired();
            entity.Property(item => item.ProviderCode).HasMaxLength(100);
            OptionalId(entity.Property(item => item.RefundOfPaymentRecordId));
            OptionalId(entity.Property(item => item.RefundOfCustomerCreditId));
            OptionalId(entity.Property(item => item.RefundIntentId));
            Timestamp(entity.Property(item => item.OccurredAt));
            entity.Property(item => item.ExternalReference).HasMaxLength(240);
            entity.Property(item => item.EvidenceJson).HasColumnType("nvarchar(max)");
            entity.Property(item => item.ReconciliationState).HasMaxLength(16).IsRequired();
            entity.Property(item => item.CodCustomerCollectionState).HasMaxLength(16);
            entity.Property(item => item.CodMerchantRemittanceState).HasMaxLength(16);
            Timestamp(entity.Property(item => item.CreatedAt));
            Timestamp(entity.Property(item => item.UpdatedAt));
            Json(entity.Property(item => item.AllocationsJson));
            Json(entity.Property(item => item.RefundsJson));
            Json(entity.Property(item => item.CustomerCreditsJson));
            Money(entity.Property(item => item.UnallocatedAmount));
            Currency(entity.Property(item => item.UnallocatedCurrency));
            Money(entity.Property(item => item.RefundableAmount));
            Currency(entity.Property(item => item.RefundableCurrency));
            entity.HasIndex(item => new { item.WorkspaceId, item.BuyerId });
        });

        // Payments-owned proof of successful disclosure. Separate from the AccessControl-owned
        // authorization and record decisions, which prove evaluation rather than disclosure.
        modelBuilder.Entity<PaymentReadAuditRecord>(entity =>
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
            OptionalId(entity.Property(item => item.RecordId));
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

    private static void Id(Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<string> property) => property.HasMaxLength(128).IsRequired();
    private static void OptionalId(Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<string?> property) => property.HasMaxLength(128);
    private static void Currency(Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<string> property) => property.HasMaxLength(3).IsFixedLength().IsRequired();
    private static void Json(Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<string> property) => property.HasColumnType("nvarchar(max)").IsRequired();
    private static void Money(Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<decimal> property) => property.HasPrecision(38, 6);
    private static void Timestamp(Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<DateTimeOffset> property) => property.HasPrecision(7);
    private static void Timestamp(Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<DateTimeOffset?> property) => property.HasPrecision(7);
    private static string CurrencyCheck(string column) => $"[{column}] LIKE '[A-Z][A-Z][A-Z]' COLLATE Latin1_General_100_BIN2 AND DATALENGTH([{column}]) = 6";
    private static string ExactValues(string column, params string[] values) => "(" + string.Join(" OR ", values.Select(value => $"([{column}] COLLATE Latin1_General_100_BIN2 = N'{value}' AND DATALENGTH([{column}]) = DATALENGTH(N'{value}'))")) + ")";
}
