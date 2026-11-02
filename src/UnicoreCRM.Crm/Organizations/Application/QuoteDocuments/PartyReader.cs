using UnicoreCRM.Crm.Organizations.Contracts;
using UnicoreCRM.Crm.Organizations.Application.Common;
using UnicoreCRM.Sales.Quotes.Contracts;
namespace UnicoreCRM.Crm.Organizations.Application.QuoteDocuments;

internal sealed class PartyReader(OrganizationAuthorization authorization, IOrganizationsPersistence persistence) : IQuoteDocumentPartyReader
{
    public string BuyerType => "ORGANIZATION_ACCOUNT";
    private static readonly string[] Fields = ["displayName", "email", "phone"];
    public async Task<IQuoteDocumentPartyLease> AcquireAsync(string buyerId, string requestId, string correlationId, CancellationToken ct)
    {
        var metadata = new OrganizationRequestMetadata(requestId, correlationId);
        var access = await authorization.AuthorizeAsync(metadata, OrganizationCapabilities.Read, ct);
        if (!access.IsSuccess || Fields.Any(f => !access.Value!.Authorization.CanRead(f))) return new Lease(null, null);
        var transaction = await persistence.BeginSerializableAsync(ct);
        try
        {
            var row = await persistence.ReadOrganizationAsync(access.Value!.Trusted.WorkspaceId, buyerId, ct);
            if (row is null || await authorization.EnforceRecordAsync(access.Value, row, "quoteDocumentParty", metadata, ct) is not null)
            { await transaction.DisposeAsync(); return new Lease(null, null); }
            var value = OrganizationFieldSecurity.Project(OrganizationProjection.Document(row), access.Value.Authorization);
            return new Lease(transaction, new(BuyerType, value.Id, row.Version, value.DisplayName, value.Email, value.Phone, row.Status != "archived"));
        }
        catch { await transaction.DisposeAsync(); throw; }
    }
    private sealed class Lease(IOrganizationsTransaction? transaction, QuoteDocumentPartySnapshot? snapshot) : IQuoteDocumentPartyLease
    {
        public QuoteDocumentPartySnapshot? Snapshot { get; } = snapshot;
        public async ValueTask DisposeAsync() { var held = Interlocked.Exchange(ref transaction, null); if (held is not null) await held.DisposeAsync(); }
    }
}
