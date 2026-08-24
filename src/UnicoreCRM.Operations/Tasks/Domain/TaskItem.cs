namespace UnicoreCRM.Operations.Tasks.Domain;

internal sealed class TaskItem
{
    private TaskItem() { }

    internal TaskItem(
        string workspaceId,
        string title,
        string? description,
        TaskPriority priority,
        string assigneeId,
        DateTimeOffset dueAt,
        TaskReferenceData references,
        string? dedupeKey,
        DateTimeOffset now)
    {
        TaskId = TaskIds.New("task");
        WorkspaceId = workspaceId;
        Title = title;
        Description = description;
        Status = TaskStatus.Open;
        Priority = priority;
        AssigneeId = assigneeId;
        DueAt = dueAt;
        ApplyReferences(references);
        DedupeKey = dedupeKey;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public string TaskId { get; private set; } = null!;
    public string WorkspaceId { get; private set; } = null!;
    public string Title { get; private set; } = null!;
    public string? Description { get; private set; }
    public TaskStatus Status { get; private set; }
    public TaskPriority Priority { get; private set; }
    public string AssigneeId { get; private set; } = null!;
    public DateTimeOffset DueAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public string? CancellationReason { get; private set; }
    public string? Outcome { get; private set; }
    public string? RelationshipType { get; private set; }
    public string? RelationshipId { get; private set; }
    public string? RecordModuleKey { get; private set; }
    public string? RecordId { get; private set; }
    public string? RecordLabel { get; private set; }
    public string? SourceType { get; private set; }
    public string? SourceId { get; private set; }
    public string? SourceEvidence { get; private set; }
    public string? DedupeKey { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? ArchivedAt { get; private set; }
    public string? ArchiveReason { get; private set; }
    public long Version { get; private set; }

    internal bool Complete(string outcome, DateTimeOffset now)
    {
        if (Status != TaskStatus.Open)
            return false;
        Status = TaskStatus.Completed;
        Outcome = outcome;
        CompletedAt = now;
        Touch(now);
        return true;
    }

    internal bool Cancel(string reason, DateTimeOffset now)
    {
        if (Status != TaskStatus.Open)
            return false;
        Status = TaskStatus.Cancelled;
        CancellationReason = reason;
        CancelledAt = now;
        Touch(now);
        return true;
    }

    internal bool Assign(string assigneeId, DateTimeOffset now)
    {
        if (Status != TaskStatus.Open)
            return false;
        AssigneeId = assigneeId;
        Touch(now);
        return true;
    }

    internal bool Reschedule(DateTimeOffset dueAt, DateTimeOffset now)
    {
        if (Status != TaskStatus.Open)
            return false;
        DueAt = dueAt;
        Touch(now);
        return true;
    }

    internal void Archive(string reason, DateTimeOffset now)
    {
        ArchivedAt = now;
        ArchiveReason = reason;
        Touch(now);
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }

    private void ApplyReferences(TaskReferenceData references)
    {
        RelationshipType = references.RelationshipType;
        RelationshipId = references.RelationshipId;
        RecordModuleKey = references.RecordModuleKey;
        RecordId = references.RecordId;
        RecordLabel = references.RecordLabel;
        SourceType = references.SourceType;
        SourceId = references.SourceId;
        SourceEvidence = references.SourceEvidence;
    }
}

internal enum TaskStatus { Open, Completed, Cancelled }
internal enum TaskPriority { Low, Normal, High, Urgent }
