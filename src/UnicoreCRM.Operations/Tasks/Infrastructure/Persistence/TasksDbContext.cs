using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Operations.Tasks.Domain;

namespace UnicoreCRM.Operations.Tasks.Infrastructure.Persistence;

internal sealed class TasksDbContext(DbContextOptions<TasksDbContext> options) : DbContext(options)
{
    internal DbSet<TaskItem> Tasks => Set<TaskItem>();
    internal DbSet<TaskActivity> Activities => Set<TaskActivity>();
    internal DbSet<TaskIdempotencyRecord> IdempotencyRecords => Set<TaskIdempotencyRecord>();
    internal DbSet<TaskAuditRecord> AuditRecords => Set<TaskAuditRecord>();
    internal DbSet<TaskOutboxMessage> OutboxMessages => Set<TaskOutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("tasks");

        modelBuilder.Entity<TaskItem>(entity =>
        {
            entity.ToTable("Tasks");
            entity.HasKey(item => item.TaskId);
            entity.Property(item => item.TaskId).HasMaxLength(128);
            entity.Property(item => item.WorkspaceId).HasMaxLength(128);
            entity.Property(item => item.Title).HasMaxLength(300);
            entity.Property(item => item.Description).HasMaxLength(4000);
            entity.Property(item => item.AssigneeId).HasMaxLength(128);
            entity.Property(item => item.RelationshipType).HasMaxLength(32);
            entity.Property(item => item.RelationshipId).HasMaxLength(128);
            entity.Property(item => item.RecordModuleKey).HasMaxLength(100);
            entity.Property(item => item.RecordId).HasMaxLength(128);
            entity.Property(item => item.RecordLabel).HasMaxLength(300);
            entity.Property(item => item.SourceType).HasMaxLength(100);
            entity.Property(item => item.SourceId).HasMaxLength(128);
            entity.Property(item => item.SourceEvidence).HasMaxLength(1000);
            entity.Property(item => item.DedupeKey).HasMaxLength(256);
            entity.Property(item => item.CancellationReason).HasMaxLength(2000);
            entity.Property(item => item.Outcome).HasMaxLength(4000);
            entity.Property(item => item.ArchiveReason).HasMaxLength(2000);
            entity.Property(item => item.Version).IsConcurrencyToken();
            entity.HasIndex(item => new { item.WorkspaceId, item.UpdatedAt, item.TaskId });
            entity.HasIndex(item => new { item.WorkspaceId, item.DueAt, item.TaskId });
            // The enforced OWN-scope predicate. ListTasks narrows by WorkspaceId and the
            // AccessControl scope assignee before counting and paging, and its default order is
            // UpdatedAt then TaskId, so this covers the security predicate, the count and the
            // ordered page in one seek instead of scanning the Workspace.
            entity.HasIndex(item => new { item.WorkspaceId, item.AssigneeId, item.UpdatedAt, item.TaskId });
        });

        modelBuilder.Entity<TaskActivity>(entity =>
        {
            entity.ToTable("Activities");
            entity.HasKey(item => item.ActivityId);
            entity.Property(item => item.ActivityId).HasMaxLength(128);
            entity.Property(item => item.WorkspaceId).HasMaxLength(128);
            entity.Property(item => item.Subject).HasMaxLength(300);
            entity.Property(item => item.Body).HasMaxLength(10000);
            entity.Property(item => item.ActorId).HasMaxLength(128);
            entity.Property(item => item.RelationshipType).HasMaxLength(32);
            entity.Property(item => item.RelationshipId).HasMaxLength(128);
            entity.Property(item => item.RecordModuleKey).HasMaxLength(100);
            entity.Property(item => item.RecordId).HasMaxLength(128);
            entity.Property(item => item.RecordLabel).HasMaxLength(300);
            entity.Property(item => item.SourceType).HasMaxLength(100);
            entity.Property(item => item.SourceId).HasMaxLength(128);
            entity.Property(item => item.SourceEvidence).HasMaxLength(1000);
            entity.HasIndex(item => new { item.WorkspaceId, item.OccurredAt, item.ActivityId });
        });

        modelBuilder.Entity<TaskIdempotencyRecord>(entity =>
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

        modelBuilder.Entity<TaskAuditRecord>(entity =>
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

        modelBuilder.Entity<TaskOutboxMessage>(entity =>
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
