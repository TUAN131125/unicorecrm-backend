using UnicoreCRM.Crm.Deals.Application.Common;
using UnicoreCRM.Crm.Deals.Contracts;

namespace UnicoreCRM.Crm.Deals.Application.MarkDealWon;

internal sealed record Command(string DealId, MarkDealWonRequest Request, DealCommandMetadata Metadata);

internal sealed class Handler(DealAuthorization authorization, IDealsPersistence persistence)
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
        if (!MarkDealWonValidation.TryWinEvidence(
                command.Request.Evidence,
                out _,
                out _,
                out _,
                out var fields))
            return DealOperationResult<DealMutationResponse>.Failure(DealErrors.WinEvidenceInvalid(fields));

        var deal = await persistence.ReadDealAsync(access.Value!.Trusted.WorkspaceId, command.DealId, cancellationToken);
        if (deal is null)
            return DealOperationResult<DealMutationResponse>.Failure(DealErrors.NotFound());
        var denied = await authorization.EnforceRecordAsync(access.Value, deal, "markDealWon", metadata, cancellationToken);
        if (denied is not null)
            return DealOperationResult<DealMutationResponse>.Failure(denied);
        var fieldDenied = DealAuthorization.EnforceFieldWrite(access.Value,
            "stageCode", "stageCategory", "wonAt", "winEvidence", "actualCloseDate");
        if (fieldDenied is not null)
            return DealOperationResult<DealMutationResponse>.Failure(fieldDenied);
        if (command.Metadata.ExpectedVersion is not { } expectedVersion)
            return DealOperationResult<DealMutationResponse>.Failure(DealErrors.Validation(
                new Dictionary<string, string[]> { ["If-Match"] = ["A resource version is required."] }));
        if (deal.Version != expectedVersion)
            return DealOperationResult<DealMutationResponse>.Failure(
                DealErrors.VersionConflict(deal.DealId, expectedVersion, deal.Version));

        // Neither source owner currently exposes an admitted commitment verifier. A payload's
        // shape cannot prove existence, workspace/deal linkage, state, validity or occurrence time.
        // Refuse both branches before mutation or replay: historical command evidence was also
        // committed without source verification and cannot serve as a trusted success response.
        return DealOperationResult<DealMutationResponse>.Failure(DealErrors.WinEvidenceInvalid(
            new Dictionary<string, string[]>
            {
                ["evidence"] = ["Authoritative Quote acceptance or Order confirmation verification is unavailable."]
            }));
    }
}
