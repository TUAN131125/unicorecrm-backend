using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Communications.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TelegramInboundPolling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TelegramAdmissions",
                schema: "communications",
                columns: table => new
                {
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdateId = table.Column<long>(type: "bigint", nullable: false),
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Classification = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CommittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelegramAdmissions", x => new { x.AccountId, x.UpdateId });
                    table.ForeignKey(
                        name: "FK_TelegramAdmissions_ChannelAccounts_WorkspaceId_AccountId",
                        columns: x => new { x.WorkspaceId, x.AccountId },
                        principalSchema: "communications",
                        principalTable: "ChannelAccounts",
                        principalColumns: new[] { "WorkspaceId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "TelegramPolling",
                schema: "communications",
                columns: table => new
                {
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    BotId = table.Column<long>(type: "bigint", nullable: false),
                    Synthetic = table.Column<bool>(type: "bit", nullable: false),
                    AdmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ConnectionGeneration = table.Column<long>(type: "bigint", nullable: false),
                    NextOffset = table.Column<long>(type: "bigint", nullable: false),
                    Fence = table.Column<long>(type: "bigint", nullable: false),
                    LeaseOwner = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LeaseExpiry = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastAttemptAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastSuccessfulPollAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastCommittedUpdateAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FailureClassification = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    FailureCount = table.Column<int>(type: "int", nullable: false),
                    RetryAfter = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelegramPolling", x => x.AccountId);
                    table.CheckConstraint("CK_TelegramPolling_Progress", "[BotId] > 0 AND [NextOffset] >= 0 AND [Fence] >= 0 AND [ConnectionGeneration] > 0 AND [FailureCount] >= 0");
                    table.ForeignKey(
                        name: "FK_TelegramPolling_ChannelAccounts_WorkspaceId_AccountId",
                        columns: x => new { x.WorkspaceId, x.AccountId },
                        principalSchema: "communications",
                        principalTable: "ChannelAccounts",
                        principalColumns: new[] { "WorkspaceId", "Id" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_TelegramAdmissions_WorkspaceId_AccountId",
                schema: "communications",
                table: "TelegramAdmissions",
                columns: new[] { "WorkspaceId", "AccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_TelegramPolling_BotId",
                schema: "communications",
                table: "TelegramPolling",
                column: "BotId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TelegramPolling_RetryAfter_LastAttemptAt",
                schema: "communications",
                table: "TelegramPolling",
                columns: new[] { "RetryAfter", "LastAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TelegramPolling_WorkspaceId_AccountId",
                schema: "communications",
                table: "TelegramPolling",
                columns: new[] { "WorkspaceId", "AccountId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TelegramAdmissions",
                schema: "communications");

            migrationBuilder.DropTable(
                name: "TelegramPolling",
                schema: "communications");
        }
    }
}
