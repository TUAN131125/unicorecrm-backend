using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using UnicoreCRM.Sales.Products.Domain;

namespace UnicoreCRM.Sales.Products.Infrastructure.Persistence;

internal sealed class ProductsDbContext(DbContextOptions<ProductsDbContext> options) : DbContext(options)
{
    internal DbSet<Product> Products => Set<Product>();
    internal DbSet<ProductIdempotencyRecord> IdempotencyRecords => Set<ProductIdempotencyRecord>();
    internal DbSet<ProductAuditRecord> AuditRecords => Set<ProductAuditRecord>();
    internal DbSet<ProductOutboxMessage> OutboxMessages => Set<ProductOutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("products");

        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("Products");
            entity.HasKey(item => item.ProductId);
            entity.Property(item => item.ProductId).HasMaxLength(128);
            entity.Property(item => item.WorkspaceId).HasMaxLength(128);
            entity.Property(item => item.NormalizedSku).HasMaxLength(80);
            entity.Property(item => item.Profile).HasConversion<ProductProfileValueConverter>().HasColumnType("nvarchar(max)");
            entity.Property(item => item.ArchiveReason).HasMaxLength(1000);
            entity.Property(item => item.Version).IsConcurrencyToken();
            entity.HasIndex(item => new { item.WorkspaceId, item.NormalizedSku }).IsUnique();
            entity.HasIndex(item => new { item.WorkspaceId, item.CreatedAt, item.ProductId });
        });

        modelBuilder.Entity<ProductIdempotencyRecord>(entity =>
        {
            entity.ToTable("IdempotencyRecords");
            entity.HasKey(item => item.ScopeKey);
            entity.Property(item => item.ScopeKey).HasMaxLength(64);
            entity.Property(item => item.WorkspaceId).HasMaxLength(128);
            entity.Property(item => item.Operation).HasMaxLength(96);
            entity.Property(item => item.ActorId).HasMaxLength(128);
            entity.Property(item => item.TargetId).HasMaxLength(128);
            entity.Property(item => item.IdempotencyKey).HasMaxLength(128);
            entity.Property(item => item.Fingerprint).HasMaxLength(64);
            entity.Property(item => item.ResponseJson).HasColumnType("nvarchar(max)");
            entity.HasIndex(item => new { item.WorkspaceId, item.CreatedAt });
        });

        modelBuilder.Entity<ProductAuditRecord>(entity =>
        {
            entity.ToTable("AuditRecords");
            entity.HasKey(item => item.AuditId);
            entity.Property(item => item.AuditId).HasMaxLength(128);
            entity.Property(item => item.Operation).HasMaxLength(96);
            entity.Property(item => item.WorkspaceId).HasMaxLength(128);
            entity.Property(item => item.ActorId).HasMaxLength(128);
            entity.Property(item => item.AggregateId).HasMaxLength(128);
            entity.Property(item => item.RequestId).HasMaxLength(128);
            entity.Property(item => item.CorrelationId).HasMaxLength(128);
            entity.Property(item => item.Outcome).HasMaxLength(32);
            entity.HasIndex(item => new { item.WorkspaceId, item.OccurredAt });
            entity.HasIndex(item => new { item.AggregateId, item.OccurredAt });
        });

        modelBuilder.Entity<ProductOutboxMessage>(entity =>
        {
            entity.ToTable("OutboxMessages");
            entity.HasKey(item => item.EventId);
            entity.Property(item => item.EventId).HasMaxLength(128);
            entity.Property(item => item.EventType).HasMaxLength(100);
            entity.Property(item => item.AggregateId).HasMaxLength(128);
            entity.Property(item => item.WorkspaceId).HasMaxLength(128);
            entity.Property(item => item.CorrelationId).HasMaxLength(128);
            entity.Property(item => item.PayloadJson).HasColumnType("nvarchar(max)");
            entity.HasIndex(item => new { item.WorkspaceId, item.OccurredAt });
        });
    }

    private sealed class ProductProfileValueConverter() : ValueConverter<ProductProfile, string>(
        value => JsonSerializer.Serialize(value, Serialization.Options),
        value => DeserializeProfile(value));

    private static ProductProfile DeserializeProfile(string value) =>
        JsonSerializer.Deserialize<ProductProfile>(value, Serialization.Options)
        ?? throw new InvalidOperationException("Persisted Product profile is invalid.");

    private static class Serialization
    {
        internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    }
}
