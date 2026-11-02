using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Sales.Quotes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class QuoteSalesAcceptance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AcceptanceInvitations",
                schema: "quotes",
                columns: table => new
                {
                    InvitationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    QuoteId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    DocumentId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    QuoteVersion = table.Column<long>(type: "bigint", nullable: false),
                    CommercialFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ProtectedDeliveryPayload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RecipientEmail = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ConsentedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DeliveryStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    DeliveryErrorCode = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    DeliveredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceptanceInvitations", x => x.InvitationId);
                });

            migrationBuilder.CreateTable(
                name: "SalesAcceptances",
                schema: "quotes",
                columns: table => new
                {
                    AcceptanceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    QuoteId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    AssuranceLevel = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Evidence = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    DocumentId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CommercialFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PolicyVersion = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    OperationalApprovalRequired = table.Column<bool>(type: "bit", nullable: false),
                    CustomerAgreedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AcceptedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ConversionStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    OrderId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    LastErrorCode = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesAcceptances", x => new { x.WorkspaceId, x.AcceptanceId });
                    table.CheckConstraint("CK_SalesAcceptances_Assurance", "[AssuranceLevel] = 'ORDINARY_SALES_UNVERIFIED'");
                    table.CheckConstraint("CK_SalesAcceptances_Snapshot", "ISJSON([SnapshotJson]) = 1");
                    table.CheckConstraint("CK_SalesAcceptances_Source", "[Source] IN ('CUSTOMER_LINK','STAFF_RECORDED')");
                    table.ForeignKey(
                        name: "FK_SalesAcceptances_Quotes_WorkspaceId_QuoteId",
                        columns: x => new { x.WorkspaceId, x.QuoteId },
                        principalSchema: "quotes",
                        principalTable: "Quotes",
                        principalColumns: new[] { "WorkspaceId", "QuoteId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AcceptanceInvitations_TokenHash",
                schema: "quotes",
                table: "AcceptanceInvitations",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceptanceInvitations_WorkspaceId_QuoteId",
                schema: "quotes",
                table: "AcceptanceInvitations",
                columns: new[] { "WorkspaceId", "QuoteId" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesAcceptances_ConversionStatus_NextAttemptAt",
                schema: "quotes",
                table: "SalesAcceptances",
                columns: new[] { "ConversionStatus", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesAcceptances_WorkspaceId_QuoteId",
                schema: "quotes",
                table: "SalesAcceptances",
                columns: new[] { "WorkspaceId", "QuoteId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AcceptanceInvitations",
                schema: "quotes");

            migrationBuilder.DropTable(
                name: "SalesAcceptances",
                schema: "quotes");
        }
    }
}
