using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.PlatformOperations.Outbox.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompleteIntegrationEventJournalFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SchemaVersion",
                schema: "ops",
                table: "IntegrationEventJournal",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "SubjectVersion",
                schema: "ops",
                table: "IntegrationEventJournal",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SchemaVersion",
                schema: "ops",
                table: "IntegrationEventJournal");

            migrationBuilder.DropColumn(
                name: "SubjectVersion",
                schema: "ops",
                table: "IntegrationEventJournal");
        }
    }
}
