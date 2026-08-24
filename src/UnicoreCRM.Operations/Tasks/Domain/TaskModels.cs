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

internal sealed record TaskReferenceData(
    string? RelationshipType,
    string? RelationshipId,
    string? RecordModuleKey,
    string? RecordId,
    string? RecordLabel,
    string? SourceType,
    string? SourceId,
    string? SourceEvidence);

internal sealed class TaskIdempotencyRecord
{
    private TaskIdempotencyRecord() { }

    internal TaskIdempotencyRecord(
        string scopeKey,
        string workspaceId,
        string operation,
        string actorId,
        string targetId,
        string idempotencyKey,
        string fingerprint,
        string responseJson,
        DateTimeOffset createdAt)
    {
        ScopeKey = scopeKey;
        WorkspaceId = workspaceId;
        Operation = operation;
        ActorId = actorId;
        TargetId = targetId;
        IdempotencyKey = idempotencyKey;
        Fingerprint = fingerprint;
        ResponseJson = responseJson;
        CreatedAt = createdAt;
    }

    public string ScopeKey { get; private set; } = null!;
    public string WorkspaceId { get; private set; } = null!;
    public string Operation { get; private set; } = null!;
    public string ActorId { get; private set; } = null!;
    public string TargetId { get; private set; } = null!;
    public string IdempotencyKey { get; private set; } = null!;
    public string Fingerprint { get; private set; } = null!;
    public string ResponseJson { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
}

internal sealed class TaskAuditRecord
{
    private TaskAuditRecord() { }

    internal TaskAuditRecord(
        string operation,
        string workspaceId,
        string actorId,
        string? aggregateId,
        string requestId,
        string correlationId,
        string outcome,
        long? priorVersion,
        long? newVersion,
        DateTimeOffset occurredAt)
    {
        AuditId = TaskIds.New("audit");
        Operation = operation;
        WorkspaceId = workspaceId;
        ActorId = actorId;
        AggregateId = aggregateId;
        RequestId = requestId;
        CorrelationId = correlationId;
        Outcome = outcome;
        PriorVersion = priorVersion;
        NewVersion = newVersion;
        OccurredAt = occurredAt;
    }

    public string AuditId { get; private set; } = null!;
    public string Operation { get; private set; } = null!;
    public string WorkspaceId { get; private set; } = null!;
    public string ActorId { get; private set; } = null!;
    public string? AggregateId { get; private set; }
    public string RequestId { get; private set; } = null!;
    public string CorrelationId { get; private set; } = null!;
    public string Outcome { get; private set; } = null!;
    public long? PriorVersion { get; private set; }
    public long? NewVersion { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
}

internal sealed class TaskOutboxMessage
{
    private TaskOutboxMessage() { }

    internal TaskOutboxMessage(
        string eventType,
        string aggregateId,
        string workspaceId,
        string correlationId,
        string payloadJson,
        DateTimeOffset occurredAt)
    {
        EventId = TaskIds.New("event");
        EventType = eventType;
        AggregateId = aggregateId;
        WorkspaceId = workspaceId;
        CorrelationId = correlationId;
        PayloadJson = payloadJson;
        OccurredAt = occurredAt;
    }

    public string EventId { get; private set; } = null!;
    public string EventType { get; private set; } = null!;
    public string AggregateId { get; private set; } = null!;
    public string WorkspaceId { get; private set; } = null!;
    public string CorrelationId { get; private set; } = null!;
    public string PayloadJson { get; private set; } = null!;
    public DateTimeOffset OccurredAt { get; private set; }
}

internal enum TaskStatus { Open, Completed, Cancelled }
internal enum TaskPriority { Low, Normal, High, Urgent }
internal enum ActivityType { Call, Email, Meeting, Note, Message, System }

internal static class TaskIds
{
    internal static string New(string prefix) => $"{prefix}_{Guid.NewGuid():N}";
}
