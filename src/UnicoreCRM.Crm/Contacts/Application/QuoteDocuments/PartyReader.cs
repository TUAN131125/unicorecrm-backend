using UnicoreCRM.Crm.Contacts.Contracts;
using UnicoreCRM.Crm.Contacts.Application.Common;
using UnicoreCRM.Sales.Quotes.Contracts;
namespace UnicoreCRM.Crm.Contacts.Application.QuoteDocuments;

internal sealed class PartyReader(ContactAuthorization authorization, IContactsPersistence persistence) : IQuoteDocumentPartyReader
{
    public string BuyerType => "CONTACT";
    private static readonly string[] Fields = ["fullName", "workEmail", "personalEmail", "mobilePhone", "workPhone", "otherPhone"];
    public async Task<IQuoteDocumentPartyLease> AcquireAsync(string buyerId, string requestId, string correlationId, CancellationToken ct)
    {
        var metadata = new ContactRequestMetadata(requestId, correlationId);
        var access = await authorization.AuthorizeAsync(metadata, ContactCapabilities.Read, ct);
        if (!access.IsSuccess || Fields.Any(f => !access.Value!.Authorization.CanRead(f))) return new Lease(null, null);
        var transaction = await persistence.BeginSerializableAsync(ct);
        try
        {
            var row = await persistence.ReadContactAsync(access.Value!.Trusted.WorkspaceId, buyerId, ct);
            if (row is null || await authorization.EnforceRecordAsync(access.Value, row, "quoteDocumentParty", metadata, ct) is not null)
            { await transaction.DisposeAsync(); return new Lease(null, null); }
            var value = ContactFieldSecurity.Project(ContactProjection.Document(row), access.Value.Authorization);
            return new Lease(transaction, new(BuyerType, value.Id, row.Version, value.FullName, value.WorkEmail ?? value.PersonalEmail, value.MobilePhone ?? value.WorkPhone ?? value.OtherPhone, row.Status != "archived"));
        }
        catch { await transaction.DisposeAsync(); throw; }
    }
    private sealed class Lease(IContactsTransaction? transaction, QuoteDocumentPartySnapshot? snapshot) : IQuoteDocumentPartyLease
    {
        public QuoteDocumentPartySnapshot? Snapshot { get; } = snapshot;
        public async ValueTask DisposeAsync() { var held = Interlocked.Exchange(ref transaction, null); if (held is not null) await held.DisposeAsync(); }
    }
}
