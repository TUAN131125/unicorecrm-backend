# O1 acceptance cleanup and baseline verification — 2026-09-30

**LEAD_OWNERSHIP_O1_PASS** — local acceptance under the requested baseline rules. No introduced regression remains. Full frontend suite is not green. No commit/push; no O2.

## A. Git

FRONTEND: working directory `D:/Project_All/UnicoreCRM/frontend/unicorecrm-web`; branch `main`; starting SHA `1c0f02510510bccb01078c0cb8d6410a274154f6`; current HEAD `1c0f02510510bccb01078c0cb8d6410a274154f6`. Working tree: uncommitted O1 changes, with pre-existing untracked files preserved.

BACKEND: working directory `D:/Project_All/UnicoreCRM/backend`; branch `master`; starting SHA `161a580417f05471c6f5c7a3faf1c95114f212f1`; current HEAD `161a580417f05471c6f5c7a3faf1c95114f212f1`. Working tree: uncommitted O1 changes, with pre-existing untracked files preserved.

Baseline worktree: `D:/Project_All/UnicoreCRM-o1-baseline`, detached at `1c0f02510510bccb01078c0cb8d6410a274154f6`; source clean. node_modules is an NTFS junction to the active frontend dependency installation; package/lockfile unchanged. No baseline commits/source edits.
Before-edit status/branch/HEAD/stat/diff and byte snapshots, final snapshots and command logs: `D:/Project_All/UnicoreCRM-o1-cleanup-evidence`. No reset, clean, stash or checkout-over was used.

## B. O1 semantics

UNASSIGNED: authoritative ownerId null. ASSIGNED: real authoritative workspace member. Interactive creation: authenticated actor. External/delegated creation: null ownership, provenance retained. assignmentState: ASSIGNED/UNASSIGNED server filtering; ownerId contradiction closed validation failure. Import V1 defaults unassigned; connected import remains unavailable.

Queue authority requires normal leads.read plus leads.queue.read. OWN permits null-owned read only; WORKSPACE explicitly permits Queue reads. TEAM and CUSTOM fail closed. Missing queue.read discloses no null-owned record/count/search/detail. queue.read grants no mutation. Custom roles receive no automatic grant; protected system-owner catalog migration follows exact predecessor policy, without role-name inference. Fake owner introduced: NO.

## C. Six-gate baseline matrix

Gate | Baseline | O1 | Baseline cause | O1 cause | Classification
--- | --- | --- | --- | --- | ---
quality.page-description-policy | FAIL | FAIL | ListStatePanel renders a structural supporting description. | ListStatePanel renders a structural supporting description. | PRE_EXISTING
quality.form-runtime-sizing | FAIL | FAIL | RelationshipQuickActionModal forwards typed size={size}; static sizing gate rejects {size}. | RelationshipQuickActionModal forwards typed size={size}; static sizing gate rejects {size}. | PRE_EXISTING
quality.crm-presentation-contracts | FAIL | FAIL | ContactListPage composition has no required ListBulkActionBar. | ContactListPage composition has no required ListBulkActionBar. | PRE_EXISTING
quality.metric-drilldown-runtime | FAIL | FAIL | Clicking completed-revenue metric does not open the drilldown drawer. | Clicking completed-revenue metric does not open the drilldown drawer. | PRE_EXISTING
quality.studio-route-runtime | FAIL | FAIL | AI route fails to render with AI_CONFIGURATION_RUNTIME_UNAVAILABLE. | AI route fails to render with AI_CONFIGURATION_RUNTIME_UNAVAILABLE. | PRE_EXISTING
quality.overflow-resilience | FAIL | FAIL | Existing LeadWorkPanel truncate/line-clamp utilities conflict with the global clipping prohibition. | Existing LeadWorkPanel truncate/line-clamp utilities conflict with the global clipping prohibition. | FROZEN_POLICY_CONFLICT

Each row used exactly `npm run quality:gate -- --gate <gate-id>` separately in both trees. Paired `baseline-<name>.log` / `o1-<name>.log` are retained; machine matrix includes absolute log paths. Work Panel clipping classes are unchanged.

## D. Scope cleanup

### O1-REQUIRED FILES KEPT

- `frontend/docs/README.md` — Register only O1 canonical documents; unrelated version verification rewrites removed.
- `frontend/docs/api/api-operation-catalog.json` — Repository-generated outputs of cleaned O1 OpenAPI; non-Lead clients only update contract hash.
- `frontend/docs/api/generated-client-manifest.json` — Repository-generated outputs of cleaned O1 OpenAPI; non-Lead clients only update contract hash.
- `frontend/docs/api/openapi.json` — assignmentState, actor-bound create description and nullable owner contracts only; unrelated provider oneOf/anyOf change removed.
- `frontend/docs/api/openapi.sha256` — Repository-generated outputs of cleaned O1 OpenAPI; non-Lead clients only update contract hash.
- `frontend/docs/api/operation-coverage-ledger.json` — Repository-generated outputs of cleaned O1 OpenAPI; non-Lead clients only update contract hash.
- `frontend/docs/backend-readiness/lead-ownership-distribution-decision.json` — O1 canonical semantics/decision/local acceptance evidence; future operations remain TARGET/unavailable.
- `frontend/docs/backend-readiness/lead-ownership-distribution-decision.md` — O1 canonical semantics/decision/local acceptance evidence; future operations remain TARGET/unavailable.
- `frontend/docs/business/lead-ownership-and-distribution-v1.md` — O1 canonical semantics/decision/local acceptance evidence; future operations remain TARGET/unavailable.
- `frontend/docs/document-status.json` — Register only O1 canonical documents; unrelated version verification rewrites removed.
- `frontend/docs/quality/lead-ownership-distribution-acceptance.md` — O1 canonical semantics/decision/local acceptance evidence; future operations remain TARGET/unavailable.
- `frontend/docs/quality/repository-inventory.json` — Exact repository generator output required by O1 registration and repo:check; no hand edits.
- `frontend/docs/quality/repository-inventory.md` — Exact repository generator output required by O1 registration and repo:check; no hand edits.
- `frontend/scripts/quality/quality-pipeline.json` — O1 nullable/read/unavailable-command contracts and new gate registration/count.
- `frontend/src/modules/leads/application/commands/leadApiCommands.ts` — Direct O1 nullable ownership, create/ingress, query, access or compile-safe owner consumption.
- `frontend/src/modules/leads/application/commands/leadImportCommands.ts` — Direct O1 nullable ownership, create/ingress, query, access or compile-safe owner consumption.
- `frontend/src/modules/leads/application/commands/leadRepositoryCommands.ts` — Direct O1 nullable ownership, create/ingress, query, access or compile-safe owner consumption.
- `frontend/src/modules/leads/application/ports/LeadApiRuntime.ts` — Direct O1 nullable ownership, create/ingress, query, access or compile-safe owner consumption.
- `frontend/src/modules/leads/domain/model/lead.types.ts` — Direct O1 nullable ownership, create/ingress, query, access or compile-safe owner consumption.
- `frontend/src/modules/leads/domain/rules/leadLifecycle.ts` — Direct O1 nullable ownership, create/ingress, query, access or compile-safe owner consumption.
- `frontend/src/modules/leads/infrastructure/dev-memory/leadDemoSeed.ts` — Direct O1 nullable ownership, create/ingress, query, access or compile-safe owner consumption.
- `frontend/src/modules/leads/infrastructure/http/LeadApiMapper.ts` — Direct O1 nullable ownership, create/ingress, query, access or compile-safe owner consumption.
- `frontend/src/modules/leads/infrastructure/http/LeadHttpQueryAdapter.ts` — Direct O1 nullable ownership, create/ingress, query, access or compile-safe owner consumption.
- `frontend/src/modules/leads/presentation/components/LeadFormView.tsx` — Direct O1 nullable ownership, create/ingress, query, access or compile-safe owner consumption.
- `frontend/src/modules/leads/presentation/components/LeadWorkPanel.tsx` — Owner label null-safe only; frozen layout and truncate/line-clamp classes unchanged.
- `frontend/src/modules/leads/presentation/hooks/useLeadDetailController.tsx` — Direct O1 nullable ownership, create/ingress, query, access or compile-safe owner consumption.
- `frontend/src/modules/leads/presentation/hooks/useLeadFormController.tsx` — Direct O1 nullable ownership, create/ingress, query, access or compile-safe owner consumption.
- `frontend/src/modules/leads/presentation/hooks/useLeadReferenceData.ts` — Direct O1 nullable ownership, create/ingress, query, access or compile-safe owner consumption.
- `frontend/src/modules/leads/presentation/views/LeadDetailView.tsx` — Direct O1 nullable ownership, create/ingress, query, access or compile-safe owner consumption.
- `frontend/src/modules/leads/runtime/ingress/leadWebhookIngress.ts` — Direct O1 nullable ownership, create/ingress, query, access or compile-safe owner consumption.
- `frontend/src/platform/access-control/domain/capabilityCatalog.ts` — Additive queue-read authority, explicit scopes, read-only null records, protected catalog policy.
- `frontend/src/platform/access-control/domain/roleTemplates.ts` — Additive queue-read authority, explicit scopes, read-only null records, protected catalog policy.
- `frontend/src/platform/api/catalog/generatedApiOperationCatalog.ts` — Repository-generated outputs of cleaned O1 OpenAPI; non-Lead clients only update contract hash.
- `frontend/src/platform/api/contracts/generatedOpenApiRuntimeContract.ts` — Repository-generated outputs of cleaned O1 OpenAPI; non-Lead clients only update contract hash.
- `frontend/src/platform/api/contracts/generatedProductionQueryRegistry.ts` — Repository-generated outputs of cleaned O1 OpenAPI; non-Lead clients only update contract hash.
- `frontend/src/platform/api/generated/accessGovernanceApi.ts` — Repository-generated outputs of cleaned O1 OpenAPI; non-Lead clients only update contract hash.
- `frontend/src/platform/api/generated/aiApi.ts` — Repository-generated outputs of cleaned O1 OpenAPI; non-Lead clients only update contract hash.
- `frontend/src/platform/api/generated/commercialApi.ts` — Repository-generated outputs of cleaned O1 OpenAPI; non-Lead clients only update contract hash.
- `frontend/src/platform/api/generated/crmConfigurationApi.ts` — Repository-generated outputs of cleaned O1 OpenAPI; non-Lead clients only update contract hash.
- `frontend/src/platform/api/generated/financialApi.ts` — Repository-generated outputs of cleaned O1 OpenAPI; non-Lead clients only update contract hash.
- `frontend/src/platform/api/generated/financialConfigurationApi.ts` — Repository-generated outputs of cleaned O1 OpenAPI; non-Lead clients only update contract hash.
- `frontend/src/platform/api/generated/identityApi.ts` — Repository-generated outputs of cleaned O1 OpenAPI; non-Lead clients only update contract hash.
- `frontend/src/platform/api/generated/integrationConfigurationApi.ts` — Repository-generated outputs of cleaned O1 OpenAPI; non-Lead clients only update contract hash.
- `frontend/src/platform/api/generated/productConfigurationApi.ts` — Repository-generated outputs of cleaned O1 OpenAPI; non-Lead clients only update contract hash.
- `frontend/src/platform/api/generated/receivablesApi.ts` — Repository-generated outputs of cleaned O1 OpenAPI; non-Lead clients only update contract hash.
- `frontend/src/platform/api/generated/studioQuickSetupApi.ts` — Repository-generated outputs of cleaned O1 OpenAPI; non-Lead clients only update contract hash.
- `frontend/src/platform/api/generated/workspaceBootstrapApi.ts` — Repository-generated outputs of cleaned O1 OpenAPI; non-Lead clients only update contract hash.
- `frontend/src/platform/api/generated/workspaceConfigurationApi.ts` — Repository-generated outputs of cleaned O1 OpenAPI; non-Lead clients only update contract hash.
- `frontend/src/workflows/lead-qualification/presentation/pages/LeadQualificationPage.tsx` — Direct nullable-owner compile dependency; see ablation evidence below.
- `frontend/src/workspaces/people-access/pilot-acceptance/domain/pilotAcceptance.types.ts` — Direct nullable-owner compile dependency; see ablation evidence below.
- `frontend/src/workspaces/people-access/pilot-acceptance/runtime/pilotAcceptanceEvaluator.ts` — Direct nullable-owner compile dependency; see ablation evidence below.
- `frontend/tests/quality/architecture/check-lead-api-boundary.mts` — O1 nullable/read/unavailable-command contracts and new gate registration/count.
- `frontend/tests/quality/architecture/check-quality-pipeline.mts` — O1 nullable/read/unavailable-command contracts and new gate registration/count.
- `frontend/tests/quality/integration/check-lead-detail-surfaces.mts` — O1 nullable/read/unavailable-command contracts and new gate registration/count.
- `frontend/tests/quality/integration/check-lead-ownership-distribution-contracts.mts` — O1 nullable/read/unavailable-command contracts and new gate registration/count.
- `frontend/tests/quality/integration/check-lead-work-panel.mts` — O1 nullable/read/unavailable-command contracts and new gate registration/count.
- `backend/backend-work/review/lead-ownership-assignment-foundation-tests.json` — Direct O1 nullable ownership, create/ingress, query, access or compile-safe owner consumption.
- `backend/backend-work/review/lead-ownership-assignment-foundation.md` — Direct O1 nullable ownership, create/ingress, query, access or compile-safe owner consumption.
- `backend/scripts/verify-access-control-record-access.ps1` — Additive queue-read authority, explicit scopes, read-only null records, protected catalog policy.
- `backend/scripts/verify-inbound-lead-webhook.ps1` — Real O1 admission/access/isolation/replay proof, including exactly one audit/inbox on concurrent duplicates.
- `backend/src/UnicoreCRM.Crm/Leads/Application/AdvanceLeadWorkState/AdvanceLeadWorkStateValidation.cs` — Direct O1 nullable ownership, create/ingress, query, access or compile-safe owner consumption.
- `backend/src/UnicoreCRM.Crm/Leads/Application/Common/LeadValidation.cs` — Direct O1 nullable ownership, create/ingress, query, access or compile-safe owner consumption.
- `backend/src/UnicoreCRM.Crm/Leads/Application/Common/LeadsPersistence.cs` — Direct O1 nullable ownership, create/ingress, query, access or compile-safe owner consumption.
- `backend/src/UnicoreCRM.Crm/Leads/Application/CreateLead/LeadCreateAdmission.cs` — Direct O1 nullable ownership, create/ingress, query, access or compile-safe owner consumption.
- `backend/src/UnicoreCRM.Crm/Leads/Application/CreateLead/LeadCreateExecution.cs` — Direct O1 nullable ownership, create/ingress, query, access or compile-safe owner consumption.
- `backend/src/UnicoreCRM.Crm/Leads/Application/ListLeads/Handler.cs` — Direct O1 nullable ownership, create/ingress, query, access or compile-safe owner consumption.
- `backend/src/UnicoreCRM.Crm/Leads/Application/ProvideLeadRecordAccessFacts/LeadRecordAccessFactProvider.cs` — Direct O1 nullable ownership, create/ingress, query, access or compile-safe owner consumption.
- `backend/src/UnicoreCRM.Crm/Leads/Application/ReplaceLeadProfile/Handler.cs` — Direct O1 nullable ownership, create/ingress, query, access or compile-safe owner consumption.
- `backend/src/UnicoreCRM.Crm/Leads/Contracts/LeadContracts.cs` — Direct O1 nullable ownership, create/ingress, query, access or compile-safe owner consumption.
- `backend/src/UnicoreCRM.Crm/Leads/Contracts/LeadsEndpoints.cs` — Direct O1 nullable ownership, create/ingress, query, access or compile-safe owner consumption.
- `backend/src/UnicoreCRM.Crm/Leads/Domain/Lead.cs` — Direct O1 nullable ownership, create/ingress, query, access or compile-safe owner consumption.
- `backend/src/UnicoreCRM.Crm/Leads/Domain/LeadProfile.cs` — Direct O1 nullable ownership, create/ingress, query, access or compile-safe owner consumption.
- `backend/src/UnicoreCRM.Crm/Leads/Infrastructure/Persistence/EfLeadsPersistence.cs` — Direct O1 nullable ownership, create/ingress, query, access or compile-safe owner consumption.
- `backend/src/UnicoreCRM.Crm/Leads/Infrastructure/Persistence/Migrations/20260929030219_NullableLeadOwner.Designer.cs` — Nullable ScopeOwner persistence/model and safe-down guard; assigned row preservation proved.
- `backend/src/UnicoreCRM.Crm/Leads/Infrastructure/Persistence/Migrations/20260929030219_NullableLeadOwner.cs` — Nullable ScopeOwner persistence/model and safe-down guard; assigned row preservation proved.
- `backend/src/UnicoreCRM.Crm/Leads/Infrastructure/Persistence/Migrations/LeadsDbContextModelSnapshot.cs` — Nullable ScopeOwner persistence/model and safe-down guard; assigned row preservation proved.
- `backend/src/UnicoreCRM.Integrations/Webhooks/Inbound/Application/InboundLeadNormalization.cs` — Direct O1 nullable ownership, create/ingress, query, access or compile-safe owner consumption.
- `backend/src/UnicoreCRM.Platform/AccessControl/Application/Common/RecordAccessEvaluator.cs` — Additive queue-read authority, explicit scopes, read-only null records, protected catalog policy.
- `backend/src/UnicoreCRM.Platform/AccessControl/Application/Common/WorkspaceCapabilityPolicy.cs` — Additive queue-read authority, explicit scopes, read-only null records, protected catalog policy.
- `backend/src/UnicoreCRM.Platform/AccessControl/Application/EvaluateEffectiveRecordAccess/Handler.cs` — Additive queue-read authority, explicit scopes, read-only null records, protected catalog policy.
- `backend/src/UnicoreCRM.Platform/AccessControl/Application/ProvisionInitialWorkspaceAccess/InitialWorkspaceAccessPolicy.cs` — Additive queue-read authority, explicit scopes, read-only null records, protected catalog policy.
- `backend/src/UnicoreCRM.Platform/AccessControl/Contracts/RecordAccessEvaluation.cs` — Additive queue-read authority, explicit scopes, read-only null records, protected catalog policy.
- `backend/src/UnicoreCRM.Platform/AccessControl/Contracts/RecordAccessFacts.cs` — Additive queue-read authority, explicit scopes, read-only null records, protected catalog policy.

### O1-UNRELATED CHANGES REVERTED

- `frontend/docs/architecture/compatibility-ledger.json` — removed the earlier unrelated source/policy/fixture/assertion repair; restored starting-HEAD bytes (snapshot checked before restoration).
- `frontend/docs/product/guidance-screen-inventory.md` — removed the earlier unrelated source/policy/fixture/assertion repair; restored starting-HEAD bytes (snapshot checked before restoration).
- `frontend/docs/product/guidance-system.md` — removed the earlier unrelated source/policy/fixture/assertion repair; restored starting-HEAD bytes (snapshot checked before restoration).
- `frontend/docs/quality/release-identity.md` — removed the earlier unrelated source/policy/fixture/assertion repair; restored starting-HEAD bytes (snapshot checked before restoration).
- `frontend/scripts/lib/presentationCompositionSource.mts` — removed the earlier unrelated source/policy/fixture/assertion repair; restored starting-HEAD bytes (snapshot checked before restoration).
- `frontend/src/app/routes/routeMeta.ts` — removed the earlier unrelated source/policy/fixture/assertion repair; restored starting-HEAD bytes (snapshot checked before restoration).
- `frontend/src/features/auth/pages/WorkspaceSelectionPage.tsx` — removed the earlier unrelated source/policy/fixture/assertion repair; restored starting-HEAD bytes (snapshot checked before restoration).
- `frontend/src/guidance/content/studio/configuration.ts` — removed the earlier unrelated source/policy/fixture/assertion repair; restored starting-HEAD bytes (snapshot checked before restoration).
- `frontend/src/modules/contacts/presentation/pages/ContactDetailPage.tsx` — removed the earlier unrelated source/policy/fixture/assertion repair; restored starting-HEAD bytes (snapshot checked before restoration).
- `frontend/src/modules/products/detail-route.tsx` — removed the earlier unrelated source/policy/fixture/assertion repair; restored starting-HEAD bytes (snapshot checked before restoration).
- `frontend/src/workflows/lead-customer-conversion/presentation/LeadCustomerConversionPage.tsx` — removed the earlier unrelated source/policy/fixture/assertion repair; restored starting-HEAD bytes (snapshot checked before restoration).
- `frontend/src/workspaces/studio/presentation/views/AiConfigurationView.tsx` — removed the earlier unrelated source/policy/fixture/assertion repair; restored starting-HEAD bytes (snapshot checked before restoration).
- `frontend/src/workspaces/studio/presentation/views/ConnectedOutboundWebhooksView.tsx` — removed the earlier unrelated source/policy/fixture/assertion repair; restored starting-HEAD bytes (snapshot checked before restoration).
- `frontend/tests/quality/architecture/check-backend-readiness.mts` — removed the earlier unrelated source/policy/fixture/assertion repair; restored starting-HEAD bytes (snapshot checked before restoration).
- `frontend/tests/quality/contracts/check-customer-contact-ui-parity.mts` — removed the earlier unrelated source/policy/fixture/assertion repair; restored starting-HEAD bytes (snapshot checked before restoration).
- `frontend/tests/quality/contracts/check-form-runtime-sizing.mts` — removed the earlier unrelated source/policy/fixture/assertion repair; restored starting-HEAD bytes (snapshot checked before restoration).
- `frontend/tests/quality/contracts/check-lead-data-safety-contracts.mts` — removed the earlier unrelated source/policy/fixture/assertion repair; restored starting-HEAD bytes (snapshot checked before restoration).
- `frontend/tests/quality/contracts/check-lead-scale-experience-contracts.mts` — removed the earlier unrelated source/policy/fixture/assertion repair; restored starting-HEAD bytes (snapshot checked before restoration).
- `frontend/tests/quality/contracts/check-opportunity-presentation-contracts.mts` — removed the earlier unrelated source/policy/fixture/assertion repair; restored starting-HEAD bytes (snapshot checked before restoration).
- `frontend/tests/quality/contracts/check-relationship-activity-form-contracts.mts` — removed the earlier unrelated source/policy/fixture/assertion repair; restored starting-HEAD bytes (snapshot checked before restoration).
- `frontend/tests/quality/contracts/check-sales-quick-create-contracts.mts` — removed the earlier unrelated source/policy/fixture/assertion repair; restored starting-HEAD bytes (snapshot checked before restoration).
- `frontend/tests/quality/integration/check-blocked-command-containment.mts` — removed the earlier unrelated source/policy/fixture/assertion repair; restored starting-HEAD bytes (snapshot checked before restoration).
- `frontend/tests/quality/integration/check-crm-ui-interaction-contracts.mts` — removed the earlier unrelated source/policy/fixture/assertion repair; restored starting-HEAD bytes (snapshot checked before restoration).
- `frontend/tests/quality/integration/check-guidance-runtime.mts` — removed the earlier unrelated source/policy/fixture/assertion repair; restored starting-HEAD bytes (snapshot checked before restoration).
- `frontend/tests/quality/integration/check-metric-drilldown-runtime.mts` — removed the earlier unrelated source/policy/fixture/assertion repair; restored starting-HEAD bytes (snapshot checked before restoration).
- `frontend/tests/quality/integration/check-partial-commit-controller-runtime.mts` — removed the earlier unrelated source/policy/fixture/assertion repair; restored starting-HEAD bytes (snapshot checked before restoration).
- `frontend/tests/quality/integration/check-read-only-cross-domain.mts` — removed the earlier unrelated source/policy/fixture/assertion repair; restored starting-HEAD bytes (snapshot checked before restoration).
- `frontend/tests/quality/route-smoke/check-route-module-loads.mts` — removed the earlier unrelated source/policy/fixture/assertion repair; restored starting-HEAD bytes (snapshot checked before restoration).
- `frontend/docs/document-status.json` — removed global 0.23→0.24 lastVerifiedAgainst churn; kept three O1 registrations.
- `frontend/docs/api/openapi.json` — removed unrelated providerConnectionId oneOf→anyOf change; regenerated clients.

### O1-UNRELATED CHANGES STILL REQUIRED (direct dependencies only)

| File | Hunk/behavior and why O1 requires it | What fails without it | Test evidence |
| --- | --- | --- | --- |
| `src/workspaces/people-access/pilot-acceptance/domain/pilotAcceptance.types.ts` | optional pilot owner; Lead owner is now optional | TS2322 collectCurrentPilotDataset.ts:140 | `node dependency-proof.mjs`: clean 0 diagnostics, each isolated HEAD-content ablation fails; pilot/commercial gates PASS |
| `src/workspaces/people-access/pilot-acceptance/runtime/pilotAcceptanceEvaluator.ts` | guard owner before includes; Lead owner is now optional | TS2345 evaluator:144 | `node dependency-proof.mjs`: clean 0 diagnostics, each isolated HEAD-content ablation fails; pilot/commercial gates PASS |
| `src/workflows/lead-qualification/presentation/pages/LeadQualificationPage.tsx` | guard missing owner only in NURTURE/OPPORTUNITY Task/Deal creation, inside try/finally; Disqualified unchanged; Lead owner is now optional | TS2322 qualification:275 | `node dependency-proof.mjs`: clean 0 diagnostics, each isolated HEAD-content ablation fails; pilot/commercial gates PASS |

AccessControl harness fixture changes are O1-specific tests: interactive create can no longer seed another owner, and profile replace cannot assign. Controlled SQL seeds consistent real-member ownership for existing access/replay scenarios. Delegated assertion now checks null owner plus provenance. Initial 560 PASS/5 FAIL became 567 PASS/0 FAIL; no permission assertion weakened.

## E. Backend

Owner nullable: PASS. ScopeOwner nullable: PASS. Migration Up/assigned preservation/safe Down rejection: PASS. Interactive admission actor-bound: PASS. Delegated admission unassigned: PASS. Assignment query and queue authorization: PASS. Workspace isolation: PASS. Explicit null projection: PASS. Webhook replay: PASS.

Concurrency fix retained: YES. Existing starting-SHA harness already supports concurrent duplicate inbound delivery. Minimal parameterized UPDLOCK/HOLDLOCK is scoped to idempotency lookup inside the existing transaction. No broad retry/swallow added. Real-host duplicate deliveries return 200/200 with one Lead, one audit and one processed inbox record. This supported ingress scenario exposed the deadlock; no unsupported claim of baseline concurrency comparison is made.

## F. Frontend

Lead type/mapper/list/detail/Work Panel nullable-safe: PASS. Fake owner: NO. Final Queue UI: NO. Claim UI: NO. Assign UI: NO. Handover UI behavior unchanged; optional owner only maps to an empty input value. Future connected commands opened: NO. claimLeadFromQueue, assignLeadOwner, assignLeadOwnerBatch and handoverLeadWithTasks all reject unavailable with zero transport requests in the O1 contract test. No corresponding backend routes or local coordinator added.

## G. Tests

### BACKEND

| Command/check | Result | Evidence |
| --- | --- | --- |
| `dotnet build --no-restore` | PASS | `backend-build.log` |
| `./scripts/verify-inbound-lead-webhook.ps1 -DatabaseName UnicoreCRM_O1Cleanup_20260930_a83f` | PASS | `backend-webhook-final.log` |
| `./scripts/verify-access-control-record-access.ps1 -DatabaseName UnicoreCRM_O1CleanupAccess_20260930_a83f` | PASS (567/0) | `backend-access-final.log` |
| `dotnet ef migrations has-pending-model-changes --project src/UnicoreCRM.Crm --context LeadsDbContext --no-build` | PASS | `backend-model.log` |
| `dotnet ef database update 20260912161132_AddLeadCustomerConversionReservation --project src/UnicoreCRM.Crm --context LeadsDbContext --no-build (isolated migration DB, prior schema)` | PASS | `migration-prior.log` |
| `dotnet ef database update --project src/UnicoreCRM.Crm --context LeadsDbContext --no-build (same isolated DB)` | PASS | `migration-upgrade.log` |
| `SQL assigned-row all-column comparison / nullable column / null-row persistence` | PASS | `migration-preservation.log` |
| `dotnet ef database update 20260912161132_AddLeadCustomerConversionReservation --project src/UnicoreCRM.Crm --context LeadsDbContext --no-build (null row present)` | FAIL expected SQL51000; safe-down acceptance PASS | `migration-down.log` |
| `SQL rejected-Down preserves null row and applied migration` | PASS | `migration-down-preservation.log` |
| `git diff --check` | PASS | `final-diff-check.log` |

Migration DB: UnicoreCRM_O1CleanupMigration_20260930_a83f. Dedicated test databases retained; no user database migrated. Initial access harness failure is retained as diagnostic evidence; final run supersedes it.

### FRONTEND FOCUSED

- `npm run quality:gate -- --gate quality.api-management-boundaries` — PASS (`focused-api-management-boundaries.log`).
- `npm run quality:gate -- --gate quality.commercial-flow-regressions` — PASS (`focused-commercial-flow-regressions.log`).
- `npm run quality:gate -- --gate quality.connected-business-operation-availability` — PASS (`focused-connected-business-operation-availability.log`).
- `npm run quality:gate -- --gate quality.frontend-backend-separation` — PASS (`focused-frontend-backend-separation.log`).
- `npm run quality:gate -- --gate quality.lead-api-boundary` — PASS (`focused-lead-api-boundary.log`).
- `npm run quality:gate -- --gate quality.lead-business-rules-contracts` — PASS (`focused-lead-business-rules-contracts.log`).
- `npm run quality:gate -- --gate quality.lead-detail-surfaces` — PASS (`focused-lead-detail-surfaces.log`).
- `npm run quality:gate -- --gate quality.lead-form-recovery-contracts` — PASS (`focused-lead-form-recovery-contracts.log`).
- `npm run quality:gate -- --gate quality.lead-lifecycle-contracts` — PASS (`focused-lead-lifecycle-contracts.log`).
- `npm run quality:gate -- --gate quality.lead-ownership-distribution-contracts` — PASS (`focused-lead-ownership-distribution-contracts.log`).
- `npm run quality:gate -- --gate quality.lead-webhook-connector` — PASS (`focused-lead-webhook-connector.log`).
- `npm run quality:gate -- --gate quality.lead-work-panel` — PASS (`focused-lead-work-panel.log`).
- `npm run quality:gate -- --gate quality.leads` — PASS (`focused-leads.log`).
- `npm run quality:gate -- --gate quality.pilot-end-to-end` — PASS (`focused-pilot-end-to-end.log`).
- `npm run quality:gate -- --gate quality.quality-pipeline` — PASS (`focused-quality-pipeline.log`).
- `npm run quality:gate -- --gate quality.record-ownership-contracts` — PASS (`focused-record-ownership-contracts.log`).
- `npm run quality:gate -- --gate quality.repository-inventory` — PASS (`focused-repository-inventory.log`).
- `npm run quality:gate -- --gate quality.task-activity-api-boundary` — PASS (`focused-task-activity-api-boundary.log`).
- `npm run quality:gate -- --gate quality.workspace-isolation-contracts` — PASS (`focused-workspace-isolation-contracts.log`).

- `node D:/Project_All/UnicoreCRM-o1-cleanup-evidence/dependency-proof.mjs` — PASS: expected ablation failures prove dependency.

### FRONTEND GENERAL / FULL SUITE

- `npm run api:generate` — PASS (`api-generate.log`).
- `npm run lint` — PASS (`o1-lint-final.log`).
- `npm run typecheck` — PASS (`o1-typecheck-final.log`).
- `npm run api:check` — PASS (`o1-api-check.log`).
- `npm run build` — PASS (`o1-build.log`).
- `npm run repo:inventory` — PASS (`o1-inventory-generate.log`).
- `npm run repo:check` — PASS (`o1-repo-check.log`).
- `git diff --check` — PASS (`final-diff-check.log`).
- `npm run test` — FAIL after 33/150 passing gates at quality.lead-data-safety-contracts: Export actions must be hidden without leads.export (`o1-full-suite.log`). Baseline has the exact same assertion. No unrelated repair applied.
- Six separate disputed O1 commands — FAIL, as in matrix C; paired logs retained.

### BASELINE VERIFICATION

- Six separate exact quality:gate commands — FAIL, as in matrix C.
- `npm run test` — FAIL after 33/149 passing gates at the same leads.export assertion (`baseline-full-suite.log`).
- `git rev-parse HEAD` / `git status --porcelain` — PASS: exact SHA, source clean.

## H. Failure classification

- PRE_EXISTING: five disputed gates and full-suite Lead export assertion, proven at exact baseline.
- FROZEN_POLICY_CONFLICT: overflow gate on existing Work Panel classes, exact same baseline cause.
- RESOLVED: nullable-dependent qualification busy-state guard corrected; O1-dependent AccessControl fixture/assertions updated and all 567 tests pass.
- INTRODUCED: none remaining in required checks. No authority/infrastructure or decision blocker. No changes to Handover SLA, team queues, claim limits, Communications or Customer qualification semantics.

## I. O1 acceptance

| Acceptance | Result |
| --- | --- |
| Manual creation actor-owned | PASS |
| External creation unassigned | PASS |
| No fake queue owner | PASS |
| Unassigned persisted | PASS |
| Unassigned projected | PASS |
| AssignmentState query | PASS |
| Queue access secure | PASS |
| Workspace isolation | PASS |
| Existing assigned Leads regression | PASS |
| Frontend nullable owner safety | PASS |
| Claim unavailable | PASS |
| Assign unavailable | PASS |
| Handover unavailable | PASS |
| No introduced regression | PASS |
| Scope pollution removed | PASS |
| Frozen Work Panel preserved | PASS |

**LEAD_OWNERSHIP_O1_PASS**. Remaining failed checks are proven baseline/frozen conflict. This report is local evidence, not independent CI/reviewer attestation. STOP for review; no O2–O7 work.
