using UnicoreCRM.Crm.Deals.Application.Common;
using UnicoreCRM.Crm.Deals.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Deals.Application.AssignDealOwner;

internal sealed record Command(string DealId, AssignDealOwnerRequest Request, DealCommandMetadata Metadata);

internal sealed class Handler(
    DealAuthorization authorization,
    DealMutationExecution execution,
    IWorkspaceMemberReferenceValidator memberValidator)
{
    internal async Task<DealOperationResult<DealMutationResponse>> HandleAsync(
        Command command,
        CancellationToken cancellationToken)
    {
        var metadata = new DealRequestMetadata(command.Metadata.RequestId, command.Metadata.CorrelationId);
        var access = await authorization.AuthorizeAsync(DealCapabilities.Assign, metadata, cancellationToken);
        if (!access.IsSuccess)
            return DealOperationResult<DealMutationResponse>.Failure(access.Error!);
        if (!DealValidation.IsEntityId(command.DealId))
            return DealOperationResult<DealMutationResponse>.Failure(DealErrors.NotFound());

        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var ownerId = DealValidation.OptionalEntity(command.Request.OwnerId, "ownerId", fields);
        if (ownerId is null)
            fields["ownerId"] = ["ownerId is required."];
        var reason = DealValidation.OptionalText(command.Request.Reason, "reason", 1000, fields);
        if (fields.Count != 0)
            return DealOperationResult<DealMutationResponse>.Failure(DealErrors.Validation(fields));

        var fingerprint = DealCommandSupport.Fingerprint(new { command.DealId, OwnerId = ownerId, Reason = reason, command.Metadata.ExpectedVersion });
        return await execution.ExecuteAsync(
            access.Value!,
            "assignDealOwner",
            "DEAL_OWNER_ASSIGNED",
            command.DealId,
            command.Metadata,
            fingerprint,
            (deal, now) => deal.AssignOwner(ownerId!, now) ? null : DealErrors.LifecycleConflict(deal.DealId),
            async (trusted, token) => await memberValidator.IsActiveMemberAsync(trusted.WorkspaceId, ownerId!, token)
                ? null
                : DealErrors.OwnerNotAssignable(),
            (recordAccess, record) => authorization.EnforceRecordAsync(
                recordAccess, record, "assignDealOwner", metadata, cancellationToken),
            recordAccess => DealAuthorization.EnforceFieldWrite(recordAccess, "ownerId"),
            cancellationToken);
    }
}
