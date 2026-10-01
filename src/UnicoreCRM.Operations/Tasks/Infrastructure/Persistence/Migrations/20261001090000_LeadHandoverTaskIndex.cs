using Microsoft.EntityFrameworkCore.Migrations;

namespace UnicoreCRM.Operations.Tasks.Infrastructure.Persistence.Migrations;

public sealed partial class LeadHandoverTaskIndex : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.CreateIndex(
        name: "IX_Tasks_LeadHandover", schema: "tasks", table: "Tasks",
        columns: ["WorkspaceId", "RecordModuleKey", "RecordId", "Status", "ArchivedAt", "TaskId"]);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropIndex(
        name: "IX_Tasks_LeadHandover", schema: "tasks", table: "Tasks");
}
