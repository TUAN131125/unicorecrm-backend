using UnicoreCRM.Operations.Tasks.Application.Common;
using UnicoreCRM.Operations.Tasks.Contracts;
using UnicoreCRM.Operations.Tasks.Domain;
using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Operations.Tasks.Application.LogActivity;

internal sealed record Command(LogActivityRequest Request, TaskCommandMetadata Metadata);

internal sealed class Handler(
    TaskAuthorization authorization,
    ITasksPersistence persistence,
    ActivityRecordAccess recordAccess,
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

        // Free-text content cannot bypass restrictive Tasks field security.
        // Referenced CRM owners govern record scope below.
        if (!TaskActivitySecurity.IsReachable(access.Value!))
            return TaskOperationResult<ActivityMutationResponse>.Failure(TaskErrors.AccessDenied());

        var trusted = access.Value!.Trusted;
        var fingerprint = TaskCommandSupport.Fingerprint(input);
        await using var transaction = await persistence.BeginSerializableAsync(cancellationToken);
        var scopeKey = TaskCommandSupport.ScopeKey(trusted, "logActivity", "WORKSPACE", command.Metadata.IdempotencyKey);
        var existing = await persistence.FindCreateIdempotencyForUpdateAsync(scopeKey, cancellationToken);
        access = await authorization.AuthorizeAsync(TaskCapabilities.Update, metadata, cancellationToken);
        if (!access.IsSuccess) return TaskOperationResult<ActivityMutationResponse>.Failure(access.Error!);
        if (access.Value!.Trusted.WorkspaceId != trusted.WorkspaceId || !TaskActivitySecurity.IsReachable(access.Value))
            return TaskOperationResult<ActivityMutationResponse>.Failure(TaskErrors.AccessDenied());
        // Receipt acquisition can wait behind another command. Resolve current record
        // authority after that wait, including on replay, before exposing any content.
        if (!await recordAccess.CanAccessAsync(access.Value, input!.References, metadata, true, cancellationToken))
            return TaskOperationResult<ActivityMutationResponse>.Failure(TaskErrors.NotFound());
        var scopes = await recordAccess.ReadScopesAsync(access.Value, metadata, cancellationToken);
        if (!await persistence.GuardActivityReferencesAsync(trusted.WorkspaceId, input.References, scopes, cancellationToken))
            return TaskOperationResult<ActivityMutationResponse>.Failure(TaskErrors.NotFound());
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
