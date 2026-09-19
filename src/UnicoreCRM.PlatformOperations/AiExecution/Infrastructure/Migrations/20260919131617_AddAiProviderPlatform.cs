using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.PlatformOperations.AiExecution.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAiProviderPlatform : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiConfigurationAudits",
                schema: "platform_ai",
                columns: table => new
                {
                    AuditId = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    MemberId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    SafeSummaryJson = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiConfigurationAudits", x => x.AuditId);
                });

            migrationBuilder.CreateTable(
                name: "AiConfigurationCommands",
                schema: "platform_ai",
                columns: table => new
                {
                    ScopeKey = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    MemberId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Fingerprint = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ResultVersion = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiConfigurationCommands", x => x.ScopeKey);
                });

            migrationBuilder.CreateTable(
                name: "AiProviderAttempts",
                schema: "platform_ai",
                columns: table => new
                {
                    AttemptId = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    ExecutionId = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    AttemptNumber = table.Column<int>(type: "int", nullable: false),
                    AttemptKind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Model = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DurationMilliseconds = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ProviderRequestId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    InputTokens = table.Column<int>(type: "int", nullable: true),
                    OutputTokens = table.Column<int>(type: "int", nullable: true),
                    ErrorCategory = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    SafeDiagnostic = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiProviderAttempts", x => x.AttemptId);
                });

            migrationBuilder.CreateTable(
                name: "WorkspaceAiConfigurations",
                schema: "platform_ai",
                columns: table => new
                {
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PrimaryProvider = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PrimaryModel = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PrimaryCredentialSource = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PrimaryProtectedCredential = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: true),
                    FallbackEnabled = table.Column<bool>(type: "bit", nullable: false),
                    FallbackProvider = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    FallbackModel = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    FallbackCredentialSource = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    FallbackProtectedCredential = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: true),
                    ActivePolicyJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ActivePrimaryProtectedCredential = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: true),
                    ActiveFallbackProtectedCredential = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: true),
                    RetryRateLimited = table.Column<bool>(type: "bit", nullable: false),
                    IsValidated = table.Column<bool>(type: "bit", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ActivatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkspaceAiConfigurations", x => x.WorkspaceId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiConfigurationAudits_WorkspaceId_OccurredAt",
                schema: "platform_ai",
                table: "AiConfigurationAudits",
                columns: new[] { "WorkspaceId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AiConfigurationCommands_WorkspaceId_MemberId_Operation_IdempotencyKey",
                schema: "platform_ai",
                table: "AiConfigurationCommands",
                columns: new[] { "WorkspaceId", "MemberId", "Operation", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiProviderAttempts_ExecutionId_AttemptNumber",
                schema: "platform_ai",
                table: "AiProviderAttempts",
                columns: new[] { "ExecutionId", "AttemptNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiConfigurationAudits",
                schema: "platform_ai");

            migrationBuilder.DropTable(
                name: "AiConfigurationCommands",
                schema: "platform_ai");

            migrationBuilder.DropTable(
                name: "AiProviderAttempts",
                schema: "platform_ai");

            migrationBuilder.DropTable(
                name: "WorkspaceAiConfigurations",
                schema: "platform_ai");
        }
    }
}
