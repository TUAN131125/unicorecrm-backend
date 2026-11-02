using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Sales.Quotes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class QuoteIssuedDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Documents",
                schema: "quotes",
                columns: table => new
                {
                    DocumentId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    QuoteId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RootQuoteId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    QuoteRevision = table.Column<int>(type: "int", nullable: false),
                    DocumentVersion = table.Column<int>(type: "int", nullable: false),
                    SourceResourceVersion = table.Column<long>(type: "bigint", nullable: false),
                    ResultResourceVersion = table.Column<long>(type: "bigint", nullable: false),
                    BindingHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CommercialFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FieldManifestJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SellerConfigurationVersion = table.Column<long>(type: "bigint", nullable: false),
                    ApprovalPolicyVersion = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    PolicyVersion = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    TemplateVersion = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    IssuedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    IssuedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PdfSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    FileSize = table.Column<int>(type: "int", nullable: false),
                    PageCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Documents", x => new { x.WorkspaceId, x.DocumentId });
                    table.CheckConstraint("CK_QuoteDocument_Bounds", "[FileSize] BETWEEN 1 AND 10485760 AND [PageCount] BETWEEN 1 AND 100 AND [DocumentVersion] >= 1 AND [QuoteRevision] >= 1 AND [SourceResourceVersion] >= 1 AND [ResultResourceVersion] = [SourceResourceVersion] + 1");
                    table.ForeignKey(
                        name: "FK_Documents_Quotes_WorkspaceId_QuoteId",
                        columns: x => new { x.WorkspaceId, x.QuoteId },
                        principalSchema: "quotes",
                        principalTable: "Quotes",
                        principalColumns: new[] { "WorkspaceId", "QuoteId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DocumentContents",
                schema: "quotes",
                columns: table => new
                {
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    DocumentId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Bytes = table.Column<byte[]>(type: "varbinary(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentContents", x => new { x.WorkspaceId, x.DocumentId });
                    table.ForeignKey(
                        name: "FK_DocumentContents_Documents_WorkspaceId_DocumentId",
                        columns: x => new { x.WorkspaceId, x.DocumentId },
                        principalSchema: "quotes",
                        principalTable: "Documents",
                        principalColumns: new[] { "WorkspaceId", "DocumentId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_WorkspaceId_QuoteId_BindingHash",
                schema: "quotes",
                table: "Documents",
                columns: new[] { "WorkspaceId", "QuoteId", "BindingHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Documents_WorkspaceId_QuoteId_DocumentVersion",
                schema: "quotes",
                table: "Documents",
                columns: new[] { "WorkspaceId", "QuoteId", "DocumentVersion" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentContents",
                schema: "quotes");

            migrationBuilder.DropTable(
                name: "Documents",
                schema: "quotes");
        }
    }
}
