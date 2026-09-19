using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.PlatformOperations.AiExecution.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CreateAiExecutionLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "platform_ai");

            migrationBuilder.CreateTable(
                name: "AiExecutions",
                schema: "platform_ai",
                columns: table => new
                {
                    ExecutionId = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    MemberId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Model = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    DurationMilliseconds = table.Column<long>(type: "bigint", nullable: false),
                    ContextTypesJson = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    EvidenceIdentifiersJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    InputTokens = table.Column<int>(type: "int", nullable: true),
                    OutputTokens = table.Column<int>(type: "int", nullable: true),
                    ProviderRequestId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ErrorCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiExecutions", x => x.ExecutionId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiExecutions_WorkspaceId_StartedAt",
                schema: "platform_ai",
                table: "AiExecutions",
                columns: new[] { "WorkspaceId", "StartedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiExecutions",
                schema: "platform_ai");
        }
    }
}
