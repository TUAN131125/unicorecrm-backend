using UnicoreCRM.Crm.Deals.Application.Common;
using UnicoreCRM.Crm.Deals.Contracts;
using UnicoreCRM.Operations.Tasks.Contracts;

namespace UnicoreCRM.Crm.Deals.Application.ValidateTaskReference;

internal sealed class DealTaskReferenceAuthority(DealAuthorization authorization, IDealsPersistence persistence)
    : IDealTaskReferenceAuthority
{
    public async Task<DealTaskReferenceStatus> ValidateAsync(string workspaceId, string dealId, bool allowArchived,
        string requestId, string correlationId, CancellationToken cancellationToken)
    {
        var metadata = new DealRequestMetadata(requestId, correlationId);
        var access = await authorization.AuthorizeAsync(DealCapabilities.Read, metadata, cancellationToken);
        if (!access.IsSuccess) return DealTaskReferenceStatus.AccessDenied;
        if (access.Value!.Trusted.WorkspaceId != workspaceId || !DealValidation.IsEntityId(dealId))
            return DealTaskReferenceStatus.NotFound;
        var deal = await persistence.ReadDealAsync(workspaceId, dealId, cancellationToken);
        if (deal is null || await authorization.EnforceRecordAsync(access.Value, deal, "validateTaskDealReference", metadata, cancellationToken) is not null)
            return DealTaskReferenceStatus.NotFound;
        // This is a validation-point decision, not a cross-owner lifecycle lock.
        return !allowArchived && deal.ArchivedAt is not null
            ? DealTaskReferenceStatus.Ineligible : DealTaskReferenceStatus.Available;
    }
}
