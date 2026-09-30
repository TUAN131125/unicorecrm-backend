# LEAD_OWNERSHIP_O2_PASS

Local implementation and verification, 2026-09-30. O2 remains uncommitted. No push. No independent reviewer/CI attestation and no connected browser E2E claim. Later phases are not started.

## A. Git

**FRONTEND**
- Working directory: `D:/Project_All/UnicoreCRM/frontend/unicorecrm-web`
- Branch: `main`
- O1 frozen commit / O2 starting SHA / O2 ending HEAD: `40a758828574c7f28191816a85686c244ec5c34b`
- Working tree: O2 changes uncommitted; existing user work preserved.

**BACKEND**
- Working directory: `D:/Project_All/UnicoreCRM/backend`
- Branch: `master`
- O1 frozen commit / O2 starting SHA / O2 ending HEAD: `ca824392e000c0ec15b707579cc9f29d0f33569c`
- Working tree: O2 changes uncommitted; existing user work preserved.

O1 was compared against the reviewed post-cleanup status, branch, HEAD, stat and complete diff before freezing. Original baseline frontend `1c0f02510510bccb01078c0cb8d6410a274154f6` remains at `D:/Project_All/UnicoreCRM-o1-baseline`. Baseline source is unchanged; dependency junction and test-output artifacts are not source modifications.

## B. Claim contract

- Operation ID: `claimLeadFromQueue`.
- HTTP: POST `/workflows/lead-queue/{leadId}/claim`, the existing canonical workflow path.
- Request fields: none; closed `{}` rejects ownerId, targetOwnerId, memberId, workspaceId and reason (422).
- Idempotency: workspace/actor/operation/target/key scope; fingerprint includes target and expected version. Exact replay returns the same command/version/result with REPLAYED outcome, no new effects. Changed fingerprint is 409.
- Concurrency: required If-Match; serializable transaction with workspace-qualified Lead UPDLOCK/HOLDLOCK before idempotency lookup. Stale unassigned version is 412 VERSION_CONFLICT.
- Success: 200 authoritative LeadMutationResponse, ownerId and ScopeOwnerId become the authenticated member, version increments once. Lifecycle, qualification, Tasks and Activities remain unchanged.
- Already assigned, including same actor with a fresh key: 409 LEAD_QUEUE_CLAIM_CONFLICT; no owner details disclosed.
- Race loser: 409 LEAD_QUEUE_CLAIM_CONFLICT. Archived: 409 LEAD_ALREADY_ARCHIVED. Missing tenant record is denied without disclosure.

## C. Authorization

- Queue requires `leads.read` + `leads.queue.read`. Claim additionally requires `leads.claim`, writable owner field and an authenticated active workspace member.
- WORKSPACE and OWN allow Queue and Claim. OWN null-owned records remain read-only for ordinary commands; explicit descriptor Claim authority is the sole exception.
- TEAM and CUSTOM data scopes fail closed. CUSTOM data scope is distinct from a custom role with explicit capabilities and OWN/WORKSPACE policy.
- Backend protected Workspace Owner full-catalog projection gains Claim through the existing exact previous-set migration, never role-name inference.
- Frontend default workspaceAdministrator full admitted catalog and salesManager template receive Claim. SalesManager TEAM scope still fails closed until supported scope authority exists. SalesRepresentative was not given Queue or Claim automatically.
- Custom roles receive neither new capability automatically. The real harness first verifies absence of Claim on its custom role, then explicitly grants it.
- Effective access allows lead.claim-from-queue on eligible unassigned records while lead.update and canUpdate remain denied for OWN Queue. Profile replacement still cannot transfer ownership.

## D. Real-host race evidence

Actual HTTP against ApiHost and isolated SQL LocalDB; actors use distinct authenticated account tokens.

```json
{
  "TaskStatusesVerified": [
    "OPEN",
    "COMPLETED",
    "CANCELLED"
  ],
  "ActivityChanges": 0,
  "Status": "PASS",
  "Database": "UnicoreCRM_O2Claim_20260930_a83f",
  "LeadId": "lead_55367760626d491ca9f99fb659cdf34e",
  "ActorA": "mem_210d9b6ff1cb4f4cb8be4177967e51a6",
  "ActorB": "mem_664dd8c0387b44c69e655598f58becd1",
  "ActorAResult": 409,
  "ActorBResult": 200,
  "FinalOwner": "mem_664dd8c0387b44c69e655598f58becd1",
  "Version": "1",
  "SuccessfulAuditCount": "1",
  "TaskChanges": 0,
  "Checks": [
    "Proof source guard private constructor=PASS",
    "Proof source guard single issuer construction=PASS",
    "Proof source guard no generic requirement or decision factory=PASS",
    "Proof source guard hard-coded leads.create=PASS",
    "Proof source guard exact trusted context binding=PASS",
    "Proof source guard delegated subject matches trusted member=PASS",
    "Ingress source guard dedicated authorizer only=PASS",
    "Admission source guard generic decision and nullable access blocked=PASS",
    "Admission source guard proof cannot rebind Workspace member or owner=PASS",
    "Execution source guard closed non-nullable admission=PASS",
    "Sender authority source guard server binding and closed payload=PASS",
    "Identity sign-in=200",
    "Identity session=200",
    "Workspace list=200",
    "Workspace bootstrap=200",
    "AccessControl authorization=200",
    "Tasks create=201",
    "Tasks get=200",
    "Leads create=201",
    "Leads get=200",
    "Deals create=201",
    "Deals get=200",
    "Inbound webhook valid signed delivery=200",
    "Inbound webhook server-assigned Lead identity=PASS",
    "Inbound webhook one canonical leads.create authorization decision=PASS",
    "Assignment foundation interactive actor and persisted external null=PASS",
    "WORKSPACE without queue permission queue list=200",
    "WORKSPACE without queue permission detail=404",
    "WORKSPACE without queue permission effective access=200",
    "WORKSPACE without queue permission unfiltered list=200",
    "WORKSPACE without queue permission search=200",
    "WORKSPACE with queue permission queue list=200",
    "WORKSPACE with queue permission detail=200",
    "WORKSPACE with queue permission effective access=200",
    "WORKSPACE with queue permission unfiltered list=200",
    "WORKSPACE with queue permission search=200",
    "Assigned query=200",
    "Contradictory assignment filter=422",
    "Invalid assignment vocabulary=422",
    "OWN with queue permission queue list=200",
    "OWN with queue permission detail=200",
    "OWN with queue permission effective access=200",
    "OWN with queue permission unfiltered list=200",
    "OWN with queue permission search=200",
    "OWN queue read cannot update=404",
    "OWN without queue permission queue list=200",
    "OWN without queue permission detail=404",
    "OWN without queue permission effective access=200",
    "OWN without queue permission unfiltered list=200",
    "OWN without queue permission search=200",
    "Team with queue permission queue list=200",
    "Team with queue permission detail=404",
    "Team with queue permission effective access=200",
    "Team with queue permission unfiltered list=200",
    "Team with queue permission search=200",
    "Custom with queue permission queue list=200",
    "Custom with queue permission detail=404",
    "Custom with queue permission effective access=200",
    "Custom with queue permission unfiltered list=200",
    "Custom with queue permission search=200",
    "Interactive rejects queue=403",
    "External rejects queue=400",
    "Interactive rejects unassigned=403",
    "External rejects unassigned=400",
    "Interactive rejects system=403",
    "External rejects system=400",
    "Interactive rejects sales_queue=403",
    "External rejects sales_queue=400",
    "Interactive rejects member_someone_else=403",
    "External rejects member_someone_else=400",
    "Assignment foundation cross-workspace list/detail/mutation=PASS",
    "Inbound webhook missing signature=401",
    "Inbound webhook invalid signature=401",
    "Inbound webhook tampered body=401",
    "Inbound webhook stale timestamp=401",
    "Inbound webhook same delivery replay=200",
    "Inbound webhook delivery conflict=409",
    "Inbound webhook Workspace spoof=400",
    "Leads interested Products gap=400",
    "Inbound webhook authorization denial=403",
    "Inbound webhook authorization denial no Lead business mutation=PASS",
    "Inbound webhook denied canonical leads.create authorization decision=PASS",
    "Inbound webhook invalid delegated member=403",
    "Inbound webhook disabled binding=404",
    "Inbound webhook unknown binding=404",
    "Inbound webhook post-Lead-commit recovery=200",
    "Inbound webhook concurrent duplicate=200/200, one Lead",
    "Inbound webhook sender authority headers ignored=200",
    "Inbound webhook sender cannot choose Workspace or delegated subject=PASS",
    "Inbound webhook Inbox persistence=PASS",
    "Inbound webhook integration actor audit=PASS",
    "Inbound webhook delivery/idempotency identity negative=PASS",
    "Queue hidden required owner fails closed=403",
    "Detail hidden required owner fails closed=403",
    "Queue profile preserves null owner=200",
    "Queue hidden phone detail=200",
    "Queue hidden phone search=200",
    "Queue cursor page=200",
    "Queue cursor page=200",
    "Queue cursor page=200",
    "Profile cannot assign Queue=403",
    "Assigned profile regression=200",
    "Profile cannot unassign existing owner=403",
    "Assigned lifecycle regression=200",
    "Assigned archive regression=200",
    "Assignment foundation field-security and cursor invariants=PASS",
    "External fixture Claim A=200",
    "External fixture Claim B=200",
    "External fixture Claim Race=200",
    "External fixture Claim Archived=200",
    "CLAIM-06 no Claim=403",
    "CLAIM missing leads.read=403",
    "Queue requires normal read=403",
    "CLAIM missing leads.queue.read=403",
    "TEAM Claim fail closed=403",
    "CUSTOM Claim fail closed=403",
    "CLAIM-03 closed body ownerId=422",
    "CLAIM-03 closed body targetOwnerId=422",
    "CLAIM-03 closed body memberId=422",
    "CLAIM-03 closed body workspaceId=422",
    "CLAIM-03 closed body reason=422",
    "CLAIM-05 archived=409",
    "CLAIM-09 inactive=403",
    "CLAIM-11 version=412",
    "Claim owner field ReadOnly=403",
    "Claim owner field Hidden=403",
    "OWN Queue effective access=200",
    "CLAIM-01 OWN success=200",
    "CLAIM-12 replay=200",
    "Changed key fingerprint rejected=409",
    "CLAIM-04 assigned same actor=409",
    "Register actor B=201",
    "Sign in actor B=200",
    "WORKSPACE Claim success=200",
    "Permanent profile cannot assign after Claim=403"
  ]
}
```

Task evidence hashes the complete ordered table JSON (all columns including nulls) with SHA-256 before and after; fixtures linked to the contested Lead cover OPEN, COMPLETED and CANCELLED. Activities are similarly compared. SQL verifies one successful audit and one ownership event with previous=null/new=actor evidence.

## E. Frontend

- Sales Queue: existing Lead List Unassigned saved view (`Chưa phân công`); existing connected Queue route reuses the same list. Legacy demo grouping does not mount in connected mode.
- Query: backend assignmentState=UNASSIGNED, server search/filter/count/pagination. Raw server page is rendered without local null-owner filtering. Contradictory owner filter is preserved for server validation.
- Action: direct Nhận Lead / Claim Lead button on eligible table/mobile rows, no modal. Availability and capabilities are explicit.
- Pending: only the clicked row is busy/disabled; duplicate submission is ignored.
- Success: wait for authoritative projection and query invalidation, notify then refresh. No optimistic ownership.
- Conflict: localized notification and authoritative refresh; failed conflict refresh keeps the row blocked.
- Network failure: formatted error and safe retry; original idempotency key and row version are retained. No connected demo/local fallback.
- Work Panel layout/source unchanged. Kanban component unchanged; Unassigned Queue is a table/mobile view.

## F. Future operations

| Operation | Connected availability |
|---|---|
| claimLeadFromQueue | AVAILABLE |
| assignLeadOwner | UNAVAILABLE |
| assignLeadOwnerBatch | UNAVAILABLE |
| handoverLeadWithTasks | UNAVAILABLE |

Unavailable future commands issue zero transport requests; no new backend routes, buttons, batch coordinator or handover runtime.

## G. Every O2 file and dependency

| Repository | Path | Reason / business behavior |
|---|---|---|
| backend | `backend-work/review/lead-queue-claim.json` | Scoped O2 authority, implementation/evidence documentation; future V1 semantics remain TARGET. |
| backend | `backend-work/review/lead-queue-claim.md` | Scoped O2 authority, implementation/evidence documentation; future V1 semantics remain TARGET. |
| backend | `scripts/verify-lead-queue-claim.ps1` | Real-host/SQL O2 authority, replay, two-actor race, audit/version and Tasks/Activities preservation; includes unchanged O1 webhook harness. |
| backend | `src/UnicoreCRM.Crm/Leads/Application/ClaimLeadFromQueue/Handler.cs` | Authenticated actor-bound atomic Claim with explicit authorization, replay and audit. |
| backend | `src/UnicoreCRM.Crm/Leads/Application/Common/LeadCommandSupport.cs` | Optional ownership event evidence for Claim; existing command payload/default behavior unchanged. |
| backend | `src/UnicoreCRM.Crm/Leads/Application/Common/LeadsPersistence.cs` | Narrow Claim load with update lock to serialize distinct actor/key races; no schema/model change. |
| backend | `src/UnicoreCRM.Crm/Leads/Application/ProvideLeadRecordAccessFacts/LeadRecordAccessFactProvider.cs` | Explicit unassigned Claim command authority; preserve ordinary OWN null-record read-only enforcement. |
| backend | `src/UnicoreCRM.Crm/Leads/Contracts/LeadContracts.cs` | Canonical closed-body Claim route and handler registration. |
| backend | `src/UnicoreCRM.Crm/Leads/Contracts/LeadsEndpoints.cs` | Canonical closed-body Claim route and handler registration. |
| backend | `src/UnicoreCRM.Crm/Leads/Domain/Lead.cs` | Null→actor owner and ScopeOwner update with one version increment; lifecycle/qualification untouched. |
| backend | `src/UnicoreCRM.Crm/Leads/Infrastructure/Persistence/EfLeadsPersistence.cs` | Narrow Claim load with update lock to serialize distinct actor/key races; no schema/model change. |
| backend | `src/UnicoreCRM.Crm/Leads/LeadsModule.cs` | Canonical closed-body Claim route and handler registration. |
| backend | `src/UnicoreCRM.Platform/AccessControl/Application/Common/RecordAccessEvaluator.cs` | Explicit unassigned Claim command authority; preserve ordinary OWN null-record read-only enforcement. |
| backend | `src/UnicoreCRM.Platform/AccessControl/Application/Common/WorkspaceCapabilityPolicy.cs` | Admit explicit leads.claim through existing catalog/default-template policy, not role-name authorization. |
| backend | `src/UnicoreCRM.Platform/AccessControl/Application/EvaluateEffectiveRecordAccess/Handler.cs` | Explicit unassigned Claim command authority; preserve ordinary OWN null-record read-only enforcement. |
| backend | `src/UnicoreCRM.Platform/AccessControl/Application/ProvisionInitialWorkspaceAccess/InitialWorkspaceAccessPolicy.cs` | Exact O1 full-catalog predecessor recognized for protected owner upgrade; no custom auto-grant. |
| backend | `src/UnicoreCRM.Platform/AccessControl/Contracts/RecordAccessFacts.cs` | Explicit unassigned Claim command authority; preserve ordinary OWN null-record read-only enforcement. |
| frontend | `docs/api/api-operation-catalog.json` | Repository generator output from the canonical O2 Claim contract; other modules change only deterministic spec metadata, not runtime behavior. |
| frontend | `docs/api/generated-client-manifest.json` | Repository generator output from the canonical O2 Claim contract; other modules change only deterministic spec metadata, not runtime behavior. |
| frontend | `docs/api/openapi.json` | Claim capability and closed empty body; remove obsolete reason/lifecycle advance contract. |
| frontend | `docs/api/openapi.sha256` | Repository generator output from the canonical O2 Claim contract; other modules change only deterministic spec metadata, not runtime behavior. |
| frontend | `docs/api/operation-coverage-ledger.json` | Repository generator output from the canonical O2 Claim contract; other modules change only deterministic spec metadata, not runtime behavior. |
| frontend | `docs/architecture/effective-record-access.md` | Document the narrow Claim exception without granting ordinary OWN Queue mutations. |
| frontend | `docs/architecture/lead-api-boundary.md` | Scoped O2 authority, implementation/evidence documentation; future V1 semantics remain TARGET. |
| frontend | `docs/backend-readiness/command-registry.json` | Scoped O2 authority, implementation/evidence documentation; future V1 semantics remain TARGET. |
| frontend | `docs/backend-readiness/concurrency-policy.md` | Scoped O2 authority, implementation/evidence documentation; future V1 semantics remain TARGET. |
| frontend | `docs/backend-readiness/idempotency-policy.md` | Scoped O2 authority, implementation/evidence documentation; future V1 semantics remain TARGET. |
| frontend | `docs/backend-readiness/lead-ownership-distribution-decision.json` | Scoped O2 authority, implementation/evidence documentation; future V1 semantics remain TARGET. |
| frontend | `docs/backend-readiness/lead-ownership-distribution-decision.md` | Scoped O2 authority, implementation/evidence documentation; future V1 semantics remain TARGET. |
| frontend | `docs/backend-readiness/operation-authorization-matrix.json` | Scoped O2 authority, implementation/evidence documentation; future V1 semantics remain TARGET. |
| frontend | `docs/backend-readiness/operation-authorization-matrix.md` | Scoped O2 authority, implementation/evidence documentation; future V1 semantics remain TARGET. |
| frontend | `docs/backend-readiness/workflow-ownership.md` | Scoped O2 authority, implementation/evidence documentation; future V1 semantics remain TARGET. |
| frontend | `docs/quality/lead-ownership-distribution-acceptance.md` | Scoped O2 authority, implementation/evidence documentation; future V1 semantics remain TARGET. |
| frontend | `docs/quality/repository-inventory.json` | Required repository inventory regeneration registers O2 hook/component/test and updated document hashes; no product behavior. |
| frontend | `docs/quality/repository-inventory.md` | Required repository inventory regeneration registers O2 hook/component/test and updated document hashes; no product behavior. |
| frontend | `scripts/quality/quality-pipeline.json` | Register exactly one new O2 gate; update exact gate count 336→337. Required pipeline dependency, no weakened assertions. |
| frontend | `src/guidance/content/crm/screens.ts` | Required Lead List bilingual guidance for direct Claim, permission and conflict behavior; no unrelated AI guidance changes. |
| frontend | `src/modules/leads/README.md` | Scoped O2 authority, implementation/evidence documentation; future V1 semantics remain TARGET. |
| frontend | `src/modules/leads/application/commands/leadApiCommands.ts` | Use authoritative server row version and stable retry intent; await projection/invalidation. |
| frontend | `src/modules/leads/application/leadOperationAvailability.ts` | Open Claim only through generated canonical adapter with empty body; future operations remain unavailable. |
| frontend | `src/modules/leads/application/ports/LeadApiRuntime.ts` | Open Claim only through generated canonical adapter with empty body; future operations remain unavailable. |
| frontend | `src/modules/leads/infrastructure/http/LeadHttpCommandAdapter.ts` | Open Claim only through generated canonical adapter with empty body; future operations remain unavailable. |
| frontend | `src/modules/leads/infrastructure/http/createLeadConnectedApiRuntime.ts` | Open Claim only through generated canonical adapter with empty body; future operations remain unavailable. |
| frontend | `src/modules/leads/presentation/components/LeadClaimButton.tsx` | Direct localized Claim action, pending accessibility, no modal. |
| frontend | `src/modules/leads/presentation/components/LeadListResults.tsx` | Integrate authorized server Unassigned view and direct table/mobile Claim; no Work Panel/Kanban layout changes. |
| frontend | `src/modules/leads/presentation/components/LeadMobileCardList.tsx` | Integrate authorized server Unassigned view and direct table/mobile Claim; no Work Panel/Kanban layout changes. |
| frontend | `src/modules/leads/presentation/components/LeadSavedViewSelector.tsx` | Integrate authorized server Unassigned view and direct table/mobile Claim; no Work Panel/Kanban layout changes. |
| frontend | `src/modules/leads/presentation/components/LeadTable.tsx` | Integrate authorized server Unassigned view and direct table/mobile Claim; no Work Panel/Kanban layout changes. |
| frontend | `src/modules/leads/presentation/hooks/useLeadActions.ts` | Integrate authorized server Unassigned view and direct table/mobile Claim; no Work Panel/Kanban layout changes. |
| frontend | `src/modules/leads/presentation/hooks/useLeadQueueClaim.ts` | Row-level pending, stable network retry key/version, conflict refresh and blocked stale row recovery. |
| frontend | `src/modules/leads/presentation/hooks/useLeadSavedViews.ts` | Integrate authorized server Unassigned view and direct table/mobile Claim; no Work Panel/Kanban layout changes. |
| frontend | `src/modules/leads/presentation/model/leadSavedViewPreferences.ts` | Integrate authorized server Unassigned view and direct table/mobile Claim; no Work Panel/Kanban layout changes. |
| frontend | `src/modules/leads/presentation/pages/LeadListPage.tsx` | Integrate authorized server Unassigned view and direct table/mobile Claim; no Work Panel/Kanban layout changes. |
| frontend | `src/modules/leads/presentation/pages/LeadQueuePage.tsx` | Reuse authoritative List in connected Queue route; isolate legacy demo surface and adapt removed reason parameter. |
| frontend | `src/modules/leads/runtime/createLeadDemoApiRuntime.ts` | Compile/runtime contract dependency: empty Claim input and real demo member ownership, no fake actor or lifecycle advance; isolated from connected mode. |
| frontend | `src/platform/access-control/domain/capabilityCatalog.ts` | Admit explicit leads.claim through existing catalog/default-template policy, not role-name authorization. |
| frontend | `src/platform/access-control/domain/roleTemplates.ts` | Admit explicit leads.claim through existing catalog/default-template policy, not role-name authorization. |
| frontend | `src/platform/api/catalog/generatedApiOperationCatalog.ts` | Repository generator output from the canonical O2 Claim contract; other modules change only deterministic spec metadata, not runtime behavior. |
| frontend | `src/platform/api/contracts/generatedOpenApiRuntimeContract.ts` | Repository generator output from the canonical O2 Claim contract; other modules change only deterministic spec metadata, not runtime behavior. |
| frontend | `src/platform/api/generated/accessGovernanceApi.ts` | Repository generator output from the canonical O2 Claim contract; other modules change only deterministic spec metadata, not runtime behavior. |
| frontend | `src/platform/api/generated/aiApi.ts` | Repository generator output from the canonical O2 Claim contract; other modules change only deterministic spec metadata, not runtime behavior. |
| frontend | `src/platform/api/generated/commercialApi.ts` | Repository generator output from the canonical O2 Claim contract; other modules change only deterministic spec metadata, not runtime behavior. |
| frontend | `src/platform/api/generated/crmConfigurationApi.ts` | Repository generator output from the canonical O2 Claim contract; other modules change only deterministic spec metadata, not runtime behavior. |
| frontend | `src/platform/api/generated/financialApi.ts` | Repository generator output from the canonical O2 Claim contract; other modules change only deterministic spec metadata, not runtime behavior. |
| frontend | `src/platform/api/generated/financialConfigurationApi.ts` | Repository generator output from the canonical O2 Claim contract; other modules change only deterministic spec metadata, not runtime behavior. |
| frontend | `src/platform/api/generated/identityApi.ts` | Repository generator output from the canonical O2 Claim contract; other modules change only deterministic spec metadata, not runtime behavior. |
| frontend | `src/platform/api/generated/integrationConfigurationApi.ts` | Repository generator output from the canonical O2 Claim contract; other modules change only deterministic spec metadata, not runtime behavior. |
| frontend | `src/platform/api/generated/productConfigurationApi.ts` | Repository generator output from the canonical O2 Claim contract; other modules change only deterministic spec metadata, not runtime behavior. |
| frontend | `src/platform/api/generated/receivablesApi.ts` | Repository generator output from the canonical O2 Claim contract; other modules change only deterministic spec metadata, not runtime behavior. |
| frontend | `src/platform/api/generated/studioQuickSetupApi.ts` | Repository generator output from the canonical O2 Claim contract; other modules change only deterministic spec metadata, not runtime behavior. |
| frontend | `src/platform/api/generated/workspaceBootstrapApi.ts` | Repository generator output from the canonical O2 Claim contract; other modules change only deterministic spec metadata, not runtime behavior. |
| frontend | `src/platform/api/generated/workspaceConfigurationApi.ts` | Repository generator output from the canonical O2 Claim contract; other modules change only deterministic spec metadata, not runtime behavior. |
| frontend | `tests/quality/architecture/check-quality-pipeline.mts` | Register exactly one new O2 gate; update exact gate count 336→337. Required pipeline dependency, no weakened assertions. |
| frontend | `tests/quality/integration/check-lead-ownership-distribution-contracts.mts` | Remove only obsolete O1 Claim-unavailable assertion now superseded by O2 positive gate; future-operation zero-HTTP assertions remain. |
| frontend | `tests/quality/integration/check-lead-queue-claim.mts` | O2 HTTP fixture/JSDOM authority, pending, retry intent, authoritative refresh and future containment tests. |

Outside the named Leads/AccessControl/OpenAPI areas: quality pipeline/count/inventory metadata is a direct gate registration dependency; bilingual guidance is required for the new action. Deterministic generated non-Lead clients carry only spec fingerprint changes. No unrelated source remediation was retained. Pre-existing backend AI plans/review artifacts remain untracked and untouched.

## H. Tests

### Backend / real Claim race / AccessControl

| Command | Result | Evidence |
|---|---|---|
| dotnet build --no-restore | PASS | o2-backend-build.log; 0 warnings / 0 errors |
| ./scripts/verify-lead-queue-claim.ps1 -DatabaseName UnicoreCRM_O2Claim_20260930_a83f | PASS | o2-claim-real.log; includes frozen O1 real inbound harness and real Claim race |
| ./scripts/verify-access-control-record-access.ps1 -DatabaseName UnicoreCRM_O2Access_20260930_b94g | PASS | o2-access.log; 567 PASS / 0 FAIL |
| dotnet ef migrations has-pending-model-changes --project src/UnicoreCRM.Crm --context LeadsDbContext --no-build | PASS | o2-model.log; no model changes |
| git diff --check (backend) | PASS | final check |

The initial AccessControl invocation after dot-sourced Claim inherited bootstrap credentials and failed sign-in. Running the unchanged harness in a separate process resolved the verification environment; no authentication source was altered.

### Frontend focused

| Command | Result |
|---|---|
| npm run quality:gate -- --gate quality.lead-queue-claim | PASS |
| npm run quality:gate -- --gate quality.lead-ownership-distribution-contracts | PASS |
| npm run quality:gate -- --gate quality.lead-api-boundary | PASS |
| npm run quality:gate -- --gate quality.lead-lifecycle-contracts | PASS |
| npm run quality:gate -- --gate quality.lead-business-rules-contracts | PASS |
| npm run quality:gate -- --gate quality.task-activity-api-boundary | PASS |
| npm run quality:gate -- --gate quality.lead-work-panel | PASS |
| npm run quality:gate -- --gate quality.lead-detail-surfaces | PASS |
| npm run quality:gate -- --gate quality.record-ownership-contracts | PASS |
| npm run quality:gate -- --gate quality.workspace-isolation-contracts | PASS |
| npm run quality:gate -- --gate quality.frontend-backend-separation | PASS |
| npm run quality:gate -- --gate quality.connected-business-operation-availability | PASS |
| npm run quality:gate -- --gate quality.quality-pipeline | PASS |
| npm run quality:gate -- --gate quality.repository-inventory | PASS |
| npm run quality:gate -- --gate quality.api-management-boundaries | PASS |
| npm run quality:gate -- --gate quality.lead-completion-api-boundary | PASS |
| npm run quality:gate -- --gate quality.leads | PASS |
| npm run quality:gate -- --gate quality.lead-webhook-connector | PASS |
| npm run quality:gate -- --gate quality.lead-form-recovery-contracts | PASS |

### Frontend general

| Command | Result |
|---|---|
| npm run api:generate | PASS |
| npm run lint | PASS |
| npm run typecheck | PASS |
| npm run api:check | PASS |
| npm run build | PASS |
| npm run repo:inventory | PASS |
| npm run repo:check | PASS |
| git diff --check (frontend) | PASS |

### Full suite and baseline evidence

`npm run test`: FAIL, 33/151 before quality.lead-data-safety-contracts, unchanged assertion “Export actions must be hidden without leads.export”. Exact original baseline: FAIL 33/149 with same cause; reviewed O1: FAIL 33/150 with same cause. Classification PRE_EXISTING. No unrelated export repair.

| Gate | Baseline | O2 | Same material cause | Classification |
|---|---|---|---|---|
| quality.page-description-policy | FAIL | FAIL | ListStatePanel supporting description under structural title; `baseline-page-description-policy.log` and `o2-focused-page-description-policy.log` | PRE_EXISTING |
| quality.form-runtime-sizing | FAIL | FAIL | RelationshipQuickActionModal noncanonical size={size}; `baseline-form-runtime-sizing.log` and `o2-focused-form-runtime-sizing.log` | PRE_EXISTING |
| quality.crm-presentation-contracts | FAIL | FAIL | ContactListPage shared bulk-action contract; `baseline-crm-presentation-contracts.log` and `o2-focused-crm-presentation-contracts.log` | PRE_EXISTING |
| quality.metric-drilldown-runtime | FAIL | FAIL | Metric click does not open fixture drawer; `baseline-metric-drilldown-runtime.log` and `o2-focused-metric-drilldown-runtime.log` | PRE_EXISTING |
| quality.studio-route-runtime | FAIL | FAIL | AI_CONFIGURATION_RUNTIME_UNAVAILABLE; `baseline-studio-route-runtime.log` and `o2-focused-studio-route-runtime.log` | PRE_EXISTING |
| quality.overflow-resilience | FAIL | FAIL | Frozen LeadWorkPanel existing truncate/line-clamp-2; `baseline-overflow-resilience.log` and `o2-focused-overflow-resilience.log` | FROZEN_POLICY_CONFLICT |
| quality.presentation-responsibility | FAIL | FAIL | ContactDetailPage stateful presentation; `o2-baseline-presentation-responsibility.log` and `o2-focused-presentation-responsibility.log` | PRE_EXISTING |
| quality.mutation-command-authority | FAIL | FAIL | Pinned blocked presentation inventory mismatch; `o2-baseline-mutation-command-authority.log` and `o2-focused-mutation-command-authority.log` | PRE_EXISTING |
| quality.guidance-contracts | FAIL | FAIL | Existing SETTINGS_AI metadata and route/docs coverage mismatch; `o2-baseline-guidance-contracts.log` and `o2-focused-guidance-contracts.log` | PRE_EXISTING |

`quality.backend-readiness`: FAIL due four pre-existing ignored test-results/**/error-context.md artifacts with no document-status entries. Clean source baseline PASS (137 documents); identical byte-hashed pre-existing ignored artifacts copied into the baseline yield the same FAIL. Classification PRE_EXISTING environment/policy conflict, not a claim that clean baseline failed. See o2-baseline-backend-readiness-parity.log and o2-backend-readiness-environment-parity.json. No generic document-status assertion weakened.

### Browser / JSDOM / connected E2E

- JSDOM + HTTP fixtures: PASS via quality.lead-queue-claim, exercises actual adapter/application/controller and pending/recovery behavior.
- Backend connected real HTTP + SQL: PASS via real Claim harness, actual two actors and durable evidence.
- Connected browser E2E: NOT RUN; not required as O7 and not represented as passing.

All command logs and exact baseline evidence: `D:/Project_All/UnicoreCRM-o1-cleanup-evidence`.

## I. O2 acceptance

| Acceptance | Result |
|---|---|
| Unassigned queue server query | PASS |
| Queue permission separation | PASS |
| Claim capability explicit | PASS |
| Claim target = authenticated actor | PASS |
| Arbitrary target impossible | PASS |
| Claim race single winner | PASS |
| Claim idempotent replay | PASS |
| Claim audit exactly once | PASS |
| Tasks unchanged | PASS |
| Archived Lead rejected | PASS |
| Cross-workspace denied | PASS |
| Custom roles not auto-granted | PASS |
| Frontend direct Claim action | PASS |
| Success authoritative refresh | PASS |
| Conflict authoritative refresh | PASS |
| No connected fallback | PASS |
| Assign unavailable | PASS |
| Handover unavailable | PASS |
| O1 regression | PASS |
| Frozen Work Panel preserved | PASS |
| No introduced regression | PASS |

O1 regression PASS means the inherited foundation checks continue to pass. No introduced regression is supported by focused/general checks and cause-matched baseline failures; it is not a claim that the entire repository suite is green.

**Verdict: LEAD_OWNERSHIP_O2_PASS — local acceptance, pending user review.**

STOP. No O3/O4/O5/O6/O7 continuation; no O2 commit or push.
