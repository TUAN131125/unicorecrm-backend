using UnicoreCRM.Crm.Deals.Application.Common;
using UnicoreCRM.Crm.Deals.Contracts;
using UnicoreCRM.Crm.Deals.Domain;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Crm.Deals.Application.CreateDeal;

internal sealed record Command(CreateDealRequest Request, DealCommandMetadata Metadata);

internal sealed class Handler(
    DealAuthorization authorization,
    IDealsPersistence persistence,
    IWorkspaceMemberReferenceValidator memberValidator,
    TimeProvider timeProvider)
{
    internal async Task<DealOperationResult<DealMutationResponse>> HandleAsync(
        Command command,
        CancellationToken cancellationToken)
    {
        var metadata = new DealRequestMetadata(command.Metadata.RequestId, command.Metadata.CorrelationId);
        var access = await authorization.AuthorizeAsync(DealCapabilities.Create, metadata, cancellationToken);
        if (!access.IsSuccess)
            return DealOperationResult<DealMutationResponse>.Failure(access.Error!);

        CreateDealValidation.TryCreate(
            command.Request,
            out var profile,
            out var stage,
            out var forecastCategory,
            out var nextActionAt,
            out var nextActionSummary,
            out var nextActionTaskId,
            out var fields);
        if (fields.Count != 0)
        {
            var productGap = fields.ContainsKey("lineItems");
            return DealOperationResult<DealMutationResponse>.Failure(
                productGap ? DealErrors.FieldValidation(fields) : DealErrors.Validation(fields));
        }
        if (stage is null)
            return DealOperationResult<DealMutationResponse>.Failure(DealErrors.StageNotFound(command.Request.StageCode!));
        if (stage.Category is not DealStageCategory.Open)
            return DealOperationResult<DealMutationResponse>.Failure(DealErrors.TerminalStageRequiresOutcome());

        var progressive = DealValidation.ProgressiveProfileErrors(profile!, stage.Code, forecastCategory);
        if (progressive.Count != 0)
            return DealOperationResult<DealMutationResponse>.Failure(DealErrors.ProgressiveProfile(progressive));

        var trusted = access.Value!.Trusted;
        if (!await memberValidator.IsActiveMemberAsync(trusted.WorkspaceId, profile!.OwnerId, cancellationToken))
            return DealOperationResult<DealMutationResponse>.Failure(DealErrors.OwnerNotAssignable());

        var fingerprint = DealCommandSupport.Fingerprint(new
        {
            profile,
            stage = stage.Code,
            forecastCategory,
            nextActionAt,
            nextActionSummary,
            nextActionTaskId
        });
        await using var transaction = await persistence.BeginSerializableAsync(cancellationToken);
        var scopeKey = DealCommandSupport.ScopeKey(trusted, "createDealCommand", "WORKSPACE", command.Metadata.IdempotencyKey);
        var existing = await persistence.FindIdempotencyAsync(scopeKey, cancellationToken);
        if (existing is not null)
        {
            var replayError = DealCommandSupport.ReplayError(existing, fingerprint);
            return replayError is null
                ? DealOperationResult<DealMutationResponse>.Success(DealCommandSupport.Replay(existing))
                : DealOperationResult<DealMutationResponse>.Failure(replayError);
        }

        var now = timeProvider.GetUtcNow();
        var deal = new Deal(trusted.WorkspaceId, profile, stage.Code, stage.Category, forecastCategory, now);
        deal.InitializeNextAction(nextActionAt, nextActionSummary, nextActionTaskId);
        persistence.AddDeal(deal);
        var response = DealCommandSupport.RecordCommit(
            persistence,
            deal,
            trusted,
            command.Metadata,
            "createDealCommand",
            "DEAL_CREATED",
            scopeKey,
            "WORKSPACE",
            fingerprint,
            null,
            now);
        await persistence.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return DealOperationResult<DealMutationResponse>.Success(response);
    }
}
