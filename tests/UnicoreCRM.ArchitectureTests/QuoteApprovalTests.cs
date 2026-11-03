using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Options;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Platform.Workspace.Infrastructure;
using UnicoreCRM.Sales.Quotes.Application.Common;
using UnicoreCRM.Sales.Quotes.Contracts;
using UnicoreCRM.Sales.Quotes.Domain;
using Xunit;

namespace UnicoreCRM.ArchitectureTests;

public sealed class QuoteApprovalTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-24T02:00:00Z");
    private static readonly QuoteApprovalPolicy DefaultPolicy = new("policy-1", 10, 10, 30, true, false,
        new Dictionary<string, decimal> { ["USD"] = 100 });

    [Theory]
    [InlineData("10", 10, 100, false)]
    [InlineData("10.000001", 10, 100, true)]
    [InlineData("0", 10.000001, 100, true)]
    [InlineData("0", 0, 100.01, true)]
    public void Thresholds_require_strictly_greater_than_policy(string lineDiscount, double discountTotal, double grandTotal, bool required)
    {
        var quote = NewQuote(discount: (decimal)discountTotal, total: (decimal)grandTotal, lineDiscount: lineDiscount);
        Assert.Equal(required, QuoteApprovalAssessment.Evaluate(quote, DefaultPolicy).Required);
    }

    [Fact]
    public void Uses_grand_total_and_requires_policy_for_each_currency()
    {
        var quote = NewQuote(subtotal: 90, total: 101);
        Assert.Contains(QuoteApprovalAssessment.Evaluate(quote, DefaultPolicy).Reasons, r => r.Code == "GRAND_TOTAL_LIMIT");
        var policy = DefaultPolicy with { GrandTotalLimits = new Dictionary<string, decimal> { ["EUR"] = 1000 } };
        Assert.Contains(QuoteApprovalAssessment.Evaluate(quote, policy).Reasons, r => r.Code == "MANUAL_REVIEW");
        Assert.False(QuoteApprovalAssessment.Evaluate(NewQuote(subtotal: 200, total: 99), DefaultPolicy).Required);
    }

    [Fact]
    public void Advanced_custom_and_postpaid_terms_are_assessed_from_owner_snapshot()
    {
        var ordinary = Payment("FULL_PAYMENT", 30);
        Assert.False(QuoteApprovalAssessment.Evaluate(NewQuote(payment: ordinary), DefaultPolicy).Required);
        var postpaid = QuoteApprovalAssessment.Evaluate(NewQuote(payment: Payment("FULL_PAYMENT", 31)), DefaultPolicy);
        Assert.Contains(postpaid.Reasons, r => r.Code == "POSTPAID_DAYS_LIMIT");
        var custom = QuoteApprovalAssessment.Evaluate(NewQuote(payment: Payment("CUSTOM", 0)), DefaultPolicy);
        Assert.Contains(custom.Reasons, r => r.Code == "CUSTOM_PAYMENT_TERMS");
        var advanced = ordinary with { Lines = [ordinary.Lines[0] with { AmountRule = new("FIXED") { Amount = new("100", "USD") } }] };
        Assert.Contains(QuoteApprovalAssessment.Evaluate(NewQuote(payment: advanced), DefaultPolicy).Reasons, r => r.Code == "CUSTOM_PAYMENT_TERMS");
    }

    [Fact]
    public async Task Required_request_then_independent_approval_records_separate_lifecycle_and_sql_intent()
    {
        var fixture = Create();
        var request = await fixture.Execute("requestQuoteApprovalCommand", "request-key");
        Assert.Equal("COMMITTED", request.Value?.Outcome);
        Assert.Equal("REVIEW", fixture.Quote.Status);
        Assert.Equal("PENDING", fixture.Quote.ApprovalStatus);
        var fingerprint = fixture.Quote.ApprovalContentFingerprint;
        Assert.NotNull(fingerprint);
        fixture.Access.Actor = "approver";
        var approve = await fixture.Execute("approveQuoteCommand", "approve-key", note: "Verified");
        Assert.True(approve.IsSuccess);
        Assert.Equal("REVIEW", fixture.Quote.Status);
        Assert.Equal("APPROVED", fixture.Quote.ApprovalStatus);
        Assert.Equal("approver", fixture.Quote.ApprovedBy);
        Assert.Equal(Now, fixture.Quote.ApprovedAt);
        Assert.Equal(fingerprint, fixture.Quote.ApprovalContentFingerprint);
        Assert.Equal(2, fixture.Persistence.Saves);
        Assert.Equal(2, fixture.Persistence.Commands.Count);
        Assert.Equal(new[] { "QUOTE_APPROVAL_REQUESTED", "QUOTE_APPROVED" }, fixture.Persistence.Outbox.Select(x => x.EventType));
        Assert.All(fixture.Persistence.Commands.Values, x => Assert.NotEmpty(x.AuditId));
    }

    [Fact]
    public async Task Ordinary_quote_is_explicitly_not_required_without_independent_approver()
    {
        var fixture = Create(required: false);
        fixture.Availability.Eligible = false;
        var result = await fixture.Execute("requestQuoteApprovalCommand", "ordinary-key");
        Assert.True(result.IsSuccess);
        Assert.Equal("NOT_REQUIRED", fixture.Quote.ApprovalStatus);
        Assert.Equal("DRAFT", fixture.Quote.Status);
        Assert.Equal(0, fixture.Availability.Calls);
    }

    [Theory]
    [InlineData("creator")]
    [InlineData("requester")]
    public async Task Creator_and_requester_cannot_approve_their_own_quote(string actor)
    {
        var fixture = Create();
        await fixture.Execute("requestQuoteApprovalCommand", "request-key");
        fixture.Access.Actor = actor;
        var result = await fixture.Execute("approveQuoteCommand", "approve-key");
        Assert.Equal("ACCESS_DENIED", result.Error?.Code);
        Assert.Equal("SELF_APPROVAL_FORBIDDEN", result.Error?.Detail);
        Assert.Equal("PENDING", fixture.Quote.ApprovalStatus);
        Assert.Equal(1, fixture.Persistence.Saves);
    }

    [Fact]
    public async Task Missing_eligible_approver_or_creator_evidence_prevents_request()
    {
        var fixture = Create();
        fixture.Availability.Eligible = false;
        var noApprover = await fixture.Execute("requestQuoteApprovalCommand", "request-key");
        Assert.Equal("NO_ELIGIBLE_APPROVER", noApprover.Error?.Detail);
        Assert.Equal(1, fixture.Quote.ResourceVersion);
        fixture.Availability.Eligible = true;
        fixture.Persistence.Creator = null;
        var legacy = await fixture.Execute("requestQuoteApprovalCommand", "legacy-key");
        Assert.Equal("LEGACY_CREATOR_EVIDENCE_MISSING", legacy.Error?.Detail);
        Assert.Equal(0, fixture.Persistence.Saves);
    }

    [Fact]
    public async Task Changed_policy_or_content_cannot_reuse_prior_approval_request()
    {
        var fixture = Create();
        await fixture.Execute("requestQuoteApprovalCommand", "request-key");
        fixture.Access.Actor = "approver";
        fixture.Policies.Policy = fixture.Policies.Policy! with { Version = "policy-2" };
        Assert.Equal("QUOTE_APPROVAL_STALE", (await fixture.Execute("approveQuoteCommand", "stale-policy")).Error?.Code);
        fixture.Policies.Policy = fixture.Policies.Policy with { Version = "policy-1" };
        Assert.True(fixture.Quote.ReplaceDraft(Content(total: 200) with { Title = "Changed commercial title" }, Now.AddMinutes(1)));
        Assert.NotNull(fixture.Quote.ApprovalContentFingerprint);
        Assert.Equal("PENDING", fixture.Quote.ApprovalStatus);
        Assert.Equal("QUOTE_APPROVAL_STALE", (await fixture.Execute("approveQuoteCommand", "stale-content")).Error?.Code);
        Assert.Equal(1, fixture.Persistence.Saves);
    }

    [Fact]
    public async Task Requests_for_changes_require_note_and_return_mutable_draft_without_acceptance()
    {
        var fixture = Create();
        await fixture.Execute("requestQuoteApprovalCommand", "request-key");
        fixture.Access.Actor = "approver";
        Assert.Equal("VALIDATION_FAILED", (await fixture.Execute("requestQuoteApprovalChangesCommand", "changes-empty")).Error?.Code);
        Assert.True((await fixture.Execute("requestQuoteApprovalChangesCommand", "changes-key", note: "Revise discount")).IsSuccess);
        Assert.Equal("CHANGES_REQUESTED", fixture.Quote.ApprovalStatus);
        Assert.Equal("DRAFT", fixture.Quote.Status);
        Assert.Null(fixture.Quote.AcceptedAt);
    }

    [Fact]
    public async Task Idempotency_replay_does_not_duplicate_evidence_and_rechecks_current_access()
    {
        var fixture = Create();
        fixture.Quote.ReplaceDraft(Content(total: 200) with { Notes = "Confidential note" }, Now);
        var expected = fixture.Quote.ResourceVersion;
        var first = await fixture.Execute("requestQuoteApprovalCommand", "request-key", expected);
        var retry = await fixture.Execute("requestQuoteApprovalCommand", "request-key", expected);
        Assert.Equal("REPLAYED", retry.Value?.Outcome);
        Assert.Equal(first.Value?.CommandId, retry.Value?.CommandId);
        Assert.Equal(1, fixture.Persistence.Saves);
        Assert.Single(fixture.Persistence.Outbox);
        fixture.Access.Hidden = "notes";
        var restrictedReplay = await fixture.Execute("requestQuoteApprovalCommand", "request-key", expected);
        Assert.Equal("REPLAYED", restrictedReplay.Value?.Outcome);
        Assert.Null(restrictedReplay.Value?.Result.Quote.Notes);
        fixture.Access.Hidden = null;
        Assert.Equal("IDEMPOTENCY_KEY_REUSED", (await fixture.Execute("requestQuoteApprovalCommand", "request-key", expected, "changed intent")).Error?.Code);
        fixture.Access.Allowed = false;
        Assert.Equal("ACCESS_DENIED", (await fixture.Execute("requestQuoteApprovalCommand", "request-key", expected)).Error?.Code);
        fixture.Access.Allowed = true;
        fixture.Access.RecordAllowed = false;
        Assert.Equal("RESOURCE_NOT_FOUND", (await fixture.Execute("requestQuoteApprovalCommand", "request-key", expected)).Error?.Code);
    }

    [Fact]
    public async Task Field_security_workspace_and_version_fail_before_mutation()
    {
        var fixture = Create();
        fixture.Access.Hidden = "notes";
        Assert.Equal("ACCESS_DENIED", (await fixture.Execute("requestQuoteApprovalCommand", "hidden-key")).Error?.Code);
        fixture.Access.Hidden = null;
        fixture.Access.ReadOnly = "approvalStatus";
        Assert.Equal("ACCESS_DENIED", (await fixture.Execute("requestQuoteApprovalCommand", "readonly-key")).Error?.Code);
        fixture.Access.ReadOnly = null;
        fixture.Access.WorkspaceId = "foreign";
        Assert.Equal("RESOURCE_NOT_FOUND", (await fixture.Execute("requestQuoteApprovalCommand", "foreign-key")).Error?.Code);
        fixture.Access.WorkspaceId = "workspace";
        Assert.Equal("VERSION_CONFLICT", (await fixture.Execute("requestQuoteApprovalCommand", "stale-key", 0)).Error?.Code);
        var missingVersion = await fixture.Handler.ExecuteAsync("requestQuoteApprovalCommand", fixture.Quote.QuoteId, null,
            new("request-test", "correlation-test", "missing-version", null), default);
        Assert.Equal("VALIDATION_FAILED", missingVersion.Error?.Code);
        Assert.Equal(0, fixture.Persistence.Saves);
    }

    [Fact]
    public async Task Missing_policy_fails_closed()
    {
        var fixture = Create(); fixture.Policies.Policy = null;
        var result = await fixture.Execute("requestQuoteApprovalCommand", "policy-key");
        Assert.Equal("INTEGRATION_UNAVAILABLE", result.Error?.Code);
        Assert.Equal(0, fixture.Persistence.Saves);
    }

    [Fact]
    public async Task Configured_policy_version_is_bound_to_effective_values_and_workspace()
    {
        var configuration = new QuoteApprovalPolicyConfiguration { Version = "label", LineDiscountPercent = 10,
            TotalDiscountPercent = 10, PostpaidDays = 30, AdvancedPaymentRequiresApproval = true,
            AlwaysRequireApproval = false, GrandTotalLimits = new() { ["USD"] = 100 } };
        var options = new QuoteApprovalPolicyOptions { Workspaces = new() { ["workspace"] = configuration } };
        var reader = new ConfiguredQuoteApprovalPolicyReader(new Monitor(options));
        var first = await reader.ReadAsync("workspace", default);
        Assert.NotNull(first);
        configuration.GrandTotalLimits["USD"] = 101;
        Assert.NotEqual(first.Version, (await reader.ReadAsync("workspace", default))!.Version);
        Assert.Null(await reader.ReadAsync("foreign", default));
        configuration.LineDiscountPercent = 101;
        Assert.Null(await reader.ReadAsync("workspace", default));
        configuration.LineDiscountPercent = 10;
        configuration.PostpaidDays = null;
        Assert.Null(await reader.ReadAsync("workspace", default));
        Assert.Null(await reader.ReadAsync("Workspace", default));
    }

    private static QuoteDraftContent Content(decimal subtotal = 100, decimal discount = 0, decimal total = 100,
        string lineDiscount = "0", PaymentAgreementSnapshotDocument? payment = null)
    {
        var line = new QuoteLineReadModel("line", "Manual", "1", new("100", "USD"), lineDiscount, "NONE",
            new("100", "USD"), new("0", "USD"), new("0", "USD"), new("100", "USD"));
        return new("Quote", "USD", "requester", null, JsonSerializer.Serialize(new[] { line }, QuotePricing.Json), "[]", "[]",
            subtotal, discount, 0, total, null, null, payment is null ? null : JsonSerializer.Serialize(payment, QuotePricing.Json));
    }

    [Fact]
    public async Task Malformed_configuration_binding_fails_closed_without_breaking_reads()
    {
        var reader = new ConfiguredQuoteApprovalPolicyReader(new InvalidMonitor());
        Assert.Null(await reader.ReadAsync("workspace", default));
    }

    private sealed class InvalidMonitor : IOptionsMonitor<QuoteApprovalPolicyOptions>
    {
        public QuoteApprovalPolicyOptions CurrentValue => throw new InvalidOperationException("Malformed threshold configuration");
        public QuoteApprovalPolicyOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<QuoteApprovalPolicyOptions, string?> listener) => null;
    }
    private static Quote NewQuote(decimal subtotal = 100, decimal discount = 0, decimal total = 100,
        string lineDiscount = "0", PaymentAgreementSnapshotDocument? payment = null) => new("workspace", "quote-test", "Q-0000000001",
            "CONTACT", "buyer", "DIRECT_SALE", null, null, null, Content(subtotal, discount, total, lineDiscount, payment), Now);
    private static PaymentAgreementSnapshotDocument Payment(string kind, int days) => new(1, kind, "USD",
        [new("pay", 1, "Payment", "FULL", new("REMAINDER"), new("100", "USD"),
            new("EVENT_RELATIVE") { Event = "ORDER_CONFIRMED", OffsetDays = days, DayBasis = "CALENDAR" }, ["BANK"], "NONE")]);

    private static Fixture Create(bool required = true)
    {
        var persistence = DispatchProxy.Create<IQuotesPersistence, PersistenceSpy>();
        var spy = (PersistenceSpy)persistence; spy.Quote = NewQuote(total: required ? 200 : 100);
        var access = new Evaluator(); var policy = new Policies();
        var availability = DispatchProxy.Create<IQuoteApprovalAvailability, AvailabilitySpy>();
        return new(new QuoteApprovalCommands(new QuoteAuthorization(access), persistence, new WorkspacePolicy(), policy,
            availability, new Clock()), spy, access, policy, (AvailabilitySpy)availability);
    }
    private sealed record Fixture(QuoteApprovalCommands Handler, PersistenceSpy Persistence, Evaluator Access,
        Policies Policies, AvailabilitySpy Availability)
    {
        internal Quote Quote => Persistence.Quote!;
        internal Task<QuoteOperationResult<QuoteMutationResponse>> Execute(string operation, string key, long? expected = null, string? note = null) =>
            Handler.ExecuteAsync(operation, Quote.QuoteId, note, new("request-test", "correlation-test", key, expected ?? Quote.ResourceVersion), default);
    }
    public class PersistenceSpy : DispatchProxy
    {
        internal Quote? Quote;
        internal string? Creator = "creator";
        internal int Saves;
        internal Dictionary<string, QuoteCommandRecord> Commands = new(StringComparer.Ordinal);
        internal List<QuoteOutboxMessage> Outbox = [];
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
        {
            "BeginAsync" => Task.FromResult<IQuoteTransaction>(new Transaction()),
            "LockAsync" => Task.CompletedTask,
            "LoadQuoteAsync" => Task.FromResult((string)args![0]! == Quote?.WorkspaceId && (string)args[1]! == Quote?.QuoteId ? Quote : null),
            "FindCommandAsync" => Task.FromResult(Commands.GetValueOrDefault((string)args![0]!)),
            "ReadCreatorAsync" => Task.FromResult(Creator),
            "AddCommand" => AddCommand((QuoteCommandRecord)args![0]!),
            "AddOutbox" => AddOutbox((QuoteOutboxMessage)args![0]!),
            "SaveChangesAsync" => Save(),
            _ => throw new InvalidOperationException($"Unexpected persistence call: {method?.Name}")
        };
        private object? AddCommand(QuoteCommandRecord value) { Commands.Add(value.ScopeKey, value); return null; }
        private object? AddOutbox(QuoteOutboxMessage value) { Outbox.Add(value); return null; }
        private Task Save() { Saves++; return Task.CompletedTask; }
    }
    private sealed class Transaction : IQuoteTransaction
    { public Task CommitAsync(CancellationToken ct) => Task.CompletedTask; public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
    public class AvailabilitySpy : DispatchProxy
    {
        internal bool Eligible = true; internal int Calls;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            Assert.Equal("HasEligibleApproverAsync", method?.Name); Calls++;
            Assert.Equal("workspace", args![0]); Assert.Equal("creator", args[2]); Assert.Equal("requester", args[3]);
            return Task.FromResult(Eligible);
        }
    }
    private sealed class Evaluator : IRecordAccessEvaluator
    {
        internal string Actor = "requester", WorkspaceId = "workspace";
        internal bool Allowed = true, RecordAllowed = true;
        internal string? Hidden, ReadOnly;
        public Task<RecordAccessAuthorization> AuthorizeResourceAsync(string resource, string capability, IReadOnlyList<string>? fields,
            RecordAccessRepresentation representation, RecordAccessRequestContext context, CancellationToken ct) =>
            Task.FromResult(new RecordAccessAuthorization(Allowed, Allowed ? "AUTHORIZED" : "ACCESS_DENIED",
                new(WorkspaceId, "account", Actor, "membership"), RecordAccessScopeFilter.Workspace, null, "WORKSPACE",
                fields!.ToDictionary(f => f, f => f == Hidden ? RecordFieldEnforcement.Withheld : f == ReadOnly ? RecordFieldEnforcement.ReadOnly : RecordFieldEnforcement.ReadWrite),
                [], ["quotes.read", "quotes.update", "quotes.approve"], "policy", resource, capability, Allowed));
        public Task<RecordAccessRecordDecision> AuthorizeRecordAsync(RecordAccessAuthorization access, string id,
            RecordAccessFacts facts, string point, RecordAccessRequestContext context, CancellationToken ct) =>
            Task.FromResult(new RecordAccessRecordDecision(RecordAllowed, "WORKSPACE", null));
    }
    private sealed class Policies : IQuoteApprovalPolicyReader
    { internal QuoteApprovalPolicy? Policy = DefaultPolicy; public Task<QuoteApprovalPolicy?> ReadAsync(string workspaceId, CancellationToken ct) => Task.FromResult(Policy); }
    private sealed class WorkspacePolicy : IQuoteWorkspacePolicyReader
    { public Task<QuoteWorkspacePolicy?> ReadAsync(string workspaceId, CancellationToken ct) => Task.FromResult<QuoteWorkspacePolicy?>(new(true, ["USD"], 1)); }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Monitor(QuoteApprovalPolicyOptions value) : IOptionsMonitor<QuoteApprovalPolicyOptions>
    {
        public QuoteApprovalPolicyOptions CurrentValue => value;
        public QuoteApprovalPolicyOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<QuoteApprovalPolicyOptions, string?> listener) => null;
    }
}
