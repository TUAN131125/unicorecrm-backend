using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Crm.Contacts.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ContactOwnerLifecycleIndexCoverage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Contacts_WorkspaceId_OwnerId_CreatedAt_ContactId",
                schema: "contacts",
                table: "Contacts");

            migrationBuilder.CreateIndex(
                name: "IX_Contacts_WorkspaceId_OwnerId_CreatedAt_ContactId",
                schema: "contacts",
                table: "Contacts",
                columns: new[] { "WorkspaceId", "OwnerId", "CreatedAt", "ContactId" })
                .Annotation("SqlServer:Include", new[] { "Status", "ArchivedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Contacts_WorkspaceId_OwnerId_CreatedAt_ContactId",
                schema: "contacts",
                table: "Contacts");

            migrationBuilder.CreateIndex(
                name: "IX_Contacts_WorkspaceId_OwnerId_CreatedAt_ContactId",
                schema: "contacts",
                table: "Contacts",
                columns: new[] { "WorkspaceId", "OwnerId", "CreatedAt", "ContactId" });
        }
    }
}
