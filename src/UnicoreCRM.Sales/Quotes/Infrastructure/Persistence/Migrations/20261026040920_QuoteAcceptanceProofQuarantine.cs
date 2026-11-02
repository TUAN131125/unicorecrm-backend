using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Sales.Quotes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class QuoteAcceptanceProofQuarantine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_Documents_WorkspaceId_QuoteId_DocumentId",
                schema: "quotes",
                table: "Documents",
                columns: new[] { "WorkspaceId", "QuoteId", "DocumentId" });

            migrationBuilder.CreateTable(
                name: "AcceptanceCases",
                schema: "quotes",
                columns: table => new
                {
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CaseId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    QuoteId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RootQuoteId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    QuoteRevision = table.Column<int>(type: "int", nullable: false),
                    DocumentId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CommercialFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    IssuedPdfSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    BuyerType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    BuyerId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PolicyVersion = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    CaseStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ResourceVersion = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceptanceCases", x => new { x.WorkspaceId, x.CaseId });
                    table.CheckConstraint("CK_AcceptanceCase_State", "[CaseStatus] COLLATE Latin1_General_100_BIN2 IN ('COLLECTING_PROOFS','QUARANTINED')");
                    table.CheckConstraint("CK_AcceptanceCase_Version", "[QuoteRevision]>=1 AND [ResourceVersion]>=1");
                    table.ForeignKey(
                        name: "FK_AcceptanceCases_Documents_WorkspaceId_QuoteId_DocumentId",
                        columns: x => new { x.WorkspaceId, x.QuoteId, x.DocumentId },
                        principalSchema: "quotes",
                        principalTable: "Documents",
                        principalColumns: new[] { "WorkspaceId", "QuoteId", "DocumentId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcceptanceProofs",
                schema: "quotes",
                columns: table => new
                {
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ProofId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CaseId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    EvidenceType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    MediaType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    FileSize = table.Column<int>(type: "int", nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PolicyVersion = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    StorageStatus = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ScanStatus = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    UploadedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    UploadedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Retention = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ResourceVersion = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceptanceProofs", x => new { x.WorkspaceId, x.ProofId });
                    table.UniqueConstraint("AK_AcceptanceProofs_WorkspaceId_CaseId_ProofId", x => new { x.WorkspaceId, x.CaseId, x.ProofId });
                    table.CheckConstraint("CK_AcceptanceProof_Bounds", "[FileSize] BETWEEN 1 AND 10485760 AND [ResourceVersion]=1 AND [MediaType]='application/pdf'");
                    table.CheckConstraint("CK_AcceptanceProof_Quarantine", "[StorageStatus] COLLATE Latin1_General_100_BIN2 = 'QUARANTINED' AND [ScanStatus] COLLATE Latin1_General_100_BIN2 = 'SCAN_PENDING'");
                    table.CheckConstraint("CK_AcceptanceProof_Type", "[EvidenceType] COLLATE Latin1_General_100_BIN2 IN ('CUSTOMER_CONSENT','IDENTITY','DELEGATION','SUPPORTING_DOCUMENT')");
                    table.ForeignKey(
                        name: "FK_AcceptanceProofs_AcceptanceCases_WorkspaceId_CaseId",
                        columns: x => new { x.WorkspaceId, x.CaseId },
                        principalSchema: "quotes",
                        principalTable: "AcceptanceCases",
                        principalColumns: new[] { "WorkspaceId", "CaseId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcceptanceProofContents",
                schema: "quotes",
                columns: table => new
                {
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ProofId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CaseId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ProtectedBytes = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ContentLength = table.Column<int>(type: "int", nullable: false),
                    ProtectionFormat = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceptanceProofContents", x => new { x.WorkspaceId, x.ProofId });
                    table.CheckConstraint("CK_AcceptanceProofContent_Bounds", "[ContentLength] BETWEEN 1 AND 10485760 AND DATALENGTH([ProtectedBytes]) > [ContentLength]");
                    table.ForeignKey(
                        name: "FK_AcceptanceProofContents_AcceptanceProofs_WorkspaceId_CaseId_ProofId",
                        columns: x => new { x.WorkspaceId, x.CaseId, x.ProofId },
                        principalSchema: "quotes",
                        principalTable: "AcceptanceProofs",
                        principalColumns: new[] { "WorkspaceId", "CaseId", "ProofId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AcceptanceCases_WorkspaceId_QuoteId_CreatedAt_CaseId",
                schema: "quotes",
                table: "AcceptanceCases",
                columns: new[] { "WorkspaceId", "QuoteId", "CreatedAt", "CaseId" });

            migrationBuilder.CreateIndex(
                name: "IX_AcceptanceCases_WorkspaceId_QuoteId_DocumentId",
                schema: "quotes",
                table: "AcceptanceCases",
                columns: new[] { "WorkspaceId", "QuoteId", "DocumentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceptanceProofContents_WorkspaceId_CaseId_ProofId",
                schema: "quotes",
                table: "AcceptanceProofContents",
                columns: new[] { "WorkspaceId", "CaseId", "ProofId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceptanceProofs_WorkspaceId_CaseId_Sha256",
                schema: "quotes",
                table: "AcceptanceProofs",
                columns: new[] { "WorkspaceId", "CaseId", "Sha256" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceptanceProofs_WorkspaceId_CaseId_UploadedAt_ProofId",
                schema: "quotes",
                table: "AcceptanceProofs",
                columns: new[] { "WorkspaceId", "CaseId", "UploadedAt", "ProofId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AcceptanceProofContents",
                schema: "quotes");

            migrationBuilder.DropTable(
                name: "AcceptanceProofs",
                schema: "quotes");

            migrationBuilder.DropTable(
                name: "AcceptanceCases",
                schema: "quotes");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Documents_WorkspaceId_QuoteId_DocumentId",
                schema: "quotes",
                table: "Documents");
        }
    }
}
