using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Communications.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConversationLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing customer history has no approved status mapping. Only an explicitly
            // admitted disposable fixture may exercise the neutral NEW synthetic backfill.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM communications.Conversations)
                   AND ISNULL(TRY_CONVERT(int, SESSION_CONTEXT(N'communications.synthetic_lifecycle_backfill')), 0) <> 1
                    THROW 51001, 'COMMUNICATION_LIFECYCLE_BACKFILL_REQUIRES_ADMISSION', 1;
                """);
            migrationBuilder.AddColumn<string>(
                name: "Status",
                schema: "communications",
                table: "Conversations",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "NEW",
                collation: "Latin1_General_100_BIN2");

            migrationBuilder.AddColumn<string>(
                name: "NewStatus",
                schema: "communications",
                table: "AuditRecords",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreviousStatus",
                schema: "communications",
                table: "AuditRecords",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_WorkspaceId_AssigneeMemberId_Status_LastActivityAt_Id",
                schema: "communications",
                table: "Conversations",
                columns: new[] { "WorkspaceId", "AssigneeMemberId", "Status", "LastActivityAt", "Id" },
                descending: new[] { false, false, false, true, true });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Conversations_Status",
                schema: "communications",
                table: "Conversations",
                sql: "[Status] IN ('NEW','OPEN','PENDING','CLOSED')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Conversations_WorkspaceId_AssigneeMemberId_Status_LastActivityAt_Id",
                schema: "communications",
                table: "Conversations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Conversations_Status",
                schema: "communications",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "Status",
                schema: "communications",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "NewStatus",
                schema: "communications",
                table: "AuditRecords");

            migrationBuilder.DropColumn(
                name: "PreviousStatus",
                schema: "communications",
                table: "AuditRecords");
        }
    }
}
