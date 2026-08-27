using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Platform.AccessControl.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecordAccessDecisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RecordAccessDecisions",
                schema: "access",
                columns: table => new
                {
                    DecisionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    MembershipId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    MemberId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ResourceKey = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    RecordId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    RequiredCapability = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Allowed = table.Column<bool>(type: "bit", nullable: false),
                    EvaluatedScope = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    DecisionCode = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    RequestId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    OwnerMatch = table.Column<bool>(type: "bit", nullable: true),
                    EvaluatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecordAccessDecisions", x => x.DecisionId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecordAccessDecisions_WorkspaceId_MembershipId_EvaluatedAt",
                schema: "access",
                table: "RecordAccessDecisions",
                columns: new[] { "WorkspaceId", "MembershipId", "EvaluatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RecordAccessDecisions_WorkspaceId_ResourceKey_RecordId",
                schema: "access",
                table: "RecordAccessDecisions",
                columns: new[] { "WorkspaceId", "ResourceKey", "RecordId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecordAccessDecisions",
                schema: "access");
        }
    }
}
