namespace UnicoreCRM.Sales.Quotes.Domain;

internal sealed class QuoteProofScanAttempt
{
    private QuoteProofScanAttempt() { }
    internal QuoteProofScanAttempt(QuoteProofScanJob job,string policyVersion,string policyDigest,string runtimeDigest,DateTimeOffset startedAt)
    { WorkspaceId=job.WorkspaceId;CaseId=job.CaseId;ProofId=job.ProofId;AttemptId=job.AttemptId!;LeaseId=job.LeaseId!;Epoch=job.Epoch!;AttemptNumber=job.LifetimeAttempts;PolicyVersion=policyVersion;PolicyDigest=policyDigest;RuntimeDigest=runtimeDigest;InputSha256=job.InputSha256;InputLength=job.InputLength;StartedAt=startedAt;LeaseExpiresAt=job.LeaseExpiresAt!.Value; }
    public string WorkspaceId { get; private set; }=null!;
    public string CaseId { get; private set; }=null!;
    public string ProofId { get; private set; }=null!;
    public string AttemptId { get; private set; }=null!;
    public string LeaseId { get; private set; }=null!;
    public string Epoch { get; private set; }=null!;
    public int AttemptNumber { get; private set; }
    public string PolicyVersion { get; private set; }=null!;
    public string PolicyDigest { get; private set; }=null!;
    public string RuntimeDigest { get; private set; }=null!;
    public string InputSha256 { get; private set; }=null!;
    public long InputLength { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset LeaseExpiresAt { get; private set; }
}
internal sealed class QuoteProofScanResult
{
    private QuoteProofScanResult() { }
    internal QuoteProofScanResult(QuoteProofScanAttempt a,string outcome,string reason,string parserStatus,string malwareVerdict,string provenanceJson,DateTimeOffset completedAt)
    { WorkspaceId=a.WorkspaceId;CaseId=a.CaseId;ProofId=a.ProofId;AttemptId=a.AttemptId;Outcome=outcome;ReasonCode=reason;ParserStatus=parserStatus;MalwareVerdict=malwareVerdict;ProvenanceJson=provenanceJson;CompletedAt=completedAt; }
    public string WorkspaceId { get; private set; }=null!;
    public string CaseId { get; private set; }=null!;
    public string ProofId { get; private set; }=null!;
    public string AttemptId { get; private set; }=null!;
    public string Outcome { get; private set; }=null!;
    public string ReasonCode { get; private set; }=null!;
    public string ParserStatus { get; private set; }=null!;
    public string MalwareVerdict { get; private set; }=null!;
    public string ProvenanceJson { get; private set; }=null!;
    public DateTimeOffset CompletedAt { get; private set; }
}
