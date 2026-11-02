using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Sales.Orders.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OrderPolicyConfirmationOrigin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ConfirmationOrigin",
                schema: "orders",
                table: "Orders",
                type: "nvarchar(48)",
                maxLength: 48,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConfirmationPolicyVersion",
                schema: "orders",
                table: "Orders",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConfirmationOrigin",
                schema: "orders",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ConfirmationPolicyVersion",
                schema: "orders",
                table: "Orders");
        }
    }
}
