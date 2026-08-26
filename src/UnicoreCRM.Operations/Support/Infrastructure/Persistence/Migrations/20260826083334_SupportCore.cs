using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Operations.Support.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SupportCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "support");

            migrationBuilder.CreateTable(
                name: "AuditRecords",
                schema: "support",
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
                name: "IdempotencyRecords",
                schema: "support",
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
                schema: "support",
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

            migrationBuilder.CreateTable(
                name: "SupportCaseComments",
                schema: "support",
                columns: table => new
                {
                    CommentId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CaseId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", maxLength: 10000, nullable: false),
                    AuthorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    IsInternal = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportCaseComments", x => x.CommentId);
                });

            migrationBuilder.CreateTable(
                name: "SupportCases",
                schema: "support",
                columns: table => new
                {
                    CaseId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CaseNumber = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CaseYear = table.Column<int>(type: "int", nullable: false),
                    CaseSequence = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", maxLength: 10000, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    Category = table.Column<int>(type: "int", nullable: false),
                    Source = table.Column<int>(type: "int", nullable: false),
                    Channel = table.Column<int>(type: "int", nullable: true),
                    RelationshipType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    RelationshipId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ContactId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    RelatedOrderId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    RelatedProductId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    RelatedOwnedProductId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    OwnerId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Tags = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NextFollowUpAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FirstResponseDueAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ResolutionDueAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReopenedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ResolutionSummary = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportCases", x => x.CaseId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditRecords_AggregateId_OccurredAt",
                schema: "support",
                table: "AuditRecords",
                columns: new[] { "AggregateId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditRecords_WorkspaceId_OccurredAt",
                schema: "support",
                table: "AuditRecords",
                columns: new[] { "WorkspaceId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_WorkspaceId_CreatedAt",
                schema: "support",
                table: "IdempotencyRecords",
                columns: new[] { "WorkspaceId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_WorkspaceId_OccurredAt",
                schema: "support",
                table: "OutboxMessages",
                columns: new[] { "WorkspaceId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportCaseComments_WorkspaceId_CaseId_CreatedAt",
                schema: "support",
                table: "SupportCaseComments",
                columns: new[] { "WorkspaceId", "CaseId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportCases_WorkspaceId_CaseNumber",
                schema: "support",
                table: "SupportCases",
                columns: new[] { "WorkspaceId", "CaseNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupportCases_WorkspaceId_CaseYear_CaseSequence",
                schema: "support",
                table: "SupportCases",
                columns: new[] { "WorkspaceId", "CaseYear", "CaseSequence" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportCases_WorkspaceId_Status_CaseId",
                schema: "support",
                table: "SupportCases",
                columns: new[] { "WorkspaceId", "Status", "CaseId" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportCases_WorkspaceId_UpdatedAt_CaseId",
                schema: "support",
                table: "SupportCases",
                columns: new[] { "WorkspaceId", "UpdatedAt", "CaseId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditRecords",
                schema: "support");

            migrationBuilder.DropTable(
                name: "IdempotencyRecords",
                schema: "support");

            migrationBuilder.DropTable(
                name: "OutboxMessages",
                schema: "support");

            migrationBuilder.DropTable(
                name: "SupportCaseComments",
                schema: "support");

            migrationBuilder.DropTable(
                name: "SupportCases",
                schema: "support");
        }
    }
}
