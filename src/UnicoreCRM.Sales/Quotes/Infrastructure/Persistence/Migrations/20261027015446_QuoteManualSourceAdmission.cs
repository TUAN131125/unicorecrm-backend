using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Sales.Quotes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class QuoteManualSourceAdmission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AcceptanceSourceVersion_State",
                schema: "quotes",
                table: "AcceptanceEvidenceSourceVersions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AcceptanceSourceVersion_Approval",
                schema: "quotes",
                table: "AcceptanceEvidenceSourceVersions",
                sql: "[Status]<>'APPROVED' OR (COALESCE(JSON_VALUE([SnapshotJson],'$.assuranceLevel'),'')='DEVELOPMENT_SIMULATION' AND COALESCE(JSON_VALUE([SnapshotJson],'$.verificationMethod'),'')='ORGANIZATION_ISSUED_AUTHORITY_WITH_INDEPENDENT_SOURCE_CHECK' AND JSON_VALUE([SnapshotJson],'$.approvedBy') IS NOT NULL AND JSON_VALUE([SnapshotJson],'$.createdBy') IS NOT NULL AND JSON_VALUE([SnapshotJson],'$.approvedBy')<>JSON_VALUE([SnapshotJson],'$.createdBy') AND JSON_QUERY([SnapshotJson],'$.evidence') IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AcceptanceSourceVersion_State",
                schema: "quotes",
                table: "AcceptanceEvidenceSourceVersions",
                sql: "[Status] COLLATE Latin1_General_100_BIN2 IN ('DRAFT','APPROVED','REVOKED')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AcceptanceSourceVersion_Approval",
                schema: "quotes",
                table: "AcceptanceEvidenceSourceVersions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AcceptanceSourceVersion_State",
                schema: "quotes",
                table: "AcceptanceEvidenceSourceVersions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AcceptanceSourceVersion_State",
                schema: "quotes",
                table: "AcceptanceEvidenceSourceVersions",
                sql: "[Status] COLLATE Latin1_General_100_BIN2 IN ('DRAFT','REVOKED')");
        }
    }
}
