using System.Globalization;
using UnicoreCRM.AI.Context;
using UnicoreCRM.Crm.Organizations.Contracts;

namespace UnicoreCRM.AI.Tools;

internal sealed class OrganizationSummaryTool(IOrganizationSummaryReader reader) : IAiContextTool
{
    internal const string ToolName = "organization.summary.read";
    public string Name => ToolName;
    public string EntityType => "organization";
    public async Task<AiContextToolResult> ExecuteAsync(string id,string requestId,string correlationId,CancellationToken ct)
    {
        var result=await reader.ReadAsync(id,requestId,correlationId,ct); if(result.Status!=OrganizationSummaryReadStatus.Succeeded)return new(Map(result.Status));
        var s=result.Summary!;var fields=new SortedDictionary<string,string>(StringComparer.Ordinal);Add(fields,"displayName",s.DisplayName);Add(fields,"status",s.Status);Add(fields,"industry",s.Industry);Add(fields,"employeeCount",s.EmployeeCount?.ToString(CultureInfo.InvariantCulture));Add(fields,"relationshipLevel",s.RelationshipLevel);
        return new(AiContextLoadStatus.Succeeded,new AiContextItem("organization",s.OrganizationId,fields,s.DisplayName,s.Version,ToolName));
    }
    private static AiContextLoadStatus Map(OrganizationSummaryReadStatus s)=>s switch{OrganizationSummaryReadStatus.AccessDenied=>AiContextLoadStatus.AccessDenied,OrganizationSummaryReadStatus.WorkspaceMismatch=>AiContextLoadStatus.WorkspaceMismatch,OrganizationSummaryReadStatus.InvalidReference=>AiContextLoadStatus.InvalidReference,OrganizationSummaryReadStatus.NotFound=>AiContextLoadStatus.NotFound,_=>AiContextLoadStatus.ToolRejected};
    private static void Add(IDictionary<string,string> fields,string key,string? value){if(value is not null)fields.Add(key,value);}
}
