using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UnicoreCRM.Operations.Tasks.Contracts;
using UnicoreCRM.Operations.Tasks.Domain;
using UnicoreCRM.Platform.Workspace.Contracts;

namespace UnicoreCRM.Operations.Tasks.Application.Common;

internal static class TaskCommandSupport
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static string ScopeKey(
        TrustedWorkspaceContext trusted,
        string operation,
        string targetId,
        string idempotencyKey) =>
        Hash($"{trusted.WorkspaceId}\n{operation}\n{trusted.MemberId}\n{targetId}\n{idempotencyKey}");

    internal static string Fingerprint<T>(T value) =>
        Hash(JsonSerializer.Serialize(value, JsonOptions));

    internal static TaskOperationError? ReplayError(TaskIdempotencyRecord existing, string fingerprint) =>
        existing.Fingerprint == fingerprint ? null : TaskErrors.IdempotencyReused(existing.IdempotencyKey);

    internal static TaskMutationResponse ReplayTask(TaskIdempotencyRecord record) =>
        (JsonSerializer.Deserialize<TaskMutationResponse>(record.ResponseJson, JsonOptions)
            ?? throw new InvalidOperationException("Stored Tasks idempotency response is invalid.")) with
        { Outcome = "REPLAYED" };

    internal static ActivityMutationResponse ReplayActivity(TaskIdempotencyRecord record) =>
        (JsonSerializer.Deserialize<ActivityMutationResponse>(record.ResponseJson, JsonOptions)
            ?? throw new InvalidOperationException("Stored Activity idempotency response is invalid.")) with
        { Outcome = "REPLAYED" };

    internal static TaskMutationResponse RecordTaskCommit(
        ITasksPersistence persistence,
        TaskItem task,
        TrustedWorkspaceContext trusted,
        TaskCommandMetadata metadata,
        string operation,
        string eventType,
        string scopeKey,
        string targetId,
        string fingerprint,
        long? priorVersion,
        DateTimeOffset now)
    {
        var audit = new TaskAuditRecord(
            operation,
            trusted.WorkspaceId,
            trusted.MemberId,
            task.TaskId,
            metadata.RequestId,
            metadata.CorrelationId,
            "COMMITTED",
            priorVersion,
            task.Version,
            now);
        var message = new TaskOutboxMessage(
            eventType,
            task.TaskId,
            trusted.WorkspaceId,
            metadata.CorrelationId,
            JsonSerializer.Serialize(new { taskId = task.TaskId, resourceVersion = task.Version }, JsonOptions),
            now);
        var response = new TaskMutationResponse(
            TaskIds.New("command"),
            metadata.CorrelationId,
            task.TaskId,
            "TASK",
            task.Version,
            TaskProjection.Utc(now),
            "COMMITTED",
            new TaskMutationResult(TaskProjection.Task(task)),
            [],
            [message.EventId],
            [audit.AuditId]);
        persistence.AddAudit(audit);
        persistence.AddOutbox(message);
        persistence.AddIdempotency(new TaskIdempotencyRecord(
            scopeKey,
            trusted.WorkspaceId,
            operation,
            trusted.MemberId,
            targetId,
            metadata.IdempotencyKey,
            fingerprint,
            JsonSerializer.Serialize(response, JsonOptions),
            now));
        return response;
    }

    internal static ActivityMutationResponse RecordActivityCommit(
        ITasksPersistence persistence,
        TaskActivity activity,
        TrustedWorkspaceContext trusted,
        TaskCommandMetadata metadata,
        string scopeKey,
        string fingerprint,
        DateTimeOffset now)
    {
        var audit = new TaskAuditRecord(
            "logActivity",
            trusted.WorkspaceId,
            trusted.MemberId,
            activity.ActivityId,
            metadata.RequestId,
            metadata.CorrelationId,
            "COMMITTED",
            null,
            activity.Version,
            now);
        var message = new TaskOutboxMessage(
            "ACTIVITY_LOGGED",
            activity.ActivityId,
            trusted.WorkspaceId,
            metadata.CorrelationId,
            JsonSerializer.Serialize(new { activityId = activity.ActivityId, resourceVersion = activity.Version }, JsonOptions),
            now);
        var response = new ActivityMutationResponse(
            TaskIds.New("command"),
            metadata.CorrelationId,
            activity.ActivityId,
            "ACTIVITY",
            activity.Version,
            TaskProjection.Utc(now),
            "COMMITTED",
            new ActivityMutationResult(TaskProjection.Activity(activity)),
            [],
            [message.EventId],
            [audit.AuditId]);
        persistence.AddAudit(audit);
        persistence.AddOutbox(message);
        persistence.AddIdempotency(new TaskIdempotencyRecord(
            scopeKey,
            trusted.WorkspaceId,
            "logActivity",
            trusted.MemberId,
            "WORKSPACE",
            metadata.IdempotencyKey,
            fingerprint,
            JsonSerializer.Serialize(response, JsonOptions),
            now));
        return response;
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
