using Microsoft.EntityFrameworkCore;
using UnicoreCRM.PlatformOperations.Inbox.Domain;

namespace UnicoreCRM.PlatformOperations.Inbox.Infrastructure.Persistence;

internal sealed class InboxDbContext(DbContextOptions<InboxDbContext> options) : DbContext(options)
{
    internal DbSet<InboxMessage> Messages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("ops");
        modelBuilder.Entity<InboxMessage>(entity =>
        {
            entity.ToTable("InboxMessages");
            entity.HasKey(item => item.InboxMessageId);
            entity.Property(item => item.InboxMessageId).HasMaxLength(128);
            entity.Property(item => item.IntegrationId).HasMaxLength(128);
            entity.Property(item => item.DeliveryId).HasMaxLength(128);
            entity.Property(item => item.PayloadHash).HasMaxLength(64);
            entity.Property(item => item.ProviderCode).HasMaxLength(80);
            entity.Property(item => item.WorkspaceId).HasMaxLength(128);
            entity.Property(item => item.DelegatedMemberId).HasMaxLength(128);
            entity.Property(item => item.CorrelationId).HasMaxLength(128);
            entity.Property(item => item.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(item => item.ResultLeadId).HasMaxLength(128);
            entity.Property(item => item.LastResultCode).HasMaxLength(96);
            entity.HasIndex(item => new { item.IntegrationId, item.DeliveryId }).IsUnique();
            entity.HasIndex(item => new { item.Status, item.UpdatedAt });
        });
    }
}
