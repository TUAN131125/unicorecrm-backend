using Microsoft.Extensions.Options;
using UnicoreCRM.Sales.Quotes.Infrastructure.Persistence;
using Xunit;

namespace UnicoreCRM.ArchitectureTests;

public sealed class QuoteCustomerDecisionPolicyTests
{
    [Fact]
    public void Missing_or_incomplete_workspace_policy_has_no_fallback()
    {
        var options = new QuoteCustomerDecisionOptions();
        var reader = new ConfiguredQuoteCustomerDecisionPolicyReader(new Monitor(options));
        Assert.Null(reader.Read("workspace"));
        options.Workspaces["workspace"] = new() { Version = "approved-v1", AllowReportedAcceptance = true };
        Assert.Null(reader.Read("workspace"));
        options.Workspaces["workspace"].AllowRejection = true;
        Assert.NotNull(reader.Read("workspace"));
        Assert.Null(reader.Read("WORKSPACE"));
        Assert.Null(reader.Read("foreign-workspace"));
    }

    [Fact]
    public void Policy_version_binds_flags_and_explicit_version_label()
    {
        var options = new QuoteCustomerDecisionOptions();
        options.Workspaces["workspace"] = new() { Version = "approved-v1", AllowReportedAcceptance = true, AllowRejection = true };
        var reader = new ConfiguredQuoteCustomerDecisionPolicyReader(new Monitor(options));
        var initial = reader.Read("workspace")!;
        Assert.Equal(initial.Version, reader.Read("workspace")!.Version);
        options.Workspaces["workspace"].AllowReportedAcceptance = false;
        var changed = reader.Read("workspace")!;
        Assert.NotEqual(initial.Version, changed.Version); Assert.False(changed.AllowReportedAcceptance);
        options.Workspaces["workspace"].Version = "approved-v2";
        Assert.NotEqual(changed.Version, reader.Read("workspace")!.Version);
        options.Workspaces["workspace"].Version = " ";
        Assert.Null(reader.Read("workspace"));
    }

    private sealed class Monitor(QuoteCustomerDecisionOptions value) : IOptionsMonitor<QuoteCustomerDecisionOptions>
    {
        public QuoteCustomerDecisionOptions CurrentValue => value;
        public QuoteCustomerDecisionOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<QuoteCustomerDecisionOptions, string?> listener) => null;
    }
}
