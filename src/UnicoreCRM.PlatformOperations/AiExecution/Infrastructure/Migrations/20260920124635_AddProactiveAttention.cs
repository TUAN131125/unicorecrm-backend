using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.PlatformOperations.AiExecution.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProactiveAttention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProactiveAudits",
                schema: "platform_ai",
                columns: table => new
                {
                    AuditId = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    MemberId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Action = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    ItemId = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    SubjectId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    SafeSummaryJson = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProactiveAudits", x => x.AuditId);
                });

            migrationBuilder.CreateTable(
                name: "ProactiveCommands",
                schema: "platform_ai",
                columns: table => new
                {
                    ScopeKey = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    MemberId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Fingerprint = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ResultJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProactiveCommands", x => x.ScopeKey);
                });

            migrationBuilder.CreateTable(
                name: "ProactiveItems",
                schema: "platform_ai",
                columns: table => new
                {
                    ItemId = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    OwnerMemberId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    TriggerType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SubjectType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SubjectId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Severity = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ReasonCode = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    TriggerFingerprint = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RiskCycleKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    FirstDetectedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastDetectedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SeenAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SnoozedUntil = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DismissedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SourceVersion = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProactiveItems", x => x.ItemId);
                });

            migrationBuilder.CreateTable(
                name: "WorkspaceProactivePolicies",
                schema: "platform_ai",
                columns: table => new
                {
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    LastEvaluationAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    NextEvaluationAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LeaseId = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkspaceProactivePolicies", x => x.WorkspaceId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProactiveAudits_WorkspaceId_OccurredAt",
                schema: "platform_ai",
                table: "ProactiveAudits",
                columns: new[] { "WorkspaceId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ProactiveCommands_WorkspaceId_MemberId_Operation_IdempotencyKey",
                schema: "platform_ai",
                table: "ProactiveCommands",
                columns: new[] { "WorkspaceId", "MemberId", "Operation", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProactiveItems_WorkspaceId_OwnerMemberId_Status_UpdatedAt",
                schema: "platform_ai",
                table: "ProactiveItems",
                columns: new[] { "WorkspaceId", "OwnerMemberId", "Status", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ProactiveItems_WorkspaceId_SubjectType_SubjectId_TriggerType",
                schema: "platform_ai",
                table: "ProactiveItems",
                columns: new[] { "WorkspaceId", "SubjectType", "SubjectId", "TriggerType" },
                unique: true,
                filter: "[Status] <> N'RESOLVED'");

            migrationBuilder.CreateIndex(
                name: "IX_ProactiveItems_WorkspaceId_SubjectType_SubjectId_TriggerType_RiskCycleKey",
                schema: "platform_ai",
                table: "ProactiveItems",
                columns: new[] { "WorkspaceId", "SubjectType", "SubjectId", "TriggerType", "RiskCycleKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceProactivePolicies_Enabled_NextEvaluationAt_LeaseExpiresAt",
                schema: "platform_ai",
                table: "WorkspaceProactivePolicies",
                columns: new[] { "Enabled", "NextEvaluationAt", "LeaseExpiresAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProactiveAudits",
                schema: "platform_ai");

            migrationBuilder.DropTable(
                name: "ProactiveCommands",
                schema: "platform_ai");

            migrationBuilder.DropTable(
                name: "ProactiveItems",
                schema: "platform_ai");

            migrationBuilder.DropTable(
                name: "WorkspaceProactivePolicies",
                schema: "platform_ai");
        }
    }
}
