using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Sales.Orders.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AcceptedQuoteOrderDraft : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Orders_WorkspaceId_SourceQuoteId",
                schema: "orders",
                table: "Orders");

            migrationBuilder.AddColumn<string>(
                name: "AcceptanceJson",
                schema: "orders",
                table: "Orders",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CommercialSnapshotFingerprint",
                schema: "orders",
                table: "Orders",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentAgreementSnapshotJson",
                schema: "orders",
                table: "Orders",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceAcceptanceId",
                schema: "orders",
                table: "Orders",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CreationEvidence",
                schema: "orders",
                columns: table => new
                {
                    AuditId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    QuoteId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    AcceptanceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    OrderId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Fingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreationEvidence", x => x.AuditId);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                schema: "orders",
                columns: table => new
                {
                    EventId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    OrderId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.EventId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_WorkspaceId_SourceQuoteId",
                schema: "orders",
                table: "Orders",
                columns: new[] { "WorkspaceId", "SourceQuoteId" },
                unique: true,
                filter: "[SourceQuoteId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CreationEvidence_WorkspaceId_AcceptanceId",
                schema: "orders",
                table: "CreationEvidence",
                columns: new[] { "WorkspaceId", "AcceptanceId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CreationEvidence",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "OutboxMessages",
                schema: "orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_WorkspaceId_SourceQuoteId",
                schema: "orders",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "AcceptanceJson",
                schema: "orders",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CommercialSnapshotFingerprint",
                schema: "orders",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaymentAgreementSnapshotJson",
                schema: "orders",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "SourceAcceptanceId",
                schema: "orders",
                table: "Orders");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_WorkspaceId_SourceQuoteId",
                schema: "orders",
                table: "Orders",
                columns: new[] { "WorkspaceId", "SourceQuoteId" });
        }
    }
}
