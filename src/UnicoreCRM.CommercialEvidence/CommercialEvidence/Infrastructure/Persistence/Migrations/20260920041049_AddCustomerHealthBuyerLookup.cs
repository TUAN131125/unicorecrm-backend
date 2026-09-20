using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.CommercialEvidence.CommercialEvidence.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerHealthBuyerLookup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_PurchaseEvidence_Workspace_Buyer_OccurredAt",
                schema: "commercial_evidence",
                table: "PurchaseEvidence",
                columns: new[] { "WorkspaceId", "BuyerRefType", "BuyerRefId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PurchaseEvidence_Workspace_Buyer_OccurredAt",
                schema: "commercial_evidence",
                table: "PurchaseEvidence");
        }
    }
}
