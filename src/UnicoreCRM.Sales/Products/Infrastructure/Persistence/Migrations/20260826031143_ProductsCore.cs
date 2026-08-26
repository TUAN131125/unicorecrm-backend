using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Sales.Products.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProductsCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "products");

            migrationBuilder.CreateTable(
                name: "AuditRecords",
                schema: "products",
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
                schema: "products",
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
                schema: "products",
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
                name: "Products",
                schema: "products",
                columns: table => new
                {
                    ProductId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Profile = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NormalizedSku = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    ArchivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ArchiveReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.ProductId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditRecords_AggregateId_OccurredAt",
                schema: "products",
                table: "AuditRecords",
                columns: new[] { "AggregateId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditRecords_WorkspaceId_OccurredAt",
                schema: "products",
                table: "AuditRecords",
                columns: new[] { "WorkspaceId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_WorkspaceId_CreatedAt",
                schema: "products",
                table: "IdempotencyRecords",
                columns: new[] { "WorkspaceId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_WorkspaceId_OccurredAt",
                schema: "products",
                table: "OutboxMessages",
                columns: new[] { "WorkspaceId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_WorkspaceId_CreatedAt_ProductId",
                schema: "products",
                table: "Products",
                columns: new[] { "WorkspaceId", "CreatedAt", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_WorkspaceId_NormalizedSku",
                schema: "products",
                table: "Products",
                columns: new[] { "WorkspaceId", "NormalizedSku" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditRecords",
                schema: "products");

            migrationBuilder.DropTable(
                name: "IdempotencyRecords",
                schema: "products");

            migrationBuilder.DropTable(
                name: "OutboxMessages",
                schema: "products");

            migrationBuilder.DropTable(
                name: "Products",
                schema: "products");
        }
    }
}
