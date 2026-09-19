using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.PlatformOperations.AiExecution.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAiConfigurationReplaySnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ResultStateJson",
                schema: "platform_ai",
                table: "AiConfigurationCommands",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ResultStateJson",
                schema: "platform_ai",
                table: "AiConfigurationCommands");
        }
    }
}
