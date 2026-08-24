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

            // Added nullable, backfilled, then tightened, so no column default survives.
            //
            // Pre-existing anchors are backfilled as AccessPending, never as Completed. The
            // previous version committed the Workspace, the membership, the configuration seed and
            // the anchor in one transaction and only then created the AccessControl assignment, so
            // an anchor written by that version proves nothing about whether the assignment exists.
            // Workspace owns no AccessControl state and this migration must not read or write it,
            // so completion cannot be decided here. AccessPending is the fail-safe value: the
            // convergent durable resume path is the authority that decides completion, and it is a
            // no-op when the assignment already exists.
            migrationBuilder.AddColumn<string>(
                name: "State",
                schema: "workspace",
                table: "InitialProvisioningRecords",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE [workspace].[InitialProvisioningRecords] SET [State] = 'AccessPending' WHERE [State] IS NULL;");

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
