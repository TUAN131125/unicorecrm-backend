using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.FileProviders;
using UnicoreCRM.Sales.Quotes.Application.Common;
using UnicoreCRM.Sales.Quotes.Contracts;
using UnicoreCRM.Sales.Quotes.Domain;
using Xunit;
namespace UnicoreCRM.ArchitectureTests;
public sealed class QuoteManualSourceWorkflowTests
{
    private static readonly DateTimeOffset Now=DateTimeOffset.Parse("2026-10-27T01:00:00Z");
    [Theory][InlineData(-24,true)][InlineData(-25,false)][InlineData(1,false)][InlineData(0,true)]
    public void Freshness_uses_observation_not_later_recording(int hours,bool expected)=>Assert.Equal(expected,QuoteManualSourceWorkflow.Fresh(Now.AddHours(hours).ToString("yyyy-MM-ddTHH:mm:ssZ"),Now));
    [Fact]public void Narrower_workspace_freshness_and_exclusive_end_are_enforced()
    {Assert.False(QuoteManualSourceWorkflow.Fresh(Now.AddHours(-2).UtcDateTime.ToString("O"),Now,1));Assert.False(QuoteManualSourceWorkflow.Fresh(Now.UtcDateTime.ToString("O"),Now,25));Assert.False(QuoteManualSourceWorkflow.Effective(Now.AddDays(-1).UtcDateTime.ToString("O"),Now.UtcDateTime.ToString("O"),Now));Assert.False(QuoteManualSourceWorkflow.Effective(Now.AddSeconds(1).UtcDateTime.ToString("O"),Now.AddDays(1).UtcDateTime.ToString("O"),Now));}
    [Fact]public void Observation_and_validity_boundaries_use_UTC_instants()
    {Assert.True(QuoteManualSourceWorkflow.Fresh(Now.AddHours(-24).UtcDateTime.ToString("O"),Now));Assert.False(QuoteManualSourceWorkflow.Fresh(Now.AddHours(-24).AddTicks(-1).UtcDateTime.ToString("O"),Now));Assert.False(QuoteManualSourceWorkflow.Fresh(Now.AddTicks(1).UtcDateTime.ToString("O"),Now));Assert.True(QuoteManualSourceWorkflow.Effective(Now.UtcDateTime.ToString("O"),Now.AddTicks(1).UtcDateTime.ToString("O"),Now));}
    [Theory][InlineData("Production",false)][InlineData("Staging",false)][InlineData("Development",true)]
    public void Config_flag_cannot_enable_simulation_on_operational_hosts(string environment,bool expected)
    {
        var reader=new QuoteIdentityPolicyReader(new Monitor(),new EnvironmentStub{EnvironmentName=environment},new Verification());Assert.Equal(expected,reader.Simulation("w"));Assert.False(reader.Simulation("foreign"));
    }
    [Fact]public void Simulation_requires_documentary_policy_and_hash_changes_when_that_policy_changes()
    {
        var verifier=new Verification();var reader=new QuoteIdentityPolicyReader(new Monitor(),new EnvironmentStub{EnvironmentName="Development"},verifier);var before=reader.Read("w");Assert.NotNull(before);verifier.Version="changed";Assert.NotEqual(before,reader.Read("w"));Assert.Null(new QuoteIdentityPolicyReader(new Monitor(),new EnvironmentStub{EnvironmentName="Development"}).Read("w"));
    }
    [Fact]public void Admission_subject_binding_is_stable_for_explicit_source_version_binding_but_not_scope_or_proof_changes()
    {
        var hash=new string('A',64);var c=new QuoteAcceptanceCase("w","c","q","root",1,"doc",hash,hash,"ORGANIZATION_ACCOUNT","org","p","creator",Now);
        var party=new QuoteAcceptancePartySnapshot("w","ORGANIZATION_ACCOUNT","org",1,"contact",1,"Synthetic", "relation","buyer",false,Now.AddDays(-1).UtcDateTime.ToString("O"),null,Now.UtcDateTime.ToString("O"),true);
        var b=new QuoteIdentityBinding("subject",party,[new("identity",hash,"SUPPORTING_DOCUMENT")],[new("authority",hash,"DELEGATION")],new(Now.AddDays(-1).UtcDateTime.ToString("O"),Now.AddDays(1).UtcDateTime.ToString("O"),"VND","1000","Quote purchase"),null,null,"creator",Now.UtcDateTime.ToString("O"));
        var fingerprint=QuoteManualSourceWorkflow.SubjectFingerprint(c,b);
        Assert.Equal(fingerprint,QuoteManualSourceWorkflow.SubjectFingerprint(c,b with{SourceId="source",SourceVersion=2}));
        Assert.NotEqual(fingerprint,QuoteManualSourceWorkflow.SubjectFingerprint(c,b with{AuthorityClaim=b.AuthorityClaim with{AmountLimit="999"}}));
        Assert.NotEqual(fingerprint,QuoteManualSourceWorkflow.SubjectFingerprint(c,b with{Party=party with{ContactVersion=2}}));
        Assert.NotEqual(fingerprint,QuoteManualSourceWorkflow.SubjectFingerprint(c,b with{AuthorityProofs=[new("different",hash,"DELEGATION")]}));
        Assert.NotEqual(QuoteManualSourceWorkflow.CheckFingerprint(c,b,"p1"),QuoteManualSourceWorkflow.CheckFingerprint(c,b with{SourceVersion=2},"p1"));
        Assert.NotEqual(QuoteManualSourceWorkflow.CheckFingerprint(c,b,"p1"),QuoteManualSourceWorkflow.CheckFingerprint(c,b,"p2"));
    }
    private sealed class Verification:IQuoteVerifiedAcceptancePolicyReader{public string Version="v1";public QuoteVerifiedAcceptancePolicy? Read(string w)=>new(Version,24,new Dictionary<string,decimal>{{"VND",100000000}});}
    private sealed class Monitor:IOptionsMonitor<QuoteIdentityOptions>{public QuoteIdentityOptions CurrentValue{get;}=new(){Workspaces=new(){{"w",new(){Enabled=true,Version="v1",ManualDevelopmentSimulation=true}}}};public QuoteIdentityOptions Get(string? n)=>CurrentValue;public IDisposable? OnChange(Action<QuoteIdentityOptions,string?> l)=>null;}
    private sealed class EnvironmentStub:IHostEnvironment{public string EnvironmentName{get;set;}="Development";public string ApplicationName{get;set;}="test";public string ContentRootPath{get;set;}="";public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();}
}
