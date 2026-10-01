namespace UnicoreCRM.Workflows.Atomic.Domain;
internal enum LeadHandoverStage { Created, LeadReserved, TasksCommitted, LeadCommitted, Completed, ManualReview }
internal sealed class LeadHandoverAnchor
{
    private LeadHandoverAnchor() { }
    internal LeadHandoverAnchor(string scopeKey, string workspaceId, string leadId, string key, string fingerprint,
        long expectedVersion, string accountId, string memberId, string membershipId, string correlationId, string requestId,
        string previousOwnerId, string nextOwnerId, string reason, int sla, DateTimeOffset now)
    {
        ScopeKey = scopeKey;
        HandoverId = WorkflowIds.New("handover");
        WorkspaceId = workspaceId;
        LeadId = leadId;
        ActiveLeadKey = workspaceId + ":" + leadId;
        IdempotencyKey = key;
        RequestFingerprint = fingerprint;
        ExpectedLeadVersion = expectedVersion;
        OriginalAccountId = accountId;
        OriginalMemberId = memberId;
        OriginalPrincipalId = memberId;
        OriginalMembershipId = membershipId;
        CorrelationId = correlationId;
        RequestId = requestId;
        PreviousOwnerId = previousOwnerId;
        NextOwnerId = nextOwnerId;
        Reason = reason;
        ResolvedSlaHours = sla;
        HandoverOccurredAt = now;
        TakeoverDueAt = now.AddHours(sla);
        CreatedAt = now;
        UpdatedAt = now;
    }
    internal string ScopeKey { get; private set; } = null!;
    internal string HandoverId { get; private set; } = null!;
    internal string WorkspaceId { get; private set; } = null!;
    internal string LeadId { get; private set; } = null!;
    internal string? ActiveLeadKey { get; private set; }
    internal string IdempotencyKey { get; private set; } = null!;
    internal string RequestFingerprint { get; private set; } = null!;
    internal long ExpectedLeadVersion { get; private set; }
    internal string OriginalAccountId { get; private set; } = null!;
    internal string OriginalMemberId { get; private set; } = null!;
    internal string OriginalMembershipId { get; private set; } = null!;
    internal string OriginalPrincipalId { get; private set; } = null!;
    internal string CorrelationId { get; private set; } = null!;
    internal string RequestId { get; private set; } = null!;
    internal string PreviousOwnerId { get; private set; } = null!;
    internal string NextOwnerId { get; private set; } = null!;
    internal string Reason { get; private set; } = null!;
    internal int ResolvedSlaHours { get; private set; }
    internal DateTimeOffset HandoverOccurredAt { get; private set; }
    internal DateTimeOffset TakeoverDueAt { get; private set; }
    internal LeadHandoverStage Stage { get; private set; }
    internal string? TasksResultJson { get; private set; }
    internal string? LeadResultJson { get; private set; }
    internal string? ResponseJson { get; private set; }
    internal string EmittedEventIdsJson { get; private set; } = "[]";
    internal string AuditEvidenceIdsJson { get; private set; } = "[]";
    internal string? LastErrorCategory { get; private set; }
    internal string? LastErrorCode { get; private set; }
    internal int AttemptCount { get; private set; }
    internal DateTimeOffset? NextRetryAt { get; private set; }
    internal string? ExecutionAttemptId { get; private set; }
    internal string? ExecutionPrincipalId { get; private set; }
    internal DateTimeOffset? ExecutionLeaseAcquiredAt { get; private set; }
    internal DateTimeOffset? ExecutionLeaseExpiresAt { get; private set; }
    internal DateTimeOffset CreatedAt { get; private set; }
    internal DateTimeOffset UpdatedAt { get; private set; }
    internal DateTimeOffset? CompletedAt { get; private set; }
    internal byte[] RowVersion { get; private set; } = [];
    internal void RecordStage(string attemptId, LeadHandoverStage stage, string? result, IReadOnlyList<string> events, IReadOnlyList<string> audits, DateTimeOffset now)
    {
        RequireLease(attemptId, now);
        Stage = stage;
        if (stage == LeadHandoverStage.TasksCommitted)
            TasksResultJson = result;
        if (stage == LeadHandoverStage.LeadCommitted)
            LeadResultJson = result;
        MergeEvidence(events, audits);
        ReleaseLease(now);
    }
    internal bool HasActiveLease(DateTimeOffset now, string attemptId) => ExecutionAttemptId is not null && ExecutionAttemptId != attemptId && ExecutionLeaseExpiresAt > now;
    internal void AcquireLease(string attemptId, string principalId, DateTimeOffset now, TimeSpan duration)
    {
        ExecutionAttemptId = attemptId;
        ExecutionPrincipalId = principalId;
        ExecutionLeaseAcquiredAt = now;
        ExecutionLeaseExpiresAt = now.Add(duration);
        UpdatedAt = now;
    }
    internal bool OwnsLease(string attemptId, DateTimeOffset now) => ExecutionAttemptId == attemptId && ExecutionLeaseExpiresAt > now;
    internal void Complete(string response, DateTimeOffset now)
    {
        ResponseJson = response;
        Stage = LeadHandoverStage.Completed;
        ActiveLeadKey = null;
        CompletedAt = now;
        UpdatedAt = now;
        LastErrorCategory = null;
        LastErrorCode = null;
        NextRetryAt = null;
        ReleaseLease(now);
    }
    internal void Retry(string attemptId, string code, DateTimeOffset next, DateTimeOffset now)
    {
        RequireLease(attemptId, now);
        AttemptCount++;
        LastErrorCategory = "TRANSIENT";
        LastErrorCode = code;
        NextRetryAt = next;
        ReleaseLease(now);
    }
    internal void ManualReview(string attemptId, string code, DateTimeOffset now)
    {
        RequireLease(attemptId, now);
        AttemptCount++;
        LastErrorCategory = "MANUAL_REVIEW";
        LastErrorCode = code;
        Stage = LeadHandoverStage.ManualReview;
        ActiveLeadKey = null;
        NextRetryAt = null;
        ReleaseLease(now);
    }
    private void RequireLease(string attemptId, DateTimeOffset now)
    {
        if (!OwnsLease(attemptId, now))
            throw new InvalidOperationException("The execution attempt does not own the workflow lease.");
    }
    private void ReleaseLease(DateTimeOffset now)
    {
        ExecutionAttemptId = null;
        ExecutionPrincipalId = null;
        ExecutionLeaseAcquiredAt = null;
        ExecutionLeaseExpiresAt = null;
        UpdatedAt = now;
    }
    private void MergeEvidence(IReadOnlyList<string> events, IReadOnlyList<string> audits)
    {
        EmittedEventIdsJson = System.Text.Json.JsonSerializer.Serialize(System.Text.Json.JsonSerializer.Deserialize<List<string>>(EmittedEventIdsJson)!.Concat(events).Distinct());
        AuditEvidenceIdsJson = System.Text.Json.JsonSerializer.Serialize(System.Text.Json.JsonSerializer.Deserialize<List<string>>(AuditEvidenceIdsJson)!.Concat(audits).Distinct());
    }
}
