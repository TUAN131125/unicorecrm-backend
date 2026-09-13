using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Crm.Contacts.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PersistContactExportAttemptCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ExportAttemptCount",
                schema: "contacts",
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
                schema: "contacts",
                table: "OutboxMessages");
        }
    }
}
