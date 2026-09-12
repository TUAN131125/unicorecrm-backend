using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Crm.Customers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LeadCustomerConversionProvenanceLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LeadConversionProvenance",
                schema: "customers",
                columns: table => new
                {
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    WorkflowId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CustomerId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SourceLeadId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PolicyVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    InitiatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Resolution = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeadConversionProvenance", x => new { x.WorkspaceId, x.WorkflowId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_LeadConversionProvenance_WorkspaceId_CustomerId_CompletedAt",
                schema: "customers",
                table: "LeadConversionProvenance",
                columns: new[] { "WorkspaceId", "CustomerId", "CompletedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LeadConversionProvenance_WorkspaceId_SourceLeadId",
                schema: "customers",
                table: "LeadConversionProvenance",
                columns: new[] { "WorkspaceId", "SourceLeadId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LeadConversionProvenance",
                schema: "customers");
        }
    }
}
