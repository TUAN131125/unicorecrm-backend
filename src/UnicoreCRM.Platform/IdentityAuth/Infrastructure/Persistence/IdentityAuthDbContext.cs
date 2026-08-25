using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Platform.IdentityAuth.Domain;

namespace UnicoreCRM.Platform.IdentityAuth.Infrastructure.Persistence;

internal sealed class IdentityAuthDbContext(DbContextOptions<IdentityAuthDbContext> options) : DbContext(options)
{
    internal DbSet<IdentityAccount> Accounts => Set<IdentityAccount>();
    internal DbSet<IdentityCredential> Credentials => Set<IdentityCredential>();
    internal DbSet<IdentitySession> Sessions => Set<IdentitySession>();
    internal DbSet<IdentityEmailVerificationChallenge> EmailVerificationChallenges => Set<IdentityEmailVerificationChallenge>();
    internal DbSet<IdentityIdempotencyRecord> IdempotencyRecords => Set<IdentityIdempotencyRecord>();
    internal DbSet<IdentityAuditRecord> AuditRecords => Set<IdentityAuditRecord>();
    internal DbSet<IdentitySecurityEvent> SecurityEvents => Set<IdentitySecurityEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("iam");

        modelBuilder.Entity<IdentityAccount>(entity =>
        {
            entity.ToTable("Accounts");
            entity.HasKey(x => x.AccountId);
            entity.Property(x => x.AccountId).HasMaxLength(64);
            entity.Property(x => x.MemberId).HasMaxLength(64);
            entity.HasIndex(x => x.MemberId).IsUnique();
            entity.Property(x => x.Email).HasMaxLength(254);
            entity.Property(x => x.NormalizedEmail).HasMaxLength(254);
            entity.HasIndex(x => x.NormalizedEmail).IsUnique();
            entity.Property(x => x.DisplayName).HasMaxLength(160);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        });

        modelBuilder.Entity<IdentityCredential>(entity =>
        {
            entity.ToTable("Credentials");
            entity.HasKey(x => x.AccountId);
            entity.Property(x => x.AccountId).HasMaxLength(64);
            entity.Property(x => x.PasswordHash).HasMaxLength(1024);
            entity.HasOne<IdentityAccount>().WithOne().HasForeignKey<IdentityCredential>(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<IdentitySession>(entity =>
        {
            entity.ToTable("Sessions");
            entity.HasKey(x => x.SessionId);
            entity.Property(x => x.SessionId).HasMaxLength(64);
            entity.Property(x => x.AccountId).HasMaxLength(64);
            entity.HasIndex(x => x.AccountId);
            entity.Property(x => x.RefreshTokenHash).HasMaxLength(64);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.RevokeReason).HasMaxLength(500);
            entity.Property(x => x.DeviceId).HasMaxLength(64);
            entity.Property(x => x.DeviceLabel).HasMaxLength(160);
            entity.Property(x => x.UserAgent).HasMaxLength(512);
            entity.HasOne<IdentityAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<IdentityEmailVerificationChallenge>(entity =>
        {
            entity.ToTable("EmailVerificationChallenges");
            entity.HasKey(x => x.ChallengeId);
            entity.Property(x => x.ChallengeId).HasMaxLength(64);
            entity.Property(x => x.AccountId).HasMaxLength(64);
            entity.Property(x => x.CodeHash).HasMaxLength(64);
            // Serves the only query shape the owner performs: the outstanding challenges of one account.
            entity.HasIndex(x => new { x.AccountId, x.ConsumedAt, x.SupersededAt });
            entity.HasOne<IdentityAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<IdentityIdempotencyRecord>(entity =>
        {
            entity.ToTable("IdempotencyRecords");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Operation).HasMaxLength(96);
            entity.Property(x => x.Key).HasMaxLength(128);
            entity.Property(x => x.Fingerprint).HasMaxLength(64);
            entity.Property(x => x.ResourceId).HasMaxLength(64);
            entity.HasIndex(x => new { x.Operation, x.Key }).IsUnique();
        });

        modelBuilder.Entity<IdentityAuditRecord>(entity =>
        {
            entity.ToTable("AuditRecords");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Operation).HasMaxLength(96);
            entity.Property(x => x.Outcome).HasMaxLength(64);
            entity.Property(x => x.AccountId).HasMaxLength(64);
            entity.Property(x => x.CorrelationId).HasMaxLength(128);
        });

        modelBuilder.Entity<IdentitySecurityEvent>(entity =>
        {
            entity.ToTable("SecurityEvents");
            entity.HasKey(x => x.EventId);
            entity.Property(x => x.EventId).HasMaxLength(64);
            entity.Property(x => x.EventType).HasMaxLength(96);
            entity.Property(x => x.AccountId).HasMaxLength(64);
            entity.Property(x => x.CorrelationId).HasMaxLength(128);
        });
    }
}
