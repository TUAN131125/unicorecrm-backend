> HISTORICAL / SUPERSEDED: results below precede scope cleanup. Current evidence and verdict: [O1 cleanup report](lead-ownership-assignment-foundation-cleanup.md).

# Lead ownership assignment foundation — O1 evidence

Verdict: **LEAD_OWNERSHIP_O1_NEEDS_FIX**. O1 implementation and focused acceptance pass locally. The initial blockers have been repaired. All 150 frontend unit/contract/integration/route-smoke gates now have local results: 144 PASS and 6 FAIL. The remaining failures are detailed below; the aggregate suite is not green. This is not an authority blocker; the approved `leads.queue.read` policy is implemented. No commit or push was made. No O2–O7 runtime was enabled.

## A. Source baseline

| Repository | Directory | Branch | Starting / ending HEAD |
| --- | --- | --- | --- |
| Frontend | `D:/Project_All/UnicoreCRM/frontend/unicorecrm-web` | main | `1c0f02510510bccb01078c0cb8d6410a274154f6` |
| Backend | `D:/Project_All/UnicoreCRM/backend` | master | `161a580417f05471c6f5c7a3faf1c95114f212f1` |

Both tracked working trees were initially clean. The four supplied frontend Ownership documents were initially untracked; they were preserved and registered, with the approved O1 policy and evidence added. Existing backend untracked proactive-AI plans and review artifacts were preserved. All implementation changes remain in the local working trees. No stash, reset, clean, destructive source rollback, branch switch or remote action occurred.

## B. Document authority

Canonical business specification: `docs/business/lead-ownership-and-distribution-v1.md`. Decision: `docs/backend-readiness/lead-ownership-distribution-decision.md` and its JSON companion. Acceptance tracker: `docs/quality/lead-ownership-distribution-acceptance.md`. All three Markdown documents are registered in `docs/document-status.json` with the actual release `unicorecrm-web@0.24.0-contract.0`; JSON is a companion, not an extra Markdown registry key. Index links were added to `docs/README.md`.

The user's explicit clarification authorizes `leads.queue.read` independently of `leads.read` and reserves Claim for O2. The appended policy and machine-readable decision reflect that clarification. The wider V1 documents remain TARGET/TRACKER, not claims that future mutations are implemented. `DECISION_REQUIRED-HO-SLA-001` remains unresolved and untouched.

Wire authority is frontend `docs/api/openapi.json`. The generated clients and manifests were regenerated through `npm run api:generate`; generated source was not hand-edited. `api:check` passed. `anyOf` expresses nullable EntityId because the existing generator supports it. No claim/assign/handover readiness label was promoted.

## C. Current source inventory

| Concern | Before | O1 result |
| --- | --- | --- |
| Backend profile / scope projection | Required owner string | Nullable `LeadProfile.OwnerId` and `Lead.ScopeOwnerId` |
| Interactive create | Could select another active member | Binds authenticated member; different owner rejected |
| Delegated webhook | Delegated member also became business owner | Business owner null; delegation remains audit provenance |
| List query | ownerId/workState/search/cursor | Adds validated assignmentState; shared SQL predicate for page/count |
| Record scope | OWN only owner match; default WORKSPACE | Descriptor opts Leads into additional unassigned read authority |
| Transport | Required nonnull owner | Required nullable owner; explicit JSON null survives global omit-null policy |
| Frontend | Required owner | Null maps to absent application owner; malformed/missing wire owner fails |
| Future commands | Connected runtime rejects them | Remain rejected; no routes or buttons added |

Inventory generation also incorporates pre-existing baseline drift (routes 79→80, workflows 22→23, etc.). Those modules/routes were already present at starting HEAD; O1 itself adds one capability and one quality gate. Final inventory records 130 capabilities and 336 gates. Logs were moved outside the frontend before regeneration so they do not enter the inventory.

## D. Domain decision

Unassigned is exactly a null authoritative owner; assigned is a real workspace-member reference. No lifecycle state, Queue aggregate, sentinel owner, default technical owner or owner backfill is introduced. Existing state transitions, qualification requirements and Task semantics remain in force. ScopeOwnerId synchronizes with the profile through existing domain writes. Nullable handling does not relax progressive qualification requirements.

## E. Creation policy

Interactive CRM POST omits owner normally and resolves to the authenticated member. An explicitly different owner, including queue/unassigned/system/sales_queue, is rejected (403), never silently accepted as assignment. Explicit null follows the actor-bound interactive policy. Delegated admission always resolves null and verifies the delegated subject against its server-issued proof; the closed webhook payload rejects caller owner/Workspace authority (400). Integration actor, delegated subject, source reference and delivery identity remain auditable. Replay preserves original committed evidence and writes no second Lead.

Connected import and future external adapters were not invented: connected import remains unavailable. The existing demo webhook and demo seed no longer create a fake owner sentinel. The existing memory Queue helper has no connected consumer; this patch does not build Queue by loading all Leads into the browser.

## F. Query contract

`GET /leads?assignmentState=ASSIGNED|UNASSIGNED` filters on the persisted scope owner. Omission adds no assignment filter, but authorization still excludes unreadable null-owned records. `UNASSIGNED` plus any ownerId is rejected with 422; unknown assignment vocabulary is also 422. ASSIGNED plus a valid ownerId intersects both predicates.

Workspace/archive predicates, record scope, assignment, work state and search are applied before ordering/paging. Count and list share the predicate. Existing updatedAt/LeadId cursor ordering is unchanged. Hidden phone is excluded from search as well as projection. Null owner is an explicit known business state, not a substitute for a hidden owner field.

## G. Access control

| Record | Scope | Ordinary read | Queue read | Result |
| --- | --- | --- | --- | --- |
| Null owner | WORKSPACE | yes | yes | readable |
| Null owner | OWN | yes | yes | readable; Queue authority does not grant mutations |
| Null owner | WORKSPACE / OWN | yes | no | no records/counts/search disclosure; detail 404 |
| Null owner | TEAM / CUSTOM | yes | yes | fail closed |
| Any | any | no | any | ordinary read authority still required |
| Assigned | OWN | yes | any | existing owner-match rule |
| Foreign Workspace | any | any | any | trusted Workspace boundary still applies |

The opt-in resource descriptor is generic; Leads alone declares the unassigned authority. Other resources retain their behavior. Effective-record-access and actual record checks use the same decision. OWN Queue reads carry `ReadOnlyScope`, so requested mutation commands and canUpdate/delete/export/approve are not accidentally restored. Audit records distinguish Queue read from owner match. WORKSPACE plus existing mutation capabilities retains ordinary mutation scope, but profile replacement cannot change ownership.

The capability catalog admits queue.read for explicit custom-role grants; it never automatically extends custom roles. The existing protected owner exact-set upgrade chain recognizes the pre-Queue set, without adding the new capability to historical predecessor snapshots. Frontend owner/manager templates gain Queue read according to the current role matrix; salesperson templates do not gain it automatically. No Claim capability implementation was added.

Required owner field hidden/masked continues to fail the operation closed, rather than falsely representing a hidden assigned record as unassigned. Cross-workspace list/detail/mutation negative checks passed. Tasks and Deals creation/read regressions passed through the same backend harness.

## H. Database and concurrency

Migration `20260929030219_NullableLeadOwner` only changes `leads.Leads.ScopeOwnerId` to nullable. Designer and snapshot match. No table, unrelated column, index, business row rewrite or migration-history rewrite is introduced. Down explicitly rejects databases containing null-owned Leads (SQL 51000); it never manufactures an empty owner.

LocalDB evidence: create a separate database at migration `20260912161132_AddLeadCustomerConversionReservation`, copy one genuinely assigned row from the isolated harness database, apply the new migration, compare all columns with SQL EXCEPT. One row before/after, no changed values, nullable metadata=1. Add one null-owned fixture and attempt Down: rejected with 51000 and data retained. EF reports no pending model changes. Fresh full application migrations and persistence/projection tests also pass.

During repeat webhook testing, concurrent identical deliveries twice returned 500/200. Stack evidence located SQL deadlock 1205 at LeadCreateExecution.SaveChanges after both serializable transactions read a missing idempotency key. The persistence lookup now acquires parameterized `UPDLOCK, HOLDLOCK` on the idempotency key range before creation, following existing repo locking patterns. Duplicate execution waits for committed replay; no broad retries or swallowed exceptions were added. Subsequent complete harness runs passed duplicate 200/200 with one Lead, replay/conflict, Inbox and audit checks.

## I. Frontend

Transport null maps to application `ownerId?: string`; it is not converted to an empty or sentinel owner. Invalid/missing wire owners fail the projection. Create omits owner so backend identity remains authoritative. Replace carries the current member or explicit null. Unassigned edits retain no selected member and display “Chưa phân công”; the form no longer silently chooses the first member. Owner selection remains disabled on Edit.

List/detail/Work Panel safely display unassigned ownership. Follow-up Task creation and qualification paths guard missing required owner instead of inventing one. Small nullable compile fixes cover pilot evidence and demo audit consumers; no Task workflow, Handover orchestration, SLA, Work Panel layout or detail-surface redesign was added. Existing demo-only behavior remains clearly separate from connected admission.

## J. Future commands

Claim, Assign, Handover and bulk Assign remain unavailable in connected runtime. New tests call rejected Claim/Assign/Handover methods and assert zero HTTP requests. Availability checks cover single and bulk assignment. Existing generated future methods are not backend implementation evidence. No Claim endpoint, Queue page, routing system, permission shortcut or local assignment coordinator was added.

## K. Files changed

Paths below are relative to their repository. Pre-existing unrelated untracked backend plans/reviews are excluded. No generated client was hand-edited.

### Backend

- `backend-work/review/lead-ownership-assignment-foundation-tests.json`
- `backend-work/review/lead-ownership-assignment-foundation.md`
- `scripts/verify-inbound-lead-webhook.ps1`
- `src/UnicoreCRM.Crm/Leads/Application/AdvanceLeadWorkState/AdvanceLeadWorkStateValidation.cs`
- `src/UnicoreCRM.Crm/Leads/Application/Common/LeadValidation.cs`
- `src/UnicoreCRM.Crm/Leads/Application/Common/LeadsPersistence.cs`
- `src/UnicoreCRM.Crm/Leads/Application/CreateLead/LeadCreateAdmission.cs`
- `src/UnicoreCRM.Crm/Leads/Application/CreateLead/LeadCreateExecution.cs`
- `src/UnicoreCRM.Crm/Leads/Application/ListLeads/Handler.cs`
- `src/UnicoreCRM.Crm/Leads/Application/ProvideLeadRecordAccessFacts/LeadRecordAccessFactProvider.cs`
- `src/UnicoreCRM.Crm/Leads/Application/ReplaceLeadProfile/Handler.cs`
- `src/UnicoreCRM.Crm/Leads/Contracts/LeadContracts.cs`
- `src/UnicoreCRM.Crm/Leads/Contracts/LeadsEndpoints.cs`
- `src/UnicoreCRM.Crm/Leads/Domain/Lead.cs`
- `src/UnicoreCRM.Crm/Leads/Domain/LeadProfile.cs`
- `src/UnicoreCRM.Crm/Leads/Infrastructure/Persistence/EfLeadsPersistence.cs`
- `src/UnicoreCRM.Crm/Leads/Infrastructure/Persistence/Migrations/20260929030219_NullableLeadOwner.Designer.cs`
- `src/UnicoreCRM.Crm/Leads/Infrastructure/Persistence/Migrations/20260929030219_NullableLeadOwner.cs`
- `src/UnicoreCRM.Crm/Leads/Infrastructure/Persistence/Migrations/LeadsDbContextModelSnapshot.cs`
- `src/UnicoreCRM.Integrations/Webhooks/Inbound/Application/InboundLeadNormalization.cs`
- `src/UnicoreCRM.Platform/AccessControl/Application/Common/RecordAccessEvaluator.cs`
- `src/UnicoreCRM.Platform/AccessControl/Application/Common/WorkspaceCapabilityPolicy.cs`
- `src/UnicoreCRM.Platform/AccessControl/Application/EvaluateEffectiveRecordAccess/Handler.cs`
- `src/UnicoreCRM.Platform/AccessControl/Application/ProvisionInitialWorkspaceAccess/InitialWorkspaceAccessPolicy.cs`
- `src/UnicoreCRM.Platform/AccessControl/Contracts/RecordAccessEvaluation.cs`
- `src/UnicoreCRM.Platform/AccessControl/Contracts/RecordAccessFacts.cs`

### Frontend

- `docs/README.md`
- `docs/api/api-operation-catalog.json`
- `docs/api/generated-client-manifest.json`
- `docs/api/openapi.json`
- `docs/api/openapi.sha256`
- `docs/api/operation-coverage-ledger.json`
- `docs/architecture/compatibility-ledger.json`
- `docs/backend-readiness/lead-ownership-distribution-decision.json`
- `docs/backend-readiness/lead-ownership-distribution-decision.md`
- `docs/business/lead-ownership-and-distribution-v1.md`
- `docs/document-status.json`
- `docs/product/guidance-screen-inventory.md`
- `docs/product/guidance-system.md`
- `docs/quality/lead-ownership-distribution-acceptance.md`
- `docs/quality/release-identity.md`
- `docs/quality/repository-inventory.json`
- `docs/quality/repository-inventory.md`
- `scripts/lib/presentationCompositionSource.mts`
- `scripts/quality/quality-pipeline.json`
- `src/app/routes/routeMeta.ts`
- `src/features/auth/pages/WorkspaceSelectionPage.tsx`
- `src/guidance/content/studio/configuration.ts`
- `src/modules/contacts/presentation/pages/ContactDetailPage.tsx`
- `src/modules/leads/application/commands/leadApiCommands.ts`
- `src/modules/leads/application/commands/leadImportCommands.ts`
- `src/modules/leads/application/commands/leadRepositoryCommands.ts`
- `src/modules/leads/application/ports/LeadApiRuntime.ts`
- `src/modules/leads/domain/model/lead.types.ts`
- `src/modules/leads/domain/rules/leadLifecycle.ts`
- `src/modules/leads/infrastructure/dev-memory/leadDemoSeed.ts`
- `src/modules/leads/infrastructure/http/LeadApiMapper.ts`
- `src/modules/leads/infrastructure/http/LeadHttpQueryAdapter.ts`
- `src/modules/leads/presentation/components/LeadFormView.tsx`
- `src/modules/leads/presentation/components/LeadWorkPanel.tsx`
- `src/modules/leads/presentation/hooks/useLeadDetailController.tsx`
- `src/modules/leads/presentation/hooks/useLeadFormController.tsx`
- `src/modules/leads/presentation/hooks/useLeadReferenceData.ts`
- `src/modules/leads/presentation/views/LeadDetailView.tsx`
- `src/modules/leads/runtime/ingress/leadWebhookIngress.ts`
- `src/modules/products/detail-route.tsx`
- `src/platform/access-control/domain/capabilityCatalog.ts`
- `src/platform/access-control/domain/roleTemplates.ts`
- `src/platform/api/catalog/generatedApiOperationCatalog.ts`
- `src/platform/api/contracts/generatedOpenApiRuntimeContract.ts`
- `src/platform/api/contracts/generatedProductionQueryRegistry.ts`
- `src/platform/api/generated/accessGovernanceApi.ts`
- `src/platform/api/generated/aiApi.ts`
- `src/platform/api/generated/commercialApi.ts`
- `src/platform/api/generated/crmConfigurationApi.ts`
- `src/platform/api/generated/financialApi.ts`
- `src/platform/api/generated/financialConfigurationApi.ts`
- `src/platform/api/generated/identityApi.ts`
- `src/platform/api/generated/integrationConfigurationApi.ts`
- `src/platform/api/generated/productConfigurationApi.ts`
- `src/platform/api/generated/receivablesApi.ts`
- `src/platform/api/generated/studioQuickSetupApi.ts`
- `src/platform/api/generated/workspaceBootstrapApi.ts`
- `src/platform/api/generated/workspaceConfigurationApi.ts`
- `src/workflows/lead-customer-conversion/presentation/LeadCustomerConversionPage.tsx`
- `src/workflows/lead-qualification/presentation/pages/LeadQualificationPage.tsx`
- `src/workspaces/people-access/pilot-acceptance/domain/pilotAcceptance.types.ts`
- `src/workspaces/people-access/pilot-acceptance/runtime/pilotAcceptanceEvaluator.ts`
- `src/workspaces/studio/presentation/views/AiConfigurationView.tsx`
- `src/workspaces/studio/presentation/views/ConnectedOutboundWebhooksView.tsx`
- `tests/quality/architecture/check-backend-readiness.mts`
- `tests/quality/architecture/check-lead-api-boundary.mts`
- `tests/quality/architecture/check-quality-pipeline.mts`
- `tests/quality/contracts/check-customer-contact-ui-parity.mts`
- `tests/quality/contracts/check-form-runtime-sizing.mts`
- `tests/quality/contracts/check-lead-data-safety-contracts.mts`
- `tests/quality/contracts/check-lead-scale-experience-contracts.mts`
- `tests/quality/contracts/check-opportunity-presentation-contracts.mts`
- `tests/quality/contracts/check-relationship-activity-form-contracts.mts`
- `tests/quality/contracts/check-sales-quick-create-contracts.mts`
- `tests/quality/integration/check-blocked-command-containment.mts`
- `tests/quality/integration/check-crm-ui-interaction-contracts.mts`
- `tests/quality/integration/check-guidance-runtime.mts`
- `tests/quality/integration/check-lead-detail-surfaces.mts`
- `tests/quality/integration/check-lead-ownership-distribution-contracts.mts`
- `tests/quality/integration/check-lead-work-panel.mts`
- `tests/quality/integration/check-metric-drilldown-runtime.mts`
- `tests/quality/integration/check-partial-commit-controller-runtime.mts`
- `tests/quality/integration/check-read-only-cross-domain.mts`
- `tests/quality/route-smoke/check-route-module-loads.mts`

O1 changes implement nullable ownership, actor-bound admission, additive queue authorization, migration/query/projection safety, generated contracts and focused regression evidence. Continuation repairs also cover the existing Products access boundary, redundant Contacts resource memo, release metadata/scanner, bilingual labels/form styling, AI guidance registration and stale quality assertions. Modal sizing, clipping and bulk-action surfaces with unresolved failures were not redesigned.

## L. Tests and local evidence

Backend commands (backend working directory):

- `dotnet build --no-restore`: PASS, zero warnings/errors.
- `./scripts/verify-inbound-lead-webhook.ps1 -DatabaseName UnicoreCRM_Ownership_6871ef6705834d66b0dd94786cccacc9`: PASS on final expanded harness. Real ASP.NET host and SQL Server LocalDB, no mocked owner persistence.
- `dotnet ef migrations has-pending-model-changes --project src/UnicoreCRM.Crm --context LeadsDbContext --no-build`: PASS, no changes.
- `dotnet ef database update 20260912161132_AddLeadCustomerConversionReservation --project src/UnicoreCRM.Crm --context LeadsDbContext --no-build`, then latest update against separate `UnicoreCRM_OwnerMigration_20260929_8ae5d76d`: PASS; SQL comparison verified assigned row preservation. Down with null row: expected rejection PASS.
- `git diff --check`: PASS.

Frontend commands (frontend working directory): `npm run lint`, `npm run typecheck`, `npm run api:check`, `npm run build`, `npm run repo:check`, `npm run quality:list`, `git diff --check`: PASS. Focused gate invocation is `npm run quality:gate -- --gate <id>`.

| Gate | Result |
| --- | --- |
| `quality.lead-ownership-distribution-contracts` | PASS |
| `quality.lead-api-boundary` | PASS |
| `quality.lead-lifecycle-contracts` | PASS |
| `quality.lead-business-rules-contracts` | PASS |
| `quality.task-activity-api-boundary` | PASS |
| `quality.lead-work-panel` | PASS |
| `quality.lead-detail-surfaces` | PASS |
| `quality.lead-form-recovery-contracts` | PASS |
| `quality.lead-webhook-connector` | PASS |
| `quality.leads` | PASS |
| `quality.record-ownership-contracts` | PASS |
| `quality.workspace-isolation-contracts` | PASS |
| `quality.frontend-backend-separation` | PASS |
| `quality.connected-business-operation-availability` | PASS |
| `quality.quality-pipeline` | PASS |
| `quality.repository-inventory` | PASS |

`npm test`: FAIL after 33/150 gates; the export-visibility assertion already failed in the baseline run (33/149 before the new gate). That stale assertion is now corrected and its gate passes. A continuation full-suite run passed 37/150 before a stale archive assertion stopped it; that gate is also corrected. All remaining gates were executed individually, then affected gates were rerun after repairs. The latest per-gate evidence totals 144 PASS / 6 FAIL / 0 NOT_RUN; this is an aggregate of local executions, not a claim that one uninterrupted `npm test` invocation passed. See `lead-ownership-assignment-foundation-tests.json` for every gate and its log. Browser-like Edit/Work Panel tests run in JSDOM; a full connected Playwright/browser acceptance was NOT_RUN and is not claimed. The backend repository has no standalone `tests/` projects; existing executable PowerShell integration harness is the backend evidence. A separately attested independent reviewer and trusted-CI evidence were not available; this report describes local execution/self-review only, not VERIFIED/FROZEN release status.

Raw logs are in `C:/Users/welcome/AppData/Local/Temp/unicore-ownership-frontend-evidence/`, `C:/Users/welcome/AppData/Local/Temp/ownership-connected-final.log`, `ownership-build.log`, and `ownership-migration*.log`. Isolated test databases are retained for inspection; no user database was migrated. Harness API processes were stopped by their owned process IDs.

## M. Failures and limitations

### Current failures — 2026-09-30

| Gate | Current evidence | Disposition |
| --- | --- | --- |
| `quality.page-description-policy` | `ListStatePanel` renders a description while the global structural-copy gate forbids it. Existing consumers use it for operational errors. | Unresolved shared policy conflict; error explanations were not removed to satisfy the gate. |
| `quality.form-runtime-sizing` | `ContactCustomerRelationshipsPanel` has two controls but uses `md`; the canonical count-based gate requires `sm`. | Unresolved existing layout mismatch; no Contact modal redesign in O1. The typed relationship wrapper is now checked correctly before this failure. |
| `quality.crm-presentation-contracts` | Contact list does not contain the required `ListBulkActionBar`, even when the test reads its actual controller/view composition. | Unresolved surface contract; no new bulk mutation UI was invented. |
| `quality.metric-drilldown-runtime` | The fixture lacks `reports.read`; the current server-admitted capability set excludes it. Added setup assertions expose the authorization cause before the formerly failing click. | Unresolved fixture/product admission mismatch; no report permission was added to system or custom roles. |
| `quality.studio-route-runtime` | Demo navigation to AI crashes with `AI_CONFIGURATION_RUNTIME_UNAVAILABLE`. AI guidance is now present, but a demo configuration gateway is absent. | Unresolved demo runtime gap; no fake configuration gateway was added. |
| `quality.overflow-resilience` | Global no-clipping rule rejects existing `truncate` / `line-clamp-2` in `LeadWorkPanel`. These classes are also present at starting HEAD; focused frozen Work Panel tests pass. | Unresolved shared rule versus frozen surface conflict; clipping/layout was not silently changed. |

These are observed failures with source-level causes. Except where explicitly stated, they were not all reproduced by executing a separate untouched baseline checkout, so this report does not blanket-classify them as proven pre-existing test failures. None is an unassigned-record authorization blocker.

### Resolved in O1 / continuation

- Explicit null omitted by global JSON settings: fixed with property-specific serialization; real HTTP tests pass.
- Nullable TypeScript consumers and edit default owner: fixed without synthesizing ownership.
- Concurrent duplicate webhook SQL deadlock: fixed by reserving the idempotency key range; complete real-host harness passes.
- Export/archive assertions: aligned to current permission plus availability guards and optional archive reason; retention checks retained.
- Backend-readiness document scan: excludes generated browser artifacts using the standard ignored-directory list.
- Product record-access boundary restored; Contacts detail uses its existing cached resource directly.
- Release Markdown/registry/compatibility metadata reconciled to actual 0.24.0-contract.0. This is mechanical presence/status/authority validation, not semantic certification of every document.
- AI guidance added with its own read capability, 80 routes / 68 contextual entries / 66 screens; content and runtime guidance checks pass.
- Partial-commit controller assertion now expects the intentional rejection after partial success; persisted-data/toast checks remain.
- Order/Quote source checks follow the delegated portal; route count includes AI; CRLF-sensitive markers and old lifecycle labels corrected.
- Quick Create test retains missing-name/contact negatives and configurable enrichment coverage, matching the frozen Lead policy.
- Relationship form tests distinguish the existing Handover shell from canonical activity forms. No Handover availability was changed.
- Blocked-command tests retain legacy refusal and zero-transport checks while recognizing admitted Customer archive and dedicated Contact archive; Customer concurrency metadata remains mandatory.
- Return creation route is checked against the actual OpenAPI `returns.update` capability, rather than a stale `returns.create` assumption.

No commit or push was made because full acceptance remains NEEDS_FIX. No independent reviewer/trusted-CI attestation or connected Playwright acceptance is claimed. Later Claim/Assign/Handover work and unresolved Handover SLA remain outside O1.

## N. O1 acceptance matrix

| Acceptance | Result | Evidence |
| --- | --- | --- |
| Manual creation actor-owned | PASS | Real HTTP creation and persisted owner |
| External creation unassigned | PASS | Signed webhook, SQL null, preserved delegated audit |
| No fake queue owner | PASS | Null authoritative owner; sentinel admission/projection rejected |
| Unassigned persisted | PASS | Migration and profile/scope SQL assertions |
| Unassigned projected | PASS | Required explicit null detail/list; frontend mapper |
| AssignmentState query | PASS | Assigned/unassigned, invalid/contradictory filters, counts/cursors |
| Queue access secure | PASS | WORKSPACE/OWN permissions, TEAM/CUSTOM fail closed, effective access, field security |
| Workspace isolation | PASS | Foreign workspace list/detail/mutation denial |
| Existing assigned Leads regression | PASS | Migration preserves all columns; edit, lifecycle, archive preserve owner |
| Frontend nullable owner safety | PASS | Strict lint, mapper, unassigned Edit and Work Panel |
| Claim still unavailable | PASS | Runtime rejection, no HTTP; no backend route added |
| Assign still unavailable | PASS | Single/bulk unavailable; profile cannot assign/unassign |
| Handover still unavailable | PASS | Runtime rejection, no HTTP; no backend route added |
| Foundation regression | FAIL | Focused frozen Lead/lifecycle gates pass; six remaining full-suite failures prevent a green suite |
| Work Panel regression | PASS | Existing behavior plus unassigned rendering assertions |

**LEAD_OWNERSHIP_O1_NEEDS_FIX**
