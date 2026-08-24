using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UnicoreCRM.Platform.Workspace.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Data-only correction for databases that already applied
    /// <c>20260824135117_InitialWorkspaceProvisioningRecovery</c>.
    ///
    /// That published migration backfilled every pre-existing anchor as
    /// <c>State = 'Completed', CompletedAt = ProvisionedAt</c>. It is immutable history and is left
    /// exactly as published, so this migration repairs the rows it fabricated instead.
    ///
    /// The backfill was wrong because the version that wrote those anchors committed the Workspace,
    /// the membership, the configuration seed and the anchor in one transaction and only then
    /// created the AccessControl assignment. Such an anchor proves nothing about whether the
    /// assignment exists, so declaring it complete fabricated a fact the Workspace owner cannot
    /// know. Workspace owns no AccessControl state and this migration must not read or write
    /// <c>access.*</c>; it only returns ambiguous rows to outstanding work. The durable resume path
    /// then decides completion through the approved AccessControl contract, converging on an
    /// existing assignment or creating a missing one exactly once.
    /// </summary>
    /// <inheritdoc />
    public partial class InitialWorkspaceProvisioningRecoveryCorrection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Only the legacy fabricated signature is repaired. A genuine completion writes its own
            // completion time in a later transaction, so CompletedAt differs from ProvisionedAt and
            // the row is left untouched. The comparison also excludes NULL CompletedAt, so anchors
            // that are already AccessPending and anchors created after this migration are unaffected.
            // Re-running the statement is a no-op because the repaired rows no longer match.
            migrationBuilder.Sql(
                """
                UPDATE [workspace].[InitialProvisioningRecords]
                SET [State] = 'AccessPending',
                    [CompletedAt] = NULL
                WHERE [State] = 'Completed'
                  AND [CompletedAt] = [ProvisionedAt];
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately empty. Reverting would have to re-fabricate a completion fact that the
            // Workspace owner cannot know, which is the defect this migration exists to repair.
            // Rolling back leaves the corrected anchors as outstanding work, which stays safe:
            // the durable resume path converges them without creating duplicate state.
        }
    }
}
