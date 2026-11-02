using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Sales.Quotes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class QuoteAutomaticConfirmationIntent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoConfirmRequested",
                schema: "quotes",
                table: "SalesAcceptances",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoConfirmRequested",
                schema: "quotes",
                table: "SalesAcceptances");
        }
    }
}
