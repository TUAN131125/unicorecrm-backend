using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Crm.Leads.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LeadRuntimeUsability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PhoneSearchText",
                schema: "leads",
                table: "Leads",
                type: "nvarchar(160)",
                maxLength: 160,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(
                """
                UPDATE [leads].[Leads]
                SET [SearchText] = UPPER(CONCAT([LeadId], CHAR(10), JSON_VALUE([Profile], '$.displayName'))),
                    [PhoneSearchText] = UPPER(CONCAT(
                        COALESCE(JSON_VALUE([Profile], '$.phone'), ''),
                        CHAR(10),
                        REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                            COALESCE(JSON_VALUE([Profile], '$.phone'), ''),
                            '+', ''), ' ', ''), '(', ''), ')', ''), '.', ''), '-', '')));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PhoneSearchText",
                schema: "leads",
                table: "Leads");
        }
    }
}
