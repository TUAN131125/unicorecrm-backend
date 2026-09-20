using UnicoreCRM.AI.Context;
using UnicoreCRM.Crm.Customers.Contracts;

namespace UnicoreCRM.AI.Tools;

internal sealed class CustomerSummaryTool(ICustomerSummaryReader reader) : IAiContextTool
{
    internal const string ToolName="customer.summary.read";public string Name=>ToolName;public string EntityType=>"customer";
    public async Task<AiContextToolResult> ExecuteAsync(string id,string requestId,string correlationId,CancellationToken ct)
    {var result=await reader.ReadAsync(id,requestId,correlationId,ct);if(result.Status!=CustomerSummaryReadStatus.Succeeded)return new(Map(result.Status));var s=result.Summary!;var fields=new SortedDictionary<string,string>(StringComparer.Ordinal);Add(fields,"customerCode",s.CustomerCode);Add(fields,"status",s.Status);Add(fields,"health",s.Health);Add(fields,"tier",s.Tier);Add(fields,"segment",s.Segment);Add(fields,"nextCareAt",s.NextCareAt);if(s.HealthAssessment is { } h){Add(fields,"healthScore",h.Score?.ToString(System.Globalization.CultureInfo.InvariantCulture));Add(fields,"healthBand",h.HealthBand);Add(fields,"churnRisk",h.ChurnRisk);Add(fields,"healthConfidence",h.Confidence);Add(fields,"healthReasonCode",h.ReasonCode);Add(fields,"purchaseCount",h.PurchaseCount.ToString(System.Globalization.CultureInfo.InvariantCulture));Add(fields,"daysSinceLastPurchase",h.DaysSinceLastPurchase?.ToString(System.Globalization.CultureInfo.InvariantCulture));Add(fields,"expectedPurchaseCadenceDays",h.ExpectedPurchaseCadenceDays?.ToString(System.Globalization.CultureInfo.InvariantCulture));Add(fields,"healthAlgorithmVersion",h.AlgorithmVersion);}return new(AiContextLoadStatus.Succeeded,new AiContextItem("customer",s.CustomerId,fields,s.CustomerCode,s.Version,ToolName));}
    private static AiContextLoadStatus Map(CustomerSummaryReadStatus s)=>s switch{CustomerSummaryReadStatus.AccessDenied=>AiContextLoadStatus.AccessDenied,CustomerSummaryReadStatus.WorkspaceMismatch=>AiContextLoadStatus.WorkspaceMismatch,CustomerSummaryReadStatus.InvalidReference=>AiContextLoadStatus.InvalidReference,CustomerSummaryReadStatus.NotFound=>AiContextLoadStatus.NotFound,_=>AiContextLoadStatus.ToolRejected};
    private static void Add(IDictionary<string,string> fields,string key,string? value){if(value is not null)fields.Add(key,value);}
}
