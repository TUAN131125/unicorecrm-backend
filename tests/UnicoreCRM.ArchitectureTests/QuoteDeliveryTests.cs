using System.Reflection;
using System.Text.Json;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Sales.Quotes.Application.Common;
using UnicoreCRM.Sales.Quotes.Contracts;
using UnicoreCRM.Sales.Quotes.Domain;
using Xunit;

namespace UnicoreCRM.ArchitectureTests;

public sealed class QuoteDeliveryTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-24T16:59:59Z");
    [Theory]
    [InlineData("2026-10-24T16:59:59Z", false)]
    [InlineData("2026-10-24T17:00:00Z", true)]
    public void Expiry_uses_inclusive_workspace_day(string instant, bool blocked) =>
        Assert.Equal(blocked, QuoteDeliveryCommands.ValidateExpiry(new(2026, 10, 24), "Asia/Ho_Chi_Minh", DateTimeOffset.Parse(instant)) is not null);
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("invalid-zone")]
    public void Unknown_timezone_fails_closed_even_without_expiry(string? timezone) =>
        Assert.Equal("WORKSPACE_TIMEZONE_INVALID", QuoteDeliveryCommands.ValidateExpiry(null, timezone, Now)?.Detail);
    [Theory]
    [InlineData("PROVIDER_ACCEPTED")]
    [InlineData("PROVIDER_DELIVERED")]
    [InlineData("FAKE")]
    public void Provider_enums_cannot_attest_delivery(string type) =>
        Assert.Equal("QUOTE_DELIVERY_EVIDENCE_INVALID", QuoteDeliveryCommands.Validate(Request() with { EvidenceType = type }, Now)?.Code);
    [Theory]
    [InlineData("EMAIL", null, null)]
    [InlineData("GMAIL", "invalid", null)]
    [InlineData("SMS", null, " ")]
    [InlineData("OTHER", null, null)]
    [InlineData("FAKE", "a@example.test", "buyer")]
    public void Invalid_recipient_or_channel_is_rejected(string channel, string? email, string? recipient) =>
        Assert.Equal(422, QuoteDeliveryCommands.Validate(Request() with { Channel = channel, RecipientEmail = email, Recipient = recipient }, Now)?.Status);
    [Fact]
    public void Reported_future_time_is_not_server_evidence()
    {
        Assert.Equal(422, QuoteDeliveryCommands.Validate(Request() with { SentAt = Now.AddSeconds(1).UtcDateTime.ToString("O") }, Now)?.Status);
        Assert.Null(QuoteDeliveryCommands.Validate(Request() with { SentAt = Now.AddYears(-1).UtcDateTime.ToString("O") }, Now));
        Assert.Equal(422, QuoteDeliveryCommands.Validate(Request() with { SentAt = "2026-01-01T00:00:00+00:00" }, Now)?.Status);
    }
    [Fact]
    public async Task Commit_replay_identity_conflicts_resend_and_stale_version_are_distinct()
    {
        var f = Create(); var original = f.Quote.ResourceVersion;
        var sent = await f.Send(Request(), "delivery-key", original);
        Assert.True(sent.IsSuccess); Assert.Equal("SENT", f.Quote.Status);
        var evidence = Assert.Single(sent.Value!.Result.Quote.DeliveryHistory!);
        Assert.Equal(original, evidence.ResourceVersion); Assert.Equal(original + 1, sent.Value.Version);
        Assert.Equal("COMMERCIAL_CONTENT_ONLY", evidence.DocumentAuthority);
        Assert.NotEqual(evidence.RecordedAt, evidence.ReportedSentAt);
        Assert.Equal("REPLAYED", (await f.Send(Request(), "delivery-key", original)).Value?.Outcome);
        Assert.Equal("REPLAYED", (await f.Send(Request(), "another-key", original + 1)).Value?.Outcome);
        Assert.Equal("IDEMPOTENCY_KEY_REUSED", (await f.Send(Request() with { DeliveryId = "different-id" }, "another-key", original + 1)).Error?.Code);
        Assert.Equal("IDEMPOTENCY_KEY_REUSED", (await f.Send(Request() with { Note = "changed" }, "delivery-key", original)).Error?.Code);
        Assert.Equal("DELIVERY_ID_REUSED", (await f.Send(Request() with { Note = "changed" }, "identity-change-key", original + 1)).Error?.Detail);
        Assert.Equal("VERSION_CONFLICT", (await f.Send(Request() with { DeliveryId = "delivery-second" }, "stale-key", original)).Error?.Code);
        Assert.True((await f.Send(Request() with { DeliveryId = "delivery-second" }, "resend-key", f.Quote.ResourceVersion)).IsSuccess);
        Assert.Equal(3, f.Persistence.Commands.Count); Assert.Equal(2, f.Persistence.Outbox.Count);
    }
    [Theory]
    [InlineData("archived")]
    [InlineData("terminal")]
    public async Task Changed_binding_or_lifecycle_cannot_send(string changed)
    {
        var f = Create();
        if (changed == "policy") f.Policies.Policy = f.Policies.Policy with { Version = "policy-changed" };
        if (changed == "fingerprint") Set(f.Quote, "ApprovalContentFingerprint", "fake");
        if (changed == "approval") Set(f.Quote, "ApprovalStatus", "PENDING");
        if (changed == "archived") Set(f.Quote, "ArchivedAt", Now);
        if (changed == "terminal") Set(f.Quote, "Status", "ACCEPTED");
        Assert.False((await f.Send(Request(), "blocked-key", f.Quote.ResourceVersion)).IsSuccess);
        Assert.Empty(f.Persistence.Commands); Assert.Empty(f.Persistence.Outbox);
    }
    [Theory]
    [InlineData("policy")]
    [InlineData("fingerprint")]
    [InlineData("approval")]
    public async Task Retired_approval_metadata_does_not_authorize_or_block_delivery(string changed)
    {
        var f = Create();
        if (changed == "policy") f.Policies.Policy = f.Policies.Policy with { Version = "retired-policy", AlwaysRequireApproval = true };
        if (changed == "fingerprint") Set(f.Quote, "ApprovalContentFingerprint", "historical-fingerprint");
        if (changed == "approval") Set(f.Quote, "ApprovalStatus", "PENDING");
        f.Access.Hidden = "approvalPolicyVersion";
        var approval = f.Quote.ApprovalStatus; var binding = f.Quote.ApprovalContentFingerprint;
        var result = await f.Send(Request(), "direct-delivery", f.Quote.ResourceVersion);
        Assert.True(result.IsSuccess, result.Error?.Detail);
        Assert.Equal(approval, f.Quote.ApprovalStatus); Assert.Equal(binding, f.Quote.ApprovalContentFingerprint);
        Assert.Equal(QuoteApprovalAssessment.ContentFingerprint(f.Quote), Assert.Single(result.Value!.Result.Quote.DeliveryHistory!).ContentFingerprint);
    }
    [Theory]
    [InlineData("deliveryHistory", false)]
    [InlineData("status", true)]
    [InlineData("grandTotal", false)]
    [InlineData("approvalPolicyVersion", false)]
    public async Task FLS_applies_before_commit_and_replay(string field, bool readOnly)
    {
        var f = Create(); var version = f.Quote.ResourceVersion;
        Assert.True((await f.Send(Request(), "fls-key", version)).IsSuccess);
        f.Access.Hidden = readOnly ? null : field; f.Access.ReadOnly = readOnly ? field : null;
        var replay = await f.Send(Request(), "fls-key", version);
        if (field == "approvalPolicyVersion")
        {
            Assert.True(replay.IsSuccess); // Retired metadata is not a delivery prerequisite.
            Assert.Null(replay.Value!.Result.Quote.ApprovalPolicyVersion); // FLS projection still protects it.
        }
        else Assert.Equal("ACCESS_DENIED", replay.Error?.Code);
        Assert.Single(f.Persistence.Commands);
    }
    private static void Set(Quote quote, string name, object value) => typeof(Quote).GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(quote, value);
    private static RecordQuoteSendEvidenceRequest Request() => new("delivery-first", "EMAIL", "USER_CONFIRMED_SENT", "buyer@example.test", SentAt: "2026-01-01T00:00:00Z");
    private static Fixture Create()
    {
        var p = DispatchProxy.Create<IQuotesPersistence, QuoteApprovalTests.PersistenceSpy>();
        var spy = (QuoteApprovalTests.PersistenceSpy)p;
        var line = new QuoteLineReadModel("line", "Manual", "1", new("100", "USD"), "0", "NONE", new("100", "USD"), new("0", "USD"), new("0", "USD"), new("100", "USD"));
        var quote = new Quote("workspace", "quote-test", "Q-0000000001", "CONTACT", "buyer", "DIRECT_SALE", null, null, null,
            new("Quote", "USD", "requester", null, JsonSerializer.Serialize(new[] { line }, QuotePricing.Json), "[]", "[]", 100, 0, 0, 100, null, null, null), Now);
        spy.Quote = quote;
        var policies = new Policies(); var assessment = QuoteApprovalAssessment.Evaluate(quote, policies.Policy);
        quote.RequestApproval(false, "[]", assessment.Fingerprint, assessment.PolicyVersion, "requester", null, Now);
        var access = new Evaluator();
        return new(new(new(access), p, new Workspace(), policies, new Zones(), new Clock()), spy, access, policies);
    }
    private sealed record Fixture(QuoteDeliveryCommands Handler, QuoteApprovalTests.PersistenceSpy Persistence, Evaluator Access, Policies Policies)
    {
        internal Quote Quote => Persistence.Quote!;
        internal Task<QuoteOperationResult<QuoteMutationResponse>> Send(RecordQuoteSendEvidenceRequest request, string key, long version) => Handler.ExecuteAsync(Quote.QuoteId, request, new("request-id", "correlation-id", key, version), default);
    }
    private sealed class Policies : IQuoteApprovalPolicyReader
    {
        internal QuoteApprovalPolicy Policy = new("policy-1", 10, 10, 30, true, false, new Dictionary<string, decimal> { ["USD"] = 100 });
        public Task<QuoteApprovalPolicy?> ReadAsync(string workspaceId, CancellationToken ct) => Task.FromResult<QuoteApprovalPolicy?>(Policy);
    }
    private sealed class Workspace : IQuoteWorkspacePolicyReader
    { public Task<QuoteWorkspacePolicy?> ReadAsync(string workspaceId, CancellationToken ct) => Task.FromResult<QuoteWorkspacePolicy?>(new(true, ["USD"], 1)); }
    private sealed class Zones : IWorkspaceTimeZoneReader
    { public Task<string?> ReadTimeZoneAsync(string workspaceId, CancellationToken ct) => Task.FromResult<string?>("Asia/Ho_Chi_Minh"); }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Evaluator : IRecordAccessEvaluator
    {
        internal string? Hidden, ReadOnly;
        public Task<RecordAccessAuthorization> AuthorizeResourceAsync(string resource, string capability, IReadOnlyList<string>? fields, RecordAccessRepresentation representation, RecordAccessRequestContext context, CancellationToken ct) =>
            Task.FromResult(new RecordAccessAuthorization(true, "AUTHORIZED", new("workspace", "account", "requester", "membership"), RecordAccessScopeFilter.Workspace, null, "WORKSPACE",
                fields!.ToDictionary(f => f, f => f == Hidden ? RecordFieldEnforcement.Withheld : f == ReadOnly ? RecordFieldEnforcement.ReadOnly : RecordFieldEnforcement.ReadWrite), [], ["quotes.read", "quotes.update"], "policy", resource, capability, true));
        public Task<RecordAccessRecordDecision> AuthorizeRecordAsync(RecordAccessAuthorization access, string id, RecordAccessFacts facts, string point, RecordAccessRequestContext context, CancellationToken ct) => Task.FromResult(new RecordAccessRecordDecision(true, "WORKSPACE", null));
    }
}
