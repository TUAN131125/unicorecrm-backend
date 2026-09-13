using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Integrations.Domain;
using UnicoreCRM.Integrations.Webhooks.Outbound.Domain;

namespace UnicoreCRM.Integrations.Infrastructure.Persistence;

internal sealed class IntegrationsDbContext(DbContextOptions<IntegrationsDbContext> options) : DbContext(options)
{
    internal DbSet<InboundIntegrationBinding> InboundBindings => Set<InboundIntegrationBinding>();
    internal DbSet<OutboundWebhookSubscription> OutboundWebhookSubscriptions => Set<OutboundWebhookSubscription>();
    internal DbSet<OutboundWebhookConsumerCursor> OutboundWebhookConsumerCursors => Set<OutboundWebhookConsumerCursor>();
    internal DbSet<OutboundWebhookDelivery> OutboundWebhookDeliveries => Set<OutboundWebhookDelivery>();
    internal DbSet<OutboundWebhookDeliveryAttempt> OutboundWebhookDeliveryAttempts => Set<OutboundWebhookDeliveryAttempt>();
    internal DbSet<OutboundWebhookIdempotency> OutboundWebhookIdempotency => Set<OutboundWebhookIdempotency>();
    internal DbSet<OutboundWebhookAudit> OutboundWebhookAudit => Set<OutboundWebhookAudit>();

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
        modelBuilder.Entity<OutboundWebhookSubscription>(e => { e.ToTable("OutboundWebhookSubscriptions"); e.HasKey(x => x.SubscriptionId); e.Property(x => x.SubscriptionId).HasMaxLength(128); e.Property(x => x.WorkspaceId).HasMaxLength(128); e.Property(x => x.Name).HasMaxLength(200); e.Property(x => x.EventType).HasMaxLength(160); e.Property(x => x.EndpointUrl).HasMaxLength(2048); e.Property(x => x.Status).HasMaxLength(16); e.Property(x => x.Version).IsConcurrencyToken(); e.Property(x => x.ProtectedSecret).HasColumnType("nvarchar(max)"); e.Property(x => x.CreatedBy).HasMaxLength(128); e.Property(x => x.CreatedAt).HasPrecision(7); e.Property(x => x.UpdatedBy).HasMaxLength(128); e.Property(x => x.UpdatedAt).HasPrecision(7); e.Property(x => x.ArchivedAt).HasPrecision(7); e.HasIndex(x => new { x.WorkspaceId, x.EventType, x.Status }); });
        modelBuilder.Entity<OutboundWebhookConsumerCursor>(e => { e.ToTable("OutboundWebhookConsumerCursors"); e.HasKey(x => x.ConsumerId); e.Property(x => x.ConsumerId).HasMaxLength(128); e.Property(x => x.LastProcessedSequence); e.Property(x => x.CreatedAt).HasPrecision(7); e.Property(x => x.UpdatedAt).HasPrecision(7); e.Property(x => x.RowVersion).IsRowVersion(); });
        modelBuilder.Entity<OutboundWebhookDelivery>(e => { e.ToTable("OutboundWebhookDeliveries"); e.HasKey(x => x.DeliveryId); e.Property(x => x.DeliveryId).HasMaxLength(128); e.Property(x => x.SubscriptionId).HasMaxLength(128); e.Property(x => x.WorkspaceId).HasMaxLength(128); e.Property(x => x.EventId).HasMaxLength(128); e.Property(x => x.EventSequence); e.Property(x => x.EventType).HasMaxLength(160); e.Property(x => x.CanonicalPayloadJson).HasColumnType("nvarchar(max)"); e.Property(x => x.Status).HasMaxLength(24); e.Property(x => x.AttemptCount); e.Property(x => x.NextAttemptAt).HasPrecision(7); e.Property(x => x.ExecutionAttemptId).HasMaxLength(64); e.Property(x => x.LeaseExpiresAt).HasPrecision(7); e.Property(x => x.LastAttemptAt).HasPrecision(7); e.Property(x => x.LastHttpStatus); e.Property(x => x.LastErrorCode).HasMaxLength(128); e.Property(x => x.CreatedAt).HasPrecision(7); e.Property(x => x.SucceededAt).HasPrecision(7); e.Property(x => x.DeadLetteredAt).HasPrecision(7); e.HasIndex(x => new { x.SubscriptionId, x.EventId }).IsUnique(); e.HasIndex(x => new { x.Status, x.NextAttemptAt, x.LeaseExpiresAt }); e.HasIndex(x => new { x.WorkspaceId, x.CreatedAt }); });
        modelBuilder.Entity<OutboundWebhookDeliveryAttempt>(e => { e.ToTable("OutboundWebhookDeliveryAttempts"); e.HasKey(x => x.AttemptId); e.Property(x => x.AttemptId).HasMaxLength(64); e.Property(x => x.DeliveryId).HasMaxLength(128); e.Property(x => x.AttemptNumber); e.Property(x => x.StartedAt).HasPrecision(7); e.Property(x => x.CompletedAt).HasPrecision(7); e.Property(x => x.Outcome).HasMaxLength(32); e.Property(x => x.HttpStatus); e.Property(x => x.ErrorCode).HasMaxLength(128); e.HasIndex(x => new { x.DeliveryId, x.AttemptNumber }).IsUnique(); });
        modelBuilder.Entity<OutboundWebhookIdempotency>(e => { e.ToTable("OutboundWebhookIdempotency"); e.HasKey(x => x.ScopeKey); e.Property(x => x.ScopeKey).HasMaxLength(64); e.Property(x => x.WorkspaceId).HasMaxLength(128); e.Property(x => x.Operation).HasMaxLength(96); e.Property(x => x.IdempotencyKey).HasMaxLength(128); e.Property(x => x.Fingerprint).HasMaxLength(64); e.Property(x => x.ResponseJson).HasColumnType("nvarchar(max)"); e.Property(x => x.CreatedAt).HasPrecision(7); });
        modelBuilder.Entity<OutboundWebhookAudit>(e => { e.ToTable("OutboundWebhookAudit"); e.HasKey(x => x.AuditId); e.Property(x => x.AuditId).HasMaxLength(128); e.Property(x => x.WorkspaceId).HasMaxLength(128); e.Property(x => x.ActorId).HasMaxLength(128); e.Property(x => x.Action).HasMaxLength(96); e.Property(x => x.TargetId).HasMaxLength(128); e.Property(x => x.CorrelationId).HasMaxLength(128); e.HasIndex(x => new { x.WorkspaceId, x.OccurredAt }); });
    }
}
