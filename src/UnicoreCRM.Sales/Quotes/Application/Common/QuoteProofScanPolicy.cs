using System.Text.Json;
using Microsoft.Extensions.Options;
namespace UnicoreCRM.Sales.Quotes.Application.Common;
internal sealed class QuoteProofScanOptions
{
    public bool Enabled { get; set; }
    public string RuntimeManifestPath { get; set; }="";
    public string RuntimeManifestSha256 { get; set; }="";
    public int PollMilliseconds { get; set; }=2000;
    public Dictionary<string,QuoteProofScanWorkspacePolicy> Workspaces { get; set; }=new(StringComparer.Ordinal);
}
internal sealed class QuoteProofScanWorkspacePolicy
{
    public bool Enabled { get; set; }
    public string Version { get; set; }="";
    public int SignatureFreshnessHours { get; set; }=24;
    public int EpochAttempts { get; set; }=3;
    public int LifetimeAttempts { get; set; }=12;
    public int LeaseSeconds { get; set; }=90;
    public int FirstBackoffSeconds { get; set; }=30;
    public int SubsequentBackoffSeconds { get; set; }=120;
}
internal sealed record QuoteProofScanPolicy(string Version,string Digest,string RuntimeDigest,string Epoch,int FreshnessHours,int EpochAttempts,int LifetimeAttempts,int LeaseSeconds,int FirstBackoffSeconds,int SubsequentBackoffSeconds);
internal interface IQuoteProofScanPolicyReader { QuoteProofScanPolicy? Read(string workspace); IReadOnlyList<string> EnabledWorkspaces(); }
internal sealed class QuoteProofScanPolicyReader(IOptionsMonitor<QuoteProofScanOptions> options):IQuoteProofScanPolicyReader
{
    private QuoteProofScanOptions? Options(){try{return options.CurrentValue;}catch(Exception e)when(e is InvalidOperationException or OptionsValidationException){return null;}}
    public IReadOnlyList<string> EnabledWorkspaces()=>Options() is {Enabled:true,Workspaces:{} ws}?ws.Keys.Where(w=>Read(w)is not null).Take(100).ToArray():[];
    public QuoteProofScanPolicy? Read(string workspace)
    {
        var config=Options();if(config is not {Enabled:true} || config.RuntimeManifestSha256.Length!=64 || config.RuntimeManifestSha256.Any(c=>!Uri.IsHexDigit(c))
            || config.Workspaces?.FirstOrDefault(x=>x.Key==workspace).Value is not {Enabled:true} p || string.IsNullOrWhiteSpace(p.Version)||p.Version.Length>100||p.Version.Any(char.IsControl)
            || p.SignatureFreshnessHours is <1 or >24 || p.EpochAttempts is <1 or >3 || p.LifetimeAttempts<p.EpochAttempts || p.LifetimeAttempts>12 || p.LeaseSeconds is <60 or >600
            || p.FirstBackoffSeconds is <1 or >600 || p.SubsequentBackoffSeconds is <1 or >3600)return null;
        var digest=QuoteApprovalAssessment.Hash(JsonSerializer.Serialize(new{p.Version,p.SignatureFreshnessHours,p.EpochAttempts,p.LifetimeAttempts,p.LeaseSeconds,p.FirstBackoffSeconds,p.SubsequentBackoffSeconds,
            structuralPolicy="STATIC_TEXT_VECTOR_QPDF12_4_2_V1",maxPages=100,maxObjects=10000,maxNesting=64,maxDecodedBytes=33554432},QuotePricing.Json));
        var runtime=config.RuntimeManifestSha256.ToUpperInvariant();return new(p.Version,digest,runtime,QuoteApprovalAssessment.Hash(digest+"|"+runtime),p.SignatureFreshnessHours,p.EpochAttempts,p.LifetimeAttempts,p.LeaseSeconds,p.FirstBackoffSeconds,p.SubsequentBackoffSeconds);
    }
}
