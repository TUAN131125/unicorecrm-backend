using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using UnicoreCRM.Crm.Organizations.Domain;

namespace UnicoreCRM.Crm.Organizations.Infrastructure.Persistence;

internal sealed class OrganizationsDbContext(DbContextOptions<OrganizationsDbContext> options) : DbContext(options)
{
    internal DbSet<Organization> Organizations => Set<Organization>();
    internal DbSet<OrganizationReadAuditRecord> ReadAuditRecords => Set<OrganizationReadAuditRecord>();
    internal DbSet<OrganizationIdempotencyRecord> IdempotencyRecords => Set<OrganizationIdempotencyRecord>();
    internal DbSet<OrganizationAuditRecord> AuditRecords => Set<OrganizationAuditRecord>();
    internal DbSet<OrganizationOutboxMessage> OutboxMessages => Set<OrganizationOutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("organizations");
        modelBuilder.Entity<Organization>(entity =>
        {
            entity.ToTable("Organizations");
            // Organization identity is always interpreted inside a trusted Workspace. A composite
            // key preserves that boundary structurally without inventing global cross-Workspace
            // uniqueness for the generic wire EntityId.
            entity.HasKey(item => new { item.WorkspaceId, item.OrganizationId });
            entity.Property(item => item.OrganizationId).HasMaxLength(128);
            entity.Property(item => item.WorkspaceId).HasMaxLength(128);
            entity.Property(item => item.DisplayName).HasMaxLength(200);
            entity.Property(item => item.Status).HasMaxLength(40);
            // Nullable by design: pre-O rows remain unowned rather than receiving a guessed owner.
            entity.Property(item => item.OwnerId).HasMaxLength(128);
            entity.Property(item => item.Version);
            entity.Property(item => item.UpdatedAt);
            entity.Property(item => item.Profile).HasConversion<OrganizationProfileValueConverter>().HasColumnType("nvarchar(max)");
            entity.HasIndex(item => new { item.WorkspaceId, item.CreatedAt, item.OrganizationId })
                .IsDescending(false, true, false);
            entity.HasIndex(item => new { item.WorkspaceId, item.OwnerId, item.CreatedAt, item.OrganizationId });
        });

        modelBuilder.Entity<OrganizationReadAuditRecord>(entity =>
        {
            entity.ToTable("ReadAuditRecords");
            entity.HasKey(item => item.AuditId);
            entity.Property(item => item.AuditId).HasMaxLength(128);
            entity.Property(item => item.Operation).HasMaxLength(128).IsRequired();
            entity.Property(item => item.WorkspaceId).HasMaxLength(128).IsRequired();
            entity.Property(item => item.ActorId).HasMaxLength(128).IsRequired();
            entity.Property(item => item.OrganizationId).HasMaxLength(128);
            entity.Property(item => item.RequestId).HasMaxLength(128).IsRequired();
            entity.Property(item => item.CorrelationId).HasMaxLength(128).IsRequired();
            entity.Property(item => item.OrganizationVersion);
            entity.Property(item => item.OccurredAt).HasPrecision(7);
            entity.HasIndex(item => new { item.WorkspaceId, item.OccurredAt });
            entity.HasIndex(item => new { item.WorkspaceId, item.OrganizationId, item.OccurredAt });
        });

        modelBuilder.Entity<OrganizationIdempotencyRecord>(entity => { entity.ToTable("IdempotencyRecords"); entity.HasKey(x => x.ScopeKey); entity.Property(x => x.ScopeKey).HasMaxLength(64); entity.Property(x => x.WorkspaceId).HasMaxLength(128); entity.Property(x => x.Operation).HasMaxLength(96); entity.Property(x => x.ActorId).HasMaxLength(128); entity.Property(x => x.TargetId).HasMaxLength(128); entity.Property(x => x.IdempotencyKey).HasMaxLength(128); entity.Property(x => x.Fingerprint).HasMaxLength(64); entity.Property(x => x.ResponseJson).HasColumnType("nvarchar(max)"); entity.Property(x => x.CreatedAt).HasPrecision(7); });
        modelBuilder.Entity<OrganizationAuditRecord>(entity => { entity.ToTable("AuditRecords"); entity.HasKey(x => x.AuditId); entity.Property(x => x.AuditId).HasMaxLength(128); entity.Property(x => x.Operation).HasMaxLength(96); entity.Property(x => x.WorkspaceId).HasMaxLength(128); entity.Property(x => x.ActorId).HasMaxLength(128); entity.Property(x => x.AggregateId).HasMaxLength(128); entity.Property(x => x.RequestId).HasMaxLength(128); entity.Property(x => x.CorrelationId).HasMaxLength(128); entity.Property(x => x.NewVersion); entity.Property(x => x.OccurredAt).HasPrecision(7); });
        modelBuilder.Entity<OrganizationOutboxMessage>(entity => { entity.ToTable("OutboxMessages"); entity.HasKey(x => x.EventId); entity.Property(x => x.EventId).HasMaxLength(128); entity.Property(x => x.EventType).HasMaxLength(100); entity.Property(x => x.AggregateId).HasMaxLength(128); entity.Property(x => x.WorkspaceId).HasMaxLength(128); entity.Property(x => x.CorrelationId).HasMaxLength(128); entity.Property(x => x.PayloadJson).HasColumnType("nvarchar(max)"); entity.Property(x => x.OccurredAt).HasPrecision(7); });
    }

    private sealed class OrganizationProfileValueConverter() : ValueConverter<OrganizationProfile, string>(
        value => JsonSerializer.Serialize(value, Serialization.Options),
        value => DeserializeProfile(value));

    private static OrganizationProfile DeserializeProfile(string value) =>
        JsonSerializer.Deserialize<OrganizationProfile>(value, Serialization.Options)
        ?? throw new InvalidOperationException("Persisted Organization profile is invalid.");

    private static class Serialization
    {
        internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    }
}
