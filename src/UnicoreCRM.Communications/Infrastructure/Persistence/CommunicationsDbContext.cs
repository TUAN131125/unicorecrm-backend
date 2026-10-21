using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Communications.Domain;
using UnicoreCRM.Communications.Infrastructure.Telegram;

namespace UnicoreCRM.Communications.Infrastructure.Persistence;

internal sealed class CommunicationsDbContext(DbContextOptions<CommunicationsDbContext> options) : DbContext(options)
{
    internal DbSet<ChannelAccount> Accounts => Set<ChannelAccount>();
    internal DbSet<ExternalParticipant> Participants => Set<ExternalParticipant>();
    internal DbSet<Conversation> Conversations => Set<Conversation>();
    internal DbSet<Message> Messages => Set<Message>();
    internal DbSet<CommunicationAudit> Audit => Set<CommunicationAudit>();
    internal DbSet<ConversationRecordLink> RecordLinks => Set<ConversationRecordLink>();
    internal DbSet<ChannelMutation> ChannelMutations => Set<ChannelMutation>();
    internal DbSet<TelegramPollingState> TelegramPolling => Set<TelegramPollingState>();
    internal DbSet<TelegramAdmission> TelegramAdmissions => Set<TelegramAdmission>();
    internal DbSet<TelegramConnection> TelegramConnections => Set<TelegramConnection>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema("communications");
        model.Entity<TelegramConnection>(e =>
        {
            e.ToTable("TelegramConnections", t => t.HasCheckConstraint("CK_TelegramConnections_Generation", "[CredentialGeneration] >= 0"));
            e.HasKey(x => x.AccountId);
            e.Property(x => x.WorkspaceId).HasMaxLength(128).UseCollation("Latin1_General_100_BIN2");
            e.Property(x => x.Ciphertext).HasMaxLength(4096);
            e.Property(x => x.ReplayKeyCiphertext).HasMaxLength(4096);
            e.Property(x => x.LastError).HasMaxLength(64);
            e.Property(x => x.CredentialGeneration).IsConcurrencyToken();
            e.HasOne<ChannelAccount>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.AccountId })
                .HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        });
        model.Entity<TelegramPollingState>(e =>
        {
            e.ToTable("TelegramPolling", t => t.HasCheckConstraint("CK_TelegramPolling_Progress", "[BotId] > 0 AND [NextOffset] >= 0 AND [Fence] >= 0 AND [ConnectionGeneration] > 0 AND [FailureCount] >= 0"));
            e.HasKey(x => x.AccountId);
            e.Property(x => x.WorkspaceId).HasMaxLength(128).UseCollation("Latin1_General_100_BIN2");
            e.Property(x => x.FailureClassification).HasMaxLength(64);
            e.HasIndex(x => x.BotId).IsUnique(); // Telegram bot exclusivity, across Workspaces, fail closed
            e.HasIndex(x => new { x.RetryAfter, x.LastAttemptAt });
            e.HasOne<ChannelAccount>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.AccountId })
                .HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        });
        model.Entity<TelegramAdmission>(e =>
        {
            e.ToTable("TelegramAdmissions");
            e.HasKey(x => new { x.AccountId, x.UpdateId });
            e.Property(x => x.WorkspaceId).HasMaxLength(128).UseCollation("Latin1_General_100_BIN2");
            e.Property(x => x.Classification).HasMaxLength(32);
            e.HasOne<ChannelAccount>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.AccountId })
                .HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        });
        model.Entity<ChannelAccount>(e =>
        {
            e.ToTable("ChannelAccounts");
            e.HasKey(x => x.Id);
            e.Property(x => x.WorkspaceId).HasMaxLength(128).UseCollation("Latin1_General_100_BIN2");
            e.Property(x => x.LocalIdentity).HasMaxLength(128).UseCollation("Latin1_General_100_BIN2");
            e.HasAlternateKey(x => new { x.WorkspaceId, x.Id });
            e.HasIndex(x => new { x.WorkspaceId, x.LocalIdentity }).IsUnique();
            e.Property(x => x.ProviderKey).HasMaxLength(32).UseCollation("Latin1_General_100_BIN2");
            e.Property(x => x.ChannelType).HasMaxLength(16);
            e.Property(x => x.DisplayName).HasMaxLength(160);
            e.Property(x => x.Status).HasMaxLength(16).UseCollation("Latin1_General_100_BIN2");
            e.Property(x => x.ConfigurationVersion).IsConcurrencyToken();
            e.HasIndex(x => new { x.WorkspaceId, x.ProviderKey, x.Status, x.CreatedAt, x.Id });
            e.HasIndex(x => new { x.WorkspaceId, x.CreatedAt, x.Id }).IsDescending(false, true, true);
            e.ToTable("ChannelAccounts", t => t.HasCheckConstraint("CK_ChannelAccounts_Metadata",
                "([ProviderKey] IS NULL AND [ChannelType] IS NULL AND [DisplayName] IS NULL AND [Status] IS NULL AND [CreatedAt] IS NULL AND [UpdatedAt] IS NULL AND [ConfigurationVersion] = 0) OR ([ProviderKey] IS NOT NULL AND [ChannelType] IS NOT NULL AND [DisplayName] IS NOT NULL AND [Status] IS NOT NULL AND [Status] IN ('DRAFT','DISABLED') AND [CreatedAt] IS NOT NULL AND [UpdatedAt] IS NOT NULL AND [ConfigurationVersion] > 0)"));
        });
        model.Entity<ChannelMutation>(e =>
        {
            e.ToTable("ChannelMutations");
            e.HasKey(x => x.Id);
            e.Property(x => x.WorkspaceId).HasMaxLength(128).UseCollation("Latin1_General_100_BIN2");
            e.Property(x => x.ActorMemberId).HasMaxLength(128).UseCollation("Latin1_General_100_BIN2");
            e.Property(x => x.RequestKey).HasMaxLength(64).UseCollation("Latin1_General_100_BIN2");
            e.Property(x => x.Fingerprint).HasMaxLength(64);
            e.Property(x => x.Operation).HasMaxLength(32);
            e.Property(x => x.CorrelationId).HasMaxLength(128);
            e.Property(x => x.ResponseJson).HasMaxLength(4096);
            e.HasIndex(x => new { x.WorkspaceId, x.ActorMemberId, x.RequestKey }).IsUnique();
            e.HasIndex(x => new { x.WorkspaceId, x.AccountId, x.RecordedAt });
            e.HasOne<ChannelAccount>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.AccountId })
                .HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        });
        model.Entity<ExternalParticipant>(e =>
        {
            e.ToTable("ExternalParticipants");
            e.HasKey(x => x.Id);
            e.Property(x => x.WorkspaceId).HasMaxLength(128).UseCollation("Latin1_General_100_BIN2");
            e.Property(x => x.LocalIdentity).HasMaxLength(128).UseCollation("Latin1_General_100_BIN2");
            e.HasAlternateKey(x => new { x.WorkspaceId, x.AccountId, x.Id });
            e.HasIndex(x => new { x.WorkspaceId, x.AccountId, x.LocalIdentity }).IsUnique();
            e.HasOne<ChannelAccount>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.AccountId })
                .HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        });
        model.Entity<Conversation>(e =>
        {
            e.ToTable("Conversations", table => table.HasCheckConstraint("CK_Conversations_Status", "[Status] IN ('NEW','OPEN','PENDING','CLOSED')"));
            e.HasKey(x => x.Id);
            e.Property(x => x.WorkspaceId).HasMaxLength(128).UseCollation("Latin1_General_100_BIN2");
            e.Property(x => x.LocalIdentity).HasMaxLength(128).UseCollation("Latin1_General_100_BIN2");
            e.Property(x => x.Version).IsConcurrencyToken();
            e.Property(x => x.Status).HasMaxLength(16).UseCollation("Latin1_General_100_BIN2");
            e.HasIndex(x => new { x.WorkspaceId, x.AssigneeMemberId, x.Status, x.LastActivityAt, x.Id }).IsDescending(false, false, false, true, true);
            e.Property(x => x.AssigneeMemberId).HasMaxLength(128).UseCollation("Latin1_General_100_BIN2");
            e.HasIndex(x => new { x.WorkspaceId, x.AssigneeMemberId, x.LastActivityAt, x.Id }).IsDescending(false, false, true, true);
            e.HasAlternateKey(x => new { x.WorkspaceId, x.AccountId, x.Id });
            e.HasIndex(x => new { x.WorkspaceId, x.AccountId, x.LocalIdentity }).IsUnique();
            e.HasIndex(x => new { x.WorkspaceId, x.LastActivityAt, x.Id }).IsDescending(false, true, true);
            e.HasOne<ChannelAccount>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.AccountId })
                .HasPrincipalKey(x => new { x.WorkspaceId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        });
        model.Entity<Message>(e =>
        {
            e.ToTable("Messages");
            e.HasKey(x => x.Id);
            e.Property(x => x.WorkspaceId).HasMaxLength(128).UseCollation("Latin1_General_100_BIN2");
            e.Property(x => x.LocalIdentity).HasMaxLength(128).UseCollation("Latin1_General_100_BIN2");
            e.Property(x => x.Text).HasMaxLength(10000);
            e.HasIndex(x => new { x.WorkspaceId, x.AccountId, x.LocalIdentity }).IsUnique();
            e.HasIndex(x => new { x.WorkspaceId, x.ConversationId, x.Sequence }).IsUnique();
            e.HasOne<Conversation>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.AccountId, x.ConversationId })
                .HasPrincipalKey(x => new { x.WorkspaceId, x.AccountId, x.Id }).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<ExternalParticipant>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.AccountId, x.ParticipantId })
                .HasPrincipalKey(x => new { x.WorkspaceId, x.AccountId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        });
        model.Entity<ConversationRecordLink>(e =>
        {
            e.ToTable("ConversationRecordLinks", t => t.HasCheckConstraint("CK_RecordLinks_TargetType", "[TargetType] IN ('LEAD','CONTACT')"));
            e.HasKey(x => x.Id);
            e.Property(x => x.WorkspaceId).HasMaxLength(128).UseCollation("Latin1_General_100_BIN2");
            e.Property(x => x.TargetType).HasMaxLength(16).UseCollation("Latin1_General_100_BIN2");
            e.Property(x => x.TargetId).HasMaxLength(128).UseCollation("Latin1_General_100_BIN2");
            e.Property(x => x.LinkedBy).HasMaxLength(128);
            e.Property(x => x.UnlinkedBy).HasMaxLength(128);
            e.HasIndex(x => new { x.WorkspaceId, x.ConversationId }).IsUnique().HasFilter("[UnlinkedAt] IS NULL");
            e.HasOne<Conversation>().WithMany().HasForeignKey(x => new { x.WorkspaceId, x.AccountId, x.ConversationId })
                .HasPrincipalKey(x => new { x.WorkspaceId, x.AccountId, x.Id }).OnDelete(DeleteBehavior.NoAction);
        });
        model.Entity<CommunicationAudit>(e =>
        {
            e.ToTable("AuditRecords");
            e.HasKey(x => x.Id);
            e.Property(x => x.Operation).HasMaxLength(64);
            e.Property(x => x.PreviousStatus).HasMaxLength(16);
            e.Property(x => x.NewStatus).HasMaxLength(16);
            e.Property(x => x.PreviousTargetType).HasMaxLength(16);
            e.Property(x => x.NewTargetType).HasMaxLength(16);
            e.Property(x => x.PreviousTargetId).HasMaxLength(128);
            e.Property(x => x.NewTargetId).HasMaxLength(128);
            e.Property(x => x.ActorMemberId).HasMaxLength(128);
            e.Property(x => x.PreviousAssigneeMemberId).HasMaxLength(128);
            e.Property(x => x.NewAssigneeMemberId).HasMaxLength(128);
            e.Property(x => x.CorrelationId).HasMaxLength(128);
            e.Property(x => x.WorkspaceId).HasMaxLength(128).UseCollation("Latin1_General_100_BIN2");
            e.HasIndex(x => new { x.WorkspaceId, x.RecordedAt });
        });
    }
}
