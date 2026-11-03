using System.Reflection;
using UnicoreCRM.Crm.Contacts.Application.Common;
using UnicoreCRM.Crm.Contacts.Domain;
using UnicoreCRM.Crm.Organizations.Application.Common;
using UnicoreCRM.Crm.Organizations.Domain;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;
using Xunit;
using Microsoft.Extensions.Logging.Abstractions;
using UnicoreCRM.Crm.Customers.Application.Health;
using UnicoreCRM.Crm.Customers.Application.Common;
using UnicoreCRM.Crm.Customers.Domain;
using UnicoreCRM.Crm.Contacts.Contracts;
using UnicoreCRM.Crm.Organizations.Contracts;
using UnicoreCRM.CommercialEvidence.CommercialEvidence.Contracts;

namespace UnicoreCRM.ArchitectureTests;

public sealed class CustomerListIdentityTests
{
    private static readonly TrustedWorkspaceContext Trusted = new("workspace-demo", "account", "member", "membership");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Owners_batch_only_requested_workspace_ids_and_keep_full_name_authority(bool organization)
    {
        var fixture = Create(organization);
        var names = await fixture.Read([fixture.Id, fixture.Id], CancellationToken.None);
        Assert.Equal("Authorized name", names[fixture.Id]);
        Assert.Equal(1, fixture.Spy.Reads);
        Assert.Equal(1, fixture.Authority.ResourceChecks);
        Assert.Equal(1, fixture.Authority.RecordChecks);
        Assert.DoesNotContain("Private alias", names.Values);
        Assert.Equal(Trusted.WorkspaceId, fixture.Spy.Workspace);
        Assert.Equal(1, fixture.Spy.Saves);
    }

    [Theory]
    [InlineData(false, "HIDDEN")]
    [InlineData(false, "MASKED")]
    [InlineData(true, "HIDDEN")]
    [InlineData(true, "MASKED")]
    public async Task Withheld_source_names_do_not_leave_owner(bool organization, string policy)
    {
        var fixture = Create(organization);
        fixture.Authority.WithholdName = policy is "HIDDEN" or "MASKED";
        Assert.Empty(await fixture.Read([fixture.Id], CancellationToken.None));
        Assert.Equal(0, fixture.Spy.Reads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Capability_workspace_and_record_denials_are_neutral(bool organization)
    {
        var fixture = Create(organization);
        fixture.Authority.Allowed = false;
        Assert.Empty(await fixture.Read([fixture.Id], CancellationToken.None));
        Assert.Equal(0, fixture.Spy.Reads);
        fixture.Authority.Allowed = true;
        fixture.Authority.Workspace = "another-workspace";
        Assert.Empty(await fixture.Read([fixture.Id], CancellationToken.None));
        Assert.Equal(0, fixture.Spy.Reads);
        fixture.Authority.Workspace = Trusted.WorkspaceId;
        fixture.Authority.RecordAllowed = false;
        Assert.Empty(await fixture.Read([fixture.Id], CancellationToken.None));
        Assert.Equal(0, fixture.Spy.Saves);
        fixture.Authority.RecordAllowed = true;
        Assert.Empty(await fixture.Read(["foreign-or-missing-id"], CancellationToken.None));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Read_only_source_name_remains_readable(bool organization)
    {
        var fixture = Create(organization); fixture.Authority.ReadOnlyName = true;
        Assert.Equal("Authorized name", (await fixture.Read([fixture.Id], CancellationToken.None))[fixture.Id]);
    }

    [Fact]
    public async Task List_handler_resolves_only_final_mixed_page_and_retains_rows_and_cursor_on_identity_denial()
    {
        var a = new Customer(Trusted.WorkspaceId, "member", "CONTACT", "contact-a", new(), DateTimeOffset.UnixEpoch);
        var b = new Customer(Trusted.WorkspaceId, "member", "ORGANIZATION_ACCOUNT", "organization-b", new(), DateTimeOffset.UnixEpoch);
        var extra = new Customer(Trusted.WorkspaceId, "member", "CONTACT", "extra-not-on-page", new(), DateTimeOffset.UnixEpoch);
        var persistence = DispatchProxy.Create<ICustomersPersistence, PagePersistenceSpy>();
        var spy = (PagePersistenceSpy)(object)persistence; spy.Candidates = [a, b, extra];
        var subjects = new SubjectBatches(); var signals = new EmptySignals();
        var health = new CustomerHealthAssessmentService(signals, signals, TimeProvider.System, NullLogger<CustomerHealthAssessmentService>.Instance);
        var handler = new UnicoreCRM.Crm.Customers.Application.ListCustomers.Handler(new CustomerAuthorization(new Authority()), persistence,
            health, TimeProvider.System, subjects, subjects);
        var query = new UnicoreCRM.Crm.Customers.Application.ListCustomers.Query(new("request-test", "correlation-test"), null, null, null, null, null, null, null, 2);
        var first = await handler.HandleAsync(query, CancellationToken.None);
        Assert.True(first.IsSuccess); Assert.Equal(3, spy.Take);
        Assert.Equal(new[] { a.CustomerId, b.CustomerId }, first.Value!.Items.Select(item => item.Id));
        Assert.Equal(new[] { "contact-a" }, subjects.ContactIds);
        Assert.Equal(new[] { "organization-b" }, subjects.OrganizationIds);
        Assert.True(first.Value.PageInfo.HasNextPage);
        Assert.Equal("Contact name", first.Value.Identities[a.CustomerId].DisplayName);
        Assert.Equal("Organization name", first.Value.Identities[b.CustomerId].DisplayName);
        Assert.DoesNotContain(extra.CustomerId, first.Value.Identities.Keys);
        subjects.Denied = true;
        var denied = await handler.HandleAsync(query, CancellationToken.None);
        Assert.True(denied.IsSuccess); Assert.Empty(denied.Value!.Identities);
        Assert.Equal(first.Value.Items.Select(item => item.Id), denied.Value.Items.Select(item => item.Id));
        Assert.Equal(first.Value.PageInfo, denied.Value.PageInfo);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(1000)]
    [InlineData(1001)]
    public async Task Search_delegates_one_bounded_filtered_query_without_scanning_unrelated_customers(int count)
    {
        var persistence = DispatchProxy.Create<ICustomersPersistence, PagePersistenceSpy>();
        var spy = (PagePersistenceSpy)(object)persistence;
        spy.Consume = true;
        spy.Candidates = Enumerable.Range(0, count).Select(i => new Customer(Trusted.WorkspaceId, "member", "CONTACT",
            $"contact-{i}", new(), DateTimeOffset.UnixEpoch.AddSeconds(i))).ToArray();
        var subjects = new SubjectBatches(); var signals = new EmptySignals();
        var health = new CustomerHealthAssessmentService(signals, signals, TimeProvider.System, NullLogger<CustomerHealthAssessmentService>.Instance);
        var handler = new UnicoreCRM.Crm.Customers.Application.ListCustomers.Handler(new CustomerAuthorization(new Authority()), persistence,
            health, TimeProvider.System, subjects, subjects);
        var query = new UnicoreCRM.Crm.Customers.Application.ListCustomers.Query(new("request-bounds", "correlation-bounds"),
            "never-match-probe", null, null, null, null, null, null, 25);
        var result = await handler.HandleAsync(query, CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
        Assert.Equal(1, spy.SearchCalls);
        Assert.Equal(1, subjects.ContactCalls);
        Assert.Equal(1, subjects.OrganizationCalls);
        Assert.Equal(26, spy.Take);
    }

    public class PagePersistenceSpy : DispatchProxy
    {
        internal IReadOnlyList<Customer> Candidates = [];
        internal int Take;
        internal bool Consume;
        internal int SearchCalls;
        private int offset;
        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            if (method?.Name == "SearchCustomersAsync")
            {
                SearchCalls++;
                Take = ((CustomerSearchPage)arguments![0]!).Take;
                return Task.FromResult<IReadOnlyList<Customer>>([]);
            }
            if (method?.Name == "ListCustomersAsync")
            {
                Take = (int)arguments![10]!;
                if (!Consume) return Task.FromResult(Candidates);
                IReadOnlyList<Customer> page = Candidates.Skip(offset).Take(Take).ToArray();
                offset += page.Count;
                return Task.FromResult(page);
            }
            if (method?.Name == "AddReadAudit") return null;
            if (method?.Name == "SaveChangesAsync") return Task.CompletedTask;
            throw new InvalidOperationException($"Unexpected Customer persistence call {method?.Name}");
        }
    }

    private sealed class SubjectBatches : IContactCustomerListIdentityParticipant, IOrganizationCustomerListIdentityParticipant
    {
        internal bool Denied;
        internal int ContactCalls, OrganizationCalls;
        internal IReadOnlyCollection<string> ContactIds = [], OrganizationIds = [];
        Task<IReadOnlyDictionary<string, string>> IContactCustomerListIdentityParticipant.ResolveVisibleAsync(TrustedWorkspaceContext trusted,
            IReadOnlyCollection<string> ids, string request, string correlation, CancellationToken ct)
        { ContactCalls++; ContactIds = ids; return Task.FromResult<IReadOnlyDictionary<string, string>>(Denied ? new Dictionary<string, string>() : ids.ToDictionary(id => id, _ => "Contact name")); }
        Task<IReadOnlyDictionary<string, string>> IOrganizationCustomerListIdentityParticipant.ResolveVisibleAsync(TrustedWorkspaceContext trusted,
            IReadOnlyCollection<string> ids, string request, string correlation, CancellationToken ct)
        { OrganizationCalls++; OrganizationIds = ids; return Task.FromResult<IReadOnlyDictionary<string, string>>(Denied ? new Dictionary<string, string>() : ids.ToDictionary(id => id, _ => "Organization name")); }
    }

    private sealed class EmptySignals : ICustomerPurchaseHealthSignalReader, ISystemCustomerPurchaseHealthSignalReader
    {
        public Task<CustomerPurchaseHealthSignalSnapshot?> ReadAsync(TrustedWorkspaceContext trusted, CustomerPurchaseHealthBuyerRef buyer,
            DateTimeOffset asOf, CancellationToken ct) => Task.FromResult<CustomerPurchaseHealthSignalSnapshot?>(null);
        public Task<IReadOnlyList<CustomerPurchaseHealthSignalSnapshot>> ReadBatchAsync(TrustedWorkspaceContext trusted,
            IReadOnlyCollection<CustomerPurchaseHealthBuyerRef> buyers, DateTimeOffset asOf, CancellationToken ct) => Task.FromResult<IReadOnlyList<CustomerPurchaseHealthSignalSnapshot>>([]);
        public Task<IReadOnlyList<CustomerPurchaseHealthSignalSnapshot>> ReadBatchAsync(string workspace,
            IReadOnlyCollection<CustomerPurchaseHealthBuyerRef> buyers, DateTimeOffset asOf, CancellationToken ct) => Task.FromResult<IReadOnlyList<CustomerPurchaseHealthSignalSnapshot>>([]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Bounds_cancellation_and_failures_are_preserved(bool organization)
    {
        var fixture = Create(organization);
        Assert.Empty(await fixture.Read([], CancellationToken.None));
        Assert.Equal(0, fixture.Authority.ResourceChecks);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Read(Enumerable.Range(0, 251).Select(i => $"id-{i}").ToArray(), CancellationToken.None));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Read([fixture.Id], cancellation.Token));
        fixture.Spy.Fail = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Read([fixture.Id], CancellationToken.None));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Identity_search_uses_only_authorized_fields_and_records(bool organization)
    {
        var f=Create(organization);
        Assert.Contains(f.Id,await f.Search("authorized",default));
        Assert.Empty(await f.Search("not-present-3388",default));
        f.Authority.WithholdName=true;
        Assert.Empty(await f.Search("Authorized",default));
        f.Authority.WithholdName=false;f.Authority.RecordAllowed=false;
        Assert.Empty(await f.Search("Authorized",default));
        f.Authority.RecordAllowed=true;f.Authority.Allowed=false;
        Assert.Empty(await f.Search("Authorized",default));
    }

    private sealed record Fixture(string Id, PersistenceSpy Spy, Authority Authority,
        Func<IReadOnlyCollection<string>, CancellationToken, Task<IReadOnlyDictionary<string, string>>> Read,
        Func<string, CancellationToken, Task<IReadOnlySet<string>>> Search);

    private static Fixture Create(bool organization)
    {
        var authority = new Authority();
        if (organization)
        {
            var record = new Organization(Trusted.WorkspaceId, "member", "Authorized name", "active", new OrganizationProfile(), DateTimeOffset.UnixEpoch);
            var persistence = DispatchProxy.Create<IOrganizationsPersistence, PersistenceSpy>();
            var spy = (PersistenceSpy)(object)persistence; spy.Record = record; spy.Id = record.OrganizationId;
            var participant = new UnicoreCRM.Crm.Organizations.Application.ResolveCustomerSubject.Participant(new OrganizationAuthorization(authority), persistence, TimeProvider.System);
            return new(spy.Id, spy, authority, (ids, ct) => participant.ResolveVisibleAsync(Trusted, ids, "request-test", "correlation-test", ct),
                (q, ct) => participant.MatchVisibleAsync(Trusted, [spy.Id], q, "request-test", "correlation-test", ct));
        }
        else
        {
            var record = new Contact(Trusted.WorkspaceId, "member", "Authorized name", "active", new ContactProfile { DisplayName = "Private alias" }, DateTimeOffset.UnixEpoch);
            var persistence = DispatchProxy.Create<IContactsPersistence, PersistenceSpy>();
            var spy = (PersistenceSpy)(object)persistence; spy.Record = record; spy.Id = record.ContactId;
            var participant = new UnicoreCRM.Crm.Contacts.Application.CustomerParticipants.SubjectParticipant(new ContactAuthorization(authority), persistence, TimeProvider.System);
            return new(spy.Id, spy, authority, (ids, ct) => participant.ResolveVisibleAsync(Trusted, ids, "request-test", "correlation-test", ct),
                (q, ct) => participant.MatchVisibleAsync(Trusted, [spy.Id], q, "request-test", "correlation-test", ct));
        }
    }

    public class PersistenceSpy : DispatchProxy
    {
        internal object? Record;
        internal string Id = "";
        internal string? Workspace;
        internal int Reads, Saves;
        internal bool Fail;
        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            var args = arguments ?? [];
            if (method?.Name is "ReadContactsByIdsAsync" or "ReadOrganizationsAsync")
            {
                Reads++; Workspace = (string)args[0]!;
                if (Fail) throw new InvalidOperationException("Test persistence failure");
                var contains = ((IReadOnlyCollection<string>)args[1]!).Contains(Id);
                if (Record is Contact contact) return Task.FromResult<IReadOnlyList<Contact>>(contains && contact.WorkspaceId == Workspace ? [contact] : []);
                if (Record is Organization org) return Task.FromResult<IReadOnlyList<Organization>>(contains && org.WorkspaceId == Workspace ? [org] : []);
            }
            if (method?.Name == "AddReadAudit") return null;
            if (method?.Name == "SaveChangesAsync") { Saves++; return Task.CompletedTask; }
            throw new InvalidOperationException($"Unexpected persistence call {method?.Name}; per-record or unbounded reads are forbidden in this test.");
        }
    }

    private sealed class Authority : IRecordAccessEvaluator
    {
        internal bool Allowed = true, RecordAllowed = true, WithholdName, ReadOnlyName;
        internal string Workspace = Trusted.WorkspaceId;
        internal int ResourceChecks, RecordChecks;
        public Task<RecordAccessAuthorization> AuthorizeResourceAsync(string resourceKey, string capability,
            IReadOnlyList<string>? fields, RecordAccessRepresentation representation, RecordAccessRequestContext context, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); ResourceChecks++;
            var enforcement = (fields ?? []).ToDictionary(field => field,
                field => WithholdName && field == (resourceKey == "contacts" ? "fullName" : "displayName") ? RecordFieldEnforcement.Withheld
                    : ReadOnlyName && field == (resourceKey == "contacts" ? "fullName" : "displayName") ? RecordFieldEnforcement.ReadOnly : RecordFieldEnforcement.ReadWrite,
                StringComparer.OrdinalIgnoreCase);
            return Task.FromResult(new RecordAccessAuthorization(Allowed, Allowed ? "ALLOW" : "ACCESS_DENIED", Trusted with { WorkspaceId = Workspace },
                RecordAccessScopeFilter.Workspace, null, "WORKSPACE", enforcement, [], [], "test", resourceKey, capability, Allowed));
        }
        public Task<RecordAccessRecordDecision> AuthorizeRecordAsync(RecordAccessAuthorization authorization, string id,
            RecordAccessFacts facts, string point, RecordAccessRequestContext context, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); RecordChecks++; return Task.FromResult(new RecordAccessRecordDecision(RecordAllowed, "OWN", RecordAllowed)); }
    }
}
