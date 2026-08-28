using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using UnicoreCRM.Crm.Contacts.Domain;

namespace UnicoreCRM.Crm.Contacts.Infrastructure.Persistence;

internal sealed class ContactsDbContext(DbContextOptions<ContactsDbContext> options) : DbContext(options)
{
    internal DbSet<Contact> Contacts => Set<Contact>();
    internal DbSet<ContactReadAuditRecord> ReadAuditRecords => Set<ContactReadAuditRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("contacts");
        modelBuilder.Entity<Contact>(entity =>
        {
            entity.ToTable("Contacts");
            entity.HasKey(item => item.ContactId);
            entity.Property(item => item.ContactId).HasMaxLength(128);
            entity.Property(item => item.WorkspaceId).HasMaxLength(128);
            entity.Property(item => item.OwnerId).HasMaxLength(128);
            entity.Property(item => item.FullName).HasMaxLength(200);
            entity.Property(item => item.Status).HasMaxLength(40);
            entity.Property(item => item.Version);
            entity.Property(item => item.UpdatedAt);
            entity.Property(item => item.Profile).HasConversion<ContactProfileValueConverter>().HasColumnType("nvarchar(max)");
            entity.HasIndex(item => new { item.WorkspaceId, item.CreatedAt, item.ContactId });
            entity.HasIndex(item => new { item.WorkspaceId, item.OwnerId, item.CreatedAt, item.ContactId });
        });

        modelBuilder.Entity<ContactReadAuditRecord>(entity =>
        {
            entity.ToTable("ReadAuditRecords");
            entity.HasKey(item => item.AuditId);
            entity.Property(item => item.AuditId).HasMaxLength(128);
            entity.Property(item => item.Operation).HasMaxLength(128).IsRequired();
            entity.Property(item => item.WorkspaceId).HasMaxLength(128).IsRequired();
            entity.Property(item => item.ActorId).HasMaxLength(128).IsRequired();
            entity.Property(item => item.ContactId).HasMaxLength(128);
            entity.Property(item => item.RequestId).HasMaxLength(128).IsRequired();
            entity.Property(item => item.CorrelationId).HasMaxLength(128).IsRequired();
            entity.Property(item => item.ContactVersion);
            entity.Property(item => item.OccurredAt).HasPrecision(7);
            entity.HasIndex(item => new { item.WorkspaceId, item.OccurredAt });
            entity.HasIndex(item => new { item.ContactId, item.OccurredAt });
        });
    }

    private sealed class ContactProfileValueConverter() : ValueConverter<ContactProfile, string>(
        value => JsonSerializer.Serialize(value, Serialization.Options),
        value => DeserializeProfile(value));

    private static ContactProfile DeserializeProfile(string value) =>
        JsonSerializer.Deserialize<ContactProfile>(value, Serialization.Options)
        ?? throw new InvalidOperationException("Persisted Contact profile is invalid.");

    private static class Serialization
    {
        internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    }
}
