using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Billing.Invoices.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InvoicesReadCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "invoices");

            migrationBuilder.CreateTable(
                name: "Invoices",
                schema: "invoices",
                columns: table => new
                {
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    InvoiceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    InvoiceNumber = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    BuyerType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    BuyerId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SellerSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BuyerSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LifecycleState = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    DeliveryState = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    IssueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Currency = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    ExchangeRateSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PaymentTerms = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreationIntentId = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    LinesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TotalsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SourceLinksJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ResourceVersion = table.Column<long>(type: "bigint", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    IssuedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                    IssueFailureCode = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    IssueEvidenceJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DiscardedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                    VoidedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                    VoidReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invoices", x => new { x.WorkspaceId, x.InvoiceId });
                    table.CheckConstraint("CK_Invoices_BuyerSnapshotJson", "ISJSON([BuyerSnapshotJson]) = 1");
                    table.CheckConstraint("CK_Invoices_BuyerType", "(([BuyerType] COLLATE Latin1_General_100_BIN2 = N'CONTACT' AND DATALENGTH([BuyerType]) = DATALENGTH(N'CONTACT')) OR ([BuyerType] COLLATE Latin1_General_100_BIN2 = N'ORGANIZATION_ACCOUNT' AND DATALENGTH([BuyerType]) = DATALENGTH(N'ORGANIZATION_ACCOUNT')))");
                    table.CheckConstraint("CK_Invoices_Currency", "[Currency] LIKE '[A-Z][A-Z][A-Z]' COLLATE Latin1_General_100_BIN2 AND DATALENGTH([Currency]) = 6");
                    table.CheckConstraint("CK_Invoices_DeliveryState", "(([DeliveryState] COLLATE Latin1_General_100_BIN2 = N'NOT_SENT' AND DATALENGTH([DeliveryState]) = DATALENGTH(N'NOT_SENT')) OR ([DeliveryState] COLLATE Latin1_General_100_BIN2 = N'SENDING' AND DATALENGTH([DeliveryState]) = DATALENGTH(N'SENDING')) OR ([DeliveryState] COLLATE Latin1_General_100_BIN2 = N'SENT' AND DATALENGTH([DeliveryState]) = DATALENGTH(N'SENT')) OR ([DeliveryState] COLLATE Latin1_General_100_BIN2 = N'DELIVERY_FAILED' AND DATALENGTH([DeliveryState]) = DATALENGTH(N'DELIVERY_FAILED')))");
                    table.CheckConstraint("CK_Invoices_ExchangeRateSnapshotJson", "[ExchangeRateSnapshotJson] IS NULL OR ISJSON([ExchangeRateSnapshotJson]) = 1");
                    table.CheckConstraint("CK_Invoices_IdempotencyKey", "LEN([IdempotencyKey]) >= 8");
                    table.CheckConstraint("CK_Invoices_IssueEvidenceJson", "[IssueEvidenceJson] IS NULL OR ISJSON([IssueEvidenceJson]) = 1");
                    table.CheckConstraint("CK_Invoices_LifecycleState", "(([LifecycleState] COLLATE Latin1_General_100_BIN2 = N'DRAFT' AND DATALENGTH([LifecycleState]) = DATALENGTH(N'DRAFT')) OR ([LifecycleState] COLLATE Latin1_General_100_BIN2 = N'ISSUING' AND DATALENGTH([LifecycleState]) = DATALENGTH(N'ISSUING')) OR ([LifecycleState] COLLATE Latin1_General_100_BIN2 = N'ISSUED' AND DATALENGTH([LifecycleState]) = DATALENGTH(N'ISSUED')) OR ([LifecycleState] COLLATE Latin1_General_100_BIN2 = N'ISSUE_FAILED' AND DATALENGTH([LifecycleState]) = DATALENGTH(N'ISSUE_FAILED')) OR ([LifecycleState] COLLATE Latin1_General_100_BIN2 = N'DISCARDED' AND DATALENGTH([LifecycleState]) = DATALENGTH(N'DISCARDED')) OR ([LifecycleState] COLLATE Latin1_General_100_BIN2 = N'VOIDED' AND DATALENGTH([LifecycleState]) = DATALENGTH(N'VOIDED')))");
                    table.CheckConstraint("CK_Invoices_LinesJson", "ISJSON([LinesJson]) = 1");
                    table.CheckConstraint("CK_Invoices_ResourceVersion", "[ResourceVersion] >= 0");
                    table.CheckConstraint("CK_Invoices_SellerSnapshotJson", "ISJSON([SellerSnapshotJson]) = 1");
                    table.CheckConstraint("CK_Invoices_SourceLinksJson", "ISJSON([SourceLinksJson]) = 1");
                    table.CheckConstraint("CK_Invoices_TotalsJson", "ISJSON([TotalsJson]) = 1");
                });

            migrationBuilder.CreateTable(
                name: "ReadAuditRecords",
                schema: "invoices",
                columns: table => new
                {
                    AuditId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RecordId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    RequestId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ResourceVersion = table.Column<long>(type: "bigint", nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadAuditRecords", x => x.AuditId);
                    table.CheckConstraint("CK_ReadAuditRecords_Outcome", "(([Outcome] COLLATE Latin1_General_100_BIN2 = N'READ' AND DATALENGTH([Outcome]) = DATALENGTH(N'READ')))");
                    table.CheckConstraint("CK_ReadAuditRecords_ResourceVersion", "[ResourceVersion] IS NULL OR [ResourceVersion] >= 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReadAuditRecords_WorkspaceId_OccurredAt",
                schema: "invoices",
                table: "ReadAuditRecords",
                columns: new[] { "WorkspaceId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Invoices",
                schema: "invoices");

            migrationBuilder.DropTable(
                name: "ReadAuditRecords",
                schema: "invoices");
        }
    }
}
