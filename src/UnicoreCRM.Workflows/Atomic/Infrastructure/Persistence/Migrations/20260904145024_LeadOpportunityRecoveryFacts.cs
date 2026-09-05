using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Workflows.Atomic.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LeadOpportunityRecoveryFacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DealId",
                schema: "workflow",
                table: "LeadQualificationAnchors",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DealVersion",
                schema: "workflow",
                table: "LeadQualificationAnchors",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DealId",
                schema: "workflow",
                table: "LeadQualificationAnchors");

            migrationBuilder.DropColumn(
                name: "DealVersion",
                schema: "workflow",
                table: "LeadQualificationAnchors");
        }
    }
}
