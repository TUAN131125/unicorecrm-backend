using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Sales.Quotes.Domain;
namespace UnicoreCRM.Sales.Quotes.Infrastructure.Persistence;

internal static class QuoteSalesAcceptanceMapping
{
    internal static void MapSalesAcceptance(this ModelBuilder b)
    {
        b.Entity<QuoteSalesAcceptance>(e=>
        {
            e.ToTable("SalesAcceptances",t=>
            {
                t.HasCheckConstraint("CK_SalesAcceptances_Source","[Source] IN ('CUSTOMER_LINK','STAFF_RECORDED')");
                t.HasCheckConstraint("CK_SalesAcceptances_Assurance","[AssuranceLevel] = 'ORDINARY_SALES_UNVERIFIED'");
                t.HasCheckConstraint("CK_SalesAcceptances_Snapshot","ISJSON([SnapshotJson]) = 1");
            });
            e.HasKey(x=>new{x.WorkspaceId,x.AcceptanceId});
            e.Property(x=>x.WorkspaceId).HasMaxLength(128); e.Property(x=>x.AcceptanceId).HasMaxLength(128);
            e.Property(x=>x.QuoteId).HasMaxLength(128); e.HasIndex(x=>new{x.WorkspaceId,x.QuoteId}).IsUnique();
            e.Property(x=>x.Source).HasMaxLength(32); e.Property(x=>x.AssuranceLevel).HasMaxLength(64);
            e.Property(x=>x.RecordedBy).HasMaxLength(128); e.Property(x=>x.Channel).HasMaxLength(32);
            e.Property(x=>x.Evidence).HasMaxLength(4000); e.Property(x=>x.DocumentId).HasMaxLength(128);
            e.Property(x=>x.CommercialFingerprint).HasMaxLength(64); e.Property(x=>x.PolicyVersion).HasMaxLength(128);
            e.Property(x=>x.AutoConfirmRequested).HasDefaultValue(false);
            e.Property(x=>x.ConversionStatus).HasMaxLength(32); e.Property(x=>x.OrderId).HasMaxLength(128);
            e.Property(x=>x.LastErrorCode).HasMaxLength(128); e.Property(x=>x.SnapshotJson).HasColumnType("nvarchar(max)");
            e.HasIndex(x=>new{x.ConversionStatus,x.NextAttemptAt});
            b.Entity<Quote>().HasOne(x=>x.SalesAcceptance).WithOne().HasForeignKey<QuoteSalesAcceptance>(x=>new{x.WorkspaceId,x.QuoteId})
                .OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<QuoteAcceptanceInvitation>(e=>
        {
            e.ToTable("AcceptanceInvitations"); e.HasKey(x=>x.InvitationId);
            foreach(var key in new[]{"InvitationId","WorkspaceId","QuoteId","DocumentId","CreatedBy"})e.Property<string>(key).HasMaxLength(128);
            e.Property(x=>x.TokenHash).HasMaxLength(64); e.HasIndex(x=>x.TokenHash).IsUnique();
            e.Property(x=>x.CommercialFingerprint).HasMaxLength(64); e.Property(x=>x.RecipientEmail).HasMaxLength(320);
            e.Property(x=>x.DeliveryStatus).HasMaxLength(32); e.Property(x=>x.DeliveryErrorCode).HasMaxLength(128);
            e.Property(x=>x.ProtectedDeliveryPayload).HasColumnType("nvarchar(max)");
            e.HasIndex(x=>new{x.WorkspaceId,x.QuoteId});
        });
    }
}
