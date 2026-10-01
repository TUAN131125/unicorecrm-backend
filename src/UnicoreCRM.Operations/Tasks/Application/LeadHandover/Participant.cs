using System.Text.Json;
using UnicoreCRM.Operations.Tasks.Application.Common;
using UnicoreCRM.Operations.Tasks.Contracts;
using UnicoreCRM.Operations.Tasks.Domain;
using UnicoreCRM.Platform.AccessControl.Contracts;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Operations.Tasks.Application.LeadHandover;

internal sealed class Participant(
    TaskAuthorization authorization,
    ICurrentWorkspace currentWorkspace,
    IServiceAccessAuthorizer serviceAccess,
    IWorkspaceMemberReferenceValidator members,
    ITasksPersistence persistence,
    TimeProvider timeProvider) : ILeadHandoverTaskParticipant
{
    internal const string RecoveryPrincipal = "svc_lead_handover_recovery";
    private const string Operation = "leadHandoverTasks";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private enum ExecutionMode { Validate, Execute, ResolveOrFence, AuthorizeReplay }

    public Task<LeadHandoverTaskResult> ValidateAsync(LeadHandoverTaskCommand command, CancellationToken cancellationToken) =>
        RunAsync(command, ExecutionMode.Validate, cancellationToken);

    public Task<LeadHandoverTaskResult> ExecuteAsync(LeadHandoverTaskCommand command, CancellationToken cancellationToken) =>
        RunAsync(command, ExecutionMode.Execute, cancellationToken);

    public Task<LeadHandoverTaskResult> ResolveOrFenceAsync(LeadHandoverTaskCommand command, CancellationToken cancellationToken) =>
        RunAsync(command, ExecutionMode.ResolveOrFence, cancellationToken);

    public Task<LeadHandoverTaskResult> AuthorizeReplayAsync(LeadHandoverTaskCommand command, CancellationToken cancellationToken) =>
        RunAsync(command, ExecutionMode.AuthorizeReplay, cancellationToken);

    private async Task<LeadHandoverTaskResult> RunAsync(LeadHandoverTaskCommand command, ExecutionMode mode, CancellationToken cancellationToken)
    {
        var fields = new Dictionary<string, string[]>();
        foreach (var (key, value) in new[] { ("leadId", command.LeadId), ("handoverId", command.HandoverId),
                     ("newOwnerId", command.NewOwnerId), ("originalActorId", command.OriginalActorId) })
            if (!TaskValidation.IsEntityId(value)) fields[key] = ["A valid entity identifier is required."];
        var reason = TaskValidation.Text(command.Reason, "reason", 1, 1000, true, fields);
        if (command.OpenTaskPolicy is not (LeadHandoverTaskPolicies.Keep or LeadHandoverTaskPolicies.Move))
            fields["openTaskPolicy"] = ["An explicit canonical open Task policy is required."];
        if (command.FrozenDueAt == default) fields["frozenDueAt"] = ["The frozen due instant is required."];
        foreach (var (key, value) in new[] { ("requestId", command.RequestId), ("correlationId", command.CorrelationId) })
            TaskValidation.Text(value, key, 1, 128, true, fields);
        if (fields.Count != 0) return Failure(TaskErrors.Validation(fields));

        var trusted = command.TrustedWorkspace;
        var metadata = new TaskRequestMetadata(command.RequestId, command.CorrelationId);
        var recovery = command.ExecutorServicePrincipalId is not null;
        if ((mode == ExecutionMode.ResolveOrFence && !recovery) || (mode == ExecutionMode.AuthorizeReplay && recovery))
            return Failure(TaskErrors.AccessDenied());
        TaskAccess? createAccess = null;
        TaskAccess? assignAccess = null;
        if (recovery)
        {
            if (command.ExecutorServicePrincipalId != RecoveryPrincipal) return Failure(TaskErrors.AccessDenied());
            var decision = await serviceAccess.AuthorizeAsync(trusted.WorkspaceId, RecoveryPrincipal,
                AccessRequirement.ForCanonicalCapability("leads.handover.recover"), command.CorrelationId, cancellationToken);
            if (!decision.IsAllowed) return Failure(TaskErrors.AccessDenied());
        }
        else
        {
            if (!currentWorkspace.IsResolved || currentWorkspace.Require() != trusted || trusted.MemberId != command.OriginalActorId)
                return Failure(TaskErrors.WorkspaceMismatch());
            var access = await authorization.AuthorizeAsync(TaskCapabilities.Create, metadata, cancellationToken);
            if (!access.IsSuccess) return Failure(access.Error!);
            if (access.Value!.Trusted != trusted) return Failure(TaskErrors.WorkspaceMismatch());
            createAccess = access.Value;
            if (command.OpenTaskPolicy == LeadHandoverTaskPolicies.Move)
            {
                access = await authorization.AuthorizeAsync(TaskCapabilities.Assign, metadata, cancellationToken);
                if (!access.IsSuccess) return Failure(access.Error!);
                if (access.Value!.Trusted != trusted) return Failure(TaskErrors.WorkspaceMismatch());
                assignAccess = access.Value;
            }
        }

        // Scope is the durable handover, independent of the current executor and request attempt.
        var scope = TaskCommandSupport.Fingerprint(new { trusted.WorkspaceId, Operation, command.HandoverId });
        var fingerprint = TaskCommandSupport.Fingerprint(new { command.LeadId, command.HandoverId,
            command.NewOwnerId, Reason = reason, command.OpenTaskPolicy, command.FrozenDueAt, command.OriginalActorId });
        await using var transaction = await persistence.BeginSerializableAsync(cancellationToken);
        var existing = await persistence.FindLeadHandoverIdempotencyForUpdateAsync(scope, cancellationToken);
        if (existing is not null)
        {
            var error = TaskCommandSupport.ReplayError(existing, fingerprint);
            if (error is not null) return Failure(error);
            var replay = JsonSerializer.Deserialize<LeadHandoverTaskResult>(existing.ResponseJson, JsonOptions)
                ?? throw new InvalidOperationException("Invalid stored Lead Handover Tasks result.");
            if (!replay.IsSuccess) return replay;
            if (mode == ExecutionMode.ResolveOrFence) return replay;
            if (!recovery)
            {
                // Replays do not discover or mutate tasks created after the original snapshot.
                foreach (var id in replay.ReassignedTaskIds.Append(replay.HandoverTaskId!))
                {
                    var task = await persistence.ReadTaskAsync(trusted.WorkspaceId, id, cancellationToken);
                    if (task is null) return Failure(TaskErrors.NotFound());
                    var denied = await authorization.EnforceRecordAsync(assignAccess ?? createAccess!, task, Operation, metadata, cancellationToken);
                    if (denied is not null) return Failure(denied);
                }
            }
            return replay with { Outcome = "REPLAYED" };
        }

        if (mode == ExecutionMode.ResolveOrFence)
        {
            // This range lock is also the first lock taken by Execute. A worker that has not
            // committed must either finish before this probe or observe the durable fence later.
            var fenceNow = timeProvider.GetUtcNow();
            var audit = new TaskAuditRecord("leadHandoverTasks:fence", trusted.WorkspaceId, RecoveryPrincipal,
                command.LeadId, command.RequestId, command.CorrelationId, "FENCED", null, null, fenceNow);
            var message = new TaskOutboxMessage("LEAD_HANDOVER_TASKS_FENCED", command.LeadId, trusted.WorkspaceId,
                command.CorrelationId, JsonSerializer.Serialize(new { command.LeadId, command.HandoverId,
                    command.OriginalActorId, executor = RecoveryPrincipal, command.RequestId, command.CorrelationId }, JsonOptions), fenceNow);
            var fenced = new LeadHandoverTaskResult(false, [], null, null, null, "FENCED", "HANDOVER_TASKS_FENCED", 409)
            {
                EmittedEventIds = [message.EventId], AuditEvidenceIds = [audit.AuditId]
            };
            persistence.AddAudit(audit);
            persistence.AddOutbox(message);
            persistence.AddIdempotency(new TaskIdempotencyRecord(scope, trusted.WorkspaceId, Operation,
                command.OriginalActorId, command.LeadId, command.HandoverId, fingerprint,
                JsonSerializer.Serialize(fenced, JsonOptions), fenceNow));
            await persistence.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return fenced;
        }

        if (mode == ExecutionMode.AuthorizeReplay)
            return Failure(new TaskOperationError("HANDOVER_TASKS_NOT_COMMITTED", 409, "No committed Tasks proof exists."));

        // Recovery may finish an already committed participant, never admit new human side effects.
        if (recovery) return Failure(new TaskOperationError("HANDOVER_TASKS_NOT_COMMITTED", 409,
            "Human Tasks admission is required before the first participant commit."));

        if (!await members.IsActiveMemberAsync(trusted.WorkspaceId, command.NewOwnerId, cancellationToken))
            return Failure(TaskErrors.Validation(new Dictionary<string, string[]> { ["newOwnerId"] = ["An active workspace member is required."] }));
        var references = new TaskReferenceData(null, null, "leads", command.LeadId, null,
            "LEAD_HANDOVER", command.HandoverId, reason);
        if (!recovery)
        {
            var denied = TaskFieldSecurity.GuardCreateWrite(createAccess!.Authorization, null, references);
            if (denied is not null) return Failure(denied);
            if (assignAccess is not null)
            {
                denied = TaskAuthorization.EnforceFieldWrite(assignAccess, "assigneeId");
                if (denied is not null) return Failure(denied);
            }
        }
        IReadOnlyList<TaskItem> eligible = command.OpenTaskPolicy == LeadHandoverTaskPolicies.Move
            ? await persistence.LoadEligibleLeadHandoverTasksForUpdateAsync(trusted.WorkspaceId, command.LeadId, cancellationToken)
            : [];
        // Validate the entire locked snapshot before the first domain mutation.
        if (!recovery)
            foreach (var task in eligible)
            {
                var denied = await authorization.EnforceRecordAsync(assignAccess!, task, Operation, metadata, cancellationToken);
                if (denied is not null) return Failure(denied);
            }
        if (mode == ExecutionMode.Validate) return new(true, [], null, null, command.FrozenDueAt, "VALIDATED");
        var now = timeProvider.GetUtcNow();
        var eventIds = new List<string>();
        var auditIds = new List<string>();
        foreach (var task in eligible)
        {
            var priorVersion = task.Version;
            var priorAssignee = task.AssigneeId;
            if (!task.Assign(command.NewOwnerId, now)) throw new InvalidOperationException("Locked eligible Task was not OPEN.");
            var proof = RecordEvidence(task, "TASK_ASSIGNED", priorVersion, priorAssignee, command, now);
            eventIds.Add(proof.EventId);
            auditIds.Add(proof.AuditId);
        }
        var title = reason!.Length <= 300 ? reason : reason[..300];
        var takeover = new TaskItem(trusted.WorkspaceId, title, null, TaskPriority.Normal,
            command.NewOwnerId, command.FrozenDueAt, references, null, now);
        persistence.AddTask(takeover);
        var takeoverProof = RecordEvidence(takeover, "TASK_CREATED", null, null, command, now);
        eventIds.Add(takeoverProof.EventId);
        auditIds.Add(takeoverProof.AuditId);
        var result = new LeadHandoverTaskResult(true, eligible.Select(task => task.TaskId).ToArray(),
            takeover.TaskId, takeover.Version, takeover.DueAt, "COMMITTED")
        {
            EmittedEventIds = eventIds,
            AuditEvidenceIds = auditIds
        };
        persistence.AddIdempotency(new TaskIdempotencyRecord(scope, trusted.WorkspaceId, Operation,
            command.OriginalActorId, command.LeadId, command.HandoverId, fingerprint,
            JsonSerializer.Serialize(result, JsonOptions), now));
        await persistence.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private (string EventId, string AuditId) RecordEvidence(TaskItem task, string eventType, long? priorVersion, string? priorAssignee,
        LeadHandoverTaskCommand command, DateTimeOffset now)
    {
        var executor = command.ExecutorServicePrincipalId ?? command.TrustedWorkspace.MemberId;
        var audit = new TaskAuditRecord(Operation, task.WorkspaceId, executor, task.TaskId,
            command.RequestId, command.CorrelationId, "COMMITTED", priorVersion, task.Version, now);
        var message = new TaskOutboxMessage(eventType, task.TaskId, task.WorkspaceId, command.CorrelationId,
            JsonSerializer.Serialize(new { taskId = task.TaskId, resourceVersion = task.Version,
                command.HandoverId, command.LeadId, command.OriginalActorId, executor,
                command.NewOwnerId, priorAssignee, reason = command.Reason.Trim(), command.OpenTaskPolicy,
                command.FrozenDueAt, command.RequestId, command.CorrelationId }, JsonOptions), now);
        persistence.AddAudit(audit);
        persistence.AddOutbox(message);
        return (message.EventId, audit.AuditId);
    }

    private static LeadHandoverTaskResult Failure(TaskOperationError error) =>
        new(false, [], null, null, null, null, error.Code, error.Status, error.FieldErrors);
}
