using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Platform.IdentityAuth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IdentityEmailOutboxSupersession : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LeasedUntil",
                schema: "iam",
                table: "EmailOutboxMessages",
                type: "datetimeoffset",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LeasedUntil",
                schema: "iam",
                table: "EmailOutboxMessages");
        }
    }
}
