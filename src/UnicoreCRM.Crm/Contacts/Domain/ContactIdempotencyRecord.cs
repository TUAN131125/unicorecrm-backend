namespace UnicoreCRM.Crm.Contacts.Domain;

internal sealed class ContactIdempotencyRecord
{
    private ContactIdempotencyRecord() { }

    internal ContactIdempotencyRecord(
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

    internal string ScopeKey { get; private set; } = null!;
    internal string WorkspaceId { get; private set; } = null!;
    internal string Operation { get; private set; } = null!;
    internal string ActorId { get; private set; } = null!;
    internal string TargetId { get; private set; } = null!;
    internal string IdempotencyKey { get; private set; } = null!;
    internal string Fingerprint { get; private set; } = null!;
    internal string ResponseJson { get; private set; } = null!;
    internal DateTimeOffset CreatedAt { get; private set; }
}
