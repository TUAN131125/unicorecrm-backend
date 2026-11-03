namespace UnicoreCRM.Workflows.Atomic.Domain;

internal sealed class AcceptedQuoteCustomerTask
{
    private AcceptedQuoteCustomerTask() { }
    internal AcceptedQuoteCustomerTask(string workspaceId,string orderId,string quoteId,string acceptanceId,DateTimeOffset now)
    { WorkspaceId=workspaceId;OrderId=orderId;QuoteId=quoteId;AcceptanceId=acceptanceId;CreatedAt=UpdatedAt=NextAttemptAt=now; }
    public string WorkspaceId {get;private set;}=null!;
    public string OrderId {get;private set;}=null!;
    public string QuoteId {get;private set;}=null!;
    public string AcceptanceId {get;private set;}=null!;
    public string State {get;private set;}="PENDING";
    public string? CustomerId {get;private set;}
    public string? Resolution {get;private set;}
    public string? LastErrorCode {get;private set;}
    public string Executor {get;private set;}="system:accepted-quote-customer";
    public int Attempts {get;private set;}
    public DateTimeOffset CreatedAt {get;private set;}
    public DateTimeOffset UpdatedAt {get;private set;}
    public DateTimeOffset NextAttemptAt {get;private set;}
    internal void Record(bool success,string? customerId,string? resolution,string? error,DateTimeOffset now)
    { Attempts++;UpdatedAt=now;CustomerId=customerId;Resolution=resolution;LastErrorCode=error;
      State=success?"COMPLETED":"PENDING";NextAttemptAt=now.AddSeconds(Math.Min(300,5*Attempts)); }
}
