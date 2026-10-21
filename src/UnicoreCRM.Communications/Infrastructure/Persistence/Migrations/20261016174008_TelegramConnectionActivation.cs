using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Communications.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TelegramConnectionActivation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ActivatedAt",
                schema: "communications",
                table: "TelegramPolling",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Active",
                schema: "communications",
                table: "TelegramPolling",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActivatedAt",
                schema: "communications",
                table: "TelegramPolling");

            migrationBuilder.DropColumn(
                name: "Active",
                schema: "communications",
                table: "TelegramPolling");
        }
    }
}
