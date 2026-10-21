namespace UnicoreCRM.Communications.Domain;

internal sealed class ConversationRecordLink
{
    private ConversationRecordLink() { }
    internal ConversationRecordLink(Conversation conversation, string type, string target, string actor, DateTimeOffset at)
    {
        if (type is not ("LEAD" or "CONTACT")) throw new ArgumentException("COMMUNICATION_VALIDATION");
        WorkspaceId = conversation.WorkspaceId;
        AccountId = conversation.AccountId;
        ConversationId = conversation.Id;
        TargetType = type;
        TargetId = CommunicationValue.Required(target, 128);
        LinkedBy = actor;
        LinkedAt = at;
    }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string WorkspaceId { get; private set; } = "";
    public Guid AccountId { get; private set; }
    public long ConversationId { get; private set; }
    public string TargetType { get; private set; } = "";
    public string TargetId { get; private set; } = "";
    public DateTimeOffset LinkedAt { get; private set; }
    public string LinkedBy { get; private set; } = "";
    public DateTimeOffset? UnlinkedAt { get; private set; }
    public string? UnlinkedBy { get; private set; }
    internal void End(string actor, DateTimeOffset at)
    {
        if (UnlinkedAt is not null) throw new InvalidOperationException("COMMUNICATION_LINK_CONFLICT");
        UnlinkedAt = at;
        UnlinkedBy = actor;
    }
}
