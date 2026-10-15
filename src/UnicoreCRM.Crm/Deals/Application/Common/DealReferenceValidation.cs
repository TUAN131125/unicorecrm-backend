using UnicoreCRM.Crm.Contacts.Contracts;
using UnicoreCRM.Crm.Organizations.Contracts;
using UnicoreCRM.Crm.Deals.Contracts;
using UnicoreCRM.Sales.Products.Contracts;

namespace UnicoreCRM.Crm.Deals.Application.Common;

internal sealed class DealReferenceValidation(
    IContactSummaryReader contacts,
    IOrganizationSummaryReader organizations,
    IProductSnapshotReader products)
{
    internal async Task<DealOperationError?> ValidateAsync(
        DealBuyerReference? buyer,
        IReadOnlyList<string>? interestedProductIds,
        DealRequestMetadata metadata,
        CancellationToken cancellationToken)
    {
        // Request syntax remains owned by each operation's validator. Owner readers decide
        // availability under current trusted Workspace, record scope and field authority.
        if (buyer is not null && DealValidation.IsEntityId(buyer.Id))
        {
            var available = buyer.Type switch
            {
                "CONTACT" => (await contacts.ReadAsync(buyer.Id!, metadata.RequestId,
                    metadata.CorrelationId, cancellationToken)).Status == ContactSummaryReadStatus.Succeeded,
                "ORGANIZATION_ACCOUNT" => (await organizations.ReadAsync(buyer.Id!, metadata.RequestId,
                    metadata.CorrelationId, cancellationToken)).Status == OrganizationSummaryReadStatus.Succeeded,
                _ => true
            };
            if (!available)
                return DealErrors.FieldValidation(new Dictionary<string, string[]>
                { ["buyerRef"] = ["The selected buyer is not available under current access."] });
        }

        if (interestedProductIds is { Count: > 0 and <= 250 } && interestedProductIds.All(DealValidation.IsEntityId))
        {
            var result = await products.ResolveAsync(interestedProductIds, cancellationToken);
            if (!result.IsAuthorized)
                return DealErrors.AccessDenied();
            if (result.Entries.Count != interestedProductIds.Distinct(StringComparer.Ordinal).Count()
                || result.Entries.Any(entry => entry.Outcome != ProductSnapshotOutcome.Resolved))
                return DealErrors.FieldValidation(new Dictionary<string, string[]>
                { ["interestedProductIds"] = ["Select active products available under current access."] });
        }
        return null;
    }
}
