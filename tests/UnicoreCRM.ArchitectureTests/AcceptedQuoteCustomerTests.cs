using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using UnicoreCRM.Crm.Customers.Contracts;
using UnicoreCRM.Crm.Customers.Domain;
using UnicoreCRM.Crm.Customers.Infrastructure.Persistence;
using UnicoreCRM.Crm.Contacts.Infrastructure.Persistence;
using UnicoreCRM.Crm.Organizations.Infrastructure.Persistence;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Platform.Workspace.Infrastructure.Persistence;
using UnicoreCRM.Sales.Orders.Infrastructure.Persistence;
using UnicoreCRM.Crm.Customers.Application.Common;
using Participant=UnicoreCRM.Crm.Customers.Application.ResolveForAcceptedQuote.Participant;
using UnicoreCRM.Workflows.Atomic.Infrastructure.Persistence;
using UnicoreCRM.Workflows.Atomic.Domain;
using UnicoreCRM.Workflows.Atomic.Application.AcceptedQuoteCustomer;
using Xunit;

namespace UnicoreCRM.ArchitectureTests;

public sealed class AcceptedQuoteCustomerTests
{
    public sealed class CopyFact:FactAttribute
    { public CopyFact(){if(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ACCEPTED_CUSTOMER_TEST_ORDER")))Skip="Requires the approved isolated accepted-order fixture.";} }
    public sealed class PendingCopyFact:FactAttribute
    { public PendingCopyFact(){if(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ACCEPTED_CUSTOMER_PENDING_ORDER")))Skip="Requires a newly queued isolated fixture with the Customer worker paused.";} }
    private static string Workspace=>Environment.GetEnvironmentVariable("ACCEPTED_CUSTOMER_TEST_WORKSPACE")??throw new InvalidOperationException();
    private static string Order=>Environment.GetEnvironmentVariable("ACCEPTED_CUSTOMER_TEST_ORDER")??throw new InvalidOperationException();
    private sealed class Scope:IAsyncDisposable
    {
        private readonly List<DbContext> contexts=[];
        internal CustomersDbContext Customers {get;}
        internal EfCustomersPersistence Persistence {get;}
        internal OrdersDbContext Orders {get;}
        internal Participant Participant {get;}
        internal Scope(bool denyOwner=false)
        {
            var connection=Environment.GetEnvironmentVariable("ConnectionStrings__UnicoreCRM")??throw new InvalidOperationException();
            var name=new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connection).InitialCatalog;
            if(!System.Text.RegularExpressions.Regex.IsMatch(name,"^UnicoreCRM_QuoteGmailTest_[a-f0-9]{32}$"))throw new InvalidOperationException("Test database required");
            Customers=new(Options<CustomersDbContext>(connection));Persistence=new(Customers);
            Orders=new(Options<OrdersDbContext>(connection));
            var contacts=new ContactsDbContext(Options<ContactsDbContext>(connection));
            var organizations=new OrganizationsDbContext(Options<OrganizationsDbContext>(connection));
            var members=new WorkspaceDbContext(Options<WorkspaceDbContext>(connection));
            contexts.AddRange([Customers,Orders,contacts,organizations,members]);
            Participant=new(new ConfirmedQuoteCustomerSourceReader(Orders),
                new Crm.Contacts.Application.CustomerParticipants.AcceptedQuoteBuyer(new EfContactsPersistence(contacts)),
                new Crm.Organizations.Application.ResolveCustomerSubject.AcceptedQuoteBuyer(new EfOrganizationsPersistence(organizations)),
                denyOwner?new DeniedMembers():new EfWorkspaceMemberReferenceValidator(members),Persistence,TimeProvider.System);
        }
        public async ValueTask DisposeAsync(){foreach(var db in contexts)await db.DisposeAsync();}
    }
    private sealed class DeniedMembers:IWorkspaceMemberReferenceValidator
    { public Task<bool> IsActiveMemberAsync(string ws,string member,CancellationToken ct)=>Task.FromResult(false); }
    private static DbContextOptions<T> Options<T>(string connection) where T:DbContext=>new DbContextOptionsBuilder<T>().UseSqlServer(connection).Options;

    [CopyFact]
    public async Task Real_sql_replay_and_concurrent_requests_reuse_customer_without_profile_overwrite()
    {
        await using var scope=new Scope();
        var source=await new ConfirmedQuoteCustomerSourceReader(scope.Orders).ReadAsync(Workspace,Order,default);Assert.NotNull(source);
        var first=await scope.Participant.ResolveAsync(Workspace,Order,default);Assert.True(first.Succeeded,first.ErrorCode);
        var customer=await scope.Customers.Customers.AsNoTracking().SingleAsync(x=>x.WorkspaceId==Workspace&&x.CustomerId==first.CustomerId);
        var before=JsonSerializer.Serialize(customer.Profile);var version=customer.Version;
        var results=await Task.WhenAll(Enumerable.Range(0,6).Select(async _=>{await using var replay=new Scope();return await replay.Participant.ResolveAsync(Workspace,Order,default);}));
        Assert.All(results,r=>{Assert.True(r.Succeeded,r.ErrorCode);Assert.Equal(first.CustomerId,r.CustomerId);});
        var after=await scope.Customers.Customers.AsNoTracking().SingleAsync(x=>x.CustomerId==first.CustomerId&&x.WorkspaceId==Workspace);
        Assert.Equal(before,JsonSerializer.Serialize(after.Profile));Assert.Equal(version,after.Version);
        Assert.Equal(1,await scope.Customers.Customers.CountAsync(x=>x.WorkspaceId==Workspace&&x.RelationshipType==source.BuyerType&&x.RelationshipId==source.BuyerId));
    }
    [CopyFact]
    public async Task Revoked_owner_cannot_create_or_reuse_even_after_prior_success()
    { await using var scope=new Scope(true);var result=await scope.Participant.ResolveAsync(Workspace,Order,default);Assert.False(result.Succeeded);Assert.Equal("CUSTOMER_OWNER_AUTHORITY_UNAVAILABLE",result.ErrorCode); }
    [CopyFact]
    public async Task Foreign_workspace_cannot_resolve_source_or_create_customer()
    { await using var scope=new Scope();var foreign=Environment.GetEnvironmentVariable("ACCEPTED_CUSTOMER_TEST_FOREIGN_WORKSPACE")??throw new InvalidOperationException();var result=await scope.Participant.ResolveAsync(foreign,Order,default);Assert.False(result.Succeeded);Assert.Equal("CONFIRMED_ACCEPTANCE_SOURCE_REQUIRED",result.ErrorCode); }
    private sealed class LostAcknowledgment(IAcceptedQuoteCustomerParticipant inner):IAcceptedQuoteCustomerParticipant
    {
        public AcceptedQuoteCustomerResult? Committed {get;private set;}
        public async Task<AcceptedQuoteCustomerResult> ResolveAsync(string ws,string order,CancellationToken ct)
        { Committed=await inner.ResolveAsync(ws,order,ct);Assert.True(Committed.Succeeded,Committed.ErrorCode);throw new IOException("Injected lost participant acknowledgment after SQL commit"); }
    }
    [PendingCopyFact]
    public async Task Lost_participant_ack_keeps_order_confirmed_and_task_pending_for_real_restart_recovery()
    {
        var pendingOrder=Environment.GetEnvironmentVariable("ACCEPTED_CUSTOMER_PENDING_ORDER");
        Assert.False(string.IsNullOrEmpty(pendingOrder));
        await using var scope=new Scope();
        var connection=Environment.GetEnvironmentVariable("ConnectionStrings__UnicoreCRM")!;
        await using var db=new WorkflowsDbContext(Options<WorkflowsDbContext>(connection));
        var task=await db.Set<AcceptedQuoteCustomerTask>().AsNoTracking().SingleAsync(x=>x.WorkspaceId==Workspace&&x.OrderId==pendingOrder);
        Assert.Equal("PENDING",task.State);
        var fault=new LostAcknowledgment(scope.Participant);
        await new RecoveryRunner(db,fault,TimeProvider.System).RunAsync(default);
        var after=await db.Set<AcceptedQuoteCustomerTask>().AsNoTracking().SingleAsync(x=>x.WorkspaceId==Workspace&&x.OrderId==pendingOrder);
        Assert.Equal("PENDING",after.State);Assert.Equal("CUSTOMER_RECOVERY_TRANSIENT_FAILURE",after.LastErrorCode);Assert.NotNull(fault.Committed?.CustomerId);
        Assert.Equal("CONFIRMED",(await scope.Orders.Orders.AsNoTracking().SingleAsync(x=>x.WorkspaceId==Workspace&&x.OrderId==pendingOrder)).State);
    }
    [CopyFact]
    public async Task Sql_unique_relationship_rejects_duplicate_owner_insert()
    {
        await using var scope=new Scope();var result=await scope.Participant.ResolveAsync(Workspace,Order,default);Assert.True(result.Succeeded);
        var source=await new ConfirmedQuoteCustomerSourceReader(scope.Orders).ReadAsync(Workspace,Order,default);Assert.NotNull(source);
        await using var tx=await scope.Persistence.BeginSerializableAsync(default);
        scope.Persistence.AddCustomer(new Customer(Workspace,source.OwnerId!,source.BuyerType,source.BuyerId,new(),DateTimeOffset.UtcNow));
        await Assert.ThrowsAsync<CustomerBusinessKeyConflictException>(()=>scope.Persistence.SaveChangesAsync(default));
    }
}
