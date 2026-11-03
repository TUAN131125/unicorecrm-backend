using UnicoreCRM.Crm.Deals.Application.Common;
using UnicoreCRM.Crm.Deals.Contracts;
using UnicoreCRM.Crm.Deals.Domain;
using UnicoreCRM.Sales.Orders.Contracts;

namespace UnicoreCRM.Crm.Deals.Application.MarkDealWon;

internal sealed record Command(string DealId, MarkDealWonRequest Request, DealCommandMetadata Metadata);

internal sealed class Handler(DealAuthorization authorization, IDealsPersistence persistence,
    DealMutationExecution execution, IDealPipelinePersistence pipelines, IDealOrderCommitmentVerifier commitments)
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
                out var type,
                out var sourceId,
                out var reportedAt,
                out var fields))
            return DealOperationResult<DealMutationResponse>.Failure(DealErrors.WinEvidenceInvalid(fields));

        var deal = await persistence.ReadDealAsync(access.Value!.Trusted.WorkspaceId, command.DealId, cancellationToken);
        if (deal is null)
            return DealOperationResult<DealMutationResponse>.Failure(DealErrors.NotFound());
        var denied = await authorization.EnforceRecordAsync(access.Value, deal, "markDealWon", metadata, cancellationToken);
        if (denied is not null)
            return DealOperationResult<DealMutationResponse>.Failure(denied);
        if (command.Metadata.ExpectedVersion is not { } expectedVersion)
            return DealOperationResult<DealMutationResponse>.Failure(DealErrors.Validation(
                new Dictionary<string, string[]> { ["If-Match"] = ["A resource version is required."] }));
        // Quote-only proof remains unavailable until a Workspace policy authorizes that path.
        if (type != "ORDER_CONFIRMED")
        {
            var fieldDenied = DealAuthorization.EnforceFieldWrite(access.Value,
                "stageCode", "stageCategory", "wonAt", "winEvidence", "actualCloseDate");
            if (fieldDenied is not null) return DealOperationResult<DealMutationResponse>.Failure(fieldDenied);
            if (deal.Version != expectedVersion) return DealOperationResult<DealMutationResponse>.Failure(
                DealErrors.VersionConflict(deal.DealId, expectedVersion, deal.Version));
            return DealOperationResult<DealMutationResponse>.Failure(InvalidEvidence());
        }
        var fingerprint = DealCommandSupport.Fingerprint(new
        {
            command.DealId, Type = type, SourceId = sourceId, ReportedAt = reportedAt,
            ExpectedVersion = expectedVersion, CommitmentVerificationVersion = 1
        });
        IDealOrderCommitmentLease? lease = null;
        try
        {
            return await execution.ExecuteAsync(access.Value, "markDealWonCommand", "DEAL_MARKED_WON", command.DealId,
                command.Metadata, fingerprint,
                (current, now) =>
                {
                    if (lease?.Commitment is not { } proof || proof.OrderId != sourceId || proof.ConfirmedAt > now
                        || current.Profile.BuyerRef != deal.Profile.BuyerRef) return InvalidEvidence();
                    return current.MarkWon(type, proof.OrderId, proof.ConfirmedAt, now) ? null : DealErrors.LifecycleConflict(current.DealId);
                },
                async (trusted, ct) =>
                {
                    lease = await commitments.AcquireAsync(trusted.WorkspaceId, sourceId!, deal.DealId,
                        deal.Profile.BuyerRef.Type, deal.Profile.BuyerRef.Id, ct);
                    return lease.Commitment is null ? InvalidEvidence() : null;
                },
                (grant, current) => authorization.EnforceRecordAsync(grant, current, "markDealWon", metadata, cancellationToken),
                grant => DealAuthorization.EnforceFieldWrite(grant, "stageCode", "stageCategory", "wonAt", "winEvidence", "actualCloseDate"),
                cancellationToken,
                async (current, ct) =>
                {
                    var configured = await pipelines.LoadPipelinesAsync(current.WorkspaceId, ct);
                    var pipeline = configured.SingleOrDefault(p => current.PipelineId is null ? p.IsDefault : p.PipelineId == current.PipelineId);
                    return pipeline is { IsActive: true } && pipeline.Stages.Any(s => s.IsActive && s.Code == "WON" && s.Category == DealStageCategory.Won)
                        ? null : DealErrors.StageNotFound("WON");
                });
        }
        finally { if (lease is not null) await lease.DisposeAsync(); }
    }

    private static DealOperationError InvalidEvidence() => DealErrors.WinEvidenceInvalid(new Dictionary<string, string[]>
    { ["evidence"] = ["A confirmed Order bound to this Deal, Buyer and valid accepted Quote is required."] });
}
