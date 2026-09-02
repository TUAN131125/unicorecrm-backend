using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Platform.AccessControl.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GetWorkspaceAccessDirectoryReadAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DirectoryReadAccessRecords",
                schema: "access",
                columns: table => new
                {
                    EvidenceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    OperationId = table.Column<string>(type: "nvarchar(96)", maxLength: 96, nullable: false),
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ActorAccountId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ActorMembershipId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ActorMemberId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RequestId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DirectoryReadAccessRecords", x => x.EvidenceId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DirectoryReadAccessRecords_WorkspaceId_OperationId_OccurredAt",
                schema: "access",
                table: "DirectoryReadAccessRecords",
                columns: new[] { "WorkspaceId", "OperationId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DirectoryReadAccessRecords",
                schema: "access");
        }
    }
}
