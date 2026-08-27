using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Operations.Tasks.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TaskOwnScopeIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Tasks_WorkspaceId_AssigneeId_UpdatedAt_TaskId",
                schema: "tasks",
                table: "Tasks",
                columns: new[] { "WorkspaceId", "AssigneeId", "UpdatedAt", "TaskId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tasks_WorkspaceId_AssigneeId_UpdatedAt_TaskId",
                schema: "tasks",
                table: "Tasks");
        }
    }
}
