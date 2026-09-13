using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.PlatformOperations.Outbox.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreateIntegrationEventJournal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ops");

            migrationBuilder.CreateTable(
                name: "IntegrationEventJournal",
                schema: "ops",
                columns: table => new
                {
                    Sequence = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EventId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SourceOwner = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    SubjectType = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    SubjectId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CanonicalEnvelopeJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrationEventJournal", x => x.Sequence);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationEventJournal_EventId",
                schema: "ops",
                table: "IntegrationEventJournal",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationEventJournal_WorkspaceId_EventType_Sequence",
                schema: "ops",
                table: "IntegrationEventJournal",
                columns: new[] { "WorkspaceId", "EventType", "Sequence" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IntegrationEventJournal",
                schema: "ops");
        }
    }
}
