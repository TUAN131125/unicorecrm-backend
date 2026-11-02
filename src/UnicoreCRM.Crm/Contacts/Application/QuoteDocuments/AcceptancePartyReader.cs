using UnicoreCRM.Crm.Contacts.Application.Common;
using UnicoreCRM.Crm.Contacts.Contracts;
using UnicoreCRM.Sales.Quotes.Contracts;
namespace UnicoreCRM.Crm.Contacts.Application.QuoteDocuments;

internal sealed class AcceptancePartyReader(ContactAuthorization authorization,IContactsPersistence contacts,
    IEnumerable<IQuoteDocumentPartyReader> buyers,TimeProvider clock):IQuoteAcceptancePartyReader
{
    public async Task<IQuoteAcceptancePartyLease> AcquireAsync(string buyerType,string buyerId,string contactId,string? relationshipId,
        string requestId,string correlationId,CancellationToken ct)
    {
        var meta=new ContactRequestMetadata(requestId,correlationId);var access=await authorization.AuthorizeAsync(meta,ContactCapabilities.Read,ct);
        if(!access.IsSuccess || !access.Value!.Authorization.CanRead("fullName") || !access.Value.Authorization.CanRead("status")
            || buyerType=="ORGANIZATION_ACCOUNT" && !access.Value.Authorization.CanRead("organizationRelationships"))return new Lease(null,null,null);
        var tx=await contacts.BeginSerializableAsync(ct);IQuoteDocumentPartyLease? organization=null;
        try
        {
            var w=access.Value.Trusted.WorkspaceId;var contact=await contacts.ReadContactAsync(w,contactId,ct);
            if(contact is null || await authorization.EnforceRecordAsync(access.Value,contact,"quoteIdentityParty",meta,ct)is not null)
            {await tx.DisposeAsync();return new Lease(null,null,null);}
            var label=ContactProjection.Document(contact).FullName;var now=clock.GetUtcNow();
            if(buyerType=="CONTACT")
            {
                if(!string.Equals(buyerId,contact.ContactId,StringComparison.OrdinalIgnoreCase) || relationshipId is not null){await tx.DisposeAsync();return new Lease(null,null,null);}
                return new Lease(tx,null,new(w,buyerType,contact.ContactId,contact.Version,contact.ContactId,contact.Version,label,null,null,null,null,null,now.UtcDateTime.ToString("O"),contact.Status!="archived"));
            }
            if(buyerType!="ORGANIZATION_ACCOUNT" || string.IsNullOrWhiteSpace(relationshipId))
            {await tx.DisposeAsync();return new Lease(null,null,null);}
            var relation=await contacts.ReadOrganizationRelationshipAsync(w,contact.ContactId,relationshipId,ct);
            if(relation is null || !string.Equals(relation.OrganizationId,buyerId,StringComparison.OrdinalIgnoreCase)){await tx.DisposeAsync();return new Lease(null,null,null);}
            var reader=buyers.SingleOrDefault(x=>x.BuyerType=="ORGANIZATION_ACCOUNT");
            if(reader is null){await tx.DisposeAsync();return new Lease(null,null,null);}
            // Match official relationship writes: Contact fence before the Organization owner fence.
            organization=await reader.AcquireAsync(buyerId,requestId,correlationId,ct);var buyer=organization.Snapshot;
            if(buyer is null){await organization.DisposeAsync();await tx.DisposeAsync();return new Lease(null,null,null);}
            return new Lease(tx,organization,new(w,buyerType,buyer.Id,buyer.Version,contact.ContactId,contact.Version,label,
                relation.RelationshipId,relation.Role,relation.IsPrimaryAffiliation,relation.EffectiveFrom.UtcDateTime.ToString("O"),relation.EffectiveTo?.UtcDateTime.ToString("O"),
                now.UtcDateTime.ToString("O"),contact.Status!="archived" && buyer.Eligible && relation.EffectiveFrom<=now && relation.EffectiveTo is null));
        }
        catch{if(organization is not null)await organization.DisposeAsync();await tx.DisposeAsync();throw;}
    }
    private sealed class Lease(IContactsTransaction? contact,IQuoteDocumentPartyLease? organization,QuoteAcceptancePartySnapshot? snapshot):IQuoteAcceptancePartyLease
    {
        public QuoteAcceptancePartySnapshot? Snapshot=>snapshot;
        public async ValueTask DisposeAsync()
        {var org=Interlocked.Exchange(ref organization,null);if(org is not null)await org.DisposeAsync();var held=Interlocked.Exchange(ref contact,null);if(held is not null)await held.DisposeAsync();}
    }
}
