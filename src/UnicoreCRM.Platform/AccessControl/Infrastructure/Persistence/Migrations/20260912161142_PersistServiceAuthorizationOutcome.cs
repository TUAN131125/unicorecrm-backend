using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Platform.AccessControl.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PersistServiceAuthorizationOutcome : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Allowed",
                schema: "access",
                table: "ServiceAuthorizationDecisions",
                type: "bit",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Allowed",
                schema: "access",
                table: "ServiceAuthorizationDecisions");
        }
    }
}
