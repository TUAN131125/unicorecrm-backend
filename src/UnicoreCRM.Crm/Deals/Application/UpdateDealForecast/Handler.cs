using UnicoreCRM.Crm.Deals.Application.Common;
using UnicoreCRM.Crm.Deals.Contracts;

namespace UnicoreCRM.Crm.Deals.Application.UpdateDealForecast;

internal sealed record Command(string DealId, UpdateDealForecastRequest Request, DealCommandMetadata Metadata);

internal sealed class Handler(DealAuthorization authorization, DealMutationExecution execution)
{
    internal async Task<DealOperationResult<DealMutationResponse>> HandleAsync(
        Command command,
        CancellationToken cancellationToken)
    {
        var access = await authorization.AuthorizeAsync(DealCapabilities.Update, command.Metadata.CorrelationId, cancellationToken);
        if (!access.IsSuccess)
            return DealOperationResult<DealMutationResponse>.Failure(access.Error!);
        if (!DealValidation.IsEntityId(command.DealId))
            return DealOperationResult<DealMutationResponse>.Failure(DealErrors.NotFound());

        var fingerprint = DealCommandSupport.Fingerprint(new { command.DealId, command.Request, command.Metadata.ExpectedVersion });
        return await execution.ExecuteAsync(
            access.Value!,
            "updateDealForecast",
            "DEAL_FORECAST_UPDATED",
            command.DealId,
            command.Metadata,
            fingerprint,
            (deal, now) =>
            {
                if (!UpdateDealForecastValidation.TryForecast(
                        command.Request,
                        deal,
                        out var closeDate,
                        out var score,
                        out var category,
                        out var fields))
                    return DealErrors.Validation(fields);
                var profile = deal.Profile with { ExpectedCloseDate = closeDate, OpportunityScore = score };
                var progressive = DealValidation.ProgressiveProfileErrors(profile, deal.StageCode, category);
                if (progressive.Count != 0)
                    return DealErrors.ProgressiveProfile(progressive);
                return deal.UpdateForecast(closeDate, score, category, access.Value!.MemberId, now)
                    ? null
                    : DealErrors.LifecycleConflict(deal.DealId);
            },
            null,
            cancellationToken);
    }
}
