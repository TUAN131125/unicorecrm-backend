using UnicoreCRM.Crm.Deals.Application.Common;
using UnicoreCRM.Crm.Deals.Contracts;

namespace UnicoreCRM.Crm.Deals.Application.MarkDealLost;

internal sealed record Command(string DealId, MarkDealLostRequest Request, DealCommandMetadata Metadata);

internal sealed class Handler(DealAuthorization authorization, DealMutationExecution execution)
{
    internal async Task<DealOperationResult<DealMutationResponse>> HandleAsync(
        Command command,
        CancellationToken cancellationToken)
    {
        var metadata = new DealRequestMetadata(command.Metadata.RequestId, command.Metadata.CorrelationId);
        var access = await authorization.AuthorizeAsync(DealCapabilities.Close, metadata, cancellationToken);
        if (!access.IsSuccess)
            return DealOperationResult<DealMutationResponse>.Failure(access.Error!);
        if (!DealValidation.IsEntityId(command.DealId))
            return DealOperationResult<DealMutationResponse>.Failure(DealErrors.NotFound());
        if (!MarkDealLostValidation.TryLost(
                command.Request,
                out var reason,
                out var note,
                out var recycleDecision,
                out var revisitAt,
                out var semanticError))
            return DealOperationResult<DealMutationResponse>.Failure(semanticError!);

        var fingerprint = DealCommandSupport.Fingerprint(new
        {
            command.DealId,
            Reason = reason,
            Note = note,
            RecycleDecision = recycleDecision,
            RevisitAt = revisitAt,
            command.Metadata.ExpectedVersion
        });
        return await execution.ExecuteAsync(
            access.Value!,
            "markDealLostCommand",
            "DEAL_MARKED_LOST",
            command.DealId,
            command.Metadata,
            fingerprint,
            (deal, now) => deal.MarkLost(reason!, note, recycleDecision, revisitAt, now)
                ? null
                : DealErrors.LifecycleConflict(deal.DealId),
            null,
            (recordAccess, record) => authorization.EnforceRecordAsync(
                recordAccess, record, "markDealLost", metadata, cancellationToken, "stageCode", "stageCategory", "lostAt", "lostReason", "lostReasonNote", "actualCloseDate"),
            cancellationToken);
    }
}
