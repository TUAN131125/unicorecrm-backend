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
        var access = await authorization.AuthorizeAsync(DealCapabilities.Read, query.CorrelationId, cancellationToken);
        if (!access.IsSuccess)
            return DealOperationResult<DealReadModel>.Failure(access.Error!);
        if (!DealValidation.IsEntityId(query.DealId))
            return DealOperationResult<DealReadModel>.Failure(DealErrors.Validation(
                new Dictionary<string, string[]> { ["dealId"] = ["dealId is not a valid entity identifier."] }));
        var trusted = access.Value!;
        var deal = await persistence.ReadDealAsync(trusted.WorkspaceId, query.DealId, cancellationToken);
        if (deal is null)
            return DealOperationResult<DealReadModel>.Failure(DealErrors.NotFound());
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
        return DealOperationResult<DealReadModel>.Success(DealProjection.Document(deal));
    }
}
