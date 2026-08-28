using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using UnicoreCRM.Crm.Customers.Domain;

namespace UnicoreCRM.Crm.Customers.Infrastructure.Persistence;

internal sealed class CustomersDbContext(DbContextOptions<CustomersDbContext> options) : DbContext(options)
{
    internal DbSet<Customer> Customers => Set<Customer>();
    internal DbSet<CustomerReadAuditRecord> ReadAuditRecords => Set<CustomerReadAuditRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("customers");
        modelBuilder.Entity<Customer>(entity =>
        {
            entity.ToTable("Customers");
            entity.HasKey(item => new { item.WorkspaceId, item.CustomerId });
            entity.Property(item => item.WorkspaceId).HasMaxLength(128);
            entity.Property(item => item.CustomerId).HasMaxLength(128);
            entity.Property(item => item.CustomerCode).HasMaxLength(80).IsRequired();
            entity.Property(item => item.Type).HasMaxLength(8).IsRequired();
            entity.Property(item => item.RelationshipType).HasMaxLength(40).IsRequired();
            entity.Property(item => item.RelationshipId).HasMaxLength(128).IsRequired();
            entity.Property(item => item.Status).HasMaxLength(40).IsRequired();
            entity.Property(item => item.Health).HasMaxLength(16).IsRequired();
            entity.Property(item => item.Version);
            entity.Property(item => item.FirstPurchaseAt).HasPrecision(7);
            entity.Property(item => item.LastPurchaseAt).HasPrecision(7);
            entity.Property(item => item.CreatedAt).HasPrecision(7);
            entity.Property(item => item.UpdatedAt).HasPrecision(7);
            entity.Property(item => item.Profile).HasConversion<CustomerProfileValueConverter>().HasColumnType("nvarchar(max)");
            entity.HasIndex(item => new { item.WorkspaceId, item.RelationshipType, item.RelationshipId }).IsUnique();
            entity.HasIndex(item => new { item.WorkspaceId, item.CreatedAt, item.CustomerId })
                .IsDescending(false, true, false);
        });

        modelBuilder.Entity<CustomerReadAuditRecord>(entity =>
        {
            entity.ToTable("ReadAuditRecords");
            entity.HasKey(item => item.AuditId);
            entity.Property(item => item.AuditId).HasMaxLength(128);
            entity.Property(item => item.Operation).HasMaxLength(128).IsRequired();
            entity.Property(item => item.WorkspaceId).HasMaxLength(128).IsRequired();
            entity.Property(item => item.ActorId).HasMaxLength(128).IsRequired();
            entity.Property(item => item.CustomerId).HasMaxLength(128);
            entity.Property(item => item.RequestId).HasMaxLength(128).IsRequired();
            entity.Property(item => item.CorrelationId).HasMaxLength(128).IsRequired();
            entity.Property(item => item.CustomerVersion);
            entity.Property(item => item.OccurredAt).HasPrecision(7);
            entity.HasIndex(item => new { item.WorkspaceId, item.OccurredAt });
            entity.HasIndex(item => new { item.WorkspaceId, item.CustomerId, item.OccurredAt });
        });
    }

    private sealed class CustomerProfileValueConverter() : ValueConverter<CustomerProfile, string>(
        value => JsonSerializer.Serialize(value, Serialization.Options),
        value => DeserializeProfile(value));

    private static CustomerProfile DeserializeProfile(string value) =>
        JsonSerializer.Deserialize<CustomerProfile>(value, Serialization.Options)
        ?? throw new InvalidOperationException("Persisted Customer profile is invalid.");

    private static class Serialization
    {
        internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    }
}
