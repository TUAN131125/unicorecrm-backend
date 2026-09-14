using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Integrations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BackfillOutboundWebhookRetryCycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE [integration].[OutboundWebhookDeliveries]
                SET [RetryCycleAttemptCount] = CASE
                    WHEN [AttemptCount] <= 0 THEN 0
                    WHEN [AttemptCount] >= 6 THEN 6
                    ELSE [AttemptCount]
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
