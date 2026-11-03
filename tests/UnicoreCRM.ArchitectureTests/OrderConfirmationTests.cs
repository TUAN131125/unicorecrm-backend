using System.Reflection;
using System.Text.Json;
using UnicoreCRM.Billing.Payments.Domain;
using UnicoreCRM.Billing.Payments.Contracts;
using UnicoreCRM.Sales.Orders.Domain;
using UnicoreCRM.Sales.Orders.Application.Common;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Workflows.Atomic.Contracts;
using Xunit;
namespace UnicoreCRM.ArchitectureTests;

public sealed class OrderConfirmationTests
{
    [Fact]
    public void Payment_plan_activation_advances_once_without_collection_evidence()
    {
        var now=DateTimeOffset.Parse("2026-11-01T00:00:00Z");
        var plan=PaymentPlan.Draft("ws_test","order_test","CONTACT","contact_test","{}","FULL_PAYMENT","VND",["schedule_test"],now);
        Assert.Equal("DRAFT",plan.State);Assert.Equal(1,plan.ResourceVersion);
        plan.Activate(now.AddMinutes(1));
        Assert.Equal("ACTIVE",plan.State);Assert.Equal(2,plan.ResourceVersion);Assert.Equal(now.AddMinutes(1),plan.ActivatedAt);
        Assert.Equal(0,plan.EvidenceCount);
        Assert.Throws<InvalidOperationException>(()=>plan.Activate(now.AddMinutes(2)));
        Assert.Equal(2,plan.ResourceVersion);
    }
    [Fact]
    public void Schedule_activation_resolves_due_date_without_satisfying_amount()
    {
        var now=DateTimeOffset.Parse("2026-11-01T00:00:00Z");
        var plan=PaymentPlan.Draft("ws_test","order_test","CONTACT","contact_test","{}","FULL_PAYMENT","VND",["schedule_test"],now);
        var source=new PaymentAgreementLineSnapshotDocument("agreement_line",1,"Full","FULL",new("REMAINDER"),new("1000","VND"),
            new("EVENT_RELATIVE"){Event="ORDER_CONFIRMED",OffsetDays=0,DayBasis="CALENDAR"},["BANK_TRANSFER"],"NONE");
        var line=PaymentScheduleLine.Draft(plan,"schedule_test",source,now);
        line.Activate(2,new DateOnly(2026,11,1),now);
        Assert.Equal("DUE",line.State);Assert.Equal(2,line.PaymentPlanVersion);Assert.Equal(0,line.SatisfiedAmount);Assert.Equal(1000,line.OutstandingAmount);
    }
    [Theory]
    [InlineData("confirmedBy")]
    [InlineData("grandTotal")]
    [InlineData("buyerRef")]
    [InlineData("paymentEvidence")]
    public void Confirmation_request_rejects_client_authority_overrides(string field)
    {
        var json="{\"paymentPlanId\":\"plan_test\",\"expectedPaymentPlanVersion\":1,\""+field+"\":\"forged\"}";
        Assert.Throws<JsonException>(()=>JsonSerializer.Deserialize<ConfirmOrderWithPaymentPlanRequest>(json,new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }
    [Fact]
    public async Task Read_only_record_scope_is_readable_but_cannot_confirm()
    {
        var authorization=new OrderAuthorization(new Evaluator());var metadata=new OrderRequestMetadata("request_test","correlation_test");
        var access=await authorization.AuthorizeAsync(metadata,default,"orders.confirm");Assert.True(access.IsSuccess);
        var order=(Order)Activator.CreateInstance(typeof(Order),true)!;
        typeof(Order).GetProperty("OrderId",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(order,"order_test");
        Assert.Null(await authorization.EnforceRecordAsync(access.Value!,order,"getOrder",metadata,default));
        var denied=await authorization.EnforceRecordAsync(access.Value!,order,"confirmOrderWithPaymentPlan",metadata,default,requireMutation:true);
        Assert.Equal("ACCESS_DENIED",denied!.Code);Assert.Equal(403,denied.Status);
    }
    [Fact]
    public void Automatic_confirmation_retains_system_origin_without_fabricating_human_review()
    {
        var order=(Order)Activator.CreateInstance(typeof(Order),true)!;
        typeof(Order).GetProperty("State",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(order,"DRAFT");
        typeof(Order).GetProperty("ResourceVersion",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(order,1L);
        var now=DateTimeOffset.Parse("2026-11-02T00:00:00Z");
        Assert.Throws<InvalidOperationException>(()=>order.Confirm("employee_forged","",null,now,"WORKSPACE_POLICY","sales-v1"));
        Assert.Equal("DRAFT",order.State);
        order.Confirm("system:quote-policy","",null,now,"WORKSPACE_POLICY","sales-v1");
        Assert.Equal("CONFIRMED",order.State);Assert.Equal("system:quote-policy",order.ConfirmedBy);
        Assert.Equal("WORKSPACE_POLICY",order.ConfirmationOrigin);Assert.Equal("sales-v1",order.ConfirmationPolicyVersion);
        Assert.Equal("",order.OperationalReviewNotes);Assert.Equal(2,order.ResourceVersion);
        Assert.Throws<InvalidOperationException>(()=>order.Confirm("system:quote-policy","",null,now,"WORKSPACE_POLICY","sales-v1"));
    }
    private sealed class Evaluator:IRecordAccessEvaluator
    {
        public Task<RecordAccessAuthorization> AuthorizeResourceAsync(string resource,string capability,IReadOnlyList<string>? fields,
            RecordAccessRepresentation representation,RecordAccessRequestContext context,CancellationToken ct)
        {
            Assert.Equal("orders.confirm",capability);
            return Task.FromResult(new RecordAccessAuthorization(true,"",new TrustedWorkspaceContext("ws_test","account_test","member_test","membership_test"),
                RecordAccessScopeFilter.Workspace,null,"WORKSPACE",fields!.ToDictionary(x=>x,_=>RecordFieldEnforcement.ReadWrite),[],[capability],"test",resource,capability,true));
        }
        public Task<RecordAccessRecordDecision> AuthorizeRecordAsync(RecordAccessAuthorization access,string id,RecordAccessFacts facts,string point,RecordAccessRequestContext context,CancellationToken ct)
            =>Task.FromResult(new RecordAccessRecordDecision(true,"WORKSPACE",true){ReadOnlyScope=true});
    }
}
