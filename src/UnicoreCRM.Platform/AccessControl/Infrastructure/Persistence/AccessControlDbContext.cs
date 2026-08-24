using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Platform.AccessControl.Domain;

namespace UnicoreCRM.Platform.AccessControl.Infrastructure.Persistence;

internal sealed class AccessControlDbContext(DbContextOptions<AccessControlDbContext> options) : DbContext(options)
{
    internal DbSet<AccessRole> Roles => Set<AccessRole>();
    internal DbSet<RoleCapability> RoleCapabilities => Set<RoleCapability>();
    internal DbSet<MembershipRoleAssignment> MembershipRoleAssignments => Set<MembershipRoleAssignment>();
    internal DbSet<RoleDataScopePolicy> RoleDataScopes => Set<RoleDataScopePolicy>();
    internal DbSet<RoleFieldSecurityPolicy> RoleFieldSecurity => Set<RoleFieldSecurityPolicy>();
    internal DbSet<AuthorizationDecisionRecord> AuthorizationDecisions => Set<AuthorizationDecisionRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("access");

        modelBuilder.Entity<AccessRole>(entity =>
        {
            entity.ToTable("Roles");
            entity.HasKey(x => x.RoleId);
            entity.HasAlternateKey(x => new { x.RoleId, x.WorkspaceId });
            entity.Property(x => x.RoleId).HasMaxLength(128);
            entity.Property(x => x.WorkspaceId).HasMaxLength(128);
            entity.Property(x => x.Name).HasMaxLength(160);
            entity.Property(x => x.Description).HasMaxLength(500);
            entity.Property(x => x.SourceTemplateId).HasMaxLength(160);
            entity.HasIndex(x => new { x.WorkspaceId, x.Name }).IsUnique();
        });

        modelBuilder.Entity<RoleCapability>(entity =>
        {
            entity.ToTable("RoleCapabilities");
            entity.HasKey(x => new { x.RoleId, x.Capability });
            entity.Property(x => x.RoleId).HasMaxLength(128);
            entity.Property(x => x.Capability).HasMaxLength(160);
            entity.HasOne<AccessRole>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MembershipRoleAssignment>(entity =>
        {
            entity.ToTable("MembershipRoleAssignments");
            entity.HasKey(x => x.AssignmentId);
            entity.Property(x => x.AssignmentId).HasMaxLength(128);
            entity.Property(x => x.WorkspaceId).HasMaxLength(128);
            entity.Property(x => x.MembershipId).HasMaxLength(128);
            entity.Property(x => x.RoleId).HasMaxLength(128);
            entity.HasIndex(x => new { x.WorkspaceId, x.MembershipId, x.RoleId }).IsUnique();
            entity.HasOne<AccessRole>()
                .WithMany()
                .HasForeignKey(x => new { x.RoleId, x.WorkspaceId })
                .HasPrincipalKey(x => new { x.RoleId, x.WorkspaceId })
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RoleDataScopePolicy>(entity =>
        {
            entity.ToTable("RoleDataScopes");
            entity.HasKey(x => x.PolicyId);
            entity.Property(x => x.PolicyId).HasMaxLength(128);
            entity.Property(x => x.RoleId).HasMaxLength(128);
            entity.Property(x => x.ResourceKey).HasMaxLength(160);
            entity.Property(x => x.Scope).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.AllowedOwnerIdsJson).HasColumnType("nvarchar(max)");
            entity.HasIndex(x => new { x.RoleId, x.ResourceKey }).IsUnique();
            entity.HasOne<AccessRole>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RoleFieldSecurityPolicy>(entity =>
        {
            entity.ToTable("RoleFieldSecurity");
            entity.HasKey(x => x.PolicyId);
            entity.Property(x => x.PolicyId).HasMaxLength(128);
            entity.Property(x => x.RoleId).HasMaxLength(128);
            entity.Property(x => x.ResourceKey).HasMaxLength(160);
            entity.Property(x => x.FieldKey).HasMaxLength(160);
            entity.Property(x => x.Access).HasConversion<string>().HasMaxLength(32);
            entity.HasIndex(x => new { x.RoleId, x.ResourceKey, x.FieldKey }).IsUnique();
            entity.HasOne<AccessRole>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AuthorizationDecisionRecord>(entity =>
        {
            entity.ToTable("AuthorizationDecisions");
            entity.HasKey(x => x.DecisionId);
            entity.Property(x => x.DecisionId).HasMaxLength(128);
            entity.Property(x => x.WorkspaceId).HasMaxLength(128);
            entity.Property(x => x.MembershipId).HasMaxLength(128);
            entity.Property(x => x.RequiredCapability).HasMaxLength(160);
            entity.Property(x => x.CorrelationId).HasMaxLength(128);
            entity.HasIndex(x => new { x.WorkspaceId, x.MembershipId, x.EvaluatedAt });
        });
    }
}
