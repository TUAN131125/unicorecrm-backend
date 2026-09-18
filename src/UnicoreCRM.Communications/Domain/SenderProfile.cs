namespace UnicoreCRM.Communications.Domain;

/// <summary>
/// A sender identity that a Workspace may expose to its members.
/// The profile owns presentation and policy-facing sender metadata only; OAuth secrets are
/// represented by an EmailConnection credential reference and never stored on this aggregate.
/// </summary>
internal sealed class SenderProfile
{
    private SenderProfile() { }

    internal SenderProfile(
        string workspaceId,
        string displayName,
        string emailAddress,
        SenderProfileType type,
        string? ownerMembershipId,
        string? replyToAddress,
        DateTimeOffset now)
    {
        SenderProfileId = CommunicationIds.New("sender");
        WorkspaceId = Require(workspaceId, nameof(workspaceId));
        DisplayName = Require(displayName, nameof(displayName));
        EmailAddress = NormalizeEmail(emailAddress, nameof(emailAddress));
        Type = type;
        OwnerMembershipId = NullIfWhiteSpace(ownerMembershipId);
        ReplyToAddress = replyToAddress is null ? null : NormalizeEmail(replyToAddress, nameof(replyToAddress));
        IsEnabled = true;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public string SenderProfileId { get; private set; } = null!;
    public string WorkspaceId { get; private set; } = null!;
    public string DisplayName { get; private set; } = null!;
    public string EmailAddress { get; private set; } = null!;
    public SenderProfileType Type { get; private set; }
    public string? OwnerMembershipId { get; private set; }
    public string? EmailConnectionId { get; private set; }
    public string? ReplyToAddress { get; private set; }
    public bool IsDefault { get; private set; }
    public bool IsEnabled { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public long Version { get; private set; }

    internal void LinkConnection(string connectionId, DateTimeOffset now)
    {
        EmailConnectionId = Require(connectionId, nameof(connectionId));
        Touch(now);
    }

    internal void SetDefault(bool isDefault, DateTimeOffset now)
    {
        IsDefault = isDefault;
        Touch(now);
    }

    internal void SetEnabled(bool isEnabled, DateTimeOffset now)
    {
        IsEnabled = isEnabled;
        Touch(now);
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }

    private static string Require(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value is required.", parameterName)
            : value.Trim();

    private static string NormalizeEmail(string value, string parameterName) =>
        Require(value, parameterName).ToLowerInvariant();

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
