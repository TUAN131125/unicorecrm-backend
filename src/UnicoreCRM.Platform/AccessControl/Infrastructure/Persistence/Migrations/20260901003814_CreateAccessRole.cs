using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Platform.AccessControl.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreateAccessRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RoleDataScopes_Roles_RoleId",
                schema: "access",
                table: "RoleDataScopes");

            migrationBuilder.DropForeignKey(
                name: "FK_RoleFieldSecurity_Roles_RoleId",
                schema: "access",
                table: "RoleFieldSecurity");

            migrationBuilder.DropIndex(
                name: "IX_Roles_WorkspaceId_Name",
                schema: "access",
                table: "Roles");

            migrationBuilder.AddColumn<string>(
                name: "NormalizedName",
                schema: "access",
                table: "Roles",
                type: "nvarchar(160)",
                maxLength: 160,
                nullable: false,
                defaultValue: "",
                collation: "Latin1_General_100_BIN2");

            migrationBuilder.AddColumn<string>(
                name: "WorkspaceId",
                schema: "access",
                table: "RoleFieldSecurity",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "WorkspaceId",
                schema: "access",
                table: "RoleDataScopes",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "AccessRoleCommandIdempotencyRecords",
                schema: "access",
                columns: table => new
                {
                    ScopeKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    OperationId = table.Column<string>(type: "nvarchar(96)", maxLength: 96, nullable: false),
                    ActorMembershipId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Fingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CommandId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RoleId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RoleVersion = table.Column<long>(type: "bigint", nullable: false),
                    AuditEvidenceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    EventId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    DirectoryRevisionAtCommit = table.Column<long>(type: "bigint", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessRoleCommandIdempotencyRecords", x => x.ScopeKey);
                });

            migrationBuilder.CreateTable(
                name: "GovernanceCommandAudits",
                schema: "access",
                columns: table => new
                {
                    EvidenceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    EvidenceType = table.Column<string>(type: "nvarchar(96)", maxLength: 96, nullable: false),
                    OperationId = table.Column<string>(type: "nvarchar(96)", maxLength: 96, nullable: false),
                    CommandId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ActorAccountId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ActorMembershipId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ActorMemberId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RequestId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RoleId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ResultingVersion = table.Column<long>(type: "bigint", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GovernanceCommandAudits", x => x.EvidenceId);
                });

            migrationBuilder.CreateTable(
                name: "OutboxEvents",
                schema: "access",
                columns: table => new
                {
                    EventId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(96)", maxLength: 96, nullable: false),
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    AggregateId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    AggregateType = table.Column<string>(type: "nvarchar(96)", maxLength: 96, nullable: false),
                    AggregateVersion = table.Column<long>(type: "bigint", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CausationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxEvents", x => x.EventId);
                });

            migrationBuilder.CreateTable(
                name: "WorkspaceDirectoryRevisions",
                schema: "access",
                columns: table => new
                {
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkspaceDirectoryRevisions", x => x.WorkspaceId);
                });

            migrationBuilder.Sql(
                """
                UPDATE [access].[Roles]
                SET [NormalizedName] = UPPER([Name]);

                UPDATE scopePolicy
                SET [WorkspaceId] = role.[WorkspaceId]
                FROM [access].[RoleDataScopes] AS scopePolicy
                INNER JOIN [access].[Roles] AS role ON role.[RoleId] = scopePolicy.[RoleId];

                UPDATE fieldPolicy
                SET [WorkspaceId] = role.[WorkspaceId]
                FROM [access].[RoleFieldSecurity] AS fieldPolicy
                INNER JOIN [access].[Roles] AS role ON role.[RoleId] = fieldPolicy.[RoleId];

                INSERT INTO [access].[WorkspaceDirectoryRevisions] ([WorkspaceId], [Revision])
                SELECT existing.[WorkspaceId], CAST(1 AS bigint)
                FROM
                (
                    SELECT [WorkspaceId] FROM [access].[Roles]
                    UNION
                    SELECT [WorkspaceId] FROM [access].[MembershipRoleAssignments]
                ) AS existing;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Roles_WorkspaceId_NormalizedName",
                schema: "access",
                table: "Roles",
                columns: new[] { "WorkspaceId", "NormalizedName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoleFieldSecurity_RoleId_WorkspaceId",
                schema: "access",
                table: "RoleFieldSecurity",
                columns: new[] { "RoleId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_RoleDataScopes_RoleId_WorkspaceId",
                schema: "access",
                table: "RoleDataScopes",
                columns: new[] { "RoleId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_AccessRoleCommandIdempotencyRecords_WorkspaceId_OperationId_ActorMembershipId",
                schema: "access",
                table: "AccessRoleCommandIdempotencyRecords",
                columns: new[] { "WorkspaceId", "OperationId", "ActorMembershipId" });

            migrationBuilder.CreateIndex(
                name: "IX_GovernanceCommandAudits_CommandId",
                schema: "access",
                table: "GovernanceCommandAudits",
                column: "CommandId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GovernanceCommandAudits_WorkspaceId_OperationId_OccurredAt",
                schema: "access",
                table: "GovernanceCommandAudits",
                columns: new[] { "WorkspaceId", "OperationId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxEvents_CausationId",
                schema: "access",
                table: "OutboxEvents",
                column: "CausationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxEvents_WorkspaceId_EventType_OccurredAt",
                schema: "access",
                table: "OutboxEvents",
                columns: new[] { "WorkspaceId", "EventType", "OccurredAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_RoleDataScopes_Roles_RoleId_WorkspaceId",
                schema: "access",
                table: "RoleDataScopes",
                columns: new[] { "RoleId", "WorkspaceId" },
                principalSchema: "access",
                principalTable: "Roles",
                principalColumns: new[] { "RoleId", "WorkspaceId" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_RoleFieldSecurity_Roles_RoleId_WorkspaceId",
                schema: "access",
                table: "RoleFieldSecurity",
                columns: new[] { "RoleId", "WorkspaceId" },
                principalSchema: "access",
                principalTable: "Roles",
                principalColumns: new[] { "RoleId", "WorkspaceId" },
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RoleDataScopes_Roles_RoleId_WorkspaceId",
                schema: "access",
                table: "RoleDataScopes");

            migrationBuilder.DropForeignKey(
                name: "FK_RoleFieldSecurity_Roles_RoleId_WorkspaceId",
                schema: "access",
                table: "RoleFieldSecurity");

            migrationBuilder.DropTable(
                name: "AccessRoleCommandIdempotencyRecords",
                schema: "access");

            migrationBuilder.DropTable(
                name: "GovernanceCommandAudits",
                schema: "access");

            migrationBuilder.DropTable(
                name: "OutboxEvents",
                schema: "access");

            migrationBuilder.DropTable(
                name: "WorkspaceDirectoryRevisions",
                schema: "access");

            migrationBuilder.DropIndex(
                name: "IX_Roles_WorkspaceId_NormalizedName",
                schema: "access",
                table: "Roles");

            migrationBuilder.DropIndex(
                name: "IX_RoleFieldSecurity_RoleId_WorkspaceId",
                schema: "access",
                table: "RoleFieldSecurity");

            migrationBuilder.DropIndex(
                name: "IX_RoleDataScopes_RoleId_WorkspaceId",
                schema: "access",
                table: "RoleDataScopes");

            migrationBuilder.DropColumn(
                name: "NormalizedName",
                schema: "access",
                table: "Roles");

            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                schema: "access",
                table: "RoleFieldSecurity");

            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                schema: "access",
                table: "RoleDataScopes");

            migrationBuilder.CreateIndex(
                name: "IX_Roles_WorkspaceId_Name",
                schema: "access",
                table: "Roles",
                columns: new[] { "WorkspaceId", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_RoleDataScopes_Roles_RoleId",
                schema: "access",
                table: "RoleDataScopes",
                column: "RoleId",
                principalSchema: "access",
                principalTable: "Roles",
                principalColumn: "RoleId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_RoleFieldSecurity_Roles_RoleId",
                schema: "access",
                table: "RoleFieldSecurity",
                column: "RoleId",
                principalSchema: "access",
                principalTable: "Roles",
                principalColumn: "RoleId",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
