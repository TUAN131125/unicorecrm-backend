using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Sales.Quotes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class QuoteCustomerDecisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomerDecisionRecords",
                schema: "quotes",
                columns: table => new
                {
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    DecisionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    QuoteId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    QuoteRevision = table.Column<int>(type: "int", nullable: false),
                    ContentResourceVersion = table.Column<long>(type: "bigint", nullable: false),
                    ResultResourceVersion = table.Column<long>(type: "bigint", nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    DecisionType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    VerificationStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ContentFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PolicyVersion = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    RequestFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CommandId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    AuditId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    EventId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerDecisionRecords", x => new { x.WorkspaceId, x.DecisionId });
                    table.CheckConstraint("CK_CustomerDecisionRecords_PayloadJson", "ISJSON([PayloadJson]) = 1");
                    table.CheckConstraint("CK_CustomerDecisionRecords_Trust", "[VerificationStatus] COLLATE Latin1_General_100_BIN2 = 'UNVERIFIED'");
                    table.CheckConstraint("CK_CustomerDecisionRecords_Type", "[DecisionType] COLLATE Latin1_General_100_BIN2 IN ('ACCEPTANCE','REJECTION')");
                    table.CheckConstraint("CK_CustomerDecisionRecords_Version", "[QuoteRevision] >= 1 AND [ContentResourceVersion] >= 1 AND [ResultResourceVersion] = [ContentResourceVersion] + 1");
                    table.ForeignKey(
                        name: "FK_CustomerDecisionRecords_Quotes_WorkspaceId_QuoteId",
                        columns: x => new { x.WorkspaceId, x.QuoteId },
                        principalSchema: "quotes",
                        principalTable: "Quotes",
                        principalColumns: new[] { "WorkspaceId", "QuoteId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDecisionRecords_WorkspaceId_QuoteId_ResultResourceVersion",
                schema: "quotes",
                table: "CustomerDecisionRecords",
                columns: new[] { "WorkspaceId", "QuoteId", "ResultResourceVersion" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerDecisionRecords",
                schema: "quotes");
        }
    }
}
