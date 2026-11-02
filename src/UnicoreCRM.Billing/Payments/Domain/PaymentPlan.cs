namespace UnicoreCRM.Billing.Payments.Domain;

internal sealed class PaymentPlan
{
    private PaymentPlan() { }
    internal static PaymentPlan Draft(string ws,string order,string buyerType,string buyer,string agreement,string kind,string currency,string[] lines,DateTimeOffset now)=>new()
    {WorkspaceId=ws,OrderId=order,PaymentPlanId="plan_"+Guid.NewGuid().ToString("N"),BuyerType=buyerType,BuyerId=buyer,
        AgreementSnapshotJson=agreement,Kind=kind,Currency=currency,ScheduleLineIdsJson=System.Text.Json.JsonSerializer.Serialize(lines),State="DRAFT",ResourceVersion=1,CreatedAt=now,UpdatedAt=now};
    internal void Activate(DateTimeOffset now){if(State!="DRAFT")throw new InvalidOperationException("Plan is not draft.");State="ACTIVE";ActivatedAt=now;UpdatedAt=now;ResourceVersion++;}

    public string WorkspaceId { get; private set; } = null!;
    public string PaymentPlanId { get; private set; } = null!;
    public string OrderId { get; private set; } = null!;
    public string BuyerType { get; private set; } = null!;
    public string BuyerId { get; private set; } = null!;
    public string Kind { get; private set; } = null!;
    public string State { get; private set; } = null!;
    public string Currency { get; private set; } = null!;
    public string AgreementSnapshotJson { get; private set; } = null!;
    public string ScheduleLineIdsJson { get; private set; } = null!;
    public string? SupersedesPlanId { get; private set; }
    public string? SupersededByPlanId { get; private set; }
    public int EvidenceCount { get; private set; }
    public long ResourceVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? ActivatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
}

internal sealed class PaymentScheduleLine
{
    private PaymentScheduleLine() { }
    internal static PaymentScheduleLine Draft(PaymentPlan plan,string id,UnicoreCRM.Billing.Payments.Contracts.PaymentAgreementLineSnapshotDocument line,DateTimeOffset now)=>new()
    {
        WorkspaceId=plan.WorkspaceId,PaymentScheduleLineId=id,PaymentPlanId=plan.PaymentPlanId,PaymentPlanVersion=plan.ResourceVersion,
        OrderId=plan.OrderId,BuyerType=plan.BuyerType,BuyerId=plan.BuyerId,Sequence=line.Sequence,Label=line.Label,Purpose=line.Purpose,
        AmountRuleJson=System.Text.Json.JsonSerializer.Serialize(line.AmountRule,Json),Amount=decimal.Parse(line.PreviewAmount.Amount,System.Globalization.CultureInfo.InvariantCulture),AmountCurrency=plan.Currency,
        DueRuleJson=System.Text.Json.JsonSerializer.Serialize(line.DueRule,Json),AllowedMethodCodesJson=System.Text.Json.JsonSerializer.Serialize(line.AllowedMethodCodes,Json),
        PreferredMethodCode=line.PreferredMethodCode,Channel=line.Channel,FulfillmentGate=line.FulfillmentGate,InvoicePolicyCode=line.InvoicePolicyCode,
        State="SCHEDULED",SatisfiedAmount=0,SatisfiedCurrency=plan.Currency,OutstandingAmount=decimal.Parse(line.PreviewAmount.Amount,System.Globalization.CultureInfo.InvariantCulture),
        OutstandingCurrency=plan.Currency,ResourceVersion=1,CreatedAt=now,UpdatedAt=now
    };
    private static readonly System.Text.Json.JsonSerializerOptions Json=new(System.Text.Json.JsonSerializerDefaults.Web);
    internal void Activate(long version,DateOnly? due,DateTimeOffset now,DateOnly? businessDate=null)
    {PaymentPlanVersion=version;ResolvedDueDate=due;State=due is null?"SCHEDULED":due<=(businessDate??DateOnly.FromDateTime(now.UtcDateTime))?"DUE":"NOT_DUE";ResourceVersion++;UpdatedAt=now;}

    public string WorkspaceId { get; private set; } = null!;
    public string PaymentScheduleLineId { get; private set; } = null!;
    public string PaymentPlanId { get; private set; } = null!;
    public long PaymentPlanVersion { get; private set; }
    public string OrderId { get; private set; } = null!;
    public string BuyerType { get; private set; } = null!;
    public string BuyerId { get; private set; } = null!;
    public int Sequence { get; private set; }
    public string Label { get; private set; } = null!;
    public string Purpose { get; private set; } = null!;
    public string AmountRuleJson { get; private set; } = null!;
    public decimal Amount { get; private set; }
    public string AmountCurrency { get; private set; } = null!;
    public string DueRuleJson { get; private set; } = null!;
    public DateOnly? ResolvedDueDate { get; private set; }
    public string AllowedMethodCodesJson { get; private set; } = null!;
    public string? PreferredMethodCode { get; private set; }
    public string? Channel { get; private set; }
    public string FulfillmentGate { get; private set; } = null!;
    public string? InvoicePolicyCode { get; private set; }
    public string State { get; private set; } = null!;
    public decimal SatisfiedAmount { get; private set; }
    public string SatisfiedCurrency { get; private set; } = null!;
    public decimal OutstandingAmount { get; private set; }
    public string OutstandingCurrency { get; private set; } = null!;
    public long ResourceVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
}
