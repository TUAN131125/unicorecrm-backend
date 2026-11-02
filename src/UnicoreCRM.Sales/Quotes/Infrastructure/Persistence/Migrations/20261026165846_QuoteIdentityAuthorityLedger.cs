using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Sales.Quotes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class QuoteIdentityAuthorityLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_AcceptanceCases_WorkspaceId_CaseId_QuoteId",
                schema: "quotes",
                table: "AcceptanceCases",
                columns: new[] { "WorkspaceId", "CaseId", "QuoteId" });

            migrationBuilder.CreateTable(
                name: "AcceptanceEvidenceSourceVersions",
                schema: "quotes",
                columns: table => new
                {
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SourceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ResourceVersion = table.Column<long>(type: "bigint", nullable: false),
                    BuyerType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    BuyerId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcceptanceEvidenceSourceVersions", x => new { x.WorkspaceId, x.SourceId, x.ResourceVersion });
                    table.CheckConstraint("CK_AcceptanceSourceVersion_Buyer", "[BuyerType] COLLATE Latin1_General_100_BIN2 IN ('CONTACT','ORGANIZATION_ACCOUNT')");
                    table.CheckConstraint("CK_AcceptanceSourceVersion_Json", "ISJSON([SnapshotJson])=1 AND DATALENGTH([SnapshotJson])<=32768");
                    table.CheckConstraint("CK_AcceptanceSourceVersion_Number", "[ResourceVersion]>=1");
                    table.CheckConstraint("CK_AcceptanceSourceVersion_State", "[Status] COLLATE Latin1_General_100_BIN2 IN ('DRAFT','REVOKED')");
                });

            migrationBuilder.CreateTable(
                name: "IdentityAuthorityVersions",
                schema: "quotes",
                columns: table => new
                {
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CaseId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ResourceVersion = table.Column<long>(type: "bigint", nullable: false),
                    QuoteId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PolicyVersion = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdentityAuthorityVersions", x => new { x.WorkspaceId, x.CaseId, x.ResourceVersion });
                    table.CheckConstraint("CK_IdentityAuthorityVersion_Json", "ISJSON([SnapshotJson])=1 AND DATALENGTH([SnapshotJson])<=131072");
                    table.CheckConstraint("CK_IdentityAuthorityVersion_Kind", "[Kind] COLLATE Latin1_General_100_BIN2 IN ('BINDING_RECORDED','SOURCE_CHECK_ATTEMPTED','ASSESSMENT_RECORDED')");
                    table.CheckConstraint("CK_IdentityAuthorityVersion_Number", "[ResourceVersion]>=1");
                    table.ForeignKey(
                        name: "FK_IdentityAuthorityVersions_AcceptanceCases_WorkspaceId_CaseId_QuoteId",
                        columns: x => new { x.WorkspaceId, x.CaseId, x.QuoteId },
                        principalSchema: "quotes",
                        principalTable: "AcceptanceCases",
                        principalColumns: new[] { "WorkspaceId", "CaseId", "QuoteId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AcceptanceEvidenceSourceVersions_WorkspaceId_BuyerType_BuyerId_SourceId_ResourceVersion",
                schema: "quotes",
                table: "AcceptanceEvidenceSourceVersions",
                columns: new[] { "WorkspaceId", "BuyerType", "BuyerId", "SourceId", "ResourceVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_IdentityAuthorityVersions_WorkspaceId_CaseId_QuoteId",
                schema: "quotes",
                table: "IdentityAuthorityVersions",
                columns: new[] { "WorkspaceId", "CaseId", "QuoteId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AcceptanceEvidenceSourceVersions",
                schema: "quotes");

            migrationBuilder.DropTable(
                name: "IdentityAuthorityVersions",
                schema: "quotes");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_AcceptanceCases_WorkspaceId_CaseId_QuoteId",
                schema: "quotes",
                table: "AcceptanceCases");
        }
    }
}
