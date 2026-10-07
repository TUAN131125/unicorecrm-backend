using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using UnicoreCRM.Crm.Leads.Application.ListLeads;
using UnicoreCRM.Crm.Leads.Application.Common;
using UnicoreCRM.Crm.Leads.Contracts;
using UnicoreCRM.Crm.Leads.Domain;
using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Crm.Leads.Application.ListLeadKanbanColumn;

internal sealed record Query(
    string? Cursor,
    int? Limit,
    string? Search,
    string Column,
    string? WorkState,
    string? OwnerId,
    string? AssignmentState,
    string RequestId,
    string CorrelationId);

internal sealed record LeadListPage(
    IReadOnlyList<LeadDocument> Items,
    string? NextCursor,
    bool HasNextPage,
    long TotalCount);

internal sealed class Handler(
    LeadAuthorization authorization,
    ILeadsPersistence persistence,
    ILeadKanbanPersistence kanban,
    IDataProtectionProvider protection,
    TimeProvider timeProvider)
{
    internal async Task<LeadOperationResult<LeadListPage>> HandleAsync(
        Query query,
        CancellationToken cancellationToken)
    {
        var metadata = new LeadRequestMetadata(query.RequestId, query.CorrelationId);
        var access = await authorization.AuthorizeAsync(LeadCapabilities.Read, metadata, cancellationToken);
        if (!access.IsSuccess)
            return LeadOperationResult<LeadListPage>.Failure(access.Error!);

        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var limit = query.Limit ?? 50;
        if (limit is < 1 or > 100)
            fields["limit"] = ["limit must be between 1 and 100."];
        var search = query.Search?.Trim();
        if (search is { Length: > 200 })
            fields["search"] = ["search cannot contain more than 200 characters."];
        if (search is { Length: 0 })
            search = null;
        var workState = ParseWorkState(query.WorkState, fields);
        if (query.Column is not "NEW" and not "CONTACTING" and not "VERIFYING" and not "POSITIVE_OUTCOME" and not "NURTURE" and not "DISQUALIFIED")
            fields["column"] = ["column is not a supported Lead Kanban column."];
        if (query.OwnerId is not null && !LeadValidation.IsEntityId(query.OwnerId))
            fields["ownerId"] = ["ownerId is not a valid entity identifier."];
        if (query.AssignmentState is not null and not "ASSIGNED" and not "UNASSIGNED")
            fields["assignmentState"] = ["assignmentState must be ASSIGNED or UNASSIGNED."];
        if (query.AssignmentState == "UNASSIGNED" && query.OwnerId is not null)
            fields["ownerId"] = ["ownerId cannot be combined with UNASSIGNED."];
        if (query.Column is "POSITIVE_OUTCOME" or "NURTURE" or "DISQUALIFIED"
            && !access.Value!.Authorization.CanRead("qualificationOutcome"))
            return LeadOperationResult<LeadListPage>.Failure(LeadErrors.AccessDenied());
        var trusted = access.Value!.Trusted;
        var binding = JsonSerializer.Serialize(new {
            trusted.WorkspaceId, trusted.MemberId, query.Column, WorkState = query.WorkState,
            query.OwnerId, query.AssignmentState, Search = search?.ToUpperInvariant(),
            access.Value.Authorization.PolicyFingerprint,
            access.Value.Authorization.ScopeFilter, access.Value.Authorization.ScopeOwnerMemberId,
            access.Value.Authorization.CanReadUnassigned, Phone = access.Value.Authorization.CanRead("phone")
        });
        var cursorCodec = new LeadKanbanCursor(protection, binding);
        cursorCodec.TryParse(query.Cursor, fields, out var cursorUpdatedAt, out var cursorLeadId);
        if (fields.Count != 0)
            return LeadOperationResult<LeadListPage>.Failure(LeadErrors.Validation(fields));

        // AccessControl resolves the record scope once and Leads pushes it into the owner query. A
        // denied scope returns nothing rather than a filtered view of everything.
        var scope = access.Value!.Authorization.ScopeFilter;
        if (scope == RecordAccessScopeFilter.Denied)
            return LeadOperationResult<LeadListPage>.Success(new([], null, false, 0));

        var scopeOwnerId = scope == RecordAccessScopeFilter.OwnedByMember
            ? access.Value!.Authorization.ScopeOwnerMemberId
            : null;
        var normalizedSearch = search?.ToUpperInvariant();
        var includePhoneSearch = access.Value!.Authorization.CanRead("phone");

        var leads = await kanban.ListColumnAsync(
            trusted.WorkspaceId,
            scopeOwnerId,
            query.OwnerId,
            query.AssignmentState,
            access.Value!.Authorization.CanReadUnassigned,
            query.Column,
            workState,
            normalizedSearch,
            includePhoneSearch,
            cursorUpdatedAt,
            cursorLeadId,
            limit + 1,
            cancellationToken);
        var totalCount = await kanban.CountColumnAsync(
            trusted.WorkspaceId,
            scopeOwnerId,
            query.OwnerId,
            query.AssignmentState,
            access.Value!.Authorization.CanReadUnassigned,
            query.Column,
            workState,
            normalizedSearch,
            includePhoneSearch,
            cancellationToken);
        persistence.AddAudit(new LeadAuditRecord(
            "listLeadKanbanColumn",
            trusted.WorkspaceId,
            trusted.MemberId,
            null,
            query.RequestId,
            query.CorrelationId,
            "READ",
            null,
            null,
            timeProvider.GetUtcNow()));
        await persistence.SaveChangesAsync(cancellationToken);
        var hasNextPage = leads.Count > limit;
        var page = hasNextPage ? leads.Take(limit).ToArray() : leads;
        var nextCursor = hasNextPage ? cursorCodec.Encode(page[^1]) : null;
        return LeadOperationResult<LeadListPage>.Success(new(
            page.Select(lead => LeadFieldSecurity.Project(LeadProjection.Document(lead), access.Value!.Authorization)).ToArray(),
            nextCursor,
            hasNextPage,
            totalCount));
    }

    private static LeadWorkState? ParseWorkState(string? value, IDictionary<string, string[]> fields) => value switch
    {
        null => null,
        "NEW" => LeadWorkState.New,
        "CONTACTING" => LeadWorkState.Contacting,
        "VERIFYING" => LeadWorkState.Verifying,
        "CLOSED" => LeadWorkState.Closed,
        _ => InvalidWorkState(fields)
    };

    private static LeadWorkState? InvalidWorkState(IDictionary<string, string[]> fields)
    {
        fields["workState"] = ["workState must be NEW, CONTACTING, VERIFYING, or CLOSED."];
        return null;
    }
}

internal sealed class LeadKanbanCursor(IDataProtectionProvider protection, string binding)
{
    private readonly IDataProtector protector = protection.CreateProtector("Leads.KanbanCursor.v1", binding);

    internal string Encode(Lead lead) => protector.Protect(LeadListCursor.Encode(lead));

    internal bool TryParse(string? cursor, IDictionary<string, string[]> fields,
        out DateTimeOffset? updatedAt, out string? leadId)
    {
        updatedAt = null;
        leadId = null;
        if (cursor is null) return true;
        try
        {
            if (cursor.Length > 4096) throw new FormatException();
            return LeadListCursor.TryParse(protector.Unprotect(cursor), fields, out updatedAt, out leadId);
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException or ArgumentException)
        {
            fields["cursor"] = ["cursor is invalid for this Kanban query."];
            return false;
        }
    }
}
