using UnicoreCRM.Operations.Tasks.Application.Common;
using UnicoreCRM.Operations.Tasks.Contracts;
using UnicoreCRM.Operations.Tasks.Domain;
using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Operations.Tasks.Application.ListActivities;

internal sealed record Query(
    string? Cursor,
    int? Limit,
    string? Search,
    string? SortDirection,
    string? Type,
    string? ActorId,
    string? RelationshipType,
    string? RelationshipId,
    string? RecordModuleKey,
    string? RecordId,
    string? OccurredFrom,
    string? OccurredTo,
    string RequestId,
    string CorrelationId);

internal sealed class Handler(
    TaskAuthorization authorization,
    ITasksPersistence persistence,
    TimeProvider timeProvider)
{
    internal async Task<TaskOperationResult<ActivityListResponse>> HandleAsync(Query query, CancellationToken cancellationToken)
    {
        var metadata = new TaskRequestMetadata(query.RequestId, query.CorrelationId);
        var access = await authorization.AuthorizeAsync(TaskCapabilities.Read, metadata, cancellationToken);
        if (!access.IsSuccess)
            return TaskOperationResult<ActivityListResponse>.Failure(access.Error!);
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        TaskValidation.TryCursor(query.Cursor, fields, out var offset);
        var limit = query.Limit ?? 50;
        if (limit is < 1 or > 250)
            fields["limit"] = ["limit must be between 1 and 250."];
        var search = query.Search?.Trim();
        if (search?.Length > 240)
            fields["search"] = ["search must contain at most 240 characters."];
        var descending = (query.SortDirection ?? "desc") switch
        {
            "asc" => false,
            "desc" => true,
            _ => InvalidDirection(fields)
        };
        var type = ParseType(query.Type, fields);
        ValidateOptionalEntity(query.ActorId, "actorId", fields);
        if (query.RelationshipType is not null && query.RelationshipType is not ("CONTACT" or "ORGANIZATION_ACCOUNT"))
            fields["relationshipType"] = ["relationshipType must be CONTACT or ORGANIZATION_ACCOUNT."];
        ValidateOptionalEntity(query.RelationshipId, "relationshipId", fields);
        if (query.RecordModuleKey is not null && (query.RecordModuleKey.Trim().Length is < 1 or > 100))
            fields["recordModuleKey"] = ["recordModuleKey must contain between 1 and 100 characters."];
        ValidateOptionalEntity(query.RecordId, "recordId", fields);
        TaskValidation.TryOptionalUtc(query.OccurredFrom, "occurredFrom", fields, out var occurredFrom);
        TaskValidation.TryOptionalUtc(query.OccurredTo, "occurredTo", fields, out var occurredTo);
        if (occurredFrom > occurredTo)
            fields["occurredFrom"] = ["occurredFrom must not be later than occurredTo."];
        if (fields.Count != 0)
            return TaskOperationResult<ActivityListResponse>.Failure(TaskErrors.Validation(fields));

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
        {
            return TaskOperationResult<ActivityListResponse>.Success(
                new ActivityListResponse([], new PageInfo(false, null, 0)));
        }

        var trusted = access.Value!.Trusted;
        var page = await persistence.ListActivitiesAsync(
            trusted.WorkspaceId,
            new ActivityListSpecification(
                offset,
                limit,
                search,
                type,
                query.ActorId,
                query.RelationshipType,
                query.RelationshipId,
                query.RecordModuleKey,
                query.RecordId,
                occurredFrom,
                occurredTo,
                descending),
            cancellationToken);
        var now = timeProvider.GetUtcNow();
        persistence.AddAudit(new TaskAuditRecord(
            "listActivities",
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
        return TaskOperationResult<ActivityListResponse>.Success(new ActivityListResponse(
            page.Items.Select(TaskProjection.Activity).ToArray(),
            new PageInfo(page.HasNextPage, page.NextOffset is null ? null : TaskValidation.Cursor(page.NextOffset.Value), page.TotalCount)));
    }

    private static bool InvalidDirection(IDictionary<string, string[]> fields)
    {
        fields["sortDirection"] = ["sortDirection must be asc or desc."];
        return true;
    }

    private static ActivityType? ParseType(string? value, IDictionary<string, string[]> fields) => value switch
    {
        null => null,
        "CALL" => ActivityType.Call,
        "EMAIL" => ActivityType.Email,
        "MEETING" => ActivityType.Meeting,
        "NOTE" => ActivityType.Note,
        "MESSAGE" => ActivityType.Message,
        "SYSTEM" => ActivityType.System,
        _ => InvalidType(fields)
    };

    private static ActivityType? InvalidType(IDictionary<string, string[]> fields)
    {
        fields["type"] = ["type is invalid."];
        return null;
    }

    private static void ValidateOptionalEntity(string? value, string field, IDictionary<string, string[]> fields)
    {
        if (value is not null && !TaskValidation.IsEntityId(value))
            fields[field] = [$"{field} is not a valid entity identifier."];
    }
}
