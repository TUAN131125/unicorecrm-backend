using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Crm.Leads.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LeadArchiveState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Leads_WorkspaceId_ScopeOwnerId_UpdatedAt_LeadId",
                schema: "leads",
                table: "Leads");

            migrationBuilder.DropIndex(
                name: "IX_Leads_WorkspaceId_UpdatedAt_LeadId",
                schema: "leads",
                table: "Leads");

            migrationBuilder.AddColumn<string>(
                name: "ArchiveReason",
                schema: "leads",
                table: "Leads",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ArchivedAt",
                schema: "leads",
                table: "Leads",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Leads_WorkspaceId_ArchivedAt_UpdatedAt_LeadId",
                schema: "leads",
                table: "Leads",
                columns: new[] { "WorkspaceId", "ArchivedAt", "UpdatedAt", "LeadId" });

            migrationBuilder.CreateIndex(
                name: "IX_Leads_WorkspaceId_ScopeOwnerId_ArchivedAt_UpdatedAt_LeadId",
                schema: "leads",
                table: "Leads",
                columns: new[] { "WorkspaceId", "ScopeOwnerId", "ArchivedAt", "UpdatedAt", "LeadId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Leads_WorkspaceId_ArchivedAt_UpdatedAt_LeadId",
                schema: "leads",
                table: "Leads");

            migrationBuilder.DropIndex(
                name: "IX_Leads_WorkspaceId_ScopeOwnerId_ArchivedAt_UpdatedAt_LeadId",
                schema: "leads",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "ArchiveReason",
                schema: "leads",
                table: "Leads");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                schema: "leads",
                table: "Leads");

            migrationBuilder.CreateIndex(
                name: "IX_Leads_WorkspaceId_ScopeOwnerId_UpdatedAt_LeadId",
                schema: "leads",
                table: "Leads",
                columns: new[] { "WorkspaceId", "ScopeOwnerId", "UpdatedAt", "LeadId" });

            migrationBuilder.CreateIndex(
                name: "IX_Leads_WorkspaceId_UpdatedAt_LeadId",
                schema: "leads",
                table: "Leads",
                columns: new[] { "WorkspaceId", "UpdatedAt", "LeadId" });
        }
    }
}
