using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Sales.Orders.Contracts;
using UnicoreCRM.Sales.Quotes.Contracts;
using UnicoreCRM.Billing.Payments.Contracts;
using UnicoreCRM.Workflows.Atomic.Application.Persistence;
using UnicoreCRM.Workflows.Atomic.Application.ConfirmOrder;
using UnicoreCRM.Workflows.Atomic.Contracts;
using UnicoreCRM.Workflows.Atomic.Domain;
namespace UnicoreCRM.Workflows.Atomic.Application.AcceptedQuoteOrder;

internal sealed class AutomaticConfirmation(IOrderConfirmationTransaction transactions,IAcceptedQuoteOrderWriter factory,
    IOrderConfirmationParticipant orders,IQuoteOrderConfirmationBinding quotes,IOrderPaymentPlanParticipant payments,
    IWorkspaceTimeZoneReader zones,TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    internal async Task<string> ExecuteAsync(AcceptedQuoteOrderSource source,CancellationToken ct)
    {
        var scope=Hash("quote-auto-confirm|"+source.WorkspaceId+"|"+source.AcceptanceId);
        var fingerprint=Hash(source.SnapshotJson+"\n"+source.AcceptanceJson+"\n"+source.CommercialFingerprint);
        await using var tx=await transactions.BeginAsync(scope,source.WorkspaceId,"quote:"+source.QuoteId,ct);
        var receipt=await tx.FindAsync(scope,ct);
        if(receipt is not null)
        {
            if(receipt.Fingerprint!=fingerprint)throw new UnicoreCRM.BuildingBlocks.BusinessCommandException("QUOTE_ACCEPTANCE_BINDING_MISMATCH");
            return JsonSerializer.Deserialize<ConfirmOrderWithPaymentPlanResponse>(receipt.ResponseJson,Json)!.Result.OrderId;
        }
        var binding=await quotes.ValidateAutomaticAsync(tx.OwnerTransaction,source,ct);
        var order=await factory.CreateInTransactionAsync(tx.OwnerTransaction,new(source.WorkspaceId,source.QuoteId,source.AcceptanceId,
            source.SnapshotJson,source.CommercialFingerprint,source.AcceptanceJson,source.AcceptedAt),ct);
        await orders.ValidateSnapshotAsync(order,binding.Binding.SnapshotJson,ct);
        var draft=await payments.EnsureDraftInTransactionAsync(tx.OwnerTransaction,source.WorkspaceId,order.Order.Id,source.SnapshotJson,ct);
        var request=new ConfirmOrderWithPaymentPlanRequest(draft.Id,draft.ResourceVersion);
        var now=clock.GetUtcNow();
        await ConfirmationRules.ValidateAsync(order.Order,binding.Binding,request,source.WorkspaceId,now,zones,ct);
        var command="auto_confirm_"+Guid.NewGuid().ToString("N");
        var version=await orders.ConfirmAutomaticallyAsync(order,command,binding.PolicyVersion,now,ct);
        var plan=await payments.ActivateAsync(tx.OwnerTransaction,source.WorkspaceId,order.Order.Id,draft.Id,draft.ResourceVersion,
            order.Order.BuyerRef.Type,order.Order.BuyerRef.Id,order.Order.PaymentAgreementSnapshot!.Value.GetRawText(),now,command,order.MemberId,ct);
        var response=new ConfirmOrderWithPaymentPlanResponse(command,scope,order.Order.Id,"order",version,now.UtcDateTime.ToString("O"),"COMMITTED",
            new(order.Order.Id,"CONFIRMED",now.UtcDateTime.ToString("O"),plan.Id,"ACTIVE",plan.ResourceVersion,false),[],[command+"-order",command+"-plan"],[scope]);
        await tx.CommitAsync(new(scope,source.WorkspaceId,order.Order.Id,order.MemberId,fingerprint,JsonSerializer.Serialize(response,Json),now),ct);
        return order.Order.Id;
    }
    private static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
