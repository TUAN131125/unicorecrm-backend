using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Crm.Deals.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LeadOpportunityUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "QualificationSourceLeadId",
                schema: "deals",
                table: "Deals",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Deals_WorkspaceId_QualificationSourceLeadId",
                schema: "deals",
                table: "Deals",
                columns: new[] { "WorkspaceId", "QualificationSourceLeadId" },
                unique: true,
                filter: "[QualificationSourceLeadId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Deals_WorkspaceId_QualificationSourceLeadId",
                schema: "deals",
                table: "Deals");

            migrationBuilder.DropColumn(
                name: "QualificationSourceLeadId",
                schema: "deals",
                table: "Deals");
        }
    }
}
