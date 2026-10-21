using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Communications.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConversationClaimAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "MessageId",
                schema: "communications",
                table: "AuditRecords",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<string>(
                name: "ActorMemberId",
                schema: "communications",
                table: "AuditRecords",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CorrelationId",
                schema: "communications",
                table: "AuditRecords",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NewAssigneeMemberId",
                schema: "communications",
                table: "AuditRecords",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Operation",
                schema: "communications",
                table: "AuditRecords",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "MESSAGE_APPENDED");

            migrationBuilder.AddColumn<string>(
                name: "PreviousAssigneeMemberId",
                schema: "communications",
                table: "AuditRecords",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActorMemberId",
                schema: "communications",
                table: "AuditRecords");

            migrationBuilder.DropColumn(
                name: "CorrelationId",
                schema: "communications",
                table: "AuditRecords");

            migrationBuilder.DropColumn(
                name: "NewAssigneeMemberId",
                schema: "communications",
                table: "AuditRecords");

            migrationBuilder.DropColumn(
                name: "Operation",
                schema: "communications",
                table: "AuditRecords");

            migrationBuilder.DropColumn(
                name: "PreviousAssigneeMemberId",
                schema: "communications",
                table: "AuditRecords");

            migrationBuilder.AlterColumn<Guid>(
                name: "MessageId",
                schema: "communications",
                table: "AuditRecords",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);
        }
    }
}
