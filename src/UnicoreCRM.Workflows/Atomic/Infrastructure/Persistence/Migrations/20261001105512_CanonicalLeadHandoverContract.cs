using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Workflows.Atomic.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CanonicalLeadHandoverContract : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A live workflow must finish under the protocol that admitted it. Do not
            // silently reinterpret a pending historical policy during deployment.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [workflow].[LeadHandoverAnchors] WHERE [ActiveLeadKey] IS NOT NULL)
                    THROW 51000, 'Complete active Lead Handover workflows before changing the contract.', 1;
                """);
            migrationBuilder.DropColumn(
                name: "OpenTaskPolicy",
                schema: "workflow",
                table: "LeadHandoverAnchors");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [workflow].[LeadHandoverAnchors] WHERE [ActiveLeadKey] IS NOT NULL)
                    THROW 51000, 'Complete active Lead Handover workflows before rollback.', 1;
                """);
            migrationBuilder.AddColumn<string>(
                name: "OpenTaskPolicy",
                schema: "workflow",
                table: "LeadHandoverAnchors",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "MOVE_LEAD_OPEN_TASKS_TO_NEW_OWNER");
        }
    }
}
