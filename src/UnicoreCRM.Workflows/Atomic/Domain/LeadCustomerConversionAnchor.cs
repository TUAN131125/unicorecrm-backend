namespace UnicoreCRM.Workflows.Atomic.Domain;

internal enum LeadCustomerConversionStage { SubjectResolved, CustomerResolved, LeadConversionRecorded, ProvenanceFinalized, Completed, ManualReview }

internal sealed class LeadCustomerConversionAnchor
{
    private LeadCustomerConversionAnchor() { }
    internal LeadCustomerConversionAnchor(string scopeKey, string workspaceId, string leadId, string idempotencyKey,
        string requestFingerprint, string businessIntentFingerprint, long expectedLeadVersion, string accountId,
        string memberId, string membershipId, string originalPrincipalId, string correlationId, string requestId,
        string subjectType, string subjectId, long subjectVersion, string leadOwnerId, DateTimeOffset now)
    {
        ScopeKey=scopeKey; ConversionId=WorkflowIds.New("conversion"); WorkspaceId=workspaceId; LeadId=leadId;
        ConversionType="LEAD_TO_CUSTOMER"; IdempotencyKey=idempotencyKey; RequestFingerprint=requestFingerprint;
        BusinessIntentFingerprint=businessIntentFingerprint; ExpectedLeadVersion=expectedLeadVersion;
        OriginalAccountId=accountId; OriginalMemberId=memberId; OriginalMembershipId=membershipId;
        OriginalPrincipalId=originalPrincipalId; CorrelationId=correlationId; RequestId=requestId;
        SubjectType=subjectType; SubjectId=subjectId; SubjectVersion=subjectVersion; FrozenLeadOwnerId=leadOwnerId;
        Stage=LeadCustomerConversionStage.SubjectResolved; CreatedAt=UpdatedAt=now;
    }
    internal string ScopeKey { get; private set; }=null!; internal string ConversionId { get; private set; }=null!;
    internal string WorkspaceId { get; private set; }=null!; internal string LeadId { get; private set; }=null!;
    internal string ConversionType { get; private set; }=null!; internal string IdempotencyKey { get; private set; }=null!;
    internal string RequestFingerprint { get; private set; }=null!; internal string BusinessIntentFingerprint { get; private set; }=null!;
    internal long ExpectedLeadVersion { get; private set; }
    internal string OriginalAccountId { get; private set; }=null!; internal string OriginalMemberId { get; private set; }=null!;
    internal string OriginalMembershipId { get; private set; }=null!; internal string OriginalPrincipalId { get; private set; }=null!;
    internal string CorrelationId { get; private set; }=null!; internal string RequestId { get; private set; }=null!;
    internal string SubjectType { get; private set; }=null!; internal string SubjectId { get; private set; }=null!;
    internal long SubjectVersion { get; private set; } internal string FrozenLeadOwnerId { get; private set; }=null!;
    internal LeadCustomerConversionStage Stage { get; private set; }
    internal string? CustomerId { get; private set; } internal long? CustomerVersion { get; private set; }
    internal string? CustomerResolution { get; private set; } internal long? LeadVersion { get; private set; }
    internal string? ResponseJson { get; private set; }
    internal string EmittedEventIdsJson { get; private set; }="[]"; internal string AuditEvidenceIdsJson { get; private set; }="[]";
    internal string? LastErrorCategory { get; private set; } internal string? LastErrorCode { get; private set; }
    internal int AttemptCount { get; private set; } internal DateTimeOffset? NextRetryAt { get; private set; }
    internal string? ExecutionAttemptId { get; private set; } internal string? ExecutionPrincipalId { get; private set; }
    internal DateTimeOffset? ExecutionLeaseAcquiredAt { get; private set; } internal DateTimeOffset? ExecutionLeaseExpiresAt { get; private set; }
    internal DateTimeOffset CreatedAt { get; private set; } internal DateTimeOffset UpdatedAt { get; private set; }
    internal DateTimeOffset? CompletedAt { get; private set; } internal byte[] RowVersion { get; private set; }=[];

    internal bool HasActiveLease(DateTimeOffset now, string attemptId) => ExecutionAttemptId is not null && ExecutionAttemptId != attemptId && ExecutionLeaseExpiresAt > now;
    internal void AcquireLease(string attemptId,string principalId,DateTimeOffset now,TimeSpan duration)
    { ExecutionAttemptId=attemptId;ExecutionPrincipalId=principalId;ExecutionLeaseAcquiredAt=now;ExecutionLeaseExpiresAt=now.Add(duration);UpdatedAt=now; }
    internal bool OwnsLease(string attemptId,DateTimeOffset now) => ExecutionAttemptId==attemptId && ExecutionLeaseExpiresAt>now;
    internal void RecordCustomer(string attemptId,string id,long version,string resolution,IReadOnlyList<string> events,IReadOnlyList<string> audits,DateTimeOffset now)
    { RequireLease(attemptId,now);CustomerId=id;CustomerVersion=version;CustomerResolution=resolution;MergeEvidence(events,audits);Stage=LeadCustomerConversionStage.CustomerResolved;ReleaseLease(now); }
    internal void RecordLead(string attemptId,long version,IReadOnlyList<string> events,IReadOnlyList<string> audits,DateTimeOffset now)
    { RequireLease(attemptId,now);LeadVersion=version;MergeEvidence(events,audits);Stage=LeadCustomerConversionStage.LeadConversionRecorded;ReleaseLease(now); }
    internal void RecordCustomerFinalization(string attemptId,long version,IReadOnlyList<string> events,IReadOnlyList<string> audits,DateTimeOffset now)
    { RequireLease(attemptId,now);CustomerVersion=version;MergeEvidence(events,audits);Stage=LeadCustomerConversionStage.ProvenanceFinalized;ReleaseLease(now); }
    internal void Complete(string response,DateTimeOffset now){ResponseJson=response;Stage=LeadCustomerConversionStage.Completed;CompletedAt=UpdatedAt=now;LastErrorCategory=LastErrorCode=null;NextRetryAt=null;ReleaseLease(now);}
    internal void Retry(string attemptId,string code,DateTimeOffset next,DateTimeOffset now){RequireLease(attemptId,now);AttemptCount++;LastErrorCategory="TRANSIENT";LastErrorCode=code;NextRetryAt=next;ReleaseLease(now);}
    internal void ManualReview(string attemptId,string code,DateTimeOffset now){RequireLease(attemptId,now);AttemptCount++;LastErrorCategory="MANUAL_REVIEW";LastErrorCode=code;Stage=LeadCustomerConversionStage.ManualReview;NextRetryAt=null;ReleaseLease(now);}
    private void RequireLease(string attemptId,DateTimeOffset now){if(!OwnsLease(attemptId,now))throw new InvalidOperationException("The execution attempt does not own the workflow lease.");}
    private void ReleaseLease(DateTimeOffset now){ExecutionAttemptId=ExecutionPrincipalId=null;ExecutionLeaseAcquiredAt=ExecutionLeaseExpiresAt=null;UpdatedAt=now;}
    private void MergeEvidence(IReadOnlyList<string> events,IReadOnlyList<string> audits)
    { EmittedEventIdsJson=System.Text.Json.JsonSerializer.Serialize(System.Text.Json.JsonSerializer.Deserialize<List<string>>(EmittedEventIdsJson)!.Concat(events).Distinct()); AuditEvidenceIdsJson=System.Text.Json.JsonSerializer.Serialize(System.Text.Json.JsonSerializer.Deserialize<List<string>>(AuditEvidenceIdsJson)!.Concat(audits).Distinct()); }
}
