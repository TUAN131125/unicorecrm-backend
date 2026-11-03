using System.Reflection;
using UnicoreCRM.Crm.Deals.Application.Common;
using UnicoreCRM.Crm.Deals.Application.MarkDealWon;
using UnicoreCRM.Crm.Deals.Contracts;
using UnicoreCRM.Crm.Deals.Domain;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;
using Xunit;

namespace UnicoreCRM.ArchitectureTests;

public sealed class MarkDealWonSecurityTests
{
    private static readonly TrustedWorkspaceContext Trusted = new("workspace-test", "account-test", "member-test", "membership-test");
    private const string Timestamp = "2026-10-24T01:00:00.0000000Z";

    [Theory]
    [InlineData("QUOTE_ACCEPTED", "missing-quote")]
    [InlineData("QUOTE_ACCEPTED", "claimed-draft-quote")]
    [InlineData("QUOTE_ACCEPTED", "claimed-sent-quote")]
    [InlineData("QUOTE_ACCEPTED", "claimed-accepted-quote")]
    [InlineData("ORDER_CONFIRMED", "missing-order")]
    [InlineData("ORDER_CONFIRMED", "claimed-confirmed-order")]
    public async Task Structurally_valid_claims_cannot_mutate_or_replay_without_owner_verification(string type, string sourceId)
    {
        var fixture = Create();
        var before = DealProjection.Document(fixture.Deal);
        var command = Intent(fixture.Deal, new(type, sourceId, Timestamp));
        var first = await fixture.Handler.HandleAsync(command, CancellationToken.None);
        var retry = await fixture.Handler.HandleAsync(command, CancellationToken.None);
        Assert.Equal("DEAL_WIN_EVIDENCE_INVALID", first.Error?.Code);
        Assert.Equal("DEAL_WIN_EVIDENCE_INVALID", retry.Error?.Code);
        Assert.Null(first.Value);
        Assert.Equal(before, DealProjection.Document(fixture.Deal));
        Assert.Equal(2, fixture.Persistence.Reads);
        // The spy throws on every call except an owner-local, no-tracking record read. In
        // particular no transaction, idempotency replay, tracked load, audit/outbox or save occurs.
    }

    [Theory]
    [InlineData(null, "source", Timestamp)]
    [InlineData("QUOTE_ACCEPTED", "bad source id", Timestamp)]
    [InlineData("QUOTE_ACCEPTED", "source", "not-a-timestamp")]
    [InlineData("UNRECOGNIZED", "source", Timestamp)]
    public async Task Malformed_evidence_is_rejected_before_record_read(string? type, string source, string occurredAt)
    {
        var fixture = Create();
        var result = await fixture.Handler.HandleAsync(Intent(fixture.Deal, new(type, source, occurredAt)), CancellationToken.None);
        Assert.Equal("DEAL_WIN_EVIDENCE_INVALID", result.Error?.Code);
        Assert.Equal(0, fixture.Persistence.Reads);
    }

    [Fact]
    public async Task Missing_capability_or_workspace_mismatch_never_reads_deal()
    {
        var fixture = Create();
        fixture.Access.Allowed = false;
        var command = Intent(fixture.Deal, new("QUOTE_ACCEPTED", "quote-test", Timestamp));
        Assert.Equal("ACCESS_DENIED", (await fixture.Handler.HandleAsync(command, CancellationToken.None)).Error?.Code);
        fixture.Access.WorkspaceMismatch = true;
        Assert.Equal("WORKSPACE_MISMATCH", (await fixture.Handler.HandleAsync(command, CancellationToken.None)).Error?.Code);
        Assert.Equal(0, fixture.Persistence.Reads);
    }

    [Fact]
    public async Task Foreign_workspace_missing_deal_and_record_scope_are_neutral()
    {
        var fixture = Create();
        var command = Intent(fixture.Deal, new("ORDER_CONFIRMED", "order-test", Timestamp));
        fixture.Persistence.WorkspaceId = "foreign-workspace";
        Assert.Equal("RESOURCE_NOT_FOUND", (await fixture.Handler.HandleAsync(command, CancellationToken.None)).Error?.Code);
        fixture.Persistence.WorkspaceId = Trusted.WorkspaceId;
        fixture.Access.RecordAllowed = false;
        Assert.Equal("RESOURCE_NOT_FOUND", (await fixture.Handler.HandleAsync(command, CancellationToken.None)).Error?.Code);
        fixture.Access.RecordAllowed = true;
        Assert.Equal("RESOURCE_NOT_FOUND", (await fixture.Handler.HandleAsync(command with { DealId = "missing-deal" }, CancellationToken.None)).Error?.Code);
    }

    [Fact]
    public async Task Field_security_and_expected_version_are_preserved()
    {
        var fixture = Create();
        var command = Intent(fixture.Deal, new("QUOTE_ACCEPTED", "quote-test", Timestamp));
        fixture.Access.ReadOnlyWinEvidence = true;
        Assert.Equal("ACCESS_DENIED", (await fixture.Handler.HandleAsync(command, CancellationToken.None)).Error?.Code);
        fixture.Access.ReadOnlyWinEvidence = false;
        Assert.Equal("VERSION_CONFLICT", (await fixture.Handler.HandleAsync(command with
        { Metadata = command.Metadata with { ExpectedVersion = fixture.Deal.Version + 1 } }, CancellationToken.None)).Error?.Code);
        Assert.Equal("VALIDATION_FAILED", (await fixture.Handler.HandleAsync(command with
        { Metadata = command.Metadata with { ExpectedVersion = null } }, CancellationToken.None)).Error?.Code);
        Assert.False(fixture.Deal.IsTerminal);
    }

    [Fact]
    public async Task Cancellation_propagates()
    {
        var fixture = Create();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Handler.HandleAsync(
            Intent(fixture.Deal, new("QUOTE_ACCEPTED", "quote-test", Timestamp)), cancelled.Token));
        Assert.Equal(0, fixture.Persistence.Reads);
    }

    private static Command Intent(Deal deal, DealWinEvidence evidence) => new(deal.DealId, new(evidence),
        new("request-test", "correlation-test", "stable-idempotency-key", deal.Version));

    private sealed record Fixture(Handler Handler, Deal Deal, PersistenceSpy Persistence, AccessEvaluator Access);

    private static Fixture Create()
    {
        var profile = new DealProfile("Security control", new("CONTACT", "contact-test"), new("100", "VND"),
            "20", Trusted.MemberId, new DateOnly(2027, 1, 1), null, null, [], null);
        var deal = new Deal(Trusted.WorkspaceId, profile, "DISCOVERY", DealStageCategory.Open,
            DealForecastCategory.Pipeline, DateTimeOffset.UnixEpoch);
        var persistence = DispatchProxy.Create<IDealsPersistence, PersistenceSpy>();
        var spy = (PersistenceSpy)(object)persistence;
        spy.Deal = deal;
        var access = new AccessEvaluator();
        return new(new(new(access), persistence), deal, spy, access);
    }

    public class PersistenceSpy : DispatchProxy
    {
        internal Deal? Deal;
        internal string WorkspaceId = Trusted.WorkspaceId;
        internal int Reads;
        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            if (method?.Name != "ReadDealAsync")
                throw new InvalidOperationException($"Blocked MarkWon must not call {method?.Name}.");
            var args = arguments ?? [];
            ((CancellationToken)args[2]!).ThrowIfCancellationRequested();
            Reads++;
            return Task.FromResult((string)args[0]! == WorkspaceId && (string)args[1]! == Deal?.DealId ? Deal : null);
        }
    }

    private sealed class AccessEvaluator : IRecordAccessEvaluator
    {
        internal bool Allowed = true, RecordAllowed = true, WorkspaceMismatch, ReadOnlyWinEvidence;
        public Task<RecordAccessAuthorization> AuthorizeResourceAsync(string resourceKey, string capability,
            IReadOnlyList<string>? fields, RecordAccessRepresentation representation, RecordAccessRequestContext context, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Assert.Equal("deals.close", capability);
            var enforcement = (fields ?? []).ToDictionary(field => field,
                field => ReadOnlyWinEvidence && field == "winEvidence" ? RecordFieldEnforcement.ReadOnly : RecordFieldEnforcement.ReadWrite,
                StringComparer.OrdinalIgnoreCase);
            return Task.FromResult(new RecordAccessAuthorization(Allowed, WorkspaceMismatch ? "WORKSPACE_MISMATCH" : "ACCESS_DENIED",
                WorkspaceMismatch ? null : Trusted, RecordAccessScopeFilter.Workspace, null, "WORKSPACE", enforcement,
                [], ["deals.read", "deals.close"], "test", resourceKey, capability, Allowed));
        }
        public Task<RecordAccessRecordDecision> AuthorizeRecordAsync(RecordAccessAuthorization access, string id,
            RecordAccessFacts facts, string point, RecordAccessRequestContext context, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Assert.Equal(Trusted.MemberId, facts.OwnerMemberId);
            return Task.FromResult(new RecordAccessRecordDecision(RecordAllowed, "WORKSPACE", RecordAllowed));
        }
    }
}
