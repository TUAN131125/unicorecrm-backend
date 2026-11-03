using System.Reflection;
using UnicoreCRM.Crm.Contacts.Contracts;
using UnicoreCRM.Crm.Deals.Application.Common;
using UnicoreCRM.Crm.Deals.Application.VerifyQuoteSource;
using UnicoreCRM.Crm.Deals.Domain;
using UnicoreCRM.Crm.Organizations.Contracts;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Sales.Quotes.Contracts;
using Xunit;

namespace UnicoreCRM.ArchitectureTests;

public sealed class QuoteRelationshipVerifierTests
{
    private static readonly TrustedWorkspaceContext Trusted = new("ws_quote", "account", "member", "membership");

    [Fact]
    public async Task Lease_holds_owner_transaction_and_trusted_workspace_until_disposed()
    {
        var f = Create();
        var lease = await Acquire(f);
        Assert.Null(lease.ErrorCode);
        Assert.Equal(f.Deal.DealId, lease.Source?.DealId);
        Assert.Equal("aggregate:" + Trusted.WorkspaceId + ":" + f.Deal.DealId, f.Lock.Scope);
        Assert.Equal(Trusted.WorkspaceId, f.Read.Workspace);
        Assert.Equal(0, f.Read.Transaction.Disposals);
        await lease.DisposeAsync();
        await lease.DisposeAsync();
        Assert.Equal(1, f.Read.Transaction.Disposals);
    }

    [Theory]
    [InlineData("buyerRef")]
    [InlineData("contactId")]
    [InlineData("sourceLeadId")]
    [InlineData("ownerId")]
    [InlineData("stageCategory")]
    [InlineData("archivedAt")]
    [InlineData("resourceVersion")]
    public async Task Withheld_source_fields_fail_before_transaction(string field)
    {
        var f = Create();
        f.Access.Hidden = field;
        await using var lease = await Acquire(f);
        Assert.Equal("ACCESS_DENIED", lease.ErrorCode);
        Assert.Equal(0, f.Read.Begins);
    }

    [Fact]
    public async Task Workspace_mismatch_and_missing_capability_never_read_owner()
    {
        var f = Create();
        f.Access.Allowed = false;
        Assert.Equal("ACCESS_DENIED", (await Acquire(f)).ErrorCode);
        f.Access.WorkspaceMismatch = true;
        Assert.Equal("ACCESS_DENIED", (await Acquire(f)).ErrorCode);
        Assert.Equal(0, f.Read.Begins);
    }

    [Fact]
    public async Task Out_of_scope_foreign_workspace_and_stale_version_release_transaction()
    {
        var f = Create();
        f.Access.RecordAllowed = false;
        Assert.Equal("RESOURCE_NOT_FOUND", (await Acquire(f)).ErrorCode);
        f.Access.RecordAllowed = true;
        f.Read.StoredWorkspace = "other-workspace";
        Assert.Equal("RESOURCE_NOT_FOUND", (await Acquire(f)).ErrorCode);
        f.Read.StoredWorkspace = Trusted.WorkspaceId;
        Assert.Equal("VERSION_CONFLICT", (await f.Verifier.AcquireDealAsync(f.Deal.DealId,
            f.Deal.Version + 1, true, "request", "correlation", default)).ErrorCode);
        Assert.Equal(3, f.Read.Transaction.Disposals);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Closed_or_archived_deal_cannot_seed_a_new_quote(bool archive)
    {
        var f = Create();
        if (archive) f.Deal.Archive("test", DateTimeOffset.UtcNow);
        else f.Deal.MarkWon("QUOTE_ACCEPTED", "quote", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        Assert.Equal("VALIDATION_FAILED", (await Acquire(f)).ErrorCode);
        Assert.Equal(1, f.Read.Transaction.Disposals);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Owner_contention_is_a_safe_contract_error_and_disposes_acquired_transaction(bool onBegin)
    {
        var f = Create();
        f.Read.BusyOnBegin = onBegin;
        f.Lock.Busy = !onBegin;
        Assert.Equal("IDEMPOTENCY_REQUEST_IN_PROGRESS", (await Acquire(f)).ErrorCode);
        Assert.Equal(onBegin ? 0 : 1, f.Read.Transaction.Disposals);
    }

    [Fact]
    public async Task Buyer_contract_eligibility_must_be_established()
    {
        var f = Create();
        Assert.True(await f.Verifier.VerifyBuyerAsync(new("CONTACT", "contact"), "request", "correlation", default));
        f.Buyers.ContactEligible = false;
        Assert.False(await f.Verifier.VerifyBuyerAsync(new("CONTACT", "contact"), "request", "correlation", default));
        Assert.True(await f.Verifier.VerifyBuyerAsync(new("ORGANIZATION_ACCOUNT", "org"), "request", "correlation", default));
        f.Buyers.OrganizationStatus = null;
        Assert.False(await f.Verifier.VerifyBuyerAsync(new("ORGANIZATION_ACCOUNT", "org"), "request", "correlation", default));
        f.Buyers.OrganizationStatus = "archived";
        Assert.False(await f.Verifier.VerifyBuyerAsync(new("ORGANIZATION_ACCOUNT", "org"), "request", "correlation", default));
    }

    private static Task<IQuoteDealSourceLease> Acquire(Fixture f) => f.Verifier.AcquireDealAsync(
        f.Deal.DealId, f.Deal.Version, true, "request", "correlation", default);
    private sealed record Fixture(QuoteRelationshipVerifier Verifier, Deal Deal, ReadSpy Read, LockSpy Lock,
        AccessEvaluator Access, Buyers Buyers);
    private static Fixture Create()
    {
        var deal = new Deal(Trusted.WorkspaceId, new("Quote source", new("CONTACT", "contact"), new("100", "VND"),
            "20", Trusted.MemberId, new(2027, 1, 1), null, null, [], null), "DISCOVERY",
            DealStageCategory.Open, DealForecastCategory.Pipeline, DateTimeOffset.UnixEpoch);
        var reads = DispatchProxy.Create<IDealsPersistence, ReadSpy>();
        var read = (ReadSpy)(object)reads; read.Deal = deal;
        var locks = DispatchProxy.Create<IDealPipelinePersistence, LockSpy>();
        var locking = (LockSpy)(object)locks;
        var access = new AccessEvaluator();
        var buyers = new Buyers();
        return new(new(new(access), reads, locks, buyers, buyers), deal, read, locking, access, buyers);
    }
    public class ReadSpy : DispatchProxy
    {
        internal Deal Deal = null!;
        internal string? Workspace;
        internal string StoredWorkspace = Trusted.WorkspaceId;
        internal int Begins;
        internal bool BusyOnBegin;
        internal Transaction Transaction = new();
        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            if (method?.Name == "BeginSerializableAsync")
            {
                Begins++;
                if (BusyOnBegin) throw new DealsPersistenceBusyException(new InvalidOperationException());
                return Task.FromResult<IDealsTransaction>(Transaction);
            }
            if (method?.Name == "ReadDealAsync")
            {
                Workspace = (string)arguments![0]!;
                return Task.FromResult(Workspace == StoredWorkspace ? Deal : null);
            }
            throw new InvalidOperationException("Quote source must not mutate or track Deals: " + method?.Name);
        }
    }
    public class LockSpy : DispatchProxy
    {
        internal string? Scope;
        internal bool Busy;
        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            Assert.Equal("LockCommandAsync", method?.Name);
            Scope = (string)arguments![0]!;
            if (Busy) throw new DealsPersistenceBusyException(new InvalidOperationException());
            return Task.CompletedTask;
        }
    }
    internal sealed class Transaction : IDealsTransaction
    {
        internal int Disposals;
        public Task CommitAsync(CancellationToken ct) => throw new InvalidOperationException("Source lease must not commit Deal mutations.");
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }
    private sealed class Buyers : IContactLinkReader, IOrganizationSummaryReader
    {
        internal bool ContactEligible = true;
        internal string? OrganizationStatus = "active";
        public Task<ContactLinkTarget?> ReadAsync(string id, string request, string correlation, CancellationToken ct) =>
            Task.FromResult<ContactLinkTarget?>(new(id, null, ContactEligible));
        public Task<IReadOnlyList<ContactLinkTarget>> SearchAsync(string search, string request, string correlation, CancellationToken ct) =>
            throw new NotSupportedException();
        Task<OrganizationSummaryReadResult> IOrganizationSummaryReader.ReadAsync(string id, string request, string correlation, CancellationToken ct) =>
            Task.FromResult(new OrganizationSummaryReadResult(OrganizationSummaryReadStatus.Succeeded,
                new(id, null, OrganizationStatus, null, null, null, 1)));
    }
    private sealed class AccessEvaluator : IRecordAccessEvaluator
    {
        internal bool Allowed = true, RecordAllowed = true, WorkspaceMismatch;
        internal string? Hidden;
        public Task<RecordAccessAuthorization> AuthorizeResourceAsync(string resource, string capability,
            IReadOnlyList<string>? fields, RecordAccessRepresentation representation, RecordAccessRequestContext context, CancellationToken ct)
        {
            Assert.Equal("deals.read", capability);
            var enforcement = fields!.ToDictionary(field => field,
                field => field == Hidden ? RecordFieldEnforcement.Withheld : RecordFieldEnforcement.ReadWrite);
            return Task.FromResult(new RecordAccessAuthorization(Allowed, "ACCESS_DENIED", WorkspaceMismatch ? null : Trusted,
                RecordAccessScopeFilter.Workspace, null, "WORKSPACE", enforcement, [], ["deals.read"], "test", resource, capability, Allowed));
        }
        public Task<RecordAccessRecordDecision> AuthorizeRecordAsync(RecordAccessAuthorization access, string id,
            RecordAccessFacts facts, string point, RecordAccessRequestContext context, CancellationToken ct) =>
            Task.FromResult(new RecordAccessRecordDecision(RecordAllowed, "WORKSPACE", RecordAllowed));
    }
}
