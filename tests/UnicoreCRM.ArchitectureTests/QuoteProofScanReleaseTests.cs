using System.Text.Json;
using Microsoft.Extensions.Options;
using UnicoreCRM.Sales.Quotes.Application.Common;
using UnicoreCRM.Sales.Quotes.Domain;
using Xunit;
namespace UnicoreCRM.ArchitectureTests;

public sealed class QuoteProofScanReleaseTests
{
    private static readonly DateTimeOffset Now=DateTimeOffset.Parse("2026-10-26T09:00:00Z");
    private const string Hash="AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string RuntimeHash="BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
    private const string ParserHash="CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC";
    private static QuoteProofScanPolicy Policy()=>new("v1",Hash,RuntimeHash,"epoch",24,3,12,90,30,120);
    private static QuoteProofRuntimeSnapshot Runtime()=>new(RuntimeHash,"1.5.4","28165",Now.AddHours(-2),ParserHash);
    [Theory][InlineData(-25,false)][InlineData(-24,true)][InlineData(1,false)]
    public void Signature_freshness_is_checked_again_at_release(int hours,bool expected)=>Assert.Equal(expected,QuoteProofScanCoordinator.Fresh(Runtime()with{SignatureUpdatedAt=Now.AddHours(hours)},Policy(),Now));
    [Fact]public void Unknown_engine_or_missing_parser_pin_cannot_authorize_a_clean_release()
    {
        Assert.False(QuoteProofScanCoordinator.Fresh(Runtime()with{EngineVersion=""},Policy(),Now));
        Assert.False(QuoteProofScanCoordinator.Fresh(Runtime()with{QpdfSha256=""},Policy(),Now));
        Assert.False(QuoteProofScanCoordinator.Fresh(Runtime()with{Digest=Hash},Policy(),Now));
    }
    [Theory][InlineData("hash")][InlineData("policy")][InlineData("runtime")][InlineData("parser")][InlineData("signature")][InlineData("case")][InlineData("pending")]
    public async Task Constructed_clean_ledger_cannot_release_a_mismatched_binding(string mismatch)
    {
        var proof=new QuoteAcceptanceProof("w","c","p","SUPPORTING_DOCUMENT","proof.pdf",610,Hash,"storage-v1","actor",Now.AddMinutes(-5));
        var job=new QuoteProofScanJob("w",mismatch=="case"?"other":"c","p",mismatch=="hash"?ParserHash:Hash,610,Now.AddMinutes(-1));
        Assert.True(job.TryClaim("epoch","attempt","lease",Now.AddMinutes(-1),TimeSpan.FromSeconds(90),3,12));
        var attempt=new QuoteProofScanAttempt(job,mismatch=="policy"?"old":"v1",Hash,RuntimeHash,Now.AddMinutes(-1));
        var runtime=Runtime();var provenance=JsonSerializer.Serialize(new{parser=new{parserIdentity="qpdf",parserVersion="qpdf version 12.4.2",parserSha256=mismatch=="parser"?Hash:ParserHash},
            scanner=new{scannerIdentity="CLAMAV_INSTREAM",runtimeDigest=mismatch=="runtime"?Hash:RuntimeHash,engineVersion="1.5.4",signatureVersion=mismatch=="signature"?"old":"28165",signatureUpdatedAt=runtime.SignatureUpdatedAt}},QuotePricing.Json);
        var result=new QuoteProofScanResult(attempt,"CLEAN","CLAMAV_SCAN_CLEAN","PASS","CLEAN",provenance,Now.AddSeconds(-30));job.BindResult(attempt.AttemptId);
        if(mismatch!="pending")Assert.True(job.TryComplete("attempt","lease","epoch","CLEAN",Now.AddSeconds(-30),TimeSpan.Zero));
        var state=(await new QuoteProofEffectiveScan(new Store(job,attempt,result),new Policies(),new Authority(runtime),new Clock()).ReadAsync("w",[proof],true,CancellationToken.None))["p"];
        Assert.False(state.ContentAvailable);Assert.False(state.Reviewable);
    }
    [Fact]public async Task Permission_is_separate_from_an_exact_technical_clean_result()
    {
        var proof=new QuoteAcceptanceProof("w","c","p","SUPPORTING_DOCUMENT","proof.pdf",610,Hash,"storage-v1","actor",Now.AddMinutes(-5));
        var job=new QuoteProofScanJob("w","c","p",Hash,610,Now.AddMinutes(-1));Assert.True(job.TryClaim("epoch","a","l",Now.AddMinutes(-1),TimeSpan.FromSeconds(90),3,12));
        var a=new QuoteProofScanAttempt(job,"v1",Hash,RuntimeHash,Now.AddMinutes(-1));var runtime=Runtime();
        var provenance=JsonSerializer.Serialize(new{parser=new{parserIdentity="qpdf",parserVersion="qpdf version 12.4.2",parserSha256=ParserHash},scanner=new{scannerIdentity="CLAMAV_INSTREAM",runtimeDigest=RuntimeHash,engineVersion="1.5.4",signatureVersion="28165",signatureUpdatedAt=runtime.SignatureUpdatedAt}},QuotePricing.Json);
        var r=new QuoteProofScanResult(a,"CLEAN","CLAMAV_SCAN_CLEAN","PASS","CLEAN",provenance,Now.AddSeconds(-30));job.BindResult("a");Assert.True(job.TryComplete("a","l","epoch","CLEAN",Now.AddSeconds(-30),TimeSpan.Zero));
        var effective=new QuoteProofEffectiveScan(new Store(job,a,r),new Policies(),new Authority(runtime),new Clock());
        var denied=(await effective.ReadAsync("w",[proof],false,CancellationToken.None))["p"];Assert.True(denied.Reviewable);Assert.False(denied.ContentAvailable);
        Assert.True((await effective.ReadAsync("w",[proof],true,CancellationToken.None))["p"].ContentAvailable);
    }
    [Fact]public void Exhausted_clean_rescan_can_be_taken_out_of_the_queue_without_resetting_counters()
    {
        var job=new QuoteProofScanJob("w","c","p",Hash,610,Now);
        for(var i=0;i<12;i++){Assert.True(job.TryClaim("epoch"+i,"a"+i,"l"+i,Now,TimeSpan.FromSeconds(90),3,12));Assert.True(job.TryComplete("a"+i,"l"+i,"epoch"+i,"CLEAN",Now,TimeSpan.Zero));}
        Assert.False(job.TryClaim("new","a13","l13",Now,TimeSpan.FromSeconds(90),3,12));Assert.True(job.MarkExhausted(3,12));
        Assert.Equal("NEEDS_INTERVENTION",job.Status);Assert.Equal("LIFETIME_RETRY_EXHAUSTED",job.InterventionReason);Assert.Equal(12,job.LifetimeAttempts);
    }
    [Fact]public void Download_lease_releases_once_and_clears_plaintext_only_when_response_ownership_ends()
    {
        var bytes=new byte[]{1,2,3};var released=0;var file=new QuoteProofDownload(bytes,"p.pdf",Hash,()=>released++);
        Assert.Equal(0,released);Assert.Equal(3,file.Bytes[2]);file.Dispose();file.Dispose();Assert.Equal(1,released);Assert.All(bytes,b=>Assert.Equal(0,b));
    }
    private sealed class Clock:TimeProvider{public override DateTimeOffset GetUtcNow()=>Now;}
    private sealed class Policies:IQuoteProofScanPolicyReader{public QuoteProofScanPolicy? Read(string w)=>Policy();public IReadOnlyList<string> EnabledWorkspaces()=>["w"];}
    private sealed class Authority(QuoteProofRuntimeSnapshot r):IQuoteProofRuntimeAuthority{public Task<QuoteProofRuntimeSnapshot?> ReadSnapshotAsync(CancellationToken ct)=>Task.FromResult<QuoteProofRuntimeSnapshot?>(r);}
    private sealed class Store(QuoteProofScanJob j,QuoteProofScanAttempt a,QuoteProofScanResult r):IQuoteProofScanStore
    {
        public Task<IReadOnlyList<QuoteProofScanJob>> JobsAsync(string w,IReadOnlyList<string> p,CancellationToken ct)=>Task.FromResult<IReadOnlyList<QuoteProofScanJob>>([j]);
        public Task<IReadOnlyList<QuoteProofScanAttempt>> AttemptsAsync(string w,IReadOnlyList<string> p,CancellationToken ct)=>Task.FromResult<IReadOnlyList<QuoteProofScanAttempt>>([a]);
        public Task<IReadOnlyList<QuoteProofScanResult>> ResultsAsync(string w,IReadOnlyList<string> p,CancellationToken ct)=>Task.FromResult<IReadOnlyList<QuoteProofScanResult>>([r]);
        public Task<IReadOnlyList<string>> CandidatesAsync(string w,string e,DateTimeOffset n,int l,CancellationToken ct)=>throw new NotSupportedException();
        public Task<QuoteAcceptanceProof?> ProofAsync(string w,string p,CancellationToken ct)=>throw new NotSupportedException();
        public Task<string?> CaseQuoteIdAsync(string w,string c,CancellationToken ct)=>throw new NotSupportedException();
        public Task<QuoteProofScanJob?> JobAsync(string w,string p,bool t,CancellationToken ct)=>throw new NotSupportedException();
        public Task<QuoteProofScanAttempt?> AttemptAsync(string w,string a,CancellationToken ct)=>throw new NotSupportedException();
        public Task<QuoteProofScanResult?> ResultAsync(string w,string a,CancellationToken ct)=>throw new NotSupportedException();
        public Task<IReadOnlyList<QuoteProofScanAttempt>> HistoryAsync(string w,string p,int o,int l,CancellationToken ct)=>throw new NotSupportedException();
        public Task<byte[]?> ReadBytesAsync(string w,string c,string p,CancellationToken ct)=>throw new NotSupportedException();
        public void AddJob(QuoteProofScanJob j)=>throw new NotSupportedException();public void AddAttempt(QuoteProofScanAttempt a)=>throw new NotSupportedException();public void AddResult(QuoteProofScanResult r)=>throw new NotSupportedException();
    }
}
