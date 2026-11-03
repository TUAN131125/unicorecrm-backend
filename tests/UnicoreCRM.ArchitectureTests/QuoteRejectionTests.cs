using System.Reflection;
using System.Text.Json;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Sales.Quotes.Application.Common;
using UnicoreCRM.Sales.Quotes.Contracts;
using UnicoreCRM.Sales.Quotes.Domain;
using Xunit;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.ArchitectureTests;

public sealed class QuoteRejectionTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-25T00:00:00Z");
    [Theory]
    [InlineData("DRAFT")]
    [InlineData("REVIEW")]
    [InlineData("ACCEPTED")]
    [InlineData("REJECTED")]
    [InlineData("EXPIRED")]
    public void Only_sent_can_be_rejected(string status)
    {
        var quote = NewQuote(); Set(quote, "Status", status);
        Assert.False(quote.Reject("Customer declined", Now, Decision(quote))); Assert.Equal(1, quote.ResourceVersion);
    }
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void A_meaningful_reason_is_required(string reason)
    {
        var quote = NewQuote(); Set(quote, "Status", "SENT");
        Assert.False(quote.Reject(reason, Now, Decision(quote))); Assert.Equal("SENT", quote.Status);
        Assert.False(quote.Reject(new string('x', 501), Now, Decision(quote))); Assert.Null(quote.RejectedAt);
    }
    [Fact]
    public void Archived_sent_cannot_be_rejected()
    {
        var quote = NewQuote(); Set(quote, "Status", "SENT"); Set(quote, "ArchivedAt", Now);
        Assert.False(quote.Reject("Customer declined", Now, Decision(quote))); Assert.Null(quote.RejectedAt);
    }
    [Fact]
    public void Rejection_preserves_commercial_approval_and_delivery_evidence()
    {
        var quote = NewQuote();
        Set(quote, "ApprovalStatus", "APPROVED"); Set(quote, "ApprovalContentFingerprint", "historical-approval");
        var delivery = new QuoteDeliveryRecordReadModel("delivery", "EMAIL", Now.AddHours(-1).UtcDateTime.ToString("O"), "delivery-content") { EvidenceType = "USER_CONFIRMED_SENT" };
        Assert.True(quote.RecordDelivery(JsonSerializer.Serialize(new[] { delivery }, QuotePricing.Json), Now.AddHours(-1)));
        var fingerprint = QuoteApprovalAssessment.ContentFingerprint(quote);
        var history = quote.DeliveryHistoryJson; var version = quote.ResourceVersion;
        Assert.True(quote.Reject("Customer declined on price", Now, Decision(quote)));
        Assert.Equal("REJECTED", quote.Status); Assert.Equal(Now, quote.RejectedAt);
        Assert.Equal(version + 1, quote.ResourceVersion); Assert.Equal(history, quote.DeliveryHistoryJson);
        Assert.Equal("APPROVED", quote.ApprovalStatus); Assert.Equal("historical-approval", quote.ApprovalContentFingerprint);
        Assert.Equal(fingerprint, QuoteApprovalAssessment.ContentFingerprint(quote));
        Assert.False(quote.Reject("Second decision", Now.AddMinutes(1), Decision(quote)));
        Assert.Null(quote.AcceptedAt);
    }
    [Fact]
    public void Rejection_of_sent_quote_does_not_invent_approval_or_validity_prerequisites()
    {
        var quote = NewQuote(); Set(quote, "Status", "SENT"); Set(quote, "ValidUntil", new DateOnly(2020, 1, 1));
        Assert.Null(quote.ApprovalStatus);
        Assert.True(quote.Reject("Customer reported refusal after validity date", Now, Decision(quote)));
    }

    [Fact]
    public async Task Rejection_commits_reason_actor_snapshot_and_one_terminal_decision()
    {
        var f = Create(); var prior = f.Quote.ResourceVersion; var history = f.Quote.DeliveryHistoryJson;
        var response = await f.Reject("reject-key", prior);
        Assert.True(response.IsSuccess); Assert.Equal("REJECTED", response.Value!.Result.Quote.Status);
        Assert.Equal(prior + 1, response.Value.Version); Assert.Equal(history, f.Quote.DeliveryHistoryJson);
        var payload = JsonDocument.Parse(Assert.Single(f.Persistence.Outbox).PayloadJson).RootElement;
        Assert.Equal("Customer declined price", payload.GetProperty("reason").GetString());
        Assert.Equal("reporter", payload.GetProperty("actor").GetString());
        Assert.Equal("UNVERIFIED", payload.GetProperty("verificationStatus").GetString());
        Assert.Equal(prior, payload.GetProperty("snapshot").GetProperty("resourceVersion").GetInt64());
        Assert.Equal(QuoteApprovalAssessment.ContentFingerprint(f.Quote), payload.GetProperty("commercialFingerprint").GetString());
        Assert.Equal("REPLAYED", (await f.Reject("reject-key", prior)).Value?.Outcome);
        Assert.Equal("IDEMPOTENCY_KEY_REUSED", (await f.Reject("reject-key", prior, "Different refusal")).Error?.Code);
        Assert.Equal("VERSION_CONFLICT", (await f.Reject("other-key", prior)).Error?.Code);
        Assert.Equal("QUOTE_REJECTION_BLOCKED", (await f.Reject("other-key", f.Quote.ResourceVersion)).Error?.Code);
        Assert.Single(f.Persistence.Commands); Assert.Single(f.Persistence.Outbox);
        Assert.Equal(1, f.Persistence.Saves); Assert.Null(f.Quote.AcceptedAt);
    }

    [Theory]
    [InlineData("deliveryHistory", false)]
    [InlineData("status", true)]
    [InlineData("rejectedAt", true)]
    [InlineData("grandTotal", false)]
    public async Task Rejection_replay_rechecks_current_field_security(string field, bool readOnly)
    {
        var f = Create(); var version = f.Quote.ResourceVersion;
        Assert.True((await f.Reject("field-key", version)).IsSuccess);
        f.Access.Hidden = readOnly ? null : field; f.Access.ReadOnly = readOnly ? field : null;
        Assert.Equal("ACCESS_DENIED", (await f.Reject("field-key", version)).Error?.Code);
        Assert.Single(f.Persistence.Commands);
    }

    [Theory]
    [InlineData("quotes.read")]
    [InlineData("quotes.update")]
    public async Task Both_read_and_update_are_required_before_any_commit_or_replay(string capability)
    {
        var f = Create(); f.Access.DeniedCapability = capability;
        Assert.Equal("ACCESS_DENIED", (await f.Reject("denied-key", f.Quote.ResourceVersion)).Error?.Code);
        Assert.Empty(f.Persistence.Commands); Assert.Equal("SENT", f.Quote.Status);
    }

    [Fact]
    public async Task Acceptance_guard_has_no_success_receipt_or_mutation_path()
    {
        var f = Create(); var version = f.Quote.ResourceVersion;
        var guard = new QuoteAcceptanceGuard(new(f.Access), f.Port);
        var result = await guard.CheckAsync(f.Quote.QuoteId, new("request-id", "correlation-id", "accept-key", version), default);
        Assert.Equal("QUOTE_ACCEPTANCE_BLOCKED", result.Code);
        Assert.Equal("CUSTOMER_EVIDENCE_VERIFIER_UNAVAILABLE", result.Detail);
        Assert.Equal(version, f.Quote.ResourceVersion); Assert.Equal("SENT", f.Quote.Status);
        Assert.Empty(f.Persistence.Commands); Assert.Empty(f.Persistence.Outbox); Assert.Equal(0, f.Persistence.Saves);
        Assert.Equal("VERSION_CONFLICT", (await guard.CheckAsync(f.Quote.QuoteId, new("request-id", "correlation-id", "accept-key", version - 1), default)).Code);
    }

    private static QuoteCustomerDecisionRecord Decision(Quote q) => new(q.WorkspaceId, Guid.NewGuid().ToString("N"), q.QuoteId,
        q.QuoteRevision, q.ResourceVersion, "reporter", "REJECTION", QuoteApprovalAssessment.ContentFingerprint(q), "test-policy", "request", "{}", "cmd", "audit", "event", Now);

    [Fact]
    public async Task Reported_acceptance_is_append_only_unverified_and_reject_keeps_history()
    {
        var f = Create(); var v = f.Quote.ResourceVersion;
        var handler = new QuoteCustomerResponseCommands(new(f.Access), f.Port, new Policies(), new Workspace(), new Clock());
        var request = new RecordQuoteCustomerDecisionRequest("decision-one", "USER_REPORTED_ACCEPTANCE", "EMAIL", "Employee reports customer agreement");
        var metadata = new QuoteCommandMetadata("request-id", "correlation-id", "response-key", v);
        var result = await handler.ExecuteAsync(f.Quote.QuoteId, request, metadata, default);
        Assert.True(result.IsSuccess); Assert.Equal("SENT", f.Quote.Status); Assert.Null(f.Quote.AcceptedAt);
        Assert.Equal(v + 1, f.Quote.ResourceVersion);
        Assert.Equal("UNVERIFIED", Assert.Single(f.Quote.CustomerDecisions).VerificationStatus);
        Assert.Equal(v, result.Value!.Result.Quote.CustomerDecisionHistory![0].ResourceVersion);
        Assert.Equal("REPLAYED", (await handler.ExecuteAsync(f.Quote.QuoteId, request, metadata, default)).Value?.Outcome);
        Assert.Equal("REPLAYED", (await handler.ExecuteAsync(f.Quote.QuoteId, request, metadata with { IdempotencyKey = "alias-key" }, default)).Value?.Outcome);
        Assert.Single(f.Quote.CustomerDecisions); Assert.Single(f.Persistence.Outbox);
        Assert.Equal("QUOTE_CUSTOMER_DECISION_ID_REUSED", (await handler.ExecuteAsync(f.Quote.QuoteId, request with { SourceDescription = "Different claim" }, metadata with { IdempotencyKey = "different-key" }, default)).Error?.Code);
        Assert.Equal("VERSION_CONFLICT", (await handler.ExecuteAsync(f.Quote.QuoteId, request with { DecisionId = "decision-two" }, metadata with { IdempotencyKey = "stale-key" }, default)).Error?.Code);
        Assert.True((await f.Reject("after-report", f.Quote.ResourceVersion)).IsSuccess);
        Assert.Equal(2, f.Quote.CustomerDecisions.Count); Assert.Equal("REJECTED", f.Quote.Status); Assert.Null(f.Quote.AcceptedAt);
        Assert.Equal("QUOTE_CUSTOMER_RESPONSE_BLOCKED", (await handler.ExecuteAsync(f.Quote.QuoteId, request with { DecisionId = "decision-three" }, metadata with { IdempotencyKey = "terminal-key", ExpectedVersion = f.Quote.ResourceVersion }, default)).Error?.Code);
        Assert.Equal(2, f.Quote.CustomerDecisions.Count);
    }

    [Fact]
    public async Task Customer_policy_is_required_even_for_receipt_replay()
    {
        var f = Create(); var v = f.Quote.ResourceVersion;
        Assert.True((await f.Reject("policy-key", v)).IsSuccess);
        var handler = new QuoteRejectionCommands(new(f.Access), f.Port, new Clock(), new MissingPolicy(), new Workspace());
        Assert.Equal("INTEGRATION_UNAVAILABLE", (await handler.ExecuteAsync(f.Quote.QuoteId, new("Customer declined price"), new("req", "corr", "policy-key", v), default)).Error?.Code);
        Assert.Single(f.Persistence.Commands);
    }
    private sealed class MissingPolicy : IQuoteCustomerDecisionPolicyReader
    { public QuoteCustomerDecisionPolicy? Read(string workspaceId) => null; }

    private static Fixture Create()
    {
        var port = DispatchProxy.Create<IQuotesPersistence, RejectionPersistence>();
        var spy = (RejectionPersistence)port; spy.Quote = NewQuote();
        spy.Quote.RecordDelivery("[]", Now.AddHours(-1));
        var access = new Evaluator();
        return new(new(new(access), port, new Clock(), new Policies(), new Workspace()), port, spy, access);
    }
    private sealed record Fixture(QuoteRejectionCommands Handler, IQuotesPersistence Port, RejectionPersistence Persistence, Evaluator Access)
    {
        internal Quote Quote => Persistence.Quote!;
        internal Task<QuoteOperationResult<QuoteMutationResponse>> Reject(string key, long version, string reason = "Customer declined price") =>
            Handler.ExecuteAsync(Quote.QuoteId, new(reason), new("request-id", "correlation-id", key, version), default);
    }
    public class RejectionPersistence : QuoteApprovalTests.PersistenceSpy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name == "FindCustomerDecisionAsync"
            ? Task.FromResult(Quote?.CustomerDecisions.SingleOrDefault(x => x.WorkspaceId == (string)args![0]! && x.DecisionId == (string)args[1]!))
            : method?.Name == "ReadQuoteAsync"
            ? Task.FromResult((string)args![0]! == Quote?.WorkspaceId && (string)args[1]! == Quote?.QuoteId ? Quote : null)
            : base.Invoke(method, args);
    }
    private sealed class Policies : IQuoteCustomerDecisionPolicyReader
    { public QuoteCustomerDecisionPolicy? Read(string workspaceId) => new("b3-test-v1", true, true); }
    private sealed class Workspace : IQuoteWorkspacePolicyReader
    { public Task<QuoteWorkspacePolicy?> ReadAsync(string workspaceId, CancellationToken ct) => Task.FromResult<QuoteWorkspacePolicy?>(new(true, ["USD"], 1)); }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Evaluator : IRecordAccessEvaluator
    {
        internal string? Hidden, ReadOnly, DeniedCapability;
        public Task<RecordAccessAuthorization> AuthorizeResourceAsync(string resource, string capability, IReadOnlyList<string>? fields, RecordAccessRepresentation representation, RecordAccessRequestContext context, CancellationToken ct)
        {
            var allowed = capability != DeniedCapability;
            return Task.FromResult(new RecordAccessAuthorization(allowed, allowed ? "AUTHORIZED" : "ACCESS_DENIED", new("workspace", "account", "reporter", "membership"), RecordAccessScopeFilter.Workspace, null, "WORKSPACE",
                fields!.ToDictionary(f => f, f => f == Hidden ? RecordFieldEnforcement.Withheld : f == ReadOnly ? RecordFieldEnforcement.ReadOnly : RecordFieldEnforcement.ReadWrite), [], ["quotes.read", "quotes.update"], "policy", resource, capability, allowed));
        }
        public Task<RecordAccessRecordDecision> AuthorizeRecordAsync(RecordAccessAuthorization access, string id, RecordAccessFacts facts, string point, RecordAccessRequestContext context, CancellationToken ct) => Task.FromResult(new RecordAccessRecordDecision(true, "WORKSPACE", null));
    }
    private static void Set(Quote quote, string name, object value) => typeof(Quote).GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(quote, value);
    private static Quote NewQuote()
    {
        var line = new QuoteLineReadModel("line", "Manual", "1", new("100", "USD"), "0", "NONE", new("100", "USD"), new("0", "USD"), new("0", "USD"), new("100", "USD"));
        return new("workspace", "quote-test", "Q-0000000001", "CONTACT", "buyer", "DIRECT_SALE", null, null, null,
            new("Quote", "USD", "owner", null, JsonSerializer.Serialize(new[] { line }, QuotePricing.Json), "[]", "[]", 100, 0, 0, 100, null, null, null), Now);
    }
}
