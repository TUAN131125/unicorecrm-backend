using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Communications.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConversationRecordLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NewTargetId",
                schema: "communications",
                table: "AuditRecords",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NewTargetType",
                schema: "communications",
                table: "AuditRecords",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreviousTargetId",
                schema: "communications",
                table: "AuditRecords",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreviousTargetType",
                schema: "communications",
                table: "AuditRecords",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ConversationRecordLinks",
                schema: "communications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConversationId = table.Column<long>(type: "bigint", nullable: false),
                    TargetType = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false, collation: "Latin1_General_100_BIN2"),
                    TargetId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    LinkedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LinkedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    UnlinkedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UnlinkedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationRecordLinks", x => x.Id);
                    table.CheckConstraint("CK_RecordLinks_TargetType", "[TargetType] IN ('LEAD','CONTACT')");
                    table.ForeignKey(
                        name: "FK_ConversationRecordLinks_Conversations_WorkspaceId_AccountId_ConversationId",
                        columns: x => new { x.WorkspaceId, x.AccountId, x.ConversationId },
                        principalSchema: "communications",
                        principalTable: "Conversations",
                        principalColumns: new[] { "WorkspaceId", "AccountId", "Id" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationRecordLinks_WorkspaceId_AccountId_ConversationId",
                schema: "communications",
                table: "ConversationRecordLinks",
                columns: new[] { "WorkspaceId", "AccountId", "ConversationId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationRecordLinks_WorkspaceId_ConversationId",
                schema: "communications",
                table: "ConversationRecordLinks",
                columns: new[] { "WorkspaceId", "ConversationId" },
                unique: true,
                filter: "[UnlinkedAt] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConversationRecordLinks",
                schema: "communications");

            migrationBuilder.DropColumn(
                name: "NewTargetId",
                schema: "communications",
                table: "AuditRecords");

            migrationBuilder.DropColumn(
                name: "NewTargetType",
                schema: "communications",
                table: "AuditRecords");

            migrationBuilder.DropColumn(
                name: "PreviousTargetId",
                schema: "communications",
                table: "AuditRecords");

            migrationBuilder.DropColumn(
                name: "PreviousTargetType",
                schema: "communications",
                table: "AuditRecords");
        }
    }
}
