using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Sales.Products.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProductConfigurationOverlay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductConfigurationDocuments",
                schema: "products",
                columns: table => new
                {
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductConfigurationDocuments", x => x.WorkspaceId);
                    table.CheckConstraint("CK_ProductConfigurationDocuments_Revision", "[Revision] >= 0");
                });

            migrationBuilder.CreateTable(
                name: "ProductConfigurationTypeOverrides",
                schema: "products",
                columns: table => new
                {
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ProductTypeCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false, collation: "Latin1_General_100_BIN2")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductConfigurationTypeOverrides", x => new { x.WorkspaceId, x.ProductTypeCode });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductConfigurationDocuments",
                schema: "products");

            migrationBuilder.DropTable(
                name: "ProductConfigurationTypeOverrides",
                schema: "products");
        }
    }
}
