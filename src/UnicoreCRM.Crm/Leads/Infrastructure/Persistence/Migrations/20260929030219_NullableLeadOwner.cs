using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Crm.Leads.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NullableLeadOwner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ScopeOwnerId",
                schema: "leads",
                table: "Leads",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Downgrade must never manufacture owners or erase unassigned business records.
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM [leads].[Leads] WHERE [ScopeOwnerId] IS NULL) THROW 51000, 'Cannot downgrade while unassigned Leads exist.', 1;");
            migrationBuilder.AlterColumn<string>(
                name: "ScopeOwnerId",
                schema: "leads",
                table: "Leads",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128,
                oldNullable: true);
        }
    }
}
