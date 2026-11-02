using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Sales.Quotes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class QuoteIndependentReviewLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_IdentityAuthorityVersion_Kind",
                schema: "quotes",
                table: "IdentityAuthorityVersions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_IdentityAuthorityVersion_Kind",
                schema: "quotes",
                table: "IdentityAuthorityVersions",
                sql: "[Kind] COLLATE Latin1_General_100_BIN2 IN ('BINDING_RECORDED','SOURCE_CHECK_ATTEMPTED','ASSESSMENT_RECORDED','REVIEW_RECORDED')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_IdentityAuthorityVersion_Review",
                schema: "quotes",
                table: "IdentityAuthorityVersions",
                sql: "[Kind]<>'REVIEW_RECORDED' OR (COALESCE(JSON_VALUE([SnapshotJson],'$.independentReview.assuranceLevel'),'')='DEVELOPMENT_SIMULATION' AND COALESCE(JSON_VALUE([SnapshotJson],'$.independentReview.actualSourceVerification'),'')='false' AND COALESCE(JSON_VALUE([SnapshotJson],'$.independentReview.environment'),'')='Development' AND COALESCE(JSON_VALUE([SnapshotJson],'$.independentReview.decision'),'') IN ('APPROVE_SIMULATION','REQUEST_MORE_EVIDENCE','REJECT_REVIEW') AND COALESCE(JSON_VALUE([SnapshotJson],'$.independentReview.reviewerId'),'')=[ActorId] AND COALESCE(JSON_VALUE([SnapshotJson],'$.independentReview.caseId'),'')=[CaseId] AND COALESCE(JSON_VALUE([SnapshotJson],'$.independentReview.quoteId'),'')=[QuoteId] AND JSON_VALUE([SnapshotJson],'$.independentReview.reviewDecisionId') IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_IdentityAuthorityVersion_Kind",
                schema: "quotes",
                table: "IdentityAuthorityVersions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_IdentityAuthorityVersion_Review",
                schema: "quotes",
                table: "IdentityAuthorityVersions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_IdentityAuthorityVersion_Kind",
                schema: "quotes",
                table: "IdentityAuthorityVersions",
                sql: "[Kind] COLLATE Latin1_General_100_BIN2 IN ('BINDING_RECORDED','SOURCE_CHECK_ATTEMPTED','ASSESSMENT_RECORDED')");
        }
    }
}
