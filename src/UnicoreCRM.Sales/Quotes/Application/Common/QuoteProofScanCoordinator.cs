using System.Security.Cryptography;
using System.Text.Json;
using UnicoreCRM.Sales.Quotes.Contracts;
using UnicoreCRM.Sales.Quotes.Domain;
namespace UnicoreCRM.Sales.Quotes.Application.Common;

internal sealed class QuoteProofScanCoordinator(IQuoteProofScanStore store,IQuotesPersistence persistence,IQuoteProofScanPolicyReader policies,
    IQuoteProofRuntimeAuthority runtime,IQuoteProofStructuralValidator parser,ITrustedProofScanner scanner,IQuoteProofScanCommitProbe probe,TimeProvider clock)
{
    internal async Task RunAsync(string workspace,string proofId,CancellationToken stopping)
    {
        var policy=policies.Read(workspace);if(policy is null)return;
        QuoteProofScanAttempt attempt;
        await using(var tx=await persistence.BeginAsync(stopping))
        {
            await persistence.LockAsync(Lock(workspace,proofId),stopping);
            var proof=await store.ProofAsync(workspace,proofId,stopping);if(proof is null)return;
            var now=clock.GetUtcNow();var job=await store.JobAsync(workspace,proofId,true,stopping);
            if(job is null){job=new(workspace,proof.CaseId,proof.ProofId,proof.Sha256,proof.FileSize,now);store.AddJob(job);}
            if(job.Status=="SCANNING")
            {
                if(job.LeaseExpiresAt>now)return;
                var expired=await store.AttemptAsync(workspace,job.AttemptId!,stopping);
                if(expired is null || await store.ResultAsync(workspace,expired.AttemptId,stopping)is not null)return;
                if(!job.TryExpireLease(expired.AttemptId,expired.LeaseId,now,Backoff(policy,job.EpochAttempts)))return;
                store.AddResult(new(expired,"SCAN_FAILED","SCAN_LEASE_EXPIRED","NOT_COMPLETED","NOT_COMPLETED","{}",now));
                job.BindResult(expired.AttemptId);job.MarkExhausted(policy.EpochAttempts,policy.LifetimeAttempts);
                await RecordAsync(proof,job,expired,"LEASE_EXPIRED",now,stopping);
                await persistence.SaveChangesAsync(stopping);probe.AfterResultFlush(workspace);await tx.CommitAsync(stopping);return;
            }
            // Budget exhaustion is sticky. A new epoch cannot reset an exhausted job.
            if((job.Status=="SCAN_FAILED" || job.Status=="CLEAN" && job.Epoch!=policy.Epoch && job.LifetimeAttempts>=policy.LifetimeAttempts)
                && job.MarkExhausted(policy.EpochAttempts,policy.LifetimeAttempts))
            {await persistence.SaveChangesAsync(stopping);await tx.CommitAsync(stopping);return;}
            if(!job.TryClaim(policy.Epoch,"qsa_"+Guid.NewGuid().ToString("N"),"qsl_"+Guid.NewGuid().ToString("N"),now,
                TimeSpan.FromSeconds(policy.LeaseSeconds),policy.EpochAttempts,policy.LifetimeAttempts))return;
            attempt=new(job,policy.Version,policy.Digest,policy.RuntimeDigest,now);store.AddAttempt(attempt);
            await RecordAsync(proof,job,attempt,"CLAIMED",now,stopping);
            await persistence.SaveChangesAsync(stopping);await tx.CommitAsync(stopping);
        }
        // No SQL transaction is held while untrusted bytes are parsed or sent to the scanner.
        await probe.AfterClaimAsync(workspace,stopping);
        string outcome="SCAN_FAILED",reason="SCAN_WORK_FAILED",parserStatus="NOT_COMPLETED",malware="NOT_COMPLETED",provenance="{}";
        using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(stopping))
        {
            deadline.CancelAfter(TimeSpan.FromSeconds(45));
            byte[]? bytes=null;
            try
            {
                bytes=await store.ReadBytesAsync(workspace,attempt.CaseId,proofId,deadline.Token);
                if(bytes is null || bytes.Length!=attempt.InputLength || Convert.ToHexString(SHA256.HashData(bytes))!=attempt.InputSha256)reason="PROOF_INPUT_BINDING_MISMATCH";
                else
                {
                    var trusted=await runtime.ReadSnapshotAsync(deadline.Token);
                    if(!Fresh(trusted,policy,clock.GetUtcNow()))reason="SCANNER_RUNTIME_OR_SIGNATURE_UNAVAILABLE";
                    else
                    {
                        var structural=await parser.ValidateAsync(bytes,deadline.Token);parserStatus=structural.Status;reason=structural.ReasonCode;
                        provenance=JsonSerializer.Serialize(new{parser=new{structural.ParserIdentity,structural.ParserVersion,structural.ParserSha256,structural.DurationMs,structural.Inspection}},QuotePricing.Json);
                        var structuralBinding=structural.InputLength==attempt.InputLength && structural.InputSha256==attempt.InputSha256
                            && structural.ParserIdentity=="qpdf" && structural.ParserVersion=="qpdf version 12.4.2" && structural.ParserSha256==trusted!.QpdfSha256;
                        if(parserStatus=="PASS" && !structuralBinding){parserStatus="INDETERMINATE";reason="PARSER_INPUT_BINDING_MISMATCH";}
                        if(parserStatus=="INVALID_PDF")outcome="INVALID_PDF";
                        else if(parserStatus=="PASS")
                        {
                            using var stream=new MemoryStream(bytes,false);
                            var av=await scanner.ScanAsync(new(workspace,attempt.CaseId,proofId,attempt.InputSha256,attempt.InputLength),stream,deadline.Token);
                            malware=av.Verdict;reason=av.ReasonCode;
                            var current=await runtime.ReadSnapshotAsync(deadline.Token);
                            var avBound=av.ProofId==proofId && av.InputLength==attempt.InputLength && av.InputSha256==attempt.InputSha256 && av.RuntimeDigest==attempt.RuntimeDigest
                                && av.ScannerIdentity=="CLAMAV_INSTREAM" && av.EngineVersion==trusted!.EngineVersion && av.SignatureVersion==trusted.SignatureVersion
                                && av.SignatureUpdatedAt==trusted.SignatureUpdatedAt && av.CompletedAt>=av.StartedAt && av.StartedAt>=attempt.StartedAt;
                            if(av.ScanStatus is not (ProofScanState.CLEAN or ProofScanState.UNSAFE))outcome="SCAN_FAILED";
                            else if(!avBound || current!=trusted || !Fresh(current,policy,clock.GetUtcNow())){outcome="SCAN_FAILED";reason="SCAN_PROVENANCE_BINDING_INVALID";}
                            else outcome=av.ScanStatus==ProofScanState.UNSAFE && malware=="UNSAFE"?"UNSAFE":av.ScanStatus==ProofScanState.CLEAN && malware=="CLEAN"?"CLEAN":"SCAN_FAILED";
                            // Only bounded, server-generated provenance. Never tool stdout, storage paths or input bytes.
                            provenance=JsonSerializer.Serialize(new{parser=new{structural.ParserIdentity,structural.ParserVersion,structural.ParserSha256,structural.DurationMs,structural.Inspection},
                                scanner=new{av.ScanId,av.ScannerIdentity,av.EngineVersion,av.SignatureVersion,av.SignatureUpdatedAt,av.RuntimeDigest,av.StartedAt,av.CompletedAt,av.DurationMs}},QuotePricing.Json);
                        }
                    }
                }
            }
            catch(OperationCanceledException)when(!stopping.IsCancellationRequested){reason="SCAN_WORK_DEADLINE_EXCEEDED";}
            catch(Exception e)when(e is IOException or UnauthorizedAccessException or CryptographicException or InvalidOperationException or JsonException){reason="SCAN_WORK_DEPENDENCY_FAILED";}
            finally{if(bytes is not null)CryptographicOperations.ZeroMemory(bytes);}
        }
        stopping.ThrowIfCancellationRequested();
        await using(var tx=await persistence.BeginAsync(stopping))
        {
            await persistence.LockAsync(Lock(workspace,proofId),stopping);
            var job=await store.JobAsync(workspace,proofId,true,stopping);var now=clock.GetUtcNow();
            if(job is null || await store.ResultAsync(workspace,attempt.AttemptId,stopping)is not null)return;
            if(policies.Read(workspace)!=policy){outcome="SCAN_FAILED";reason="SCAN_POLICY_CHANGED_DURING_WORK";}
            if(outcome=="CLEAN" && !Fresh(await runtime.ReadSnapshotAsync(stopping),policy,clock.GetUtcNow())){outcome="SCAN_FAILED";reason="SCAN_AUTHORITY_CHANGED_BEFORE_COMMIT";}
            now=clock.GetUtcNow();
            if(!job.TryComplete(attempt.AttemptId,attempt.LeaseId,attempt.Epoch,outcome,now,Backoff(policy,job.EpochAttempts)))return;
            store.AddResult(new(attempt,outcome,reason,parserStatus,malware,provenance,now));job.BindResult(attempt.AttemptId);
            if(outcome=="SCAN_FAILED")job.MarkExhausted(policy.EpochAttempts,policy.LifetimeAttempts);
            var proof=await store.ProofAsync(workspace,proofId,stopping);
            await RecordAsync(proof!,job,attempt,outcome,now,stopping);
            await persistence.SaveChangesAsync(stopping);probe.AfterResultFlush(workspace);await tx.CommitAsync(stopping);
        }
    }
    internal static bool Fresh(QuoteProofRuntimeSnapshot? runtime,QuoteProofScanPolicy policy,DateTimeOffset now)=>runtime is not null && runtime.Digest==policy.RuntimeDigest
        && runtime.EngineVersion=="1.5.4" && runtime.QpdfSha256.Length==64 && runtime.QpdfSha256.All(Uri.IsHexDigit)
        && !string.IsNullOrWhiteSpace(runtime.SignatureVersion) && runtime.SignatureUpdatedAt<=now && runtime.SignatureUpdatedAt>=now.AddHours(-policy.FreshnessHours);
    private static string Lock(string w,string p)=>"proof-scan:"+w.ToLowerInvariant()+":"+p.ToLowerInvariant();
    private static TimeSpan Backoff(QuoteProofScanPolicy p,int attempts)=>TimeSpan.FromSeconds(attempts<=1?p.FirstBackoffSeconds:p.SubsequentBackoffSeconds);
    private async Task RecordAsync(QuoteAcceptanceProof proof,QuoteProofScanJob job,QuoteProofScanAttempt attempt,string action,DateTimeOffset now,CancellationToken ct)
    {
        // Cases belong to this owner; the worker never reads another owner's tables.
        var quoteId=await store.CaseQuoteIdAsync(proof.WorkspaceId,proof.CaseId,ct)??throw new InvalidOperationException("Proof case binding unavailable.");
        var payload=JsonSerializer.Serialize(new{quoteId,proof.CaseId,proof.ProofId,attempt.AttemptId,action,job.Status,job.ResourceVersion,attempt.PolicyVersion},QuotePricing.Json);
        var id=attempt.AttemptId+"_"+action;var scope=QuoteApprovalAssessment.Hash("proof-scan|"+proof.WorkspaceId+"|"+id);
        persistence.AddCommand(new(scope,proof.WorkspaceId,"SYSTEM_QUOTE_PROOF_SCANNER","scanQuoteAcceptanceProof",quoteId,
            QuoteApprovalAssessment.Hash(payload),payload,"audit_"+id,id,id,job.ResourceVersion-1,job.ResourceVersion,now));
        persistence.AddOutbox(new("event_"+id,proof.WorkspaceId,quoteId,"QuoteAcceptanceProofScan"+action,id,payload,now));
    }
}
