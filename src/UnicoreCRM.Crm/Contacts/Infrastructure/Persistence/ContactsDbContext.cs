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
    internal DbSet<ContactIdempotencyRecord> IdempotencyRecords => Set<ContactIdempotencyRecord>();
    internal DbSet<ContactOrganizationRelationship> OrganizationRelationships => Set<ContactOrganizationRelationship>();
    internal DbSet<ContactCustomerRelationship> CustomerRelationships => Set<ContactCustomerRelationship>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("contacts");
        modelBuilder.Entity<Contact>(entity =>
        {
            entity.ToTable("Contacts");
            entity.HasKey(item => item.ContactId);
            entity.HasAlternateKey(item => new { item.WorkspaceId, item.ContactId });
            entity.Property(item => item.ContactId).HasMaxLength(128);
            entity.Property(item => item.WorkspaceId).HasMaxLength(128);
            entity.Property(item => item.OwnerId).HasMaxLength(128);
            entity.Property(item => item.FullName).HasMaxLength(ContactNameBound.MaxLength);
            entity.Property(item => item.Status).HasMaxLength(40);
            entity.Property(item => item.Version).IsConcurrencyToken();
            entity.Property(item => item.UpdatedAt);
            entity.Property(item => item.ArchivedAt).HasPrecision(7);
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

        modelBuilder.Entity<ContactOrganizationRelationship>(entity =>
        {
            entity.ToTable("OrganizationRelationships", table =>
            {
                table.HasCheckConstraint("CK_OrganizationRelationships_Role", "[Role] COLLATE Latin1_General_100_BIN2 IN (N'employee',N'executive',N'decision_maker',N'buyer',N'finance',N'technical',N'advisor',N'partner',N'other')");
                table.HasCheckConstraint("CK_OrganizationRelationships_EndState", "([EffectiveTo] IS NULL AND [EndedReason] IS NULL) OR ([EffectiveTo] IS NOT NULL AND LEN(LTRIM(RTRIM([EndedReason]))) > 0)");
                table.HasCheckConstraint("CK_OrganizationRelationships_DateRange", "[EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]");
            });
            entity.HasKey(item => item.RelationshipId);
            entity.Property(item => item.RelationshipId).HasMaxLength(128);
            entity.Property(item => item.WorkspaceId).HasMaxLength(128);
            entity.Property(item => item.ContactId).HasMaxLength(128);
            entity.Property(item => item.OrganizationId).HasMaxLength(128);
            entity.Property(item => item.Role).HasMaxLength(40);
            entity.Property(item => item.IsPrimaryAffiliation);
            entity.Property(item => item.EffectiveFrom).HasPrecision(7);
            entity.Property(item => item.EffectiveTo).HasPrecision(7);
            entity.Property(item => item.EndedReason).HasMaxLength(1000);
            entity.Property(item => item.CreatedAt).HasPrecision(7);
            entity.Property(item => item.CreatedBy).HasMaxLength(128);
            entity.Property(item => item.UpdatedAt).HasPrecision(7);
            entity.Property(item => item.UpdatedBy).HasMaxLength(128);
            entity.Property(item => item.LegacyEvidenceJson).HasColumnType("nvarchar(max)");
            entity.HasOne<Contact>().WithMany().HasForeignKey(item => new { item.WorkspaceId, item.ContactId })
                .HasPrincipalKey(item => new { item.WorkspaceId, item.ContactId }).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(item => new { item.WorkspaceId, item.ContactId, item.OrganizationId }).IsUnique().HasFilter("[EffectiveTo] IS NULL");
            entity.HasIndex(item => new { item.WorkspaceId, item.ContactId }).IsUnique().HasFilter("[EffectiveTo] IS NULL AND [IsPrimaryAffiliation] = 1");
            entity.HasIndex(item => new { item.WorkspaceId, item.ContactId, item.EffectiveTo, item.EffectiveFrom, item.RelationshipId });
            entity.HasIndex(item => new { item.WorkspaceId, item.OrganizationId, item.EffectiveTo });
        });

        modelBuilder.Entity<ContactCustomerRelationship>(entity =>
        {
            entity.ToTable("CustomerRelationships", table =>
            {
                table.HasCheckConstraint("CK_CustomerRelationships_Role", "[Role] COLLATE Latin1_General_100_BIN2 IN (N'primary_contact',N'billing',N'decision_maker',N'end_user',N'technical',N'support',N'other')");
                table.HasCheckConstraint("CK_CustomerRelationships_EndState", "([EffectiveTo] IS NULL AND [EndedReason] IS NULL) OR ([EffectiveTo] IS NOT NULL AND LEN(LTRIM(RTRIM([EndedReason]))) > 0)");
                table.HasCheckConstraint("CK_CustomerRelationships_DateRange", "[EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]");
            });
            entity.HasKey(item => item.RelationshipId);
            entity.Property(item => item.RelationshipId).HasMaxLength(128);
            entity.Property(item => item.WorkspaceId).HasMaxLength(128);
            entity.Property(item => item.ContactId).HasMaxLength(128);
            entity.Property(item => item.CustomerId).HasMaxLength(128);
            entity.Property(item => item.Role).HasMaxLength(40);
            entity.Property(item => item.EffectiveFrom).HasPrecision(7);
            entity.Property(item => item.EffectiveTo).HasPrecision(7);
            entity.Property(item => item.EndedReason).HasMaxLength(1000);
            entity.Property(item => item.CreatedAt).HasPrecision(7);
            entity.Property(item => item.CreatedBy).HasMaxLength(128);
            entity.Property(item => item.UpdatedAt).HasPrecision(7);
            entity.Property(item => item.UpdatedBy).HasMaxLength(128);
            entity.HasOne<Contact>().WithMany().HasForeignKey(item => new { item.WorkspaceId, item.ContactId })
                .HasPrincipalKey(item => new { item.WorkspaceId, item.ContactId }).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(item => new { item.WorkspaceId, item.ContactId, item.CustomerId }).IsUnique().HasFilter("[EffectiveTo] IS NULL");
            entity.HasIndex(item => new { item.WorkspaceId, item.CustomerId }).IsUnique().HasFilter("[EffectiveTo] IS NULL AND [Role] = N'primary_contact'");
            entity.HasIndex(item => new { item.WorkspaceId, item.ContactId, item.EffectiveTo, item.EffectiveFrom, item.RelationshipId });
            entity.HasIndex(item => new { item.WorkspaceId, item.CustomerId, item.EffectiveTo });
        });

        modelBuilder.Entity<ContactIdempotencyRecord>(entity =>
        {
            entity.ToTable("IdempotencyRecords");
            entity.HasKey(item => item.ScopeKey);
            entity.Property(item => item.ScopeKey).HasMaxLength(64);
            entity.Property(item => item.WorkspaceId).HasMaxLength(128).IsRequired();
            entity.Property(item => item.Operation).HasMaxLength(96).IsRequired();
            entity.Property(item => item.ActorId).HasMaxLength(128).IsRequired();
            entity.Property(item => item.TargetId).HasMaxLength(128).IsRequired();
            entity.Property(item => item.IdempotencyKey).HasMaxLength(128).IsRequired();
            entity.Property(item => item.Fingerprint).HasMaxLength(64).IsRequired();
            entity.Property(item => item.ResponseJson).HasColumnType("nvarchar(max)").IsRequired();
            entity.Property(item => item.CreatedAt).HasPrecision(7);
            entity.HasIndex(item => new { item.WorkspaceId, item.CreatedAt });
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
            entity.Property(item => item.ResultJson).HasColumnType("nvarchar(max)");
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
