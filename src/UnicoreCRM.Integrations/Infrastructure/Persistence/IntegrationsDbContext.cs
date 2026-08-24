using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Integrations.Domain;

namespace UnicoreCRM.Integrations.Infrastructure.Persistence;

internal sealed class IntegrationsDbContext(DbContextOptions<IntegrationsDbContext> options) : DbContext(options)
{
    internal DbSet<InboundIntegrationBinding> InboundBindings => Set<InboundIntegrationBinding>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("integration");
        modelBuilder.Entity<InboundIntegrationBinding>(entity =>
        {
            entity.ToTable("InboundBindings");
            entity.HasKey(item => item.IntegrationId);
            entity.Property(item => item.IntegrationId).HasMaxLength(128);
            entity.Property(item => item.ProviderCode).HasMaxLength(80);
            entity.Property(item => item.WorkspaceId).HasMaxLength(128);
            entity.Property(item => item.DelegatedMemberId).HasMaxLength(128);
            entity.Property(item => item.SecretReference).HasMaxLength(160);
            entity.HasIndex(item => new { item.WorkspaceId, item.IsEnabled });
        });
    }
}
