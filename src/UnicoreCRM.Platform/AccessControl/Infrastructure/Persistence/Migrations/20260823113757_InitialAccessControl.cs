using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Platform.AccessControl.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialAccessControl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "access");

            migrationBuilder.CreateTable(
                name: "AuthorizationDecisions",
                schema: "access",
                columns: table => new
                {
                    DecisionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    MembershipId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RequiredCapability = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Allowed = table.Column<bool>(type: "bit", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    EvaluatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthorizationDecisions", x => x.DecisionId);
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                schema: "access",
                columns: table => new
                {
                    RoleId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SourceTemplateId = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.RoleId);
                    table.UniqueConstraint("AK_Roles_RoleId_WorkspaceId", x => new { x.RoleId, x.WorkspaceId });
                });

            migrationBuilder.CreateTable(
                name: "MembershipRoleAssignments",
                schema: "access",
                columns: table => new
                {
                    AssignmentId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    MembershipId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RoleId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    AssignedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MembershipRoleAssignments", x => x.AssignmentId);
                    table.ForeignKey(
                        name: "FK_MembershipRoleAssignments_Roles_RoleId_WorkspaceId",
                        columns: x => new { x.RoleId, x.WorkspaceId },
                        principalSchema: "access",
                        principalTable: "Roles",
                        principalColumns: new[] { "RoleId", "WorkspaceId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RoleCapabilities",
                schema: "access",
                columns: table => new
                {
                    RoleId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Capability = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleCapabilities", x => new { x.RoleId, x.Capability });
                    table.ForeignKey(
                        name: "FK_RoleCapabilities_Roles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "access",
                        principalTable: "Roles",
                        principalColumn: "RoleId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RoleDataScopes",
                schema: "access",
                columns: table => new
                {
                    PolicyId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RoleId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ResourceKey = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Scope = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    AllowedOwnerIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleDataScopes", x => x.PolicyId);
                    table.ForeignKey(
                        name: "FK_RoleDataScopes_Roles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "access",
                        principalTable: "Roles",
                        principalColumn: "RoleId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RoleFieldSecurity",
                schema: "access",
                columns: table => new
                {
                    PolicyId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RoleId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ResourceKey = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    FieldKey = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Access = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleFieldSecurity", x => x.PolicyId);
                    table.ForeignKey(
                        name: "FK_RoleFieldSecurity_Roles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "access",
                        principalTable: "Roles",
                        principalColumn: "RoleId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuthorizationDecisions_WorkspaceId_MembershipId_EvaluatedAt",
                schema: "access",
                table: "AuthorizationDecisions",
                columns: new[] { "WorkspaceId", "MembershipId", "EvaluatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MembershipRoleAssignments_RoleId_WorkspaceId",
                schema: "access",
                table: "MembershipRoleAssignments",
                columns: new[] { "RoleId", "WorkspaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_MembershipRoleAssignments_WorkspaceId_MembershipId_RoleId",
                schema: "access",
                table: "MembershipRoleAssignments",
                columns: new[] { "WorkspaceId", "MembershipId", "RoleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoleDataScopes_RoleId_ResourceKey",
                schema: "access",
                table: "RoleDataScopes",
                columns: new[] { "RoleId", "ResourceKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoleFieldSecurity_RoleId_ResourceKey_FieldKey",
                schema: "access",
                table: "RoleFieldSecurity",
                columns: new[] { "RoleId", "ResourceKey", "FieldKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Roles_WorkspaceId_Name",
                schema: "access",
                table: "Roles",
                columns: new[] { "WorkspaceId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuthorizationDecisions",
                schema: "access");

            migrationBuilder.DropTable(
                name: "MembershipRoleAssignments",
                schema: "access");

            migrationBuilder.DropTable(
                name: "RoleCapabilities",
                schema: "access");

            migrationBuilder.DropTable(
                name: "RoleDataScopes",
                schema: "access");

            migrationBuilder.DropTable(
                name: "RoleFieldSecurity",
                schema: "access");

            migrationBuilder.DropTable(
                name: "Roles",
                schema: "access");
        }
    }
}
