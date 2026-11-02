using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Crm.Deals.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class QuoteRetentionReference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Deals_WorkspaceId_WinEvidenceType_WinEvidenceSourceId",
                schema: "deals",
                table: "Deals",
                columns: new[] { "WorkspaceId", "WinEvidenceType", "WinEvidenceSourceId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Deals_WorkspaceId_WinEvidenceType_WinEvidenceSourceId",
                schema: "deals",
                table: "Deals");
        }
    }
}
