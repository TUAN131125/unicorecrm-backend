using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Platform.AccessControl.Infrastructure.Persistence.Migrations;

public partial class RemoveHumanLeadHandoverCapability : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Capability upgrades already invalidate the directory without changing role identity.
        // Preserve Version=0 so untouched initial roles remain eligible for historical upgrades.
        migrationBuilder.Sql("""
            DECLARE @RemovedRoles TABLE ([RoleId] nvarchar(128) PRIMARY KEY);

            DELETE FROM [access].[RoleCapabilities]
            OUTPUT DELETED.[RoleId] INTO @RemovedRoles ([RoleId])
            WHERE [Capability] COLLATE Latin1_General_100_BIN2 = N'leads.handover';

            UPDATE directory
            SET [Revision] = directory.[Revision] + 1
            FROM [access].[WorkspaceDirectoryRevisions] directory
            WHERE EXISTS (
                SELECT 1 FROM [access].[Roles] role
                INNER JOIN @RemovedRoles removed ON removed.[RoleId] = role.[RoleId]
                WHERE role.[WorkspaceId] = directory.[WorkspaceId]);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // The removed human capability is obsolete. Rollback must not recreate authority.
    }
}
