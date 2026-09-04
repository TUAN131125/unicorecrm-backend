using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Crm.Leads.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LeadWorkingSetAndOpportunity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DealRef",
                schema: "leads",
                table: "Leads",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SearchText",
                schema: "leads",
                table: "Leads",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: false,
                defaultValue: "");

            // Existing rows predate the derived projection. Backfill from the same required fields
            // used by Lead.BuildSearchText so a migrated populated Workspace is searchable
            // immediately rather than only after each Lead is edited.
            migrationBuilder.Sql(
                """
                UPDATE [leads].[Leads]
                SET [SearchText] = UPPER(CONCAT(
                    [LeadId], CHAR(10),
                    COALESCE(JSON_VALUE([Profile], '$.displayName'), ''), CHAR(10),
                    COALESCE(JSON_VALUE([Profile], '$.source'), '')))
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DealRef",
                schema: "leads",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "SearchText",
                schema: "leads",
                table: "Leads");
        }
    }
}
