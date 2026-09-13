using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Integrations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HardenOutboundWebhookDeliveryLease : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RetryCycleAttemptCount",
                schema: "integration",
                table: "OutboundWebhookDeliveries",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                schema: "integration",
                table: "OutboundWebhookDeliveries",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RetryCycleAttemptCount",
                schema: "integration",
                table: "OutboundWebhookDeliveries");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                schema: "integration",
                table: "OutboundWebhookDeliveries");
        }
    }
}
