using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Sales.Products.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProductConfigurationTrustedRevision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductConfigurationTrustedRevisions",
                schema: "products",
                columns: table => new
                {
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    GreatestTrustedRevision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductConfigurationTrustedRevisions", x => x.WorkspaceId);
                    table.CheckConstraint("CK_ProductConfigurationTrustedRevisions_Revision", "[GreatestTrustedRevision] >= 0");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductConfigurationTrustedRevisions",
                schema: "products");
        }
    }
}
