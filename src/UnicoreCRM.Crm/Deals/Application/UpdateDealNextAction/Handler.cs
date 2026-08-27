using UnicoreCRM.Crm.Deals.Application.Common;
using UnicoreCRM.Crm.Deals.Contracts;

namespace UnicoreCRM.Crm.Deals.Application.UpdateDealNextAction;

internal sealed record Command(string DealId, UpdateDealNextActionRequest Request, DealCommandMetadata Metadata);

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
        var nextActionAt = DealValidation.RequiredUtc(command.Request.NextActionAt, "nextActionAt", fields);
        var summary = DealValidation.OptionalText(command.Request.NextActionSummary, "nextActionSummary", 1000, fields);
        var taskId = DealValidation.OptionalEntity(command.Request.TaskId, "taskId", fields);
        if (fields.Count != 0)
            return DealOperationResult<DealMutationResponse>.Failure(DealErrors.Validation(fields));

        var fingerprint = DealCommandSupport.Fingerprint(new
        {
            command.DealId,
            NextActionAt = nextActionAt,
            Summary = summary,
            TaskId = taskId,
            command.Metadata.ExpectedVersion
        });
        return await execution.ExecuteAsync(
            access.Value!,
            "updateDealNextAction",
            "DEAL_NEXT_ACTION_UPDATED",
            command.DealId,
            command.Metadata,
            fingerprint,
            (deal, now) => deal.UpdateNextAction(nextActionAt!.Value, summary, taskId, now)
                ? null
                : DealErrors.LifecycleConflict(deal.DealId),
            null,
            (recordAccess, record) => authorization.EnforceRecordAsync(
                recordAccess, record, "updateDealNextAction", metadata, cancellationToken, "nextActionAt", "nextActionSummary", "nextActionRef"),
            cancellationToken);
    }
}
