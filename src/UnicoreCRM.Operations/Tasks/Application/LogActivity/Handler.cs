using UnicoreCRM.Operations.Tasks.Application.Common;
using UnicoreCRM.Operations.Tasks.Contracts;
using UnicoreCRM.Operations.Tasks.Domain;
using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Operations.Tasks.Application.LogActivity;

internal sealed record Command(LogActivityRequest Request, TaskCommandMetadata Metadata);

internal sealed class Handler(
    TaskAuthorization authorization,
    ITasksPersistence persistence,
    TimeProvider timeProvider)
{
    internal async Task<TaskOperationResult<ActivityMutationResponse>> HandleAsync(Command command, CancellationToken cancellationToken)
    {
        var metadata = new TaskRequestMetadata(command.Metadata.RequestId, command.Metadata.CorrelationId);
        var access = await authorization.AuthorizeAsync(TaskCapabilities.Update, metadata, cancellationToken);
        if (!access.IsSuccess)
            return TaskOperationResult<ActivityMutationResponse>.Failure(access.Error!);
        if (!LogActivityValidation.TryActivity(command.Request, out var input, out var fields))
            return TaskOperationResult<ActivityMutationResponse>.Failure(TaskErrors.Validation(fields));

        // TaskActivity is an AUTHORITY_GAP for record access, so it fails closed outside WORKSPACE
        // scope. No current authority settles whether an Activity is inside the `tasks` record
        // scope: a TaskActivity carries no task reference, and its `actorId` is the actor, not one of
        // the admitted ownership attributes (`ownerId`, `assigneeId`, `createdBy`, `assignedTo`), so
        // it has no owner an OWN, TEAM or CUSTOM scope could be evaluated against. Activities are
        // also Workspace-wide and carry subject, body, actor and record references for every module,
        // so treating a restricted scope as unrestricted would leak Workspace-wide activity to a
        // caller whose Task records are restricted. Until the scope fact is frozen, only a caller
        // whose effective `tasks` scope is WORKSPACE reaches Activities at all.
        if (access.Value!.Authorization.ScopeFilter != RecordAccessScopeFilter.Workspace)
            return TaskOperationResult<ActivityMutationResponse>.Failure(TaskErrors.AccessDenied());

        var trusted = access.Value!.Trusted;
        var fingerprint = TaskCommandSupport.Fingerprint(input);
        await using var transaction = await persistence.BeginSerializableAsync(cancellationToken);
        var scopeKey = TaskCommandSupport.ScopeKey(trusted, "logActivity", "WORKSPACE", command.Metadata.IdempotencyKey);
        var existing = await persistence.FindIdempotencyAsync(scopeKey, cancellationToken);
        if (existing is not null)
        {
            var replayError = TaskCommandSupport.ReplayError(existing, fingerprint);
            return replayError is null
                ? TaskOperationResult<ActivityMutationResponse>.Success(TaskCommandSupport.ReplayActivity(existing))
                : TaskOperationResult<ActivityMutationResponse>.Failure(replayError);
        }
        var now = timeProvider.GetUtcNow();
        var activity = new TaskActivity(
            trusted.WorkspaceId,
            input!.Type,
            input.Subject,
            input.Body,
            trusted.MemberId,
            input.References,
            now);
        persistence.AddActivity(activity);
        var response = TaskCommandSupport.RecordActivityCommit(
            persistence,
            activity,
            trusted,
            command.Metadata,
            scopeKey,
            fingerprint,
            now);
        await persistence.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return TaskOperationResult<ActivityMutationResponse>.Success(response);
    }
}
