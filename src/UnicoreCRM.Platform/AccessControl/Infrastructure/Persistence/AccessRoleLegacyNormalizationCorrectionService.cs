using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace UnicoreCRM.Platform.AccessControl.Infrastructure.Persistence;

/// <summary>
/// Repairs the one historical createAccessRole normalization defect using the exact runtime
/// implementation. SQL UPPER is deliberately not used: its Unicode behavior is collation-specific
/// and cannot establish parity with string.Trim().ToUpperInvariant().
/// </summary>
internal sealed class AccessRoleLegacyNormalizationCorrectionService(
    AccessControlDbContext dbContext,
    ILogger<AccessRoleLegacyNormalizationCorrectionService> logger)
{
    private const int RequiredColumnCount = 7;
    private const int ApplicationLockTimeoutMilliseconds = 60_000;

    internal async Task RunAsync(CancellationToken cancellationToken)
    {
        if (!await EnsureCorrectedStorageIsPresentAsync(dbContext, cancellationToken))
        {
            logger.LogDebug("AccessControl Roles persistence is absent; no legacy role normalization correction is required.");
            return;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            await using var lockCommand = dbContext.Database.GetDbConnection().CreateCommand();
            lockCommand.Transaction = transaction.GetDbTransaction();
            lockCommand.CommandText =
                """
                DECLARE @result int;
                EXEC @result = sys.sp_getapplock
                    @Resource = N'UnicoreCRM.AccessControl.AccessRoleLegacyNormalization',
                    @LockMode = 'Exclusive',
                    @LockOwner = 'Transaction',
                    @LockTimeout = 60000;
                SELECT @result;
                """;
            var lockResult = Convert.ToInt32(
                await lockCommand.ExecuteScalarAsync(cancellationToken),
                CultureInfo.InvariantCulture);
            if (lockResult < 0)
            {
                throw new InvalidOperationException(
                    $"Could not acquire the AccessRole legacy-normalization upgrade lock within {ApplicationLockTimeoutMilliseconds} ms (sp_getapplock result {lockResult}).");
            }

            var workspaceIds = await dbContext.Roles
                .AsNoTracking()
                .Select(role => role.WorkspaceId)
                .Distinct()
                .OrderBy(workspaceId => workspaceId)
                .ToArrayAsync(cancellationToken);

            var workspacesRequiringRepair = new List<string>();
            foreach (var workspaceId in workspaceIds)
            {
                var roles = await ReadWorkspaceRolesAsync(dbContext, workspaceId, cancellationToken);
                var canonicalOwners = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                var requiresRepair = false;
                foreach (var role in roles)
                {
                    var canonical = CanonicalName(role.Name);
                    if (!canonicalOwners.TryGetValue(canonical, out var roleIds))
                    {
                        roleIds = [];
                        canonicalOwners.Add(canonical, roleIds);
                    }
                    roleIds.Add(role.RoleId);
                    requiresRepair |= !string.Equals(role.NormalizedName, canonical, StringComparison.Ordinal);
                }

                var collision = canonicalOwners.FirstOrDefault(entry => entry.Value.Count > 1);
                if (collision.Value is not null)
                {
                    throw new InvalidOperationException(
                        $"AccessRole legacy normalization collision in Workspace '{workspaceId}': roles [{string.Join(", ", collision.Value.Order(StringComparer.Ordinal))}] normalize to the same canonical key. Resolve the conflicting role names before restarting; no role row was changed.");
                }
                if (requiresRepair)
                    workspacesRequiringRepair.Add(workspaceId);
            }

            var repaired = 0;
            foreach (var workspaceId in workspacesRequiringRepair)
            {
                var roles = await ReadWorkspaceRolesAsync(dbContext, workspaceId, cancellationToken);
                var repairs = roles
                    .Select(role => new RoleRepair(role.RoleId, role.NormalizedName, CanonicalName(role.Name)))
                    .Where(role => !string.Equals(role.CurrentNormalizedName, role.CanonicalNormalizedName, StringComparison.Ordinal))
                    .ToArray();
                var occupied = roles.Select(role => role.NormalizedName).ToHashSet(StringComparer.Ordinal);

                foreach (var repair in repairs)
                {
                    string placeholder;
                    do
                    {
                        placeholder = $"__UNICORE_LEGACY_NORMALIZATION_{Guid.NewGuid():N}";
                    }
                    while (!occupied.Add(placeholder));

                    var affected = await dbContext.Database.ExecuteSqlInterpolatedAsync(
                        $"""
                        UPDATE [access].[Roles]
                        SET [NormalizedName] = {placeholder}
                        WHERE [WorkspaceId] = {workspaceId}
                          AND [RoleId] = {repair.RoleId}
                          AND [NormalizedName] = {repair.CurrentNormalizedName}
                        """,
                        cancellationToken);
                    if (affected != 1)
                        throw new InvalidOperationException($"AccessRole '{repair.RoleId}' changed while its legacy normalization was being corrected.");
                    repair.Placeholder = placeholder;
                }

                foreach (var repair in repairs)
                {
                    var affected = await dbContext.Database.ExecuteSqlInterpolatedAsync(
                        $"""
                        UPDATE [access].[Roles]
                        SET [NormalizedName] = {repair.CanonicalNormalizedName}
                        WHERE [WorkspaceId] = {workspaceId}
                          AND [RoleId] = {repair.RoleId}
                          AND [NormalizedName] = {repair.Placeholder}
                        """,
                        cancellationToken);
                    if (affected != 1)
                        throw new InvalidOperationException($"AccessRole '{repair.RoleId}' could not be finalized during legacy normalization correction.");
                }
                repaired += repairs.Length;
            }

            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation(
                "AccessControl verified exact legacy AccessRole normalization and repaired {RoleCount} role rows.",
                repaired);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static Task<LegacyRole[]> ReadWorkspaceRolesAsync(
        AccessControlDbContext dbContext,
        string workspaceId,
        CancellationToken cancellationToken) =>
        dbContext.Roles
            .AsNoTracking()
            .Where(role => role.WorkspaceId == workspaceId)
            .OrderBy(role => role.RoleId)
            .Select(role => new LegacyRole(role.RoleId, role.Name, role.NormalizedName))
            .ToArrayAsync(cancellationToken);

    private static string CanonicalName(string name)
    {
        var canonical = name.Trim().ToUpperInvariant();
        if (canonical.Length > 320)
            throw new InvalidOperationException("A historical AccessRole canonical name exceeds the corrected UTF-16 storage boundary.");
        return canonical;
    }

    private static async Task<bool> EnsureCorrectedStorageIsPresentAsync(
        AccessControlDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var rolesTableExists = await dbContext.Database.SqlQueryRaw<int>(
                """
                SELECT COUNT(*) AS [Value]
                FROM sys.tables AS tableDefinition
                INNER JOIN sys.schemas AS schemaDefinition ON schemaDefinition.[schema_id] = tableDefinition.[schema_id]
                WHERE schemaDefinition.[name] = N'access'
                  AND tableDefinition.[name] = N'Roles'
                """)
            .SingleAsync(cancellationToken);
        if (rolesTableExists == 0)
            return false;

        var correctedColumns = await dbContext.Database.SqlQueryRaw<int>(
                """
                SELECT COUNT(*) AS [Value]
                FROM sys.columns AS columnDefinition
                INNER JOIN sys.tables AS tableDefinition ON tableDefinition.[object_id] = columnDefinition.[object_id]
                INNER JOIN sys.schemas AS schemaDefinition ON schemaDefinition.[schema_id] = tableDefinition.[schema_id]
                WHERE schemaDefinition.[name] = N'access'
                  AND
                  (
                      (tableDefinition.[name] = N'Roles' AND columnDefinition.[name] IN (N'Name', N'NormalizedName', N'SourceTemplateId') AND columnDefinition.[max_length] >= 640)
                      OR (tableDefinition.[name] = N'Roles' AND columnDefinition.[name] = N'Description' AND columnDefinition.[max_length] >= 2000)
                      OR (tableDefinition.[name] = N'RoleDataScopes' AND columnDefinition.[name] = N'ResourceKey' AND columnDefinition.[max_length] >= 640)
                      OR (tableDefinition.[name] = N'RoleFieldSecurity' AND columnDefinition.[name] IN (N'ResourceKey', N'FieldKey') AND columnDefinition.[max_length] >= 640)
                  )
                """)
            .SingleAsync(cancellationToken);
        if (correctedColumns != RequiredColumnCount)
        {
            throw new InvalidOperationException(
                "The AccessControl Unicode-storage correction migration has not been applied. Expected seven widened createAccessRole columns before legacy normalization correction.");
        }
        return true;
    }

    private sealed record LegacyRole(string RoleId, string Name, string NormalizedName);

    private sealed record RoleRepair(
        string RoleId,
        string CurrentNormalizedName,
        string CanonicalNormalizedName)
    {
        internal string Placeholder { get; set; } = null!;
    }
}
