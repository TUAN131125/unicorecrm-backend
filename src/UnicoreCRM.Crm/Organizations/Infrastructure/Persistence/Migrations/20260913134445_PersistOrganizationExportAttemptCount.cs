using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Crm.Organizations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PersistOrganizationExportAttemptCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ExportAttemptCount",
                schema: "organizations",
                table: "OutboxMessages",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExportAttemptCount",
                schema: "organizations",
                table: "OutboxMessages");
        }
    }
}
