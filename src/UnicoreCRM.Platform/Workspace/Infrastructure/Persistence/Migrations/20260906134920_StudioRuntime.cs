using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Platform.Workspace.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StudioRuntime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StudioCommandRecords",
                schema: "workspace",
                columns: table => new
                {
                    ScopeKey = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    OperationId = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RequestFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ResponseJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudioCommandRecords", x => x.ScopeKey);
                });

            migrationBuilder.CreateTable(
                name: "StudioConfigurationAudits",
                schema: "workspace",
                columns: table => new
                {
                    AuditId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ActorMemberId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudioConfigurationAudits", x => x.AuditId);
                });

            migrationBuilder.CreateTable(
                name: "StudioConfigurations",
                schema: "workspace",
                columns: table => new
                {
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    PublicationStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PublishedRevision = table.Column<long>(type: "bigint", nullable: true),
                    BusinessInformationJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AddressesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LocaleRegionJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BlueprintJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FeaturesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedByMemberId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudioConfigurations", x => x.WorkspaceId);
                    table.ForeignKey(
                        name: "FK_StudioConfigurations_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalSchema: "workspace",
                        principalTable: "Workspaces",
                        principalColumn: "WorkspaceId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StudioOutboxEvents",
                schema: "workspace",
                columns: table => new
                {
                    EventId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    AggregateId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    AggregateVersion = table.Column<long>(type: "bigint", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudioOutboxEvents", x => x.EventId);
                });

            migrationBuilder.CreateTable(
                name: "StudioQuickSetups",
                schema: "workspace",
                columns: table => new
                {
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CurrentStepId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CompletedStepIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SkippedStepIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AutoOpenDismissedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastOpenedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    FlowVersion = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudioQuickSetups", x => x.WorkspaceId);
                    table.ForeignKey(
                        name: "FK_StudioQuickSetups_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalSchema: "workspace",
                        principalTable: "Workspaces",
                        principalColumn: "WorkspaceId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql(
                """
                INSERT INTO [workspace].[StudioConfigurations]
                    ([WorkspaceId], [Revision], [PublicationStatus], [PublishedRevision],
                     [BusinessInformationJson], [AddressesJson], [LocaleRegionJson], [BlueprintJson], [FeaturesJson],
                     [UpdatedAt], [UpdatedByMemberId])
                SELECT
                    w.[WorkspaceId], 1, N'DRAFT', NULL,
                    N'{"displayName":"' + STRING_ESCAPE(w.[Name], 'json') + N'","tradingName":"","legalName":"","registrationNumber":"","taxId":"","industry":"","representativeName":"","email":"","supportEmail":"","billingEmail":"","phone":"","website":"","logoReference":""}',
                    N'[]',
                    N'{"supportedLocales":["' + STRING_ESCAPE(COALESCE(b.[Locale], N'en'), 'json') + N'"],"defaultLocale":"' + STRING_ESCAPE(COALESCE(b.[Locale], N'en'), 'json') + N'","timezone":"' + STRING_ESCAPE(COALESCE(b.[TimeZone], N'UTC'), 'json') + N'","countryCode":"' + CASE WHEN b.[Locale] = N'vi' THEN N'VN' ELSE N'US' END + N'","dateFormat":"' + CASE WHEN b.[Locale] = N'vi' THEN N'DD/MM/YYYY' ELSE N'MM/DD/YYYY' END + N'","weekStartsOn":1,"currencies":{"baseCurrency":"' + STRING_ESCAPE(COALESCE(b.[BaseCurrency], N'USD'), 'json') + N'","enabledCurrencies":["' + STRING_ESCAPE(COALESCE(b.[BaseCurrency], N'USD'), 'json') + N'"],"displayMode":"FULL","exchangeRateMode":"MANUAL","exchangeRateProviderConnectionId":null},"exchangeRates":[]}',
                    N'{"businessModel":"B2B","workflow":{"dealUsageMode":"OPTIONAL","quoteUsageMode":"QUOTE","quoteRequirement":null,"paymentMode":null,"orderMode":null,"defaultCustomerType":"COMPANY","defaultRevenueModel":"SUBSCRIPTION","salesMotion":"ENTERPRISE_SALES","pipelineTemplate":"B2B_SALES"}}',
                    N'{"leads":' + CASE WHEN EXISTS (SELECT 1 FROM OPENJSON(COALESCE(b.[EnabledModuleKeysJson], N'[]')) WHERE [value] = N'leads') THEN N'true' ELSE N'false' END
                    + N',"customers":true,"contacts":' + CASE WHEN EXISTS (SELECT 1 FROM OPENJSON(COALESCE(b.[EnabledModuleKeysJson], N'[]')) WHERE [value] = N'contacts') THEN N'true' ELSE N'false' END
                    + N',"deals":' + CASE WHEN EXISTS (SELECT 1 FROM OPENJSON(COALESCE(b.[EnabledModuleKeysJson], N'[]')) WHERE [value] = N'deals') THEN N'true' ELSE N'false' END
                    + N',"quotes":' + CASE WHEN EXISTS (SELECT 1 FROM OPENJSON(COALESCE(b.[EnabledModuleKeysJson], N'[]')) WHERE [value] = N'quotes') THEN N'true' ELSE N'false' END
                    + N',"orders":' + CASE WHEN EXISTS (SELECT 1 FROM OPENJSON(COALESCE(b.[EnabledModuleKeysJson], N'[]')) WHERE [value] = N'orders') THEN N'true' ELSE N'false' END
                    + N',"support":' + CASE WHEN EXISTS (SELECT 1 FROM OPENJSON(COALESCE(b.[EnabledModuleKeysJson], N'[]')) WHERE [value] = N'support') THEN N'true' ELSE N'false' END
                    + N',"organizations":' + CASE WHEN EXISTS (SELECT 1 FROM OPENJSON(COALESCE(b.[EnabledModuleKeysJson], N'[]')) WHERE [value] = N'organizations') THEN N'true' ELSE N'false' END
                    + N',"tasks":' + CASE WHEN EXISTS (SELECT 1 FROM OPENJSON(COALESCE(b.[EnabledModuleKeysJson], N'[]')) WHERE [value] = N'tasks') THEN N'true' ELSE N'false' END
                    + N',"payments":' + CASE WHEN EXISTS (SELECT 1 FROM OPENJSON(COALESCE(b.[EnabledModuleKeysJson], N'[]')) WHERE [value] = N'payments') THEN N'true' ELSE N'false' END
                    + N',"invoices":' + CASE WHEN EXISTS (SELECT 1 FROM OPENJSON(COALESCE(b.[EnabledModuleKeysJson], N'[]')) WHERE [value] = N'invoices') THEN N'true' ELSE N'false' END
                    + N',"shipping":' + CASE WHEN EXISTS (SELECT 1 FROM OPENJSON(COALESCE(b.[EnabledModuleKeysJson], N'[]')) WHERE [value] = N'shipping') THEN N'true' ELSE N'false' END
                    + N',"returns":' + CASE WHEN EXISTS (SELECT 1 FROM OPENJSON(COALESCE(b.[EnabledModuleKeysJson], N'[]')) WHERE [value] = N'returns') THEN N'true' ELSE N'false' END + N'}',
                    SYSUTCDATETIME(), NULL
                FROM [workspace].[Workspaces] w
                LEFT JOIN [workspace].[BootstrapProjections] b ON b.[WorkspaceId] = w.[WorkspaceId]
                WHERE NOT EXISTS (
                    SELECT 1 FROM [workspace].[StudioConfigurations] s WHERE s.[WorkspaceId] = w.[WorkspaceId]);

                INSERT INTO [workspace].[StudioQuickSetups]
                    ([WorkspaceId], [Status], [CurrentStepId], [CompletedStepIdsJson], [SkippedStepIdsJson],
                     [AutoOpenDismissedAt], [LastOpenedAt], [CompletedAt], [Revision], [FlowVersion])
                SELECT w.[WorkspaceId], N'NOT_STARTED', N'business-profile', N'[]', N'[]', NULL, NULL, NULL, 1, 3
                FROM [workspace].[Workspaces] w
                WHERE NOT EXISTS (
                    SELECT 1 FROM [workspace].[StudioQuickSetups] q WHERE q.[WorkspaceId] = w.[WorkspaceId]);
                """);

            migrationBuilder.CreateIndex(
                name: "IX_StudioCommandRecords_WorkspaceId_OperationId_OccurredAt",
                schema: "workspace",
                table: "StudioCommandRecords",
                columns: new[] { "WorkspaceId", "OperationId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StudioConfigurationAudits_WorkspaceId_OccurredAt",
                schema: "workspace",
                table: "StudioConfigurationAudits",
                columns: new[] { "WorkspaceId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StudioOutboxEvents_PublishedAt_OccurredAt",
                schema: "workspace",
                table: "StudioOutboxEvents",
                columns: new[] { "PublishedAt", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StudioCommandRecords",
                schema: "workspace");

            migrationBuilder.DropTable(
                name: "StudioConfigurationAudits",
                schema: "workspace");

            migrationBuilder.DropTable(
                name: "StudioConfigurations",
                schema: "workspace");

            migrationBuilder.DropTable(
                name: "StudioOutboxEvents",
                schema: "workspace");

            migrationBuilder.DropTable(
                name: "StudioQuickSetups",
                schema: "workspace");
        }
    }
}
