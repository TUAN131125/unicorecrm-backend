using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Integrations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialInboundIntegrationBindings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "integration");

            migrationBuilder.CreateTable(
                name: "InboundBindings",
                schema: "integration",
                columns: table => new
                {
                    IntegrationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ProviderCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    DelegatedMemberId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SecretReference = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboundBindings", x => x.IntegrationId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InboundBindings_WorkspaceId_IsEnabled",
                schema: "integration",
                table: "InboundBindings",
                columns: new[] { "WorkspaceId", "IsEnabled" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InboundBindings",
                schema: "integration");
        }
    }
}
