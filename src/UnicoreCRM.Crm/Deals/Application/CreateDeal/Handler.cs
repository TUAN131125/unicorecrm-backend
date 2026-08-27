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
            // Answered from stored evidence alone, so an owner deactivated after the original commit
            // cannot retroactively invalidate the replay or create a second Deal.
            var replayError = DealCommandSupport.ReplayError(existing, fingerprint);
            return replayError is null
                ? DealOperationResult<DealMutationResponse>.Success(Project(DealCommandSupport.Replay(existing), access.Value!))
                : DealOperationResult<DealMutationResponse>.Failure(replayError);
        }

        // Creation is a resource-level question, so no record scope applies, but field security
        // still does: a field the caller may not write must not be written on the way in either. It
        // follows the replay branch, so a committed creation stays replayable after a field turns
        // READ_ONLY or HIDDEN - the replay writes nothing.
        var createWriteError = DealFieldSecurity.GuardCreateWrite(
            access.Value!.Authorization, profile!, nextActionAt, nextActionSummary, nextActionTaskId);
        if (createWriteError is not null)
            return DealOperationResult<DealMutationResponse>.Failure(createWriteError);

        // Only a genuinely new command evaluates current mutable owner/member state.
        if (!await memberValidator.IsActiveMemberAsync(trusted.WorkspaceId, profile!.OwnerId, cancellationToken))
            return DealOperationResult<DealMutationResponse>.Failure(DealErrors.OwnerNotAssignable());

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
        return DealOperationResult<DealMutationResponse>.Success(Project(response, access.Value!));
    }

    private static DealMutationResponse Project(DealMutationResponse response, DealAccess access) =>
        response with
        {
            Result = new DealMutationResult(DealFieldSecurity.Project(response.Result.Deal, access.Authorization))
        };
}
