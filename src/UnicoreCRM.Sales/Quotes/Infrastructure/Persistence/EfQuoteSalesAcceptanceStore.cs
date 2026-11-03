using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using UnicoreCRM.Sales.Quotes.Application.Common;
using UnicoreCRM.Sales.Quotes.Contracts;
using UnicoreCRM.Sales.Quotes.Domain;
using UnicoreCRM.Platform.Workspace.Contracts;
namespace UnicoreCRM.Sales.Quotes.Infrastructure.Persistence;

internal sealed class EfQuoteSalesAcceptanceStore(QuotesDbContext db,TimeProvider clock,QuoteSalesPolicyReader policies,IQuoteWorkspacePolicyReader workspaces)
    : IQuoteSalesAcceptanceStore, IAcceptedQuoteOrderQueue
{
    public Task<QuoteSalesAcceptance?> ReadAsync(string workspace,string quote,CancellationToken ct)=>
        db.Set<QuoteSalesAcceptance>().SingleOrDefaultAsync(x=>x.WorkspaceId==workspace && x.QuoteId==quote,ct);
    public Task<QuoteAcceptanceInvitation?> InvitationAsync(string hash,CancellationToken ct)=>
        db.Set<QuoteAcceptanceInvitation>().SingleOrDefaultAsync(x=>x.TokenHash==hash,ct);
    public Task<QuoteAcceptanceInvitation?> InvitationByIdAsync(string ws,string quote,string id,CancellationToken ct)=>
        db.Set<QuoteAcceptanceInvitation>().SingleOrDefaultAsync(x=>x.WorkspaceId==ws && x.QuoteId==quote && x.InvitationId==id,ct);
    public async Task<IReadOnlyList<QuoteAcceptanceInvitation>> InvitationsAsync(string ws,string quote,CancellationToken ct)=>
        await db.Set<QuoteAcceptanceInvitation>().Where(x=>x.WorkspaceId==ws && x.QuoteId==quote).OrderByDescending(x=>x.CreatedAt).Take(50).ToArrayAsync(ct);
    public void Add(QuoteSalesAcceptance acceptance)=>db.Add(acceptance);
    public void Add(QuoteAcceptanceInvitation invitation)=>db.Add(invitation);
    public Task ReloadAsync(QuoteAcceptanceInvitation invitation,CancellationToken ct)=>db.Entry(invitation).ReloadAsync(ct);
    public async Task ReloadQuoteAsync(Quote quote,CancellationToken ct)
    {
        await db.Entry(quote).ReloadAsync(ct);
        db.Entry(quote).Reference(x=>x.SalesAcceptance).IsLoaded=false;
        await db.Entry(quote).Reference(x=>x.SalesAcceptance).LoadAsync(ct);
    }
    public Task<bool> HasUnresolvedDeliveryAsync(string ws,string quote,CancellationToken ct)=>
        db.Set<QuoteAcceptanceInvitation>().AnyAsync(x=>x.WorkspaceId==ws && x.QuoteId==quote
            && (x.DeliveryStatus=="SENDING" || x.DeliveryStatus=="UNKNOWN"
                // Older sends did not distinguish pre-send failure from a lost SMTP acknowledgment.
                || x.DeliveryStatus=="FAILED" && (x.DeliveryErrorCode=="GMAIL_DELIVERY_TIMEOUT" || x.DeliveryErrorCode=="GMAIL_DELIVERY_FAILED")),ct);
    public async Task<IReadOnlyList<AcceptedQuoteOrderSource>> PendingAsync(int limit,CancellationToken ct)
    {
        var now=clock.GetUtcNow();
        var rows=await db.Set<QuoteSalesAcceptance>().AsNoTracking().Where(x=>x.OrderId==null && (x.NextAttemptAt==null || x.NextAttemptAt<=now))
            .OrderBy(x=>x.AcceptedAt).Take(Math.Clamp(limit,1,100)).ToArrayAsync(ct);
        var result=new List<AcceptedQuoteOrderSource>();
        foreach(var a in rows)
        {
            var q=await db.Quotes.AsNoTracking().SingleOrDefaultAsync(x=>x.WorkspaceId==a.WorkspaceId && x.QuoteId==a.QuoteId,ct);
            var p=policies.Read(a.WorkspaceId);
            if(!a.AutoConfirmRequested && (q is null || q.Status!="ACCEPTED" || q.ArchivedAt is not null || p is null || p.RequireVerifiedSignerAuthority
                || (await workspaces.ReadAsync(a.WorkspaceId,ct)) is not {Enabled:true}
                || !p.OrderDraftLimits.TryGetValue(q.Currency,out var max) || q.GrandTotalAmount>max
                || QuoteApprovalAssessment.ContentFingerprint(q)!=a.CommercialFingerprint))
            { await RetryAsync(a.WorkspaceId,a.AcceptanceId,"ACCEPTANCE_POLICY_OR_BINDING_BLOCKED",ct); continue; }
            result.Add(new(a.AcceptanceId,a.WorkspaceId,a.QuoteId,a.SnapshotJson,a.CommercialFingerprint,
                JsonSerializer.Serialize(QuoteSalesAcceptanceService.View(a) with {ConversionStatus="PENDING",OrderId=null,LastErrorCode=null},QuotePricing.Json),a.AcceptedAt,a.AutoConfirmRequested));
        }
        return result;
    }
    public async Task CompleteAsync(string ws,string id,string order,CancellationToken ct)
    {
        var a=await db.Set<QuoteSalesAcceptance>().SingleAsync(x=>x.WorkspaceId==ws && x.AcceptanceId==id,ct);
        if(a.OrderId is not null && a.OrderId!=order)throw new InvalidOperationException("Conflicting durable Order receipt.");
        a.Complete(order); await db.SaveChangesAsync(ct);
    }
    public async Task RetryAsync(string ws,string id,string code,CancellationToken ct)
    {
        await db.Set<QuoteSalesAcceptance>().Where(x=>x.WorkspaceId==ws && x.AcceptanceId==id && x.OrderId==null)
            .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.LastErrorCode,code).SetProperty(x=>x.ConversionStatus,code=="ORDER_CREATION_RETRY_PENDING"?"PENDING":"BLOCKED").SetProperty(x=>x.Attempts,x=>x.Attempts+1)
                .SetProperty(x=>x.NextAttemptAt,clock.GetUtcNow().AddSeconds(15)),ct);
    }
}
