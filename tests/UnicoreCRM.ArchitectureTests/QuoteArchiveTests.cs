using System.Reflection;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Sales.Quotes.Application.Common;
using UnicoreCRM.Sales.Quotes.Contracts;
using Xunit;

namespace UnicoreCRM.ArchitectureTests;

public sealed class QuoteArchiveTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-25T00:00:00Z");

    [Theory]
    [InlineData("DRAFT")]
    [InlineData("REVIEW")]
    [InlineData("REJECTED")]
    [InlineData("EXPIRED")]
    public async Task Archive_retains_lifecycle_and_evidence_and_commits_under_all_owner_leases(string status)
    {
        var f = Create(status); var before = f.Spy.Quote!.ResourceVersion;
        var delivery = f.Spy.Quote.DeliveryHistoryJson;
        var fingerprint = QuoteApprovalAssessment.ContentFingerprint(f.Spy.Quote);
        f.Spy.VerifyLeasesAtCommit = () => Assert.All(f.Verifiers, v => Assert.False(v.Disposed));
        var result = await f.Execute("archive-key", before);
        Assert.True(result.IsSuccess); Assert.Equal(status, f.Spy.Quote.Status);
        Assert.Equal(Now, f.Spy.Quote.ArchivedAt); Assert.Equal("No longer in use", f.Spy.Quote.ArchiveReason);
        Assert.Equal(before + 1, f.Spy.Quote.ResourceVersion); Assert.Equal(delivery, f.Spy.Quote.DeliveryHistoryJson);
        Assert.Equal(fingerprint, QuoteApprovalAssessment.ContentFingerprint(f.Spy.Quote));
        Assert.All(f.Verifiers, v => Assert.True(v.Disposed)); Assert.Single(f.Spy.Outbox);
        Assert.Equal("QUOTE_ARCHIVED", f.Spy.Outbox[0].EventType);
        Assert.Equal("REPLAYED", (await f.Execute("archive-key", before)).Value?.Outcome);
        Assert.All(f.Verifiers, v => Assert.Equal(1, v.Calls)); Assert.Single(f.Spy.Commands);
        Assert.Equal("VERSION_CONFLICT", (await f.Execute("stale-key", before)).Error?.Code);
    }

    [Theory]
    [InlineData(QuoteRetentionOwner.Deals, QuoteRetentionEligibility.Blocked, "DOWNSTREAM_OBLIGATION")]
    [InlineData(QuoteRetentionOwner.Orders, QuoteRetentionEligibility.Blocked, "DOWNSTREAM_OBLIGATION")]
    [InlineData(QuoteRetentionOwner.Payments, QuoteRetentionEligibility.Blocked, "DOWNSTREAM_OBLIGATION")]
    [InlineData(QuoteRetentionOwner.Deals, QuoteRetentionEligibility.Unavailable, "BLOCKED_CONTRACT")]
    [InlineData(QuoteRetentionOwner.Orders, QuoteRetentionEligibility.Unavailable, "BLOCKED_CONTRACT")]
    [InlineData(QuoteRetentionOwner.Payments, QuoteRetentionEligibility.Unavailable, "BLOCKED_CONTRACT")]
    public async Task Every_owner_obligation_or_unavailability_blocks_before_any_mutation(QuoteRetentionOwner owner, QuoteRetentionEligibility eligibility, string detail)
    {
        var f = Create("REJECTED"); f.Verifiers.Single(v => v.Owner == owner).Eligibility = eligibility;
        var before = f.Spy.Quote!.ResourceVersion;
        Assert.Equal(detail, (await f.Execute("blocked-key", before)).Error?.Detail);
        Assert.Null(f.Spy.Quote.ArchivedAt); Assert.Equal(before, f.Spy.Quote.ResourceVersion);
        Assert.Empty(f.Spy.Commands); Assert.Empty(f.Spy.Outbox); Assert.Equal(0, f.Spy.Saves);
        Assert.All(f.Verifiers.Where(v => v.Calls > 0), v => Assert.True(v.Disposed));
    }

    [Fact]
    public async Task Missing_or_duplicate_participants_are_not_negative_obligation_proof()
    {
        Assert.False(QuoteRetentionVerification.Available([]));
        var f = Create("DRAFT");
        var handler = new QuoteArchiveCommands(new(f.Access), f.Port, new Policy(), new Workspace(), f.Verifiers.Take(2), new Clock());
        Assert.Equal("BLOCKED_CONTRACT", (await handler.ExecuteAsync(f.Spy.Quote!.QuoteId, new("Reason"), new("request-id", "correlation-id", "missing-key", f.Spy.Quote.ResourceVersion), default)).Error?.Detail);
        Assert.Empty(f.Spy.Commands);
    }

    [Theory]
    [InlineData("SENT")]
    [InlineData("ACCEPTED")]
    public async Task Active_offer_or_accepted_quote_cannot_be_archived(string status)
    {
        var f = Create(status);
        Assert.Equal("QUOTE_STATE_INVALID", (await f.Execute("state-key", f.Spy.Quote!.ResourceVersion)).Error?.Detail);
        Assert.All(f.Verifiers, v => Assert.Equal(0, v.Calls)); Assert.Null(f.Spy.Quote.ArchivedAt);
    }

    [Theory]
    [InlineData("quotes.read")]
    [InlineData("quotes.delete")]
    public async Task Both_capabilities_are_required(string capability)
    {
        var f = Create("DRAFT"); f.Access.Denied = capability;
        Assert.Equal("ACCESS_DENIED", (await f.Execute("permission-key", f.Spy.Quote!.ResourceVersion)).Error?.Code);
        Assert.Empty(f.Spy.Commands);
    }

    [Fact]
    public async Task Replay_rechecks_archive_field_security()
    {
        var f = Create("DRAFT"); var version = f.Spy.Quote!.ResourceVersion;
        Assert.True((await f.Execute("fls-key", version)).IsSuccess);
        f.Access.ReadOnly = "archivedAt";
        Assert.Equal("ACCESS_DENIED", (await f.Execute("fls-key", version)).Error?.Code);
        Assert.Single(f.Spy.Commands);
    }

    private static Fixture Create(string status)
    {
        var port = DispatchProxy.Create<IQuotesPersistence, Persistence>();
        var spy = (Persistence)port; spy.Quote = QuoteRevisionTests.Source(); QuoteRevisionTests.Set(spy.Quote, "Status", status);
        var access = new Evaluator(); var owners = Enum.GetValues<QuoteRetentionOwner>().Select(o => new Verifier(o)).ToArray();
        return new(new(new(access), port, new Policy(), new Workspace(), owners, new Clock()), port, spy, access, owners);
    }
    private sealed record Fixture(QuoteArchiveCommands Handler, IQuotesPersistence Port, Persistence Spy, Evaluator Access, Verifier[] Verifiers)
    {
        internal Task<QuoteOperationResult<QuoteMutationResponse>> Execute(string key, long version) => Handler.ExecuteAsync(Spy.Quote!.QuoteId, new("No longer in use"), new("request-id", "correlation-id", key, version), default);
    }
    public class Persistence : QuoteApprovalTests.PersistenceSpy
    {
        internal Action? VerifyLeasesAtCommit;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name == "BeginAsync"
            ? Task.FromResult<IQuoteTransaction>(new Transaction(this)) : base.Invoke(method, args);
        private sealed class Transaction(Persistence spy) : IQuoteTransaction
        {
            public Task CommitAsync(CancellationToken ct) { spy.VerifyLeasesAtCommit?.Invoke(); return Task.CompletedTask; }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
    private sealed class Verifier(QuoteRetentionOwner owner) : IQuoteRetentionVerifier
    {
        public QuoteRetentionOwner Owner => owner;
        internal QuoteRetentionEligibility Eligibility = QuoteRetentionEligibility.Clear;
        internal int Calls; internal bool Disposed;
        public Task<IQuoteRetentionLease> AcquireAsync(string quoteId, string requestId, string correlationId, CancellationToken ct)
        { Calls++; return Task.FromResult<IQuoteRetentionLease>(new Lease(this)); }
        private sealed class Lease(Verifier owner) : IQuoteRetentionLease
        {
            public QuoteRetentionEligibility Eligibility => owner.Eligibility;
            public ValueTask DisposeAsync() { owner.Disposed = true; return ValueTask.CompletedTask; }
        }
    }
    private sealed class Policy : IQuoteLifecyclePolicyReader { public QuoteLifecyclePolicy? Read(string ws) => new("approved-v1", true, true, true); }
    private sealed class Workspace : IQuoteWorkspacePolicyReader { public Task<QuoteWorkspacePolicy?> ReadAsync(string ws, CancellationToken ct) => Task.FromResult<QuoteWorkspacePolicy?>(new(true, ["USD"], 1)); }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Evaluator : IRecordAccessEvaluator
    {
        internal string? Denied, ReadOnly;
        public Task<RecordAccessAuthorization> AuthorizeResourceAsync(string resource, string capability, IReadOnlyList<string>? fields, RecordAccessRepresentation representation, RecordAccessRequestContext context, CancellationToken ct) => Task.FromResult(new RecordAccessAuthorization(capability != Denied, "AUTHORIZED", new("workspace", "account", "actor", "membership"), RecordAccessScopeFilter.Workspace, null, "WORKSPACE", fields!.ToDictionary(f => f, f => f == ReadOnly ? RecordFieldEnforcement.ReadOnly : RecordFieldEnforcement.ReadWrite), [], ["quotes.read", "quotes.delete"], "policy", resource, capability, true));
        public Task<RecordAccessRecordDecision> AuthorizeRecordAsync(RecordAccessAuthorization access, string id, RecordAccessFacts facts, string point, RecordAccessRequestContext context, CancellationToken ct) => Task.FromResult(new RecordAccessRecordDecision(true, "WORKSPACE", null));
    }
}
