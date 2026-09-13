using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Crm.Customers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PersistCustomerExportAttemptCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ExportAttemptCount",
                schema: "customers",
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
                schema: "customers",
                table: "OutboxMessages");
        }
    }
}
