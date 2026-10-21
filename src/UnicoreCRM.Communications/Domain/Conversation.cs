namespace UnicoreCRM.Communications.Domain;

// Internal local identities only. No provider normalization, recipient or consent policy.
internal static class CommunicationValue
{
    internal static string Required(string value, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximum || value != value.Trim())
            throw new ArgumentException("COMMUNICATION_VALIDATION");
        return value;
    }
}

internal sealed partial class ChannelAccount
{
    private ChannelAccount() { }
    internal ChannelAccount(string workspaceId, string localIdentity)
    {
        WorkspaceId = CommunicationValue.Required(workspaceId, 128);
        LocalIdentity = CommunicationValue.Required(localIdentity, 128);
    }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string WorkspaceId { get; private set; } = "";
    public string LocalIdentity { get; private set; } = "";
    // No active/provider/credential state until account connection is admitted.
}

internal sealed class ExternalParticipant
{
    private ExternalParticipant() { }
    internal ExternalParticipant(ChannelAccount account, string localIdentity)
    {
        WorkspaceId = account.WorkspaceId;
        AccountId = account.Id;
        LocalIdentity = CommunicationValue.Required(localIdentity, 128);
    }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string WorkspaceId { get; private set; } = "";
    public Guid AccountId { get; private set; }
    public string LocalIdentity { get; private set; } = "";
}

internal sealed class Conversation
{
    internal static bool IsStatus(string status) => status is "NEW" or "OPEN" or "PENDING" or "CLOSED";
    private Conversation() { }
    internal Conversation(ChannelAccount account, string localIdentity, DateTimeOffset createdAt, string? assigneeMemberId = null)
    {
        WorkspaceId = account.WorkspaceId;
        AccountId = account.Id;
        LocalIdentity = CommunicationValue.Required(localIdentity, 128);
        LastActivityAt = createdAt.ToUniversalTime();
        AssigneeMemberId = assigneeMemberId is null ? null : CommunicationValue.Required(assigneeMemberId, 128);
    }
    public long Id { get; private set; }
    public string WorkspaceId { get; private set; } = "";
    public Guid AccountId { get; private set; }
    public string LocalIdentity { get; private set; } = "";
    public DateTimeOffset LastActivityAt { get; private set; }
    public long Version { get; private set; }
    public string? AssigneeMemberId { get; private set; }
    public string Status { get; private set; } = "NEW";

    internal void RecordLinkChange() => Version = checked(Version + 1);

    internal bool Transition(string target)
    {
        if (!IsStatus(target)) throw new ArgumentException("COMMUNICATION_VALIDATION");
        if (target == Status) return false;
        var allowed = (Status, target) is ("NEW", "OPEN") or ("NEW", "CLOSED")
            or ("OPEN", "PENDING") or ("OPEN", "CLOSED") or ("PENDING", "OPEN")
            or ("PENDING", "CLOSED") or ("CLOSED", "OPEN");
        if (!allowed) throw new InvalidOperationException("COMMUNICATION_TRANSITION_CONFLICT");
        Version = checked(Version + 1);
        Status = target;
        return true;
    }

    internal bool Claim(string memberId)
    {
        CommunicationValue.Required(memberId, 128);
        if (AssigneeMemberId == memberId) return false;
        if (AssigneeMemberId is not null) throw new InvalidOperationException("COMMUNICATION_CLAIM_CONFLICT");
        AssigneeMemberId = memberId;
        Version = checked(Version + 1);
        return true;
    }

    internal Message Append(ExternalParticipant participant, string localIdentity, string text, DateTimeOffset occurredAt)
    {
        if (Id == 0 || participant.AccountId != AccountId || participant.WorkspaceId != WorkspaceId)
            throw new InvalidOperationException("COMMUNICATION_BINDING_INVALID");
        var message = new Message(this, participant, localIdentity, text, occurredAt, checked(Version + 1));
        Version = message.Sequence;
        OnTrustedNewInboundAppend();
        if (message.OccurredAt > LastActivityAt) LastActivityAt = message.OccurredAt;
        return message;
    }

    private void OnTrustedNewInboundAppend()
    {
        if (Status is "PENDING" or "CLOSED") Status = "OPEN";
    }
}

internal sealed class Message
{
    private Message() { }
    internal Message(Conversation conversation, ExternalParticipant participant, string localIdentity,
        string text, DateTimeOffset occurredAt, long sequence)
    {
        WorkspaceId = conversation.WorkspaceId;
        AccountId = conversation.AccountId;
        ConversationId = conversation.Id;
        ParticipantId = participant.Id;
        LocalIdentity = CommunicationValue.Required(localIdentity, 128);
        if (string.IsNullOrWhiteSpace(text) || text.Length > 10000) throw new ArgumentException("COMMUNICATION_VALIDATION");
        Text = text; // preserve whitespace; local storage bound is not an Email size policy
        OccurredAt = occurredAt.ToUniversalTime();
        Sequence = sequence;
    }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string WorkspaceId { get; private set; } = "";
    public Guid AccountId { get; private set; }
    public long ConversationId { get; private set; }
    public Guid ParticipantId { get; private set; }
    public string LocalIdentity { get; private set; } = "";
    public long Sequence { get; private set; }
    public string Text { get; private set; } = "";
    public DateTimeOffset OccurredAt { get; private set; }
}

internal sealed record CommunicationAudit(Guid Id, string WorkspaceId, long ConversationId,
    Guid? MessageId, DateTimeOffset RecordedAt, string Operation = "MESSAGE_APPENDED",
    string? ActorMemberId = null, string? PreviousAssigneeMemberId = null,
    string? NewAssigneeMemberId = null, string? CorrelationId = null,
    string? PreviousStatus = null, string? NewStatus = null,
    string? PreviousTargetType = null, string? PreviousTargetId = null,
    string? NewTargetType = null, string? NewTargetId = null);
