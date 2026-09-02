namespace UnicoreCRM.Platform.AccessControl.Domain;

/// <summary>
/// The owner-local concurrency anchor for one Workspace membership's AccessControl assignment set.
/// Absence is the logical version zero; a row is materialized only by the first committed
/// replacement and therefore starts at version one.
/// </summary>
internal sealed class MemberAccessVersionAnchor
{
    private MemberAccessVersionAnchor() { }

    internal MemberAccessVersionAnchor(string workspaceId, string membershipId)
    {
        WorkspaceId = workspaceId;
        MembershipId = membershipId;
        Version = 1;
    }

    public string WorkspaceId { get; private set; } = null!;
    public string MembershipId { get; private set; } = null!;
    public long Version { get; private set; }

    internal void Advance() => Version = checked(Version + 1);
}

/// <summary>
/// The durable idempotency completion for <c>replaceWorkspaceMemberAccess</c>. It is deliberately
/// separate from role-command completions because its aggregate identity and version are a target
/// membership and <c>MemberAccessVersion</c>, not an <c>AccessRole</c>.
/// </summary>
internal sealed class MemberAccessCommandIdempotencyRecord
{
    private MemberAccessCommandIdempotencyRecord() { }

    internal MemberAccessCommandIdempotencyRecord(
        string operationId,
        string workspaceId,
        string actorMembershipId,
        string idempotencyKey,
        string fingerprint,
        string commandId,
        string membershipId,
        long memberAccessVersion,
        string auditEvidenceId,
        string eventId,
        long directoryRevisionAtCommit,
        string correlationId,
        DateTimeOffset occurredAt)
    {
        ScopeKey = AccessRoleCommandIdempotencyRecord.CreateScopeKey(
            operationId,
            workspaceId,
            actorMembershipId,
            idempotencyKey);
        WorkspaceId = workspaceId;
        OperationId = operationId;
        ActorMembershipId = actorMembershipId;
        IdempotencyKey = idempotencyKey;
        Fingerprint = fingerprint;
        CommandId = commandId;
        MembershipId = membershipId;
        MemberAccessVersion = memberAccessVersion;
        AuditEvidenceId = auditEvidenceId;
        EventId = eventId;
        DirectoryRevisionAtCommit = directoryRevisionAtCommit;
        CorrelationId = correlationId;
        OccurredAt = occurredAt;
    }

    public string ScopeKey { get; private set; } = null!;
    public string WorkspaceId { get; private set; } = null!;
    public string OperationId { get; private set; } = null!;
    public string ActorMembershipId { get; private set; } = null!;
    public string IdempotencyKey { get; private set; } = null!;
    public string Fingerprint { get; private set; } = null!;
    public string CommandId { get; private set; } = null!;
    public string MembershipId { get; private set; } = null!;
    public long MemberAccessVersion { get; private set; }
    public string AuditEvidenceId { get; private set; } = null!;
    public string EventId { get; private set; } = null!;
    public long DirectoryRevisionAtCommit { get; private set; }
    public string CorrelationId { get; private set; } = null!;
    public DateTimeOffset OccurredAt { get; private set; }
}
