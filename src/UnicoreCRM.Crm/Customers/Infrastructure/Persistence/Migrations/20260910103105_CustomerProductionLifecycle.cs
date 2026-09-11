using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Crm.Customers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CustomerProductionLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Customers_WorkspaceId_CreatedAt_CustomerId",
                schema: "customers",
                table: "Customers");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "LastPurchaseAt",
                schema: "customers",
                table: "Customers",
                type: "datetimeoffset(7)",
                precision: 7,
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset(7)",
                oldPrecision: 7);

            migrationBuilder.AlterColumn<string>(
                name: "Health",
                schema: "customers",
                table: "Customers",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(16)",
                oldMaxLength: 16);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "FirstPurchaseAt",
                schema: "customers",
                table: "Customers",
                type: "datetimeoffset(7)",
                precision: 7,
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset(7)",
                oldPrecision: 7);

            migrationBuilder.AddColumn<string>(
                name: "OwnerId",
                schema: "customers",
                table: "Customers",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SearchText",
                schema: "customers",
                table: "Customers",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Segment",
                schema: "customers",
                table: "Customers",
                type: "nvarchar(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Tier",
                schema: "customers",
                table: "Customers",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE [customers].[Customers]
                SET [Segment] = UPPER(NULLIF(LTRIM(RTRIM(JSON_VALUE([Profile], '$.segment'))), N'')),
                    [Tier] = UPPER(NULLIF(LTRIM(RTRIM(JSON_VALUE([Profile], '$.tier'))), N'')),
                    [SearchText] = UPPER(CONCAT([CustomerCode], N' ',
                        COALESCE(JSON_VALUE([Profile], '$.segment'), N''), N' ',
                        COALESCE(JSON_VALUE([Profile], '$.tier'), N'')));
                """);

            migrationBuilder.CreateTable(
                name: "AuditRecords",
                schema: "customers",
                columns: table => new
                {
                    AuditId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(96)", maxLength: 96, nullable: false),
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    AggregateId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RequestId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    NewVersion = table.Column<long>(type: "bigint", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditRecords", x => x.AuditId);
                });

            migrationBuilder.CreateTable(
                name: "IdempotencyRecords",
                schema: "customers",
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
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdempotencyRecords", x => x.ScopeKey);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                schema: "customers",
                columns: table => new
                {
                    EventId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AggregateId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.EventId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_WorkspaceId_CustomerCode",
                schema: "customers",
                table: "Customers",
                columns: new[] { "WorkspaceId", "CustomerCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_WorkspaceId_OwnerId_Status_CreatedAt_CustomerId",
                schema: "customers",
                table: "Customers",
                columns: new[] { "WorkspaceId", "OwnerId", "Status", "CreatedAt", "CustomerId" },
                descending: new[] { false, false, false, true, false });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_WorkspaceId_Segment",
                schema: "customers",
                table: "Customers",
                columns: new[] { "WorkspaceId", "Segment" });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_WorkspaceId_Status_CreatedAt_CustomerId",
                schema: "customers",
                table: "Customers",
                columns: new[] { "WorkspaceId", "Status", "CreatedAt", "CustomerId" },
                descending: new[] { false, false, true, false });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_WorkspaceId_Tier",
                schema: "customers",
                table: "Customers",
                columns: new[] { "WorkspaceId", "Tier" });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_WorkspaceId_Type",
                schema: "customers",
                table: "Customers",
                columns: new[] { "WorkspaceId", "Type" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditRecords",
                schema: "customers");

            migrationBuilder.DropTable(
                name: "IdempotencyRecords",
                schema: "customers");

            migrationBuilder.DropTable(
                name: "OutboxMessages",
                schema: "customers");

            migrationBuilder.DropIndex(
                name: "IX_Customers_WorkspaceId_CustomerCode",
                schema: "customers",
                table: "Customers");

            migrationBuilder.DropIndex(
                name: "IX_Customers_WorkspaceId_OwnerId_Status_CreatedAt_CustomerId",
                schema: "customers",
                table: "Customers");

            migrationBuilder.DropIndex(
                name: "IX_Customers_WorkspaceId_Segment",
                schema: "customers",
                table: "Customers");

            migrationBuilder.DropIndex(
                name: "IX_Customers_WorkspaceId_Status_CreatedAt_CustomerId",
                schema: "customers",
                table: "Customers");

            migrationBuilder.DropIndex(
                name: "IX_Customers_WorkspaceId_Tier",
                schema: "customers",
                table: "Customers");

            migrationBuilder.DropIndex(
                name: "IX_Customers_WorkspaceId_Type",
                schema: "customers",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                schema: "customers",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "SearchText",
                schema: "customers",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "Segment",
                schema: "customers",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "Tier",
                schema: "customers",
                table: "Customers");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "LastPurchaseAt",
                schema: "customers",
                table: "Customers",
                type: "datetimeoffset(7)",
                precision: 7,
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)),
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset(7)",
                oldPrecision: 7,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Health",
                schema: "customers",
                table: "Customers",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(16)",
                oldMaxLength: 16,
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "FirstPurchaseAt",
                schema: "customers",
                table: "Customers",
                type: "datetimeoffset(7)",
                precision: 7,
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)),
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset(7)",
                oldPrecision: 7,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_WorkspaceId_CreatedAt_CustomerId",
                schema: "customers",
                table: "Customers",
                columns: new[] { "WorkspaceId", "CreatedAt", "CustomerId" },
                descending: new[] { false, true, false });
        }
    }
}
