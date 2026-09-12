using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Platform.AccessControl.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceRecoveryAuthorization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ServiceAuthorizationDecisions",
                schema: "access",
                columns: table => new
                {
                    DecisionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ServicePrincipalId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RequiredCapability = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    EvaluatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceAuthorizationDecisions", x => x.DecisionId);
                });

            migrationBuilder.CreateTable(
                name: "WorkspaceServiceCapabilityGrants",
                schema: "access",
                columns: table => new
                {
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ServicePrincipalId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Capability = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    GrantedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkspaceServiceCapabilityGrants", x => new { x.WorkspaceId, x.ServicePrincipalId, x.Capability });
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceAuthorizationDecisions_WorkspaceId_ServicePrincipalId_EvaluatedAt",
                schema: "access",
                table: "ServiceAuthorizationDecisions",
                columns: new[] { "WorkspaceId", "ServicePrincipalId", "EvaluatedAt" });

            migrationBuilder.Sql("""
                INSERT INTO [access].[WorkspaceServiceCapabilityGrants] ([WorkspaceId], [ServicePrincipalId], [Capability], [GrantedAt])
                SELECT d.[WorkspaceId], N'svc_lead_customer_conversion_recovery', N'leads.convert_to_customer.recover', SYSUTCDATETIME()
                FROM [access].[WorkspaceDirectoryRevisions] d
                WHERE NOT EXISTS (
                    SELECT 1 FROM [access].[WorkspaceServiceCapabilityGrants] g
                    WHERE g.[WorkspaceId] = d.[WorkspaceId]
                      AND g.[ServicePrincipalId] = N'svc_lead_customer_conversion_recovery'
                      AND g.[Capability] = N'leads.convert_to_customer.recover')
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ServiceAuthorizationDecisions",
                schema: "access");

            migrationBuilder.DropTable(
                name: "WorkspaceServiceCapabilityGrants",
                schema: "access");
        }
    }
}
