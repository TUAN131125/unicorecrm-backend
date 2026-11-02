using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using UnicoreCRM.Sales.Quotes.Domain;
namespace UnicoreCRM.Sales.Quotes.Application.Common;

internal interface IQuoteIdentityPersistence
{
    Task<QuoteIdentityVersion?> CurrentAsync(string w,string c,CancellationToken ct);
    Task<IReadOnlyList<QuoteIdentityVersion>> HistoryAsync(string w,string c,int offset,int limit,CancellationToken ct);
    Task<QuoteEvidenceSourceVersion?> SourceAsync(string w,string sourceId,CancellationToken ct);
    Task<IReadOnlyList<QuoteEvidenceSourceVersion>> SourceHistoryAsync(string w,string sourceId,int offset,int limit,CancellationToken ct);
    Task<IReadOnlyList<QuoteEvidenceSourceVersion>> SourcesAsync(string w,string buyerType,string buyerId,int offset,int limit,CancellationToken ct);
    Task<IReadOnlyList<QuoteAcceptanceProof>> ProofsAsync(string w,string c,IReadOnlyList<string> ids,CancellationToken ct);
    Task<QuoteIdentityVersion?> ConfirmationAsync(string w,string c,string proofId,string sha256,CancellationToken ct);
    Task<IReadOnlyList<QuoteIdentityVersion>> ReviewsAsync(string w,string c,int offset,int limit,CancellationToken ct);
    Task<QuoteIdentityVersion?> LatestKindAsync(string w,string c,string kind,CancellationToken ct);
    void AddVersion(QuoteIdentityVersion row);void AddSource(QuoteEvidenceSourceVersion row);
}
internal sealed class QuoteIdentityOptions{public Dictionary<string,QuoteIdentityWorkspacePolicy> Workspaces{get;set;}=new(StringComparer.Ordinal);}
internal sealed class QuoteIdentityWorkspacePolicy{public bool Enabled{get;set;}public string Version{get;set;}="";public bool ManualDevelopmentSimulation{get;set;}public bool IndependentReviewSimulation{get;set;}public string ReviewPolicyVersion{get;set;}="";}
internal interface IQuoteIdentityPolicyReader{string? Read(string workspace); bool Simulation(string workspace)=>false; string? ReviewPolicy(string workspace)=>null;}
internal sealed class QuoteIdentityPolicyReader(IOptionsMonitor<QuoteIdentityOptions> options,IHostEnvironment? environment=null,IQuoteVerifiedAcceptancePolicyReader? verification=null):IQuoteIdentityPolicyReader
{
    public bool Simulation(string workspace)
    {try{return environment?.IsDevelopment()==true && options.CurrentValue.Workspaces?.FirstOrDefault(x=>x.Key==workspace).Value is {Enabled:true,ManualDevelopmentSimulation:true};}catch(Exception e)when(e is InvalidOperationException or OptionsValidationException){return false;}}
    public string? ReviewPolicy(string workspace)
    {
        try
        {
            var p=options.CurrentValue.Workspaces?.FirstOrDefault(x=>x.Key==workspace).Value;
            return Simulation(workspace) && Read(workspace)is {} identity && p is {IndependentReviewSimulation:true} && !string.IsNullOrWhiteSpace(p.ReviewPolicyVersion) && p.ReviewPolicyVersion.Length<=80 && !p.ReviewPolicyVersion.Any(char.IsControl)
                ?p.ReviewPolicyVersion+":"+QuoteApprovalAssessment.Hash(identity+"|B6-DOC-REVIEW-v1|D1_IDENTITY_AUTHORITY_CONSENT|D2_DUTIES|D3_PROVENANCE|D4_COMMERCIAL|D5_ISOLATION|NO_OPERATIONAL_VERIFICATION"):null;
        }
        catch(Exception e)when(e is InvalidOperationException or OptionsValidationException){return null;}
    }
    public string? Read(string workspace)
    {
        try
        {
            var p=options.CurrentValue.Workspaces?.FirstOrDefault(x=>x.Key==workspace).Value;
            if(Simulation(workspace)&&verification?.Read(workspace)is null)return null;
            return p is {Enabled:true} && !string.IsNullOrWhiteSpace(p.Version) && p.Version.Length<=100 && !p.Version.Any(char.IsControl)
                ?p.Version+":"+QuoteApprovalAssessment.Hash(Simulation(workspace)?"ORGANIZATION_ISSUED_AUTHORITY_WITH_INDEPENDENT_SOURCE_CHECK|DEVELOPMENT_SIMULATION|NO_OPERATIONAL_VERIFICATION|"+verification?.Read(workspace)?.Version:"CASE_BOUND_DOCUMENTARY_EVIDENCE_V1|NO_ADMITTED_POSITIVE_SOURCE|PURPOSE_QUOTE_ACCEPTANCE|IMMUTABLE_HISTORY"):null;
        }
        catch(Exception e)when(e is InvalidOperationException or OptionsValidationException){return null;}
    }
}
internal interface IQuoteIdentityCommitProbe{void AfterFlush(string workspace);}
internal sealed class DevelopmentQuoteIdentityCommitProbe(string? workspace=null,bool fail=false):IQuoteIdentityCommitProbe
{public void AfterFlush(string w){if(w==workspace && fail)throw new InvalidOperationException("ISOLATED_IDENTITY_SQL_ROLLBACK_PROBE");}}
