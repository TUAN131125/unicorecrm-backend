using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.PlatformOperations.Inbox.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialInboundDeliveryInbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ops");

            migrationBuilder.CreateTable(
                name: "InboxMessages",
                schema: "ops",
                columns: table => new
                {
                    InboxMessageId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    IntegrationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    DeliveryId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PayloadHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ProviderCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    DelegatedMemberId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    ResultLeadId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    LastResultCode = table.Column<string>(type: "nvarchar(96)", maxLength: 96, nullable: true),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboxMessages", x => x.InboxMessageId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InboxMessages_IntegrationId_DeliveryId",
                schema: "ops",
                table: "InboxMessages",
                columns: new[] { "IntegrationId", "DeliveryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InboxMessages_Status_UpdatedAt",
                schema: "ops",
                table: "InboxMessages",
                columns: new[] { "Status", "UpdatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InboxMessages",
                schema: "ops");
        }
    }
}
