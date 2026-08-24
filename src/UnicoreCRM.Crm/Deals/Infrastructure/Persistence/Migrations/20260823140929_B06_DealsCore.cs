using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Crm.Deals.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class B06_DealsCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "deals");

            migrationBuilder.CreateTable(
                name: "AuditRecords",
                schema: "deals",
                columns: table => new
                {
                    AuditId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(96)", maxLength: 96, nullable: false),
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    AggregateId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    RequestId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PriorVersion = table.Column<long>(type: "bigint", nullable: true),
                    NewVersion = table.Column<long>(type: "bigint", nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditRecords", x => x.AuditId);
                });

            migrationBuilder.CreateTable(
                name: "Deals",
                schema: "deals",
                columns: table => new
                {
                    DealId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Profile = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StageCode = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    StageCategory = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ForecastCategory = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    ForecastHistory = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StageEnteredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    NextActionAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    NextActionSummary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    NextActionType = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    NextActionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    WinEvidenceType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    WinEvidenceSourceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    WinEvidenceOccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    WonAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LostAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ActualCloseDate = table.Column<DateOnly>(type: "date", nullable: true),
                    LostReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    LostReasonNote = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    RecycleDecision = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    RecycleEligible = table.Column<bool>(type: "bit", nullable: true),
                    RevisitAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ArchiveReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Deals", x => x.DealId);
                });

            migrationBuilder.CreateTable(
                name: "IdempotencyRecords",
                schema: "deals",
                columns: table => new
                {
                    ScopeKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(96)", maxLength: 96, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    TargetId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Fingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ResponseJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdempotencyRecords", x => x.ScopeKey);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                schema: "deals",
                columns: table => new
                {
                    EventId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AggregateId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.EventId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditRecords_AggregateId_OccurredAt",
                schema: "deals",
                table: "AuditRecords",
                columns: new[] { "AggregateId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditRecords_WorkspaceId_OccurredAt",
                schema: "deals",
                table: "AuditRecords",
                columns: new[] { "WorkspaceId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Deals_WorkspaceId_StageCategory_StageCode",
                schema: "deals",
                table: "Deals",
                columns: new[] { "WorkspaceId", "StageCategory", "StageCode" });

            migrationBuilder.CreateIndex(
                name: "IX_Deals_WorkspaceId_UpdatedAt_DealId",
                schema: "deals",
                table: "Deals",
                columns: new[] { "WorkspaceId", "UpdatedAt", "DealId" });

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_WorkspaceId_CreatedAt",
                schema: "deals",
                table: "IdempotencyRecords",
                columns: new[] { "WorkspaceId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_WorkspaceId_OccurredAt",
                schema: "deals",
                table: "OutboxMessages",
                columns: new[] { "WorkspaceId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditRecords",
                schema: "deals");

            migrationBuilder.DropTable(
                name: "Deals",
                schema: "deals");

            migrationBuilder.DropTable(
                name: "IdempotencyRecords",
                schema: "deals");

            migrationBuilder.DropTable(
                name: "OutboxMessages",
                schema: "deals");
        }
    }
}
