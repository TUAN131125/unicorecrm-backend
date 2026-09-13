using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Operations.Tasks.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PersistTaskExportAttemptCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ExportAttemptCount",
                schema: "tasks",
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
                schema: "tasks",
                table: "OutboxMessages");
        }
    }
}
