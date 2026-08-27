using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Crm.Leads.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LeadScopeOwnerProjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ScopeOwnerId",
                schema: "leads",
                table: "Leads",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            // The column is a queryable projection of the owner already inside the JSON profile, so
            // existing rows are backfilled from that same source rather than left empty. An empty
            // value would make OWN scope deny every existing record - fail-closed, but wrong.
            migrationBuilder.Sql(
                "UPDATE [leads].[Leads] SET [ScopeOwnerId] = ISNULL(JSON_VALUE([Profile], '$.ownerId'), '');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ScopeOwnerId",
                schema: "leads",
                table: "Leads");
        }
    }
}
