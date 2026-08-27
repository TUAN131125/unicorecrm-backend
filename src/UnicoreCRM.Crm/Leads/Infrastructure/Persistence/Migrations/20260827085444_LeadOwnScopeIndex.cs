using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Crm.Leads.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LeadOwnScopeIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Leads_WorkspaceId_ScopeOwnerId_UpdatedAt_LeadId",
                schema: "leads",
                table: "Leads",
                columns: new[] { "WorkspaceId", "ScopeOwnerId", "UpdatedAt", "LeadId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Leads_WorkspaceId_ScopeOwnerId_UpdatedAt_LeadId",
                schema: "leads",
                table: "Leads");
        }
    }
}
