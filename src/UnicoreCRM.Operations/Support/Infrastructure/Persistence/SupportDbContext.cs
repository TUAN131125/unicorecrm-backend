using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using UnicoreCRM.Operations.Support.Domain;

namespace UnicoreCRM.Operations.Support.Infrastructure.Persistence;

/// <summary>
/// The Support owner persistence boundary. It maps only Support-owned state into the
/// <c>support</c> logical schema. It holds no navigation property, foreign key or query that
/// reaches Tasks, CRM, Sales or any other owner.
/// </summary>
internal sealed class SupportDbContext(DbContextOptions<SupportDbContext> options) : DbContext(options)
{
    private static readonly JsonSerializerOptions TagOptions = new(JsonSerializerDefaults.Web);

    internal DbSet<SupportCase> Cases => Set<SupportCase>();
    internal DbSet<SupportCaseComment> Comments => Set<SupportCaseComment>();
    internal DbSet<SupportIdempotencyRecord> IdempotencyRecords => Set<SupportIdempotencyRecord>();
    internal DbSet<SupportAuditRecord> AuditRecords => Set<SupportAuditRecord>();
    internal DbSet<SupportOutboxMessage> OutboxMessages => Set<SupportOutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("support");

        modelBuilder.Entity<SupportCase>(entity =>
        {
            entity.ToTable("SupportCases");
            entity.HasKey(item => item.CaseId);
            entity.Property(item => item.CaseId).HasMaxLength(128);
            entity.Property(item => item.WorkspaceId).HasMaxLength(128);
            entity.Property(item => item.CaseNumber).HasMaxLength(128);
            entity.Property(item => item.Title).HasMaxLength(300);
            entity.Property(item => item.Description).HasMaxLength(10000);
            entity.Property(item => item.RelationshipType).HasMaxLength(32);
            entity.Property(item => item.RelationshipId).HasMaxLength(128);
            entity.Property(item => item.ContactId).HasMaxLength(128);
            entity.Property(item => item.RelatedOrderId).HasMaxLength(128);
            entity.Property(item => item.RelatedProductId).HasMaxLength(128);
            entity.Property(item => item.RelatedOwnedProductId).HasMaxLength(128);
            entity.Property(item => item.OwnerId).HasMaxLength(128);
            entity.Property(item => item.ResolutionSummary).HasMaxLength(4000);
            entity.Property(item => item.Tags)
                .HasConversion(
                    value => JsonSerializer.Serialize(value, TagOptions),
                    value => JsonSerializer.Deserialize<string[]>(value, TagOptions) ?? Array.Empty<string>(),
                    new ValueComparer<IReadOnlyList<string>>(
                        (left, right) => left != null && right != null && left.SequenceEqual(right),
                        value => value.Aggregate(0, (hash, entry) => HashCode.Combine(hash, entry.GetHashCode(StringComparison.Ordinal))),
                        value => value.ToArray()))
                .HasColumnType("nvarchar(max)");
            entity.Property(item => item.Version).IsConcurrencyToken();
            // The human-readable case number is unique inside its owning Workspace.
            entity.HasIndex(item => new { item.WorkspaceId, item.CaseNumber }).IsUnique();
            entity.HasIndex(item => new { item.WorkspaceId, item.CaseYear, item.CaseSequence });
            entity.HasIndex(item => new { item.WorkspaceId, item.UpdatedAt, item.CaseId });
            entity.HasIndex(item => new { item.WorkspaceId, item.Status, item.CaseId });
        });

        modelBuilder.Entity<SupportCaseComment>(entity =>
        {
            entity.ToTable("SupportCaseComments");
            entity.HasKey(item => item.CommentId);
            entity.Property(item => item.CommentId).HasMaxLength(128);
            entity.Property(item => item.WorkspaceId).HasMaxLength(128);
            entity.Property(item => item.CaseId).HasMaxLength(128);
            entity.Property(item => item.Body).HasMaxLength(10000);
            entity.Property(item => item.AuthorId).HasMaxLength(128);
            entity.HasIndex(item => new { item.WorkspaceId, item.CaseId, item.CreatedAt });
        });

        modelBuilder.Entity<SupportIdempotencyRecord>(entity =>
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

        modelBuilder.Entity<SupportAuditRecord>(entity =>
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

        modelBuilder.Entity<SupportOutboxMessage>(entity =>
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
}
