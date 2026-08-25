using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Platform.IdentityAuth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IdentityEmailVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmailVerificationChallenges",
                schema: "iam",
                columns: table => new
                {
                    ChallengeId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    AccountId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CodeHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ResendAvailableAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    MaxAttempts = table.Column<int>(type: "int", nullable: false),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SupersededAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailVerificationChallenges", x => x.ChallengeId);
                    table.ForeignKey(
                        name: "FK_EmailVerificationChallenges_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "iam",
                        principalTable: "Accounts",
                        principalColumn: "AccountId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmailVerificationChallenges_AccountId_ConsumedAt_SupersededAt",
                schema: "iam",
                table: "EmailVerificationChallenges",
                columns: new[] { "AccountId", "ConsumedAt", "SupersededAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmailVerificationChallenges",
                schema: "iam");
        }
    }
}
