using UnicoreCRM.Crm.Deals.Application.Common;
using UnicoreCRM.Crm.Deals.Contracts;

namespace UnicoreCRM.Crm.Deals.Application.ArchiveDeal;

internal sealed record Command(string DealId, ArchiveDealRequest Request, DealCommandMetadata Metadata);

internal sealed class Handler(DealAuthorization authorization, DealMutationExecution execution)
{
    internal async Task<DealOperationResult<DealMutationResponse>> HandleAsync(
        Command command,
        CancellationToken cancellationToken)
    {
        var metadata = new DealRequestMetadata(command.Metadata.RequestId, command.Metadata.CorrelationId);
        var access = await authorization.AuthorizeAsync(DealCapabilities.Delete, metadata, cancellationToken);
        if (!access.IsSuccess)
            return DealOperationResult<DealMutationResponse>.Failure(access.Error!);
        if (!DealValidation.IsEntityId(command.DealId))
            return DealOperationResult<DealMutationResponse>.Failure(DealErrors.NotFound());

        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var reason = DealValidation.RequiredText(command.Request.Reason, "reason", 500, fields);
        if (fields.Count != 0)
            return DealOperationResult<DealMutationResponse>.Failure(DealErrors.Validation(fields));
        var fingerprint = DealCommandSupport.Fingerprint(new { command.DealId, Reason = reason, command.Metadata.ExpectedVersion });
        return await execution.ExecuteAsync(
            access.Value!,
            "archiveDealCommand",
            "DEAL_ARCHIVED",
            command.DealId,
            command.Metadata,
            fingerprint,
            (deal, now) => deal.Archive(reason!, now) ? null : DealErrors.LifecycleConflict(deal.DealId),
            null,
            (recordAccess, record) => authorization.EnforceRecordAsync(
                recordAccess, record, "archiveDeal", metadata, cancellationToken),
            recordAccess => DealAuthorization.EnforceFieldWrite(recordAccess, "archivedAt", "archiveReason"),
            cancellationToken);
    }
}
