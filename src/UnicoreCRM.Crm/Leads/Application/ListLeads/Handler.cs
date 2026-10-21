using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using UnicoreCRM.Crm.Leads.Application.Common;
using UnicoreCRM.Crm.Leads.Contracts;
using UnicoreCRM.Crm.Leads.Domain;
using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Crm.Leads.Application.ListLeads;

internal sealed record Query(
    string? Cursor,
    int? Limit,
    string? Search,
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
    TimeProvider timeProvider,
    IDataProtectionProvider protection)
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
        if (limit is < 1 or > 250)
            fields["limit"] = ["limit must be between 1 and 250."];
        var search = query.Search?.Trim();
        if (search is { Length: > 200 })
            fields["search"] = ["search cannot contain more than 200 characters."];
        if (search is { Length: 0 })
            search = null;
        var workState = ParseWorkState(query.WorkState, fields);
        if (query.OwnerId is not null && !LeadValidation.IsEntityId(query.OwnerId))
            fields["ownerId"] = ["ownerId is not a valid entity identifier."];
        if (query.AssignmentState is not null and not "ASSIGNED" and not "UNASSIGNED")
            fields["assignmentState"] = ["assignmentState must be ASSIGNED or UNASSIGNED."];
        if (query.AssignmentState == "UNASSIGNED" && query.OwnerId is not null)
            fields["ownerId"] = ["ownerId cannot be combined with UNASSIGNED."];
        if (fields.Count != 0)
            return LeadOperationResult<LeadListPage>.Failure(LeadErrors.Validation(fields));

        var trusted = access.Value!.Trusted;
        var policy = access.Value.Authorization;
        var scope = policy.ScopeFilter;
        var scopeOwnerId = scope == RecordAccessScopeFilter.OwnedByMember ? policy.ScopeOwnerMemberId : null;
        var normalizedSearch = search?.ToUpperInvariant();
        var includePhoneSearch = policy.CanRead("phone");
        // Bind effective authority, not an additional individual-principal restriction. Limit changes
        // only the window length; the keyset predicate and authorized query remain identical.
        var binding = JsonSerializer.Serialize(new
        {
            trusted.WorkspaceId,
            WorkState = workState,
            query.OwnerId,
            query.AssignmentState,
            Search = normalizedSearch,
            ScopeFilter = scope,
            ScopeOwnerMemberId = scopeOwnerId,
            policy.CanReadUnassigned,
            Phone = includePhoneSearch,
            policy.PolicyFingerprint
        });
        var cursorCodec = new LeadTableCursor(protection, binding);
        cursorCodec.TryParse(query.Cursor, fields, out var cursorUpdatedAt, out var cursorLeadId);
        if (fields.Count != 0)
            return LeadOperationResult<LeadListPage>.Failure(LeadErrors.Validation(fields));

        // AccessControl resolves the record scope once and Leads pushes it into the owner query. A
        // denied scope returns nothing rather than a filtered view of everything.
        if (scope == RecordAccessScopeFilter.Denied)
            return LeadOperationResult<LeadListPage>.Success(new([], null, false, 0));

        var leads = await persistence.ListLeadsAsync(
            trusted.WorkspaceId,
            scopeOwnerId,
            query.OwnerId,
            query.AssignmentState,
            access.Value!.Authorization.CanReadUnassigned,
            workState,
            normalizedSearch,
            includePhoneSearch,
            cursorUpdatedAt,
            cursorLeadId,
            limit + 1,
            cancellationToken);
        var totalCount = await persistence.CountLeadsAsync(
            trusted.WorkspaceId,
            scopeOwnerId,
            query.OwnerId,
            query.AssignmentState,
            access.Value!.Authorization.CanReadUnassigned,
            workState,
            normalizedSearch,
            includePhoneSearch,
            cancellationToken);
        persistence.AddAudit(new LeadAuditRecord(
            "listLeads",
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

internal static class LeadListCursor
{
    internal static string Encode(Lead lead)
    {
        var value = string.Create(CultureInfo.InvariantCulture, $"{lead.UpdatedAt.UtcTicks}\n{lead.LeadId}");
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    internal static bool TryParse(
        string? cursor,
        IDictionary<string, string[]> fields,
        out DateTimeOffset? updatedAt,
        out string? leadId)
    {
        updatedAt = null;
        leadId = null;
        if (cursor is null)
            return true;
        try
        {
            var value = cursor.Replace('-', '+').Replace('_', '/');
            value = value.PadRight(value.Length + ((4 - value.Length % 4) % 4), '=');
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(value)).Split('\n');
            if (decoded.Length != 2
                || !long.TryParse(decoded[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                || ticks < DateTimeOffset.MinValue.UtcTicks
                || ticks > DateTimeOffset.MaxValue.UtcTicks
                || !LeadValidation.IsEntityId(decoded[1]))
            {
                throw new FormatException();
            }
            updatedAt = new DateTimeOffset(ticks, TimeSpan.Zero);
            leadId = decoded[1];
            return true;
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            fields["cursor"] = ["cursor is invalid."];
            return false;
        }
    }
}
