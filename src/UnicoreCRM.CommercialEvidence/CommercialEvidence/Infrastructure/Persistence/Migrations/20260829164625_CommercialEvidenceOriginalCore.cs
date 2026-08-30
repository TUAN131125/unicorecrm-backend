using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.CommercialEvidence.CommercialEvidence.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CommercialEvidenceOriginalCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "commercial_evidence");

            migrationBuilder.CreateTable(
                name: "PurchaseEvidence",
                schema: "commercial_evidence",
                columns: table => new
                {
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    EvidenceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    EvidenceType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false, collation: "Latin1_General_100_BIN2"),
                    BuyerRefType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false, collation: "Latin1_General_100_BIN2"),
                    BuyerRefId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SourceType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, collation: "Latin1_General_100_BIN2"),
                    SourceSystem = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true, collation: "Latin1_General_100_BIN2"),
                    SourceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    PolicyVersion = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseEvidence", x => new { x.WorkspaceId, x.EvidenceId });
                    table.CheckConstraint("CK_PurchaseEvidence_BuyerRefType", "(([BuyerRefType] COLLATE Latin1_General_100_BIN2 = N'CONTACT' AND DATALENGTH([BuyerRefType]) = DATALENGTH(N'CONTACT')) OR ([BuyerRefType] COLLATE Latin1_General_100_BIN2 = N'ORGANIZATION_ACCOUNT' AND DATALENGTH([BuyerRefType]) = DATALENGTH(N'ORGANIZATION_ACCOUNT')))");
                    table.CheckConstraint("CK_PurchaseEvidence_CorrelationId", "DATALENGTH([CorrelationId]) > 0");
                    table.CheckConstraint("CK_PurchaseEvidence_EvidenceType", "(([EvidenceType] COLLATE Latin1_General_100_BIN2 = N'ORDER_COMPLETED' AND DATALENGTH([EvidenceType]) = DATALENGTH(N'ORDER_COMPLETED')) OR ([EvidenceType] COLLATE Latin1_General_100_BIN2 = N'EXTERNAL_PURCHASE_CONFIRMED' AND DATALENGTH([EvidenceType]) = DATALENGTH(N'EXTERNAL_PURCHASE_CONFIRMED')) OR ([EvidenceType] COLLATE Latin1_General_100_BIN2 = N'HISTORICAL_PURCHASE_IMPORTED' AND DATALENGTH([EvidenceType]) = DATALENGTH(N'HISTORICAL_PURCHASE_IMPORTED')))");
                    table.CheckConstraint("CK_PurchaseEvidence_PolicyVersion", "DATALENGTH([PolicyVersion]) > 0");
                    table.CheckConstraint("CK_PurchaseEvidence_SourceId", "DATALENGTH([SourceId]) > 0");
                    table.CheckConstraint("CK_PurchaseEvidence_SourceMapping", "(([SourceType] = N'ORDER' AND [EvidenceType] = N'ORDER_COMPLETED' AND [SourceSystem] IS NULL) OR ([SourceType] = N'EXTERNAL_PURCHASE' AND [EvidenceType] = N'EXTERNAL_PURCHASE_CONFIRMED' AND [SourceSystem] IS NOT NULL AND DATALENGTH([SourceSystem]) > 0) OR ([SourceType] = N'HISTORICAL_IMPORT' AND [EvidenceType] = N'HISTORICAL_PURCHASE_IMPORTED' AND [SourceSystem] IS NOT NULL AND DATALENGTH([SourceSystem]) > 0))");
                    table.CheckConstraint("CK_PurchaseEvidence_SourceType", "(([SourceType] COLLATE Latin1_General_100_BIN2 = N'ORDER' AND DATALENGTH([SourceType]) = DATALENGTH(N'ORDER')) OR ([SourceType] COLLATE Latin1_General_100_BIN2 = N'EXTERNAL_PURCHASE' AND DATALENGTH([SourceType]) = DATALENGTH(N'EXTERNAL_PURCHASE')) OR ([SourceType] COLLATE Latin1_General_100_BIN2 = N'HISTORICAL_IMPORT' AND DATALENGTH([SourceType]) = DATALENGTH(N'HISTORICAL_IMPORT')))");
                });

            migrationBuilder.CreateTable(
                name: "AuditRecords",
                schema: "commercial_evidence",
                columns: table => new
                {
                    AuditId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    EvidenceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Operation = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false, collation: "Latin1_General_100_BIN2"),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    PolicyVersion = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommercialEvidenceAudit", x => x.AuditId);
                    table.CheckConstraint("CK_CommercialEvidenceAudit_Operation", "(([Operation] COLLATE Latin1_General_100_BIN2 = N'ORIGINAL_APPEND' AND DATALENGTH([Operation]) = DATALENGTH(N'ORIGINAL_APPEND')))");
                    table.ForeignKey(
                        name: "FK_CommercialEvidenceAudit_PurchaseEvidence",
                        columns: x => new { x.WorkspaceId, x.EvidenceId },
                        principalSchema: "commercial_evidence",
                        principalTable: "PurchaseEvidence",
                        principalColumns: new[] { "WorkspaceId", "EvidenceId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CommercialEvidenceAudit_Workspace_OccurredAt",
                schema: "commercial_evidence",
                table: "AuditRecords",
                columns: new[] { "WorkspaceId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "UX_CommercialEvidenceAudit_Workspace_Evidence",
                schema: "commercial_evidence",
                table: "AuditRecords",
                columns: new[] { "WorkspaceId", "EvidenceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_PurchaseEvidence_Workspace_Source",
                schema: "commercial_evidence",
                table: "PurchaseEvidence",
                columns: new[] { "WorkspaceId", "SourceType", "SourceSystem", "SourceId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditRecords",
                schema: "commercial_evidence");

            migrationBuilder.DropTable(
                name: "PurchaseEvidence",
                schema: "commercial_evidence");
        }
    }
}
