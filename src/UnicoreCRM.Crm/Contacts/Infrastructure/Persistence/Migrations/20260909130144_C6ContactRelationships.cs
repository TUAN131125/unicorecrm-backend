using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Crm.Contacts.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class C6ContactRelationships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_Contacts_WorkspaceId_ContactId",
                schema: "contacts",
                table: "Contacts",
                columns: new[] { "WorkspaceId", "ContactId" });

            migrationBuilder.CreateTable(
                name: "CustomerRelationships",
                schema: "contacts",
                columns: table => new
                {
                    RelationshipId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ContactId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CustomerId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Role = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    EffectiveFrom = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    EffectiveTo = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                    EndedReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerRelationships", x => x.RelationshipId);
                    table.CheckConstraint("CK_CustomerRelationships_DateRange", "[EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]");
                    table.CheckConstraint("CK_CustomerRelationships_EndState", "([EffectiveTo] IS NULL AND [EndedReason] IS NULL) OR ([EffectiveTo] IS NOT NULL AND LEN(LTRIM(RTRIM([EndedReason]))) > 0)");
                    table.CheckConstraint("CK_CustomerRelationships_Role", "[Role] COLLATE Latin1_General_100_BIN2 IN (N'primary_contact',N'billing',N'decision_maker',N'end_user',N'technical',N'support',N'other')");
                    table.ForeignKey(
                        name: "FK_CustomerRelationships_Contacts_WorkspaceId_ContactId",
                        columns: x => new { x.WorkspaceId, x.ContactId },
                        principalSchema: "contacts",
                        principalTable: "Contacts",
                        principalColumns: new[] { "WorkspaceId", "ContactId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationRelationships",
                schema: "contacts",
                columns: table => new
                {
                    RelationshipId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    WorkspaceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ContactId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    OrganizationId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Role = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    IsPrimaryAffiliation = table.Column<bool>(type: "bit", nullable: false),
                    EffectiveFrom = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    EffectiveTo = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                    EndedReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    LegacyEvidenceJson = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationRelationships", x => x.RelationshipId);
                    table.CheckConstraint("CK_OrganizationRelationships_DateRange", "[EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]");
                    table.CheckConstraint("CK_OrganizationRelationships_EndState", "([EffectiveTo] IS NULL AND [EndedReason] IS NULL) OR ([EffectiveTo] IS NOT NULL AND LEN(LTRIM(RTRIM([EndedReason]))) > 0)");
                    table.CheckConstraint("CK_OrganizationRelationships_Role", "[Role] COLLATE Latin1_General_100_BIN2 IN (N'employee',N'executive',N'decision_maker',N'buyer',N'finance',N'technical',N'advisor',N'partner',N'other')");
                    table.ForeignKey(
                        name: "FK_OrganizationRelationships_Contacts_WorkspaceId_ContactId",
                        columns: x => new { x.WorkspaceId, x.ContactId },
                        principalSchema: "contacts",
                        principalTable: "Contacts",
                        principalColumns: new[] { "WorkspaceId", "ContactId" },
                        onDelete: ReferentialAction.Restrict);
                });

            // Legacy embedded Organization relationships are migration evidence, not a second
            // write store. Valid rows are copied once; invalid/ambiguous rows are retained in a
            // deterministic quarantine table for explicit reconciliation instead of being lost or
            // coerced into C6 semantics. The legacy primary-representative flag is preserved only
            // inside LegacyEvidenceJson because it is not Contact primary-affiliation authority.
            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'[contacts].[RelationshipMigrationIssues]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [contacts].[RelationshipMigrationIssues](
                        [IssueId] nvarchar(64) NOT NULL CONSTRAINT [PK_RelationshipMigrationIssues] PRIMARY KEY,
                        [WorkspaceId] nvarchar(128) NOT NULL,
                        [ContactId] nvarchar(128) NOT NULL,
                        [LegacyRelationshipId] nvarchar(128) NULL,
                        [IssueCode] nvarchar(80) NOT NULL,
                        [LegacyEvidenceJson] nvarchar(max) NOT NULL,
                        [DetectedAt] datetimeoffset(7) NOT NULL);
                END;

                CREATE TABLE #C6OrganizationTargets(
                    [WorkspaceId] nvarchar(128) NOT NULL,
                    [OrganizationId] nvarchar(128) NOT NULL,
                    CONSTRAINT [PK_C6OrganizationTargets] PRIMARY KEY ([WorkspaceId], [OrganizationId]));
                IF OBJECT_ID(N'[organizations].[Organizations]', N'U') IS NOT NULL
                    EXEC sp_executesql N'INSERT INTO #C6OrganizationTargets ([WorkspaceId],[OrganizationId]) SELECT [WorkspaceId],[OrganizationId] FROM [organizations].[Organizations]';

                INSERT INTO [contacts].[RelationshipMigrationIssues]
                    ([IssueId],[WorkspaceId],[ContactId],[LegacyRelationshipId],[IssueCode],[LegacyEvidenceJson],[DetectedAt])
                SELECT CONVERT(varchar(64), HASHBYTES('SHA2_256', CONCAT(c.[WorkspaceId],N'|',c.[ContactId],N'||INVALID_PROFILE_JSON|',c.[Profile])), 2),
                       c.[WorkspaceId], c.[ContactId], NULL, N'INVALID_PROFILE_JSON', c.[Profile], SYSUTCDATETIME()
                FROM [contacts].[Contacts] c
                WHERE ISJSON(c.[Profile]) <> 1
                  AND NOT EXISTS (SELECT 1 FROM [contacts].[RelationshipMigrationIssues] i
                                  WHERE i.[IssueId] = CONVERT(varchar(64), HASHBYTES('SHA2_256', CONCAT(c.[WorkspaceId],N'|',c.[ContactId],N'||INVALID_PROFILE_JSON|',c.[Profile])), 2));

                WITH [Legacy] AS (
                    SELECT c.[WorkspaceId], c.[ContactId], j.[value] AS [Evidence],
                           JSON_VALUE(j.[value], '$.id') AS [RelationshipId],
                           JSON_VALUE(j.[value], '$.organizationAccountId') AS [OrganizationId],
                           JSON_VALUE(j.[value], '$.role') AS [Role],
                           TRY_CONVERT(datetimeoffset(7), JSON_VALUE(j.[value], '$.effectiveFrom'), 127) AS [EffectiveFrom],
                           TRY_CONVERT(datetimeoffset(7), JSON_VALUE(j.[value], '$.effectiveTo'), 127) AS [EffectiveTo],
                           JSON_VALUE(j.[value], '$.endedReason') AS [EndedReason],
                           TRY_CONVERT(datetimeoffset(7), JSON_VALUE(j.[value], '$.createdAt'), 127) AS [CreatedAt],
                           COALESCE(JSON_VALUE(j.[value], '$.createdBy'), N'migration:c6') AS [CreatedBy],
                           TRY_CONVERT(datetimeoffset(7), JSON_VALUE(j.[value], '$.updatedAt'), 127) AS [UpdatedAt],
                           COALESCE(JSON_VALUE(j.[value], '$.updatedBy'), JSON_VALUE(j.[value], '$.createdBy'), N'migration:c6') AS [UpdatedBy]
                    FROM [contacts].[Contacts] c
                    CROSS APPLY OPENJSON(CASE WHEN ISJSON(c.[Profile]) = 1 THEN c.[Profile] ELSE N'{}' END, '$.organizationRelationships') j
                ), [Classified] AS (
                    SELECT l.*,
                      CASE
                        WHEN l.[RelationshipId] IS NULL OR LEN(l.[RelationshipId]) NOT BETWEEN 1 AND 128 OR l.[RelationshipId] LIKE '%[^A-Za-z0-9._:-]%' THEN N'INVALID_RELATIONSHIP_ID'
                        WHEN l.[OrganizationId] IS NULL OR LEN(l.[OrganizationId]) NOT BETWEEN 1 AND 128 OR l.[OrganizationId] LIKE '%[^A-Za-z0-9._:-]%' THEN N'INVALID_ORGANIZATION_ID'
                        WHEN NOT EXISTS (SELECT 1 FROM #C6OrganizationTargets t WHERE t.[WorkspaceId]=l.[WorkspaceId] AND t.[OrganizationId]=l.[OrganizationId]) THEN N'INVALID_OR_UNVERIFIED_ORGANIZATION_TARGET'
                        WHEN l.[Role] IS NULL OR l.[Role] COLLATE Latin1_General_100_BIN2 NOT IN (N'employee',N'executive',N'decision_maker',N'buyer',N'finance',N'technical',N'advisor',N'partner',N'other') THEN N'INVALID_ROLE'
                        WHEN l.[EffectiveFrom] IS NULL OR l.[CreatedAt] IS NULL THEN N'INVALID_TIMESTAMP'
                        WHEN JSON_VALUE(l.[Evidence], '$.effectiveTo') IS NOT NULL AND l.[EffectiveTo] IS NULL THEN N'INVALID_TIMESTAMP'
                        WHEN JSON_VALUE(l.[Evidence], '$.updatedAt') IS NOT NULL AND l.[UpdatedAt] IS NULL THEN N'INVALID_TIMESTAMP'
                        WHEN LEN(LTRIM(RTRIM(l.[CreatedBy]))) NOT BETWEEN 1 AND 128 OR LEN(LTRIM(RTRIM(l.[UpdatedBy]))) NOT BETWEEN 1 AND 128 THEN N'INVALID_ACTOR'
                        WHEN LEN(l.[EndedReason]) > 1000 THEN N'INVALID_END_STATE'
                        WHEN l.[EffectiveTo] IS NOT NULL AND (l.[EffectiveTo] < l.[EffectiveFrom] OR NULLIF(LTRIM(RTRIM(l.[EndedReason])), N'') IS NULL) THEN N'INVALID_END_STATE'
                        WHEN l.[EffectiveTo] IS NULL AND l.[EndedReason] IS NOT NULL THEN N'INVALID_END_STATE'
                        WHEN COUNT(*) OVER (PARTITION BY l.[RelationshipId]) > 1 THEN N'DUPLICATE_RELATIONSHIP_ID'
                        WHEN l.[EffectiveTo] IS NULL AND COUNT(*) OVER (PARTITION BY l.[WorkspaceId], l.[ContactId], l.[OrganizationId], CASE WHEN l.[EffectiveTo] IS NULL THEN 1 ELSE 0 END) > 1 THEN N'DUPLICATE_ACTIVE_PAIR'
                      END AS [IssueCode]
                    FROM [Legacy] l
                )
                INSERT INTO [contacts].[RelationshipMigrationIssues]
                    ([IssueId],[WorkspaceId],[ContactId],[LegacyRelationshipId],[IssueCode],[LegacyEvidenceJson],[DetectedAt])
                SELECT CONVERT(varchar(64), HASHBYTES('SHA2_256', CONCAT(c.[WorkspaceId],N'|',c.[ContactId],N'|',COALESCE(c.[RelationshipId],N''),N'|',c.[IssueCode],N'|',c.[Evidence])), 2),
                       c.[WorkspaceId], c.[ContactId], c.[RelationshipId], c.[IssueCode], c.[Evidence], SYSUTCDATETIME()
                FROM [Classified] c
                WHERE c.[IssueCode] IS NOT NULL
                  AND NOT EXISTS (SELECT 1 FROM [contacts].[RelationshipMigrationIssues] i
                                  WHERE i.[IssueId] = CONVERT(varchar(64), HASHBYTES('SHA2_256', CONCAT(c.[WorkspaceId],N'|',c.[ContactId],N'|',COALESCE(c.[RelationshipId],N''),N'|',c.[IssueCode],N'|',c.[Evidence])), 2));

                WITH [Legacy] AS (
                    SELECT c.[WorkspaceId], c.[ContactId], j.[value] AS [Evidence],
                           JSON_VALUE(j.[value], '$.id') AS [RelationshipId], JSON_VALUE(j.[value], '$.organizationAccountId') AS [OrganizationId],
                           JSON_VALUE(j.[value], '$.role') AS [Role], TRY_CONVERT(datetimeoffset(7), JSON_VALUE(j.[value], '$.effectiveFrom'), 127) AS [EffectiveFrom],
                           TRY_CONVERT(datetimeoffset(7), JSON_VALUE(j.[value], '$.effectiveTo'), 127) AS [EffectiveTo], JSON_VALUE(j.[value], '$.endedReason') AS [EndedReason],
                           TRY_CONVERT(datetimeoffset(7), JSON_VALUE(j.[value], '$.createdAt'), 127) AS [CreatedAt],
                           COALESCE(JSON_VALUE(j.[value], '$.createdBy'), N'migration:c6') AS [CreatedBy],
                           COALESCE(TRY_CONVERT(datetimeoffset(7), JSON_VALUE(j.[value], '$.updatedAt'), 127), TRY_CONVERT(datetimeoffset(7), JSON_VALUE(j.[value], '$.createdAt'), 127)) AS [UpdatedAt],
                           COALESCE(JSON_VALUE(j.[value], '$.updatedBy'), JSON_VALUE(j.[value], '$.createdBy'), N'migration:c6') AS [UpdatedBy]
                    FROM [contacts].[Contacts] c CROSS APPLY OPENJSON(CASE WHEN ISJSON(c.[Profile]) = 1 THEN c.[Profile] ELSE N'{}' END, '$.organizationRelationships') j
                )
                INSERT INTO [contacts].[OrganizationRelationships]
                    ([RelationshipId],[WorkspaceId],[ContactId],[OrganizationId],[Role],[IsPrimaryAffiliation],[EffectiveFrom],[EffectiveTo],[EndedReason],[CreatedAt],[CreatedBy],[UpdatedAt],[UpdatedBy],[LegacyEvidenceJson])
                SELECT l.[RelationshipId],l.[WorkspaceId],l.[ContactId],l.[OrganizationId],l.[Role],0,l.[EffectiveFrom],l.[EffectiveTo],l.[EndedReason],l.[CreatedAt],l.[CreatedBy],l.[UpdatedAt],l.[UpdatedBy],l.[Evidence]
                FROM [Legacy] l
                WHERE NOT EXISTS (SELECT 1 FROM [contacts].[RelationshipMigrationIssues] i WHERE i.[WorkspaceId]=l.[WorkspaceId] AND i.[ContactId]=l.[ContactId] AND (i.[LegacyRelationshipId]=l.[RelationshipId] OR i.[LegacyRelationshipId] IS NULL))
                  AND NOT EXISTS (SELECT 1 FROM [contacts].[OrganizationRelationships] r WHERE r.[RelationshipId]=l.[RelationshipId]);
                DROP TABLE #C6OrganizationTargets;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerRelationships_WorkspaceId_ContactId_CustomerId",
                schema: "contacts",
                table: "CustomerRelationships",
                columns: new[] { "WorkspaceId", "ContactId", "CustomerId" },
                unique: true,
                filter: "[EffectiveTo] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerRelationships_WorkspaceId_ContactId_EffectiveTo_EffectiveFrom_RelationshipId",
                schema: "contacts",
                table: "CustomerRelationships",
                columns: new[] { "WorkspaceId", "ContactId", "EffectiveTo", "EffectiveFrom", "RelationshipId" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerRelationships_WorkspaceId_CustomerId",
                schema: "contacts",
                table: "CustomerRelationships",
                columns: new[] { "WorkspaceId", "CustomerId" },
                unique: true,
                filter: "[EffectiveTo] IS NULL AND [Role] = N'primary_contact'");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerRelationships_WorkspaceId_CustomerId_EffectiveTo",
                schema: "contacts",
                table: "CustomerRelationships",
                columns: new[] { "WorkspaceId", "CustomerId", "EffectiveTo" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationRelationships_WorkspaceId_ContactId",
                schema: "contacts",
                table: "OrganizationRelationships",
                columns: new[] { "WorkspaceId", "ContactId" },
                unique: true,
                filter: "[EffectiveTo] IS NULL AND [IsPrimaryAffiliation] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationRelationships_WorkspaceId_ContactId_EffectiveTo_EffectiveFrom_RelationshipId",
                schema: "contacts",
                table: "OrganizationRelationships",
                columns: new[] { "WorkspaceId", "ContactId", "EffectiveTo", "EffectiveFrom", "RelationshipId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationRelationships_WorkspaceId_ContactId_OrganizationId",
                schema: "contacts",
                table: "OrganizationRelationships",
                columns: new[] { "WorkspaceId", "ContactId", "OrganizationId" },
                unique: true,
                filter: "[EffectiveTo] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationRelationships_WorkspaceId_OrganizationId_EffectiveTo",
                schema: "contacts",
                table: "OrganizationRelationships",
                columns: new[] { "WorkspaceId", "OrganizationId", "EffectiveTo" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerRelationships",
                schema: "contacts");

            migrationBuilder.DropTable(
                name: "OrganizationRelationships",
                schema: "contacts");

            migrationBuilder.Sql("DROP TABLE IF EXISTS [contacts].[RelationshipMigrationIssues];");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Contacts_WorkspaceId_ContactId",
                schema: "contacts",
                table: "Contacts");
        }
    }
}
