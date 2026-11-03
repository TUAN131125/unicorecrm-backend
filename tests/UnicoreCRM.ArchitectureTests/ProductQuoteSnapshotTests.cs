using System.Reflection;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Sales.Products.Application.Common;
using UnicoreCRM.Sales.Products.Contracts;
using UnicoreCRM.Sales.Products.Domain;
using Handler = UnicoreCRM.Sales.Products.Application.ReadQuoteProductSnapshots.Handler;
using Xunit;

namespace UnicoreCRM.ArchitectureTests;

public sealed class ProductQuoteSnapshotTests
{
    private static readonly TrustedWorkspaceContext Trusted = new("workspace-test", "account-test", "member-test", "membership-test");

    [Fact]
    public async Task Captures_authoritative_commercial_values_in_one_workspace_batch()
    {
        var (handler, persistence, _) = Create();
        var product = Product();
        persistence.Products = [product];
        var result = await handler.ReadAsync([product.ProductId, product.ProductId, "missing"], "req", "corr", default);
        Assert.True(result.IsAuthorized);
        var snapshot = Assert.Single(result.Items).Value;
        Assert.Equal(new QuoteProductSnapshot(product.ProductId, "SKU", "Authoritative", "SERVICE", "Description",
            "MONTHLY", new ProductMoney("123.45", "USD"), "10", "EXCLUSIVE", 0), snapshot);
        Assert.Equal(1, persistence.Reads);
        Assert.Equal(2, persistence.LastIds!.Count);
        Assert.Equal(Trusted.WorkspaceId, persistence.LastWorkspace);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("sku")]
    [InlineData("name")]
    [InlineData("type")]
    [InlineData("description")]
    [InlineData("billingCycle")]
    [InlineData("unitPrice")]
    [InlineData("taxRate")]
    [InlineData("taxMode")]
    [InlineData("version")]
    public async Task Every_captured_field_must_be_readable(string hidden)
    {
        var (handler, persistence, access) = Create();
        access.Hidden = hidden;
        var result = await handler.ReadAsync(["product"], "req", "corr", default);
        Assert.False(result.IsAuthorized);
        Assert.Empty(result.Items);
        Assert.Equal(0, persistence.Reads);
    }

    [Fact]
    public async Task Missing_capability_or_trusted_workspace_returns_no_facts()
    {
        var (handler, persistence, access) = Create();
        access.Allowed = false;
        Assert.False((await handler.ReadAsync(["product"], "req", "corr", default)).IsAuthorized);
        access.Allowed = true;
        access.Trusted = null;
        Assert.False((await handler.ReadAsync(["product"], "req", "corr", default)).IsAuthorized);
        Assert.Equal(0, persistence.Reads);
    }

    [Fact]
    public async Task Inactive_archived_and_record_denied_products_are_not_capturable()
    {
        var (handler, persistence, access) = Create();
        var archived = Product();
        archived.Archive("test", DateTimeOffset.UtcNow);
        persistence.Products = [Product("INACTIVE"), archived];
        Assert.Empty((await handler.ReadAsync(["product"], "req", "corr", default)).Items);
        persistence.Products = [Product()];
        access.RecordAllowed = false;
        Assert.Empty((await handler.ReadAsync(["product"], "req", "corr", default)).Items);
        access.Scope = RecordAccessScopeFilter.OwnedByMember;
        var reads = persistence.Reads;
        Assert.Empty((await handler.ReadAsync(["product"], "req", "corr", default)).Items);
        Assert.Equal(reads, persistence.Reads);
    }

    [Fact]
    public async Task Batch_limit_and_cancellation_fail_before_persistence()
    {
        var (handler, persistence, _) = Create();
        await Assert.ThrowsAsync<ArgumentException>(() => handler.ReadAsync(Enumerable.Range(0, 501).Select(i => $"product-{i}").ToArray(), "req", "corr", default));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handler.ReadAsync(["product"], "req", "corr", new CancellationToken(true)));
        Assert.Equal(0, persistence.Reads);
    }

    private static Product Product(string status = "ACTIVE") => new(Trusted.WorkspaceId,
        new ProductProfile("SKU", "SKU", "Authoritative", "SERVICE", status, "CAT", "Description", "UNIT",
            new ProductMoneyValue("123.45", "USD"), null, "10", "EXCLUSIVE", "MONTHLY", true, true, null, null, []), DateTimeOffset.UtcNow);

    private static (Handler, PersistenceSpy, AccessEvaluator) Create()
    {
        var persistence = DispatchProxy.Create<IProductsPersistence, PersistenceSpy>();
        var spy = (PersistenceSpy)persistence;
        var access = new AccessEvaluator();
        return (new Handler(access, persistence), spy, access);
    }

    public class PersistenceSpy : DispatchProxy
    {
        internal IReadOnlyList<Product> Products = [];
        internal int Reads;
        internal string? LastWorkspace;
        internal IReadOnlyCollection<string>? LastIds;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            Assert.Equal("ReadProductSnapshotsAsync", method?.Name);
            Reads++;
            LastWorkspace = (string)args![0]!;
            LastIds = (IReadOnlyCollection<string>)args[1]!;
            return Task.FromResult(Products);
        }
    }

    private sealed class AccessEvaluator : IRecordAccessEvaluator
    {
        internal bool Allowed = true, RecordAllowed = true;
        internal string? Hidden;
        internal TrustedWorkspaceContext? Trusted = ProductQuoteSnapshotTests.Trusted;
        internal RecordAccessScopeFilter Scope = RecordAccessScopeFilter.Workspace;
        public Task<RecordAccessAuthorization> AuthorizeResourceAsync(string resource, string capability,
            IReadOnlyList<string>? fields, RecordAccessRepresentation representation, RecordAccessRequestContext context, CancellationToken ct)
        {
            Assert.Equal("products", resource);
            Assert.Equal("products.read", capability);
            Assert.Equal(new RecordAccessRequestContext("req", "corr"), context);
            var enforcement = fields!.ToDictionary(field => field, field => field == Hidden ? RecordFieldEnforcement.Withheld : RecordFieldEnforcement.ReadOnly);
            return Task.FromResult(new RecordAccessAuthorization(Allowed, "AUTHORIZED", Trusted, Scope, null, "WORKSPACE", enforcement,
                [], ["products.read"], "test", resource, capability, Allowed));
        }
        public Task<RecordAccessRecordDecision> AuthorizeRecordAsync(RecordAccessAuthorization access, string id,
            RecordAccessFacts facts, string point, RecordAccessRequestContext context, CancellationToken ct)
        {
            Assert.Null(facts.OwnerMemberId);
            return Task.FromResult(new RecordAccessRecordDecision(RecordAllowed, "WORKSPACE", null));
        }
    }
}
