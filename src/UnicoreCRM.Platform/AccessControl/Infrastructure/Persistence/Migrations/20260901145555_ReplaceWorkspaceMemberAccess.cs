using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Platform.AccessControl.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceWorkspaceMemberAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "RoleId",
                schema: "access",
                table: "GovernanceCommandAudits",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128);

            migrationBuilder.AddColumn<string>(
                name: "TargetMembershipId",
                schema: "access",
                table: "GovernanceCommandAudits",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MemberAccessCommandIdempotencyRecords",
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
                    MembershipId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    MemberAccessVersion = table.Column<long>(type: "bigint", nullable: false),
                    AuditEvidenceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    EventId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    DirectoryRevisionAtCommit = table.Column<long>(type: "bigint", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemberAccessCommandIdempotencyRecords", x => x.ScopeKey);
                });

            migrationBuilder.CreateTable(
                name: "MemberAccessVersions",
                schema: "access",
                columns: table => new
                {
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    MembershipId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemberAccessVersions", x => new { x.WorkspaceId, x.MembershipId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_MemberAccessCommandIdempotencyRecords_WorkspaceId_OperationId_ActorMembershipId",
                schema: "access",
                table: "MemberAccessCommandIdempotencyRecords",
                columns: new[] { "WorkspaceId", "OperationId", "ActorMembershipId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MemberAccessCommandIdempotencyRecords",
                schema: "access");

            migrationBuilder.DropTable(
                name: "MemberAccessVersions",
                schema: "access");

            migrationBuilder.DropColumn(
                name: "TargetMembershipId",
                schema: "access",
                table: "GovernanceCommandAudits");

            migrationBuilder.AlterColumn<string>(
                name: "RoleId",
                schema: "access",
                table: "GovernanceCommandAudits",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128,
                oldNullable: true);
        }
    }
}
