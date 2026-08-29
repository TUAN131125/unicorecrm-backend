using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Crm.Customers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CustomersRequiredEnumConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_Customers_Health",
                schema: "customers",
                table: "Customers",
                sql: "(([Health] COLLATE Latin1_General_100_BIN2 = N'GOOD' AND DATALENGTH([Health]) = DATALENGTH(N'GOOD')) OR ([Health] COLLATE Latin1_General_100_BIN2 = N'WATCH' AND DATALENGTH([Health]) = DATALENGTH(N'WATCH')) OR ([Health] COLLATE Latin1_General_100_BIN2 = N'RISK' AND DATALENGTH([Health]) = DATALENGTH(N'RISK')))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Customers_RelationshipType",
                schema: "customers",
                table: "Customers",
                sql: "(([RelationshipType] COLLATE Latin1_General_100_BIN2 = N'CONTACT' AND DATALENGTH([RelationshipType]) = DATALENGTH(N'CONTACT')) OR ([RelationshipType] COLLATE Latin1_General_100_BIN2 = N'ORGANIZATION_ACCOUNT' AND DATALENGTH([RelationshipType]) = DATALENGTH(N'ORGANIZATION_ACCOUNT')))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Customers_Status",
                schema: "customers",
                table: "Customers",
                sql: "(([Status] COLLATE Latin1_General_100_BIN2 = N'NEW' AND DATALENGTH([Status]) = DATALENGTH(N'NEW')) OR ([Status] COLLATE Latin1_General_100_BIN2 = N'ACTIVE' AND DATALENGTH([Status]) = DATALENGTH(N'ACTIVE')) OR ([Status] COLLATE Latin1_General_100_BIN2 = N'AT_RISK' AND DATALENGTH([Status]) = DATALENGTH(N'AT_RISK')) OR ([Status] COLLATE Latin1_General_100_BIN2 = N'INACTIVE' AND DATALENGTH([Status]) = DATALENGTH(N'INACTIVE')) OR ([Status] COLLATE Latin1_General_100_BIN2 = N'CHURNED' AND DATALENGTH([Status]) = DATALENGTH(N'CHURNED')) OR ([Status] COLLATE Latin1_General_100_BIN2 = N'DO_NOT_CONTACT' AND DATALENGTH([Status]) = DATALENGTH(N'DO_NOT_CONTACT')) OR ([Status] COLLATE Latin1_General_100_BIN2 = N'ARCHIVED' AND DATALENGTH([Status]) = DATALENGTH(N'ARCHIVED')))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Customers_Type",
                schema: "customers",
                table: "Customers",
                sql: "(([Type] COLLATE Latin1_General_100_BIN2 = N'B2C' AND DATALENGTH([Type]) = DATALENGTH(N'B2C')) OR ([Type] COLLATE Latin1_General_100_BIN2 = N'B2B' AND DATALENGTH([Type]) = DATALENGTH(N'B2B')))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Customers_Health",
                schema: "customers",
                table: "Customers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Customers_RelationshipType",
                schema: "customers",
                table: "Customers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Customers_Status",
                schema: "customers",
                table: "Customers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Customers_Type",
                schema: "customers",
                table: "Customers");
        }
    }
}
