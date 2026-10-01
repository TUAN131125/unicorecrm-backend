using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Platform.Workspace.Infrastructure.Persistence.Migrations
{
    public partial class LeadHandoverAcceptanceSla : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE s
                SET [BlueprintJson] = JSON_MODIFY(s.[BlueprintJson], '$.workflow.handoverAcceptanceSlaHours', 24)
                FROM [workspace].[StudioConfigurations] s
                WHERE NOT EXISTS (
                    SELECT 1 FROM OPENJSON(s.[BlueprintJson], '$.workflow')
                    WHERE [key] = N'handoverAcceptanceSlaHours');
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Preserve configured SLA values: this data backfill cannot be safely reversed.
        }
    }
}
