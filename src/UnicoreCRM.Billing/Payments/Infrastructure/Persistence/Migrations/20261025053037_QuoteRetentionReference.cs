using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Billing.Payments.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class QuoteRetentionReference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "QuoteSourceId",
                schema: "payments",
                table: "PaymentPlans",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true,
                computedColumnSql: "CONVERT(nvarchar(128), JSON_VALUE([AgreementSnapshotJson], '$.sourceQuoteId'))",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentPlans_WorkspaceId_QuoteSourceId",
                schema: "payments",
                table: "PaymentPlans",
                columns: new[] { "WorkspaceId", "QuoteSourceId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PaymentPlans_WorkspaceId_QuoteSourceId",
                schema: "payments",
                table: "PaymentPlans");

            migrationBuilder.DropColumn(
                name: "QuoteSourceId",
                schema: "payments",
                table: "PaymentPlans");
        }
    }
}
