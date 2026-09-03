using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Crm.Contacts.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ContactQualificationParticipant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NormalizedPersonalEmail",
                schema: "contacts",
                table: "Contacts",
                type: "nvarchar(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedWorkEmail",
                schema: "contacts",
                table: "Contacts",
                type: "nvarchar(320)",
                maxLength: 320,
                nullable: true);

            // Backfill the derived projections for rows that predate them, so an already-present
            // Contact still participates in the Workspace-wide duplicate guard instead of being
            // invisible to it. The profile is persisted as camelCase JSON by the owner's value
            // converter, so JSON_VALUE reaches the two addresses directly.
            //
            // T-SQL UPPER() is collation-sensitive while the runtime rule is ToUpperInvariant(). The
            // two agree across the ASCII range that dominates this data; a row whose address falls
            // outside it and whose collation disagrees would normalize differently here than at
            // runtime. That is accepted for a one-time backfill of pre-existing rows: every row
            // written from now on is normalized by the domain rule.
            migrationBuilder.Sql("""
                UPDATE contacts.Contacts
                SET NormalizedWorkEmail =
                        NULLIF(UPPER(LTRIM(RTRIM(JSON_VALUE(Profile, '$.workEmail')))), N''),
                    NormalizedPersonalEmail =
                        NULLIF(UPPER(LTRIM(RTRIM(JSON_VALUE(Profile, '$.personalEmail')))), N'');
                """);

            migrationBuilder.CreateTable(
                name: "AuditRecords",
                schema: "contacts",
                columns: table => new
                {
                    AuditId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(96)", maxLength: 96, nullable: false),
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    AggregateId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RequestId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    ActorType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditRecords", x => x.AuditId);
                });

            migrationBuilder.CreateTable(
                name: "ConversionRecords",
                schema: "contacts",
                columns: table => new
                {
                    ScopeKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ConversionKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ContactId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversionRecords", x => x.ScopeKey);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                schema: "contacts",
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
                name: "IX_Contacts_WorkspaceId_NormalizedPersonalEmail",
                schema: "contacts",
                table: "Contacts",
                columns: new[] { "WorkspaceId", "NormalizedPersonalEmail" });

            migrationBuilder.CreateIndex(
                name: "IX_Contacts_WorkspaceId_NormalizedWorkEmail",
                schema: "contacts",
                table: "Contacts",
                columns: new[] { "WorkspaceId", "NormalizedWorkEmail" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditRecords_AggregateId_OccurredAt",
                schema: "contacts",
                table: "AuditRecords",
                columns: new[] { "AggregateId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditRecords_WorkspaceId_OccurredAt",
                schema: "contacts",
                table: "AuditRecords",
                columns: new[] { "WorkspaceId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ConversionRecords_WorkspaceId_CreatedAt",
                schema: "contacts",
                table: "ConversionRecords",
                columns: new[] { "WorkspaceId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_WorkspaceId_OccurredAt",
                schema: "contacts",
                table: "OutboxMessages",
                columns: new[] { "WorkspaceId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditRecords",
                schema: "contacts");

            migrationBuilder.DropTable(
                name: "ConversionRecords",
                schema: "contacts");

            migrationBuilder.DropTable(
                name: "OutboxMessages",
                schema: "contacts");

            migrationBuilder.DropIndex(
                name: "IX_Contacts_WorkspaceId_NormalizedPersonalEmail",
                schema: "contacts",
                table: "Contacts");

            migrationBuilder.DropIndex(
                name: "IX_Contacts_WorkspaceId_NormalizedWorkEmail",
                schema: "contacts",
                table: "Contacts");

            migrationBuilder.DropColumn(
                name: "NormalizedPersonalEmail",
                schema: "contacts",
                table: "Contacts");

            migrationBuilder.DropColumn(
                name: "NormalizedWorkEmail",
                schema: "contacts",
                table: "Contacts");
        }
    }
}
