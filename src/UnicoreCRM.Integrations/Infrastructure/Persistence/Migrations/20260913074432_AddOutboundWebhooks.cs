using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Integrations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboundWebhooks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OutboundWebhookAudit",
                schema: "integration",
                columns: table => new
                {
                    AuditId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(96)", maxLength: 96, nullable: false),
                    TargetId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboundWebhookAudit", x => x.AuditId);
                });

            migrationBuilder.CreateTable(
                name: "OutboundWebhookConsumerCursors",
                schema: "integration",
                columns: table => new
                {
                    ConsumerId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboundWebhookConsumerCursors", x => x.ConsumerId);
                });

            migrationBuilder.CreateTable(
                name: "OutboundWebhookDeliveries",
                schema: "integration",
                columns: table => new
                {
                    DeliveryId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SubscriptionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    EventId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    CanonicalPayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExecutionAttemptId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastErrorCode = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboundWebhookDeliveries", x => x.DeliveryId);
                });

            migrationBuilder.CreateTable(
                name: "OutboundWebhookDeliveryAttempts",
                schema: "integration",
                columns: table => new
                {
                    AttemptId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DeliveryId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    AttemptNumber = table.Column<int>(type: "int", nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ErrorCode = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboundWebhookDeliveryAttempts", x => x.AttemptId);
                });

            migrationBuilder.CreateTable(
                name: "OutboundWebhookIdempotency",
                schema: "integration",
                columns: table => new
                {
                    ScopeKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(96)", maxLength: 96, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Fingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ResponseJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboundWebhookIdempotency", x => x.ScopeKey);
                });

            migrationBuilder.CreateTable(
                name: "OutboundWebhookSubscriptions",
                schema: "integration",
                columns: table => new
                {
                    SubscriptionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    EndpointUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ProtectedSecret = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboundWebhookSubscriptions", x => x.SubscriptionId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutboundWebhookAudit_WorkspaceId_OccurredAt",
                schema: "integration",
                table: "OutboundWebhookAudit",
                columns: new[] { "WorkspaceId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OutboundWebhookDeliveries_Status_NextAttemptAt_LeaseExpiresAt",
                schema: "integration",
                table: "OutboundWebhookDeliveries",
                columns: new[] { "Status", "NextAttemptAt", "LeaseExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OutboundWebhookDeliveries_SubscriptionId_EventId",
                schema: "integration",
                table: "OutboundWebhookDeliveries",
                columns: new[] { "SubscriptionId", "EventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboundWebhookDeliveries_WorkspaceId_CreatedAt",
                schema: "integration",
                table: "OutboundWebhookDeliveries",
                columns: new[] { "WorkspaceId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OutboundWebhookDeliveryAttempts_DeliveryId_AttemptNumber",
                schema: "integration",
                table: "OutboundWebhookDeliveryAttempts",
                columns: new[] { "DeliveryId", "AttemptNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboundWebhookSubscriptions_WorkspaceId_EventType_Status",
                schema: "integration",
                table: "OutboundWebhookSubscriptions",
                columns: new[] { "WorkspaceId", "EventType", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OutboundWebhookAudit",
                schema: "integration");

            migrationBuilder.DropTable(
                name: "OutboundWebhookConsumerCursors",
                schema: "integration");

            migrationBuilder.DropTable(
                name: "OutboundWebhookDeliveries",
                schema: "integration");

            migrationBuilder.DropTable(
                name: "OutboundWebhookDeliveryAttempts",
                schema: "integration");

            migrationBuilder.DropTable(
                name: "OutboundWebhookIdempotency",
                schema: "integration");

            migrationBuilder.DropTable(
                name: "OutboundWebhookSubscriptions",
                schema: "integration");
        }
    }
}
