using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Billing.Payments.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OrderPaymentPlanActivation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlanEvents",
                schema: "payments",
                columns: table => new
                {
                    EventId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PlanId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CommandId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanEvents", x => x.EventId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlanEvents_WorkspaceId_PlanId_EventType",
                schema: "payments",
                table: "PlanEvents",
                columns: new[] { "WorkspaceId", "PlanId", "EventType" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlanEvents",
                schema: "payments");
        }
    }
}
