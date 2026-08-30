using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Sales.Quotes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class QuotesReadCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "quotes");

            migrationBuilder.CreateTable(
                name: "Quotes",
                schema: "quotes",
                columns: table => new
                {
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    QuoteId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    QuoteNumber = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    QuoteRevision = table.Column<int>(type: "int", nullable: false),
                    RootQuoteId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RevisionOfQuoteId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    BuyerType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    BuyerId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SourcePath = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    SourceDealId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ContactId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    SourceLeadId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Currency = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    OwnerId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    RecipientEmail = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    LineItemsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AdjustmentsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SubtotalAmount = table.Column<decimal>(type: "decimal(38,6)", precision: 38, scale: 6, nullable: false),
                    SubtotalCurrency = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    DiscountTotalAmount = table.Column<decimal>(type: "decimal(38,6)", precision: 38, scale: 6, nullable: false),
                    DiscountTotalCurrency = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    TaxTotalAmount = table.Column<decimal>(type: "decimal(38,6)", precision: 38, scale: 6, nullable: false),
                    TaxTotalCurrency = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    GrandTotalAmount = table.Column<decimal>(type: "decimal(38,6)", precision: 38, scale: 6, nullable: false),
                    GrandTotalCurrency = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    ValidUntil = table.Column<DateOnly>(type: "date", nullable: true),
                    ReviewRequestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                    SentAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                    AcceptedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                    RejectedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                    ExpiredAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                    ArchiveReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ActionsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ApprovalStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ApprovalRequired = table.Column<bool>(type: "bit", nullable: true),
                    ApprovalReasonsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ApprovalRequestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                    ApprovalRequestedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                    ApprovedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ApprovalDecisionNote = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ApprovalContentFingerprint = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ApprovalPolicyVersion = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    PaymentAgreementJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DeliveryHistoryJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SenderName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    SenderAddress = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SenderEmail = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    SenderTaxId = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    ResourceVersion = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Quotes", x => new { x.WorkspaceId, x.QuoteId });
                    table.CheckConstraint("CK_Quotes_ActionsJson", "ISJSON([ActionsJson]) = 1");
                    table.CheckConstraint("CK_Quotes_AdjustmentsJson", "[AdjustmentsJson] IS NULL OR ISJSON([AdjustmentsJson]) = 1");
                    table.CheckConstraint("CK_Quotes_ApprovalReasonsJson", "[ApprovalReasonsJson] IS NULL OR ISJSON([ApprovalReasonsJson]) = 1");
                    table.CheckConstraint("CK_Quotes_ApprovalStatus", "[ApprovalStatus] IS NULL OR ((([ApprovalStatus] COLLATE Latin1_General_100_BIN2 = N'NOT_REQUIRED' AND DATALENGTH([ApprovalStatus]) = DATALENGTH(N'NOT_REQUIRED')) OR ([ApprovalStatus] COLLATE Latin1_General_100_BIN2 = N'PENDING' AND DATALENGTH([ApprovalStatus]) = DATALENGTH(N'PENDING')) OR ([ApprovalStatus] COLLATE Latin1_General_100_BIN2 = N'APPROVED' AND DATALENGTH([ApprovalStatus]) = DATALENGTH(N'APPROVED')) OR ([ApprovalStatus] COLLATE Latin1_General_100_BIN2 = N'CHANGES_REQUESTED' AND DATALENGTH([ApprovalStatus]) = DATALENGTH(N'CHANGES_REQUESTED'))))");
                    table.CheckConstraint("CK_Quotes_BuyerType", "(([BuyerType] COLLATE Latin1_General_100_BIN2 = N'CONTACT' AND DATALENGTH([BuyerType]) = DATALENGTH(N'CONTACT')) OR ([BuyerType] COLLATE Latin1_General_100_BIN2 = N'ORGANIZATION_ACCOUNT' AND DATALENGTH([BuyerType]) = DATALENGTH(N'ORGANIZATION_ACCOUNT')))");
                    table.CheckConstraint("CK_Quotes_DeliveryHistoryJson", "[DeliveryHistoryJson] IS NULL OR ISJSON([DeliveryHistoryJson]) = 1");
                    table.CheckConstraint("CK_Quotes_LineItemsJson", "ISJSON([LineItemsJson]) = 1");
                    table.CheckConstraint("CK_Quotes_PaymentAgreementJson", "[PaymentAgreementJson] IS NULL OR ISJSON([PaymentAgreementJson]) = 1");
                    table.CheckConstraint("CK_Quotes_QuoteRevision", "[QuoteRevision] >= 1");
                    table.CheckConstraint("CK_Quotes_ResourceVersion", "[ResourceVersion] >= 0");
                    table.CheckConstraint("CK_Quotes_SourcePath", "(([SourcePath] COLLATE Latin1_General_100_BIN2 = N'DEAL' AND DATALENGTH([SourcePath]) = DATALENGTH(N'DEAL')) OR ([SourcePath] COLLATE Latin1_General_100_BIN2 = N'DIRECT_SALE' AND DATALENGTH([SourcePath]) = DATALENGTH(N'DIRECT_SALE')))");
                    table.CheckConstraint("CK_Quotes_Status", "(([Status] COLLATE Latin1_General_100_BIN2 = N'DRAFT' AND DATALENGTH([Status]) = DATALENGTH(N'DRAFT')) OR ([Status] COLLATE Latin1_General_100_BIN2 = N'REVIEW' AND DATALENGTH([Status]) = DATALENGTH(N'REVIEW')) OR ([Status] COLLATE Latin1_General_100_BIN2 = N'SENT' AND DATALENGTH([Status]) = DATALENGTH(N'SENT')) OR ([Status] COLLATE Latin1_General_100_BIN2 = N'ACCEPTED' AND DATALENGTH([Status]) = DATALENGTH(N'ACCEPTED')) OR ([Status] COLLATE Latin1_General_100_BIN2 = N'REJECTED' AND DATALENGTH([Status]) = DATALENGTH(N'REJECTED')) OR ([Status] COLLATE Latin1_General_100_BIN2 = N'EXPIRED' AND DATALENGTH([Status]) = DATALENGTH(N'EXPIRED')))");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_WorkspaceId_BuyerType_BuyerId",
                schema: "quotes",
                table: "Quotes",
                columns: new[] { "WorkspaceId", "BuyerType", "BuyerId" });

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_WorkspaceId_CreatedAt_QuoteId",
                schema: "quotes",
                table: "Quotes",
                columns: new[] { "WorkspaceId", "CreatedAt", "QuoteId" },
                descending: new[] { false, true, false });

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_WorkspaceId_GrandTotalAmount_QuoteId",
                schema: "quotes",
                table: "Quotes",
                columns: new[] { "WorkspaceId", "GrandTotalAmount", "QuoteId" });

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_WorkspaceId_QuoteNumber_QuoteId",
                schema: "quotes",
                table: "Quotes",
                columns: new[] { "WorkspaceId", "QuoteNumber", "QuoteId" });

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_WorkspaceId_SourceDealId",
                schema: "quotes",
                table: "Quotes",
                columns: new[] { "WorkspaceId", "SourceDealId" });

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_WorkspaceId_Status",
                schema: "quotes",
                table: "Quotes",
                columns: new[] { "WorkspaceId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_WorkspaceId_UpdatedAt_QuoteId",
                schema: "quotes",
                table: "Quotes",
                columns: new[] { "WorkspaceId", "UpdatedAt", "QuoteId" },
                descending: new[] { false, true, false });

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_WorkspaceId_ValidUntil_QuoteId",
                schema: "quotes",
                table: "Quotes",
                columns: new[] { "WorkspaceId", "ValidUntil", "QuoteId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Quotes",
                schema: "quotes");
        }
    }
}
