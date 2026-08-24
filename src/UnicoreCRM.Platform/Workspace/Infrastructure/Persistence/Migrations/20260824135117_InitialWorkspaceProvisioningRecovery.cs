using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Platform.Workspace.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialWorkspaceProvisioningRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CompletedAt",
                schema: "workspace",
                table: "InitialProvisioningRecords",
                type: "datetimeoffset",
                nullable: true);

            // Added nullable, backfilled, then tightened, so no column default survives and every
            // pre-existing anchor - which could only exist after a fully completed provisioning -
            // is recorded as completed rather than as outstanding work.
            migrationBuilder.AddColumn<string>(
                name: "State",
                schema: "workspace",
                table: "InitialProvisioningRecords",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE [workspace].[InitialProvisioningRecords] SET [State] = 'Completed', [CompletedAt] = [ProvisionedAt] WHERE [State] IS NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "State",
                schema: "workspace",
                table: "InitialProvisioningRecords",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(32)",
                oldMaxLength: 32,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_InitialProvisioningRecords_State_ProvisionedAt",
                schema: "workspace",
                table: "InitialProvisioningRecords",
                columns: new[] { "State", "ProvisionedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InitialProvisioningRecords_State_ProvisionedAt",
                schema: "workspace",
                table: "InitialProvisioningRecords");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                schema: "workspace",
                table: "InitialProvisioningRecords");

            migrationBuilder.DropColumn(
                name: "State",
                schema: "workspace",
                table: "InitialProvisioningRecords");
        }
    }
}
