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

/// <summary>
/// One durable owner-local idempotency completion. The uniqueness scope is
/// <c>(WorkspaceId, operationId, ActorMembershipId, IdempotencyKey)</c>: the operation is a scope
/// column, so <c>createAccessRole</c> and <c>replaceAccessRole</c> never share a record and the same
/// key value may be used independently under each operation.
/// </summary>
internal sealed class AccessRoleCommandIdempotencyRecord
{
    private AccessRoleCommandIdempotencyRecord() { }

    internal AccessRoleCommandIdempotencyRecord(
        string operationId,
        string workspaceId,
        string actorMembershipId,
        string idempotencyKey,
        string fingerprint,
        string commandId,
        string roleId,
        long roleVersion,
        string auditEvidenceId,
        string eventId,
        long directoryRevisionAtCommit,
        string correlationId,
        DateTimeOffset occurredAt)
    {
        ScopeKey = CreateScopeKey(operationId, workspaceId, actorMembershipId, idempotencyKey);
        WorkspaceId = workspaceId;
        OperationId = operationId;
        ActorMembershipId = actorMembershipId;
        IdempotencyKey = idempotencyKey;
        Fingerprint = fingerprint;
        CommandId = commandId;
        RoleId = roleId;
        RoleVersion = roleVersion;
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

    internal static string CreateScopeKey(
        string operationId,
        string workspaceId,
        string actorMembershipId,
        string idempotencyKey)
    {
        var value = $"{operationId}\n{workspaceId.Length}:{workspaceId}\n{actorMembershipId.Length}:{actorMembershipId}\n{idempotencyKey.Length}:{idempotencyKey}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }
}

/// <summary>
/// The append-only <c>ACCESS_GOVERNANCE_COMMAND</c> evidence written by an AccessControl governance
/// command. It carries only command envelope identities, trusted scalar actor provenance and the
/// command aggregate version transition: no capability array, policy payload, prior configuration
/// snapshot or directory projection is copied here.
/// </summary>
internal sealed class AccessGovernanceCommandAudit
{
    private AccessGovernanceCommandAudit() { }

    internal AccessGovernanceCommandAudit(
        string operationId,
        string commandId,
        string workspaceId,
        string actorAccountId,
        string actorMembershipId,
        string actorMemberId,
        string requestId,
        string correlationId,
        string roleId,
        long? priorVersion,
        long resultingVersion,
        string? reason,
        DateTimeOffset occurredAt)
        : this(
            operationId,
            commandId,
            workspaceId,
            actorAccountId,
            actorMembershipId,
            actorMemberId,
            requestId,
            correlationId,
            roleId,
            null,
            priorVersion,
            resultingVersion,
            reason,
            occurredAt)
    {
    }

    private AccessGovernanceCommandAudit(
        string operationId,
        string commandId,
        string workspaceId,
        string actorAccountId,
        string actorMembershipId,
        string actorMemberId,
        string requestId,
        string correlationId,
        string? roleId,
        string? targetMembershipId,
        long? priorVersion,
        long resultingVersion,
        string? reason,
        DateTimeOffset occurredAt)
    {
        EvidenceId = AccessControlIds.New("audit");
        EvidenceType = "ACCESS_GOVERNANCE_COMMAND";
        OperationId = operationId;
        CommandId = commandId;
        WorkspaceId = workspaceId;
        ActorAccountId = actorAccountId;
        ActorMembershipId = actorMembershipId;
        ActorMemberId = actorMemberId;
        RequestId = requestId;
        CorrelationId = correlationId;
        RoleId = roleId;
        TargetMembershipId = targetMembershipId;
        PriorVersion = priorVersion;
        ResultingVersion = resultingVersion;
        Reason = reason;
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
    public string? RoleId { get; private set; }
    public string? TargetMembershipId { get; private set; }

    /// <summary>
    /// The command aggregate version observed under the owner lock. Role creation has no prior
    /// version and stores null; versioned role and member-access commands store their accepted
    /// <c>If-Match</c> version.
    /// </summary>
    public long? PriorVersion { get; private set; }
    public long ResultingVersion { get; private set; }

    /// <summary>
    /// Free-text governance provenance supplied by a lifecycle command. It is explanatory only: no
    /// authorization, routing or business rule reads it, and it is deliberately absent from
    /// canonical role state, the composed directory and every outbox payload.
    /// </summary>
    public string? Reason { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public string Outcome { get; private set; } = null!;

    internal static AccessGovernanceCommandAudit ForMemberAccess(
        string operationId,
        string commandId,
        string workspaceId,
        string actorAccountId,
        string actorMembershipId,
        string actorMemberId,
        string requestId,
        string correlationId,
        string targetMembershipId,
        long priorVersion,
        long resultingVersion,
        DateTimeOffset occurredAt) => new(
            operationId,
            commandId,
            workspaceId,
            actorAccountId,
            actorMembershipId,
            actorMemberId,
            requestId,
            correlationId,
            null,
            targetMembershipId,
            priorVersion,
            resultingVersion,
            null,
            occurredAt);
}

internal sealed class AccessControlOutboxEvent
{
    private AccessControlOutboxEvent() { }

    internal AccessControlOutboxEvent(
        string eventType,
        string roleId,
        long aggregateVersion,
        string correlationId,
        string causationId,
        string payloadJson,
        DateTimeOffset occurredAt,
        string workspaceId)
        : this(
            eventType,
            roleId,
            "ACCESS_ROLE",
            aggregateVersion,
            correlationId,
            causationId,
            payloadJson,
            occurredAt,
            workspaceId)
    {
    }

    private AccessControlOutboxEvent(
        string eventType,
        string aggregateId,
        string aggregateType,
        long aggregateVersion,
        string correlationId,
        string causationId,
        string payloadJson,
        DateTimeOffset occurredAt,
        string workspaceId)
    {
        EventId = AccessControlIds.New("event");
        EventType = eventType;
        WorkspaceId = workspaceId;
        AggregateId = aggregateId;
        AggregateType = aggregateType;
        AggregateVersion = aggregateVersion;
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

    internal static AccessControlOutboxEvent ForMemberAccess(
        string eventType,
        string membershipId,
        long aggregateVersion,
        string correlationId,
        string causationId,
        string payloadJson,
        DateTimeOffset occurredAt,
        string workspaceId) => new(
            eventType,
            membershipId,
            "WORKSPACE_MEMBER_ACCESS",
            aggregateVersion,
            correlationId,
            causationId,
            payloadJson,
            occurredAt,
            workspaceId);
}
