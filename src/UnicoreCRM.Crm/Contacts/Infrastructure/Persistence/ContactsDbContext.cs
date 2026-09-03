using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using UnicoreCRM.Crm.Contacts.Domain;

namespace UnicoreCRM.Crm.Contacts.Infrastructure.Persistence;

internal sealed class ContactsDbContext(DbContextOptions<ContactsDbContext> options) : DbContext(options)
{
    internal DbSet<Contact> Contacts => Set<Contact>();
    internal DbSet<ContactReadAuditRecord> ReadAuditRecords => Set<ContactReadAuditRecord>();
    internal DbSet<ContactAuditRecord> AuditRecords => Set<ContactAuditRecord>();
    internal DbSet<ContactOutboxMessage> OutboxMessages => Set<ContactOutboxMessage>();
    internal DbSet<ContactConversionRecord> ConversionRecords => Set<ContactConversionRecord>();

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
            entity.Property(item => item.NormalizedWorkEmail).HasMaxLength(320);
            entity.Property(item => item.NormalizedPersonalEmail).HasMaxLength(320);
            entity.HasIndex(item => new { item.WorkspaceId, item.CreatedAt, item.ContactId });
            entity.HasIndex(item => new { item.WorkspaceId, item.OwnerId, item.CreatedAt, item.ContactId });
            // Detection indexes for the Workspace-wide duplicate guard. Deliberately NOT unique: no
            // authority makes email a Contact uniqueness invariant, the field is optional so many
            // keyless Contacts must coexist, and a constraint here would bind every future Contact
            // path - import, merge, migration - to a rule frozen only for the qualification
            // workflow. The concurrent-create race is closed by the SERIALIZABLE range lock these
            // seeks take, not by a constraint.
            entity.HasIndex(item => new { item.WorkspaceId, item.NormalizedWorkEmail });
            entity.HasIndex(item => new { item.WorkspaceId, item.NormalizedPersonalEmail });
        });

        modelBuilder.Entity<ContactAuditRecord>(entity =>
        {
            entity.ToTable("AuditRecords");
            entity.HasKey(item => item.AuditId);
            entity.Property(item => item.AuditId).HasMaxLength(128);
            entity.Property(item => item.Operation).HasMaxLength(96).IsRequired();
            entity.Property(item => item.WorkspaceId).HasMaxLength(128).IsRequired();
            entity.Property(item => item.ActorId).HasMaxLength(128).IsRequired();
            entity.Property(item => item.ActorType).HasMaxLength(32).IsRequired();
            entity.Property(item => item.AggregateId).HasMaxLength(128).IsRequired();
            entity.Property(item => item.RequestId).HasMaxLength(128).IsRequired();
            entity.Property(item => item.CorrelationId).HasMaxLength(128).IsRequired();
            entity.Property(item => item.Outcome).HasMaxLength(32).IsRequired();
            entity.Property(item => item.OccurredAt).HasPrecision(7);
            entity.HasIndex(item => new { item.WorkspaceId, item.OccurredAt });
            entity.HasIndex(item => new { item.AggregateId, item.OccurredAt });
        });

        modelBuilder.Entity<ContactOutboxMessage>(entity =>
        {
            entity.ToTable("OutboxMessages");
            entity.HasKey(item => item.EventId);
            entity.Property(item => item.EventId).HasMaxLength(128);
            entity.Property(item => item.EventType).HasMaxLength(100).IsRequired();
            entity.Property(item => item.AggregateId).HasMaxLength(128).IsRequired();
            entity.Property(item => item.WorkspaceId).HasMaxLength(128).IsRequired();
            entity.Property(item => item.CorrelationId).HasMaxLength(128).IsRequired();
            entity.Property(item => item.PayloadJson).HasColumnType("nvarchar(max)").IsRequired();
            entity.Property(item => item.OccurredAt).HasPrecision(7);
            entity.HasIndex(item => new { item.WorkspaceId, item.OccurredAt });
        });

        modelBuilder.Entity<ContactConversionRecord>(entity =>
        {
            entity.ToTable("ConversionRecords");
            entity.HasKey(item => item.ScopeKey);
            entity.Property(item => item.ScopeKey).HasMaxLength(64);
            entity.Property(item => item.WorkspaceId).HasMaxLength(128).IsRequired();
            entity.Property(item => item.ConversionKey).HasMaxLength(256).IsRequired();
            entity.Property(item => item.ContactId).HasMaxLength(128).IsRequired();
            entity.Property(item => item.CreatedAt).HasPrecision(7);
            entity.HasIndex(item => new { item.WorkspaceId, item.CreatedAt });
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
