# O4_SECURITY_RECOVERY_CORRECTIVE_REPORT

Backend-only implementation and executed evidence. Frontend and API generation remain main-owned.

## BASELINE

Backend HEAD: `695f4f34d0171105c39d053c00ea6a352f64407d`, unchanged.
Frontend requested baseline: `d009b6697cdf5848eaf7a8c3ea0a42f6d21181db`; frontend was not edited or independently verified by this worker.
Committed/pushed: **NO**. Backend build/verifier lane: **RELEASED**.

Backend patch files changed: **8**, plus this untracked local evidence report (9 files in git status). Frontend files changed by this worker: **0**.

- [src/UnicoreCRM.Workflows/Atomic/Contracts/LeadHandoverWorkflow.cs](D:/Project_All/UnicoreCRM/backend/src/UnicoreCRM.Workflows/Atomic/Contracts/LeadHandoverWorkflow.cs)
- [src/UnicoreCRM.Workflows/Atomic/Application/HandoverLead/Handler.cs](D:/Project_All/UnicoreCRM/backend/src/UnicoreCRM.Workflows/Atomic/Application/HandoverLead/Handler.cs)
- [src/UnicoreCRM.Workflows/Atomic/Domain/LeadHandoverAnchor.cs](D:/Project_All/UnicoreCRM/backend/src/UnicoreCRM.Workflows/Atomic/Domain/LeadHandoverAnchor.cs)
- [src/UnicoreCRM.Crm/Leads/Contracts/LeadHandoverParticipant.cs](D:/Project_All/UnicoreCRM/backend/src/UnicoreCRM.Crm/Leads/Contracts/LeadHandoverParticipant.cs)
- [src/UnicoreCRM.Crm/Leads/Application/HandoverLead/Participant.cs](D:/Project_All/UnicoreCRM/backend/src/UnicoreCRM.Crm/Leads/Application/HandoverLead/Participant.cs)
- [scripts/verify-lead-handover.ps1](D:/Project_All/UnicoreCRM/backend/scripts/verify-lead-handover.ps1)
- [scripts/LeadHandoverRealVerifier/Program.cs](D:/Project_All/UnicoreCRM/backend/scripts/LeadHandoverRealVerifier/Program.cs)
- [scripts/LeadHandoverRealVerifier/README.md](D:/Project_All/UnicoreCRM/backend/scripts/LeadHandoverRealVerifier/README.md)

## P1-A SAFE COMMAND RECEIPT

Full LeadDocument removed from LeadHandoverResult. Fresh completion stores only the five admitted Task/SLA receipt fields. Completed historical ResponseJson can contain an obsolete full Lead; typed deserialization discards it, and the real-host regression seeds displayName/phone/email and proves no lead property or protected values leave the response.

Initial OWN A-to-B execution and exact completed replay: PASS. Receipt equality and Task SQL hashes prove no mutation or duplicate takeover during replay. Current leads.assign/tasks.assign/tasks.create disclosure denials, restoration, and hidden Task dueAt remain covered. No post-transfer Lead/Task record scope is reapplied to the receipt. Preparation.Authorization remains for Lead record-scope and ownerId write admission. Obsolete participant Project and Lead response projection are removed.

Result: PASS for backend scope. Frontend authoritative refresh and stale-data eviction: main-owned, NOT VERIFIED by this worker.

## P1-B AMBIGUOUS 403

Backend capability denial and restoration of exact completed replay: PASS. Frontend retained key/version/payload and fresh-command denial semantics: main-owned, NOT VERIFIED by this worker.

## P1-C RECOVERY FAIRNESS

Denied due nonterminal workflows receive NextRetryAt=now+1 minute, UpdatedAt=now, LastErrorCategory=TRANSIENT, LastErrorCode=RECOVERY_ACCESS_DENIED. No schema change. Deferral re-fetches current coordination state and guards nonterminal/due/no-valid-lease; EF rowversion prevents overwriting concurrent progress. Concurrent progress wins without stage regression.

Scan ordering: UpdatedAt then ScopeKey. Valid active leases are excluded before Take(10). Deferral still rechecks all guards after service authorization. SingleOrDefault safely handles a vanished anchor.

Permanent real-host fixture: an even-older valid lease plus ten denied due SQL coordination anchors in workspace A precede a real authorized crash-after-Tasks workflow in workspace B. Scan one defers all ten denied rows; scan two completes the authorized workflow. Leased-row rowversion stays unchanged. The initial denial/restoration scenario uses the same real workflow and advances NextRetryAt beyond the backoff in isolated SQL. Assertions cover future bounded retry, error fields, no hot loop, unchanged stage/active reservation, unchanged Task hash, no compensation/duplicates, and frozen SLA/dueAt. The two-context real SQL probe proves stale deferral loses to a competing worker lease; no mock replaces concurrency evidence.

Result: PASS.

## P2 MONOTONIC LEAD PROJECTION

Frontend shared projection boundary and guards: main-owned, NOT VERIFIED by this worker. Backend Handover-specific Lead projection removed: PASS.

## MIGRATIONS

Published migrations/designers edited: NO. All five affected EF models unchanged. Forward-only rollback note added to the existing real-host verifier README. Automatic schema Down is unsupported for production rollback because obsolete authority and historical OpenTaskPolicy cannot be reconstructed; restore application/database through the deployment rollback plan. Migration Down guards checked statically; rollback not executed.

Result: PASS within corrective backend scope.

## BACKEND VERIFICATION

All commands ran sequentially in the owned backend lane; each final verifier exited successfully.

| Check | Executed result | Evidence |
|---|---|---|
| Full solution build and real-host verifier build | 0 warnings, 0 errors | [o4-corrective-verifier.log](D:/Project_All/UnicoreCRM/backend/artifacts/o4-corrective-verifier.log) |
| O4 verifier | 235 O4 checks; 227 HTTP checks; 9 real concurrent HTTP pairings | [o4-corrective-verifier.log](D:/Project_All/UnicoreCRM/backend/artifacts/o4-corrective-verifier.log) |
| Tasks verifier | 48 focused checks, 0 failures; 8 SQL checks | [o4-corrective-tasks.log](D:/Project_All/UnicoreCRM/backend/artifacts/o4-corrective-tasks.log) |
| Workspace verifier | 43 passed, 0 failed | [o4-corrective-workspace.log](D:/Project_All/UnicoreCRM/backend/artifacts/o4-corrective-workspace.log) |
| AccessControl verifier | 567 passed, 0 failed | [o4-corrective-access.log](D:/Project_All/UnicoreCRM/backend/artifacts/o4-corrective-access.log) |
| Assign regression | 34 Assign checks; 185 HTTP checks; no Task/Activity changes | [o4-corrective-assign.log](D:/Project_All/UnicoreCRM/backend/artifacts/o4-corrective-assign.log) |
| Five DbContext models | Leads, Tasks, Workspace, AccessControl, Workflows: no pending changes; all exit 0 | [o4-corrective-models.log](D:/Project_All/UnicoreCRM/backend/artifacts/o4-corrective-models.log) |

Commands (from workspace root unless specified):

```powershell
dotnet build backend/UnicoreCRM.slnx --nologo -p:UseSharedCompilation=false
pwsh -NoProfile -File backend/scripts/verify-lead-handover.ps1 -DatabaseName UnicoreCRM_O2Claim_O4_Corrective_20261002_04 -RuntimeReady
dotnet run --project backend/scripts/TasksHandoverVerifier/TasksHandoverVerifier.csproj
dotnet run --project backend/scripts/TasksHandoverVerifier/TasksHandoverVerifier.csproj -- --sql
dotnet run --project backend/scripts/LeadHandoverWorkspaceVerifier/UnicoreCRM.LeadHandover.WorkspaceVerifier.csproj
pwsh -NoProfile -File backend/scripts/verify-access-control-record-access.ps1 -DatabaseName UnicoreCRM_RecordAccess_O4_Corrective_20261002_01 -KeepDatabase
pwsh -NoProfile -File backend/scripts/verify-lead-owner-assign.ps1 -DatabaseName UnicoreCRM_O2Claim_O3_Corrective_20261002_01
# From backend; repeat for each project/context listed in model evidence:
dotnet ef migrations has-pending-model-changes --project src/UnicoreCRM.Crm --context LeadsDbContext --no-build
git -C backend diff --check
```

`git diff --check`: PASS. Migration/designer diff: empty. LocalDB fixture databases and logs retained; fresh database names used. Tasks --sql also reruns the 48 focused checks; counts above are unique checks, not inflated by reruns.

## FRONTEND / OPENAPI

Main reported generator completed with only LeadHandoverResult lead property/required removal, 23 generated artifacts, and frontend core/gate progress. No frontend certification is made here. Authoritative OpenAPI belongs at frontend/unicorecrm-web/docs/api/openapi.json, with the design-authority mirror; there is no backend-owned authoritative OpenAPI change for this worker.

## STALE CONTRACT SEARCH

No active Handover result Lead, participant Project, Handover Lead projection, old public route, openTaskPolicy, taskTargets, human leads.handover capability, or client SLA calculation introduced. Remaining obsolete public strings in the changed verifier are explicit rejection/historical-upgrade fixtures. leads.handover.recover remains the canonical service capability. Historical migration/docs strings remain unchanged.

## FAILURES / LIMITATIONS

Two intermediate new test-fixture SQL failures (filtered-index QUOTED_IDENTIFIER and historical JSON sqlcmd quoting) were corrected, followed by complete successful O4 reruns. No unresolved new backend failure. No pre-existing backend failure found by these checks. Frontend, exhaustive scheduler interleavings, power-loss recovery, and production rollback execution were not verified by this worker. Nine bounded HTTP pairings and deterministic real SQL orderings provide the recorded concurrency evidence.

Read-only adversarial source review found no actionable production or final-fixture defect. It is source review, not formal reviewer-principal attestation or a control-system freeze. No FROZEN/release certification is claimed.

## FINAL STATUS

Backend observed findings: no unresolved P0/P1/P2/P3 in corrective scope.
O4 SECURITY/RECOVERY CORRECTIVE: **PASS for backend scope**.
Combined frontend/backend release status: main must assemble its independent frontend evidence.
Commit readiness: **READY FOR INDEPENDENT SOURCE REVIEW**. No commit or push performed.
