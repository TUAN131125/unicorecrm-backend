using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Workflows.Atomic.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LeadHandoverAnchor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LeadHandoverAnchors",
                schema: "workflow",
                columns: table => new
                {
                    ScopeKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    HandoverId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    LeadId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ActiveLeadKey = table.Column<string>(type: "nvarchar(257)", maxLength: 257, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RequestFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ExpectedLeadVersion = table.Column<long>(type: "bigint", nullable: false),
                    OriginalAccountId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    OriginalMemberId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    OriginalMembershipId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    OriginalPrincipalId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RequestId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PreviousOwnerId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    NewOwnerId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    OpenTaskPolicy = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ResolvedSlaHours = table.Column<int>(type: "int", nullable: false),
                    HandoverOccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TakeoverDueAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Stage = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    TasksResultJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LeadResultJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResponseJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EmittedEventIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AuditEvidenceIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LastErrorCategory = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    LastErrorCode = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    NextRetryAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ExecutionAttemptId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ExecutionPrincipalId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ExecutionLeaseAcquiredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ExecutionLeaseExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeadHandoverAnchors", x => x.ScopeKey);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LeadHandoverAnchors_ActiveLeadKey",
                schema: "workflow",
                table: "LeadHandoverAnchors",
                column: "ActiveLeadKey",
                unique: true,
                filter: "[ActiveLeadKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LeadHandoverAnchors_HandoverId",
                schema: "workflow",
                table: "LeadHandoverAnchors",
                column: "HandoverId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeadHandoverAnchors_Stage_NextRetryAt_UpdatedAt",
                schema: "workflow",
                table: "LeadHandoverAnchors",
                columns: new[] { "Stage", "NextRetryAt", "UpdatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM [workflow].[LeadHandoverAnchors] WHERE [ActiveLeadKey] IS NOT NULL) THROW 51000, 'Cannot remove active Lead handover coordination state.', 1;");
            migrationBuilder.DropTable(
                name: "LeadHandoverAnchors",
                schema: "workflow");
        }
    }
}
