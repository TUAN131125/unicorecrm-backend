using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using UnicoreCRM.Crm.Leads.Domain;

namespace UnicoreCRM.Crm.Leads.Infrastructure.Persistence;

internal sealed class LeadsDbContext(DbContextOptions<LeadsDbContext> options) : DbContext(options)
{
    internal DbSet<Lead> Leads => Set<Lead>();
    internal DbSet<LeadIdempotencyRecord> IdempotencyRecords => Set<LeadIdempotencyRecord>();
    internal DbSet<LeadAuditRecord> AuditRecords => Set<LeadAuditRecord>();
    internal DbSet<LeadOutboxMessage> OutboxMessages => Set<LeadOutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("leads");

        modelBuilder.Entity<Lead>(entity =>
        {
            entity.ToTable("Leads");
            entity.HasKey(item => item.LeadId);
            entity.Property(item => item.LeadId).HasMaxLength(128);
            entity.Property(item => item.WorkspaceId).HasMaxLength(128);
            entity.Property(item => item.ScopeOwnerId).HasMaxLength(128);
            entity.Property(item => item.Profile).HasConversion<LeadProfileValueConverter>().HasColumnType("nvarchar(max)");
            entity.Property(item => item.DisqualifiedBy).HasMaxLength(128);
            entity.Property(item => item.DisqualificationReason).HasMaxLength(2000);
            entity.Property(item => item.DisqualificationEvidence).HasMaxLength(4000);
            entity.Property(item => item.Version).IsConcurrencyToken();
            entity.HasIndex(item => new { item.WorkspaceId, item.UpdatedAt, item.LeadId });
        });

        modelBuilder.Entity<LeadIdempotencyRecord>(entity =>
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

        modelBuilder.Entity<LeadAuditRecord>(entity =>
        {
            entity.ToTable("AuditRecords");
            entity.HasKey(item => item.AuditId);
            entity.Property(item => item.AuditId).HasMaxLength(128);
            entity.Property(item => item.Operation).HasMaxLength(96);
            entity.Property(item => item.WorkspaceId).HasMaxLength(128);
            entity.Property(item => item.ActorId).HasMaxLength(128);
            entity.Property(item => item.ActorType).HasMaxLength(32);
            entity.Property(item => item.DelegatedSubjectId).HasMaxLength(128);
            entity.Property(item => item.SourceReference).HasMaxLength(128);
            entity.Property(item => item.AggregateId).HasMaxLength(128);
            entity.Property(item => item.RequestId).HasMaxLength(128);
            entity.Property(item => item.CorrelationId).HasMaxLength(128);
            entity.Property(item => item.Outcome).HasMaxLength(32);
            entity.HasIndex(item => new { item.WorkspaceId, item.OccurredAt });
            entity.HasIndex(item => new { item.AggregateId, item.OccurredAt });
            entity.HasIndex(item => new { item.ActorType, item.ActorId, item.OccurredAt });
        });

        modelBuilder.Entity<LeadOutboxMessage>(entity =>
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

    private sealed class LeadProfileValueConverter() : ValueConverter<LeadProfile, string>(
        value => JsonSerializer.Serialize(value, Serialization.Options),
        value => DeserializeProfile(value));

    private static LeadProfile DeserializeProfile(string value) =>
        JsonSerializer.Deserialize<LeadProfile>(value, Serialization.Options)
        ?? throw new InvalidOperationException("Persisted Lead profile is invalid.");

    private static class Serialization
    {
        internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    }
}
