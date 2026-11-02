using UnicoreCRM.Crm.Contacts.Contracts;
using UnicoreCRM.Crm.Deals.Application.Common;
using UnicoreCRM.Crm.Deals.Contracts;
using UnicoreCRM.Crm.Deals.Domain;
using UnicoreCRM.Crm.Organizations.Contracts;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Sales.Quotes.Contracts;

namespace UnicoreCRM.Crm.Deals.Application.VerifyQuoteSource;

internal sealed class QuoteRelationshipVerifier(
    DealAuthorization authorization,
    IDealsPersistence persistence,
    IDealPipelinePersistence pipelinePersistence,
    IContactLinkReader contacts,
    IOrganizationSummaryReader organizations) : IQuoteRelationshipVerifier
{
    private static readonly string[] RequiredFields =
        ["id", "buyerRef", "contactId", "sourceLeadId", "ownerId", "resourceVersion", "stageCategory", "archivedAt"];
    private static readonly RecordAccessRepresentation Representation = RecordAccessRepresentation.Create(
        "deal.quoteSource", DealFieldSecurity.FieldKeys.Except(RequiredFields, StringComparer.OrdinalIgnoreCase).ToArray());

    public async Task<bool> VerifyBuyerAsync(QuoteBuyerReference buyer, string requestId, string correlationId,
        CancellationToken cancellationToken)
    {
        if (!DealValidation.IsEntityId(buyer.Id)) return false;
        if (buyer.Type == "CONTACT")
            return (await contacts.ReadAsync(buyer.Id, requestId, correlationId, cancellationToken)) is { Eligible: true };
        if (buyer.Type != "ORGANIZATION_ACCOUNT") return false;
        var result = await organizations.ReadAsync(buyer.Id, requestId, correlationId, cancellationToken);
        // A withheld lifecycle status cannot establish eligibility.
        return result.Status == OrganizationSummaryReadStatus.Succeeded && result.Summary?.Status == "active";
    }

    public async Task<IQuoteDealSourceLease> AcquireDealAsync(string dealId, long? expectedVersion, bool requireOpen,
        string requestId, string correlationId, CancellationToken cancellationToken)
    {
        var metadata = new DealRequestMetadata(requestId, correlationId);
        var access = await authorization.AuthorizeAsync(DealCapabilities.Read, metadata, cancellationToken, Representation);
        if (!access.IsSuccess || RequiredFields.Any(field => !access.Value!.Authorization.CanRead(field)))
            return new SourceLease("ACCESS_DENIED");
        if (!DealValidation.IsEntityId(dealId)) return new SourceLease("RESOURCE_NOT_FOUND");

        IDealsTransaction? transaction = null;
        try
        {
            transaction = await persistence.BeginSerializableAsync(cancellationToken);
            var workspaceId = access.Value!.Trusted.WorkspaceId;
            // Every Deal mutation uses this same aggregate command lock. Keep the owner
            // transaction alive across Quote persistence without enlisting another DbContext.
            await pipelinePersistence.LockCommandAsync($"aggregate:{workspaceId}:{dealId}", cancellationToken);
            var deal = await persistence.ReadDealAsync(workspaceId, dealId, cancellationToken);
            string? error = null;
            if (deal is null || await authorization.EnforceRecordAsync(access.Value, deal,
                    "verifyQuoteDealSource", metadata, cancellationToken) is not null)
                error = "RESOURCE_NOT_FOUND";
            else if (expectedVersion.HasValue && expectedVersion.Value != deal.Version)
                error = "VERSION_CONFLICT";
            else if (requireOpen && (deal.StageCategory != DealStageCategory.Open || deal.IsArchived))
                error = "VALIDATION_FAILED";

            if (error is not null)
            {
                await transaction.DisposeAsync();
                return new SourceLease(error);
            }

            var profile = deal!.Profile;
            return new SourceLease(null, new QuoteDealSource(deal.DealId,
                new QuoteBuyerReference(profile.BuyerRef.Type, profile.BuyerRef.Id), profile.ContactId,
                profile.SourceLeadId, profile.OwnerId, deal.Version), transaction);
        }
        catch (DealsPersistenceBusyException)
        {
            if (transaction is not null) await transaction.DisposeAsync();
            return new SourceLease("IDEMPOTENCY_REQUEST_IN_PROGRESS");
        }
        catch
        {
            if (transaction is not null) await transaction.DisposeAsync();
            throw;
        }
    }

    private sealed class SourceLease(string? errorCode, QuoteDealSource? source = null,
        IDealsTransaction? transaction = null) : IQuoteDealSourceLease
    {
        private IDealsTransaction? heldTransaction = transaction;
        public string? ErrorCode { get; } = errorCode;
        public QuoteDealSource? Source { get; } = source;
        public async ValueTask DisposeAsync()
        {
            var held = Interlocked.Exchange(ref heldTransaction, null);
            if (held is not null) await held.DisposeAsync();
        }
    }
}
