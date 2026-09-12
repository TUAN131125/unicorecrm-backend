using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Workflows.Atomic.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LeadCustomerConversionAnchor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LeadCustomerConversionAnchors",
                schema: "workflow",
                columns: table => new
                {
                    ScopeKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ConversionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    LeadId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ConversionType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Fingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ExpectedLeadVersion = table.Column<long>(type: "bigint", nullable: false),
                    OriginalAccountId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    OriginalMemberId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    OriginalMembershipId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RequestId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SubjectType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SubjectMode = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SelectedSubjectId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    NewContactJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StakeholderJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FrozenLeadOwnerId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    FrozenDoNotCall = table.Column<bool>(type: "bit", nullable: true),
                    FrozenDoNotEmail = table.Column<bool>(type: "bit", nullable: true),
                    Stage = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    SubjectId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    SubjectVersion = table.Column<long>(type: "bigint", nullable: true),
                    SubjectCreated = table.Column<bool>(type: "bit", nullable: true),
                    CustomerId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CustomerVersion = table.Column<long>(type: "bigint", nullable: true),
                    CustomerResolution = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    StakeholderRelationshipId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    LeadVersion = table.Column<long>(type: "bigint", nullable: true),
                    ResponseJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EmittedEventIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AuditEvidenceIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LastErrorCategory = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    LastErrorCode = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    NextRetryAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RecoveryExecutorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeadCustomerConversionAnchors", x => x.ScopeKey);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LeadCustomerConversionAnchors_ConversionId",
                schema: "workflow",
                table: "LeadCustomerConversionAnchors",
                column: "ConversionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeadCustomerConversionAnchors_Stage_NextRetryAt_UpdatedAt",
                schema: "workflow",
                table: "LeadCustomerConversionAnchors",
                columns: new[] { "Stage", "NextRetryAt", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LeadCustomerConversionAnchors_WorkspaceId_LeadId_ConversionType",
                schema: "workflow",
                table: "LeadCustomerConversionAnchors",
                columns: new[] { "WorkspaceId", "LeadId", "ConversionType" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LeadCustomerConversionAnchors",
                schema: "workflow");
        }
    }
}
