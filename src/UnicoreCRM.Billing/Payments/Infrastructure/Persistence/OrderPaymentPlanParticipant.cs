using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using UnicoreCRM.BuildingBlocks;
using UnicoreCRM.Billing.Payments.Contracts;
using UnicoreCRM.Billing.Payments.Domain;
using UnicoreCRM.Billing.Payments.Application.Common;
using UnicoreCRM.Platform.Workspace.Contracts;
namespace UnicoreCRM.Billing.Payments.Infrastructure.Persistence;

internal sealed class OrderPaymentPlanParticipant(PaymentsDbContext db,TimeProvider clock,IWorkspaceTimeZoneReader zones):IOrderPaymentPlanParticipant
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    public async Task EnsureDraftAsync(string ws,string order,string snapshot,CancellationToken ct)
    {
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        await EnsureDraftInTransactionAsync(new OwnerTransaction(db.Database.GetDbConnection(),tx.GetDbTransaction()),ws,order,snapshot,ct);
        await tx.CommitAsync(ct);
    }
    public async Task<PaymentPlanDocument> EnsureDraftInTransactionAsync(OwnerTransaction owner,string ws,string order,string snapshot,CancellationToken ct)
    {
        if(!ReferenceEquals(db.Database.GetDbConnection(),owner.Connection))db.Database.SetDbConnection(owner.Connection,false);
        if(db.Database.CurrentTransaction?.GetDbTransaction()!=owner.Transaction)await db.Database.UseTransactionAsync(owner.Transaction,ct);
        using var source=JsonDocument.Parse(snapshot);var q=source.RootElement;
        var agreement=q.GetProperty("paymentAgreement");var a=agreement.Deserialize<PaymentAgreementSnapshotDocument>(Json)??throw new BusinessCommandException("PAYMENT_AGREEMENT_REQUIRED");
        var key="order-plan:"+ws.ToLowerInvariant()+":"+order.ToLowerInvariant();
        await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int;EXEC @r=sys.sp_getapplock @Resource={key},@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=15000;IF @r<0 THROW 51001,'Plan lock unavailable',1;",ct);
        var existing=await db.PaymentPlans.SingleOrDefaultAsync(x=>x.WorkspaceId==ws && x.OrderId==order,ct);
        if(existing is not null)
        {if(!JsonElement.DeepEquals(agreement,JsonSerializer.Deserialize<JsonElement>(existing.AgreementSnapshotJson)))throw new BusinessCommandException("PAYMENT_PLAN_AGREEMENT_MISMATCH");return PaymentProjection.Plan(existing);}
        var now=clock.GetUtcNow();var ids=a.Lines.Select(_=>"schedule_"+Guid.NewGuid().ToString("N")).ToArray();var buyer=q.GetProperty("buyerRef");
        var plan=PaymentPlan.Draft(ws,order,buyer.GetProperty("type").GetString()!,buyer.GetProperty("id").GetString()!,agreement.GetRawText(),a.Kind,a.Currency,ids,now);
        _=PaymentProjection.Plan(plan);db.Add(plan);
        for(var i=0;i<a.Lines.Count;i++){var line=PaymentScheduleLine.Draft(plan,ids[i],a.Lines[i],now);_=PaymentProjection.ScheduleLine(line);db.Add(line);}
        db.Add(new PaymentPlanEvent(ws,plan.PaymentPlanId,"order-draft:"+order,"PAYMENT_PLAN_DRAFT_CREATED",JsonSerializer.Serialize(new{orderId=order,sourceQuoteId=q.GetProperty("id").GetString()},Json),now));
        await db.SaveChangesAsync(ct);return PaymentProjection.Plan(plan);
    }
    public async Task<PaymentPlanDocument> ActivateAsync(OwnerTransaction tx,string ws,string order,string id,long expected,string buyerType,string buyer,
        string agreement,DateTimeOffset now,string command,string actor,CancellationToken ct)
    {
        if(!ReferenceEquals(db.Database.GetDbConnection(),tx.Connection))db.Database.SetDbConnection(tx.Connection,false);
        if(db.Database.CurrentTransaction?.GetDbTransaction()!=tx.Transaction)await db.Database.UseTransactionAsync(tx.Transaction,ct);
        var plan=await db.PaymentPlans.SingleOrDefaultAsync(x=>x.WorkspaceId==ws && x.OrderId==order && x.PaymentPlanId==id,ct)
            ??throw new BusinessCommandException("PAYMENT_PLAN_DRAFT_REQUIRED");
        if(plan.ResourceVersion!=expected)throw new BusinessCommandException("PAYMENT_PLAN_VERSION_CONFLICT",412);
        if(plan.State!="DRAFT")throw new BusinessCommandException("PAYMENT_PLAN_DRAFT_REQUIRED");
        if(plan.BuyerType!=buyerType || plan.BuyerId!=buyer || !JsonElement.DeepEquals(JsonSerializer.Deserialize<JsonElement>(agreement),JsonSerializer.Deserialize<JsonElement>(plan.AgreementSnapshotJson)))
            throw new BusinessCommandException("PAYMENT_PLAN_AGREEMENT_MISMATCH");
        var a=JsonSerializer.Deserialize<PaymentAgreementSnapshotDocument>(agreement,Json)!;
        if(plan.Currency!=a.Currency || plan.Kind!=a.Kind || plan.SupersededByPlanId is not null || plan.ActivatedAt is not null)
            throw new BusinessCommandException("PAYMENT_PLAN_AGREEMENT_MISMATCH");
        DateOnly today;
        try{var zone=await zones.ReadTimeZoneAsync(ws,ct)??throw new TimeZoneNotFoundException();today=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now,TimeZoneInfo.FindSystemTimeZoneById(zone)).DateTime);}
        catch(Exception e)when(e is TimeZoneNotFoundException or InvalidTimeZoneException){throw new BusinessCommandException("WORKSPACE_TIMEZONE_UNAVAILABLE");}
        var lines=await db.PaymentScheduleLines.Where(x=>x.WorkspaceId==ws && x.PaymentPlanId==id).OrderBy(x=>x.Sequence).ToArrayAsync(ct);
        var ids=JsonSerializer.Deserialize<string[]>(plan.ScheduleLineIdsJson)!;
        if(lines.Length!=a.Lines.Count || !ids.Order().SequenceEqual(lines.Select(x=>x.PaymentScheduleLineId).Order()) || plan.EvidenceCount!=0)throw new BusinessCommandException("PAYMENT_PLAN_AGREEMENT_MISMATCH");
        plan.Activate(now);
        foreach(var line in lines)
        {
            var source=a.Lines.Single(x=>x.Sequence==line.Sequence);
            if(line.Amount!=decimal.Parse(source.PreviewAmount.Amount,System.Globalization.CultureInfo.InvariantCulture) || line.SatisfiedAmount!=0
                || line.OutstandingAmount!=line.Amount || line.PaymentPlanVersion!=expected || line.OrderId!=order || line.BuyerId!=buyer || line.BuyerType!=buyerType
                || line.State!="SCHEDULED" || line.AmountCurrency!=a.Currency || line.SatisfiedCurrency!=a.Currency || line.OutstandingCurrency!=a.Currency
                || line.Label!=source.Label || line.Purpose!=source.Purpose || line.FulfillmentGate!=source.FulfillmentGate
                || line.PreferredMethodCode!=source.PreferredMethodCode || line.Channel!=source.Channel || line.InvoicePolicyCode!=source.InvoicePolicyCode
                || !JsonElement.DeepEquals(JsonSerializer.Deserialize<JsonElement>(line.AllowedMethodCodesJson),JsonSerializer.SerializeToElement(source.AllowedMethodCodes,Json))
                || !JsonElement.DeepEquals(JsonSerializer.Deserialize<JsonElement>(line.AmountRuleJson),JsonSerializer.SerializeToElement(source.AmountRule,Json))
                || !JsonElement.DeepEquals(JsonSerializer.Deserialize<JsonElement>(line.DueRuleJson),JsonSerializer.SerializeToElement(source.DueRule,Json)))throw new BusinessCommandException("PAYMENT_PLAN_AGREEMENT_MISMATCH");
            DateOnly? due=null;
            if(source.DueRule.Type=="FIXED_DATE")due=DateOnly.ParseExact(source.DueRule.Date!,"yyyy-MM-dd");
            if(source.DueRule.Type=="EVENT_RELATIVE" && source.DueRule.Event=="ORDER_CONFIRMED")
            {if(source.DueRule.DayBasis!="CALENDAR")throw new BusinessCommandException("PAYMENT_CALENDAR_AUTHORITY_REQUIRED");due=today.AddDays(source.DueRule.OffsetDays??0);}
            line.Activate(plan.ResourceVersion,due,now,today);
        }
        db.Add(new PaymentPlanEvent(ws,id,command,"PAYMENT_PLAN_ACTIVATED",JsonSerializer.Serialize(new{actor,orderId=order,version=plan.ResourceVersion,activatedAt=now},Json),now));
        await db.SaveChangesAsync(ct);return PaymentProjection.Plan(plan);
    }
}
