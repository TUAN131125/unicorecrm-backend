namespace UnicoreCRM.Operations.Tasks.Domain;

internal sealed class TaskActivity
{
    private TaskActivity() { }

    internal TaskActivity(
        string workspaceId,
        ActivityType type,
        string subject,
        string? body,
        string actorId,
        TaskReferenceData references,
        DateTimeOffset occurredAt)
    {
        ActivityId = TaskIds.New("activity");
        WorkspaceId = workspaceId;
        Type = type;
        Subject = subject;
        Body = body;
        ActorId = actorId;
        OccurredAt = occurredAt;
        RelationshipType = references.RelationshipType;
        RelationshipId = references.RelationshipId;
        RecordModuleKey = references.RecordModuleKey;
        RecordId = references.RecordId;
        RecordLabel = references.RecordLabel;
        SourceType = references.SourceType;
        SourceId = references.SourceId;
        SourceEvidence = references.SourceEvidence;
    }

    public string ActivityId { get; private set; } = null!;
    public string WorkspaceId { get; private set; } = null!;
    public ActivityType Type { get; private set; }
    public string Subject { get; private set; } = null!;
    public string? Body { get; private set; }
    public string ActorId { get; private set; } = null!;
    public DateTimeOffset OccurredAt { get; private set; }
    public string? RelationshipType { get; private set; }
    public string? RelationshipId { get; private set; }
    public string? RecordModuleKey { get; private set; }
    public string? RecordId { get; private set; }
    public string? RecordLabel { get; private set; }
    public string? SourceType { get; private set; }
    public string? SourceId { get; private set; }
    public string? SourceEvidence { get; private set; }
    public long Version { get; private set; }
}

internal enum ActivityType { Call, Email, Meeting, Note, Message, System }
