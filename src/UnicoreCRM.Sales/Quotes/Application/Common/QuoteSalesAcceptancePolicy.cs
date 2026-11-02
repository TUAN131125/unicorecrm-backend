using Microsoft.Extensions.Options;
namespace UnicoreCRM.Sales.Quotes.Application.Common;

internal sealed class QuoteSalesAcceptanceOptions
{
    public string PublicBaseUrl { get; set; }="";
    public Dictionary<string,QuoteSalesAcceptancePolicy> Workspaces { get; set; }=new(StringComparer.Ordinal);
}
internal sealed class QuoteSalesAcceptancePolicy
{
    public string Version { get; set; }="";
    public bool Enabled { get; set; }
    public bool OrdinarySalesWithoutInternalApproval { get; set; }
    public bool RequirePaymentInstructions { get; set; }
    public bool AutoConfirmOrders { get; set; }
    public Dictionary<string,decimal> AutoConfirmLimits { get; set; }=new(StringComparer.Ordinal);
    public string[] AutoConfirmProductTypes { get; set; }=[];
    public bool AllowCustomerLink { get; set; }
    public bool AllowStaffRecorded { get; set; }
    public bool RequireVerifiedSignerAuthority { get; set; }=true;
    public bool RequireStaffOperationalApproval { get; set; }=true;
    public int InvitationHours { get; set; }=48;
    public Dictionary<string,decimal> OrderDraftLimits { get; set; }=new(StringComparer.Ordinal);
}
internal sealed class QuoteSalesPolicyReader(IOptionsMonitor<QuoteSalesAcceptanceOptions> options)
{
    internal QuoteSalesAcceptancePolicy? Read(string workspace) => options.CurrentValue.Workspaces.TryGetValue(workspace,out var p)
        && p.Enabled && !string.IsNullOrWhiteSpace(p.Version) && p.Version.Length<=80 && p.InvitationHours is >=1 and <=168 ? p:null;
    internal bool WithoutInternalApproval(string workspace) => Read(workspace)?.OrdinarySalesWithoutInternalApproval == true;
    internal string? PublicBaseUrl
    {
        get
        {
            var value=options.CurrentValue.PublicBaseUrl;
            return Uri.TryCreate(value,UriKind.Absolute,out var uri) && (uri.Scheme=="https" || uri.IsLoopback && uri.Scheme=="http")
                && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment) ? value.TrimEnd('/') : null;
        }
    }
}
