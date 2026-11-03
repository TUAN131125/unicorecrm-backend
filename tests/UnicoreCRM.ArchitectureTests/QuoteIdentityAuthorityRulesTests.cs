using UnicoreCRM.Sales.Quotes.Domain;
using Xunit;
namespace UnicoreCRM.ArchitectureTests;

/// <summary>Synthetic scope/freshness facts only; no actual identity source or representative is verified.</summary>
public sealed class QuoteIdentityAuthorityRulesTests
{
    private static readonly DateTimeOffset Now=DateTimeOffset.Parse("2026-10-26T12:00:00Z");
    private const string Hash="AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private static QuoteIdentityCaseBinding Binding()=>new("w","c","q","root",1,"doc",Hash,Hash,"ORGANIZATION_ACCOUNT","buyer");

    [Theory]
    [InlineData("CONTACT","buyer",true)]
    [InlineData("ORGANIZATION_ACCOUNT","buyer",false)]
    [InlineData("CUSTOMER","buyer",false)]
    [InlineData("CONTACT","other",false)]
    public void Customer_account_or_affiliation_is_not_a_substitute_for_exact_buyer_identity(string type,string id,bool expected)=>
        Assert.Equal(expected,QuoteIdentityAuthorityRules.MatchesBuyer("CONTACT","buyer",type,id));

    [Fact]public void Exact_clean_reference_is_applicable_without_becoming_identity_or_acceptance_verification()=>
        Assert.True(QuoteIdentityAuthorityRules.MatchesCleanProof(Binding(),new(Binding(),"proof",Hash,true)));

    [Theory]
    [InlineData("workspace")][InlineData("case")][InlineData("quote")][InlineData("root")][InlineData("revision")]
    [InlineData("document")][InlineData("issuedHash")][InlineData("commercialHash")][InlineData("buyer")][InlineData("buyerType")]
    public void Existing_proof_of_another_binding_is_rejected(string field)
    {
        var actual=field switch
        {
            "workspace"=>Binding()with{WorkspaceId="foreign"},"case"=>Binding()with{CaseId="other"},
            "quote"=>Binding()with{QuoteId="other"},"root"=>Binding()with{RootQuoteId="other"},
            "revision"=>Binding()with{QuoteRevision=2},"document"=>Binding()with{DocumentId="other"},
            "issuedHash"=>Binding()with{IssuedPdfSha256=new string('B',64)},"commercialHash"=>Binding()with{CommercialFingerprint=new string('B',64)},
            "buyer"=>Binding()with{BuyerId="other"},_=>Binding()with{BuyerType="CONTACT"}
        };
        Assert.False(QuoteIdentityAuthorityRules.MatchesCleanProof(Binding(),new(actual,"proof",Hash,true)));
    }
    [Fact]public void Pending_unsafe_or_stale_effective_clean_and_invalid_hash_never_pass_proof_binding()
    {
        Assert.False(QuoteIdentityAuthorityRules.MatchesCleanProof(Binding(),new(Binding(),"proof",Hash,false)));
        Assert.False(QuoteIdentityAuthorityRules.MatchesCleanProof(Binding(),new(Binding(),"proof","client-claim",true)));
        Assert.False(QuoteIdentityAuthorityRules.MatchesCleanProof(Binding()with{QuoteRevision=0},new(Binding(),"proof",Hash,true)));
    }
    [Theory]
    [InlineData(100000000,100000000,true)]
    [InlineData(100000001,200000000,false)]
    [InlineData(1000,999,false)]
    [InlineData(-1,1000,false)]
    public void Workflow_ceiling_and_source_amount_limit_are_separate(decimal total,decimal sourceLimit,bool expected)=>
        Assert.Equal(expected,QuoteIdentityAuthorityRules.CoversAmount("VND",total,"VND",sourceLimit,new Dictionary<string,decimal>{{"VND",100000000m}}));
    [Fact]public void Missing_limit_never_means_unlimited_and_no_fx_or_implicit_currency_policy_exists()
    {
        var limits=new Dictionary<string,decimal>{{"VND",100000000m}};
        Assert.False(QuoteIdentityAuthorityRules.CoversAmount("VND",1,"VND",null,limits));
        Assert.False(QuoteIdentityAuthorityRules.CoversAmount("VND",1,"USD",100,limits));
        Assert.False(QuoteIdentityAuthorityRules.CoversAmount("USD",1,"USD",100,limits));
        Assert.True(QuoteIdentityAuthorityRules.CoversAmount("USD",1,"USD",100,new Dictionary<string,decimal>{{"USD",50}}));
    }
    [Fact]public void Open_ended_crm_affiliation_does_not_supply_an_authority_expiration_or_revocation_conclusion()
    {
        Assert.False(QuoteIdentityAuthorityRules.IsEffective(Now.AddDays(-1),null,false,Now));
        Assert.False(QuoteIdentityAuthorityRules.IsEffective(Now.AddDays(-1),Now.AddDays(1),null,Now));
        Assert.False(QuoteIdentityAuthorityRules.IsEffective(Now.AddDays(-1),Now.AddDays(1),true,Now));
        Assert.False(QuoteIdentityAuthorityRules.IsEffective(Now.AddSeconds(1),Now.AddDays(1),false,Now));
        Assert.False(QuoteIdentityAuthorityRules.IsEffective(Now.AddDays(-1),Now,false,Now));
        Assert.True(QuoteIdentityAuthorityRules.IsEffective(Now,Now.AddSeconds(1),false,Now));
    }
    [Fact]public void Freshness_is_bounded_and_never_accepts_future_or_unknown_check_time()
    {
        Assert.True(QuoteIdentityAuthorityRules.IsFresh(Now.AddHours(-24),24,Now));
        Assert.False(QuoteIdentityAuthorityRules.IsFresh(Now.AddHours(-24).AddTicks(-1),24,Now));
        Assert.False(QuoteIdentityAuthorityRules.IsFresh(Now.AddTicks(1),24,Now));
        Assert.False(QuoteIdentityAuthorityRules.IsFresh(null,24,Now));
        Assert.False(QuoteIdentityAuthorityRules.IsFresh(Now,25,Now));
    }
}
