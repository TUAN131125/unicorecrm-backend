using System.Data;
using Microsoft.Data.SqlClient;
using UnicoreCRM.Operations.Tasks.Contracts;
using UnicoreCRM.Operations.Tasks.Domain;
using UnicoreCRM.Platform.AccessControl.Contracts;

namespace UnicoreCRM.Operations.Tasks.Infrastructure.Persistence;

internal static class ActivityReadSql
{
    internal sealed record Query(string Sql, object[] Parameters);

    internal static Query CommitGuard(string workspaceId, TaskReferenceData references, IReadOnlyList<ActivityReadScope> scopes)
    {
        var source = Facts(workspaceId, scopes, true);
        var parameters = source.Parameters.ToList();
        void Parameter(string name, string? value) => parameters.Add(new SqlParameter(name, SqlDbType.NVarChar, 128) { Value = (object?)value ?? DBNull.Value });
        Parameter("@recordModule", references.RecordModuleKey ?? Application.Common.ActivityRecordAccess.RelationshipModule(references.RelationshipType));
        Parameter("@recordId", references.RecordId ?? references.RelationshipId);
        Parameter("@relationshipType", references.RelationshipType);
        Parameter("@relationshipId", references.RelationshipId);
        Parameter("@relationshipModule", Application.Common.ActivityRecordAccess.RelationshipModule(references.RelationshipType));
        // Executed on the Tasks SERIALIZABLE connection. The exact owner rows/relationship
        // ranges read by this proof stay protected until the Activity/receipt commit.
        var sql = $"""
            SELECT CAST(CASE WHEN
                EXISTS (SELECT 1 FROM ({source.Sql}) p
                        WHERE p.ModuleKey=@recordModule AND p.RecordId=@recordId
                          AND (@relationshipType IS NULL OR
                               (p.RelationshipType=@relationshipType AND p.RelationshipId=@relationshipId)))
                AND (@relationshipType IS NULL OR
                     EXISTS (SELECT 1 FROM ({source.Sql}) r
                             WHERE r.ModuleKey=@relationshipModule AND r.RecordId=@relationshipId))
                THEN 1 ELSE 0 END AS int) AS [Value]
            """;
        return new(sql, parameters.ToArray());
    }

    internal static Query Build(string workspaceId, IReadOnlyList<ActivityReadScope> scopes)
    {
        var source = Facts(workspaceId, scopes, false);
        if (source.Parameters.Length == 0) return new("SELECT a.* FROM tasks.Activities a WHERE 1=0", []);
        var facts = source.Sql;
        const string module = "COALESCE(a.RecordModuleKey, CASE a.RelationshipType WHEN N'CONTACT' THEN N'contacts' WHEN N'ORGANIZATION_ACCOUNT' THEN N'organizations' END)";
        const string id = "COALESCE(a.RecordId,a.RelationshipId)";
        var sql = $"""
            SELECT a.* FROM tasks.Activities a
            WHERE a.WorkspaceId=@activityWorkspace AND a.Type<>5
              AND a.SourceEvidence IS NULL
              AND ((a.SourceType IS NULL AND a.SourceId IS NULL) OR
                   (a.SourceId={id} AND
                    (({module}=N'leads' AND a.SourceType IN (N'LEAD_ACTIVITY',N'LEAD_DETAIL')) OR
                     ({module}=N'contacts' AND a.SourceType IN (N'CONTACT_ACTIVITY',N'CONTACT_DETAIL')) OR
                     ({module}=N'organizations' AND a.SourceType IN (N'ORGANIZATION_ACTIVITY',N'ORGANIZATION_DETAIL')) OR
                     ({module}=N'customers' AND a.SourceType IN (N'CUSTOMER_ACTIVITY',N'CUSTOMER_DETAIL',N'CUSTOMER_360')))))
              AND EXISTS (SELECT 1 FROM ({facts}) p
                          WHERE p.ModuleKey={module} AND p.RecordId={id}
                            AND (a.RelationshipType IS NULL OR
                                 (p.RelationshipType=a.RelationshipType AND p.RelationshipId=a.RelationshipId)))
              AND (a.RelationshipType IS NULL OR
                   EXISTS (SELECT 1 FROM ({facts}) r
                           WHERE r.RecordId=a.RelationshipId AND
                            ((a.RelationshipType=N'CONTACT' AND r.ModuleKey=N'contacts') OR
                             (a.RelationshipType=N'ORGANIZATION_ACCOUNT' AND r.ModuleKey=N'organizations'))))
            """;
        return new(sql, source.Parameters);
    }
    private static Query Facts(string workspaceId, IReadOnlyList<ActivityReadScope> scopes, bool forWrite)
    {
        if (scopes.Count == 0) return new("SELECT CAST(NULL AS nvarchar(100)) AS ModuleKey, CAST(NULL AS nvarchar(128)) AS RecordId, CAST(NULL AS nvarchar(32)) AS RelationshipType, CAST(NULL AS nvarchar(128)) AS RelationshipId WHERE 1=0", []);
        var parameters = new List<object> { new SqlParameter("@activityWorkspace", SqlDbType.NVarChar, 128) { Value = workspaceId } };
        var projections = new List<string>();
        foreach (var scope in scopes)
        {
            var policy = scope.Authorization;
            if (policy.ScopeFilter is not (RecordAccessScopeFilter.Workspace or RecordAccessScopeFilter.OwnedByMember)) continue;
            var index = projections.Count;
            parameters.Add(new SqlParameter($"@module{index}", SqlDbType.NVarChar, 100) { Value = scope.ModuleKey });
            parameters.Add(new SqlParameter($"@owner{index}", SqlDbType.NVarChar, 128) { Value = (object?)policy.ScopeOwnerMemberId ?? DBNull.Value });
            parameters.Add(new SqlParameter($"@owned{index}", SqlDbType.Bit) { Value = policy.ScopeFilter == RecordAccessScopeFilter.OwnedByMember });
            parameters.Add(new SqlParameter($"@queue{index}", SqlDbType.Bit) { Value = policy.CanReadUnassigned });
            parameters.Add(new SqlParameter($"@restrict{index}", SqlDbType.Bit) { Value = policy.RestrictUnassigned });
            // FactsSql is static owner persistence code, never HTTP input. All request/policy
            // values are parameters. Translate the AccessControl collection filter, not roles.
            projections.Add($"""
                SELECT @module{index} AS ModuleKey, f.RecordId, f.RelationshipType, f.RelationshipId
                FROM ({scope.FactsSql}) f
                WHERE f.WorkspaceId=@activityWorkspace
                  AND (@owned{index}=0 OR f.OwnerId=@owner{index} OR (@queue{index}=1 AND f.OwnerId IS NULL))
                  AND (@restrict{index}=0 OR @queue{index}=1 OR f.OwnerId IS NOT NULL)
                  {(forWrite ? "AND f.CanLog=1" : "")}
                """);
        }
        if (projections.Count == 0) return Facts(workspaceId, [], forWrite);
        return new(string.Join("\nUNION ALL\n", projections), parameters.ToArray());
    }

}
