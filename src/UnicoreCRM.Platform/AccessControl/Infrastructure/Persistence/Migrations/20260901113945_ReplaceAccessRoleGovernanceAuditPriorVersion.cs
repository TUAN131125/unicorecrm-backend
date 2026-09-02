using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Platform.AccessControl.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceAccessRoleGovernanceAuditPriorVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "PriorVersion",
                schema: "access",
                table: "GovernanceCommandAudits",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PriorVersion",
                schema: "access",
                table: "GovernanceCommandAudits");
        }
    }
}
