using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UnicoreCRM.Crm.Customers.Application.Common;
using UnicoreCRM.Crm.Customers.Domain;

namespace UnicoreCRM.Crm.Customers.Infrastructure.Persistence;

internal sealed partial class EfCustomersPersistence
{
    public async Task<IReadOnlyList<Customer>> SearchCustomersAsync(CustomerSearchPage query, CancellationToken ct)
    {
        // The existing 1000-candidate materialization budget is unchanged. Matching happens
        // in SQL first; unrelated Customer rows never consume that application budget.
        if (query.Take is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(query));
        var started = System.Diagnostics.Stopwatch.StartNew();
        const string denied = "SELECT CAST(NULL AS nvarchar(128)) AS SourceId WHERE 1=0";
        var connection = dbContext.Database.GetDbConnection();
        var opened = connection.State != ConnectionState.Open;
        if (opened) await connection.OpenAsync(ct);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandTimeout = 15;
            // Only server-authored owner ID projections are composed here. All request values
            // remain parameters; no PII is copied into Customer storage or query diagnostics.
            command.CommandText = $$"""
                /* CustomerSearch.OwnerIds.v1 */
                SELECT TOP (@take) c.CustomerId FROM customers.Customers c
                WHERE c.WorkspaceId=@ws
                AND (@scopeOwner IS NULL OR c.OwnerId=@scopeOwner)
                AND (@owner IS NULL OR c.OwnerId=@owner)
                AND (@type IS NULL OR c.Type=@type)
                AND ((@status IS NULL AND c.Status<>N'ARCHIVED') OR c.Status=@status)
                AND (@segment IS NULL OR c.Segment=@segment) AND (@tier IS NULL OR c.Tier=@tier)
                AND (@cursorAt IS NULL OR c.CreatedAt<@cursorAt OR (c.CreatedAt=@cursorAt AND c.CustomerId>@cursorId))
                AND ((@readCode=1 AND CHARINDEX(@search,UPPER(c.CustomerCode) COLLATE Latin1_General_100_BIN2)>0)
                  OR (@readSegment=1 AND CHARINDEX(@search,UPPER(c.Segment) COLLATE Latin1_General_100_BIN2)>0)
                  OR (@readTier=1 AND CHARINDEX(@search,UPPER(c.Tier) COLLATE Latin1_General_100_BIN2)>0)
                  OR (@readTags=1 AND EXISTS(SELECT 1 FROM OPENJSON(c.Profile,'$.tags') t WHERE CHARINDEX(@search,UPPER(t.value) COLLATE Latin1_General_100_BIN2)>0))
                  OR (c.RelationshipType=N'CONTACT' AND EXISTS(SELECT 1 FROM ({{query.Contacts?.Sql ?? denied}}) source WHERE source.SourceId=c.RelationshipId))
                  OR (c.RelationshipType=N'ORGANIZATION_ACCOUNT' AND EXISTS(SELECT 1 FROM ({{query.Organizations?.Sql ?? denied}}) source WHERE source.SourceId=c.RelationshipId)))
                ORDER BY c.CreatedAt DESC,c.CustomerId ASC
                """;
            var values = new Dictionary<string, object?> {
                ["@take"] = query.Take, ["@ws"] = query.WorkspaceId, ["@scopeOwner"] = query.ScopeOwnerId,
                ["@owner"] = query.OwnerId, ["@type"] = query.Type, ["@status"] = query.Status,
                ["@segment"] = query.Segment, ["@tier"] = query.Tier, ["@search"] = query.Search,
                ["@cursorId"] = query.CursorId, ["@readCode"] = query.ReadableFields.Contains("customerCode"),
                ["@readSegment"] = query.ReadableFields.Contains("segment"), ["@readTier"] = query.ReadableFields.Contains("tier"),
                ["@readTags"] = query.ReadableFields.Contains("tags") };
            foreach (var plan in new[] { query.Contacts, query.Organizations })
                if (plan is not null) foreach (var parameter in plan.Parameters) values.Add(parameter.Key, parameter.Value);
            foreach (var (name, value) in values)
                command.Parameters.Add(new SqlParameter(name, value ?? DBNull.Value));
            command.Parameters.Add(new SqlParameter("@cursorAt", SqlDbType.DateTimeOffset) { Value = (object?)query.CursorAt ?? DBNull.Value });
            var ids = new List<string>();
            await using (var reader = await command.ExecuteReaderAsync(ct))
                while (await reader.ReadAsync(ct)) ids.Add(reader.GetString(0));
            if (ids.Count == 0)
            {
                logger?.LogInformation("Customer owner search: SqlReadQueries={SqlReadQueries}, SourceRowsMaterialized={SourceRowsMaterialized}, CustomerRows={CustomerRows}, ElapsedMs={ElapsedMs}", 1, 0, 0, started.ElapsedMilliseconds);
                return [];
            }
            // A fixed second owner query hydrates only this bounded page, preserving ordering.
            var result = await dbContext.Customers.AsNoTracking().Where(c => c.WorkspaceId == query.WorkspaceId && ids.Contains(c.CustomerId)
                    && (query.ScopeOwnerId == null || c.OwnerId == query.ScopeOwnerId)
                    && (query.OwnerId == null || c.OwnerId == query.OwnerId))
                .OrderByDescending(c => c.CreatedAt).ThenBy(c => c.CustomerId).ToArrayAsync(ct);
            logger?.LogInformation("Customer owner search: SqlReadQueries={SqlReadQueries}, SourceRowsMaterialized={SourceRowsMaterialized}, CustomerRows={CustomerRows}, ElapsedMs={ElapsedMs}", 2, 0, result.Length, started.ElapsedMilliseconds);
            return result;
        }
        finally { if (opened) await connection.CloseAsync(); }
    }
}
