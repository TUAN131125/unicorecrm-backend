using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Platform.AccessControl.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecordAccessDecisionEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EnforcementPoint",
                schema: "access",
                table: "RecordAccessDecisions",
                type: "nvarchar(160)",
                maxLength: 160,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PolicyFingerprint",
                schema: "access",
                table: "RecordAccessDecisions",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RestrictedFields",
                schema: "access",
                table: "RecordAccessDecisions",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EnforcementPoint",
                schema: "access",
                table: "RecordAccessDecisions");

            migrationBuilder.DropColumn(
                name: "PolicyFingerprint",
                schema: "access",
                table: "RecordAccessDecisions");

            migrationBuilder.DropColumn(
                name: "RestrictedFields",
                schema: "access",
                table: "RecordAccessDecisions");
        }
    }
}
