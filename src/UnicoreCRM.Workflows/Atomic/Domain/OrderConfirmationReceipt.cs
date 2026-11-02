namespace UnicoreCRM.Workflows.Atomic.Domain;
internal sealed class OrderConfirmationReceipt
{
    private OrderConfirmationReceipt(){}
    internal OrderConfirmationReceipt(string scope,string ws,string order,string actor,string fingerprint,string response,DateTimeOffset now)
    {ScopeKey=scope;WorkspaceId=ws;OrderId=order;ActorId=actor;Fingerprint=fingerprint;ResponseJson=response;OccurredAt=now;}
    public string ScopeKey {get;private set;}=null!;
    public string WorkspaceId {get;private set;}=null!;
    public string OrderId {get;private set;}=null!;
    public string ActorId {get;private set;}=null!;
    public string Fingerprint {get;private set;}=null!;
    public string ResponseJson {get;private set;}=null!;
    public DateTimeOffset OccurredAt {get;private set;}
}
