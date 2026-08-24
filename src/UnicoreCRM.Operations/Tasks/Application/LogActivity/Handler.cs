using UnicoreCRM.Operations.Tasks.Application.Common;
using UnicoreCRM.Operations.Tasks.Contracts;
using UnicoreCRM.Operations.Tasks.Domain;

namespace UnicoreCRM.Operations.Tasks.Application.LogActivity;

internal sealed record Command(LogActivityRequest Request, TaskCommandMetadata Metadata);

internal sealed class Handler(
    TaskAuthorization authorization,
    ITasksPersistence persistence,
    TimeProvider timeProvider)
{
    internal async Task<TaskOperationResult<ActivityMutationResponse>> HandleAsync(Command command, CancellationToken cancellationToken)
    {
        var access = await authorization.AuthorizeAsync(TaskCapabilities.Update, command.Metadata.CorrelationId, cancellationToken);
        if (!access.IsSuccess)
            return TaskOperationResult<ActivityMutationResponse>.Failure(access.Error!);
        if (!LogActivityValidation.TryActivity(command.Request, out var input, out var fields))
            return TaskOperationResult<ActivityMutationResponse>.Failure(TaskErrors.Validation(fields));
        var trusted = access.Value!;
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
