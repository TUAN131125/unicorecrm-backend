using UnicoreCRM.Operations.Support.Application.Common;
using UnicoreCRM.Operations.Support.Contracts;
using UnicoreCRM.Operations.Support.Domain;
using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Operations.Support.Application.ListSupportCases;

internal sealed record Query(
    string? Cursor,
    int? Limit,
    string? Search,
    string? SortBy,
    string? SortDirection,
    string? Status,
    string? Priority,
    string? Category,
    string? OwnerId,
    string? RelationshipType,
    string? RelationshipId,
    string? SlaStatus,
    string RequestId,
    string CorrelationId);

internal sealed class Handler(
    SupportAuthorization authorization,
    ISupportPersistence persistence,
    TimeProvider timeProvider)
{
    internal async Task<SupportOperationResult<SupportCaseListResponse>> HandleAsync(
        Query query,
        CancellationToken cancellationToken)
    {
        var metadata = new SupportRequestMetadata(query.RequestId, query.CorrelationId);
        var access = await authorization.AuthorizeAsync(SupportCapabilities.Read, metadata, cancellationToken);
        if (!access.IsSuccess)
            return SupportOperationResult<SupportCaseListResponse>.Failure(access.Error!);

        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        SupportValidation.TryCursor(query.Cursor, fields, out var offset);
        var limit = query.Limit ?? 50;
        if (limit is < 1 or > 250)
            fields["limit"] = ["limit must be between 1 and 250."];
        var search = query.Search?.Trim();
        if (search?.Length > 240)
            fields["search"] = ["search must contain at most 240 characters."];
        if (search?.Length == 0)
            search = null;
        var sortBy = query.SortBy ?? "updatedAt";
        if (sortBy is not ("updatedAt" or "createdAt" or "priority" or "resolutionDueAt" or "caseNumber"))
            fields["sortBy"] = ["sortBy must be one of updatedAt, createdAt, priority, resolutionDueAt, caseNumber."];
        var descending = (query.SortDirection ?? "desc") switch
        {
            "asc" => false,
            "desc" => true,
            _ => InvalidDirection(fields)
        };
        var status = query.Status is null ? null : SupportValidation.Status(query.Status, "status", fields);
        var priority = query.Priority is null ? null : SupportValidation.Priority(query.Priority, "priority", fields);
        var category = query.Category is null ? null : SupportValidation.Category(query.Category, "category", fields);
        if (query.OwnerId is not null && !SupportValidation.IsEntityId(query.OwnerId))
            fields["ownerId"] = ["ownerId is not a valid entity identifier."];
        if (query.RelationshipType is not null && query.RelationshipType is not ("CONTACT" or "ORGANIZATION_ACCOUNT"))
            fields["relationshipType"] = ["relationshipType must be CONTACT or ORGANIZATION_ACCOUNT."];
        if (query.RelationshipId is not null && !SupportValidation.IsEntityId(query.RelationshipId))
            fields["relationshipId"] = ["relationshipId is not a valid entity identifier."];
        if (query.SlaStatus is not null && !SupportValidation.IsSlaStatus(query.SlaStatus))
            fields["slaStatus"] = ["slaStatus is not an admitted Support Case SLA status."];
        if (fields.Count != 0)
            return SupportOperationResult<SupportCaseListResponse>.Failure(SupportErrors.Validation(fields));

        var trusted = access.Value!.Trusted;

        // The record scope becomes a query predicate rather than a per-row decision: AccessControl
        // resolves the filter once, and Support pushes it into the owner query ahead of the count,
        // the ordering and the page. Filtering after the fact would leak hidden rows through
        // totalCount and through page boundaries.
        var scope = access.Value!.Authorization.ScopeFilter;
        if (scope == RecordAccessScopeFilter.Denied)
        {
            return SupportOperationResult<SupportCaseListResponse>.Success(
                new SupportCaseListResponse([], new SupportPageInfo(false, null, 0)));
        }

        var page = await persistence.ListCasesAsync(
            trusted.WorkspaceId,
            new SupportCaseListSpecification(
                offset,
                limit,
                search,
                status,
                priority,
                category,
                query.OwnerId,
                query.RelationshipType,
                query.RelationshipId,
                query.SlaStatus,
                sortBy,
                descending,
                scope == RecordAccessScopeFilter.OwnedByMember ? access.Value!.Authorization.ScopeOwnerMemberId : null),
            cancellationToken);

        var now = timeProvider.GetUtcNow();
        persistence.AddAudit(new SupportAuditRecord(
            "listSupportCases",
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
        return SupportOperationResult<SupportCaseListResponse>.Success(new SupportCaseListResponse(
            page.Items.Select(item => SupportFieldSecurity.Project(SupportProjection.Case(item), access.Value!.Authorization)).ToArray(),
            new SupportPageInfo(
                page.HasNextPage,
                page.NextOffset is null ? null : SupportValidation.Cursor(page.NextOffset.Value),
                page.TotalCount)));
    }

    private static bool InvalidDirection(IDictionary<string, string[]> fields)
    {
        fields["sortDirection"] = ["sortDirection must be asc or desc."];
        return true;
    }
}
