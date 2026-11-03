using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Sales.Quotes.Application.Common;
using UnicoreCRM.Sales.Quotes.Contracts;
using UnicoreCRM.Sales.Quotes.Domain;
using Xunit;
namespace UnicoreCRM.ArchitectureTests;

public sealed class QuoteDocumentTests
{
    [Fact]
    public void Invalid_document_policy_binding_fails_closed_without_breaking_quote_reads()
    {
        Assert.Null(new QuoteDocumentPolicyReader(new InvalidOptions()).EnabledVersion("workspace"));
    }
    private sealed class InvalidOptions : Microsoft.Extensions.Options.IOptionsMonitor<QuoteDocumentOptions>
    {
        public QuoteDocumentOptions CurrentValue => throw new InvalidOperationException("Invalid Enabled binding");
        public QuoteDocumentOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<QuoteDocumentOptions, string?> listener) => null;
    }
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-25T00:00:00Z");
    [Fact]
    public async Task Issuance_binds_pre_mutation_version_and_holds_owner_leases_through_commit()
    {
        var f = Create(); var before = f.Spy.Quote!.ResourceVersion; var fingerprint = QuoteApprovalAssessment.ContentFingerprint(f.Spy.Quote);
        var delivery = f.Spy.Quote.DeliveryHistoryJson;
        f.Spy.OnCommit = () => { Assert.False(f.Seller.Disposed); Assert.False(f.Buyer.Disposed); };
        var result = await f.Issue("document-key", before); Assert.True(result.IsSuccess, result.Error?.Detail);
        var d = Assert.Single(f.Documents.Items);
        Assert.Equal(before, d.SourceResourceVersion); Assert.Equal(before + 1, d.ResultResourceVersion);
        Assert.Equal("DRAFT", f.Spy.Quote.Status); Assert.Equal(delivery, f.Spy.Quote.DeliveryHistoryJson);
        Assert.Equal(fingerprint, QuoteApprovalAssessment.ContentFingerprint(f.Spy.Quote));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(f.Renderer.Bytes)), d.PdfSha256);
        Assert.Single(f.Spy.Commands); Assert.Single(f.Spy.Outbox); Assert.True(f.Seller.Disposed); Assert.True(f.Buyer.Disposed);
        Assert.Equal("QUOTE_DOCUMENT_ISSUED", f.Spy.Outbox[0].EventType);
    }
    [Fact]
    public async Task Replay_authorizes_historical_fields_and_buyer_before_new_lifecycle_eligibility()
    {
        var f = Create(); var version = f.Spy.Quote!.ResourceVersion; Assert.True((await f.Issue("historical-key", version)).IsSuccess);
        QuoteRevisionTests.Set(f.Spy.Quote, "Status", "EXPIRED"); QuoteRevisionTests.Set(f.Spy.Quote, "BuyerId", "current-other-buyer");
        var replay = await f.Issue("historical-key", version); Assert.Equal("REPLAYED", replay.Value?.Outcome);
        Assert.Equal("buyer", f.Buyer.LastId); Assert.Single(f.Documents.Items); Assert.Equal(1, f.Renderer.Calls);
        f.Access.Hidden = "grandTotal";
        Assert.Equal("ACCESS_DENIED", (await f.Issue("historical-key", version)).Error?.Code);
    }
    [Theory]
    [InlineData("RENDER_TIMEOUT")]
    [InlineData("RENDER_MEMORY_LIMIT")]
    [InlineData("RENDER_DOCUMENT_LIMIT")]
    public async Task Rendering_failure_creates_no_document_receipt_event_or_version_change(string failure)
    {
        var f = Create(); var before = f.Spy.Quote!.ResourceVersion; f.Renderer.Failure = failure;
        Assert.Equal(failure, (await f.Issue("failure-key", before)).Error?.Detail);
        Assert.Equal(before, f.Spy.Quote.ResourceVersion); Assert.Empty(f.Documents.Items); Assert.Empty(f.Spy.Commands); Assert.Empty(f.Spy.Outbox); Assert.Equal(0, f.Spy.Saves);
        Assert.True(f.Seller.Disposed); Assert.True(f.Buyer.Disposed);
    }
    [Fact]
    public async Task Read_permission_does_not_grant_issue_and_hidden_rendered_fields_are_refused()
    {
        var f = Create(); f.Access.DenyIssue = true;
        Assert.Equal("ACCESS_DENIED", (await f.Issue("denied-key", f.Spy.Quote!.ResourceVersion)).Error?.Code);
        f.Access.DenyIssue = false; f.Access.Hidden = "paymentAgreement";
        Assert.Equal("ACCESS_DENIED", (await f.Issue("hidden-key", f.Spy.Quote.ResourceVersion)).Error?.Code);
        Assert.Empty(f.Documents.Items); Assert.Equal(0, f.Renderer.Calls);
    }
    [Fact]
    public async Task Missing_seller_or_document_policy_remain_blockers_while_old_approval_is_historical()
    {
        var f = Create(); f.Seller.Missing = true;
        Assert.Equal("SELLER_LEGAL_NAME_REQUIRED", (await f.Issue("seller-key", f.Spy.Quote!.ResourceVersion)).Error?.Detail);
        f.Seller.Missing = false; f.DocumentPolicy.Enabled = false;
        Assert.Equal("DOCUMENT_POLICY_MISSING_OR_DISABLED", (await f.Issue("policy-key", f.Spy.Quote.ResourceVersion)).Error?.Detail);
        f.DocumentPolicy.Enabled = true; QuoteRevisionTests.Set(f.Spy.Quote, "ApprovalPolicyVersion", "old-policy");
        Assert.True((await f.Issue("direct-key", f.Spy.Quote.ResourceVersion)).IsSuccess);
        Assert.Equal("old-policy", f.Spy.Quote.ApprovalPolicyVersion);
        Assert.Equal("direct-issuance-v1", Assert.Single(f.Documents.Items).ApprovalPolicyVersion);
    }
    private static Fixture Create()
    {
        var port = DispatchProxy.Create<IQuotesPersistence, Persistence>(); var spy = (Persistence)port; spy.Quote = QuoteRevisionTests.Source();
        QuoteRevisionTests.Set(spy.Quote, "Status", "DRAFT"); var policy = new Approvals();
        var assessment = QuoteApprovalAssessment.Evaluate(spy.Quote, policy.Value);
        spy.Quote.RequestApproval(false, "[]", assessment.Fingerprint, assessment.PolicyVersion, "requester", null, Now);
        var access = new Access(); var documents = new Documents(); var seller = new Seller(); var buyer = new Buyer(); var renderer = new Renderer(); var documentPolicy = new Policy();
        var handler = new QuoteDocumentCommands(new(access), port, documents, documentPolicy, new Workspace(), policy, new Zone(), seller, [buyer], renderer, new Clock());
        return new(handler, spy, documents, access, seller, buyer, renderer, documentPolicy);
    }
    private sealed record Fixture(QuoteDocumentCommands Handler, Persistence Spy, Documents Documents, Access Access, Seller Seller, Buyer Buyer, Renderer Renderer, Policy DocumentPolicy)
    {
        internal Task<QuoteOperationResult<QuoteDocumentIssueResponse>> Issue(string key, long version) => Handler.IssueAsync(Spy.Quote!.QuoteId, new(), new("request", "correlation", key, version), default);
    }
    public class Persistence : QuoteApprovalTests.PersistenceSpy
    {
        internal Action? OnCommit;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name == "BeginAsync" ? Task.FromResult<IQuoteTransaction>(new Tx(this)) : base.Invoke(method, args);
        private sealed class Tx(Persistence spy) : IQuoteTransaction
        { public Task CommitAsync(CancellationToken ct) { spy.OnCommit?.Invoke(); return Task.CompletedTask; } public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
    }
    private sealed class Documents : IQuoteDocumentPersistence
    {
        internal List<QuoteDocument> Items = [];
        public Task<QuoteDocument?> FindAsync(string ws, string q, string id, CancellationToken ct) => Task.FromResult(Items.SingleOrDefault(x => x.DocumentId == id && x.WorkspaceId == ws && x.QuoteId == q));
        public Task<QuoteDocument?> FindBindingAsync(string ws, string q, string hash, CancellationToken ct) => Task.FromResult(Items.SingleOrDefault(x => x.BindingHash == hash && x.WorkspaceId == ws && x.QuoteId == q));
        public Task<int> NextVersionAsync(string ws, string q, CancellationToken ct) => Task.FromResult(Items.Count + 1);
        public Task<IReadOnlyList<QuoteDocument>> ListAsync(string ws, string q, int offset, int limit, CancellationToken ct) => Task.FromResult<IReadOnlyList<QuoteDocument>>(Items.Skip(offset).Take(limit).ToArray());
        public Task<byte[]?> ReadBytesAsync(string ws, string id, CancellationToken ct) => Task.FromResult<byte[]?>(null);
        public void Add(QuoteDocument document, byte[] bytes) => Items.Add(document);
    }
    private sealed class Seller : IQuoteSellerReader, IQuoteSellerLease
    {
        internal bool Missing, Disposed;
        public QuoteSellerSnapshot? Snapshot => Missing ? null : new("workspace", 3, "Synthetic", "Synthetic Company", "Synthetic address", null, "seller@example.test", null, null, "UTC");
        public string? ErrorCode => null; public IReadOnlyList<string> Blockers => Missing ? ["SELLER_LEGAL_NAME_REQUIRED"] : [];
        public Task<IQuoteSellerLease> AcquireAsync(string request, string correlation, CancellationToken ct) { Disposed = false; return Task.FromResult<IQuoteSellerLease>(this); }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
    private sealed class Buyer : IQuoteDocumentPartyReader, IQuoteDocumentPartyLease
    {
        internal bool Disposed; internal string? LastId;
        public string BuyerType => "CONTACT";
        public QuoteDocumentPartySnapshot? Snapshot => new("CONTACT", LastId!, 2, "Synthetic buyer", null, null, true);
        public Task<IQuoteDocumentPartyLease> AcquireAsync(string id, string request, string correlation, CancellationToken ct) { Disposed = false; LastId = id; return Task.FromResult<IQuoteDocumentPartyLease>(this); }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
    private sealed class Renderer : IQuotePdfRenderer
    {
        internal string? Failure; internal int Calls; internal byte[] Bytes = Encoding.ASCII.GetBytes("%PDF-unit-orchestration-stub");
        public Task<QuotePdfRenderResult> RenderAsync(string snapshot, CancellationToken ct) { Calls++; if (Failure is not null) throw new QuotePdfException(Failure); return Task.FromResult(new QuotePdfRenderResult(Bytes, 1, 1, 1024)); }
    }
    private sealed class Policy : IQuoteDocumentPolicyReader { internal bool Enabled = true; public string? EnabledVersion(string ws) => Enabled ? "doc-policy-v1" : null; }
    private sealed class Approvals : IQuoteApprovalPolicyReader
    {
        internal QuoteApprovalPolicy Value = new("approval-v1", 10, 10, 30, true, false, new Dictionary<string, decimal> { ["USD"] = 5000 });
        public Task<QuoteApprovalPolicy?> ReadAsync(string ws, CancellationToken ct) => Task.FromResult<QuoteApprovalPolicy?>(Value);
    }
    private sealed class Workspace : IQuoteWorkspacePolicyReader { public Task<QuoteWorkspacePolicy?> ReadAsync(string ws, CancellationToken ct) => Task.FromResult<QuoteWorkspacePolicy?>(new(true, ["USD"], 3)); }
    private sealed class Zone : IWorkspaceTimeZoneReader { public Task<string?> ReadTimeZoneAsync(string ws, CancellationToken ct) => Task.FromResult<string?>("UTC"); }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Access : IRecordAccessEvaluator
    {
        internal bool DenyIssue; internal string? Hidden;
        public Task<RecordAccessAuthorization> AuthorizeResourceAsync(string resource, string capability, IReadOnlyList<string>? fields, RecordAccessRepresentation representation, RecordAccessRequestContext context, CancellationToken ct) =>
            Task.FromResult(new RecordAccessAuthorization(!(DenyIssue && capability == "quotes.issueDocument"), "AUTHORIZED", new("workspace", "account", "issuer", "membership"), RecordAccessScopeFilter.Workspace, null, "WORKSPACE",
                fields!.ToDictionary(f => f, f => f == Hidden ? RecordFieldEnforcement.Withheld : RecordFieldEnforcement.ReadWrite), [], ["quotes.read", "quotes.issueDocument"], "policy", resource, capability, true));
        public Task<RecordAccessRecordDecision> AuthorizeRecordAsync(RecordAccessAuthorization access, string id, RecordAccessFacts facts, string point, RecordAccessRequestContext context, CancellationToken ct) => Task.FromResult(new RecordAccessRecordDecision(true, "WORKSPACE", null));
    }
}
