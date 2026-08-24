namespace UnicoreCRM.Operations.Tasks.Domain;

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
