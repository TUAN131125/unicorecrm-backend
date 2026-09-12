using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Crm.Customers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CorrectLeadConversionProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "InitiatedBy",
                schema: "customers",
                table: "LeadConversionProvenance",
                newName: "OriginalPrincipalId");

            migrationBuilder.AddColumn<string>(
                name: "CompletionExecutorPrincipalId",
                schema: "customers",
                table: "LeadConversionProvenance",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompletionExecutorPrincipalId",
                schema: "customers",
                table: "LeadConversionProvenance");

            migrationBuilder.RenameColumn(
                name: "OriginalPrincipalId",
                schema: "customers",
                table: "LeadConversionProvenance",
                newName: "InitiatedBy");
        }
    }
}
