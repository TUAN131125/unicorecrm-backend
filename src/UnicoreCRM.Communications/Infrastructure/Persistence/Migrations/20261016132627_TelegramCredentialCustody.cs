using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Communications.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TelegramCredentialCustody : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TelegramConnections",
                schema: "communications",
                columns: table => new
                {
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    CredentialId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Ciphertext = table.Column<string>(type: "nvarchar(max)", maxLength: 4096, nullable: true),
                    ReplayKeyCiphertext = table.Column<string>(type: "nvarchar(max)", maxLength: 4096, nullable: false),
                    CredentialGeneration = table.Column<long>(type: "bigint", nullable: false),
                    VerifiedGeneration = table.Column<long>(type: "bigint", nullable: true),
                    VerifiedConfigurationVersion = table.Column<long>(type: "bigint", nullable: true),
                    LastVerifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Disconnected = table.Column<bool>(type: "bit", nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelegramConnections", x => x.AccountId);
                    table.CheckConstraint("CK_TelegramConnections_Generation", "[CredentialGeneration] >= 0");
                    table.ForeignKey(
                        name: "FK_TelegramConnections_ChannelAccounts_WorkspaceId_AccountId",
                        columns: x => new { x.WorkspaceId, x.AccountId },
                        principalSchema: "communications",
                        principalTable: "ChannelAccounts",
                        principalColumns: new[] { "WorkspaceId", "Id" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_TelegramConnections_WorkspaceId_AccountId",
                schema: "communications",
                table: "TelegramConnections",
                columns: new[] { "WorkspaceId", "AccountId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TelegramConnections",
                schema: "communications");
        }
    }
}
