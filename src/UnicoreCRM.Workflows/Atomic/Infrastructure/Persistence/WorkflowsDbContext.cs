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
    internal DbSet<LeadCustomerConversionAnchor> LeadCustomerConversionAnchors => Set<LeadCustomerConversionAnchor>();

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
        modelBuilder.Entity<LeadCustomerConversionAnchor>(entity =>
        {
            entity.ToTable("LeadCustomerConversionAnchors"); entity.HasKey(x=>x.ScopeKey); entity.Property(x=>x.ScopeKey).HasMaxLength(64);
            entity.Property(x=>x.ConversionId).HasMaxLength(128).IsRequired(); entity.HasIndex(x=>x.ConversionId).IsUnique();
            foreach(var name in new[]{"WorkspaceId","LeadId","ConversionType","IdempotencyKey","OriginalAccountId","OriginalMemberId","OriginalMembershipId","CorrelationId","RequestId","SubjectType","SubjectMode","SelectedSubjectId","SubjectId","CustomerId","StakeholderRelationshipId","RecoveryExecutorId","FrozenLeadOwnerId"}) entity.Property(name).HasMaxLength(128);
            entity.Property(x=>x.ExpectedLeadVersion); entity.Property(x=>x.SubjectVersion); entity.Property(x=>x.SubjectCreated);
            entity.Property(x=>x.FrozenDoNotCall); entity.Property(x=>x.FrozenDoNotEmail); entity.Property(x=>x.CustomerVersion);
            entity.Property(x=>x.LeadVersion); entity.Property(x=>x.AttemptCount); entity.Property(x=>x.CreatedAt).HasPrecision(7);
            entity.Property(x=>x.UpdatedAt).HasPrecision(7); entity.Property(x=>x.CompletedAt).HasPrecision(7);
            entity.Property(x=>x.Fingerprint).HasMaxLength(64).IsRequired(); entity.Property(x=>x.NewContactJson).HasColumnType("nvarchar(max)"); entity.Property(x=>x.StakeholderJson).HasColumnType("nvarchar(max)"); entity.Property(x=>x.ResponseJson).HasColumnType("nvarchar(max)");
            entity.Property(x=>x.EmittedEventIdsJson).HasColumnType("nvarchar(max)"); entity.Property(x=>x.AuditEvidenceIdsJson).HasColumnType("nvarchar(max)");
            entity.Property(x=>x.CustomerResolution).HasMaxLength(16); entity.Property(x=>x.LastErrorCategory).HasMaxLength(32); entity.Property(x=>x.LastErrorCode).HasMaxLength(128);
            entity.Property(x=>x.Stage).HasConversion<string>().HasMaxLength(40); entity.Property(x=>x.RowVersion).IsRowVersion();
            entity.HasIndex(x=>new{x.WorkspaceId,x.LeadId,x.ConversionType}).IsUnique(); entity.HasIndex(x=>new{x.Stage,x.NextRetryAt,x.UpdatedAt});
        });
    }
}
