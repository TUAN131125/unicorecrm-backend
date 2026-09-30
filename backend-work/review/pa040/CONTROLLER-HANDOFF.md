# CRM-PA-040 — User-confirmed Task creation

Implementation is complete for controller review. No commit or push was performed. PA-050 was not started.

## Source delta

- Canonical baseline: `3d69e398056b92ebefe479affdf6e887c522b628`
- Current HEAD: `3d69e398056b92ebefe479affdf6e887c522b628`
- Delta: **15 files, 429 insertions, 3 deletions**.
- Artifact: `D:\Project_All\UnicoreCRM\backend\backend-work\review\pa040\CRM-PA-040-source-delta-3d69e39.patch`
- SHA-256: `1b808c6c7b358654138f31bcf31cdb28e2f93e8cba80e28e10c81f7a40716fa8`
- Includes only production/test source under `src/` and `scripts/`. Excludes plans, handoffs, review evidence, logs, generated outputs and unrelated untracked files.
- Forward apply against a complete canonical-baseline archive: PASS; every resulting changed file matches the temporary Git index blob.
- Reverse apply against current source: PASS. Actual forward/reverse round trip restores the complete baseline file-hash manifest: PASS.
- `git diff --check`, including all new source through a temporary index: PASS. Real Git index remains unstaged.

## Implementation

`POST /ai/proactive/items/{itemId}/confirm-task` accepts only final human title, description, assigneeId, dueAt and priority. It requires Idempotency-Key, does not require If-Match, rejects unknown fields, and has a bounded JSON body that accommodates escaped Unicode at the existing Tasks maximum lengths.

The application reauthorizes the current Workspace, capability, CUSTOMER/CUSTOMER_HEALTH_RISK OPEN item, item owner, and Customers-owned authorized Attention projection before invoking Tasks. The existing Customers reader remains responsible for current owner, customers.view, record scope and readable health. No Customers persistence is accessed from AI.

The narrow Tasks participant delegates to unchanged ordinary `CreateTask.Handler`. That handler continues to own all Tasks security, validation, serializable commit, normalized fingerprint, durable idempotency, identity/version, audit and TASK_CREATED outbox. Server provenance is `SourceRef(PROACTIVE_AI, itemId)` with null evidence and no invented Customer RecordRef. The response contains only itemId/taskId/taskVersion/outcome.

After Tasks commits or replays, a fresh persistence scope records deterministic, idempotent TASK_SUGGESTION_ACCEPTED evidence. A persistence failure returns a retryable 503 instructing same-key retry; the committed Task remains authoritative. Replay repairs absent evidence without duplicating Task/audit/outbox/idempotency. Cancellation/lost acknowledgement after commit also converges through ordinary Tasks replay.

## Verification results

| Gate | Result | Evidence |
|---|---|---|
| Full solution build | PASS — 0 warnings, 0 errors | `build.log` |
| Proactive scenario corpus | PASS — 133 cases, including 26 confirmation cases | `proactive.log` |
| Proactive real SQL corpus | PASS — 299 total cases, including 46 confirmation/Tasks SQL checks and 41 suggestion SQL checks | `proactive-sql.log` |
| Tasks regression | PASS — 281 Nurture/Tasks/API checks; ordinary Tasks security/replay also covered by AccessControl | `tasks-final.log`, `access.log` |
| SQL idempotency/failure | PASS — normalized replay, all changed final-intent fields conflict, post-commit audit failure repair, lost acknowledgement recovery, concurrent audit repair | `proactive-sql.log`, `ConfirmationSqlVerifier.cs` |
| Customers real ApiHost/security | PASS — 274 checks | `customers-final.log` |
| Customer Health | PASS — 29 cases | `health.log` |
| CommercialEvidence | PASS — 102 checks, architecture and pending model | `commercial.log` |
| AccessControl | PASS — 565 checks | `access.log` |
| AI provider verifier | PASS | `provider.log` |
| AI advisory/assistant | PASS | `advisory.log` |
| PlatformOperations pending model | PASS — AiExecutionDbContext, InboxDbContext, IntegrationEventJournalDbContext; all report no changes | EF CLI checks |
| Tasks pending model | PASS — TasksDbContext reports no changes | EF CLI check |
| Customers pending model | PASS — CustomersDbContext reports no changes | EF CLI check and `customers-final.log` |
| Clean migrations | PASS — isolated SQL corpus/ApiHost databases initialized using unchanged canonical migration sets | SQL/API logs |
| Accepted migration upgrade/no-op | PASS — platform_ai and Tasks already current, no migrations applied, before/after data hashes equal | `upgrade.log`, `check-upgrade.ps1` |
| Source architecture | PASS — no new project/DbContext/migration; unchanged provider, Tasks create and Customers authorities | source inspection and delta manifest |
| Git whitespace check | PASS, including untracked source in temporary index | `delta-metadata.json` |

Upgrade data SHA-256 before and after: `E7FCB4190D9140477ACC3D4D5AF06F2AFE73C3DA62CC6453C1DFA5A4C44340B9`.

One stale regression assertion was corrected: Nurture expected the Opportunity route to be absent (404), but the canonical baseline already maps that route in `LeadQualificationEndpoints.cs:21`. The test now expects rejection of its empty intent (422). Production Lead/Workflow code is unchanged. The initial run's single failure is retained in `tasks-regression.log`; the corrected complete run passes in `tasks-final.log`.

The existing Operations project only gains verifier friend-assembly access; no production project or dependency was added.

## Mandatory scenario coverage

| Scenarios | Executable evidence |
|---|---|
| 1–5: commit, normalized replay, changed title/assignee/date/priority conflicts | ConfirmationSqlVerifier, real ApiHost confirmation |
| 6–16: other owner, reassigned Customer, capabilities, scope, hidden/masked health, non-OPEN states, foreign Workspace | ConfirmationVerifier plus real Customers/AccessControl API fixtures |
| 17–23: Tasks capability, title/description limits, inactive assignee, field security, priority/date authority | ConfirmationSqlVerifier and real ApiHost; maximum escaped Unicode accepted |
| 24–26: server provenance and client override rejection | SQL persisted SourceRef checks and strict JSON tests |
| 27–30: zero provider, unchanged Attention on success/failure, no Customer mutation | SQL state comparison, API before/after Customer/Attention snapshots and execution/attempt counts |
| 31–36: exactly one Tasks audit/outbox/idempotency and safe acceptance, replay without duplicates | Real SQL counts and API persisted-row checks |
| 37–38: post-commit audit failure and lost acknowledgement converge | SQL failure interceptor, lost-ack participant wrapper, replay/repair and cardinality checks |
| 39: no prompt/provider output/credentials persisted by confirmation | Safe audit allow-list check; no provider/context payload accepted or passed; existing Tasks stores only final human intent/provenance |
| 40: no pending EF model | Explicit PlatformOperations/Tasks/Customers checks |

## Changed files

1. `scripts/ProactiveVerifier/ConfirmationSqlVerifier.cs`
2. `scripts/ProactiveVerifier/ConfirmationVerifier.cs`
3. `scripts/ProactiveVerifier/Program.cs`
4. `scripts/verify-customers-read-core.ps1`
5. `scripts/verify-lead-nurture-qualification-api.ps1`
6. `src/UnicoreCRM.AI/Gateway/AiEndpoints.cs`
7. `src/UnicoreCRM.AI/Gateway/GatewayModule.cs`
8. `src/UnicoreCRM.AI/Gateway/ProactiveTaskConfirmationApplication.cs`
9. `src/UnicoreCRM.AI/Gateway/ProactiveTaskConfirmationContracts.cs`
10. `src/UnicoreCRM.Operations/Tasks/Application/CreateProactiveFollowUp/Participant.cs`
11. `src/UnicoreCRM.Operations/Tasks/Contracts/ProactiveTaskCreationParticipant.cs`
12. `src/UnicoreCRM.Operations/Tasks/TasksModule.cs`
13. `src/UnicoreCRM.Operations/UnicoreCRM.Operations.csproj`
14. `src/UnicoreCRM.PlatformOperations/AiExecution/Contracts/ProactiveStore.cs`
15. `src/UnicoreCRM.PlatformOperations/AiExecution/Infrastructure/EfProactiveStore.cs`

## Explicit boundaries

- No provider call from confirm-task; no background provider call added.
- No automatic Task creation; only explicit authorized human confirmation creates a Task.
- No Attention lifecycle mutation; no Customer mutation.
- No frontend changes. Frontend remains clean at `50e41a336485c104e209412eae7b156a0d522571`.
- No new project, DbContext, table, migration or pending EF model change.
- No commit/push and no PA-050 implementation.

CRM-PA-040 CONFIRMED TASK READY FOR CONTROLLER REVIEW
