using Microsoft.Extensions.Options;
using UnicoreCRM.Sales.Quotes.Application.Common;
using UnicoreCRM.Platform.AccessControl.Application.Common;
using Xunit;
namespace UnicoreCRM.ArchitectureTests;

public sealed class QuoteVerifiedAcceptanceTests
{
    [Fact]
    public void Exact_workspace_policy_is_required_and_effective_values_change_version()
    {
        var config = new QuoteVerifiedAcceptanceOptions(); var policy = new QuoteVerifiedAcceptanceWorkspacePolicy { Enabled = true, Version = "approved-B" };
        config.Workspaces["workspace"] = policy; var reader = new QuoteVerifiedAcceptancePolicyReader(new Monitor(config));
        var before = reader.Read("workspace"); Assert.NotNull(before); Assert.Null(reader.Read("Workspace")); Assert.Null(reader.Read("foreign"));
        policy.AuthorityFreshnessHours = 12; Assert.NotEqual(before.Version, reader.Read("workspace")!.Version);
        policy.Enabled = false; Assert.Null(reader.Read("workspace"));
    }
    [Fact]
    public void Invalid_options_fail_closed_and_do_not_turn_reads_into_errors()
    {
        Assert.Null(new QuoteVerifiedAcceptancePolicyReader(new Monitor(null)).Read("workspace"));
        var value = new QuoteVerifiedAcceptanceOptions(); value.Workspaces["workspace"] = new() { Enabled = true, Version = "approved", AuthorityFreshnessHours = 25 };
        Assert.Null(new QuoteVerifiedAcceptancePolicyReader(new Monitor(value)).Read("workspace"));
        value.Workspaces["workspace"].AuthorityFreshnessHours = 24; value.Workspaces["workspace"].CurrencyLimits.Clear();
        Assert.Null(new QuoteVerifiedAcceptancePolicyReader(new Monitor(value)).Read("workspace"));
    }
    [Fact]
    public void Grand_total_threshold_is_inclusive_and_no_currency_conversion_is_inferred()
    {
        var p = Policy(); Assert.True(QuoteVerifiedAcceptanceRules.WithinValueLimit(p, "VND", 100000000));
        Assert.False(QuoteVerifiedAcceptanceRules.WithinValueLimit(p, "VND", 100000001));
        Assert.False(QuoteVerifiedAcceptanceRules.WithinValueLimit(p, "USD", 1));
        Assert.False(QuoteVerifiedAcceptanceRules.WithinValueLimit(p, "VND", -1));
    }
    [Fact]
    public void Pre_acceptance_authority_requires_fresh_nonfuture_in_scope_unrevoked_facts()
    {
        var now = DateTimeOffset.Parse("2026-10-25T12:00:00Z"); var p = Policy(); var start = now.AddDays(-7); var end = now.AddDays(1);
        Assert.True(QuoteVerifiedAcceptanceRules.AuthorityFreshBeforeAcceptance(p, now.AddHours(-24), start, end, false, now));
        Assert.False(QuoteVerifiedAcceptanceRules.AuthorityFreshBeforeAcceptance(p, now.AddHours(-24).AddTicks(-1), start, end, false, now));
        Assert.False(QuoteVerifiedAcceptanceRules.AuthorityFreshBeforeAcceptance(p, now.AddTicks(1), start, end, false, now));
        Assert.False(QuoteVerifiedAcceptanceRules.AuthorityFreshBeforeAcceptance(p, now, start, now, false, now));
        Assert.False(QuoteVerifiedAcceptanceRules.AuthorityFreshBeforeAcceptance(p, now, start, end, true, now));
        Assert.False(QuoteVerifiedAcceptanceRules.AuthorityFreshBeforeAcceptance(p, now, now.AddHours(1), end, false, now));
        // This predicate is deliberately pre-acceptance only; no historical acceptance expiry job exists.
    }
    [Fact]
    public void New_capabilities_are_explicitly_assignable_but_never_owner_defaults()
    {
        foreach (var cap in new[] { "quotes.submitAcceptanceEvidence", "quotes.readAcceptanceEvidence", "quotes.verifyAcceptanceEvidence", "quotes.acceptVerified" })
        {
            Assert.Contains(cap, WorkspaceCapabilityPolicy.CustomRoleAssignableCapabilities);
            Assert.DoesNotContain(cap, WorkspaceCapabilityPolicy.WorkspaceOwnerCapabilities);
        }
    }
    [Fact]
    public void Legacy_self_approved_flag_is_not_independent_approval_evidence()
    {
        var quote = QuoteRevisionTests.Source(); var now = DateTimeOffset.Parse("2026-10-25T12:00:00Z");
        quote.RequestApproval(true, "[]", "fingerprint", "policy", "requester", null, now);
        quote.DecideApproval(true, "creator", null, now);
        Assert.False(QuoteVerifiedAcceptanceRules.HasIndependentApproval(quote, "creator"));
        quote.DecideApproval(true, "requester", null, now);
        Assert.False(QuoteVerifiedAcceptanceRules.HasIndependentApproval(quote, "creator"));
        quote.DecideApproval(true, "independent", null, now);
        Assert.False(QuoteVerifiedAcceptanceRules.HasIndependentApproval(quote, null));
        Assert.True(QuoteVerifiedAcceptanceRules.HasIndependentApproval(quote, "creator"));
    }
    [Fact]
    public void Legacy_delivery_or_a_different_revision_cannot_establish_current_delivery_precondition()
    {
        var row = new UnicoreCRM.Sales.Quotes.Contracts.QuoteDeliveryRecordReadModel("delivery", "OTHER", "2026-10-25T12:00:00Z", "fingerprint")
            { QuoteRevision = 2, EvidenceType = "USER_CONFIRMED_SENT" };
        var json = System.Text.Json.JsonSerializer.Serialize(new[] { row }, QuotePricing.Json);
        Assert.True(QuoteVerifiedAcceptanceRules.HasCurrentManualDelivery(json, 2, "fingerprint"));
        Assert.False(QuoteVerifiedAcceptanceRules.HasCurrentManualDelivery(json, 1, "fingerprint"));
        Assert.False(QuoteVerifiedAcceptanceRules.HasCurrentManualDelivery(json, 2, "changed"));
        Assert.False(QuoteVerifiedAcceptanceRules.HasCurrentManualDelivery("malformed", 2, "fingerprint"));
        Assert.False(QuoteVerifiedAcceptanceRules.HasCurrentManualDelivery(null, 2, "fingerprint"));
    }
    private static QuoteVerifiedAcceptancePolicy Policy() => new("approved", 24, new Dictionary<string, decimal> { ["VND"] = 100000000 });
    private sealed class Monitor(QuoteVerifiedAcceptanceOptions? value) : IOptionsMonitor<QuoteVerifiedAcceptanceOptions>
    {
        public QuoteVerifiedAcceptanceOptions CurrentValue => value ?? throw new InvalidOperationException("Invalid bool binding");
        public QuoteVerifiedAcceptanceOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<QuoteVerifiedAcceptanceOptions, string?> listener) => null;
    }
}
