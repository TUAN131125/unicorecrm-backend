using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Communications.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ChannelAccountManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChannelType",
                schema: "communications",
                table: "ChannelAccounts",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ConfigurationVersion",
                schema: "communications",
                table: "ChannelAccounts",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                schema: "communications",
                table: "ChannelAccounts",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                schema: "communications",
                table: "ChannelAccounts",
                type: "nvarchar(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderKey",
                schema: "communications",
                table: "ChannelAccounts",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true,
                collation: "Latin1_General_100_BIN2");

            migrationBuilder.AddColumn<string>(
                name: "Status",
                schema: "communications",
                table: "ChannelAccounts",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true,
                collation: "Latin1_General_100_BIN2");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                schema: "communications",
                table: "ChannelAccounts",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ChannelMutations",
                schema: "communications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorMemberId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    RequestKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Fingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ResponseJson = table.Column<string>(type: "nvarchar(max)", maxLength: 4096, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelMutations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChannelMutations_ChannelAccounts_WorkspaceId_AccountId",
                        columns: x => new { x.WorkspaceId, x.AccountId },
                        principalSchema: "communications",
                        principalTable: "ChannelAccounts",
                        principalColumns: new[] { "WorkspaceId", "Id" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelAccounts_WorkspaceId_ProviderKey_Status_CreatedAt_Id",
                schema: "communications",
                table: "ChannelAccounts",
                columns: new[] { "WorkspaceId", "ProviderKey", "Status", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelAccounts_WorkspaceId_CreatedAt_Id",
                schema: "communications",
                table: "ChannelAccounts",
                columns: new[] { "WorkspaceId", "CreatedAt", "Id" },
                descending: new[] { false, true, true });

            migrationBuilder.AddCheckConstraint(
                name: "CK_ChannelAccounts_Metadata",
                schema: "communications",
                table: "ChannelAccounts",
                sql: "([ProviderKey] IS NULL AND [ChannelType] IS NULL AND [DisplayName] IS NULL AND [Status] IS NULL AND [CreatedAt] IS NULL AND [UpdatedAt] IS NULL AND [ConfigurationVersion] = 0) OR ([ProviderKey] IS NOT NULL AND [ChannelType] IS NOT NULL AND [DisplayName] IS NOT NULL AND [Status] IS NOT NULL AND [Status] IN ('DRAFT','DISABLED') AND [CreatedAt] IS NOT NULL AND [UpdatedAt] IS NOT NULL AND [ConfigurationVersion] > 0)");

            migrationBuilder.CreateIndex(
                name: "IX_ChannelMutations_WorkspaceId_AccountId_RecordedAt",
                schema: "communications",
                table: "ChannelMutations",
                columns: new[] { "WorkspaceId", "AccountId", "RecordedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelMutations_WorkspaceId_ActorMemberId_RequestKey",
                schema: "communications",
                table: "ChannelMutations",
                columns: new[] { "WorkspaceId", "ActorMemberId", "RequestKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex("IX_ChannelAccounts_WorkspaceId_CreatedAt_Id", "ChannelAccounts", "communications");
            migrationBuilder.DropTable(
                name: "ChannelMutations",
                schema: "communications");

            migrationBuilder.DropIndex(
                name: "IX_ChannelAccounts_WorkspaceId_ProviderKey_Status_CreatedAt_Id",
                schema: "communications",
                table: "ChannelAccounts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ChannelAccounts_Metadata",
                schema: "communications",
                table: "ChannelAccounts");

            migrationBuilder.DropColumn(
                name: "ChannelType",
                schema: "communications",
                table: "ChannelAccounts");

            migrationBuilder.DropColumn(
                name: "ConfigurationVersion",
                schema: "communications",
                table: "ChannelAccounts");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                schema: "communications",
                table: "ChannelAccounts");

            migrationBuilder.DropColumn(
                name: "DisplayName",
                schema: "communications",
                table: "ChannelAccounts");

            migrationBuilder.DropColumn(
                name: "ProviderKey",
                schema: "communications",
                table: "ChannelAccounts");

            migrationBuilder.DropColumn(
                name: "Status",
                schema: "communications",
                table: "ChannelAccounts");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                schema: "communications",
                table: "ChannelAccounts");
        }
    }
}
