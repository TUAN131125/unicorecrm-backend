using System.Text.Json;
using UnicoreCRM.BuildingBlocks;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Sales.Orders.Contracts;
using UnicoreCRM.Sales.Quotes.Contracts;
using UnicoreCRM.Workflows.Atomic.Application.ConfirmOrder;
using UnicoreCRM.Workflows.Atomic.Contracts;
using Xunit;

namespace UnicoreCRM.ArchitectureTests;

public sealed class OrderServiceClassificationTests
{
    [Theory]
    [InlineData("service")]
    [InlineData("SERVICE")]
    [InlineData("DIGITAL")]
    public async Task Canonical_service_and_existing_nonphysical_snapshots_do_not_require_shipping(string type)
    {
        var order=Order(type);
        await Validate(order);
        Assert.Equal(type,order.LineItems[0].ProductTypeSnapshot);
    }

    [Theory]
    [InlineData("physical_product")]
    [InlineData("PHYSICAL")]
    [InlineData("Service")]
    [InlineData("digital")]
    [InlineData("subscription")]
    [InlineData("license")]
    [InlineData("unknown")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Physical_unknown_and_unsupported_types_keep_fulfillment_guard(string? type)
    {
        var error=await Assert.ThrowsAsync<BusinessCommandException>(()=>Validate(Order(type)));
        Assert.Equal("FULFILLMENT_INFORMATION_REQUIRED",error.Code);
    }

    [Fact]
    public async Task Canonical_service_does_not_bypass_credit_or_operational_review()
    {
        var credit=Order("service") with {CreditPolicyEvaluation=new("APPROVAL_REQUIRED",["CREDIT_APPROVAL_REQUIRED"])};
        Assert.Equal("CREDIT_APPROVAL_REQUIRED",(await Assert.ThrowsAsync<BusinessCommandException>(()=>Validate(credit))).Code);
        Assert.Equal("OPERATIONAL_APPROVAL_REQUIRED",(await Assert.ThrowsAsync<BusinessCommandException>(()=>Validate(Order("service"),true))).Code);
    }

    private static Task Validate(OrderReadModel order,bool review=false)=>ConfirmationRules.ValidateAsync(order,new QuoteOrderConfirmationBinding("{}",review),
        new ConfirmOrderWithPaymentPlanRequest("plan_test",1),"ws_test",DateTimeOffset.Parse("2026-11-02T00:00:00Z"),new Zones(),default);
    private static OrderReadModel Order(string? type)
    {
        var money=new OrderMoney("100","VND");
        var line=new OrderLineReadModel("line_test",null,"Service", "1",money,"0","EXCLUSIVE",money,new("0","VND"),new("0","VND"),money){ProductTypeSnapshot=type};
        return new("order_test","O-test","2026-11-02",new("CONTACT","contact_test"),"DRAFT",[line],money,new("0","VND"),new("0","VND"),money,"VND",new(new(false,[]),new(false,[])),1,"2026-11-02T00:00:00Z","2026-11-02T00:00:00Z") {
            PaymentAgreementSnapshot=JsonSerializer.SerializeToElement(new {lines=new[]{new {dueRule=new {type="EVENT_RELATIVE",@event="ORDER_CONFIRMED",offsetDays=0}}}})
        };
    }
    private sealed class Zones:IWorkspaceTimeZoneReader
    {
        public Task<string?> ReadTimeZoneAsync(string workspaceId,CancellationToken cancellationToken)=>Task.FromResult<string?>("Asia/Ho_Chi_Minh");
    }
}
