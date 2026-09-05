using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Workflows.Atomic.Domain;

namespace UnicoreCRM.Workflows.Atomic.Infrastructure.Persistence;

/// <summary>
/// Workflows-owned coordination state in the <c>workflow</c> logical schema. It holds no business
/// state of any owner: only the anchor that lets a multi-owner qualification converge. No foreign
/// entity, table or navigation appears here.
/// </summary>
internal sealed class WorkflowsDbContext(DbContextOptions<WorkflowsDbContext> options) : DbContext(options)
{
    internal DbSet<LeadQualificationAnchor> LeadQualificationAnchors => Set<LeadQualificationAnchor>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("workflow");
        modelBuilder.Entity<LeadQualificationAnchor>(entity =>
        {
            entity.ToTable("LeadQualificationAnchors");
            // The workflow identity is the primary key, so two concurrent requests carrying the same
            // Idempotency-Key contend on the insert rather than both starting an execution.
            entity.HasKey(item => item.ScopeKey);
            entity.Property(item => item.ScopeKey).HasMaxLength(64);
            entity.Property(item => item.WorkspaceId).HasMaxLength(128).IsRequired();
            entity.Property(item => item.Workflow).HasMaxLength(96).IsRequired();
            entity.Property(item => item.LeadId).HasMaxLength(128).IsRequired();
            entity.Property(item => item.IdempotencyKey).HasMaxLength(128).IsRequired();
            entity.Property(item => item.Fingerprint).HasMaxLength(64).IsRequired();
            entity.Property(item => item.ExpectedLeadVersion);
            entity.Property(item => item.IntentVersion);
            entity.Property(item => item.ParticipantMemberId).HasMaxLength(128);
            entity.Property(item => item.TaskAssigneeId).HasMaxLength(128);
            entity.Property(item => item.CorrelationId).HasMaxLength(128);
            entity.Property(item => item.Stage).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(item => item.ContactId).HasMaxLength(128);
            entity.Property(item => item.ContactVersion);
            entity.Property(item => item.ContactWasCreated);
            entity.Property(item => item.ContactDisplayName).HasMaxLength(200);
            entity.Property(item => item.TaskId).HasMaxLength(128);
            entity.Property(item => item.TaskVersion);
            entity.Property(item => item.DealId).HasMaxLength(128);
            entity.Property(item => item.DealVersion);
            entity.Property(item => item.LeadVersion);
            entity.Property(item => item.ResponseJson).HasColumnType("nvarchar(max)");
            entity.Property(item => item.CreatedAt).HasPrecision(7);
            entity.Property(item => item.UpdatedAt).HasPrecision(7);
            entity.Property(item => item.RowVersion).IsRowVersion();
            entity.HasIndex(item => new { item.WorkspaceId, item.LeadId });
            // The resume scan: outstanding anchors, oldest first.
            entity.HasIndex(item => new { item.Stage, item.UpdatedAt });
        });
    }
}
