using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UnicoreCRM.Crm.Customers.Application.Common;
using UnicoreCRM.Crm.Customers.Contracts;
using UnicoreCRM.Crm.Customers.Domain;
using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Crm.Customers.Application.ListCustomers;

internal sealed record Query(CustomerRequestMetadata Metadata, string? Q, string? Type, string? Status,
    string? OwnerId, string? Segment, string? Tier, string? Cursor, int Limit);

internal sealed partial class Handler(CustomerAuthorization authorization, ICustomersPersistence persistence, TimeProvider timeProvider)
{
    private static readonly HashSet<string> Types = ["B2C", "B2B"];
    private static readonly HashSet<string> Statuses = ["NEW", "ACTIVE", "AT_RISK", "INACTIVE", "CHURNED", "DO_NOT_CONTACT", "ARCHIVED"];
    private static readonly HashSet<string> Tiers = ["STANDARD", "SILVER", "GOLD", "PLATINUM", "STRATEGIC"];

    internal async Task<CustomerOperationResult<CustomerListResponse>> HandleAsync(Query query, CancellationToken cancellationToken)
    {
        var access = await authorization.AuthorizeAsync(query.Metadata, cancellationToken);
        if (!access.IsSuccess) return CustomerOperationResult<CustomerListResponse>.Failure(access.Error!);
        var fields = new Dictionary<string, string[]>();
        if (query.Limit is < 1 or > 250) fields["limit"] = ["limit must be between 1 and 250."];
        var search = Normalize(query.Q, 240, "q", fields)?.ToUpperInvariant();
        var type = Normalize(query.Type, 8, "type", fields)?.ToUpperInvariant();
        var status = Normalize(query.Status, 40, "status", fields)?.ToUpperInvariant();
        var ownerId = Normalize(query.OwnerId, 128, "ownerId", fields);
        var segment = Normalize(query.Segment, 160, "segment", fields)?.ToUpperInvariant();
        var tier = Normalize(query.Tier, 40, "tier", fields)?.ToUpperInvariant();
        if (type is not null && !Types.Contains(type)) fields["type"] = ["type is invalid."];
        if (status is not null && !Statuses.Contains(status)) fields["status"] = ["status is invalid."];
        if (tier is not null && !Tiers.Contains(tier)) fields["tier"] = ["tier is invalid."];
        if (ownerId is not null && !EntityIdPattern().IsMatch(ownerId)) fields["ownerId"] = ["ownerId is invalid."];
        CustomerListCursor.TryParse(query.Cursor, fields, out var cursorCreatedAt, out var cursorCustomerId);
        if (fields.Count != 0) return CustomerOperationResult<CustomerListResponse>.Failure(CustomerErrors.Validation(fields));

        var scope = access.Value!.Authorization.ScopeFilter;
        if (scope is RecordAccessScopeFilter.Denied or RecordAccessScopeFilter.NotEvaluated)
            return CustomerOperationResult<CustomerListResponse>.Success(new([], new(null, false)));
        var scopeOwner = scope == RecordAccessScopeFilter.OwnedByMember ? access.Value.Authorization.ScopeOwnerMemberId : null;
        if (scope == RecordAccessScopeFilter.OwnedByMember && scopeOwner is null)
            return CustomerOperationResult<CustomerListResponse>.Success(new([], new(null, false)));
        if (scope is not (RecordAccessScopeFilter.Workspace or RecordAccessScopeFilter.OwnedByMember))
            return CustomerOperationResult<CustomerListResponse>.Success(new([], new(null, false)));

        var candidates = await persistence.ListCustomersAsync(access.Value.Trusted.WorkspaceId, scopeOwner, ownerId,
            type, status, segment, tier, search, cursorCreatedAt, cursorCustomerId, query.Limit + 1, cancellationToken);
        persistence.AddReadAudit(new CustomerReadAuditRecord("listCustomers", access.Value.Trusted.WorkspaceId,
            access.Value.Trusted.MemberId, null, query.Metadata.RequestId, query.Metadata.CorrelationId, null, timeProvider.GetUtcNow()));
        await persistence.SaveChangesAsync(cancellationToken);
        var hasNext = candidates.Count > query.Limit;
        var page = hasNext ? candidates.Take(query.Limit).ToArray() : candidates;
        var next = hasNext ? CustomerListCursor.Encode(page[^1]) : null;
        var items = page.Select(x => CustomerFieldSecurity.Project(CustomerProjection.Document(x), access.Value.Authorization)).ToArray();
        return CustomerOperationResult<CustomerListResponse>.Success(new(items, new(next, hasNext)));
    }

    private static string? Normalize(string? value, int max, string field, IDictionary<string, string[]> fields)
    { if (value is null) return null; var v = value.Trim(); if (v.Length == 0) return null; if (v.Length > max) fields[field] = [$"{field} must not exceed {max} characters."]; return v; }
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)] private static partial Regex EntityIdPattern();
}

internal static partial class CustomerListCursor
{
    internal static string Encode(Customer customer)
    {
        var value = string.Create(CultureInfo.InvariantCulture, $"{customer.CreatedAt.UtcTicks}\n{customer.CustomerId}");
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
    internal static bool TryParse(string? cursor, IDictionary<string, string[]> fields, out DateTimeOffset? createdAt, out string? customerId)
    {
        createdAt = null; customerId = null; if (cursor is null) return true;
        try
        {
            if (cursor.Length is < 1 or > 512 || !CursorPattern().IsMatch(cursor)) throw new FormatException();
            var value = cursor.Replace('-', '+').Replace('_', '/'); value = value.PadRight(value.Length + ((4 - value.Length % 4) % 4), '=');
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(value)).Split('\n');
            if (decoded.Length != 2 || !long.TryParse(decoded[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                || ticks < DateTimeOffset.MinValue.UtcTicks || ticks > DateTimeOffset.MaxValue.UtcTicks || !EntityIdPattern().IsMatch(decoded[1])) throw new FormatException();
            createdAt = new DateTimeOffset(ticks, TimeSpan.Zero); customerId = decoded[1]; return true;
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        { fields["cursor"] = ["cursor is invalid."]; return false; }
    }
    [GeneratedRegex("^[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant)] private static partial Regex CursorPattern();
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)] private static partial Regex EntityIdPattern();
}
