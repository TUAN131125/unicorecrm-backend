using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Platform.AccessControl.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LeadHandoverRecoveryGrant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO [access].[WorkspaceServiceCapabilityGrants] ([WorkspaceId], [ServicePrincipalId], [Capability], [GrantedAt])
                SELECT d.[WorkspaceId], N'svc_lead_handover_recovery', N'leads.handover.recover', SYSUTCDATETIME()
                FROM [access].[WorkspaceDirectoryRevisions] d
                WHERE NOT EXISTS (SELECT 1 FROM [access].[WorkspaceServiceCapabilityGrants] g
                    WHERE g.[WorkspaceId]=d.[WorkspaceId] AND g.[ServicePrincipalId]=N'svc_lead_handover_recovery'
                        AND g.[Capability]=N'leads.handover.recover');
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM [access].[WorkspaceServiceCapabilityGrants] WHERE [ServicePrincipalId]=N'svc_lead_handover_recovery' AND [Capability]=N'leads.handover.recover';");

        }
    }
}
