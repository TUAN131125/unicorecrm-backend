using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Communications.Domain;

namespace UnicoreCRM.Communications.Infrastructure.Persistence;

/// <summary>
/// Communications persistence boundary. It owns sender profiles, provider connections and email
/// delivery records in the communications schema. References to Customers, SupportCases and
/// Memberships remain opaque identifiers; this context never reaches into another owner's tables.
/// </summary>
internal sealed class CommunicationsDbContext(DbContextOptions<CommunicationsDbContext> options)
    : DbContext(options)
{
    internal DbSet<SenderProfile> SenderProfiles => Set<SenderProfile>();
    internal DbSet<EmailConnection> EmailConnections => Set<EmailConnection>();
    internal DbSet<EmailMessage> EmailMessages => Set<EmailMessage>();
    internal DbSet<EmailRecipient> EmailRecipients => Set<EmailRecipient>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("communications");

        modelBuilder.Entity<EmailConnection>(entity =>
        {
            entity.ToTable("EmailConnections");
            entity.HasKey(item => item.EmailConnectionId);
            entity.HasAlternateKey(item => new { item.WorkspaceId, item.EmailConnectionId });

            entity.Property(item => item.EmailConnectionId).HasMaxLength(128);
            entity.Property(item => item.WorkspaceId).HasMaxLength(128);
            entity.Property(item => item.Provider).HasConversion<string>().HasMaxLength(32);
            entity.Property(item => item.EmailAddress).HasMaxLength(320);
            entity.Property(item => item.CredentialReference).HasMaxLength(512);
            entity.Property(item => item.OwnerMembershipId).HasMaxLength(128);
            entity.Property(item => item.ProviderAccountId).HasMaxLength(256);
            entity.Property(item => item.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(item => item.ConnectedAt).HasPrecision(7);
            entity.Property(item => item.UpdatedAt).HasPrecision(7);
            entity.Property(item => item.RevokedAt).HasPrecision(7);
            entity.Property(item => item.Version).IsConcurrencyToken();

            entity.HasIndex(item => new { item.WorkspaceId, item.Provider, item.EmailAddress }).IsUnique();
            entity.HasIndex(item => new { item.WorkspaceId, item.OwnerMembershipId, item.Status });
        });

        modelBuilder.Entity<SenderProfile>(entity =>
        {
            entity.ToTable("SenderProfiles");
            entity.HasKey(item => item.SenderProfileId);
            entity.HasAlternateKey(item => new { item.WorkspaceId, item.SenderProfileId });

            entity.Property(item => item.SenderProfileId).HasMaxLength(128);
            entity.Property(item => item.WorkspaceId).HasMaxLength(128);
            entity.Property(item => item.DisplayName).HasMaxLength(200);
            entity.Property(item => item.EmailAddress).HasMaxLength(320);
            entity.Property(item => item.Type).HasConversion<string>().HasMaxLength(32);
            entity.Property(item => item.OwnerMembershipId).HasMaxLength(128);
            entity.Property(item => item.EmailConnectionId).HasMaxLength(128);
            entity.Property(item => item.ReplyToAddress).HasMaxLength(320);
            entity.Property(item => item.CreatedAt).HasPrecision(7);
            entity.Property(item => item.UpdatedAt).HasPrecision(7);
            entity.Property(item => item.Version).IsConcurrencyToken();

            entity.HasOne<EmailConnection>()
                .WithMany()
                .HasForeignKey(item => new { item.WorkspaceId, item.EmailConnectionId })
                .HasPrincipalKey(item => new { item.WorkspaceId, item.EmailConnectionId })
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(item => new { item.WorkspaceId, item.EmailAddress }).IsUnique();
            entity.HasIndex(item => new { item.WorkspaceId, item.OwnerMembershipId, item.IsEnabled });
            entity.HasIndex(item => item.WorkspaceId)
                .IsUnique()
                .HasFilter("[IsDefault] = 1");
        });

        modelBuilder.Entity<EmailMessage>(entity =>
        {
            entity.ToTable("EmailMessages");
            entity.HasKey(item => item.EmailMessageId);
            entity.HasAlternateKey(item => new { item.WorkspaceId, item.EmailMessageId });

            entity.Property(item => item.EmailMessageId).HasMaxLength(128);
            entity.Property(item => item.WorkspaceId).HasMaxLength(128);
            entity.Property(item => item.SenderProfileId).HasMaxLength(128);
            entity.Property(item => item.ActorMembershipId).HasMaxLength(128);
            entity.Property(item => item.CustomerId).HasMaxLength(128);
            entity.Property(item => item.SupportCaseId).HasMaxLength(128);
            entity.Property(item => item.Subject).HasMaxLength(998);
            entity.Property(item => item.Body).HasColumnType("nvarchar(max)");
            entity.Property(item => item.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(item => item.ProviderMessageId).HasMaxLength(512);
            entity.Property(item => item.ProviderThreadId).HasMaxLength(512);
            entity.Property(item => item.FailureCode).HasMaxLength(100);
            entity.Property(item => item.FailureMessage).HasMaxLength(2000);
            entity.Property(item => item.CreatedAt).HasPrecision(7);
            entity.Property(item => item.UpdatedAt).HasPrecision(7);
            entity.Property(item => item.SentAt).HasPrecision(7);
            entity.Property(item => item.Version).IsConcurrencyToken();

            entity.HasOne<SenderProfile>()
                .WithMany()
                .HasForeignKey(item => new { item.WorkspaceId, item.SenderProfileId })
                .HasPrincipalKey(item => new { item.WorkspaceId, item.SenderProfileId })
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(item => new { item.WorkspaceId, item.CreatedAt, item.EmailMessageId });
            entity.HasIndex(item => new { item.WorkspaceId, item.CustomerId, item.CreatedAt });
            entity.HasIndex(item => new { item.WorkspaceId, item.SupportCaseId, item.CreatedAt });
            entity.HasIndex(item => new { item.WorkspaceId, item.Status, item.CreatedAt });
        });

        modelBuilder.Entity<EmailRecipient>(entity =>
        {
            entity.ToTable("EmailRecipients");
            entity.HasKey(item => item.EmailRecipientId);

            entity.Property(item => item.EmailRecipientId).HasMaxLength(128);
            entity.Property(item => item.WorkspaceId).HasMaxLength(128);
            entity.Property(item => item.EmailMessageId).HasMaxLength(128);
            entity.Property(item => item.Type).HasConversion<string>().HasMaxLength(16);
            entity.Property(item => item.EmailAddress).HasMaxLength(320);
            entity.Property(item => item.DisplayName).HasMaxLength(200);
            entity.Property(item => item.CreatedAt).HasPrecision(7);

            entity.HasOne<EmailMessage>()
                .WithMany()
                .HasForeignKey(item => new { item.WorkspaceId, item.EmailMessageId })
                .HasPrincipalKey(item => new { item.WorkspaceId, item.EmailMessageId })
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(item => new
            {
                item.WorkspaceId,
                item.EmailMessageId,
                item.Type,
                item.EmailAddress
            }).IsUnique();
        });
    }
}
