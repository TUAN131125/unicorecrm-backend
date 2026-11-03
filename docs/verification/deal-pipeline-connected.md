# Authoritative Deal pipeline verification

**Follow-up:** [Final acceptance review](deal-pipeline-final-acceptance.md) supersedes the PL-21 classification and adds restricted-role, historical replay and independent baseline evidence. It also records a pre-existing High Won-evidence verification gap; syntactic outcome guards must not be described as authoritative evidence validation.

Verified locally on 2026-10-24. Connected functionality passes the recorded API, SQL and private-browser journeys. **Final acceptance is incomplete**: PL-21 has no valid persisted fixture, the Won visual case has no eligible Deal/evidence, and repository-wide quality checks retain the failures described below. This report does not establish ACCEPTED or FROZEN status.

## A. Implementation and environment

| Repository | Branch | Starting commit |
|---|---|---|
| Backend | `master` | `13cd39eba01ebc9dc238f8b0ef98e174c1d4d3f6` |
| Frontend | `feat/deal-compact-kanban-card` | `814cc4685b6d8f8ead2fc945f4607a2d0168320d` |

Existing local changes were captured before editing and preserved. No commit, merge or push was performed. SQL identity is `DESKTOP-ICH225R / UnicoreCRM_Development`, verified as local and previously confirmed synthetic development data by the user. Connection configuration was not changed.

| Workspace | ID | Purpose |
|---|---|---|
| `unicore-demo-anh-duong` | `ws_f2332a12d56a421d8a722bd2f65c5efb` | Existing demo; original CRM records preserved |
| `unicore-demo-anh-duong-control` | `ws_811689cfc4b446eab4f29dc109e2abf2` | Authorized synthetic configuration/Deal acceptance fixtures |
| `ki-m-th-pipeline-provisioning-c019e52d` | `ws_772b487b87e84456b5020631fbaedd27` | New-workspace provisioning and negative-permission fixture |

Backend changes are concentrated in Deals: `Domain/DealPipeline.cs`, `Application/Common/DealPipelineRegistry.cs`, its persistence port, configuration contracts/endpoints, EF mapping and additive migration. Deal create/list/stage/profile/forecast/replay use the registry. ApiHost maps the configuration endpoints; the existing workspace-provisioning workflow invokes the narrow Deals provisioning contract after access initialization.

Frontend changes connect the generated configuration client through `DealPipelineHttpAdapter`, one shared workspace/session-scoped resource, Studio's controller/view, Kanban's pipeline-filtered windows, forms and Detail V5. Contracts, generated artifacts, guidance and repository inventory were updated. Generated files for other API groups change their specification hash; they do not introduce Auth, AI, Quote or Order behavior.

Exact file deltas relative to task entry, including pre-existing-file distinctions, are in [Backend scope](../../../task-runtime/deal-pipeline/backend/task-scope.json) and [Frontend scope](../../../task-runtime/deal-pipeline/frontend/task-scope.json). Entry status, patches and hashes are retained beside these files. Existing non-ASCII-named V5 screenshots were not fully covered by the entry hash collector and remain untouched; their absence from that collector must not be treated as proof they were newly created.

## B. Architecture and compatibility

Deals owns authoritative pipeline/stage configuration. Studio is a consumer. Connected mode never inserts browser defaults or repairs malformed server documents. Pipeline and stage IDs remain stable; existing stage code/category cannot change. Each pipeline retains active WON and LOST identities and at least one active OPEN stage. Referenced stages, including archived/historical use, cannot be removed or disabled. Default changes atomically update affected versions and apply only to new Deals.

The registry persists legacy forecast floors and positive-amount requirements. Custom stages use the existing score thresholds with a PIPELINE floor. Display order and default probability do not rewrite Opportunity Score or history. Create validation uses the form-selected pipeline's policy; editing uses the Deal's assigned pipeline, even when another pipeline contains the same stage code.

Configuration mutations use current authorization, idempotency, expected version, audit and outbox in an owner-local transaction. Workspace registry/command/aggregate locks prevent configuration/Deal races. Replay precedes mutable validation after authorization; legacy single and batch response enrichment adds the deterministic PipelineId without rewriting stored command evidence.

The external authority operation registry and project-extension file under `design-authority/canonical-design/authority/` were updated for the explicitly requested Deals-owned operations. Originals are retained in `task-runtime/deal-pipeline/authority/`. This is an explicit project extension, not a claim of canonical freeze. Standalone stage CRUD/deletion operations remain BLOCKED.

## C. Migration and data evidence

Applied migration: `20261023150422_AuthoritativeDealPipelines`. It adds owner-local tables, creates deterministic default definitions for workspaces already represented by Deals, backfills only `PipelineId`, then enforces composite workspace/pipeline/stage references. Unknown legacy stage/category data fails closed. `Down` refuses destructive rollback; use reviewed forward repair or verified backup restoration.

| Original demo data | Before | After |
|---|---:|---:|
| Leads | 150 | 150 |
| Contacts | 100 | 100 |
| Organizations | 35 | 35 |
| Customers | 70 | 70 |
| Deals | 180 | 180 |
| Tasks | 160 | 160 |
| Activities | 300 | 300 |
| Products | 30 | 30 |
| Support Cases | 25 | 25 |
| Total | 1,050 | 1,050 |

The 180 demo Deals and the other workspace's original Deal preserve exact hashes of **all original columns**, including stage, version and history. Original non-Deal CRM rows also retain their counts and hashes. Deal-to-stage/category orphans: **0**. Every initialized workspace retains exactly one active default. The control workspace currently contains 8 intentional synthetic Deals and 12 pipelines/56 stage definitions from real-command acceptance attempts; these are retained for review, not automatically deleted. No original demo reseed was invoked.

See [migration preservation](../../../task-runtime/deal-pipeline/backend/migration-preservation.json) and [SQL counts, stage groups, defaults, hashes and query samples](../../../task-runtime/deal-pipeline/backend/metrics-and-preservation.json).

## D. API contracts

| Operation | Endpoint | Authorization |
|---|---|---|
| `listCrmPipelines` | `GET /crm-configuration/pipelines` | `studio.read` OR `deals.read` |
| `getCrmPipeline` | `GET /crm-configuration/pipelines/{pipelineId}` | Same, current workspace only |
| `createCrmPipeline` | `POST /crm-configuration/pipelines` | `studio.configure` |
| `updateCrmPipeline` | `PATCH /crm-configuration/pipelines/{pipelineId}` | `studio.configure` + `If-Match` |

Requests require trusted workspace context and `X-Request-Id`; writes require `Idempotency-Key`. Save submits a complete aggregate with bilingual names, stable stage identity/code/category, order, probability, color and active/default flags. The server controls actor, time, policy and resource version. Mutation responses include outcome, authoritative pipeline and audit evidence. Detail/list documents include inactive definitions. Deal create may omit pipeline/stage to select the server default/first active OPEN stage. Deal list accepts `pipelineId` before materialization.

Recorded negative results include 403 ACCESS_DENIED, 404 RESOURCE_NOT_FOUND, 412 VERSION_CONFLICT, 422 invalid/foreign/inactive stage, 409 PIPELINE_STAGE_IN_USE and IDEMPOTENCY_KEY_REUSED. Lock contention is translated to structured retryable RESOURCE_BUSY. No new production bypass or aggregate endpoint outside owner authorization was introduced.

OpenAPI now describes 329 operations: 303 ready and 26 blocked. The four pipeline operations are backed by the real endpoints. Unrelated existing OpenAPI breaking-baseline findings were not accepted wholesale.

## E. Connected acceptance

| Case | Status | Actual evidence |
|---|---|---|
| PL-01 new workspace default | PASS | Public provisioning, SQL default present before first registry GET; replay creates no duplicate |
| PL-02 add OPEN stage | PASS | Studio save + actual API/SQL |
| PL-03 refresh Studio | PASS | Reload and authoritative readback |
| PL-04 logout/login | PASS | Private browser reauthentication, configuration retained |
| PL-05 custom Kanban column | PASS | Filtered backend queries and real column |
| PL-06 create at custom stage | PASS | API/SQL and UI create; score zero remains zero in API fixture |
| PL-07 move to custom stage | PASS | Real stage command, SQL relationship/forecast check |
| PL-08 rename stage | PASS | Persisted bilingual label through API and consumers |
| PL-09 reorder stages | PASS | Studio → Kanban → Detail order |
| PL-10 disable referenced stage | PASS | 409 PIPELINE_STAGE_IN_USE, data retained |
| PL-11 invalid code | PASS | Structured rejection |
| PL-12 foreign-pipeline stage | PASS | Rejected; no reassignment |
| PL-13 missing Studio write permission | PASS | CRM read 200; configuration POST/PATCH 403 |
| PL-14 cross-workspace access | PASS | API read/mutation denial and actual UI workspace switch |
| PL-15 concurrent config update | PASS | Parallel same-version commands: one accepted/replayed, one 412 |
| PL-16 ordinary stage → WON | PASS | Rejected; typed outcome/evidence command still required |
| PL-17 ordinary stage → LOST | PASS | Rejected; typed outcome command still required |
| PL-18 ApiHost restart | PASS | Private host stopped/restarted; config and Deal persisted |
| PL-19 original demo Deals | PASS | 181 original Deal row hashes unchanged |
| PL-20 multiple pipelines | PASS | Assigned PipelineId verified; omission selects current default |
| PL-21 historical inactive Deal | BLOCKED | No existing Deal references an inactive stage; supported commands refuse retiring referenced stages. Unit presentation coverage passes, persisted journey not claimed |
| PL-22 change default | PASS | Atomic default switch; existing Deal remains assigned to original pipeline |

Additional real concurrency checks cover competing stage changes on one Deal and stage deactivation racing with Deal creation. The latter committed deactivation and rejected creation with 422; inactive references remained zero. Three actual pre-migration demo create intents replay after migration and expose PipelineId; changing explicit forecast intent is rejected with 409.

Evidence: [API cases](../../../task-runtime/deal-pipeline/backend/api-acceptance.json), [concurrency](../../../task-runtime/deal-pipeline/backend/concurrency-acceptance.json), [configuration/stage race](../../../task-runtime/deal-pipeline/backend/configuration-stage-race.json), [provisioning](../../../task-runtime/deal-pipeline/backend/provisioning-acceptance.json), [permissions](../../../task-runtime/deal-pipeline/backend/permission-acceptance.json), [legacy replay](../../../task-runtime/deal-pipeline/backend/legacy-replay-acceptance.json).

## F. Frontend and visual acceptance

Two real-backend Playwright tests passed in **45.3 seconds**, using a private agent-owned browser, Vite 3042 and ApiHost 5098. No API responses were mocked. The main journey creates/renames/reorders a control pipeline, creates a Deal in its custom stage, moves it, refreshes, switches workspace and logs out/in. A regression specifically creates amount 0 in a custom PROPOSAL while the page displays another pipeline whose PROPOSAL requires positive amount; actual POST returns 201.

The read-only visual matrix covers one active OPEN stage, five OPEN stages/seven total, six OPEN/eight total with long labels, an unused inactive stage and an existing Lost Deal. All four sizes pass: 1440×900, 1366×768, 768×1024 and 390×844. Kanban horizontal scrolling stays inside its workspace; no document horizontal overflow. Won is BLOCKED because no eligible existing Won Deal/verified commercial evidence exists. Historical-inactive persisted visual acceptance is BLOCKED as PL-21 explains. No fake history or SQL outcome update was used.

See [browser results](../../../task-runtime/deal-pipeline/backend/browser/pipeline-connected-results.json), [visual matrix](../../../task-runtime/deal-pipeline/backend/browser/pipeline-visual-matrix.json), [structured problems](../../../task-runtime/deal-pipeline/backend/browser/pipeline-problems.json), [mobile Detail](../../../task-runtime/deal-pipeline/backend/browser/custom-pipeline-mobile.png), [large Kanban](../../../task-runtime/deal-pipeline/backend/browser/matrix-eight-total-advisory-kanban-desktop.png) and [Studio inactive stage](../../../task-runtime/deal-pipeline/backend/browser/matrix-inactive-unused-desktop.png).

## G. Security and operational limits

The existing supplied read-only account had no eligible CRM-read workspace for this test; its permissions were not altered. Official Development bootstrap created two synthetic identity fixtures (`pipeline.provisioning@example.com`, `pipeline.readonly@example.com`) and read-only membership only in the newly provisioned test workspace. The CRM dataset bootstrap was not invoked. No credential was written into source or evidence. No outbound provider, email, webhook, payment or commercial completion was executed.

Each of six registry reads used exactly **one registry SELECT**, with ten total SQL commands including authorization/audit. See recorded sample latency rather than inferring production performance. Registry reads/default ensures currently acquire an exclusive workspace lock, so concurrent writers may queue. Broad load/capacity testing: NOT_RUN. Concurrency evidence proves only the bounded scenarios above.

Independent read-only review found no remaining actionable Critical/High backend issue. Its final P2 form-policy finding was fixed and the actual browser regression passed; disposition PASS_WITH_ADVISORY. Review does not supply isolated reviewer-principal attestation or FROZEN status.

## H. Quality and reproduction

Backend build: PASS, 0 warnings/errors. Focused pipeline unit tests: PASS, 6/6. Full Backend tests: **FAIL, 52 passed/4 failed/56 total**. Exact task-entry reconstructed source had the same four failures (46 passed/4 failed/50 total). Their violation counts remain 549 owner-map, 16 durable metadata, 138 layer/workflow and 55 endpoint metadata messages. Compiler-generated state-machine index changes are the only raw layer-message difference. See [baseline comparison](../../../task-runtime/deal-pipeline/backend/architecture-baseline-comparison.json) and [final run](../../../task-runtime/deal-pipeline/backend/tests-final.log).

Frontend source-only strict compilation and production build: PASS. The full `npm run verify`: **FAIL** at three pre-existing TypeScript diagnostics in `deal-quote-transition-outcome.test.mts` and `studio-first-visit.spec.ts`; it stops there, so later full-suite gates are NOT_RUN by that invocation. Focused checks run separately; their exact final outcomes are in [focused results](../../../task-runtime/deal-pipeline/frontend/focused-results.json). Relevant passing checks include strict-core/type-regions, pipeline authority, Deal lifecycle, API contracts/boundaries, Studio single authority, presentation responsibility, mutation authority/conflicts, guidance and composition.

Focused results: **19 PASS / 5 FAIL**. Explicit baseline failures: backend-readiness has two existing uncatalogued Studio Connections documents; bilingual check has eight existing literals; connected platform configuration gate has an existing developer-webhook label mismatch; opportunity presentation gate still expects the pre-V5 unscoped navigation expression; quality-pipeline retains a stale second total assertion of 345 (task entry already had 347 gates; now 348). Source hashes and task-entry patch evidence are retained. Generator `--check` retains 44 unrelated Order/Quote/Customer/Activity breaking-baseline findings. No broad baseline acceptance or unrelated gate weakening was used. The intentional new pipeline gate increments the registry count from 347 to 348; its current registry-count and uniqueness assertions were updated, while the older unrelated stale assertion remains reported. Window-query assertions now also require pipeline scoping and cross-pipeline rejection. Repository inventory verification passes.

Reproduction on this same verified local database, with its existing local secrets/configuration and synthetic demo/control access:

```powershell
cd D:\Project_All\UnicoreCRM\backend
dotnet build UnicoreCRM.slnx --no-restore
# First run on an admitted development copy only, before migration:
pwsh -NoProfile -File scripts/verify-deal-pipelines.ps1 -Mode Baseline
# Existing baseline is protected against overwrite; skip Baseline on this checkout.
pwsh -NoProfile -File scripts/verify-deal-pipelines.ps1 -Mode Migrate
pwsh -NoProfile -File scripts/verify-deal-pipelines.ps1 -Mode Acceptance
pwsh -NoProfile -File scripts/verify-deal-pipelines.ps1 -Mode Provisioning
pwsh -NoProfile -File scripts/verify-deal-pipelines.ps1 -Mode SyntheticPermissions
pwsh -NoProfile -File scripts/verify-deal-pipelines.ps1 -Mode Legacy
pwsh -NoProfile -File scripts/verify-deal-pipelines.ps1 -Mode Browser
pwsh -NoProfile -File scripts/verify-deal-pipelines.ps1 -Mode Metrics
dotnet test tests/UnicoreCRM.ArchitectureTests/UnicoreCRM.ArchitectureTests.csproj --no-restore --filter FullyQualifiedName~DealPipelinePolicyTests

cd D:\Project_All\UnicoreCRM\frontend\unicorecrm-web
npm run quality:gate -- --gate quality.deal-pipeline-authority
npm run quality:gate -- --gate quality.deal-stage-window-query
npm run repo:check
npm run build
npm run verify
```

These are verification commands, not a CRM reseed. Acceptance writes are limited to the declared synthetic fixtures; browser runs intentionally create a new named control fixture per run. The API command tests use stable replay keys. Provisioning/SyntheticPermissions modes explicitly create test identities/membership through the official development mechanism; do not run them as ordinary startup. Baseline mode is read-only and refuses an existing baseline. Every harness mode fails closed outside the exact admitted local SQL identity. On another machine, review the harness environment allowlist and fixture identities rather than changing connection configuration or silently selecting another database. Private hosts stop in `finally`; browser evidence is stored outside source.

To inspect through the normal UI, run the Backend in Development and Frontend Connected mode using the existing application configuration, sign in with the established development account, select **UniCoreCRM Demo - Kiểm tra cô lập**, then open **Studio → Pipeline & trạng thái** and **Cơ hội**. Select the acceptance pipeline. Existing demo Deals remain under **UniCoreCRM Demo - Công ty Ánh Dương** and its migrated default pipeline.

## I. Review state

Both repositories remain on their starting branches and commits with uncommitted work for review. Migration is applied only to the verified development database. No destructive cleanup or rollback was run. Temporary runtime logs, baseline reconstruction and browser evidence are outside both source trees. Existing Dashboard, Customer, Deal V5 and demo-seeder changes remain in place. `git diff --check` passes for both repositories; Frontend security scan passes. The new read-only baseline mode was tested to reject overwriting the existing preservation baseline (`BASELINE_ALREADY_EXISTS`). A temporary diagnostic JSON was removed; intentional fixtures and review evidence were retained. Final status/diff checks and exact scope are retained in the task evidence directory.
