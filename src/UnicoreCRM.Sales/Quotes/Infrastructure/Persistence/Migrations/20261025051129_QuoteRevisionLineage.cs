using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Sales.Quotes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class QuoteRevisionLineage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Quotes_WorkspaceId_RootQuoteId_QuoteRevision",
                schema: "quotes",
                table: "Quotes",
                columns: new[] { "WorkspaceId", "RootQuoteId", "QuoteRevision" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Quotes_WorkspaceId_RootQuoteId_QuoteRevision",
                schema: "quotes",
                table: "Quotes");
        }
    }
}
