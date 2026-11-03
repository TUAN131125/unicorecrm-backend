# UniCoreCRM — SQL provenance and Opportunity authority acceptance

Date: 2026-10-29, Asia/Ho_Chi_Minh. Evidence directory: `task-runtime/sql-provenance-opportunity-20261029/`. This audit is read-only with respect to SQL; the historical 66 rows were not repaired or altered by this task. Evidence from the preceding task is explicitly identified as historical rather than a fresh run.

## A. Git and SQL baseline

Backend `master` at `13cd39eba01ebc9dc238f8b0ef98e174c1d4d3f6`; frontend `feat/deal-compact-kanban-card` at `814cc4685b6d8f8ead2fc945f4607a2d0168320d`. Both were extensively dirty at entry. NUL-delimited status, patches and SHA-256 inventories of tracked and nonignored untracked files, including five Unicode paths, are in the evidence directory. Control tree was separately inventoried. No reset, commit, pull or push.

Fresh entry SQL: `DESKTOP-ICH225R`, `UnicoreCRM_Development`, DB id 9, recovery database GUID `E0292B6C-8138-49B0-B7B4-264D783AFC4A`, 117,202 rows across 160 PK tables. All current user tables have PKs (`sql-nopk.json`); snapshot includes 21 migration-history tables and row hashes. Migration IDs are retained in `sql-before.json`. No schema or SQL write was requested by this audit. Hashes are evidence of row equality, not a field-level before image.

## B. SQL mutation investigation

The previous entry snapshot contained 110,872 rows, and previous final 117,202; 66 original row hashes changed, zero missing, zero migration changes (`../opportunity-intent-lifecycle-20261028/sql-preservation-final.json`). Original full row hashes exist, but no original individual columns or payload hashes. Accordingly the previous assertion that all 64 *transitioned* to PUBLISHED cannot be proven from that snapshot: all are PUBLISHED now, with one export attempt, but prior state is unknown. Cannot prove that only publication fields changed, or that original payload, aggregate, workspace and correlation were unchanged. Current per-event source owner, IDs, workspace, type, timestamps, attempts, correlation, aggregate where available, and SHA-256 of payload/envelope are in `outbox-current.json`; canonical journal type and subject are in `journal-correlation.json` (owner event type and canonical integration event type may differ). No separate CreatedAt exists on these source records: OccurredAt is reported as such, not mislabeled.

All 64 events have unique current journal records newly present since the previous entry, with matching current envelope SHA-256 and WorkspaceId. Journal sequence 707–775; recorded at 15:47:34.023–15:47:35.437 UTC; publication at 15:47:34.211–15:47:35.461 UTC, 28 October. Owners: Contacts 46, Workflows 7, Customers 6, Tasks 3, Organizations 2. Journal EventId has a unique index; current attempt count 1 does not establish absence of duplicate external deliveries or previous source mutations. A normal relay claims leases, admits journal and acknowledges the source (`IntegrationEventRelayWorker.cs`); this correlation supports that mechanism but does not establish which process executed it, that every original field complied with policy, or that the 64 comprised one worker batch. The worker default batch is 50 per source and may loop.

Cursor `outbound-webhooks-v1` is global to its consumer, currently sequence 809, updated 15:47:36.546 UTC, with journal range 1–809 and 809 records; the 64 journal sequences are <=809. The consumer reads sequence ascending after its cursor, serializable transaction, creates at most one delivery per subscription/event by unique index, advances cursor and commits together; initialization starts at tail. Current state is consistent with its mechanism, but old sequence and subscriber state are unavailable, so absence of skip/replay cannot be established. It is not a workspace-specific offset.

Quick Setup Workspace `ws_f2332a12d56a421d8a722bd2f65c5efb`: current revision 8, flow 3, IN_PROGRESS, current step locale-currency, skipped business-profile, last open 15:51:58.717 and dismissed 15:52:06.235 UTC; there is no UpdatedAt or RowVersion field in this model. Three new, previously absent audit identities are recorded in the prior final snapshot, each with a corresponding command record: revision 6 OPENED at 15:51:58.717, revision 7 STEP_SKIPPED business-profile at 15:52:05.012, revision 8 DISMISSED at 15:52:06.235; actor member `mem_62775573ab7b405db2863247bf7cbea7`, correlation IDs in `sql-studio_audit.json`. Their timings and current fields are consistent with the Workspace owner's serializable `CommitQuickSetupAsync`, which persists audit, command and outbox together. These records attribute the *commands* to the member, not a particular OS process or human. Open changes LastOpenedAt and revision; skip modifies completed/skipped step lists, current step, status and revision as applicable; dismiss changes AutoOpenDismissedAt and revision. Exact net field differences and original values cannot be reconstructed from the row hash alone. Endpoint commands and frontend runtime invoke these operations; audit attribution does not prove which UI, harness or host issued them.

Existing observation: process inventory at entry showed SQL Server PID 5768 and no visible dotnet/UniCoreCRM process; it is a point-in-time view, not a history. Database session view showed only the audit reader. No server audit configured, existing XE sessions were system/telemetry sessions (no proof of row-level write history), SQL Agent lists only syspolicy purge with no matching job history. No task-owned host log spans 22:47 local; worker/actor cannot be inferred from time. No other services were stopped. Normal ApiHost registers relay and webhook workers; the task-specific `--demo-seed-host` removes UniCoreCRM hosted services, subject to its registration filter; this does not prove what another host did.

## C. Provenance verdict

**SQL_MUTATION_PARTIALLY_ATTRIBUTED**: the three new Quick Setup commands have member/correlation evidence; 64 source events correspond to matching newly recorded journal events and a compatible current cursor. Relay process and cursor writer remain PROVENANCE_UNDETERMINED. No integrity violation is confirmed, but policy compliance and complete data integrity are not verified: original column values and authoritative process evidence are missing. Current source/journal envelope equality is not proof of original payload equality.

## D. Opportunity authority inventory

`CORE_AUTHORITY.md` ranks baseline/OpenAPI, approved decisions and owner documents over code; implementation evidence is not authority. Completed matching receipt is replayable only after authorization; no adopted pending response/status query, loser terminal/recovery contract, or universally valid no-effects signal was found. Closed ProblemDetails disallows an undeclared metadata extension. See [decision packet](../../../task-runtime/sql-provenance-opportunity-20261029/decision-packet.md) and previous packet linked there.

## E. Same-key pending

Historical actual: 63/64 and 95/96 HTTP 200, one 500 per measurement. Completion 24 ms after the latter response cannot prove the loser's last-read state. Recommend bounded server reconciliation and a typed pending outcome, with precise status/code/retry limits approved by API and Workflow owners. Existing generic 409 or 503 is not adopted as pending. PROPOSED_PENDING_APPROVAL; implementation BLOCKED_AUTHORITY_GAP.

## F. Different-key rejection

Historical competing intents: one 200 Completed and one 422 Started, one Contact and Deal total; retry loser 412. Started alone does not prove no participant effect because participant commits may precede anchor progress. Recommend durable terminal receipt only with verified no-effects, forward recovery for partial/unknown; transition owner, reason and replay behavior need ratification and persistence decision. Historical anchors remain untouched. BLOCKED_AUTHORITY_GAP.

## G. Validation/no-effects

One historical missing-estimate fixture returned 422 with no anchor, Contact, Task, Deal, business receipt or outbox and unchanged Lead. It cannot establish the universal C1 invariant. `Handler.cs` maps Task failure after Contact and Deal 422 after earlier participants to the same `LEAD_OPPORTUNITY_INPUT_INVALID`; the missing-value check can run with an existing anchor. Thus this current code cannot safely promise no-effects by error code. Propose a separate provable pre-admission code if owner approves ordering/concurrency proof, otherwise approved typed metadata; frontend must continue freezing unknown outcomes. BLOCKED_AUTHORITY_GAP.

## H. Approved implementation

No semantic implementation in this task: no adopted pending, terminal or no-effects decision was found and no authority was invented. Existing authorized Completed receipt re-read and prior frontend scope checks remain unchanged. No migrations, production schema, permissions, contract or governance edits.

## I. HTTP / SQL acceptance

No fresh live mutation acceptance was performed in this audit; the older fixtures are now original rows under the new snapshot. Historical real HTTP/SQL evidence remains under `../opportunity-intent-lifecycle-20261028/`: `race-task.json`, `pending-measure.json`, `different-key-final.json`, `missing-final.json`, `replay-security.json`, `task-500-recovery.json`. They cover observed contention, counts, changed payload, authorization/workspace, stale version and restart replay as described in the prior acceptance; they do not close current gaps or substitute for fresh acceptance after ratification.

## J. Connected Chromium

Not rerun in this audit. Historical connected browser observed field error with disabled editing after 422, hence edit/resubmit FAIL; browser pending/unknown, stale and workspace switch were not demonstrated. Previous connected Customer 360/Deal/Quote/PDF read checks were reported as passing, not a fresh comprehensive CRM acceptance.

## K. Security and quality regression

Fresh backend `dotnet test UnicoreCRM.slnx --no-restore`: 474 passed, four architecture failures, same counts as historical baseline. Test invocation builds the projects; no fault injection was attempted. Prior security checks showed authorization/cross-workspace denial, but no new security HTTP scenarios were run. The architecture failures remain baseline findings, not fixed or waived. Fresh frontend focused gates: 10/10 PASS, including qualification API boundary, partial-commit controller, connected runtime, strict core/type regions, contract and error presentation (`frontend-focused.json`); lint PASS; build and bundle budget PASS (largest 482.50 KiB / 500 KiB). Fresh canonical `npm run verify`: 48 PASS, gate 49 `quality.repository-cleanup` FAIL (14 exact-content duplicate files, expected 0); remaining 279 gates NOT_RUN (`frontend-verify.log`). No baseline or budget was changed.

## L. SQL preservation

No automatic outbox whitelist: compare every original PK and hash, and distinguish business/immutable, owner-command mutable Quick Setup, operational mutable outbox/cursor and append-only audit/journal. Operational lifecycle possibility does not establish provenance. Fresh final snapshot: 117,202 rows / 160 PK tables, zero original rows changed or missing, zero additions and zero migration changes (`sql-preservation-final.json`). This establishes row-hash preservation during this audit only; it does not resolve historical provenance. No original audit, cursor, outbox or setup row was reverted. Historical 66 are not silently reclassified as verified.

## M. Remaining blockers

Observed product defects: 500 under contention and disabled correction after tested 422. Authority gaps: pending wire outcome, rejected-anchor handling and trustworthy no-effects signal. Evidence gaps: prior row column images, relay process identity, original cursor and end-to-end delivery history. Fault after flush is NOT_RUN_POLICY_BLOCKED; existing tests cannot prove real SQL rollback at that boundary.

## N. Final verdict

SQL provenance: SQL_MUTATION_PARTIALLY_ATTRIBUTED. Opportunity functionality: NEEDS_FIX. Contract authority: AUTHORITY_GAP, none of the three proposals ratified. Full Quality: NOT PASS until the canonical pipeline and backend failures are resolved. No B6.5, Gate C or commercial operational acceptance claimed.

## O. Git status

Entry dirty state and hashes preserved; final delta is in `source-delta.json`. Task deliverables are this report and the new local decision/evidence packet. No commit, reset, push, merge or destructive cleanup.

## P. Next steps

1. Obtain historical row images and independently retained relay/SQL audit logs if available; compare all 66 fields and attribute the worker and cursor.
2. API/Workflow, participant, security and frontend owners decide the three choices in the decision packet; do not regard this proposal as approval.
3. Implement the adopted contract narrowly, then perform fresh isolated HTTP/SQL and connected Chromium acceptance and rerun quality gates, including the policy-blocked fault case only if separately permitted.
