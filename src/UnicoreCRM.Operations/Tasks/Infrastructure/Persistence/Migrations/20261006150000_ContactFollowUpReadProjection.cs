using Microsoft.EntityFrameworkCore.Migrations;

namespace UnicoreCRM.Operations.Tasks.Infrastructure.Persistence.Migrations;

public sealed partial class ContactFollowUpReadProjection : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE VIEW [tasks].[ContactFollowUpReadProjection] AS
        SELECT [WorkspaceId], [RecordId] AS [ContactId], [AssigneeId], MIN([DueAt]) AS [MinDueAt]
        FROM [tasks].[Tasks]
        WHERE [Status] = 0 AND [ArchivedAt] IS NULL
          AND [RecordModuleKey] = N'contacts' AND [RecordId] IS NOT NULL
          AND [RecordId] <> N''
        GROUP BY [WorkspaceId], [RecordId], [AssigneeId]
        """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("DROP VIEW [tasks].[ContactFollowUpReadProjection]");
}
