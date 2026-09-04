using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Workflows.Atomic.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LeadQualificationAnchor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "workflow");

            migrationBuilder.CreateTable(
                name: "LeadQualificationAnchors",
                schema: "workflow",
                columns: table => new
                {
                    ScopeKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Workflow = table.Column<string>(type: "nvarchar(96)", maxLength: 96, nullable: false),
                    LeadId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Fingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ExpectedLeadVersion = table.Column<long>(type: "bigint", nullable: false),
                    Stage = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ContactId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    TaskId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    LeadVersion = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeadQualificationAnchors", x => x.ScopeKey);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LeadQualificationAnchors_Stage_UpdatedAt",
                schema: "workflow",
                table: "LeadQualificationAnchors",
                columns: new[] { "Stage", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LeadQualificationAnchors_WorkspaceId_LeadId",
                schema: "workflow",
                table: "LeadQualificationAnchors",
                columns: new[] { "WorkspaceId", "LeadId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LeadQualificationAnchors",
                schema: "workflow");
        }
    }
}
