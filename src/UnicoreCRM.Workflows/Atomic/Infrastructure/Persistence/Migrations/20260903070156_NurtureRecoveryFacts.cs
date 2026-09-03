using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Workflows.Atomic.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NurtureRecoveryFacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContactDisplayName",
                schema: "workflow",
                table: "LeadQualificationAnchors",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ContactVersion",
                schema: "workflow",
                table: "LeadQualificationAnchors",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ContactWasCreated",
                schema: "workflow",
                table: "LeadQualificationAnchors",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CorrelationId",
                schema: "workflow",
                table: "LeadQualificationAnchors",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IntentVersion",
                schema: "workflow",
                table: "LeadQualificationAnchors",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ParticipantMemberId",
                schema: "workflow",
                table: "LeadQualificationAnchors",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                schema: "workflow",
                table: "LeadQualificationAnchors",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<string>(
                name: "TaskAssigneeId",
                schema: "workflow",
                table: "LeadQualificationAnchors",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "TaskVersion",
                schema: "workflow",
                table: "LeadQualificationAnchors",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContactDisplayName",
                schema: "workflow",
                table: "LeadQualificationAnchors");

            migrationBuilder.DropColumn(
                name: "ContactVersion",
                schema: "workflow",
                table: "LeadQualificationAnchors");

            migrationBuilder.DropColumn(
                name: "ContactWasCreated",
                schema: "workflow",
                table: "LeadQualificationAnchors");

            migrationBuilder.DropColumn(
                name: "CorrelationId",
                schema: "workflow",
                table: "LeadQualificationAnchors");

            migrationBuilder.DropColumn(
                name: "IntentVersion",
                schema: "workflow",
                table: "LeadQualificationAnchors");

            migrationBuilder.DropColumn(
                name: "ParticipantMemberId",
                schema: "workflow",
                table: "LeadQualificationAnchors");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                schema: "workflow",
                table: "LeadQualificationAnchors");

            migrationBuilder.DropColumn(
                name: "TaskAssigneeId",
                schema: "workflow",
                table: "LeadQualificationAnchors");

            migrationBuilder.DropColumn(
                name: "TaskVersion",
                schema: "workflow",
                table: "LeadQualificationAnchors");
        }
    }
}
