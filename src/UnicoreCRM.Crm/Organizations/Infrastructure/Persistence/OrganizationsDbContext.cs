using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using UnicoreCRM.Crm.Organizations.Domain;

namespace UnicoreCRM.Crm.Organizations.Infrastructure.Persistence;

internal sealed class OrganizationsDbContext(DbContextOptions<OrganizationsDbContext> options) : DbContext(options)
{
    internal DbSet<Organization> Organizations => Set<Organization>();
    internal DbSet<OrganizationReadAuditRecord> ReadAuditRecords => Set<OrganizationReadAuditRecord>();

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
            entity.Property(item => item.Version);
            entity.Property(item => item.UpdatedAt);
            entity.Property(item => item.Profile).HasConversion<OrganizationProfileValueConverter>().HasColumnType("nvarchar(max)");
            entity.HasIndex(item => new { item.WorkspaceId, item.CreatedAt, item.OrganizationId })
                .IsDescending(false, true, false);
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
