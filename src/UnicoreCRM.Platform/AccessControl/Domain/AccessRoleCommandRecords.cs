using System.Security.Cryptography;
using System.Text;

namespace UnicoreCRM.Platform.AccessControl.Domain;

internal sealed class WorkspaceAccessDirectoryRevision
{
    private WorkspaceAccessDirectoryRevision() { }
    internal WorkspaceAccessDirectoryRevision(string workspaceId)
    {
        WorkspaceId = workspaceId;
        Revision = 1;
    }

    public string WorkspaceId { get; private set; } = null!;
    public long Revision { get; private set; }
    internal void Advance() => Revision = checked(Revision + 1);
}

internal sealed class AccessRoleCommandIdempotencyRecord
{
    private AccessRoleCommandIdempotencyRecord() { }

    internal AccessRoleCommandIdempotencyRecord(
        string workspaceId,
        string actorMembershipId,
        string idempotencyKey,
        string fingerprint,
        string commandId,
        string roleId,
        string auditEvidenceId,
        string eventId,
        long directoryRevisionAtCommit,
        string correlationId,
        DateTimeOffset occurredAt)
    {
        ScopeKey = CreateScopeKey(workspaceId, actorMembershipId, idempotencyKey);
        WorkspaceId = workspaceId;
        OperationId = "createAccessRole";
        ActorMembershipId = actorMembershipId;
        IdempotencyKey = idempotencyKey;
        Fingerprint = fingerprint;
        CommandId = commandId;
        RoleId = roleId;
        RoleVersion = 0;
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
    public string RoleId { get; private set; } = null!;
    public long RoleVersion { get; private set; }
    public string AuditEvidenceId { get; private set; } = null!;
    public string EventId { get; private set; } = null!;
    public long DirectoryRevisionAtCommit { get; private set; }
    public string CorrelationId { get; private set; } = null!;
    public DateTimeOffset OccurredAt { get; private set; }

    internal static string CreateScopeKey(string workspaceId, string actorMembershipId, string idempotencyKey)
    {
        var value = $"createAccessRole\n{workspaceId.Length}:{workspaceId}\n{actorMembershipId.Length}:{actorMembershipId}\n{idempotencyKey.Length}:{idempotencyKey}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }
}

internal sealed class AccessGovernanceCommandAudit
{
    private AccessGovernanceCommandAudit() { }

    internal AccessGovernanceCommandAudit(
        string commandId,
        string workspaceId,
        string actorAccountId,
        string actorMembershipId,
        string actorMemberId,
        string requestId,
        string correlationId,
        string roleId,
        DateTimeOffset occurredAt)
    {
        EvidenceId = AccessControlIds.New("audit");
        EvidenceType = "ACCESS_GOVERNANCE_COMMAND";
        OperationId = "createAccessRole";
        CommandId = commandId;
        WorkspaceId = workspaceId;
        ActorAccountId = actorAccountId;
        ActorMembershipId = actorMembershipId;
        ActorMemberId = actorMemberId;
        RequestId = requestId;
        CorrelationId = correlationId;
        RoleId = roleId;
        ResultingVersion = 0;
        OccurredAt = occurredAt;
        Outcome = "COMMITTED";
    }

    public string EvidenceId { get; private set; } = null!;
    public string EvidenceType { get; private set; } = null!;
    public string OperationId { get; private set; } = null!;
    public string CommandId { get; private set; } = null!;
    public string WorkspaceId { get; private set; } = null!;
    public string ActorAccountId { get; private set; } = null!;
    public string ActorMembershipId { get; private set; } = null!;
    public string ActorMemberId { get; private set; } = null!;
    public string RequestId { get; private set; } = null!;
    public string CorrelationId { get; private set; } = null!;
    public string RoleId { get; private set; } = null!;
    public long ResultingVersion { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public string Outcome { get; private set; } = null!;
}

internal sealed class AccessControlOutboxEvent
{
    private AccessControlOutboxEvent() { }

    internal AccessControlOutboxEvent(
        string roleId,
        string correlationId,
        string causationId,
        string payloadJson,
        DateTimeOffset occurredAt,
        string workspaceId)
    {
        EventId = AccessControlIds.New("event");
        EventType = "ACCESS_ROLE_CREATED";
        WorkspaceId = workspaceId;
        AggregateId = roleId;
        AggregateType = "ACCESS_ROLE";
        AggregateVersion = 0;
        OccurredAt = occurredAt;
        CorrelationId = correlationId;
        CausationId = causationId;
        PayloadJson = payloadJson;
    }

    public string EventId { get; private set; } = null!;
    public string EventType { get; private set; } = null!;
    public string WorkspaceId { get; private set; } = null!;
    public string AggregateId { get; private set; } = null!;
    public string AggregateType { get; private set; } = null!;
    public long AggregateVersion { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public string CorrelationId { get; private set; } = null!;
    public string CausationId { get; private set; } = null!;
    public string PayloadJson { get; private set; } = null!;
}
