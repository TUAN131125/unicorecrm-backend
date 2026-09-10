using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Crm.Organizations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OrganizationAuthoritativeListProjections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Organizations_WorkspaceId_CreatedAt_OrganizationId",
                schema: "organizations",
                table: "Organizations");

            migrationBuilder.DropIndex(
                name: "IX_Organizations_WorkspaceId_OwnerId_CreatedAt_OrganizationId",
                schema: "organizations",
                table: "Organizations");

            migrationBuilder.AddColumn<string>(
                name: "Industry",
                schema: "organizations",
                table: "Organizations",
                type: "nvarchar(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SearchText",
                schema: "organizations",
                table: "Organizations",
                type: "nvarchar(800)",
                maxLength: 800,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SizeBand",
                schema: "organizations",
                table: "Organizations",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            // Backfill query projections from the Organization-owned aggregate/profile. OwnerId
            // is intentionally untouched: pre-O Organizations remain unowned.
            migrationBuilder.Sql("""
                UPDATE [organizations].[Organizations]
                SET [Industry] = NULLIF(LEFT(UPPER(LTRIM(RTRIM(JSON_VALUE([Profile], '$.industry')))), 160), N''),
                    [SizeBand] = NULLIF(LEFT(UPPER(LTRIM(RTRIM(JSON_VALUE([Profile], '$.sizeBand')))), 80), N''),
                    [SearchText] = LEFT(UPPER(CONCAT(
                        [DisplayName], N' ',
                        COALESCE(JSON_VALUE([Profile], '$.legalName'), N''), N' ',
                        COALESCE(JSON_VALUE([Profile], '$.taxCode'), N''), N' ',
                        COALESCE(JSON_VALUE([Profile], '$.domain'), N''))), 800)
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_WorkspaceId_Industry",
                schema: "organizations",
                table: "Organizations",
                columns: new[] { "WorkspaceId", "Industry" });

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_WorkspaceId_OwnerId_Status_CreatedAt_OrganizationId",
                schema: "organizations",
                table: "Organizations",
                columns: new[] { "WorkspaceId", "OwnerId", "Status", "CreatedAt", "OrganizationId" },
                descending: new[] { false, false, false, true, false });

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_WorkspaceId_SizeBand",
                schema: "organizations",
                table: "Organizations",
                columns: new[] { "WorkspaceId", "SizeBand" });

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_WorkspaceId_Status_CreatedAt_OrganizationId",
                schema: "organizations",
                table: "Organizations",
                columns: new[] { "WorkspaceId", "Status", "CreatedAt", "OrganizationId" },
                descending: new[] { false, false, true, false });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Organizations_WorkspaceId_Industry",
                schema: "organizations",
                table: "Organizations");

            migrationBuilder.DropIndex(
                name: "IX_Organizations_WorkspaceId_OwnerId_Status_CreatedAt_OrganizationId",
                schema: "organizations",
                table: "Organizations");

            migrationBuilder.DropIndex(
                name: "IX_Organizations_WorkspaceId_SizeBand",
                schema: "organizations",
                table: "Organizations");

            migrationBuilder.DropIndex(
                name: "IX_Organizations_WorkspaceId_Status_CreatedAt_OrganizationId",
                schema: "organizations",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "Industry",
                schema: "organizations",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "SearchText",
                schema: "organizations",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "SizeBand",
                schema: "organizations",
                table: "Organizations");

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_WorkspaceId_CreatedAt_OrganizationId",
                schema: "organizations",
                table: "Organizations",
                columns: new[] { "WorkspaceId", "CreatedAt", "OrganizationId" },
                descending: new[] { false, true, false });

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_WorkspaceId_OwnerId_CreatedAt_OrganizationId",
                schema: "organizations",
                table: "Organizations",
                columns: new[] { "WorkspaceId", "OwnerId", "CreatedAt", "OrganizationId" });
        }
    }
}
