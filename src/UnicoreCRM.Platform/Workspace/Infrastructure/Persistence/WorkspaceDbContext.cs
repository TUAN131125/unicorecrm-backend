using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Platform.Workspace.Domain;

namespace UnicoreCRM.Platform.Workspace.Infrastructure.Persistence;

internal sealed class WorkspaceDbContext(DbContextOptions<WorkspaceDbContext> options) : DbContext(options)
{
    internal DbSet<WorkspaceDefinition> Workspaces => Set<WorkspaceDefinition>();
    internal DbSet<WorkspaceMembership> Memberships => Set<WorkspaceMembership>();
    internal DbSet<WorkspaceBootstrapProjection> BootstrapProjections => Set<WorkspaceBootstrapProjection>();
    internal DbSet<WorkspaceAccessRecord> AccessRecords => Set<WorkspaceAccessRecord>();
    internal DbSet<InitialWorkspaceProvisioningRecord> InitialProvisioningRecords => Set<InitialWorkspaceProvisioningRecord>();
    internal DbSet<StudioConfiguration> StudioConfigurations => Set<StudioConfiguration>();
    internal DbSet<StudioQuickSetup> StudioQuickSetups => Set<StudioQuickSetup>();
    internal DbSet<StudioCommandRecord> StudioCommandRecords => Set<StudioCommandRecord>();
    internal DbSet<StudioConfigurationAudit> StudioConfigurationAudits => Set<StudioConfigurationAudit>();
    internal DbSet<StudioOutboxEvent> StudioOutboxEvents => Set<StudioOutboxEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("workspace");

        modelBuilder.Entity<WorkspaceDefinition>(entity =>
        {
            entity.ToTable("Workspaces");
            entity.HasKey(x => x.WorkspaceId);
            entity.Property(x => x.WorkspaceId).HasMaxLength(128);
            entity.Property(x => x.Key).HasMaxLength(120);
            entity.HasIndex(x => x.Key).IsUnique();
            entity.Property(x => x.Name).HasMaxLength(200);
            entity.Property(x => x.LogoText).HasMaxLength(8);
        });

        modelBuilder.Entity<WorkspaceMembership>(entity =>
        {
            entity.ToTable("Memberships");
            entity.HasKey(x => x.MembershipId);
            entity.Property(x => x.MembershipId).HasMaxLength(128);
            entity.Property(x => x.WorkspaceId).HasMaxLength(128);
            entity.Property(x => x.AccountId).HasMaxLength(64);
            entity.Property(x => x.MemberId).HasMaxLength(64);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            entity.HasIndex(x => new { x.WorkspaceId, x.AccountId }).IsUnique();
            entity.HasIndex(x => new { x.AccountId, x.MemberId, x.Status });
            entity.HasOne<WorkspaceDefinition>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WorkspaceBootstrapProjection>(entity =>
        {
            entity.ToTable("BootstrapProjections");
            entity.HasKey(x => x.WorkspaceId);
            entity.Property(x => x.WorkspaceId).HasMaxLength(128);
            entity.Property(x => x.Locale).HasMaxLength(2);
            entity.Property(x => x.TimeZone).HasMaxLength(100);
            entity.Property(x => x.BaseCurrency).HasMaxLength(3);
            entity.Property(x => x.CapabilitiesJson).HasColumnType("nvarchar(max)");
            entity.Property(x => x.EnabledModuleKeysJson).HasColumnType("nvarchar(max)");
            entity.Property(x => x.AvailableProductSpacesJson).HasColumnType("nvarchar(max)");
            entity.HasOne<WorkspaceDefinition>().WithOne().HasForeignKey<WorkspaceBootstrapProjection>(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<InitialWorkspaceProvisioningRecord>(entity =>
        {
            entity.ToTable("InitialProvisioningRecords");
            entity.HasKey(x => x.AccountId);
            entity.Property(x => x.AccountId).HasMaxLength(64);
            entity.Property(x => x.MemberId).HasMaxLength(64);
            entity.Property(x => x.WorkspaceId).HasMaxLength(128);
            entity.Property(x => x.MembershipId).HasMaxLength(128);
            entity.Property(x => x.IdempotencyKey).HasMaxLength(128);
            entity.Property(x => x.RequestFingerprint).HasMaxLength(64);
            entity.Property(x => x.State).HasConversion<string>().HasMaxLength(32);
            entity.HasIndex(x => x.WorkspaceId).IsUnique();
            entity.HasIndex(x => new { x.State, x.ProvisionedAt });
            entity.HasOne<WorkspaceDefinition>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WorkspaceAccessRecord>(entity =>
        {
            entity.ToTable("AccessRecords");
            entity.HasKey(x => x.AccessRecordId);
            entity.Property(x => x.AccessRecordId).HasMaxLength(128);
            entity.Property(x => x.Operation).HasMaxLength(96);
            entity.Property(x => x.AccountId).HasMaxLength(64);
            entity.Property(x => x.WorkspaceId).HasMaxLength(128);
            entity.Property(x => x.RequestId).HasMaxLength(128);
            entity.Property(x => x.CorrelationId).HasMaxLength(128);
            entity.Property(x => x.Outcome).HasMaxLength(32);
            entity.HasIndex(x => new { x.AccountId, x.OccurredAt });
        });

        modelBuilder.Entity<StudioConfiguration>(entity =>
        {
            entity.ToTable("StudioConfigurations");
            entity.HasKey(x => x.WorkspaceId);
            entity.Property(x => x.WorkspaceId).HasMaxLength(128);
            entity.Property(x => x.PublicationStatus).HasMaxLength(32);
            entity.Property(x => x.BusinessInformationJson).HasColumnType("nvarchar(max)");
            entity.Property(x => x.AddressesJson).HasColumnType("nvarchar(max)");
            entity.Property(x => x.LocaleRegionJson).HasColumnType("nvarchar(max)");
            entity.Property(x => x.BlueprintJson).HasColumnType("nvarchar(max)");
            entity.Property(x => x.FeaturesJson).HasColumnType("nvarchar(max)");
            entity.Property(x => x.UpdatedByMemberId).HasMaxLength(128);
            entity.HasOne<WorkspaceDefinition>().WithOne().HasForeignKey<StudioConfiguration>(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StudioQuickSetup>(entity =>
        {
            entity.ToTable("StudioQuickSetups");
            entity.HasKey(x => x.WorkspaceId);
            entity.Property(x => x.WorkspaceId).HasMaxLength(128);
            entity.Property(x => x.Status).HasMaxLength(32);
            entity.Property(x => x.CurrentStepId).HasMaxLength(64);
            entity.Property(x => x.CompletedStepIdsJson).HasColumnType("nvarchar(max)");
            entity.Property(x => x.SkippedStepIdsJson).HasColumnType("nvarchar(max)");
            entity.HasOne<WorkspaceDefinition>().WithOne().HasForeignKey<StudioQuickSetup>(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StudioCommandRecord>(entity =>
        {
            entity.ToTable("StudioCommandRecords");
            entity.HasKey(x => x.ScopeKey);
            entity.Property(x => x.ScopeKey).HasMaxLength(512);
            entity.Property(x => x.WorkspaceId).HasMaxLength(128);
            entity.Property(x => x.OperationId).HasMaxLength(160);
            entity.Property(x => x.IdempotencyKey).HasMaxLength(128);
            entity.Property(x => x.RequestFingerprint).HasMaxLength(64);
            entity.Property(x => x.ResponseJson).HasColumnType("nvarchar(max)");
            entity.HasIndex(x => new { x.WorkspaceId, x.OperationId, x.OccurredAt });
        });

        modelBuilder.Entity<StudioConfigurationAudit>(entity =>
        {
            entity.ToTable("StudioConfigurationAudits");
            entity.HasKey(x => x.AuditId);
            entity.Property(x => x.AuditId).HasMaxLength(128);
            entity.Property(x => x.WorkspaceId).HasMaxLength(128);
            entity.Property(x => x.Action).HasMaxLength(64);
            entity.Property(x => x.ActorMemberId).HasMaxLength(128);
            entity.Property(x => x.CorrelationId).HasMaxLength(128);
            entity.Property(x => x.Summary).HasMaxLength(1000);
            entity.HasIndex(x => new { x.WorkspaceId, x.OccurredAt });
        });

        modelBuilder.Entity<StudioOutboxEvent>(entity =>
        {
            entity.ToTable("StudioOutboxEvents");
            entity.HasKey(x => x.EventId);
            entity.Property(x => x.EventId).HasMaxLength(128);
            entity.Property(x => x.WorkspaceId).HasMaxLength(128);
            entity.Property(x => x.EventType).HasMaxLength(160);
            entity.Property(x => x.AggregateId).HasMaxLength(128);
            entity.Property(x => x.CorrelationId).HasMaxLength(128);
            entity.Property(x => x.PayloadJson).HasColumnType("nvarchar(max)");
            entity.HasIndex(x => new { x.PublishedAt, x.OccurredAt });
        });
    }
}
