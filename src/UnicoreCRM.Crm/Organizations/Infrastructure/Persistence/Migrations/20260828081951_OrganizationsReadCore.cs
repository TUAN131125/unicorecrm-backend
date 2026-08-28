using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Crm.Organizations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OrganizationsReadCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "organizations");

            migrationBuilder.CreateTable(
                name: "Organizations",
                schema: "organizations",
                columns: table => new
                {
                    OrganizationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Profile = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Organizations", x => new { x.WorkspaceId, x.OrganizationId });
                });

            migrationBuilder.CreateTable(
                name: "ReadAuditRecords",
                schema: "organizations",
                columns: table => new
                {
                    AuditId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    OrganizationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    RequestId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    OrganizationVersion = table.Column<long>(type: "bigint", nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadAuditRecords", x => x.AuditId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_WorkspaceId_CreatedAt_OrganizationId",
                schema: "organizations",
                table: "Organizations",
                columns: new[] { "WorkspaceId", "CreatedAt", "OrganizationId" },
                descending: new[] { false, true, false });

            migrationBuilder.CreateIndex(
                name: "IX_ReadAuditRecords_WorkspaceId_OccurredAt",
                schema: "organizations",
                table: "ReadAuditRecords",
                columns: new[] { "WorkspaceId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ReadAuditRecords_WorkspaceId_OrganizationId_OccurredAt",
                schema: "organizations",
                table: "ReadAuditRecords",
                columns: new[] { "WorkspaceId", "OrganizationId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Organizations",
                schema: "organizations");

            migrationBuilder.DropTable(
                name: "ReadAuditRecords",
                schema: "organizations");
        }
    }
}
