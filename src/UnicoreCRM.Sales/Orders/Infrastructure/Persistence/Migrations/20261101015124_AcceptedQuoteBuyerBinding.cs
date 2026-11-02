using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Sales.Orders.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AcceptedQuoteBuyerBinding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BuyerNameSnapshot",
                schema: "orders",
                table: "Orders",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "OperationalApprovalRequired",
                schema: "orders",
                table: "Orders",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "SourceQuoteResourceVersion",
                schema: "orders",
                table: "Orders",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BuyerNameSnapshot",
                schema: "orders",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "OperationalApprovalRequired",
                schema: "orders",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "SourceQuoteResourceVersion",
                schema: "orders",
                table: "Orders");
        }
    }
}
