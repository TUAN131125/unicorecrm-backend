using UnicoreCRM.Crm.Deals.Application.Common;
using UnicoreCRM.Crm.Deals.Contracts;

namespace UnicoreCRM.Crm.Deals.Application.MarkDealWon;

internal sealed record Command(string DealId, MarkDealWonRequest Request, DealCommandMetadata Metadata);

internal sealed class Handler(DealAuthorization authorization, DealMutationExecution execution)
{
    internal async Task<DealOperationResult<DealMutationResponse>> HandleAsync(
        Command command,
        CancellationToken cancellationToken)
    {
        var access = await authorization.AuthorizeAsync(DealCapabilities.Close, command.Metadata.CorrelationId, cancellationToken);
        if (!access.IsSuccess)
            return DealOperationResult<DealMutationResponse>.Failure(access.Error!);
        if (!DealValidation.IsEntityId(command.DealId))
            return DealOperationResult<DealMutationResponse>.Failure(DealErrors.NotFound());
        if (!MarkDealWonValidation.TryWinEvidence(
                command.Request.Evidence,
                out var evidenceType,
                out var sourceId,
                out var occurredAt,
                out var fields))
            return DealOperationResult<DealMutationResponse>.Failure(DealErrors.WinEvidenceInvalid(fields));

        var fingerprint = DealCommandSupport.Fingerprint(new
        {
            command.DealId,
            EvidenceType = evidenceType,
            SourceId = sourceId,
            OccurredAt = occurredAt,
            command.Metadata.ExpectedVersion
        });
        return await execution.ExecuteAsync(
            access.Value!,
            "markDealWonCommand",
            "DEAL_MARKED_WON",
            command.DealId,
            command.Metadata,
            fingerprint,
            (deal, now) => deal.MarkWon(evidenceType!, sourceId!, occurredAt, now)
                ? null
                : DealErrors.WonTransitionBlocked(deal.DealId),
            null,
            cancellationToken);
    }
}
