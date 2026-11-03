using System.Reflection;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Sales.Products.Contracts;
using UnicoreCRM.Sales.Quotes.Application.Common;
using UnicoreCRM.Sales.Quotes.Contracts;
using UnicoreCRM.Sales.Quotes.Domain;
using Xunit;

namespace UnicoreCRM.ArchitectureTests;

public sealed class QuoteDraftCommandSecurityTests
{
    private static readonly TrustedWorkspaceContext Trusted = new("ws_security", "account", "member", "membership");
    private static readonly QuoteDraftRequest Request = new("Security quote", "VND",
        [new("Manual consulting", "1", new("1000", "VND"), "0", "NONE")],
        new("CONTACT", "contact"), "DIRECT_SALE", Notes: "Sensitive note");
    private static readonly QuoteCommandMetadata Command = new("request", "correlation", "key", null);

    [Fact]
    public async Task Mutation_occurrence_time_conforms_to_utc_wire_contract()
    {
        var fixture = Create();
        var result = await Execute(fixture);
        Assert.True(result.IsSuccess);
        Assert.EndsWith("Z", result.Value!.OccurredAt);
        Assert.False(result.Value.Result.Quote.Actions.Accept.Allowed);
        Assert.Equal(new[] { "QUOTE_ACCEPTANCE_BLOCKED" }, result.Value.Result.Quote.Actions.Accept.BlockerCodes);
    }

    [Fact]
    public async Task Disabled_workspace_quote_policy_refuses_new_write()
    {
        var fixture = Create(); fixture.Ports.PolicyEnabled = false;
        Assert.Equal("ACCESS_DENIED", (await Execute(fixture)).Error?.Code);
        Assert.Equal(0, fixture.Persistence.Saves);
        Assert.Null(fixture.Persistence.Quote);
    }

    [Theory]
    [InlineData("title")]
    [InlineData("lineItems")]
    [InlineData("ownerId")]
    [InlineData("notes")]
    [InlineData("grandTotal")]
    public async Task Create_cannot_write_fields_restricted_to_read_only(string field)
    {
        var f = Create(); f.Access.ReadOnly = field;
        Assert.Equal("ACCESS_DENIED", (await Execute(f)).Error?.Code);
        Assert.Null(f.Persistence.Quote);
        Assert.Equal(0, f.Persistence.Saves);
    }

    [Fact]
    public async Task Update_cannot_change_read_only_content_and_does_not_mutate_aggregate()
    {
        var f = Create(); Assert.True((await Execute(f)).IsSuccess);
        f.Persistence.Command = null;
        f.Access.ReadOnly = "title";
        var old = f.Persistence.Quote!;
        var version = old.ResourceVersion;
        var result = await f.Handler.ExecuteAsync(Request with { Title = "Unauthorized replacement" },
            Command with { ExpectedVersion = version, IdempotencyKey = "update" }, old.QuoteId, null, default);
        Assert.Equal("ACCESS_DENIED", result.Error?.Code);
        Assert.Equal(Request.Title, old.Title);
        Assert.Equal(version, old.ResourceVersion);
        Assert.Equal(1, f.Persistence.Saves);
    }

    [Fact]
    public async Task Replay_obeys_current_read_projection_without_requiring_current_write_permission()
    {
        var f = Create(); Assert.True((await Execute(f)).IsSuccess);
        f.Access.Hidden = "notes";
        f.Access.ReadOnly = "title";
        var replay = await Execute(f);
        Assert.Null(replay.Error);
        Assert.Equal("REPLAYED", replay.Value?.Outcome);
        Assert.Null(replay.Value?.Result.Quote.Notes);
        Assert.Equal("Sensitive note", f.Persistence.Quote?.Notes);
        Assert.Equal(1, f.Persistence.Saves);
    }

    [Fact]
    public async Task Replay_requires_current_record_scope_and_current_capability()
    {
        var f = Create(); Assert.True((await Execute(f)).IsSuccess);
        f.Access.RecordAllowed = false;
        Assert.Equal("RESOURCE_NOT_FOUND", (await Execute(f)).Error?.Code);
        f.Access.RecordAllowed = true; f.Access.Allowed = false;
        Assert.Equal("ACCESS_DENIED", (await Execute(f)).Error?.Code);
        Assert.Equal(1, f.Persistence.Saves);
    }

    [Fact]
    public async Task Replay_does_not_expose_foreign_workspace_record()
    {
        var f = Create(); Assert.True((await Execute(f)).IsSuccess);
        Set(f.Persistence.Quote!, "WorkspaceId", "other-workspace");
        Assert.Equal("RESOURCE_NOT_FOUND", (await Execute(f)).Error?.Code);
        Assert.Equal(Trusted.WorkspaceId, f.Persistence.LastReadWorkspace);
    }

    [Theory]
    [InlineData("SENT", null, null)]
    [InlineData("ACCEPTED", null, null)]
    [InlineData("DRAFT", "archived", null)]
    public async Task Lifecycle_bound_content_cannot_be_replaced(string status, string? archived, string? approval)
    {
        var f = Create(); Assert.True((await Execute(f)).IsSuccess);
        f.Persistence.Command = null;
        var quote = f.Persistence.Quote!;
        Set(quote, "Status", status);
        if (archived is not null) Set(quote, "ArchivedAt", DateTimeOffset.UtcNow);
        Set(quote, "ApprovalStatus", approval);
        var version = quote.ResourceVersion;
        var result = await f.Handler.ExecuteAsync(Request with { Title = "Forbidden" },
            Command with { ExpectedVersion = version }, quote.QuoteId, null, default);
        Assert.Equal("QUOTE_DRAFT_IMMUTABLE", result.Error?.Code);
        Assert.Equal(Request.Title, quote.Title);
        Assert.Equal(version, quote.ResourceVersion);
        Assert.Equal(1, f.Persistence.Saves);
    }

    [Theory]
    [InlineData("DRAFT", null)]
    [InlineData("DRAFT", "PENDING")]
    [InlineData("REVIEW", "APPROVED")]
    public async Task Commercial_replacement_preserves_historical_approval_without_granting_authority(string status, string? approval)
    {
        var f = Create(); Assert.True((await Execute(f)).IsSuccess);
        f.Persistence.Command = null;
        var quote = f.Persistence.Quote!;
        Set(quote, "ApprovalRequired", true);
        Set(quote, "Status", status);
        Set(quote, "ApprovalStatus", approval);
        Set(quote, "ApprovalContentFingerprint", "old-fingerprint");
        Set(quote, "ApprovalPolicyVersion", "old-policy");
        Set(quote, "ApprovedBy", "old-approver");
        var approvedAt = DateTimeOffset.UtcNow;
        Set(quote, "ApprovedAt", approvedAt);
        var version = quote.ResourceVersion;
        var result = await f.Handler.ExecuteAsync(Request with { Title = "Forbidden approval bypass" },
            Command with { ExpectedVersion = version }, quote.QuoteId, null, default);
        Assert.Null(result.Error);
        Assert.Equal(version + 1, quote.ResourceVersion);
        Assert.Equal("DRAFT", quote.Status);
        Assert.Equal(approval, quote.ApprovalStatus);
        Assert.True(quote.ApprovalRequired);
        Assert.Equal("old-fingerprint", quote.ApprovalContentFingerprint);
        Assert.Equal("old-policy", quote.ApprovalPolicyVersion);
        Assert.Equal(approvedAt, quote.ApprovedAt);
        Assert.Equal("old-approver", quote.ApprovedBy);
        Assert.Equal(2, f.Persistence.Saves);
    }

    [Fact]
    public async Task Commercial_edit_does_not_write_retired_approval_fields()
    {
        var f = Create(); Assert.True((await Execute(f)).IsSuccess);
        f.Persistence.Command = null;
        var quote = f.Persistence.Quote!;
        Set(quote, "ApprovalRequired", true);
        f.Access.ReadOnly = "approvedBy";
        var version = quote.ResourceVersion;
        var result = await f.Handler.ExecuteAsync(Request with { Title = "Forbidden" },
            Command with { ExpectedVersion = version }, quote.QuoteId, null, default);
        Assert.Null(result.Error);
        Assert.Equal(version + 1, quote.ResourceVersion);
        Assert.Null(quote.ApprovedBy);
        Assert.True(quote.ApprovalRequired);
    }

    [Fact]
    public async Task Identical_read_only_payment_snapshot_can_survive_unrelated_draft_update()
    {
        var f = Create();
        var request = Request with { PaymentAgreement = new("FULL_PAYMENT", "VND",
            [new(1, "Full payment", "FULL", new("REMAINDER"),
                new("FIXED_DATE") { Date = "2026-12-01" }, ["BANK"], "NONE")]) };
        var created = await f.Handler.ExecuteAsync(request, Command, null, null, default);
        Assert.Null(created.Error);
        var quote = f.Persistence.Quote!;
        var originalSnapshot = quote.PaymentAgreementJson;
        Assert.NotNull(originalSnapshot);
        f.Persistence.Command = null;
        f.Access.ReadOnly = "paymentAgreement";
        var updated = await f.Handler.ExecuteAsync(request with { Title = "Updated title" },
            Command with { ExpectedVersion = quote.ResourceVersion }, quote.QuoteId, null, default);
        Assert.Null(updated.Error);
        Assert.Equal("Updated title", quote.Title);
        Assert.Equal(originalSnapshot, quote.PaymentAgreementJson);
        Assert.Equal(2, f.Persistence.Saves);
    }

    [Fact]
    public async Task Default_owner_and_member_validation_use_trusted_workspace()
    {
        var f = Create();
        var result = await Execute(f);
        Assert.Null(result.Error);
        Assert.Equal(Trusted.MemberId, f.Persistence.Quote?.OwnerId);
        Assert.Equal((Trusted.WorkspaceId, Trusted.MemberId), f.Ports.MemberCheck);
        Assert.Equal(Trusted.WorkspaceId, f.Ports.PolicyWorkspace);
    }

    [Fact]
    public async Task Invalid_owner_and_own_scope_owner_reassignment_fail_closed()
    {
        var f = Create(); f.Ports.MemberValid = false;
        Assert.Equal("VALIDATION_FAILED", (await Execute(f)).Error?.Code);
        f.Ports.MemberValid = true;
        f.Access.OwnScope = true;
        var result = await f.Handler.ExecuteAsync(Request with { OwnerId = "other-member" }, Command, null, null, default);
        Assert.Equal("ACCESS_DENIED", result.Error?.Code);
        Assert.Equal((Trusted.WorkspaceId, "other-member"), f.Ports.MemberCheck);
        Assert.Null(f.Persistence.Quote);
    }

    [Fact]
    public async Task Update_lookup_cannot_target_a_foreign_workspace_quote()
    {
        var f = Create(); Assert.True((await Execute(f)).IsSuccess);
        var quote = f.Persistence.Quote!; f.Persistence.Command = null;
        Set(quote, "WorkspaceId", "other-workspace");
        var result = await f.Handler.ExecuteAsync(Request, Command with { ExpectedVersion = quote.ResourceVersion },
            quote.QuoteId, null, default);
        Assert.Equal("RESOURCE_NOT_FOUND", result.Error?.Code);
        Assert.Equal(Trusted.WorkspaceId, f.Persistence.LastReadWorkspace);
        Assert.Equal(1, f.Persistence.Saves);
    }

    private static Task<QuoteOperationResult<QuoteMutationResponse>> Execute(Fixture f) =>
        f.Handler.ExecuteAsync(Request, Command, null, null, default);
    private static void Set(Quote quote, string property, object? value) => typeof(Quote)
        .GetProperty(property, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(quote, value);
    private sealed record Fixture(QuoteDraftCommands Handler, PersistenceSpy Persistence, AccessEvaluator Access, Ports Ports);
    private static Fixture Create()
    {
        var persistence = DispatchProxy.Create<IQuotesPersistence, PersistenceSpy>();
        var access = new AccessEvaluator(); var ports = new Ports();
        return new(new(new(access), persistence, ports, ports, ports, ports, TimeProvider.System),
            (PersistenceSpy)(object)persistence, access, ports);
    }
    public class PersistenceSpy : DispatchProxy
    {
        internal Quote? Quote;
        internal QuoteCommandRecord? Command;
        internal int Saves;
        internal string? LastReadWorkspace;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            switch (method?.Name)
            {
                case "BeginAsync": return Task.FromResult<IQuoteTransaction>(new Transaction());
                case "LockAsync": return Task.CompletedTask;
                case "FindCommandAsync": return Task.FromResult(Command);
                case "NextNumberAsync": return Task.FromResult("Q-000001");
                case "LoadQuoteAsync":
                case "ReadQuoteAsync":
                    LastReadWorkspace = (string)args![0]!;
                    return Task.FromResult(Quote?.WorkspaceId == LastReadWorkspace && Quote?.QuoteId == (string)args[1]! ? Quote : null);
                case "AddQuote": Quote = (Quote)args![0]!; return null;
                case "AddCommand": Command = (QuoteCommandRecord)args![0]!; return null;
                case "AddOutbox": return null;
                case "SaveChangesAsync": Saves++; return Task.CompletedTask;
                default: throw new InvalidOperationException("Unexpected persistence call: " + method?.Name);
            }
        }
    }
    private sealed class Transaction : IQuoteTransaction
    {
        public Task CommitAsync(CancellationToken ct) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class Ports : IQuoteRelationshipVerifier, IQuoteProductSnapshotReader,
        IWorkspaceMemberReferenceValidator, IQuoteWorkspacePolicyReader
    {
        internal bool MemberValid = true;
        internal bool PolicyEnabled = true;
        internal (string Workspace, string Member) MemberCheck;
        internal string? PolicyWorkspace;
        public Task<bool> VerifyBuyerAsync(QuoteBuyerReference buyer, string request, string correlation, CancellationToken ct) => Task.FromResult(true);
        public Task<IQuoteDealSourceLease> AcquireDealAsync(string deal, long? version, bool open, string request, string correlation, CancellationToken ct) =>
            throw new InvalidOperationException("Direct sale must not acquire a Deal source.");
        public Task<QuoteProductSnapshotResult> ReadAsync(IReadOnlyCollection<string> ids, string request, string correlation, CancellationToken ct) =>
            throw new InvalidOperationException("Manual lines must not read Product snapshots.");
        public Task<bool> IsActiveMemberAsync(string workspace, string member, CancellationToken ct)
        { MemberCheck = (workspace, member); return Task.FromResult(MemberValid); }
        public Task<QuoteWorkspacePolicy?> ReadAsync(string workspace, CancellationToken ct)
        { PolicyWorkspace = workspace; return Task.FromResult<QuoteWorkspacePolicy?>(new(PolicyEnabled, ["VND"], 1)); }
    }
    private sealed class AccessEvaluator : IRecordAccessEvaluator
    {
        internal bool Allowed = true, RecordAllowed = true, OwnScope;
        internal string? Hidden, ReadOnly;
        public Task<RecordAccessAuthorization> AuthorizeResourceAsync(string resource, string capability,
            IReadOnlyList<string>? fields, RecordAccessRepresentation representation, RecordAccessRequestContext context, CancellationToken ct)
        {
            Assert.Contains(capability, new[] { "quotes.create", "quotes.update" });
            var enforcement = fields!.ToDictionary(field => field, field => field == Hidden ? RecordFieldEnforcement.Withheld
                : field == ReadOnly ? RecordFieldEnforcement.ReadOnly : RecordFieldEnforcement.ReadWrite);
            return Task.FromResult(new RecordAccessAuthorization(Allowed, "ACCESS_DENIED", Trusted,
                OwnScope ? RecordAccessScopeFilter.OwnedByMember : RecordAccessScopeFilter.Workspace,
                OwnScope ? Trusted.MemberId : null, "WORKSPACE", enforcement, [], [capability], "test", resource, capability, Allowed));
        }
        public Task<RecordAccessRecordDecision> AuthorizeRecordAsync(RecordAccessAuthorization access, string id,
            RecordAccessFacts facts, string point, RecordAccessRequestContext context, CancellationToken ct) =>
            Task.FromResult(new RecordAccessRecordDecision(RecordAllowed, "WORKSPACE", RecordAllowed));
    }
}
