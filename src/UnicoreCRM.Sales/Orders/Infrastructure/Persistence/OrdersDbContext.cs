using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Sales.Orders.Domain;

namespace UnicoreCRM.Sales.Orders.Infrastructure.Persistence;

internal sealed class OrdersDbContext(DbContextOptions<OrdersDbContext> options) : DbContext(options)
{
    internal DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("orders");
        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable("Orders", table =>
            {
                table.HasCheckConstraint("CK_Orders_BuyerType", ExactValues("BuyerType", "CONTACT", "ORGANIZATION_ACCOUNT"));
                table.HasCheckConstraint("CK_Orders_State", ExactValues("State", "DRAFT", "CONFIRMED", "COMPLETED", "CANCELLED"));
                table.HasCheckConstraint("CK_Orders_ResourceVersion", "[ResourceVersion] >= 0");
                table.HasCheckConstraint("CK_Orders_LineItemsJson", "ISJSON([LineItemsJson]) = 1");
                table.HasCheckConstraint("CK_Orders_ActionsJson", "ISJSON([ActionsJson]) = 1");
                table.HasCheckConstraint("CK_Orders_AdjustmentsJson", "[AdjustmentsJson] IS NULL OR ISJSON([AdjustmentsJson]) = 1");
                table.HasCheckConstraint("CK_Orders_ShippingAddressJson", "[ShippingAddressJson] IS NULL OR ISJSON([ShippingAddressJson]) = 1");
                table.HasCheckConstraint("CK_Orders_CreditPolicyEvaluationJson", "[CreditPolicyEvaluationJson] IS NULL OR ISJSON([CreditPolicyEvaluationJson]) = 1");
                table.HasCheckConstraint("CK_Orders_CreditApprovalJson", "[CreditApprovalJson] IS NULL OR ISJSON([CreditApprovalJson]) = 1");
            });
            entity.HasKey(item => new { item.WorkspaceId, item.OrderId });
            entity.Property(item => item.WorkspaceId).HasMaxLength(128);
            entity.Property(item => item.OrderId).HasMaxLength(128);
            entity.Property(item => item.OrderNumber).HasMaxLength(120).IsRequired();
            entity.Property(item => item.OrderDate).HasColumnType("date");
            entity.Property(item => item.BuyerType).HasMaxLength(32).IsRequired();
            entity.Property(item => item.BuyerId).HasMaxLength(128).IsRequired();
            entity.Property(item => item.ContactId).HasMaxLength(128);
            entity.Property(item => item.SourceLeadId).HasMaxLength(128);
            entity.Property(item => item.SourceQuoteId).HasMaxLength(128);
            entity.Property(item => item.SourceQuoteNumber).HasMaxLength(120);
            entity.Property(item => item.SourceDealId).HasMaxLength(128);
            entity.Property(item => item.State).HasMaxLength(16).IsRequired();
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
            entity.Property(item => item.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
            Timestamp(entity.Property(item => item.ConfirmedAt));
            Timestamp(entity.Property(item => item.CompletedAt));
            Timestamp(entity.Property(item => item.CancelledAt));
            entity.Property(item => item.ExpectedDeliveryDate).HasColumnType("date");
            entity.Property(item => item.RecipientName).HasMaxLength(240);
            entity.Property(item => item.RecipientPhone).HasMaxLength(80);
            entity.Property(item => item.RecipientEmail).HasMaxLength(320);
            entity.Property(item => item.ShippingAddressJson).HasColumnType("nvarchar(max)");
            entity.Property(item => item.OwnerId).HasMaxLength(128);
            entity.Property(item => item.Notes).HasMaxLength(4000);
            entity.Property(item => item.CreditPolicyEvaluationJson).HasColumnType("nvarchar(max)");
            entity.Property(item => item.ActionsJson).HasColumnType("nvarchar(max)").IsRequired();
            Timestamp(entity.Property(item => item.ArchivedAt));
            entity.Property(item => item.ArchiveReason).HasMaxLength(500);
            entity.Property(item => item.ResourceVersion);
            Timestamp(entity.Property(item => item.CreatedAt));
            Timestamp(entity.Property(item => item.UpdatedAt));
            entity.Property(item => item.CreditApprovalJson).HasColumnType("nvarchar(max)");

            entity.HasIndex(item => new { item.WorkspaceId, item.UpdatedAt, item.OrderId })
                .IsDescending(false, true, true);
            entity.HasIndex(item => new { item.WorkspaceId, item.CreatedAt, item.OrderId });
            entity.HasIndex(item => new { item.WorkspaceId, item.OrderDate, item.OrderId });
            entity.HasIndex(item => new { item.WorkspaceId, item.GrandTotalAmount, item.OrderId });
            entity.HasIndex(item => new { item.WorkspaceId, item.OrderNumber, item.OrderId });
            entity.HasIndex(item => new { item.WorkspaceId, item.State });
            entity.HasIndex(item => new { item.WorkspaceId, item.SourceQuoteId });
            entity.HasIndex(item => new { item.WorkspaceId, item.SourceDealId });
            entity.HasIndex(item => new { item.WorkspaceId, item.BuyerType, item.BuyerId });
        });
    }

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
