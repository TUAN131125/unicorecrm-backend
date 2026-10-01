using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Crm.Leads.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LeadHandoverReservation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PendingHandoverId",
                schema: "leads",
                table: "Leads",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM [leads].[Leads] WHERE [PendingHandoverId] IS NOT NULL) THROW 51000, 'Cannot remove an active Lead handover reservation.', 1;");
            migrationBuilder.DropColumn(
                name: "PendingHandoverId",
                schema: "leads",
                table: "Leads");
        }
    }
}
