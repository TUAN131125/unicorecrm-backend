using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Crm.Customers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIntegrationEventExport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExportState",
                schema: "customers",
                table: "OutboxMessages",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IntegrationEnvelopeJson",
                schema: "customers",
                table: "OutboxMessages",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastRelayError",
                schema: "customers",
                table: "OutboxMessages",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LeaseExpiresAt",
                schema: "customers",
                table: "OutboxMessages",
                type: "datetimeoffset(7)",
                precision: 7,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextEligibleAt",
                schema: "customers",
                table: "OutboxMessages",
                type: "datetimeoffset(7)",
                precision: 7,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PublishedAt",
                schema: "customers",
                table: "OutboxMessages",
                type: "datetimeoffset(7)",
                precision: 7,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RelayAttemptId",
                schema: "customers",
                table: "OutboxMessages",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_ExportState_NextEligibleAt_LeaseExpiresAt_OccurredAt",
                schema: "customers",
                table: "OutboxMessages",
                columns: new[] { "ExportState", "NextEligibleAt", "LeaseExpiresAt", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_ExportState_NextEligibleAt_LeaseExpiresAt_OccurredAt",
                schema: "customers",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "ExportState",
                schema: "customers",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "IntegrationEnvelopeJson",
                schema: "customers",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "LastRelayError",
                schema: "customers",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "LeaseExpiresAt",
                schema: "customers",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "NextEligibleAt",
                schema: "customers",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "PublishedAt",
                schema: "customers",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "RelayAttemptId",
                schema: "customers",
                table: "OutboxMessages");
        }
    }
}
