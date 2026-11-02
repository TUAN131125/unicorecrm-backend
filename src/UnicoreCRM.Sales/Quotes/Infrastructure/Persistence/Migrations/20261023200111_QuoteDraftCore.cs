using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Sales.Quotes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class QuoteDraftCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "QuoteNumbers",
                schema: "quotes");
            // Preserve pre-existing numeric business identities, including archived records.
            // Sequence allocation may have gaps; numbers are never based on row counts.
            migrationBuilder.Sql("""
                DECLARE @next bigint = (SELECT ISNULL(MAX(TRY_CONVERT(bigint, SUBSTRING(QuoteNumber, 3, 118))), 0) + 1
                    FROM quotes.Quotes WHERE QuoteNumber LIKE N'Q-%');
                IF @next > 1 BEGIN
                    DECLARE @statement nvarchar(200) = N'ALTER SEQUENCE quotes.QuoteNumbers RESTART WITH ' + CONVERT(nvarchar(30), @next);
                    EXEC sys.sp_executesql @statement;
                END;
                """);

            migrationBuilder.AddColumn<string>(
                name: "ProductSnapshotsJson",
                schema: "quotes",
                table: "Quotes",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CommandRecords",
                schema: "quotes",
                columns: table => new
                {
                    ScopeKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    QuoteId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Fingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ResponseJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AuditId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RequestId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PriorVersion = table.Column<long>(type: "bigint", nullable: true),
                    ResourceVersion = table.Column<long>(type: "bigint", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommandRecords", x => x.ScopeKey);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                schema: "quotes",
                columns: table => new
                {
                    EventId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    QuoteId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.EventId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_WorkspaceId_QuoteNumber",
                schema: "quotes",
                table: "Quotes",
                columns: new[] { "WorkspaceId", "QuoteNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommandRecords_AuditId",
                schema: "quotes",
                table: "CommandRecords",
                column: "AuditId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommandRecords_WorkspaceId_QuoteId_OccurredAt",
                schema: "quotes",
                table: "CommandRecords",
                columns: new[] { "WorkspaceId", "QuoteId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_WorkspaceId_OccurredAt",
                schema: "quotes",
                table: "OutboxMessages",
                columns: new[] { "WorkspaceId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CommandRecords",
                schema: "quotes");

            migrationBuilder.DropTable(
                name: "OutboxMessages",
                schema: "quotes");

            migrationBuilder.DropIndex(
                name: "IX_Quotes_WorkspaceId_QuoteNumber",
                schema: "quotes",
                table: "Quotes");

            migrationBuilder.DropColumn(
                name: "ProductSnapshotsJson",
                schema: "quotes",
                table: "Quotes");

            migrationBuilder.DropSequence(
                name: "QuoteNumbers",
                schema: "quotes");
        }
    }
}
