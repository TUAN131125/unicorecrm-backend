using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Communications.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConversationAssigneeReadScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AssigneeMemberId",
                schema: "communications",
                table: "Conversations",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true,
                collation: "Latin1_General_100_BIN2");

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_WorkspaceId_AssigneeMemberId_LastActivityAt_Id",
                schema: "communications",
                table: "Conversations",
                columns: new[] { "WorkspaceId", "AssigneeMemberId", "LastActivityAt", "Id" },
                descending: new[] { false, false, true, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Conversations_WorkspaceId_AssigneeMemberId_LastActivityAt_Id",
                schema: "communications",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "AssigneeMemberId",
                schema: "communications",
                table: "Conversations");
        }
    }
}
