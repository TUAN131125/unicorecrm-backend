using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using UnicoreCRM.BuildingBlocks;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Sales.Orders.Contracts;
using UnicoreCRM.Sales.Quotes.Contracts;
using UnicoreCRM.Billing.Payments.Contracts;
using UnicoreCRM.Workflows.Atomic.Contracts;
using UnicoreCRM.Workflows.Atomic.Domain;
using UnicoreCRM.Workflows.Atomic.Application.Persistence;
namespace UnicoreCRM.Workflows.Atomic.Application.ConfirmOrder;

internal sealed class Handler(IOrderConfirmationTransaction transactions,IOrderConfirmationParticipant orders,
    IQuoteOrderConfirmationBinding quotes,IOrderPaymentPlanParticipant payments,ICurrentWorkspace workspace,TimeProvider clock,IWorkspaceTimeZoneReader zones)
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    public async Task<ConfirmOrderWithPaymentPlanResponse> ExecuteAsync(string id,ConfirmOrderWithPaymentPlanRequest request,
        string requestId,string correlation,string key,long expected,CancellationToken ct)
    {
        if(!workspace.IsResolved)throw new BusinessCommandException("ACCESS_DENIED",403);
        if(!Entity(id) || !Entity(request.PaymentPlanId) || request.ExpectedPaymentPlanVersion<0 || request.OperationalReviewNotes?.Length>2000)
            throw new BusinessCommandException("VALIDATION_FAILED",422);
        var trusted=workspace.Require();var scope=Hash(trusted.WorkspaceId+"|"+trusted.MemberId+"|order-confirm|"+key);
        var fingerprint=Hash(JsonSerializer.Serialize(new{id,request,expected},Json));
        await using var tx=await transactions.BeginAsync(scope,trusted.WorkspaceId,id,ct);
        var source=await orders.ReadAsync(tx.OwnerTransaction,id,requestId,correlation,ct);
        var receipt=await tx.FindAsync(scope,ct);
        if(receipt is not null)
        {
            if(receipt.Fingerprint!=fingerprint)throw new BusinessCommandException("IDEMPOTENCY_KEY_REUSED");
            return JsonSerializer.Deserialize<ConfirmOrderWithPaymentPlanResponse>(receipt.ResponseJson,Json)! with {Outcome="REPLAYED"};
        }
        var o=source.Order;
        if(o.ResourceVersion!=expected)throw new BusinessCommandException("VERSION_CONFLICT",412);
        if(o.State!="DRAFT" || o.ArchivedAt is not null)throw new BusinessCommandException("ORDER_CONFIRMATION_BLOCKED");
        if(o.SourceQuoteId is null || o.CommercialSnapshotFingerprint is null || o.SourceQuoteResourceVersion is null || o.PaymentAgreementSnapshot is null)
            throw new BusinessCommandException("QUOTE_ACCEPTANCE_BINDING_MISMATCH");
        var binding=await quotes.ValidateAsync(tx.OwnerTransaction,source.WorkspaceId,o.SourceQuoteId,id,source.AcceptanceId,
            o.CommercialSnapshotFingerprint,o.SourceQuoteResourceVersion.Value,requestId,correlation,ct);
        await orders.ValidateSnapshotAsync(source,binding.SnapshotJson,ct);
        var now=clock.GetUtcNow();
        await ConfirmationRules.ValidateAsync(o,binding,request,source.WorkspaceId,now,zones,ct);
        var fulfillment=request.Fulfillment;
        var command="confirm_"+Guid.NewGuid().ToString("N");
        // Orders saves first within the same uncommitted transaction. A Payments failure
        // must roll it back; neither business state is externally committed on its own.
        var version=await orders.ConfirmAsync(source,command,request.OperationalReviewNotes?.Trim()??"",fulfillment,now,ct);
        var plan=await payments.ActivateAsync(tx.OwnerTransaction,source.WorkspaceId,id,request.PaymentPlanId,request.ExpectedPaymentPlanVersion,
            o.BuyerRef.Type,o.BuyerRef.Id,o.PaymentAgreementSnapshot.Value.GetRawText(),now,command,source.MemberId,ct);
        var response=new ConfirmOrderWithPaymentPlanResponse(command,correlation,id,"order",version,now.UtcDateTime.ToString("O"),"COMMITTED",
            new(id,"CONFIRMED",now.UtcDateTime.ToString("O"),plan.Id,"ACTIVE",plan.ResourceVersion,false),[],[command+"-order",command+"-plan"],[scope]);
        await tx.EnqueueCustomerAsync(source.WorkspaceId,id,o.SourceQuoteId,source.AcceptanceId,now,ct);
        await tx.CommitAsync(new(scope,source.WorkspaceId,id,source.MemberId,fingerprint,JsonSerializer.Serialize(response,Json),now),ct);
        return response;
    }
    private static bool Entity(string? value)=>value is not null && Regex.IsMatch(value,"^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$",RegexOptions.CultureInvariant);
    private static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
