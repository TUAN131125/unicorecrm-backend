using UnicoreCRM.Operations.Tasks.Application.Common;
using UnicoreCRM.Operations.Tasks.Contracts;
using UnicoreCRM.Operations.Tasks.Domain;
using Domain = UnicoreCRM.Operations.Tasks.Domain;

namespace UnicoreCRM.Operations.Tasks.Application.ListTasks;

internal sealed record Query(
    string? Cursor,
    int? Limit,
    string? Search,
    string? SortBy,
    string? SortDirection,
    string? Status,
    string? Priority,
    string? AssigneeId,
    string? RelationshipType,
    string? RelationshipId,
    string? RecordModuleKey,
    string? RecordId,
    string? OverdueAt,
    string RequestId,
    string CorrelationId);

internal sealed class Handler(
    TaskAuthorization authorization,
    ITasksPersistence persistence,
    TimeProvider timeProvider)
{
    internal async Task<TaskOperationResult<TaskListResponse>> HandleAsync(Query query, CancellationToken cancellationToken)
    {
        var access = await authorization.AuthorizeAsync(TaskCapabilities.Read, query.CorrelationId, cancellationToken);
        if (!access.IsSuccess)
            return TaskOperationResult<TaskListResponse>.Failure(access.Error!);
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        TaskValidation.TryCursor(query.Cursor, fields, out var offset);
        var limit = query.Limit ?? 50;
        if (limit is < 1 or > 250)
            fields["limit"] = ["limit must be between 1 and 250."];
        var search = query.Search?.Trim();
        if (search?.Length > 240)
            fields["search"] = ["search must contain at most 240 characters."];
        var sortBy = query.SortBy ?? "updatedAt";
        if (sortBy is not ("updatedAt" or "createdAt" or "dueAt" or "priority" or "title"))
            fields["sortBy"] = ["sortBy is invalid."];
        var descending = (query.SortDirection ?? "desc") switch
        {
            "asc" => false,
            "desc" => true,
            _ => InvalidDirection(fields)
        };
        var status = ParseStatus(query.Status, fields);
        var priority = ParsePriority(query.Priority, fields);
        ValidateOptionalEntity(query.AssigneeId, "assigneeId", fields);
        ValidateRelationship(query.RelationshipType, query.RelationshipId, fields);
        ValidateOptionalText(query.RecordModuleKey, "recordModuleKey", 100, fields);
        ValidateOptionalEntity(query.RecordId, "recordId", fields);
        TaskValidation.TryOptionalUtc(query.OverdueAt, "overdueAt", fields, out var overdueAt);
        if (fields.Count != 0)
            return TaskOperationResult<TaskListResponse>.Failure(TaskErrors.Validation(fields));

        var trusted = access.Value!;
        var page = await persistence.ListTasksAsync(
            trusted.WorkspaceId,
            new TaskListSpecification(
                offset,
                limit,
                search,
                status,
                priority,
                query.AssigneeId,
                query.RelationshipType,
                query.RelationshipId,
                query.RecordModuleKey,
                query.RecordId,
                overdueAt,
                sortBy,
                descending),
            cancellationToken);
        var now = timeProvider.GetUtcNow();
        persistence.AddAudit(new TaskAuditRecord(
            "listTasks",
            trusted.WorkspaceId,
            trusted.MemberId,
            null,
            query.RequestId,
            query.CorrelationId,
            "READ",
            null,
            null,
            now));
        await persistence.SaveChangesAsync(cancellationToken);
        return TaskOperationResult<TaskListResponse>.Success(new TaskListResponse(
            page.Items.Select(TaskProjection.Task).ToArray(),
            new PageInfo(page.HasNextPage, page.NextOffset is null ? null : TaskValidation.Cursor(page.NextOffset.Value), page.TotalCount)));
    }

    private static bool InvalidDirection(IDictionary<string, string[]> fields)
    {
        fields["sortDirection"] = ["sortDirection must be asc or desc."];
        return true;
    }

    private static Domain.TaskStatus? ParseStatus(string? value, IDictionary<string, string[]> fields) => value switch
    {
        null => null,
        "OPEN" => Domain.TaskStatus.Open,
        "COMPLETED" => Domain.TaskStatus.Completed,
        "CANCELLED" => Domain.TaskStatus.Cancelled,
        _ => InvalidStatus(fields)
    };

    private static Domain.TaskStatus? InvalidStatus(IDictionary<string, string[]> fields)
    {
        fields["status"] = ["status must be OPEN, COMPLETED, or CANCELLED."];
        return null;
    }

    private static TaskPriority? ParsePriority(string? value, IDictionary<string, string[]> fields) => value switch
    {
        null => null,
        "LOW" => TaskPriority.Low,
        "NORMAL" => TaskPriority.Normal,
        "HIGH" => TaskPriority.High,
        "URGENT" => TaskPriority.Urgent,
        _ => InvalidPriority(fields)
    };

    private static TaskPriority? InvalidPriority(IDictionary<string, string[]> fields)
    {
        fields["priority"] = ["priority must be LOW, NORMAL, HIGH, or URGENT."];
        return null;
    }

    private static void ValidateRelationship(string? type, string? id, IDictionary<string, string[]> fields)
    {
        if (type is not null && type is not ("CONTACT" or "ORGANIZATION_ACCOUNT"))
            fields["relationshipType"] = ["relationshipType must be CONTACT or ORGANIZATION_ACCOUNT."];
        ValidateOptionalEntity(id, "relationshipId", fields);
    }

    private static void ValidateOptionalEntity(string? value, string field, IDictionary<string, string[]> fields)
    {
        if (value is not null && !TaskValidation.IsEntityId(value))
            fields[field] = [$"{field} is not a valid entity identifier."];
    }

    private static void ValidateOptionalText(string? value, string field, int maximum, IDictionary<string, string[]> fields)
    {
        if (value is not null && (value.Trim().Length is < 1 || value.Trim().Length > maximum))
            fields[field] = [$"{field} must contain between 1 and {maximum} characters."];
    }
}
