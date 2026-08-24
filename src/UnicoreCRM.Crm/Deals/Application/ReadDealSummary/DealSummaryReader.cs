using UnicoreCRM.Crm.Deals.Application.Common;
using UnicoreCRM.Crm.Deals.Contracts;
using UnicoreCRM.Crm.Deals.Domain;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Deals.Application.ReadDealSummary;

internal sealed class DealSummaryReader(
    ICurrentWorkspace currentWorkspace,
    IAccessAuthorizer accessAuthorizer,
    IDealsPersistence persistence,
    TimeProvider timeProvider) : IDealSummaryReader
{
    public async Task<DealSummaryReadResult> ReadAsync(
        string dealId,
        string requestId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (!currentWorkspace.IsResolved)
            return new(DealSummaryReadStatus.WorkspaceMismatch);

        var access = await accessAuthorizer.AuthorizeAsync(DealCapabilities.Read, correlationId, cancellationToken);
        if (!access.IsAllowed)
        {
            return new(access.Code == "WORKSPACE_MISMATCH"
                ? DealSummaryReadStatus.WorkspaceMismatch
                : DealSummaryReadStatus.AccessDenied);
        }

        if (!DealValidation.IsEntityId(dealId))
            return new(DealSummaryReadStatus.InvalidReference);

        var trusted = currentWorkspace.Require();
        var deal = await persistence.ReadDealAsync(trusted.WorkspaceId, dealId, cancellationToken);
        if (deal is null || !CanReadRecord(access.Context!, trusted.MemberId, deal.Profile.OwnerId))
            return new(DealSummaryReadStatus.NotFound);

        var document = DealProjection.Document(deal);
        var summary = new DealSummaryProjection(
            deal.DealId,
            Visible(access.Context!, "name") ? document.Name : null,
            Visible(access.Context!, "stageCode") ? document.StageCode : null,
            Visible(access.Context!, "stageCategory") ? document.StageCategory : null,
            Visible(access.Context!, "opportunityScore") ? document.OpportunityScore : null,
            Visible(access.Context!, "expectedCloseDate") ? document.ExpectedCloseDate : null,
            Visible(access.Context!, "nextActionAt") ? document.NextActionAt : null,
            Visible(access.Context!, "nextActionSummary") ? document.NextActionSummary : null);

        persistence.AddAudit(new DealAuditRecord(
            "readDealSummary",
            trusted.WorkspaceId,
            trusted.MemberId,
            deal.DealId,
            requestId,
            correlationId,
            "READ",
            deal.Version,
            deal.Version,
            timeProvider.GetUtcNow()));
        await persistence.SaveChangesAsync(cancellationToken);
        return new(DealSummaryReadStatus.Succeeded, summary);
    }

    private static bool CanReadRecord(AuthorizationContextDocument context, string memberId, string ownerId)
    {
        var scope = context.DataScopes.FirstOrDefault(item =>
            string.Equals(item.ResourceKey, "deals", StringComparison.OrdinalIgnoreCase));
        return scope?.Scope.ToUpperInvariant() switch
        {
            null or "WORKSPACE" => true,
            "OWN" => string.Equals(memberId, ownerId, StringComparison.Ordinal),
            _ => false
        };
    }

    private static bool Visible(AuthorizationContextDocument context, string fieldKey)
    {
        var field = context.FieldSecurity.FirstOrDefault(item =>
            string.Equals(item.ResourceKey, "deals", StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.FieldKey, fieldKey, StringComparison.OrdinalIgnoreCase));
        return field is null || field.Access is "READ_ONLY" or "READ_WRITE";
    }
}
