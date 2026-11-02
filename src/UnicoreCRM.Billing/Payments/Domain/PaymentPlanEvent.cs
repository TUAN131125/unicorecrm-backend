namespace UnicoreCRM.Billing.Payments.Domain;
internal sealed class PaymentPlanEvent
{
    private PaymentPlanEvent(){}
    internal PaymentPlanEvent(string ws,string plan,string command,string type,string payload,DateTimeOffset now)
    {EventId=type=="PAYMENT_PLAN_ACTIVATED"?command+"-plan":"plan_event_"+Guid.NewGuid().ToString("N");WorkspaceId=ws;PlanId=plan;CommandId=command;EventType=type;PayloadJson=payload;OccurredAt=now;}
    public string EventId {get;private set;}=null!;
    public string WorkspaceId {get;private set;}=null!;
    public string PlanId {get;private set;}=null!;
    public string CommandId {get;private set;}=null!;
    public string EventType {get;private set;}=null!;
    public string PayloadJson {get;private set;}=null!;
    public DateTimeOffset OccurredAt {get;private set;}
}
