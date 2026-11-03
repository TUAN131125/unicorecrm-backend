using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UnicoreCRM.Crm.Customers.Application.Common;
using UnicoreCRM.Crm.Customers.Contracts;
using UnicoreCRM.Crm.Customers.Domain;
using UnicoreCRM.Platform.Workspace.Contracts;
using UnicoreCRM.Sales.Orders.Contracts;
namespace UnicoreCRM.Crm.Customers.Application.ResolveForAcceptedQuote;

internal sealed class Participant(IConfirmedQuoteCustomerSourceReader sources,IAcceptedQuoteContactBuyer contacts,
    IAcceptedQuoteOrganizationBuyer organizations,IWorkspaceMemberReferenceValidator members,ICustomersPersistence persistence,
    TimeProvider clock):IAcceptedQuoteCustomerParticipant
{
    internal const string Executor="system:accepted-quote-customer";
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    public async Task<AcceptedQuoteCustomerResult> ResolveAsync(string workspaceId,string orderId,CancellationToken ct)
    {
        var source=await sources.ReadAsync(workspaceId,orderId,ct);
        if(source is null||source.WorkspaceId!=workspaceId||source.OrderId!=orderId)return Failed("CONFIRMED_ACCEPTANCE_SOURCE_REQUIRED");
        if(string.IsNullOrWhiteSpace(source.OwnerId)||!await members.IsActiveMemberAsync(workspaceId,source.OwnerId,ct))return Failed("CUSTOMER_OWNER_AUTHORITY_UNAVAILABLE");
        var eligible=source.BuyerType switch {
            "CONTACT"=>await contacts.ExistsActiveAsync(workspaceId,source.BuyerId,ct),
            "ORGANIZATION_ACCOUNT"=>await organizations.ExistsActiveAsync(workspaceId,source.BuyerId,ct),_=>false};
        if(!eligible)return Failed("CUSTOMER_BUYER_AUTHORITY_UNAVAILABLE");
        var scope=Hash($"{workspaceId}\nacceptedQuoteCustomer\n{orderId}");
        var fingerprint=Hash(JsonSerializer.Serialize(source,Json));
        await using var tx=await persistence.BeginSerializableAsync(ct);
        var existing=await persistence.LoadBySubjectAsync(workspaceId,source.BuyerType,source.BuyerId,ct);
        if(existing?.Status=="ARCHIVED")return Failed("CUSTOMER_RELATIONSHIP_ARCHIVED");
        var prior=await persistence.FindIdempotencyAsync(scope,ct);
        if(prior is not null)
        {
            if(prior.Fingerprint!=fingerprint)return Failed("CUSTOMER_SOURCE_BINDING_CHANGED");
            var replay=JsonSerializer.Deserialize<AcceptedQuoteCustomerResult>(prior.ResponseJson,Json)!;
            return existing?.CustomerId==replay.CustomerId?replay:Failed("CUSTOMER_RELATIONSHIP_UNAVAILABLE");
        }
        var now=clock.GetUtcNow();var customer=existing;var resolution="REUSED";
        if(customer is null)
        {
            customer=new Customer(workspaceId,source.OwnerId,source.BuyerType,source.BuyerId,new CustomerProfile {
                CreatedFromEvidenceId=source.AcceptanceId,ConversionPolicyVersion="ACCEPTED_QUOTE_CUSTOMER_V1",
                ConversionCorrelationId=orderId,SourceSystem="ACCEPTED_QUOTE" },now);
            persistence.AddCustomer(customer);resolution="CREATED";
        }
        var result=new AcceptedQuoteCustomerResult(true,customer.CustomerId,resolution,null);
        persistence.AddAudit(new CustomerAuditRecord("resolveCustomerFromAcceptedQuote",workspaceId,Executor,customer.CustomerId,scope,orderId,customer.Version,now));
        persistence.AddOutbox(new CustomerOutboxMessage("CUSTOMER_LINKED_FROM_ACCEPTED_QUOTE",customer.CustomerId,workspaceId,orderId,
            JsonSerializer.Serialize(new{customer.CustomerId,source.QuoteId,source.AcceptanceId,source.OrderId,source.BuyerType,source.BuyerId,Executor,Resolution=resolution},Json),now));
        persistence.AddIdempotency(new CustomerIdempotencyRecord(scope,workspaceId,"resolveCustomerFromAcceptedQuote",Executor,orderId,scope,
            fingerprint,JsonSerializer.Serialize(result,Json),now));
        try{await persistence.SaveChangesAsync(ct);await tx.CommitAsync(ct);}
        catch(CustomerBusinessKeyConflictException){return Failed("CUSTOMER_CONCURRENT_RESOLUTION_RETRY");}
        catch(CustomersPersistenceConcurrencyException){return Failed("CUSTOMER_CONCURRENT_RESOLUTION_RETRY");}
        return result;
    }
    private static AcceptedQuoteCustomerResult Failed(string code)=>new(false,null,null,code);
    private static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
