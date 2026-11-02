using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using UnicoreCRM.Crm.Deals.Domain;

namespace UnicoreCRM.Crm.Deals.Infrastructure.Persistence;

internal sealed class DealsDbContext(DbContextOptions<DealsDbContext> options) : DbContext(options)
{
    internal DbSet<Deal> Deals => Set<Deal>();
    internal DbSet<DealPipeline> Pipelines => Set<DealPipeline>();
    internal DbSet<DealIdempotencyRecord> IdempotencyRecords => Set<DealIdempotencyRecord>();
    internal DbSet<DealAuditRecord> AuditRecords => Set<DealAuditRecord>();
    internal DbSet<DealOutboxMessage> OutboxMessages => Set<DealOutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("deals");

        modelBuilder.Entity<DealPipeline>(entity =>
        {
            entity.ToTable("Pipelines", table => table.HasCheckConstraint("CK_Pipelines_DefaultActive", "[IsDefault] = 0 OR [IsActive] = 1"));
            entity.HasKey(item => new { item.WorkspaceId, item.PipelineId });
            entity.Property(item => item.WorkspaceId).HasMaxLength(128);
            entity.Property(item => item.PipelineId).HasMaxLength(128);
            entity.Property(item => item.NameVi).HasMaxLength(200);
            entity.Property(item => item.NameEn).HasMaxLength(200);
            entity.Property(item => item.Description).HasMaxLength(2000);
            entity.Property(item => item.UpdatedBy).HasMaxLength(128);
            entity.Property(item => item.Version).IsConcurrencyToken();
            entity.HasIndex(item => item.WorkspaceId).IsUnique().HasFilter("[IsDefault] = 1 AND [IsActive] = 1");
            entity.OwnsMany(item => item.Stages, stages =>
            {
                stages.ToTable("PipelineStages");
                stages.WithOwner().HasForeignKey("WorkspaceId", "PipelineId");
                stages.Property<string>("WorkspaceId").HasMaxLength(128);
                stages.Property<string>("PipelineId").HasMaxLength(128);
                stages.Property(item => item.StageId).HasMaxLength(128);
                stages.HasKey("WorkspaceId", "PipelineId", nameof(DealPipelineStage.StageId));
                stages.Property(item => item.Code).HasMaxLength(120);
                stages.HasIndex("WorkspaceId", "PipelineId", nameof(DealPipelineStage.Code)).IsUnique();
                stages.Property(item => item.LabelVi).HasMaxLength(200);
                stages.Property(item => item.LabelEn).HasMaxLength(200);
                stages.Property(item => item.Color).HasMaxLength(16);
                stages.Property(item => item.Category).HasConversion<string>().HasMaxLength(16);
                stages.Property(item => item.ForecastFloor).HasConversion<string>().HasMaxLength(24);
            });
        });

        modelBuilder.Entity<Deal>(entity =>
        {
            entity.ToTable("Deals");
            entity.HasKey(item => item.DealId);
            entity.Property(item => item.DealId).HasMaxLength(128);
            entity.Property(item => item.WorkspaceId).HasMaxLength(128);
            entity.Property(item => item.PipelineId).HasMaxLength(128);
            entity.HasOne<DealPipeline>().WithMany().HasForeignKey(item => new { item.WorkspaceId, item.PipelineId }).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(item => new { item.WorkspaceId, item.PipelineId, item.StageCode });
            entity.Property(item => item.ScopeOwnerId).HasMaxLength(128);
            entity.Property(item => item.QualificationSourceLeadId).HasMaxLength(128);
            entity.Property(item => item.Profile).HasConversion<DealProfileValueConverter>().HasColumnType("nvarchar(max)");
            entity.Property(item => item.StageCode).HasMaxLength(120);
            entity.Property(item => item.StageCategory).HasConversion<string>().HasMaxLength(16);
            entity.Property(item => item.ForecastCategory).HasConversion<string>().HasMaxLength(24);
            var forecastHistory = entity.Property(item => item.ForecastHistory)
                .HasConversion<ForecastHistoryValueConverter>()
                .HasColumnType("nvarchar(max)");
            forecastHistory.Metadata.SetValueComparer(new ValueComparer<IReadOnlyList<DealForecastHistory>>(
                (left, right) => left!.SequenceEqual(right!),
                value => value.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode())),
                value => value.ToArray()));
            entity.Property(item => item.NextActionSummary).HasMaxLength(1000);
            entity.Property(item => item.NextActionType).HasMaxLength(16);
            entity.Property(item => item.NextActionId).HasMaxLength(128);
            entity.Property(item => item.WinEvidenceType).HasMaxLength(32);
            entity.Property(item => item.WinEvidenceSourceId).HasMaxLength(128);
            entity.HasIndex(item => new { item.WorkspaceId, item.WinEvidenceType, item.WinEvidenceSourceId });
            entity.Property(item => item.LostReason).HasMaxLength(500);
            entity.Property(item => item.LostReasonNote).HasMaxLength(2000);
            entity.Property(item => item.RecycleDecision).HasConversion<string>().HasMaxLength(32);
            entity.Property(item => item.ArchiveReason).HasMaxLength(500);
            entity.Property(item => item.Version).IsConcurrencyToken();
            entity.HasIndex(item => new { item.WorkspaceId, item.UpdatedAt, item.DealId });
            // The enforced OWN-scope predicate. ReadDealsAsync narrows by WorkspaceId and the
            // AccessControl scope owner; Deals then orders and pages in memory, so UpdatedAt and
            // DealId are carried for covering rather than for ordering - the leading two columns
            // are what makes the security predicate a seek instead of a Workspace scan.
            entity.HasIndex(item => new { item.WorkspaceId, item.ScopeOwnerId, item.UpdatedAt, item.DealId });
            entity.HasIndex(item => new { item.WorkspaceId, item.StageCategory, item.StageCode });
            entity.HasIndex(item => new { item.WorkspaceId, item.QualificationSourceLeadId })
                .IsUnique()
                .HasFilter("[QualificationSourceLeadId] IS NOT NULL");
        });

        modelBuilder.Entity<DealIdempotencyRecord>(entity =>
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

        modelBuilder.Entity<DealAuditRecord>(entity =>
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

        modelBuilder.Entity<DealOutboxMessage>(entity =>
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

    private sealed class DealProfileValueConverter() : ValueConverter<DealProfile, string>(
        value => JsonSerializer.Serialize(value, Serialization.Options),
        value => DeserializeProfile(value));

    private sealed class ForecastHistoryValueConverter() : ValueConverter<IReadOnlyList<DealForecastHistory>, string>(
        value => JsonSerializer.Serialize(value, Serialization.Options),
        value => DeserializeHistory(value));

    private static DealProfile DeserializeProfile(string value) =>
        JsonSerializer.Deserialize<DealProfile>(value, Serialization.Options)
        ?? throw new InvalidOperationException("Persisted Deal profile is invalid.");

    private static IReadOnlyList<DealForecastHistory> DeserializeHistory(string value) =>
        JsonSerializer.Deserialize<IReadOnlyList<DealForecastHistory>>(value, Serialization.Options)
        ?? throw new InvalidOperationException("Persisted Deal forecast history is invalid.");

    private static class Serialization
    {
        internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    }
}
