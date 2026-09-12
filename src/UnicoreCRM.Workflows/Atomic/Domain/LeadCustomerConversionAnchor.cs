namespace UnicoreCRM.Workflows.Atomic.Domain;

internal enum LeadCustomerConversionStage { Created, Prepared, SubjectResolved, CustomerResolved, StakeholderResolved, LeadConversionRecorded, Completed, ManualReview }

internal sealed class LeadCustomerConversionAnchor
{
    private LeadCustomerConversionAnchor() { }
    internal LeadCustomerConversionAnchor(string scopeKey, string workspaceId, string leadId, string idempotencyKey,
        string fingerprint, long expectedLeadVersion, string accountId, string memberId, string membershipId,
        string correlationId, string requestId, string subjectType, string subjectMode, string? selectedSubjectId,
        string? newContactJson, string? stakeholderJson, DateTimeOffset now)
    {
        ScopeKey=scopeKey; ConversionId=WorkflowIds.New("conversion"); WorkspaceId=workspaceId; LeadId=leadId;
        ConversionType="LEAD_TO_CUSTOMER"; IdempotencyKey=idempotencyKey; Fingerprint=fingerprint;
        ExpectedLeadVersion=expectedLeadVersion; OriginalAccountId=accountId; OriginalMemberId=memberId;
        OriginalMembershipId=membershipId; CorrelationId=correlationId; RequestId=requestId; SubjectType=subjectType;
        SubjectMode=subjectMode; SelectedSubjectId=selectedSubjectId; NewContactJson=newContactJson;
        StakeholderJson=stakeholderJson; Stage=LeadCustomerConversionStage.Created; CreatedAt=UpdatedAt=now;
    }
    internal string ScopeKey { get; private set; }=null!; internal string ConversionId { get; private set; }=null!;
    internal string WorkspaceId { get; private set; }=null!; internal string LeadId { get; private set; }=null!;
    internal string ConversionType { get; private set; }=null!; internal string IdempotencyKey { get; private set; }=null!;
    internal string Fingerprint { get; private set; }=null!; internal long ExpectedLeadVersion { get; private set; }
    internal string OriginalAccountId { get; private set; }=null!; internal string OriginalMemberId { get; private set; }=null!;
    internal string OriginalMembershipId { get; private set; }=null!; internal string CorrelationId { get; private set; }=null!;
    internal string RequestId { get; private set; }=null!; internal string SubjectType { get; private set; }=null!;
    internal string SubjectMode { get; private set; }=null!; internal string? SelectedSubjectId { get; private set; }
    internal string? NewContactJson { get; private set; } internal string? StakeholderJson { get; private set; }
    internal string? FrozenLeadOwnerId { get; private set; } internal bool? FrozenDoNotCall { get; private set; }
    internal bool? FrozenDoNotEmail { get; private set; }
    internal LeadCustomerConversionStage Stage { get; private set; } internal string? SubjectId { get; private set; }
    internal long? SubjectVersion { get; private set; } internal bool? SubjectCreated { get; private set; }
    internal string? CustomerId { get; private set; } internal long? CustomerVersion { get; private set; }
    internal string? CustomerResolution { get; private set; } internal string? StakeholderRelationshipId { get; private set; }
    internal long? LeadVersion { get; private set; } internal string? ResponseJson { get; private set; }
    internal string EmittedEventIdsJson { get; private set; }="[]"; internal string AuditEvidenceIdsJson { get; private set; }="[]";
    internal string? LastErrorCategory { get; private set; } internal string? LastErrorCode { get; private set; }
    internal int AttemptCount { get; private set; } internal DateTimeOffset? NextRetryAt { get; private set; }
    internal string? RecoveryExecutorId { get; private set; } internal DateTimeOffset CreatedAt { get; private set; }
    internal DateTimeOffset UpdatedAt { get; private set; } internal DateTimeOffset? CompletedAt { get; private set; }
    internal byte[] RowVersion { get; private set; }=[];
    internal void Prepare(string? ownerId,bool? doNotCall,bool? doNotEmail,DateTimeOffset now){FrozenLeadOwnerId=ownerId;FrozenDoNotCall=doNotCall;FrozenDoNotEmail=doNotEmail;Stage=LeadCustomerConversionStage.Prepared;UpdatedAt=now;}
    internal void RecordSubject(string id,long version,bool created,DateTimeOffset now){SubjectId=id;SubjectVersion=version;SubjectCreated=created;Stage=LeadCustomerConversionStage.SubjectResolved;UpdatedAt=now;}
    internal void RecordCustomer(string id,long version,string resolution,IReadOnlyList<string> events,IReadOnlyList<string> audits,DateTimeOffset now){CustomerId=id;CustomerVersion=version;CustomerResolution=resolution;MergeEvidence(events,audits);Stage=LeadCustomerConversionStage.CustomerResolved;UpdatedAt=now;}
    internal void SkipOrRecordStakeholder(string? id,DateTimeOffset now){StakeholderRelationshipId=id;Stage=LeadCustomerConversionStage.StakeholderResolved;UpdatedAt=now;}
    internal void RecordLead(long version,IReadOnlyList<string> events,IReadOnlyList<string> audits,DateTimeOffset now){LeadVersion=version;MergeEvidence(events,audits);Stage=LeadCustomerConversionStage.LeadConversionRecorded;UpdatedAt=now;}
    internal void RecordCustomerFinalization(long version,IReadOnlyList<string> events,IReadOnlyList<string> audits,DateTimeOffset now){CustomerVersion=version;MergeEvidence(events,audits);UpdatedAt=now;}
    internal void Complete(string response,DateTimeOffset now){ResponseJson=response;Stage=LeadCustomerConversionStage.Completed;CompletedAt=UpdatedAt=now;LastErrorCategory=LastErrorCode=null;NextRetryAt=null;}
    internal void Retry(string code,DateTimeOffset next,string executor,DateTimeOffset now){AttemptCount++;LastErrorCategory="TRANSIENT";LastErrorCode=code;NextRetryAt=next;RecoveryExecutorId=executor;UpdatedAt=now;}
    internal void ManualReview(string code,string executor,DateTimeOffset now){AttemptCount++;LastErrorCategory="MANUAL_REVIEW";LastErrorCode=code;RecoveryExecutorId=executor;Stage=LeadCustomerConversionStage.ManualReview;NextRetryAt=null;UpdatedAt=now;}
    private void MergeEvidence(IReadOnlyList<string> events,IReadOnlyList<string> audits)
    { EmittedEventIdsJson=System.Text.Json.JsonSerializer.Serialize(System.Text.Json.JsonSerializer.Deserialize<List<string>>(EmittedEventIdsJson)!.Concat(events).Distinct()); AuditEvidenceIdsJson=System.Text.Json.JsonSerializer.Serialize(System.Text.Json.JsonSerializer.Deserialize<List<string>>(AuditEvidenceIdsJson)!.Concat(audits).Distinct()); }
}
