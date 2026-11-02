using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Sales.Quotes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAcceptanceProofScanLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AcceptanceProofScanAttempts",
                schema: "quotes",
                columns: table => new
                {
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    AttemptId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CaseId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ProofId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    LeaseId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Epoch = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    AttemptNumber = table.Column<int>(type: "int", nullable: false),
                    PolicyVersion = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    PolicyDigest = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RuntimeDigest = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    InputSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    InputLength = table.Column<long>(type: "bigint", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceptanceProofScanAttempts", x => new { x.WorkspaceId, x.AttemptId });
                    table.UniqueConstraint("AK_AcceptanceProofScanAttempts_WorkspaceId_CaseId_ProofId_AttemptId", x => new { x.WorkspaceId, x.CaseId, x.ProofId, x.AttemptId });
                    table.CheckConstraint("CK_ProofScanAttempt_Bounds", "[AttemptNumber] BETWEEN 1 AND 12 AND [InputLength] BETWEEN 1 AND 10485760 AND [LeaseExpiresAt]>[StartedAt]");
                    table.ForeignKey(
                        name: "FK_AcceptanceProofScanAttempts_AcceptanceProofs_WorkspaceId_CaseId_ProofId",
                        columns: x => new { x.WorkspaceId, x.CaseId, x.ProofId },
                        principalSchema: "quotes",
                        principalTable: "AcceptanceProofs",
                        principalColumns: new[] { "WorkspaceId", "CaseId", "ProofId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcceptanceProofScanJobs",
                schema: "quotes",
                columns: table => new
                {
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ProofId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CaseId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    InputSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    InputLength = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Epoch = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    AttemptId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    LeaseId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LifetimeAttempts = table.Column<int>(type: "int", nullable: false),
                    EpochAttempts = table.Column<int>(type: "int", nullable: false),
                    ResourceVersion = table.Column<long>(type: "bigint", nullable: false),
                    LastResultAttemptId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    InterventionReason = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceptanceProofScanJobs", x => new { x.WorkspaceId, x.ProofId });
                    table.CheckConstraint("CK_ProofScanJob_Bounds", "[LifetimeAttempts] BETWEEN 0 AND 12 AND [EpochAttempts] BETWEEN 0 AND 3 AND [EpochAttempts]<=[LifetimeAttempts] AND [ResourceVersion]>=1 AND [InputLength] BETWEEN 1 AND 10485760");
                    table.CheckConstraint("CK_ProofScanJob_Lease", "([Status]='SCANNING' AND [AttemptId] IS NOT NULL AND [LeaseId] IS NOT NULL AND [LeaseExpiresAt] IS NOT NULL) OR ([Status]<>'SCANNING' AND [LeaseId] IS NULL AND [LeaseExpiresAt] IS NULL)");
                    table.CheckConstraint("CK_ProofScanJob_State", "[Status] COLLATE Latin1_General_100_BIN2 IN ('SCAN_PENDING','SCANNING','CLEAN','UNSAFE','INVALID_PDF','SCAN_FAILED','NEEDS_INTERVENTION')");
                    table.ForeignKey(
                        name: "FK_AcceptanceProofScanJobs_AcceptanceProofs_WorkspaceId_CaseId_ProofId",
                        columns: x => new { x.WorkspaceId, x.CaseId, x.ProofId },
                        principalSchema: "quotes",
                        principalTable: "AcceptanceProofs",
                        principalColumns: new[] { "WorkspaceId", "CaseId", "ProofId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AcceptanceProofScanResults",
                schema: "quotes",
                columns: table => new
                {
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    AttemptId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CaseId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ProofId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ReasonCode = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    ParserStatus = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    MalwareVerdict = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ProvenanceJson = table.Column<string>(type: "nvarchar(max)", maxLength: 16000, nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceptanceProofScanResults", x => new { x.WorkspaceId, x.AttemptId });
                    table.CheckConstraint("CK_ProofScanResult_Clean", "[Outcome]<>'CLEAN' OR ([ParserStatus]='PASS' AND [MalwareVerdict]='CLEAN')");
                    table.CheckConstraint("CK_ProofScanResult_Json", "ISJSON([ProvenanceJson])=1");
                    table.CheckConstraint("CK_ProofScanResult_Outcome", "[Outcome] COLLATE Latin1_General_100_BIN2 IN ('CLEAN','UNSAFE','INVALID_PDF','SCAN_FAILED')");
                    table.ForeignKey(
                        name: "FK_AcceptanceProofScanResults_AcceptanceProofScanAttempts_WorkspaceId_CaseId_ProofId_AttemptId",
                        columns: x => new { x.WorkspaceId, x.CaseId, x.ProofId, x.AttemptId },
                        principalSchema: "quotes",
                        principalTable: "AcceptanceProofScanAttempts",
                        principalColumns: new[] { "WorkspaceId", "CaseId", "ProofId", "AttemptId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AcceptanceProofScanAttempts_WorkspaceId_ProofId_AttemptNumber",
                schema: "quotes",
                table: "AcceptanceProofScanAttempts",
                columns: new[] { "WorkspaceId", "ProofId", "AttemptNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcceptanceProofScanJobs_WorkspaceId_CaseId_ProofId",
                schema: "quotes",
                table: "AcceptanceProofScanJobs",
                columns: new[] { "WorkspaceId", "CaseId", "ProofId" });

            migrationBuilder.CreateIndex(
                name: "IX_AcceptanceProofScanJobs_WorkspaceId_Status_NextAttemptAt",
                schema: "quotes",
                table: "AcceptanceProofScanJobs",
                columns: new[] { "WorkspaceId", "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AcceptanceProofScanResults_WorkspaceId_CaseId_ProofId_AttemptId",
                schema: "quotes",
                table: "AcceptanceProofScanResults",
                columns: new[] { "WorkspaceId", "CaseId", "ProofId", "AttemptId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AcceptanceProofScanJobs",
                schema: "quotes");

            migrationBuilder.DropTable(
                name: "AcceptanceProofScanResults",
                schema: "quotes");

            migrationBuilder.DropTable(
                name: "AcceptanceProofScanAttempts",
                schema: "quotes");
        }
    }
}
