using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Billing.Payments.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CurrentOrderPaymentPlan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PaymentPlans_WorkspaceId_OrderId",
                schema: "payments",
                table: "PaymentPlans");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentPlans_WorkspaceId_OrderId",
                schema: "payments",
                table: "PaymentPlans",
                columns: new[] { "WorkspaceId", "OrderId" },
                unique: true,
                filter: "[State] IN ('DRAFT','ACTIVE')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PaymentPlans_WorkspaceId_OrderId",
                schema: "payments",
                table: "PaymentPlans");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentPlans_WorkspaceId_OrderId",
                schema: "payments",
                table: "PaymentPlans",
                columns: new[] { "WorkspaceId", "OrderId" });
        }
    }
}
