using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Crm.Customers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FinalizeLeadCustomerConversionProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CompletedAt",
                schema: "customers",
                table: "LeadConversionProvenance",
                type: "datetimeoffset",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset");

            migrationBuilder.AddColumn<string>(
                name: "CompletionAuditId",
                schema: "customers",
                table: "LeadConversionProvenance",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CompletionEventId",
                schema: "customers",
                table: "LeadConversionProvenance",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompletionAuditId",
                schema: "customers",
                table: "LeadConversionProvenance");

            migrationBuilder.DropColumn(
                name: "CompletionEventId",
                schema: "customers",
                table: "LeadConversionProvenance");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CompletedAt",
                schema: "customers",
                table: "LeadConversionProvenance",
                type: "datetimeoffset",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)),
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset",
                oldNullable: true);
        }
    }
}
