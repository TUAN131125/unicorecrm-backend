namespace UnicoreCRM.Communications.Infrastructure.Telegram;

// Provider infrastructure state; never an alternative ChannelAccount or Message store.
internal sealed class TelegramPollingState
{
    public Guid AccountId { get; set; }
    public string WorkspaceId { get; set; } = "";
    public long BotId { get; set; }
    public bool Synthetic { get; set; }
    public bool Active { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset AdmittedAt { get; set; }
    public long ConnectionGeneration { get; set; }
    public long NextOffset { get; set; }
    public long Fence { get; set; }
    public Guid? LeaseOwner { get; set; }
    public DateTimeOffset? LeaseExpiry { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public DateTimeOffset? LastSuccessfulPollAt { get; set; }
    public DateTimeOffset? LastCommittedUpdateAt { get; set; }
    public string? FailureClassification { get; set; }
    public int FailureCount { get; set; }
    public DateTimeOffset? RetryAfter { get; set; }
}

internal sealed class TelegramAdmission
{
    public Guid AccountId { get; set; }
    public long UpdateId { get; set; }
    public string WorkspaceId { get; set; } = "";
    public string Classification { get; set; } = "";
    public DateTimeOffset CommittedAt { get; set; }
}
