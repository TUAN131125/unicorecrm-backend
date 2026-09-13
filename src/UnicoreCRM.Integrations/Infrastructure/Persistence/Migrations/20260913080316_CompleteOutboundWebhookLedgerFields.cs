using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Integrations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompleteOutboundWebhookLedgerFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ArchivedAt",
                schema: "integration",
                table: "OutboundWebhookSubscriptions",
                type: "datetimeoffset(7)",
                precision: 7,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                schema: "integration",
                table: "OutboundWebhookSubscriptions",
                type: "datetimeoffset(7)",
                precision: 7,
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                schema: "integration",
                table: "OutboundWebhookSubscriptions",
                type: "datetimeoffset(7)",
                precision: 7,
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<long>(
                name: "Version",
                schema: "integration",
                table: "OutboundWebhookSubscriptions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                schema: "integration",
                table: "OutboundWebhookIdempotency",
                type: "datetimeoffset(7)",
                precision: 7,
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CompletedAt",
                schema: "integration",
                table: "OutboundWebhookDeliveryAttempts",
                type: "datetimeoffset(7)",
                precision: 7,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HttpStatus",
                schema: "integration",
                table: "OutboundWebhookDeliveryAttempts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StartedAt",
                schema: "integration",
                table: "OutboundWebhookDeliveryAttempts",
                type: "datetimeoffset(7)",
                precision: 7,
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "NextAttemptAt",
                schema: "integration",
                table: "OutboundWebhookDeliveries",
                type: "datetimeoffset(7)",
                precision: 7,
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "LeaseExpiresAt",
                schema: "integration",
                table: "OutboundWebhookDeliveries",
                type: "datetimeoffset(7)",
                precision: 7,
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                schema: "integration",
                table: "OutboundWebhookDeliveries",
                type: "datetimeoffset(7)",
                precision: 7,
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset");

            migrationBuilder.AddColumn<int>(
                name: "AttemptCount",
                schema: "integration",
                table: "OutboundWebhookDeliveries",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeadLetteredAt",
                schema: "integration",
                table: "OutboundWebhookDeliveries",
                type: "datetimeoffset(7)",
                precision: 7,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "EventSequence",
                schema: "integration",
                table: "OutboundWebhookDeliveries",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastAttemptAt",
                schema: "integration",
                table: "OutboundWebhookDeliveries",
                type: "datetimeoffset(7)",
                precision: 7,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LastHttpStatus",
                schema: "integration",
                table: "OutboundWebhookDeliveries",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SucceededAt",
                schema: "integration",
                table: "OutboundWebhookDeliveries",
                type: "datetimeoffset(7)",
                precision: 7,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                schema: "integration",
                table: "OutboundWebhookConsumerCursors",
                type: "datetimeoffset(7)",
                precision: 7,
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<long>(
                name: "LastProcessedSequence",
                schema: "integration",
                table: "OutboundWebhookConsumerCursors",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                schema: "integration",
                table: "OutboundWebhookConsumerCursors",
                type: "datetimeoffset(7)",
                precision: 7,
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                schema: "integration",
                table: "OutboundWebhookSubscriptions");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                schema: "integration",
                table: "OutboundWebhookSubscriptions");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                schema: "integration",
                table: "OutboundWebhookSubscriptions");

            migrationBuilder.DropColumn(
                name: "Version",
                schema: "integration",
                table: "OutboundWebhookSubscriptions");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                schema: "integration",
                table: "OutboundWebhookIdempotency");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                schema: "integration",
                table: "OutboundWebhookDeliveryAttempts");

            migrationBuilder.DropColumn(
                name: "HttpStatus",
                schema: "integration",
                table: "OutboundWebhookDeliveryAttempts");

            migrationBuilder.DropColumn(
                name: "StartedAt",
                schema: "integration",
                table: "OutboundWebhookDeliveryAttempts");

            migrationBuilder.DropColumn(
                name: "AttemptCount",
                schema: "integration",
                table: "OutboundWebhookDeliveries");

            migrationBuilder.DropColumn(
                name: "DeadLetteredAt",
                schema: "integration",
                table: "OutboundWebhookDeliveries");

            migrationBuilder.DropColumn(
                name: "EventSequence",
                schema: "integration",
                table: "OutboundWebhookDeliveries");

            migrationBuilder.DropColumn(
                name: "LastAttemptAt",
                schema: "integration",
                table: "OutboundWebhookDeliveries");

            migrationBuilder.DropColumn(
                name: "LastHttpStatus",
                schema: "integration",
                table: "OutboundWebhookDeliveries");

            migrationBuilder.DropColumn(
                name: "SucceededAt",
                schema: "integration",
                table: "OutboundWebhookDeliveries");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                schema: "integration",
                table: "OutboundWebhookConsumerCursors");

            migrationBuilder.DropColumn(
                name: "LastProcessedSequence",
                schema: "integration",
                table: "OutboundWebhookConsumerCursors");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                schema: "integration",
                table: "OutboundWebhookConsumerCursors");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "NextAttemptAt",
                schema: "integration",
                table: "OutboundWebhookDeliveries",
                type: "datetimeoffset",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset(7)",
                oldPrecision: 7);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "LeaseExpiresAt",
                schema: "integration",
                table: "OutboundWebhookDeliveries",
                type: "datetimeoffset",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset(7)",
                oldPrecision: 7,
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                schema: "integration",
                table: "OutboundWebhookDeliveries",
                type: "datetimeoffset",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset(7)",
                oldPrecision: 7);
        }
    }
}
