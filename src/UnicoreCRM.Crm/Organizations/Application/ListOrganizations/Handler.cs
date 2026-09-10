using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UnicoreCRM.Crm.Organizations.Application.Common;
using UnicoreCRM.Crm.Organizations.Contracts;
using UnicoreCRM.Crm.Organizations.Domain;
using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Crm.Organizations.Application.ListOrganizations;

internal sealed record Query(OrganizationRequestMetadata Metadata, string? Q, string? Status,
    string? Industry, string? SizeBand, string? OwnerId, string? Cursor, int Limit);

internal sealed partial class Handler(OrganizationAuthorization authorization, IOrganizationsPersistence persistence, TimeProvider timeProvider)
{
    private static readonly HashSet<string> Statuses = ["prospect", "active", "strategic", "inactive", "archived"];

    internal async Task<OrganizationOperationResult<OrganizationListResponse>> HandleAsync(Query query, CancellationToken cancellationToken)
    {
        var access = await authorization.AuthorizeAsync(query.Metadata, cancellationToken);
        if (!access.IsSuccess) return OrganizationOperationResult<OrganizationListResponse>.Failure(access.Error!);

        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (query.Limit is < 1 or > 250) fields["limit"] = ["limit must be between 1 and 250."];
        var search = Normalize(query.Q, 240, "q", fields);
        var industry = Normalize(query.Industry, 160, "industry", fields)?.ToUpperInvariant();
        var sizeBand = Normalize(query.SizeBand, 80, "sizeBand", fields)?.ToUpperInvariant();
        var status = Normalize(query.Status, 40, "status", fields)?.ToLowerInvariant();
        if (status is not null && !Statuses.Contains(status)) fields["status"] = ["status is invalid."];
        var ownerId = Normalize(query.OwnerId, 128, "ownerId", fields);
        if (ownerId is not null && !EntityIdPattern().IsMatch(ownerId)) fields["ownerId"] = ["ownerId is not a valid entity identifier."];
        OrganizationListCursor.TryParse(query.Cursor, fields, out var cursorCreatedAt, out var cursorOrganizationId);
        if (fields.Count != 0) return OrganizationOperationResult<OrganizationListResponse>.Failure(OrganizationErrors.Validation(fields));

        var scope = access.Value!.Authorization.ScopeFilter;
        if (scope is RecordAccessScopeFilter.Denied or RecordAccessScopeFilter.NotEvaluated)
            return OrganizationOperationResult<OrganizationListResponse>.Success(new([], new(null, false)));
        var scopeOwnerId = scope == RecordAccessScopeFilter.OwnedByMember ? access.Value.Authorization.ScopeOwnerMemberId : null;
        if (scope == RecordAccessScopeFilter.OwnedByMember && scopeOwnerId is null)
            return OrganizationOperationResult<OrganizationListResponse>.Success(new([], new(null, false)));

        var candidates = await persistence.ListOrganizationsAsync(access.Value.Trusted.WorkspaceId, scopeOwnerId,
            ownerId, status, industry, sizeBand, search?.ToUpperInvariant(), cursorCreatedAt,
            cursorOrganizationId, query.Limit + 1, cancellationToken);

        persistence.AddReadAudit(new OrganizationReadAuditRecord("listOrganizations", access.Value.Trusted.WorkspaceId,
            access.Value.Trusted.MemberId, null, query.Metadata.RequestId, query.Metadata.CorrelationId, null, timeProvider.GetUtcNow()));
        await persistence.SaveChangesAsync(cancellationToken);

        var hasNextPage = candidates.Count > query.Limit;
        var page = hasNextPage ? candidates.Take(query.Limit).ToArray() : candidates;
        var nextCursor = hasNextPage ? OrganizationListCursor.Encode(page[^1]) : null;
        var items = page.Select(organization => OrganizationFieldSecurity.Project(
            OrganizationProjection.Document(organization), access.Value.Authorization)).ToArray();
        return OrganizationOperationResult<OrganizationListResponse>.Success(new(items, new(nextCursor, hasNextPage)));
    }

    private static string? Normalize(string? value, int maxLength, string field, IDictionary<string, string[]> fields)
    {
        if (value is null) return null;
        var normalized = value.Trim();
        if (normalized.Length == 0) return null;
        if (normalized.Length > maxLength) fields[field] = [$"{field} cannot contain more than {maxLength} characters."];
        return normalized;
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex EntityIdPattern();
}

internal static partial class OrganizationListCursor
{
    internal static string Encode(Organization organization)
    {
        var value = string.Create(CultureInfo.InvariantCulture, $"{organization.CreatedAt.UtcTicks}\n{organization.OrganizationId}");
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    internal static bool TryParse(string? cursor, IDictionary<string, string[]> fields,
        out DateTimeOffset? createdAt, out string? organizationId)
    {
        createdAt = null;
        organizationId = null;
        if (cursor is null) return true;
        try
        {
            if (cursor.Length is < 1 or > 512 || !CursorPattern().IsMatch(cursor)) throw new FormatException();
            var value = cursor.Replace('-', '+').Replace('_', '/');
            value = value.PadRight(value.Length + ((4 - value.Length % 4) % 4), '=');
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(value)).Split('\n');
            if (decoded.Length != 2 || !long.TryParse(decoded[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                || ticks < DateTimeOffset.MinValue.UtcTicks || ticks > DateTimeOffset.MaxValue.UtcTicks
                || !EntityIdPattern().IsMatch(decoded[1])) throw new FormatException();
            createdAt = new DateTimeOffset(ticks, TimeSpan.Zero);
            organizationId = decoded[1];
            return true;
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            fields["cursor"] = ["cursor is invalid."];
            return false;
        }
    }

    [GeneratedRegex("^[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant)] private static partial Regex CursorPattern();
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)] private static partial Regex EntityIdPattern();
}
