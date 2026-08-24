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
            entity.HasIndex(x => x.WorkspaceId).IsUnique();
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
            entity.Property(x => x.CorrelationId).HasMaxLength(128);
            entity.HasIndex(x => new { x.AccountId, x.OccurredAt });
        });
    }
}
