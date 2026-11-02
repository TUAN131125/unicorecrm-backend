namespace UnicoreCRM.Sales.Orders.Domain;
internal sealed class OrderCreationEvidence
{
    private OrderCreationEvidence() { }
    internal OrderCreationEvidence(string id,string ws,string quote,string acceptance,string order,string fingerprint,string payload,DateTimeOffset now)
    {AuditId=id;WorkspaceId=ws;QuoteId=quote;AcceptanceId=acceptance;OrderId=order;Fingerprint=fingerprint;PayloadJson=payload;OccurredAt=now;}
    public string AuditId { get; private set; }=null!;
    public string WorkspaceId { get; private set; }=null!;
    public string QuoteId { get; private set; }=null!;
    public string AcceptanceId { get; private set; }=null!;
    public string OrderId { get; private set; }=null!;
    public string Fingerprint { get; private set; }=null!;
    public string PayloadJson { get; private set; }=null!;
    public DateTimeOffset OccurredAt { get; private set; }
}
internal sealed class OrderOutboxMessage
{
    private OrderOutboxMessage() { }
    internal OrderOutboxMessage(string id,string ws,string order,string payload,DateTimeOffset now,string eventType="ORDER_DRAFT_CREATED_FROM_ACCEPTED_QUOTE")
    {EventId=id;WorkspaceId=ws;OrderId=order;PayloadJson=payload;OccurredAt=now;EventType=eventType;}
    public string EventId { get; private set; }=null!;
    public string WorkspaceId { get; private set; }=null!;
    public string OrderId { get; private set; }=null!;
    public string EventType { get; private set; }="ORDER_DRAFT_CREATED_FROM_ACCEPTED_QUOTE";
    public string PayloadJson { get; private set; }=null!;
    public DateTimeOffset OccurredAt { get; private set; }
}
