using System.Text.Json;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using UnicoreCRM.Sales.Quotes.Application.Common;
using UnicoreCRM.Sales.Quotes.Contracts;
using Xunit;
namespace UnicoreCRM.ArchitectureTests;
public sealed class QuoteIndependentReviewTests
{
    private static SubmitQuoteIndependentReview Request()=>new("APPROVE_SIMULATION","Synthetic documentary review","doc",1,new string('A',64),"subject",3,
        QuoteIndependentReviewRules.Criteria.Select(c=>new QuoteReviewFinding(c,"PASS","Retained artifact and procedure reviewed",["consent"])).ToArray(),new(["consent"],"2026-10-27T00:00:00Z","Synthetic retained customer-origin artifact","Signed unconditional assent to exact B5 document","EXPLICIT_UNCONDITIONAL_SIGNED_ASSENT"),ReviewBindingFingerprint:new string('A',64));
    [Theory][InlineData("D1")][InlineData("D2")][InlineData("D3")][InlineData("D4")][InlineData("D5")]
    public void Every_canonical_criterion_requires_outcome_rationale_and_artifact(string criterion)
    {
        var r=Request();Assert.True(QuoteIndependentReviewRules.Valid(r));Assert.True(QuoteIndependentReviewRules.Complete(r));
        Assert.False(QuoteIndependentReviewRules.Complete(r with{Findings=r.Findings.Where(f=>f.Criterion!=criterion).ToArray()}));
        Assert.False(QuoteIndependentReviewRules.Complete(r with{Findings=r.Findings.Select(f=>f.Criterion==criterion?f with{Outcome="INSUFFICIENT"}:f).ToArray()}));
        Assert.False(QuoteIndependentReviewRules.Complete(r with{Findings=r.Findings.Select(f=>f.Criterion==criterion?f with{ProofIds=[]}:f).ToArray()}));
        Assert.False(QuoteIndependentReviewRules.Valid(r with{Findings=r.Findings.Select(f=>f.Criterion==criterion?f with{Rationale=""}:f).ToArray()}));
    }
    [Theory][InlineData("creator")][InlineData("requester")][InlineData("case-recorder")][InlineData("proof-uploader")][InlineData("identity-recorder")][InlineData("source-creator")][InlineData("source-preparer")][InlineData("source-checker")]
    public void Owner_role_cannot_override_any_forbidden_participation(string actor)=>Assert.False(Independent(actor));
    [Fact]public void Otherwise_independent_B1_or_source_approver_is_permitted_by_approved_matrix()=>Assert.True(Independent("independent-approver"));
    private static bool Independent(string actor)=>QuoteIndependentReviewRules.Independent(actor,"creator","requester","case-recorder",["proof-uploader"],"identity-recorder","source-creator","source-preparer","source-checker");
    [Fact]public void Checkboxes_cannot_replace_consent_or_promote_trust()
    {
        var r=Request();Assert.False(QuoteIndependentReviewRules.Complete(r with{Consent=null}));
        Assert.False(QuoteIndependentReviewRules.Complete(r with{Consent=r.Consent! with{Finding="CONDITIONAL_OR_NEGATIVE"}}));
        Assert.False(QuoteIndependentReviewRules.Valid(r with{Decision="VERIFIED"}));
        Assert.Throws<JsonException>(()=>JsonSerializer.Deserialize<SubmitQuoteIndependentReview>(JsonSerializer.Serialize(r,JsonSerializerOptions.Web).TrimEnd('}')+",\"reviewedBy\":\"owner\"}",JsonSerializerOptions.Web));
    }
    [Fact]public void Negative_decisions_need_reason_but_do_not_need_positive_findings()
    {var r=Request() with{Decision="REQUEST_MORE_EVIDENCE",Findings=[],Consent=null};Assert.True(QuoteIndependentReviewRules.Valid(r));Assert.False(QuoteIndependentReviewRules.Complete(r));Assert.False(QuoteIndependentReviewRules.Valid(r with{Reason=""}));}
    [Theory][InlineData("Production",false)][InlineData("Staging",false)][InlineData("Development",true)]
    public void Positive_policy_requires_explicit_trusted_development_configuration(string environment,bool enabled)
    {
        var config=new Monitor();var reader=new QuoteIdentityPolicyReader(config,new Host{EnvironmentName=environment},new Verification());Assert.Equal(enabled,reader.ReviewPolicy("w")is not null);Assert.Null(reader.ReviewPolicy("other"));
        var before=reader.ReviewPolicy("w");config.CurrentValue.Workspaces["w"].ReviewPolicyVersion="v2";if(enabled)Assert.NotEqual(before,reader.ReviewPolicy("w"));
        config.CurrentValue.Workspaces["w"].IndependentReviewSimulation=false;Assert.Null(reader.ReviewPolicy("w"));
    }
    private sealed class Verification:IQuoteVerifiedAcceptancePolicyReader{public QuoteVerifiedAcceptancePolicy? Read(string w)=>new("v",24,new Dictionary<string,decimal>{{"VND",100000000}});}
    private sealed class Monitor:IOptionsMonitor<QuoteIdentityOptions>{public QuoteIdentityOptions CurrentValue{get;}=new(){Workspaces=new(){{"w",new(){Enabled=true,Version="v1",ManualDevelopmentSimulation=true,IndependentReviewSimulation=true,ReviewPolicyVersion="review-v1"}}}};public QuoteIdentityOptions Get(string? n)=>CurrentValue;public IDisposable? OnChange(Action<QuoteIdentityOptions,string?> l)=>null;}
    private sealed class Host:IHostEnvironment{public string EnvironmentName{get;set;}="Development";public string ApplicationName{get;set;}="tests";public string ContentRootPath{get;set;}="";public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();}
}
