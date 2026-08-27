using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Crm.Deals.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DealOwnScopeIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Deals_WorkspaceId_ScopeOwnerId_UpdatedAt_DealId",
                schema: "deals",
                table: "Deals",
                columns: new[] { "WorkspaceId", "ScopeOwnerId", "UpdatedAt", "DealId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Deals_WorkspaceId_ScopeOwnerId_UpdatedAt_DealId",
                schema: "deals",
                table: "Deals");
        }
    }
}
