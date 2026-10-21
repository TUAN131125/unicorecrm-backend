using UnicoreCRM.Operations.Tasks.Application.Common;
using UnicoreCRM.Operations.Tasks.Contracts;
using UnicoreCRM.Operations.Tasks.Domain;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Operations.Tasks.Application.CreateTask;

internal sealed record Command(CreateTaskRequest Request, TaskCommandMetadata Metadata);

internal sealed class Handler(
    TaskAuthorization authorization,
    ITasksPersistence persistence,
    IWorkspaceMemberReferenceValidator memberValidator,
    IDealTaskReferenceAuthority dealReferences,
    IContactTaskReferenceAuthority contactReferences,
    IOrganizationTaskReferenceAuthority organizationReferences,
    ICustomerTaskReferenceAuthority customerReferences,
    TimeProvider timeProvider)
{
    internal async Task<TaskOperationResult<TaskMutationResponse>> HandleAsync(Command command, CancellationToken cancellationToken)
    {
        var metadata = new TaskRequestMetadata(command.Metadata.RequestId, command.Metadata.CorrelationId);
        var access = await authorization.AuthorizeAsync(TaskCapabilities.Create, metadata, cancellationToken);
        if (!access.IsSuccess)
            return TaskOperationResult<TaskMutationResponse>.Failure(access.Error!);
        if (!CreateTaskValidation.TryCreate(command.Request, out var input, out var fields))
            return TaskOperationResult<TaskMutationResponse>.Failure(TaskErrors.Validation(fields));

        var trusted = access.Value!.Trusted;
        if (string.Equals(input!.References.RecordModuleKey, "deals", StringComparison.OrdinalIgnoreCase))
        {
            var reference = await dealReferences.ValidateAsync(trusted.WorkspaceId, input.References.RecordId!, true,
                metadata.RequestId, metadata.CorrelationId, cancellationToken);
            if (reference != DealTaskReferenceStatus.Available)
                return TaskOperationResult<TaskMutationResponse>.Failure(reference == DealTaskReferenceStatus.AccessDenied
                    ? TaskErrors.AccessDenied() : TaskErrors.NotFound());
        }
        ContactTaskReferenceStatus? contactReference = null;
        if (string.Equals(input!.References.RecordModuleKey, "contacts", StringComparison.OrdinalIgnoreCase))
        {
            // Recheck current Contact authority before returning any cached protected response.
            // Archived remains eligible only for an exact replay, never a new association.
            contactReference = await contactReferences.ValidateAsync(trusted.WorkspaceId, input.References.RecordId!,
                metadata.RequestId, metadata.CorrelationId, cancellationToken);
            if (contactReference is not (ContactTaskReferenceStatus.Available or ContactTaskReferenceStatus.Archived))
                return TaskOperationResult<TaskMutationResponse>.Failure(contactReference == ContactTaskReferenceStatus.AccessDenied
                    ? TaskErrors.AccessDenied() : TaskErrors.NotFound());
        }
        // Both wire representations of an Organization association must identify the same
        // target. An alternate relationshipRef must not bypass target authorization.
        var organizationRecord = string.Equals(input!.References.RecordModuleKey, "organizations", StringComparison.OrdinalIgnoreCase);
        var organizationRelationship = input.References.RelationshipType == "ORGANIZATION_ACCOUNT";
        if (organizationRecord || organizationRelationship)
        {
            if ((input.References.RecordModuleKey is not null && !organizationRecord)
                || (input.References.RelationshipType is not null && !organizationRelationship)
                || (input.References.RecordId is not null && input.References.RelationshipId is not null
                    && input.References.RecordId != input.References.RelationshipId))
                return TaskOperationResult<TaskMutationResponse>.Failure(TaskErrors.Validation(
                    new Dictionary<string, string[]> { ["recordRef"] = ["Organization references must identify the same Organization."] }));
            var reference = await organizationReferences.CanReferenceAsync(trusted.WorkspaceId,
                input.References.RecordId ?? input.References.RelationshipId!, metadata.RequestId, metadata.CorrelationId, cancellationToken);
            // Recheck before the receipt branch: archive/revoked read denies even exact replay.
            if (!reference)
                return TaskOperationResult<TaskMutationResponse>.Failure(TaskErrors.NotFound());
        }
        if (string.Equals(input!.References.RecordModuleKey, "customers", StringComparison.OrdinalIgnoreCase))
        {
            // Customer association has one supported wire shape. Do not conflate a
            // Customer ID with its Contact/Organization subject relationship.
            if (input.References.RelationshipType is not null)
                return TaskOperationResult<TaskMutationResponse>.Failure(TaskErrors.Validation(
                    new Dictionary<string, string[]> { ["relationshipRef"] = ["Customer references cannot include relationshipRef."] }));
            // Current Customer read/scope and lifecycle must gate cached responses too.
            if (!await customerReferences.CanReferenceAsync(trusted.WorkspaceId, input.References.RecordId!,
                metadata.RequestId, metadata.CorrelationId, cancellationToken))
                return TaskOperationResult<TaskMutationResponse>.Failure(TaskErrors.NotFound());
        }
        var fingerprint = TaskCommandSupport.Fingerprint(input!);
        await using var transaction = await persistence.BeginSerializableAsync(cancellationToken);
        var scopeKey = TaskCommandSupport.ScopeKey(trusted, "createTask", "WORKSPACE", command.Metadata.IdempotencyKey,
            command.Metadata.IdempotencyScopeActorId);
        var existing = await persistence.FindCreateIdempotencyForUpdateAsync(scopeKey, cancellationToken);
        if (existing is not null)
        {
            // Answered from stored evidence alone, so an assignee deactivated after the original
            // commit cannot retroactively invalidate the replay or create a second Task.
            var replayError = TaskCommandSupport.ReplayError(existing, fingerprint);
            return replayError is null
                ? TaskOperationResult<TaskMutationResponse>.Success(Project(TaskCommandSupport.ReplayTask(existing), access.Value!))
                : TaskOperationResult<TaskMutationResponse>.Failure(replayError);
        }

        if (contactReference == ContactTaskReferenceStatus.Archived)
            return TaskOperationResult<TaskMutationResponse>.Failure(TaskErrors.NotFound());

        // Creation is a resource-level question, so no record scope applies, but field security
        // still does: a field the caller may not write must not be written on the way in either. It
        // follows the replay branch, so a committed creation stays replayable after a field turns
        // READ_ONLY or HIDDEN - the replay writes nothing.
        var createWriteError = TaskFieldSecurity.GuardCreateWrite(access.Value!.Authorization, input!.Description, input.References);
        if (createWriteError is not null)
            return TaskOperationResult<TaskMutationResponse>.Failure(createWriteError);

        if (string.Equals(input.References.RecordModuleKey, "deals", StringComparison.OrdinalIgnoreCase))
        {
            var reference = await dealReferences.ValidateAsync(trusted.WorkspaceId, input.References.RecordId!, false,
                metadata.RequestId, metadata.CorrelationId, cancellationToken);
            if (reference != DealTaskReferenceStatus.Available)
                return TaskOperationResult<TaskMutationResponse>.Failure(reference switch
                {
                    DealTaskReferenceStatus.AccessDenied => TaskErrors.AccessDenied(),
                    DealTaskReferenceStatus.Ineligible => TaskErrors.Validation(new Dictionary<string, string[]>
                    { ["recordRef.recordId"] = ["An archived Deal cannot receive a new follow-up Task."] }),
                    _ => TaskErrors.NotFound()
                });
        }

        // Only a genuinely new command evaluates current mutable member state.
        if (!await memberValidator.IsActiveMemberAsync(trusted.WorkspaceId, input.AssigneeId, cancellationToken))
            return TaskOperationResult<TaskMutationResponse>.Failure(TaskErrors.Validation(
                new Dictionary<string, string[]> { ["assigneeId"] = ["assigneeId must reference an active member of the trusted workspace."] }));

        var now = timeProvider.GetUtcNow();
        var task = new TaskItem(
            trusted.WorkspaceId,
            input.Title,
            input.Description,
            input.Priority,
            input.AssigneeId,
            input.DueAt,
            input.References,
            input.DedupeKey,
            now);
        persistence.AddTask(task);
        var response = TaskCommandSupport.RecordTaskCommit(
            persistence,
            task,
            trusted,
            command.Metadata,
            "createTask",
            "TASK_CREATED",
            scopeKey,
            "WORKSPACE",
            fingerprint,
            null,
            now);
        await persistence.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return TaskOperationResult<TaskMutationResponse>.Success(Project(response, access.Value!));
    }

    private static TaskMutationResponse Project(TaskMutationResponse response, TaskAccess access) =>
        response with
        {
            Result = new TaskMutationResult(TaskFieldSecurity.Project(response.Result.Task, access.Authorization))
        };
}
