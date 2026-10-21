using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Operations.Tasks.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IndexActivityRecordHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Activities_RecordHistory",
                schema: "tasks",
                table: "Activities",
                columns: new[] { "WorkspaceId", "RecordModuleKey", "RecordId", "OccurredAt", "ActivityId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Activities_RecordHistory",
                schema: "tasks",
                table: "Activities");
        }
    }
}
