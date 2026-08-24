using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Crm.Leads.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExecutionProvenanceToLeadAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ActorType",
                schema: "leads",
                table: "AuditRecords",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Member");

            migrationBuilder.AddColumn<string>(
                name: "DelegatedSubjectId",
                schema: "leads",
                table: "AuditRecords",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceReference",
                schema: "leads",
                table: "AuditRecords",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditRecords_ActorType_ActorId_OccurredAt",
                schema: "leads",
                table: "AuditRecords",
                columns: new[] { "ActorType", "ActorId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AuditRecords_ActorType_ActorId_OccurredAt",
                schema: "leads",
                table: "AuditRecords");

            migrationBuilder.DropColumn(
                name: "ActorType",
                schema: "leads",
                table: "AuditRecords");

            migrationBuilder.DropColumn(
                name: "DelegatedSubjectId",
                schema: "leads",
                table: "AuditRecords");

            migrationBuilder.DropColumn(
                name: "SourceReference",
                schema: "leads",
                table: "AuditRecords");
        }
    }
}
