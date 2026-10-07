using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Crm.Contacts.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ContactListReadProjectionIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Contacts_WorkspaceId_FullName_ContactId",
                schema: "contacts",
                table: "Contacts",
                columns: new[] { "WorkspaceId", "FullName", "ContactId" });

            migrationBuilder.CreateIndex(
                name: "IX_Contacts_WorkspaceId_UpdatedAt_ContactId",
                schema: "contacts",
                table: "Contacts",
                columns: new[] { "WorkspaceId", "UpdatedAt", "ContactId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Contacts_WorkspaceId_FullName_ContactId",
                schema: "contacts",
                table: "Contacts");

            migrationBuilder.DropIndex(
                name: "IX_Contacts_WorkspaceId_UpdatedAt_ContactId",
                schema: "contacts",
                table: "Contacts");
        }
    }
}
