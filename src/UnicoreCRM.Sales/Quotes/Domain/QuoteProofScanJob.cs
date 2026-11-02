namespace UnicoreCRM.Sales.Quotes.Domain;

/// <summary>Owner-local lease/CAS invariants. Persistence/worker activation awaits the approved scan policy.</summary>
internal sealed class QuoteProofScanJob
{
    private QuoteProofScanJob() { }
    internal QuoteProofScanJob(string workspaceId,string caseId,string proofId,string inputSha256,long inputLength,DateTimeOffset now)
    {
        if(string.IsNullOrWhiteSpace(workspaceId)||string.IsNullOrWhiteSpace(caseId)||string.IsNullOrWhiteSpace(proofId)
            || inputSha256.Length!=64 || inputSha256.Any(c=>!Uri.IsHexDigit(c)) || inputLength is <1 or >10485760)throw new ArgumentException("Invalid scan job binding.");
        WorkspaceId=workspaceId;CaseId=caseId;ProofId=proofId;InputSha256=inputSha256.ToUpperInvariant();InputLength=inputLength;NextAttemptAt=now;
    }
    public string WorkspaceId { get; private set; } = null!;
    public string CaseId { get; private set; } = null!;
    public string ProofId { get; private set; } = null!;
    public string InputSha256 { get; private set; } = null!;
    public long InputLength { get; private set; }
    public string Status { get; private set; }="SCAN_PENDING";
    public string? Epoch { get; private set; }
    public string? AttemptId { get; private set; }
    public string? LeaseId { get; private set; }
    public DateTimeOffset? LeaseExpiresAt { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public int LifetimeAttempts { get; private set; }
    public int EpochAttempts { get; private set; }
    public long ResourceVersion { get; private set; }=1;
    public string? LastResultAttemptId { get; private set; }
    public string? InterventionReason { get; private set; }
    internal bool MarkExhausted(int epochMaximum,int lifetimeMaximum)
    {
        if(Status=="SCANNING" || Status=="NEEDS_INTERVENTION" || LifetimeAttempts<lifetimeMaximum && EpochAttempts<epochMaximum)return false;
        Status="NEEDS_INTERVENTION";InterventionReason=LifetimeAttempts>=lifetimeMaximum?"LIFETIME_RETRY_EXHAUSTED":"EPOCH_RETRY_EXHAUSTED";ResourceVersion=checked(ResourceVersion+1);return true;
    }
    internal void BindResult(string attemptId){LastResultAttemptId=attemptId;}


    internal bool TryClaim(string epoch,string attemptId,string leaseId,DateTimeOffset now,TimeSpan leaseDuration,int epochMaximum,int lifetimeMaximum,bool allowTerminalRescan=false)
    {
        if(string.IsNullOrWhiteSpace(epoch)||string.IsNullOrWhiteSpace(attemptId)||string.IsNullOrWhiteSpace(leaseId)
            || leaseDuration<TimeSpan.FromSeconds(30)||leaseDuration>TimeSpan.FromMinutes(10)||epochMaximum is <1 or >10 || lifetimeMaximum<epochMaximum || lifetimeMaximum>20)
            throw new ArgumentException("Invalid bounded scan claim.");
        if(Status is "SCANNING" or "NEEDS_INTERVENTION" || NextAttemptAt>now || LifetimeAttempts>=lifetimeMaximum)return false;
        var changed=Epoch!=epoch;
        if(!changed && (Status is "CLEAN" or "UNSAFE" or "INVALID_PDF" || EpochAttempts>=epochMaximum))return false;
        if(changed && Status is "UNSAFE" or "INVALID_PDF" && !allowTerminalRescan)return false;
        if(changed)EpochAttempts=0;
        Epoch=epoch;AttemptId=attemptId;LeaseId=leaseId;LeaseExpiresAt=now+leaseDuration;Status="SCANNING";
        LifetimeAttempts++;EpochAttempts++;ResourceVersion=checked(ResourceVersion+1);return true;
    }
    internal bool TryComplete(string attemptId,string leaseId,string epoch,string terminal,DateTimeOffset now,TimeSpan retryAfter)
    {
        if(terminal is not ("CLEAN" or "UNSAFE" or "INVALID_PDF" or "SCAN_FAILED") || retryAfter<TimeSpan.Zero || retryAfter>TimeSpan.FromHours(1))throw new ArgumentException("Invalid terminal scan outcome.");
        if(Status!="SCANNING" || AttemptId!=attemptId || LeaseId!=leaseId || Epoch!=epoch || LeaseExpiresAt is null || now>=LeaseExpiresAt)return false;
        Status=terminal;LeaseId=null;LeaseExpiresAt=null;NextAttemptAt=terminal=="SCAN_FAILED"?now+retryAfter:now;ResourceVersion=checked(ResourceVersion+1);return true;
    }
    // The owner must append an immutable LEASE_EXPIRED result in the same transaction as this update.
    internal bool TryExpireLease(string attemptId,string leaseId,DateTimeOffset now,TimeSpan retryAfter)
    {
        if(retryAfter<TimeSpan.Zero || retryAfter>TimeSpan.FromHours(1))throw new ArgumentException("Invalid recovery backoff.");
        if(Status!="SCANNING" || AttemptId!=attemptId || LeaseId!=leaseId || LeaseExpiresAt is null || now<LeaseExpiresAt)return false;
        Status="SCAN_FAILED";LeaseId=null;LeaseExpiresAt=null;NextAttemptAt=now+retryAfter;ResourceVersion=checked(ResourceVersion+1);return true;
    }
}
