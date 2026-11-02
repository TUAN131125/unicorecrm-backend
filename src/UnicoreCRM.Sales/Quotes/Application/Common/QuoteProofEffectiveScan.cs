using System.Text.Json;
using UnicoreCRM.Sales.Quotes.Contracts;
using UnicoreCRM.Sales.Quotes.Domain;
namespace UnicoreCRM.Sales.Quotes.Application.Common;

internal sealed class QuoteProofEffectiveScan(IQuoteProofScanStore store,IQuoteProofScanPolicyReader policies,IQuoteProofRuntimeAuthority runtime,TimeProvider clock)
{
    internal async Task<IReadOnlyDictionary<string,QuoteProofSecurity>> ReadAsync(string w,IReadOnlyList<QuoteAcceptanceProof> proofs,bool canContent,CancellationToken ct)
    {
        var jobs=await store.JobsAsync(w,proofs.Select(p=>p.ProofId).ToArray(),ct);
        var ids=jobs.Where(j=>j.LastResultAttemptId is not null).Select(j=>j.LastResultAttemptId!).ToArray();
        var results=await store.ResultsAsync(w,ids,ct);var attempts=await store.AttemptsAsync(w,ids,ct);
        var policy=policies.Read(w);var trusted=policy is null?null:await runtime.ReadSnapshotAsync(ct);var now=clock.GetUtcNow();
        return proofs.ToDictionary(p=>p.ProofId,p=>
        {
            var j=jobs.SingleOrDefault(x=>x.ProofId==p.ProofId);var r=results.SingleOrDefault(x=>x.AttemptId==j?.LastResultAttemptId);
            var a=attempts.SingleOrDefault(x=>x.AttemptId==j?.LastResultAttemptId);
            var valid=policy is not null && QuoteProofScanCoordinator.Fresh(trusted,policy,now) && j is {Status:"CLEAN"} && j.Epoch==policy.Epoch
                && j.WorkspaceId==p.WorkspaceId && j.CaseId==p.CaseId && j.InputSha256==p.Sha256 && j.InputLength==p.FileSize
                && a is not null && a.WorkspaceId==p.WorkspaceId && a.CaseId==p.CaseId && a.ProofId==p.ProofId && a.InputSha256==p.Sha256 && a.InputLength==p.FileSize
                && a.Epoch==policy.Epoch && a.PolicyVersion==policy.Version && a.PolicyDigest==policy.Digest && a.RuntimeDigest==policy.RuntimeDigest
                && r is {Outcome:"CLEAN",ParserStatus:"PASS",MalwareVerdict:"CLEAN"} && r.WorkspaceId==p.WorkspaceId && r.CaseId==p.CaseId && r.ProofId==p.ProofId
                && r.CompletedAt>=a.StartedAt && r.CompletedAt<a.LeaseExpiresAt && r.CompletedAt<=now && Provenance(r,trusted!);
            var reason=j?.Status=="CLEAN" && !valid?"EFFECTIVE_CLEAN_RESCAN_REQUIRED":j?.InterventionReason??r?.ReasonCode??"SCAN_PENDING";
            var status=j?.Status=="CLEAN" && !valid?"SCAN_PENDING":j?.Status??"SCAN_PENDING";
            return new QuoteProofSecurity(p.ProofId,p.StorageStatus,status,canContent && valid,policy is not null && QuoteProofScanCoordinator.Fresh(trusted,policy,now),
                r?.ParserStatus??"NOT_COMPLETED",r?.MalwareVerdict??"NOT_COMPLETED",reason,r?.CompletedAt.UtcDateTime.ToString("O"),a?.PolicyVersion,valid);
        },StringComparer.Ordinal);
    }
    private static bool Provenance(QuoteProofScanResult r,QuoteProofRuntimeSnapshot trusted)
    {
        try
        {
            using var doc=JsonDocument.Parse(r.ProvenanceJson);var s=doc.RootElement.GetProperty("scanner");var p=doc.RootElement.GetProperty("parser");
            return s.GetProperty("scannerIdentity").GetString()=="CLAMAV_INSTREAM" && s.GetProperty("runtimeDigest").GetString()==trusted.Digest
                && s.GetProperty("engineVersion").GetString()==trusted.EngineVersion && s.GetProperty("signatureVersion").GetString()==trusted.SignatureVersion
                && s.GetProperty("signatureUpdatedAt").GetDateTimeOffset()==trusted.SignatureUpdatedAt && p.GetProperty("parserIdentity").GetString()=="qpdf"
                && p.GetProperty("parserVersion").GetString()=="qpdf version 12.4.2" && p.GetProperty("parserSha256").GetString()==trusted.QpdfSha256;
        }
        catch(Exception e)when(e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException){return false;}
    }
}
