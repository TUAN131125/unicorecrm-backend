using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Sales.Orders.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OrderOperationalConfirmation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ConfirmedBy",
                schema: "orders",
                table: "Orders",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OperationalReviewNotes",
                schema: "orders",
                table: "Orders",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConfirmedBy",
                schema: "orders",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "OperationalReviewNotes",
                schema: "orders",
                table: "Orders");
        }
    }
}
