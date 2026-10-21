namespace UnicoreCRM.Communications.Domain;

internal sealed partial class ChannelAccount
{
    // Null metadata denotes a pre-management account. Adoption requires a separate explicit operation.
    public string? ProviderKey { get; private set; }
    public string? ChannelType { get; private set; }
    public string? DisplayName { get; private set; }
    public string? Status { get; private set; }
    public long ConfigurationVersion { get; private set; }
    public DateTimeOffset? CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
    internal void InvalidateConnection(DateTimeOffset now)
    {
        ConfigurationVersion = checked(ConfigurationVersion + 1);
        UpdatedAt = now;
    }

    internal static ChannelAccount Draft(string workspace, string provider, string channelType, string name, DateTimeOffset now)
    {
        var account = new ChannelAccount(workspace, $"channel-{Guid.NewGuid():N}")
        {
            ProviderKey = provider, ChannelType = channelType,
            DisplayName = CommunicationValue.Required(name, 160), Status = "DRAFT",
            ConfigurationVersion = 1, CreatedAt = now, UpdatedAt = now
        };
        return account;
    }

    internal bool Configure(string? name, bool? disabled, DateTimeOffset now)
    {
        if (Status is not ("DRAFT" or "DISABLED")) throw new InvalidOperationException("CHANNEL_LEGACY_ACCOUNT");
        var nextName = name is null ? DisplayName : CommunicationValue.Required(name, 160);
        var nextStatus = disabled.HasValue ? disabled.Value ? "DISABLED" : "DRAFT" : Status;
        if (nextName == DisplayName && nextStatus == Status) return false;
        DisplayName = nextName;
        Status = nextStatus;
        ConfigurationVersion = checked(ConfigurationVersion + 1);
        UpdatedAt = now;
        return true;
    }
}

// One owner-local row serves both audit and durable mutation replay. It contains safe metadata only.
internal sealed class ChannelMutation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string WorkspaceId { get; set; } = "";
    public Guid AccountId { get; set; }
    public string ActorMemberId { get; set; } = "";
    public string RequestKey { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public string Operation { get; set; } = "";
    public long Version { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public string CorrelationId { get; set; } = "";
    public string ResponseJson { get; set; } = "";
}
