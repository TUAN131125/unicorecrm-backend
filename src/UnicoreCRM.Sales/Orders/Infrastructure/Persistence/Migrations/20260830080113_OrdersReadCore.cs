using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Sales.Orders.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OrdersReadCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "orders");

            migrationBuilder.CreateTable(
                name: "Orders",
                schema: "orders",
                columns: table => new
                {
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    OrderId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    OrderNumber = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    OrderDate = table.Column<DateOnly>(type: "date", nullable: false),
                    BuyerType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    BuyerId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ContactId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    SourceLeadId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    SourceQuoteId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    SourceQuoteNumber = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    SourceDealId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    State = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
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
                    Currency = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                    ExpectedDeliveryDate = table.Column<DateOnly>(type: "date", nullable: true),
                    RecipientName = table.Column<string>(type: "nvarchar(240)", maxLength: 240, nullable: true),
                    RecipientPhone = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    RecipientEmail = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    ShippingAddressJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OwnerId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    CreditPolicyEvaluationJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ActionsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                    ArchiveReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ResourceVersion = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    CreditApprovalJson = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orders", x => new { x.WorkspaceId, x.OrderId });
                    table.CheckConstraint("CK_Orders_ActionsJson", "ISJSON([ActionsJson]) = 1");
                    table.CheckConstraint("CK_Orders_AdjustmentsJson", "[AdjustmentsJson] IS NULL OR ISJSON([AdjustmentsJson]) = 1");
                    table.CheckConstraint("CK_Orders_BuyerType", "(([BuyerType] COLLATE Latin1_General_100_BIN2 = N'CONTACT' AND DATALENGTH([BuyerType]) = DATALENGTH(N'CONTACT')) OR ([BuyerType] COLLATE Latin1_General_100_BIN2 = N'ORGANIZATION_ACCOUNT' AND DATALENGTH([BuyerType]) = DATALENGTH(N'ORGANIZATION_ACCOUNT')))");
                    table.CheckConstraint("CK_Orders_CreditApprovalJson", "[CreditApprovalJson] IS NULL OR ISJSON([CreditApprovalJson]) = 1");
                    table.CheckConstraint("CK_Orders_CreditPolicyEvaluationJson", "[CreditPolicyEvaluationJson] IS NULL OR ISJSON([CreditPolicyEvaluationJson]) = 1");
                    table.CheckConstraint("CK_Orders_LineItemsJson", "ISJSON([LineItemsJson]) = 1");
                    table.CheckConstraint("CK_Orders_ResourceVersion", "[ResourceVersion] >= 0");
                    table.CheckConstraint("CK_Orders_ShippingAddressJson", "[ShippingAddressJson] IS NULL OR ISJSON([ShippingAddressJson]) = 1");
                    table.CheckConstraint("CK_Orders_State", "(([State] COLLATE Latin1_General_100_BIN2 = N'DRAFT' AND DATALENGTH([State]) = DATALENGTH(N'DRAFT')) OR ([State] COLLATE Latin1_General_100_BIN2 = N'CONFIRMED' AND DATALENGTH([State]) = DATALENGTH(N'CONFIRMED')) OR ([State] COLLATE Latin1_General_100_BIN2 = N'COMPLETED' AND DATALENGTH([State]) = DATALENGTH(N'COMPLETED')) OR ([State] COLLATE Latin1_General_100_BIN2 = N'CANCELLED' AND DATALENGTH([State]) = DATALENGTH(N'CANCELLED')))");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_WorkspaceId_BuyerType_BuyerId",
                schema: "orders",
                table: "Orders",
                columns: new[] { "WorkspaceId", "BuyerType", "BuyerId" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_WorkspaceId_CreatedAt_OrderId",
                schema: "orders",
                table: "Orders",
                columns: new[] { "WorkspaceId", "CreatedAt", "OrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_WorkspaceId_GrandTotalAmount_OrderId",
                schema: "orders",
                table: "Orders",
                columns: new[] { "WorkspaceId", "GrandTotalAmount", "OrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_WorkspaceId_OrderDate_OrderId",
                schema: "orders",
                table: "Orders",
                columns: new[] { "WorkspaceId", "OrderDate", "OrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_WorkspaceId_OrderNumber_OrderId",
                schema: "orders",
                table: "Orders",
                columns: new[] { "WorkspaceId", "OrderNumber", "OrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_WorkspaceId_SourceDealId",
                schema: "orders",
                table: "Orders",
                columns: new[] { "WorkspaceId", "SourceDealId" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_WorkspaceId_SourceQuoteId",
                schema: "orders",
                table: "Orders",
                columns: new[] { "WorkspaceId", "SourceQuoteId" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_WorkspaceId_State",
                schema: "orders",
                table: "Orders",
                columns: new[] { "WorkspaceId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_WorkspaceId_UpdatedAt_OrderId",
                schema: "orders",
                table: "Orders",
                columns: new[] { "WorkspaceId", "UpdatedAt", "OrderId" },
                descending: new[] { false, true, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Orders",
                schema: "orders");
        }
    }
}
