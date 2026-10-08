using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Crm.Contacts.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ContactListLifecycleIndexCoverage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Contacts_WorkspaceId_UpdatedAt_ContactId",
                schema: "contacts",
                table: "Contacts");

            migrationBuilder.CreateIndex(
                name: "IX_Contacts_WorkspaceId_UpdatedAt_ContactId",
                schema: "contacts",
                table: "Contacts",
                columns: new[] { "WorkspaceId", "UpdatedAt", "ContactId" })
                .Annotation("SqlServer:Include", new[] { "Status", "ArchivedAt", "OwnerId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Contacts_WorkspaceId_UpdatedAt_ContactId",
                schema: "contacts",
                table: "Contacts");

            migrationBuilder.CreateIndex(
                name: "IX_Contacts_WorkspaceId_UpdatedAt_ContactId",
                schema: "contacts",
                table: "Contacts",
                columns: new[] { "WorkspaceId", "UpdatedAt", "ContactId" });
        }
    }
}
