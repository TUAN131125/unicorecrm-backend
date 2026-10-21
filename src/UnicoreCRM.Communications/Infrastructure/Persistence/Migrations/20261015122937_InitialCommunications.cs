using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Communications.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCommunications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "communications");

            migrationBuilder.CreateTable(
                name: "AuditRecords",
                schema: "communications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ConversationId = table.Column<long>(type: "bigint", nullable: false),
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChannelAccounts",
                schema: "communications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    LocalIdentity = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelAccounts", x => x.Id);
                    table.UniqueConstraint("AK_ChannelAccounts_WorkspaceId_Id", x => new { x.WorkspaceId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "Conversations",
                schema: "communications",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocalIdentity = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    LastActivityAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Conversations", x => x.Id);
                    table.UniqueConstraint("AK_Conversations_WorkspaceId_AccountId_Id", x => new { x.WorkspaceId, x.AccountId, x.Id });
                    table.ForeignKey(
                        name: "FK_Conversations_ChannelAccounts_WorkspaceId_AccountId",
                        columns: x => new { x.WorkspaceId, x.AccountId },
                        principalSchema: "communications",
                        principalTable: "ChannelAccounts",
                        principalColumns: new[] { "WorkspaceId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "ExternalParticipants",
                schema: "communications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocalIdentity = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalParticipants", x => x.Id);
                    table.UniqueConstraint("AK_ExternalParticipants_WorkspaceId_AccountId_Id", x => new { x.WorkspaceId, x.AccountId, x.Id });
                    table.ForeignKey(
                        name: "FK_ExternalParticipants_ChannelAccounts_WorkspaceId_AccountId",
                        columns: x => new { x.WorkspaceId, x.AccountId },
                        principalSchema: "communications",
                        principalTable: "ChannelAccounts",
                        principalColumns: new[] { "WorkspaceId", "Id" });
                });

            migrationBuilder.CreateTable(
                name: "Messages",
                schema: "communications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConversationId = table.Column<long>(type: "bigint", nullable: false),
                    ParticipantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocalIdentity = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", maxLength: 10000, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Messages_Conversations_WorkspaceId_AccountId_ConversationId",
                        columns: x => new { x.WorkspaceId, x.AccountId, x.ConversationId },
                        principalSchema: "communications",
                        principalTable: "Conversations",
                        principalColumns: new[] { "WorkspaceId", "AccountId", "Id" });
                    table.ForeignKey(
                        name: "FK_Messages_ExternalParticipants_WorkspaceId_AccountId_ParticipantId",
                        columns: x => new { x.WorkspaceId, x.AccountId, x.ParticipantId },
                        principalSchema: "communications",
                        principalTable: "ExternalParticipants",
                        principalColumns: new[] { "WorkspaceId", "AccountId", "Id" });
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditRecords_WorkspaceId_RecordedAt",
                schema: "communications",
                table: "AuditRecords",
                columns: new[] { "WorkspaceId", "RecordedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelAccounts_WorkspaceId_LocalIdentity",
                schema: "communications",
                table: "ChannelAccounts",
                columns: new[] { "WorkspaceId", "LocalIdentity" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_WorkspaceId_AccountId_LocalIdentity",
                schema: "communications",
                table: "Conversations",
                columns: new[] { "WorkspaceId", "AccountId", "LocalIdentity" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_WorkspaceId_LastActivityAt_Id",
                schema: "communications",
                table: "Conversations",
                columns: new[] { "WorkspaceId", "LastActivityAt", "Id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalParticipants_WorkspaceId_AccountId_LocalIdentity",
                schema: "communications",
                table: "ExternalParticipants",
                columns: new[] { "WorkspaceId", "AccountId", "LocalIdentity" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Messages_WorkspaceId_AccountId_ConversationId",
                schema: "communications",
                table: "Messages",
                columns: new[] { "WorkspaceId", "AccountId", "ConversationId" });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_WorkspaceId_AccountId_LocalIdentity",
                schema: "communications",
                table: "Messages",
                columns: new[] { "WorkspaceId", "AccountId", "LocalIdentity" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Messages_WorkspaceId_AccountId_ParticipantId",
                schema: "communications",
                table: "Messages",
                columns: new[] { "WorkspaceId", "AccountId", "ParticipantId" });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_WorkspaceId_ConversationId_Sequence",
                schema: "communications",
                table: "Messages",
                columns: new[] { "WorkspaceId", "ConversationId", "Sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditRecords",
                schema: "communications");

            migrationBuilder.DropTable(
                name: "Messages",
                schema: "communications");

            migrationBuilder.DropTable(
                name: "Conversations",
                schema: "communications");

            migrationBuilder.DropTable(
                name: "ExternalParticipants",
                schema: "communications");

            migrationBuilder.DropTable(
                name: "ChannelAccounts",
                schema: "communications");
        }
    }
}
