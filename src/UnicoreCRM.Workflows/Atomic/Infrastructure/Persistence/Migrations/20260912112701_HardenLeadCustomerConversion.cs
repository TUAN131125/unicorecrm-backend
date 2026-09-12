using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Workflows.Atomic.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HardenLeadCustomerConversion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE [workflow].[LeadCustomerConversionAnchors] SET [Stage] = 'ManualReview' WHERE [Stage] IN ('Created', 'Prepared')");
            migrationBuilder.Sql("UPDATE [workflow].[LeadCustomerConversionAnchors] SET [Stage] = 'CustomerResolved' WHERE [Stage] = 'StakeholderResolved'");

            migrationBuilder.DropColumn(
                name: "FrozenDoNotCall",
                schema: "workflow",
                table: "LeadCustomerConversionAnchors");

            migrationBuilder.DropColumn(
                name: "FrozenDoNotEmail",
                schema: "workflow",
                table: "LeadCustomerConversionAnchors");

            migrationBuilder.DropColumn(
                name: "NewContactJson",
                schema: "workflow",
                table: "LeadCustomerConversionAnchors");

            migrationBuilder.DropColumn(
                name: "RecoveryExecutorId",
                schema: "workflow",
                table: "LeadCustomerConversionAnchors");

            migrationBuilder.DropColumn(
                name: "StakeholderJson",
                schema: "workflow",
                table: "LeadCustomerConversionAnchors");

            migrationBuilder.DropColumn(
                name: "SubjectCreated",
                schema: "workflow",
                table: "LeadCustomerConversionAnchors");

            migrationBuilder.DropColumn(name: "SubjectMode", schema: "workflow", table: "LeadCustomerConversionAnchors");
            migrationBuilder.DropColumn(name: "StakeholderRelationshipId", schema: "workflow", table: "LeadCustomerConversionAnchors");
            migrationBuilder.DropColumn(name: "SelectedSubjectId", schema: "workflow", table: "LeadCustomerConversionAnchors");

            migrationBuilder.RenameColumn(
                name: "Fingerprint",
                schema: "workflow",
                table: "LeadCustomerConversionAnchors",
                newName: "RequestFingerprint");

            migrationBuilder.AlterColumn<long>(
                name: "SubjectVersion",
                schema: "workflow",
                table: "LeadCustomerConversionAnchors",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "SubjectId",
                schema: "workflow",
                table: "LeadCustomerConversionAnchors",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "FrozenLeadOwnerId",
                schema: "workflow",
                table: "LeadCustomerConversionAnchors",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BusinessIntentFingerprint",
                schema: "workflow",
                table: "LeadCustomerConversionAnchors",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(name: "OriginalPrincipalId", schema: "workflow",
                table: "LeadCustomerConversionAnchors", type: "nvarchar(128)", maxLength: 128, nullable: true);
            migrationBuilder.Sql("UPDATE [workflow].[LeadCustomerConversionAnchors] SET [OriginalPrincipalId] = [OriginalMemberId] WHERE [OriginalPrincipalId] IS NULL");
            migrationBuilder.AlterColumn<string>(name: "OriginalPrincipalId", schema: "workflow",
                table: "LeadCustomerConversionAnchors", type: "nvarchar(128)", maxLength: 128, nullable: false,
                oldClrType: typeof(string), oldType: "nvarchar(128)", oldMaxLength: 128, oldNullable: true);

            migrationBuilder.AddColumn<string>(name: "ExecutionAttemptId", schema: "workflow", table: "LeadCustomerConversionAnchors",
                type: "nvarchar(128)", maxLength: 128, nullable: true);
            migrationBuilder.AddColumn<string>(name: "ExecutionPrincipalId", schema: "workflow", table: "LeadCustomerConversionAnchors",
                type: "nvarchar(128)", maxLength: 128, nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExecutionLeaseAcquiredAt",
                schema: "workflow",
                table: "LeadCustomerConversionAnchors",
                type: "datetimeoffset(7)",
                precision: 7,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExecutionLeaseExpiresAt",
                schema: "workflow",
                table: "LeadCustomerConversionAnchors",
                type: "datetimeoffset(7)",
                precision: 7,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BusinessIntentFingerprint",
                schema: "workflow",
                table: "LeadCustomerConversionAnchors");

            migrationBuilder.DropColumn(
                name: "ExecutionLeaseAcquiredAt",
                schema: "workflow",
                table: "LeadCustomerConversionAnchors");

            migrationBuilder.DropColumn(
                name: "ExecutionLeaseExpiresAt",
                schema: "workflow",
                table: "LeadCustomerConversionAnchors");

            migrationBuilder.DropColumn(name: "ExecutionAttemptId", schema: "workflow", table: "LeadCustomerConversionAnchors");
            migrationBuilder.DropColumn(name: "ExecutionPrincipalId", schema: "workflow", table: "LeadCustomerConversionAnchors");
            migrationBuilder.DropColumn(name: "OriginalPrincipalId", schema: "workflow", table: "LeadCustomerConversionAnchors");

            migrationBuilder.RenameColumn(
                name: "RequestFingerprint",
                schema: "workflow",
                table: "LeadCustomerConversionAnchors",
                newName: "Fingerprint");

            migrationBuilder.AddColumn<string>(name: "SubjectMode", schema: "workflow", table: "LeadCustomerConversionAnchors", type: "nvarchar(128)", maxLength: 128, nullable: false, defaultValue: "EXISTING");
            migrationBuilder.AddColumn<string>(name: "StakeholderRelationshipId", schema: "workflow", table: "LeadCustomerConversionAnchors", type: "nvarchar(128)", maxLength: 128, nullable: true);
            migrationBuilder.AddColumn<string>(name: "SelectedSubjectId", schema: "workflow", table: "LeadCustomerConversionAnchors", type: "nvarchar(128)", maxLength: 128, nullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "SubjectVersion",
                schema: "workflow",
                table: "LeadCustomerConversionAnchors",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<string>(
                name: "SubjectId",
                schema: "workflow",
                table: "LeadCustomerConversionAnchors",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "FrozenLeadOwnerId",
                schema: "workflow",
                table: "LeadCustomerConversionAnchors",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128);

            migrationBuilder.AddColumn<bool>(
                name: "FrozenDoNotCall",
                schema: "workflow",
                table: "LeadCustomerConversionAnchors",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "FrozenDoNotEmail",
                schema: "workflow",
                table: "LeadCustomerConversionAnchors",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NewContactJson",
                schema: "workflow",
                table: "LeadCustomerConversionAnchors",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecoveryExecutorId",
                schema: "workflow",
                table: "LeadCustomerConversionAnchors",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StakeholderJson",
                schema: "workflow",
                table: "LeadCustomerConversionAnchors",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SubjectCreated",
                schema: "workflow",
                table: "LeadCustomerConversionAnchors",
                type: "bit",
                nullable: true);
        }
    }
}
