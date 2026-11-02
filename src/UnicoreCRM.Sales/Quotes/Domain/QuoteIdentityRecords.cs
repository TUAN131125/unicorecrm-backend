namespace UnicoreCRM.Sales.Quotes.Domain;

internal sealed class QuoteIdentityVersion
{
    private QuoteIdentityVersion(){}
    internal QuoteIdentityVersion(string w,string c,string q,long version,string kind,string actor,string reason,string json,string policy,DateTimeOffset now)
    {WorkspaceId=w;CaseId=c;QuoteId=q;ResourceVersion=version;Kind=kind;ActorId=actor;Reason=reason;SnapshotJson=json;PolicyVersion=policy;RecordedAt=now;}
    public string WorkspaceId{get;private set;}=null!;public string CaseId{get;private set;}=null!;public string QuoteId{get;private set;}=null!;
    public long ResourceVersion{get;private set;}public string Kind{get;private set;}=null!;public string ActorId{get;private set;}=null!;
    public string Reason{get;private set;}=null!;public string SnapshotJson{get;private set;}=null!;public string PolicyVersion{get;private set;}=null!;public DateTimeOffset RecordedAt{get;private set;}
}
internal sealed class QuoteEvidenceSourceVersion
{
    private QuoteEvidenceSourceVersion(){}
    internal QuoteEvidenceSourceVersion(string w,string id,long version,string buyerType,string buyerId,string status,string actor,string json,DateTimeOffset now)
    {WorkspaceId=w;SourceId=id;ResourceVersion=version;BuyerType=buyerType;BuyerId=buyerId;Status=status;ActorId=actor;SnapshotJson=json;RecordedAt=now;}
    public string WorkspaceId{get;private set;}=null!;public string SourceId{get;private set;}=null!;public long ResourceVersion{get;private set;}
    public string BuyerType{get;private set;}=null!;public string BuyerId{get;private set;}=null!;public string Status{get;private set;}=null!;
    public string ActorId{get;private set;}=null!;public string SnapshotJson{get;private set;}=null!;public DateTimeOffset RecordedAt{get;private set;}
}
