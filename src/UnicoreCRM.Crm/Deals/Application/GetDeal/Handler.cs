using UnicoreCRM.Crm.Deals.Application.Common;
using UnicoreCRM.Crm.Deals.Contracts;
using UnicoreCRM.Crm.Deals.Domain;

namespace UnicoreCRM.Crm.Deals.Application.GetDeal;

internal sealed record Query(string DealId, string RequestId, string CorrelationId);

internal sealed class Handler(
    DealAuthorization authorization,
    IDealsPersistence persistence,
    TimeProvider timeProvider)
{
    internal async Task<DealOperationResult<DealReadModel>> HandleAsync(Query query, CancellationToken cancellationToken)
    {
        var metadata = new DealRequestMetadata(query.RequestId, query.CorrelationId);
        var access = await authorization.AuthorizeAsync(DealCapabilities.Read, metadata, cancellationToken);
        if (!access.IsSuccess)
            return DealOperationResult<DealReadModel>.Failure(access.Error!);
        if (!DealValidation.IsEntityId(query.DealId))
            return DealOperationResult<DealReadModel>.Failure(DealErrors.Validation(
                new Dictionary<string, string[]> { ["dealId"] = ["dealId is not a valid entity identifier."] }));
        var trusted = access.Value!.Trusted;
        var deal = await persistence.ReadDealAsync(trusted.WorkspaceId, query.DealId, cancellationToken);
        if (deal is null)
            return DealOperationResult<DealReadModel>.Failure(DealErrors.NotFound());

        // Record scope is enforced here, not left to the consumer. A deal inside the trusted
        // Workspace but outside the caller's record scope is reported as not found.
        var denied = await authorization.EnforceRecordAsync(access.Value!, deal, "getDeal", metadata, cancellationToken);
        if (denied is not null)
            return DealOperationResult<DealReadModel>.Failure(denied);

        persistence.AddAudit(new DealAuditRecord(
            "getDeal",
            trusted.WorkspaceId,
            trusted.MemberId,
            deal.DealId,
            query.RequestId,
            query.CorrelationId,
            "READ",
            deal.Version,
            deal.Version,
            timeProvider.GetUtcNow()));
        await persistence.SaveChangesAsync(cancellationToken);
        return DealOperationResult<DealReadModel>.Success(
            DealFieldSecurity.Project(DealProjection.Document(deal), access.Value!.Authorization));
    }
}
