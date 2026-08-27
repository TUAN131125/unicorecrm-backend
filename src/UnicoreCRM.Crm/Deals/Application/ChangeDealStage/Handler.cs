using UnicoreCRM.Crm.Deals.Application.Common;
using UnicoreCRM.Crm.Deals.Contracts;
using UnicoreCRM.Crm.Deals.Domain;

namespace UnicoreCRM.Crm.Deals.Application.ChangeDealStage;

internal sealed record Command(string DealId, ChangeDealStageRequest Request, DealCommandMetadata Metadata);

internal sealed class Handler(DealAuthorization authorization, DealMutationExecution execution)
{
    internal async Task<DealOperationResult<DealMutationResponse>> HandleAsync(
        Command command,
        CancellationToken cancellationToken)
    {
        var metadata = new DealRequestMetadata(command.Metadata.RequestId, command.Metadata.CorrelationId);
        var access = await authorization.AuthorizeAsync(DealCapabilities.Update, metadata, cancellationToken);
        if (!access.IsSuccess)
            return DealOperationResult<DealMutationResponse>.Failure(access.Error!);
        if (!DealValidation.IsEntityId(command.DealId))
            return DealOperationResult<DealMutationResponse>.Failure(DealErrors.NotFound());

        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var stageCode = DealValidation.RequiredText(command.Request.StageCode, "stageCode", 120, fields);
        if (fields.Count != 0)
            return DealOperationResult<DealMutationResponse>.Failure(DealErrors.Validation(fields));
        if (!DealStages.TryGet(stageCode!, out var stage))
            return DealOperationResult<DealMutationResponse>.Failure(DealErrors.StageNotFound(stageCode!));
        var targetStage = stage!;
        if (targetStage.Category is not DealStageCategory.Open)
            return DealOperationResult<DealMutationResponse>.Failure(DealErrors.TerminalStageRequiresOutcome());

        var fingerprint = DealCommandSupport.Fingerprint(new { command.DealId, StageCode = targetStage.Code, command.Metadata.ExpectedVersion });
        return await execution.ExecuteAsync(
            access.Value!,
            "changeDealStageCommand",
            "DEAL_STAGE_CHANGED",
            command.DealId,
            command.Metadata,
            fingerprint,
            (deal, now) =>
            {
                var forecast = DealStages.DeriveForecast(targetStage.Code, deal.Profile.OpportunityScore);
                var progressive = DealValidation.ProgressiveProfileErrors(deal.Profile, targetStage.Code, forecast);
                if (progressive.Count != 0)
                    return DealErrors.ProgressiveProfile(progressive);
                return deal.ChangeStage(targetStage.Code, targetStage.Category, forecast, access.Value!.Trusted.MemberId, now) switch
                {
                    DealTransitionResult.Succeeded => null,
                    DealTransitionResult.InvalidTransition => DealErrors.InvalidStageTransition(deal.DealId),
                    _ => DealErrors.LifecycleConflict(deal.DealId)
                };
            },
            null,
            (recordAccess, record) => authorization.EnforceRecordAsync(
                recordAccess, record, "changeDealStage", metadata, cancellationToken),
            recordAccess => DealAuthorization.EnforceFieldWrite(recordAccess, "stageCode", "stageCategory", "stageEnteredAt"),
            cancellationToken);
    }
}
