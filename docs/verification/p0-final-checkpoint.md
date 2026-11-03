# Connected CRM P0 — final review và checkpoint proposal

Ngày 21/10/2026, Asia/Saigon. **Verdict: NEEDS_FIX.** Không stage, commit hoặc push; không sửa implementation trong lượt review này. Functional journey hiện PASS, nhưng cumulative patch chưa đáp ứng tiêu chí public contract/client đồng bộ. Không chấp nhận baseline debt thay người dùng.

## 1. Finding chặn checkpoint

**F01 — Activity request contract không phản ánh security tightening (P1/high acceptance impact).**

`Tasks/Application/LogActivity/LogActivityValidation.cs:43–55` đã bỏ `SYSTEM` khỏi loại Activity được client ghi và yêu cầu một CRM reference hợp lệ, source marker phù hợp. `frontend/docs/api/openapi.json:65254` vẫn khai báo LogActivityRequest.type bằng ActivityType; enum tại dòng 58165 vẫn gồm SYSTEM, required chỉ có type/subject. `TaskApiRuntime.ts` vẫn dùng ActivityType cho LogActivityInput; generated client/OpenAPI không thay trong patch. Đây là narrowing có chủ ý về bảo mật, nhưng là thay đổi observable của public request contract chưa được đồng bộ. Không nên mở lại SYSTEM để làm test xanh.

Bằng chứng: cumulative diff bỏ nhánh SYSTEM so với HEAD gốc; run security-final có assertion `HTTP cannot forge system audit`: expected=422/actual=422. API contract gate vẫn PASS bởi vì spec/client cùng cũ. Gate này không kiểm tra toàn bộ runtime validator nên không phủ định finding.

Điều kiện đóng F01: tách vocabulary manual-write khỏi read/history nếu cần giữ SYSTEM trong responses; mô tả reference/source constraints, cập nhật OpenAPI theo authority và generate client; thêm contract test chứng minh payload contract hợp lệ tương thích với runtime. Chỉ sửa trong P0; không cần feature mới hay refactor kiến trúc. Chưa triển khai trong task review.

## 2. Cumulative diff và phạm vi

Đã kiểm tra tracked diff cùng các file source/test mới. Inventory trước review: Backend 303 file dirty/untracked, Frontend 77; phần runtime/config/test dự kiến P0 là **39 Backend + 22 Frontend**. `backend-file-review.tsv` và `frontend-file-review.tsv` phân loại từng file, hash và tracked status, bao gồm các file loại trừ. Không suy toàn bộ working tree là P0.

| Nhóm | Files / semantics | Evidence chính |
|---|---|---|
| P0 Activity Security | ActivityRecordAccess; owner fact providers/module registration; TaskActivitySecurity; log validation/handler; all-target read/write/replay authority | 560 assertions SQL/HTTP, OWN/WORKSPACE, denied users, TEAM denial, FLS, source/SYSTEM negatives, wait/commit race barriers |
| P0 ACT-PERF | Owner persistence fact SQL + ports; ActivityReadScope/ActivityReadSql; ListActivities/EfTasksPersistence; record history index | 107 measured requests, mỗi request đúng 2 Activity SQL statements; same/distinct 100/1,000/5,000, count/search/cursor/global/record |
| P0 Connected CRM E2E | Lead create submit closes correct lifecycle; conversion retires committed draft; Lead/Contact history capability/scope wiring; guidance | UI 12 bước + connected 108 + focused history contracts |
| Customer projection | Contact summary resolves canonical subject through Customers participant; Contact lookup removes Organization fallback; useContactOverview keeps stakeholder distinct | canonical Contact/Organization unique SQL, scope/capability negative cases, Overview contract |
| Customer Create Task | Customer panel reuses public TaskCreateModal/command; direct Customer recordRef/sourceRef, target key; TaskDetail locale bridge opt-out | modal session protocol, negative panel authority, real create/complete UI + SQL + restart |
| Tests/scripts/migration | 2 Playwright configs/specs, focused JSDOM tests, security harness, verifier compile wiring and retained DB option, one additive index migration | direct focused execution and real SQL runs; handover in-process |
| Docs/evidence | 4 older reports retained as historical claims, this final report and selected sanitized evidence | compare executed output; older NEEDS_FIX reports are not current verdict authority |
| Unrelated | backend-work state/handoffs/evidence/tasks; B2/B5 inventories/patches; lead auxiliary audit; pycache | excluded individually in TSV; no deletion |

Security review: CRM authority comes from current AccessControl and module facts; every referenced target is checked, sourceRef is not trusted evidence, search/count/page apply SQL authority first. SQL text comes from static module persistence code, values are parameters. TEAM remains denied rather than falling back. No new role bypass, fixture branch in production handlers, second Task form/adapter, duplicate completion-to-Activity write, HTTP endpoint addition or destructive data migration found in reviewed candidate.

Fixtures are explicit: harness inserts synthetic records/policies/accounts in a fresh GUID test DB and authenticates through real ApiHost. Scale data is synthetic SQL data. JSDOM tests use demo auth/in-process ports and are labeled accordingly; they do not prove real Backend behavior. UI-only spec observes HTTP responses but drives mutations through actual controls, without request client writes, route interception, injected records or localStorage authentication. Separate crm-real spec intentionally combines API setup with browser projection and is not called UI-only.

Secret scan: no private key/JWT/provider key matched across dirty text files. Credential/connection matches were reviewed: runtime-generated test password, interpolated integrated-auth DB templates, public demo fixture credentials, and prior unrelated artifact references. No runtime credentials or concrete connection-string dumps are in selected evidence. Host/bootstrap logs and generated data-protection key rings remain excluded. This is scoped inspection, not a repository-wide secret-history certification.

## 3. Quality gate classification

Baseline is **commit gốc**, not pre-CustomerTask dirty snapshot. Fresh Git archives of Backend `7b734f98a7173af2e24e094392826a20b6e11359` and Frontend `99db1773c03ce4a4b820db7e793da6eca872f862` were executed. Integrity check compared all 1,392/2,276 tracked archive files with Git blobs: no content differences after LF/CRLF normalization (archive honors local checkout line endings). Shared Node dependencies and same copied `.unicore-ai`/design-authority were used; this proves code baseline under the current authority environment, not historical authority at original commit time.

| Gate | Current | Baseline evidence / classification | Acceptance impact |
|---|---|---|---|
| Backend architecture | 33 PASS / 4 FAIL | Fresh baseline also 33/4; exact error-line sets added=[]/removed=[] for all four, TRX retained | Baseline debt; no new checker regression. Production blocker; functional waiver must be explicit |
| TaskCreateModal source contract | FAIL missing createTaskCommand in component, line 23 | Same failure on original FE commit; unchanged controller owns command | Baseline verification debt directly related to P0; direct modal+connected tests compensate functionally, gate is not PASS |
| Backend readiness | FAIL 2 cycles | Same ContactFormModal↔contactDraftRecovery and TaskCreateModal↔useTaskCreateController on original commit | Baseline architecture debt, no waiver assumed |
| Quality pipeline | FAIL 347 != 345 | Original run fails earlier on missing typescript declaration. Both original/current manifest have 347 entries, same checker hash expecting345. Patch adds only required typescript dependency | Baseline masked inventory defect; distinct from same-first-error claim |
| Repository inventory | FAIL drift | Original commit also FAIL; runtime structural counts remain 16 modules/83 routes/67 loadable/137 capabilities/13 flags/23 workflows | Baseline plus expected dirty/P0 file-count delta. Do not claim byte-identical drift or regenerate unrelated inventories |
| Task/commercial parity | FAIL Quote A at line214 | Same [] vs ['Quote A'] on original commit | Baseline fixture/parity debt; later assertions in this file NOT_RUN |
| Full verify | FAIL at gate10/327; 9 passed | Current failure is above proven baseline readiness cycles | Remaining 317 entries NOT_RUN through full pipeline; individually run gates are recorded separately |
| Presentation / guidance / API management / frontend-backend separation / strict core / strict regions / typecheck / lint | PASS | Current executed logs | No observed new gate failure |
| Mutation authority | First assertion OK but OPEN_HANDLE => FAIL; isolated retry PASS | Original baseline isolated PASS | Transient runner failure retained; latest isolated execution PASS, no claim first invocation passed |
| API contract/generated client | PASS 328 operations / 300 ready / 28 blocked | Spec/client consistency only | F01 remains a NEW manual-review FAIL despite gate PASS |
| Build / bundle | PASS, largest 486.99 KiB <500 | Fresh independent build | Full verify stops before build, therefore executed separately |
| Positive TEAM / production load / independent principal attestation | BLOCKED or NOT_RUN | No Team authority, no SLA/load campaign, no isolated reviewer attestation in this session | Outside functional P0 positive acceptance; blocks corresponding production claims |

The four architecture failures concern owner mapping, layer/workflow edges, persistence metadata, endpoint admission. SQL projection contract introduces cross-module schema coupling, so future schema/authorization changes must keep owner SQL and in-process checks equivalent; checker equality is not a blanket architectural approval. The new migration is index-only and does not create new ownership metadata errors. No additional P0 runtime gate regression found in executed tests. F01 is unresolved and prevents ACCEPTED.

## 4. Final verification and provenance

Windows; .NET SDK10.0.401; Node22.23.1; SQL Server localhost17.0.1000.7; integrated auth; Vite connected + Playwright Chromium; fresh GUID DB each run. No production data used; test DBs retained; existing browser outputs copied before each runner cleanup. No original source changed during review; manifest post-check sourceDrift=[] for both repositories. `source-manifest.json` records per-file SHA-256 and aggregate; compiled assembly hashes retained separately. Commit HEAD alone is insufficient for this dirty subject.

| Run (21/10, UTC+7) | Actual | Evidence/run ID |
|---|---|---|
| Security/perf 18:01:57–18:03:00 | **560 PASS / 0 FAIL**, including Customer Task, replay, race, FLS, scopes, projection; 107 benchmarks each 2 Activity SQL | 3d6f57f724044bd2a6ab2f18b376d77f |
| First UI run | FAIL Workspace fallback locator after 5-second URL wait; Login PASS, remaining prerequisite chain BLOCKED | 31b36d83d60740a6a9e4a37135166319; failed screenshot and steps retained |
| UI rerun 18:08:23–18:09:33 | **12/12 UI steps PASS**, 11/11 harness assertions; duplicate count1, valid member/ref, COMPLETED after restart and real reauth | 0f6425d7cf69463da843f37ec3f2a403 |
| Connected 18:10:25–18:11:20 | **108 PASS / 0 FAIL**, API+browser projection/history; both canonical subjects unique; history retained after affiliation ends, new write404; restart | 39ce8211a2774901b9ddb0ab96e8554f |
| FE focused JSDOM | Customer session, Customer panel, Contact Overview, Contact History, Lead History PASS | current-*.txt; exact commands/timestamps in current-frontend-results.json |
| Contact follow-up | 30 checks PASS on real SQL, DB retained with --keep-database | contact-follow-up.txt |
| Tasks handover | In-process checks PASS | tasks-handover.txt; --sql intentionally NOT_RUN because verifier unconditionally drops its generated DB |

560 is the actual assertion count of this invocation, not a repetition of the earlier report's 553. Synthetic benchmark does not represent p95, concurrency or production SLA. Distinct targets at100/1,000/5,000: WORKSPACE 79.46/217.31/367.17ms, OWN89.09/176.28/273.68ms; those six filtered requests each30 total pipeline commands and2 Activity queries.

UI Task ID `task_843992482a9c45e094a164fb4cdac8d0`; Customer `customer_d09046068679494e8f47bc12bdd0ba67`; unique title count1; Customer recordRef; no subject relationshipRef; completion persists. Browser relogin runs before ApiHost restart; post-restart verification uses real API login/get/list. Do not call this browser-after-restart E2E.

## 5. Git checkpoint proposal — pending F01 and user approval

Lists below contain exact repo-relative paths, **not executed staging commands**. No `git add .`; review explicit path lists and `git diff --cached --name-status` only after approval. Source list excludes all existing unrelated files. F01 remediation will require an updated candidate manifest and focused revalidation before the lists can be final.

### Backend runtime/config/test candidate (39)

- `.gitignore`
- `scripts/ContactFollowUpVerifier/ContactFollowUpVerifier.csproj`
- `scripts/ContactFollowUpVerifier/Program.cs`
- `scripts/TasksHandoverVerifier/Program.cs`
- `scripts/TasksHandoverVerifier/TasksHandoverVerifier.csproj`
- `scripts/verify-activity-security.ps1`
- `src/UnicoreCRM.Crm/Contacts/Application/Common/ContactsApplication.cs`
- `src/UnicoreCRM.Crm/Contacts/Application/GetRelationshipSummary/Handler.cs`
- `src/UnicoreCRM.Crm/Contacts/Application/ProvideContactRecordAccessFacts/ContactRecordAccessFactProvider.cs`
- `src/UnicoreCRM.Crm/Contacts/ContactsModule.cs`
- `src/UnicoreCRM.Crm/Contacts/Infrastructure/Persistence/EfContactsPersistence.cs`
- `src/UnicoreCRM.Crm/Customers/Application/Common/CustomersApplication.cs`
- `src/UnicoreCRM.Crm/Customers/Application/ProvideCustomerRecordAccessFacts/CustomerRecordAccessFactProvider.cs`
- `src/UnicoreCRM.Crm/Customers/Application/ResolveRelationshipTargets/Participant.cs`
- `src/UnicoreCRM.Crm/Customers/Contracts/CustomerRelationshipTargetParticipant.cs`
- `src/UnicoreCRM.Crm/Customers/CustomersModule.cs`
- `src/UnicoreCRM.Crm/Customers/Infrastructure/Persistence/EfCustomersPersistence.cs`
- `src/UnicoreCRM.Crm/Leads/Application/Common/LeadsPersistence.cs`
- `src/UnicoreCRM.Crm/Leads/Application/ProvideLeadRecordAccessFacts/LeadRecordAccessFactProvider.cs`
- `src/UnicoreCRM.Crm/Leads/Infrastructure/Persistence/EfLeadsPersistence.cs`
- `src/UnicoreCRM.Crm/Leads/LeadsModule.cs`
- `src/UnicoreCRM.Crm/Organizations/Application/Common/OrganizationsApplication.cs`
- `src/UnicoreCRM.Crm/Organizations/Application/ProvideOrganizationRecordAccessFacts/OrganizationRecordAccessFactProvider.cs`
- `src/UnicoreCRM.Crm/Organizations/Infrastructure/Persistence/EfOrganizationsPersistence.cs`
- `src/UnicoreCRM.Crm/Organizations/OrganizationsModule.cs`
- `src/UnicoreCRM.Operations/Tasks/Application/Common/ActivityRecordAccess.cs`
- `src/UnicoreCRM.Operations/Tasks/Application/Common/TaskRecordAccess.cs`
- `src/UnicoreCRM.Operations/Tasks/Application/Common/TasksPersistence.cs`
- `src/UnicoreCRM.Operations/Tasks/Application/ListActivities/Handler.cs`
- `src/UnicoreCRM.Operations/Tasks/Application/LogActivity/Handler.cs`
- `src/UnicoreCRM.Operations/Tasks/Application/LogActivity/LogActivityValidation.cs`
- `src/UnicoreCRM.Operations/Tasks/Contracts/ActivityReferenceAuthority.cs`
- `src/UnicoreCRM.Operations/Tasks/Infrastructure/Persistence/ActivityReadSql.cs`
- `src/UnicoreCRM.Operations/Tasks/Infrastructure/Persistence/EfTasksPersistence.cs`
- `src/UnicoreCRM.Operations/Tasks/Infrastructure/Persistence/Migrations/20261021075605_IndexActivityRecordHistory.Designer.cs`
- `src/UnicoreCRM.Operations/Tasks/Infrastructure/Persistence/Migrations/20261021075605_IndexActivityRecordHistory.cs`
- `src/UnicoreCRM.Operations/Tasks/Infrastructure/Persistence/Migrations/TasksDbContextModelSnapshot.cs`
- `src/UnicoreCRM.Operations/Tasks/Infrastructure/Persistence/TasksDbContext.cs`
- `src/UnicoreCRM.Operations/Tasks/TasksModule.cs`

### Frontend runtime/config/test candidate (22)

- `playwright.crm-real.config.ts`
- `playwright.crm-ui.config.ts`
- `scripts/quality/quality-pipeline.json`
- `src/guidance/content/crm/extendedScreens.ts`
- `src/guidance/content/crm/screens.ts`
- `src/modules/contacts/presentation/hooks/useContactOverview.ts`
- `src/modules/contacts/presentation/model/contactCustomerLookup.ts`
- `src/modules/contacts/presentation/model/contactHistory.ts`
- `src/modules/customers/presentation/detail/CustomerConnectedWorkspace.tsx`
- `src/modules/customers/presentation/detail/CustomerRelationshipPanel.tsx`
- `src/modules/leads/presentation/hooks/useLeadFormController.tsx`
- `src/modules/leads/presentation/hooks/useLeadHistory.ts`
- `src/modules/leads/presentation/model/leadHistory.ts`
- `src/modules/tasks/presentation/pages/TaskDetailPage.tsx`
- `src/workflows/lead-customer-conversion/presentation/LeadCustomerConversionPage.tsx`
- `tests/contract/contact-history.test.mts`
- `tests/contract/contact-overview.test.mts`
- `tests/contract/customer-relationship-panel.test.mts`
- `tests/contract/customer-task-create-session.test.mts`
- `tests/contract/lead-history.test.mts`
- `tests/e2e/crm-real.spec.ts`
- `tests/e2e/crm-ui-real.spec.ts`

### Documentation/evidence proposed in Backend

- `docs/verification/p0-final-checkpoint.md` (this report).
- `docs/verification/evidence/p0-final-checkpoint-20261021/` selected sanitized files, enumerated by `artifact-hashes.json`; source manifest, both TRX, baseline/current logs as `.txt`, run/results/http JSON, plans/indexes, screenshots, hashes and new-source bytes. No raw host logs or key ring. This is an explicit file allowlist, not permission to recursively stage future additions.

Not included: existing `backend-work/**` and `frontend/backend-work/**` (prior task/governance work, no current attestation); frontend `review-artifacts/**` (older B2/B5/auxiliary tasks); backend `scripts/ContactFollowUpVerifier/evidence/**`, `scripts/LeadTableCursorVerifier/__pycache__/**` (runtime/generated); existing historical `docs/verification/**` reports/evidence other than new final package (kept locally, superseded for verdict; avoid broad recursive staging); `docs/architecture/team-scope.md` (P1 decision context, no TEAM implementation); ignored artifacts, build output, test-results, node_modules, DBs, logs, keys and local configuration. Exact exclusions and SHA-256 at review start are in both file-review TSVs. None deleted.

Migration: only `20261021075605_IndexActivityRecordHistory`, Designer, model snapshot and TasksDbContext declaration. Up creates `tasks.Activities.IX_Activities_RecordHistory(WorkspaceId,RecordModuleKey,RecordId,OccurredAt,ActivityId)`; Down drops only that index. Fresh DB official migrate and benchmark index evidence PASS. Existing populated DB upgrade/down and index lock duration were NOT_RUN in this review; production rollout needs its own upgrade/load rehearsal. No data rewrite or TEAM schema.

Reproduce exact pair after approval: record immutable full Backend commit SHA and Frontend commit SHA in a separate release manifest together with parents, tree hashes, OpenAPI SHA-256, migration ID, .NET/Node lockfile hashes, authority snapshot digest and evidence artifact-hashes digest. Use the same checkpoint ID in both commit messages. Put Frontend SHA in Backend commit trailer after FE commit exists; publish the manifest after BE commit exists so it names both, avoiding circular commit hashes. A version label/branch alone is insufficient. `checkpoint-pair.pending.json` deliberately has null commit slots; current parent SHAs are not the checkpoint SHAs.

Rollback Backend: from a clean deployment checkout redeploy the pinned pre-checkpoint BE SHA; additive index can remain because old runtime tolerates it. If schema rollback is needed, use the Tasks DbContext migration tool targeting the immediate previous migration after approval/backup; inspect generated SQL so only this index is dropped. Do not rollback all module migrations or delete the test DB. After checkpoint commit, a reviewed revert commit is the Git rollback; no reset/clean of this dirty tree.

Rollback Frontend: redeploy build of the pinned previous FE SHA (and compatible BE pair), or create a reviewed revert commit. Keep existing dirty files and the before manifest. Pre-commit rollback of review artifacts means simply excluding this final documentation; this review changed no original runtime source. No automatic restore overwriting pre-existing work.

## 6. Remaining risks and conditional limits

F01 must close before checkpoint approval. Baseline debts above still require an explicit functional-only waiver; this report does not grant it. Task create reference lifecycle has validation-time checks, without Activity's commit-time lock proof. Activity commit guard locks target/relationship facts under SERIALIZABLE but does not establish atomic policy/membership revocation across every interleaving; collection reads are not a universal single policy/data snapshot. Offset pagination can shift during concurrent changes. TEAM has negative proof only. Coarse free-text FLS can suppress whole Activity rather than redact. SYSTEM/legacy unanchored/source rows remain stored but can disappear from read/search results; compatibility must be documented by F01.

UI Workspace fallback has one observed timing failure and a successful unchanged rerun; keep as harness robustness debt, not a newly proven product bug. Zero-area unsaved guard is still dismissed via visible UI only when it reports0 areas. Local Task locale fix uses existing i18n skip mechanism; global bridge behavior elsewhere is outside this patch. No unified timeline, automatic completion Activity, or messaging delivery is implied.

This is source/evidence review from the current chat, not a governed independent principal attestation or freeze. No fabricated reviewer identity; no governed sign-off issued. Production release requires full gates, explicit debt resolution, deployment/upgrade rehearsal, capacity testing and required independent authority controls.

## 7. P1 backlog — proposal only

1. Repair TaskCreateModal/controller gate ownership and type-only dependency cycles; register intended test inventory and make runner resource behavior deterministic.
2. Reconcile repository/pipeline inventory with exact clean source subject; resolve baseline Backend owner/layer/persistence/endpoint admission debt in their own scope.
3. Rehearse populated-DB index upgrade/rollback; production load, concurrency, memory and plan-stability campaign.
4. Define and test policy/membership revocation snapshot semantics and Customer Task commit-time lifecycle races.
5. Workspace-owned Team/TeamMembership and validated module TeamId assignment, then positive TEAM authorization; preserve fail-closed until admitted.
6. Stabilize Workspace redirect waits and zero-area unsaved guard; address broader translation bridge only under a dedicated task.

F01 is **P0 remediation**, not deferred to this P1 backlog. No P1 implemented.
