using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using UnicoreCRM.BuildingBlocks;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Sales.Quotes.Contracts;
using UnicoreCRM.Sales.Quotes.Domain;
using UnicoreCRM.Sales.Quotes.Application.Common;
namespace UnicoreCRM.Sales.Quotes.Infrastructure.Persistence;

internal sealed class QuoteOrderConfirmationBindingReader(QuotesDbContext db,QuoteSalesPolicyReader policies,
    IQuoteWorkspacePolicyReader workspaces,IQuoteRelationshipVerifier relationships,IEnumerable<IQuoteAutomaticBuyerReader> automaticBuyers,
    IQuoteDocumentPersistence documents,IWorkspaceTimeZoneReader zones,TimeProvider clock,IQuoteApprovalPolicyReader approvals,IQuotesPersistence quotes):IQuoteOrderConfirmationBinding
{
    public async Task<QuoteOrderConfirmationBinding> ValidateAsync(OwnerTransaction tx,string ws,string quote,string order,
        string acceptance,string fingerprint,long version,string request,string correlation,CancellationToken ct)
    {
        // Retained constructor compatibility for historical consumers; B1 is not an issuance prerequisite.
        _ = approvals;
        db.Database.SetDbConnection(tx.Connection,false);await db.Database.UseTransactionAsync(tx.Transaction,ct);
        var q=await db.Quotes.AsNoTracking().SingleOrDefaultAsync(x=>x.WorkspaceId==ws && x.QuoteId==quote,ct);
        var a=await db.Set<QuoteSalesAcceptance>().AsNoTracking().SingleOrDefaultAsync(x=>x.WorkspaceId==ws && x.AcceptanceId==acceptance && x.QuoteId==quote,ct);
        if(q is null || a is null || q.Status!="ACCEPTED" || q.ArchivedAt is not null || a.OrderId!=order || a.ConversionStatus!="COMPLETED"
            || a.CommercialFingerprint!=fingerprint || QuoteApprovalAssessment.ContentFingerprint(q)!=fingerprint)
            throw new BusinessCommandException("QUOTE_ACCEPTANCE_BINDING_MISMATCH");
        using var snapshot=JsonDocument.Parse(a.SnapshotJson);
        if(snapshot.RootElement.GetProperty("resourceVersion").GetInt64()!=version)throw new BusinessCommandException("QUOTE_ACCEPTANCE_BINDING_MISMATCH");
        var p=policies.Read(ws);
        if(p is null || (await workspaces.ReadAsync(ws,ct)) is not {Enabled:true})throw new BusinessCommandException("ACCEPTANCE_POLICY_DISABLED");
        if(p.RequirePaymentInstructions)throw new BusinessCommandException("PAYMENT_ACCOUNT_AUTHORITY_REQUIRED");
        if(p.RequireVerifiedSignerAuthority)throw new BusinessCommandException("VERIFIED_SIGNER_AUTHORITY_REQUIRED");
        if((a.Source=="STAFF_RECORDED" && !p.AllowStaffRecorded) || (a.Source=="CUSTOMER_LINK" && !p.AllowCustomerLink)
            || !p.OrderDraftLimits.TryGetValue(q.Currency,out var limit) || q.GrandTotalAmount>limit)throw new BusinessCommandException("ACCEPTANCE_POLICY_DISABLED");
        if(!await relationships.VerifyBuyerAsync(new(q.BuyerType,q.BuyerId),request,correlation,ct))throw new BusinessCommandException("BUYER_BINDING_INVALID");
        return new(a.SnapshotJson,a.OperationalApprovalRequired || a.Source=="STAFF_RECORDED" && p.RequireStaffOperationalApproval);
    }
    public async Task<AutomaticQuoteOrderBinding> ValidateAutomaticAsync(OwnerTransaction tx,AcceptedQuoteOrderSource source,CancellationToken ct)
    {
        db.Database.SetDbConnection(tx.Connection,false);await db.Database.UseTransactionAsync(tx.Transaction,ct);
        await quotes.LockAsync("aggregate:"+source.WorkspaceId+":"+source.QuoteId,ct);
        var q=await db.Quotes.AsNoTracking().SingleOrDefaultAsync(x=>x.WorkspaceId==source.WorkspaceId && x.QuoteId==source.QuoteId,ct);
        var a=await db.Set<QuoteSalesAcceptance>().AsNoTracking().SingleOrDefaultAsync(x=>x.WorkspaceId==source.WorkspaceId && x.AcceptanceId==source.AcceptanceId && x.QuoteId==source.QuoteId,ct);
        if(q is null || a is null || !a.AutoConfirmRequested || q.Status!="ACCEPTED" || q.ArchivedAt is not null
            || a.AssuranceLevel!="ORDINARY_SALES_UNVERIFIED" || a.SnapshotJson!=source.SnapshotJson
            || a.CommercialFingerprint!=source.CommercialFingerprint || QuoteApprovalAssessment.ContentFingerprint(q)!=a.CommercialFingerprint)
            throw new BusinessCommandException("QUOTE_ACCEPTANCE_BINDING_MISMATCH");
        var p=policies.Read(source.WorkspaceId);
        if(p is null || !p.AutoConfirmOrders || (await workspaces.ReadAsync(source.WorkspaceId,ct)) is not {Enabled:true})throw new BusinessCommandException("AUTO_CONFIRM_POLICY_DISABLED");
        if(p.RequirePaymentInstructions)throw new BusinessCommandException("PAYMENT_ACCOUNT_AUTHORITY_REQUIRED");
        if(p.RequireVerifiedSignerAuthority)throw new BusinessCommandException("VERIFIED_SIGNER_AUTHORITY_REQUIRED");
        if(a.Source=="STAFF_RECORDED" && !p.AllowStaffRecorded || a.Source=="CUSTOMER_LINK" && !p.AllowCustomerLink)throw new BusinessCommandException("ACCEPTANCE_POLICY_DISABLED");
        if(!p.OrderDraftLimits.TryGetValue(q.Currency,out var acceptanceLimit) || q.GrandTotalAmount>acceptanceLimit)throw new BusinessCommandException("ACCEPTANCE_POLICY_DISABLED");
        if(!p.AutoConfirmLimits.TryGetValue(q.Currency,out var max) || max<=0 || q.GrandTotalAmount<=0 || q.GrandTotalAmount>max)throw new BusinessCommandException("AUTO_CONFIRM_RISK_LIMIT_EXCEEDED");
        var snapshot=System.Text.Json.JsonSerializer.Deserialize<QuoteReadModel>(a.SnapshotJson,QuotePricing.Json)!;
        if(snapshot.BuyerRef.Type!=q.BuyerType || snapshot.BuyerRef.Id!=q.BuyerId)throw new BusinessCommandException("BUYER_BINDING_INVALID");
        if(snapshot.LineItems.Any(x=>x.ProductTypeSnapshot is null || !p.AutoConfirmProductTypes.Contains(x.ProductTypeSnapshot,StringComparer.Ordinal)))throw new BusinessCommandException("AUTO_CONFIRM_PRODUCT_TYPE_BLOCKED");
        var validity=QuoteValidity.Evaluate(q.ValidUntil,await zones.ReadTimeZoneAsync(source.WorkspaceId,ct),clock.GetUtcNow());
        if(validity.State is "OVERDUE" or "TIMEZONE_UNAVAILABLE")throw new BusinessCommandException("QUOTE_EXPIRED_OR_TIMEZONE_UNAVAILABLE");
        var readers=automaticBuyers.Where(x=>x.BuyerType==q.BuyerType).ToArray();
        if(readers.Length!=1 || !await readers[0].IsEligibleAsync(tx,source.WorkspaceId,q.BuyerId,ct))throw new BusinessCommandException("BUYER_BINDING_INVALID");
        var d=await documents.FindAsync(source.WorkspaceId,source.QuoteId,a.DocumentId,ct);
        var bytes=d is null ? null : await documents.ReadBytesAsync(source.WorkspaceId,a.DocumentId,ct);
        if(d is null || d.QuoteRevision!=q.QuoteRevision || d.CommercialFingerprint!=a.CommercialFingerprint || bytes is null || bytes.Length!=d.FileSize
            || Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))!=d.PdfSha256)throw new BusinessCommandException("DOCUMENT_INTEGRITY_FAILED");
        return new(new(a.SnapshotJson,a.OperationalApprovalRequired || a.Source=="STAFF_RECORDED" && p.RequireStaffOperationalApproval),p.Version);
    }

}
