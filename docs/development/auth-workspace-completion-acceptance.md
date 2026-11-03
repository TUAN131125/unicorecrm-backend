# Authentication and Workspace completion acceptance

Date: 2026-10-29 (Asia/Saigon). Overall result: **BLOCKED for all three end-to-end features**. Permitted frontend corrections were implemented and tested. No backend mutation, migration, canonical admission, integrity pin, credential configuration or CRM/Quote behavior was changed.

| Feature | Backend | Frontend | HTTP | SQL | Browser | Verdict |
|---|---|---|---|---|---|---|
| Change Password | No admitted self-service operation; no route added | Account Security form not introduced without an admitted API | Proposed `/auth/password-changes` returns 404 | No credential writes; acceptance not run | Functional journey blocked | BLOCKED |
| Forgot Password | Both existing reset operations explicitly NOT_ADMITTED | Existing pages corrected: error/success handling, validation, in-memory token, URL replacement, duplicate-submit protection | Real ApiHost request/reset endpoints return 404 | No reset challenge or credential writes; persistence/restart acceptance blocked | Connected headless Chromium negative checks pass; successful reset journey blocked | BLOCKED |
| Invite Member | Four invitation operations explicitly NOT_ADMITTED; approved mutation participants absent | Safe login/register/verify return; authoritative connected workspace resolution; Development storage isolated | Real ApiHost invite/accept/resend/revoke endpoints return 404 | No invitation, membership or assignment writes; persistence/restart acceptance blocked | Connected Chromium link/redirect checks pass; successful invite journey blocked | BLOCKED |

## One actionable admission blocker

The active canonical `design-authority/canonical-design/BASELINE.json` names `authority/operation-registry.json` as machine authority. That registry marks `requestPasswordReset`, `resetPassword`, `inviteWorkspaceMember`, `acceptWorkspaceInvitation`, `resendWorkspaceInvitation` and `revokeWorkspaceInvitation` **NOT_ADMITTED / AUTHORITY_GAP**. No equivalent self-service `changePassword` operation exists. `rotateManagedMemberPassword` has different authorization and is also unadmitted.

The older `.unicore-ai/tools/lookup_operation.py` reports frontend `PRODUCTION_CONTRACT_READY`; this is insufficient backend admission. The canonical `authority/precedence-and-architecture.md` explicitly says frontend 2xx contracts are wire authority only and retains the invitation/membership coordination gaps. Current backend `docs/architecture/CURRENT_IMPLEMENTATION_AUTHORITY.md` independently preserves those fail-closed gaps.

The required isolated artifact is [auth-workspace-admission-proposal.md](auth-workspace-admission-proposal.md). It bundles the seven operations, existing versus proposed wire contracts, credential/session policy, reset/invitation lifecycle, ownership, transaction/recovery participants and verification obligations into one concrete project-owner admission decision. Its contents are proposals, not adopted authority. Implementation requires that decision to be adopted through the authorized control process; this task did not self-adopt it.

## Implemented frontend scope

Paths below are relative to `frontend/unicorecrm-web/`:

- `src/features/auth/pages/ForgotPasswordPage.tsx`: failed API/network requests retain the form and show a safe categorized error; accepted requests show generic eligibility wording; connected request IDs never become token links.
- `src/features/auth/pages/ResetPasswordPage.tsx`: validates token/password/confirmation; removes token from the current URL/history entry while retaining it in component memory; distinguishes expired/invalid-used/service errors; prevents concurrent submits and clears credentials on completion.
- `src/features/auth/routing/passwordRecoveryFeedback.ts`: bilingual error mapping through the existing application-error presentation boundary.
- `src/features/auth/pages/LoginPage.tsx`, `RegisterPage.tsx`, `VerifyEmailPage.tsx`: preserve the validated internal invitation return through authentication and verification, before Workspace Selection can trigger initial provisioning. Existing safe-return parsing remains the authority for allowed in-app destinations.
- `src/features/auth/pages/InvitationAcceptancePage.tsx`: awaits actual acceptance, reloads connected memberships, requires an active membership, bootstraps the returned Workspace through the connected boundary, and navigates using its real key. Development workspace lookup is gated to Development. Repeated pending clicks issue one command; errors remain visible.
- `src/platform/workspace-membership/runtime/workspaceMembershipRuntime.ts`: connected callers cannot use Development browser storage or membership lists as authority, or persist mutations through that Development runtime. Existing connected AccessControl directory readers remain the member-list authority.
- `tests/fixtures/runtime/check-password-recovery.mts` and `check-invitation-navigation.mts`: focused behavioral regressions invoked by existing `tests/quality/integration/check-auth-session-contracts.mts`.
- `docs/quality/repository-inventory.json` and `.md`: regenerated for added files. Protected module/route/capability counts are unchanged; no quality threshold, allowlist or snapshot was weakened.

No generated API files were changed. Existing reset wire remains `{token,nextPassword}` to `{completedAt}`. Existing `getWorkspaceAccessDirectory` and member-access operations were reused as current authority; this task did not broaden them into invitation APIs. Pending invitations cannot be made functional before backend admission.

Existing UI entry points: People sidebar → **Members & Invitations**, `/w/:workspaceKey/people/members`, already consumes the connected `AccessGovernanceHttpAdapter.getWorkspaceAccessDirectory` snapshot in `UsersPermissionsPage`. No Account Security route exists. The current UserMenu **Security** and **Sessions** callbacks lead to `/w/:workspaceKey/people/audit`; **Profile** leads to Members and **Personal settings** displays a toast. These are existing limitations; no working password form is implied by those menu labels.

## Executed evidence and practical limits

Evidence is kept in one task directory, `task-runtime/auth-completion-20261029/`, relative to the workspace root. It contains dirty-source preimages/state, two browser screenshots, `http-blocked-routes.json`, `browser-results.json`, the reusable `verify-connected-browser.cjs`, and the frontend verification log. None is a trusted freeze/adoption attestation.

Backend focused command:

```powershell
dotnet test tests/UnicoreCRM.ArchitectureTests/UnicoreCRM.ArchitectureTests.csproj --no-restore --filter 'FullyQualifiedName~AccessControl|FullyQualifiedName~Workspace|FullyQualifiedName~IdentityAuth' --logger 'console;verbosity=minimal'
```

Result: **68 passed, 0 failed, 0 skipped**. This is a focused baseline check, not a new password/invitation security suite and not a claim that the historical full suite (474 passes / four baseline failures) was rerun.

HTTP verification used a task-owned current-source ASP.NET ApiHost on `127.0.0.1:5098` with existing `--demo-seed-host` support, disabling application background workers. Synthetic requests reached all seven blocked/proposed paths and returned **404**. This proves no callable success bypass; it does not prove authentication, token consumption, authorization denial or membership creation.

`node task-runtime/auth-completion-20261029/verify-connected-browser.cjs` used task-owned headless Chromium, a separate Vite server on `127.0.0.1:5399`, connected mode and the real task ApiHost. Passed checks: actual request/reset 404 responses never show success; no connected request-ID reset link; reset query removed; no reset token/password in localStorage, sessionStorage or current history state; missing token disables submit; invitation sign-in/register links retain the internal return; external return URL discarded. This is negative connected-browser regression evidence, **not** successful reset/invitation end-to-end acceptance. Synthetic-gateway React/JSDOM checks additionally exercise positive UI outcomes, expiry/reuse, duplicate submits and authoritative membership/bootstrap sequencing; they are not SQL/SMTP proof.

SQL: local configuration contains no usable application connection string. No real application database was selected by guess. The HTTP host referenced a task-specific nonexistent database and never ran migration/seed commands or database-backed handlers. Read-only LocalDB `DB_ID('UnicoreCRM_AuthCompletion_NoWrites_20261029')` returned null after HTTP/browser checks: **NO_TASK_DATABASE_CREATED**. Original SQL data was not reset/reseeded or directly mutated. Original-data snapshot/readback, credential update, consumed challenges, invitation status, roles, audit/outbox and restart durability are **NOT_VERIFIED/BLOCKED**, not PASS.

Migration baseline: IdentityAuth's latest source migration remains `20260831115803_IdentityReadAuditConformance`; no migration was added or applied. Workspace/AccessControl schema and migrations remain unchanged.

Gmail SMTP: **NOT_VERIFIED**. No external email was sent. No deterministic reset/invitation delivery was claimed; those workflows remain blocked. No Google Login/OAuth implementation was added.

## Verification status

Focused frontend TypeScript, production build, auth-session/UX, guidance, presentation, mutation authority, strict typing, API boundaries, backend separation/readiness and inventory checks passed during implementation. Full-quality PASS is not inferred from focused checks.

Independent source review: **PASS within the changed frontend scope**, with no remaining actionable findings. One initial P2 was fixed: changing reset links on the same mounted route now adopts the incoming token and isolates stale success/error/finally callbacks by token generation. The regression covers pending A→B replacement, completed→C reopening and stale C failure while D is pending. This source review is not a trusted control-plane attestation or a backend feature acceptance.

Final executed checks:

| Command/check | Actual result |
|---|---|
| `npx tsc --noEmit` | PASS after final token-generation fix |
| `npm run build` | PASS after final changes; bundle budget PASS, largest chunk 482.61 KiB / 500 KiB limit |
| `npm run quality:gate -- --gate quality.auth-session-contracts` | PASS; includes both new behavioral fixtures, final same-route race regression and synthetic Register→Verify→Login invitation return |
| `quality.auth-ux-contracts`, `quality.guidance-contracts`, `quality.presentation-responsibility`, `quality.mutation-command-authority` | PASS |
| `quality.strict-core`, `quality.strict-type-regions`, `quality.api-management-boundaries`, `quality.frontend-backend-separation`, `quality.backend-readiness`, `quality.quality-pipeline` | PASS |
| `npm run api:check` | PASS |
| `npm run quality:group -- --group route-smoke` | PASS 8/8 after final changes |
| `npm run repo:check` | Final PASS; inventory fingerprint `cd67abf734c26dd28d208035f04a48b687d4d0633705462c2fb3c14107d4473a` |
| `npm run verify` | FAIL: 47/328 gates passed, stopped at gate 48 `quality.repository-inventory`; remaining gates NOT_RUN |
| Headless Chromium against real ApiHost | PASS for the five negative/navigation checks documented above; feature success journeys BLOCKED |

The full pipeline inventory failure occurred while inventory regeneration overlapped an existing mutation-negative-control test. That test temporarily appended ` && false` to unrelated `IntegrationsView.tsx` and restored it in `finally`; the generated inventory captured the temporary nine-byte change. After the pipeline ended, inventory was regenerated and its exact failed gate passed. `IntegrationsView.tsx` matches the original preimage byte-for-byte (SHA-256 `8001dea622f46f16835e05bb4f5124e03f8477ac009999f5a32d1d4c8b30e06e`). The full pipeline was not rerun to completion, so it remains a recorded failed run, not full-quality PASS. No unrelated source was changed to address it.

Source preservation check against all pre-existing dirty snapshots: backend zero differences; control zero differences; frontend only the two intentionally regenerated inventory files differ. Newly modified source files are exactly the scoped frontend files listed above. Backend added only this report and its admission proposal. No commit, push, merge, reset, rebase, pull or SQL DML was performed.

## Admission continuation: one exact review subject

The follow-up request was handled without reworking the already-passing frontend. The existing proposal was refined in place; no duplicate proposal was created. One bounded machine-readable review package was added:

- Artifact: [auth-workspace-admission-bundle.json](auth-workspace-admission-bundle.json).
- Exact SHA-256: `68ba1c0725b0411dcceeb8aeec7ed99318f4d1f1b1ec9b04f785d04888ad280a`.
- State: **PREPARED_NOT_ADOPTED_NOT_ADMITTED**.
- Subject: exactly seven operation IDs, 38 required OpenAPI components, two new self-service DTOs, five narrow participant boundaries, security/recovery policies, retained invitation directory projection, and bound canonical/policy/proposal preimages.

Current canonical OpenAPI remains `0.23.21-contract.0`, SHA-256 `4bcde6f6c52bf96545106cff5273aff1681b66c8ee239c91cbef921edfd70880`. Frontend observed only: `0.24.0-contract.0`, SHA-256 `13ffb0783b8dbbffd41b5a73e83d29580e98c0221fa85fc933ae97f28f4cd25f`. All six existing source operation bodies/path metadata/security match; shared closure differs only by additive frontend `ErrorCode` enum values. Those unrelated values and the full frontend document are **not** adopted. The subject retains canonical components, proposes `changePassword`, and explicitly requests just one existing-wire amendment: declare 409 `ProblemDetails` on reset-request idempotency mismatch, keeping its request/202 response unchanged.

Advisory independent review of the exact final hash: **PASS**, with five initial package findings resolved. The review checked 80 applicable error/status mappings, compensation/claim-release boundaries, dispatcher eligibility dependency, directory governance receipts and effective grant comparisons. This is not a policy-authenticated signed independent review or an adoption record. No reviewer credential was read, created, reused as owner authority or supplied to a writer process.

Executed preparation check:

```powershell
python task-runtime/auth-completion-20261029/prepare-admission.py --check
```

Result: **PACKAGE_INTEGRITY_PASS_NO_ADMISSION**. Verified exact operation/closure scope, canonical retained schemas, isolated 409 delta, self-only DTO, declared error/status mappings, participant/recovery completeness, all bound-input hashes and no invitation dependency for either password slice. Five in-memory negative controls were rejected: unrelated operation, altered existing authorization, unused foreign schema, caller-supplied target account, and fabricated ADMITTED status. This checker prepares/verifies review inputs only and is not an adopted admission/trust engine.

### Exact remaining authority requirements

| Operation | Missing authority | Contract/review subject |
|---|---|---|
| `changePassword` | New self-service wire, IdentityAuth authorization/credential/session policy and operation admission | Final bundle hash above; `proposedOpenApiFragment` and CHANGE_PASSWORD slice |
| `requestPasswordReset` | `AG-IDENTITY-PASSWORD-RESET` decision, issuance/privacy/delivery policy, explicit 409 declaration | Same hash; PASSWORD_RESET slice and explicit wire amendment |
| `resetPassword` | Same gap; challenge/purpose/consumption/session/replay/persistence policy | Same hash; PASSWORD_RESET slice |
| `inviteWorkspaceMember` | `AG-WORKSPACE-INVITATION`, Workspace ownership and Workflows/AccessControl/IdentityAuth participant adoption | Same hash; WORKSPACE_INVITATION slice and named ports |
| `acceptWorkspaceInvitation` | Same gap; verified recipient, reservation→assignment→activation, recovery/compensation | Same hash; acceptance workflow and error mappings |
| `resendWorkspaceInvitation` | Same gap; token rotation/delivery/revision/claim serialization | Same hash; resend/revoke workflow |
| `revokeWorkspaceInvitation` | Same gap; terminal revocation/delivery eligibility/governance receipt | Same hash; resend/revoke workflow |

The active Solo v2 policy hash was verified by the independent advisory reviewer: `7816032c334656fcc665926c0198e2c5898d03fd99ba4260b1ab823c44780be2`. It expressly excludes authority changes from ordinary development authorization. The recognized path is retained `governance-change-control-v1.json`: scoped Architect review, Governor ratification/scope lock, one writer admission, immutable control-plane subject, separated reviews/trusted verification and Gatekeeper acceptance, with distinct Project Owner adoption. Local full-SHA bootstrap authority is recognized; that does not make local tests freeze-grade or remove formal assurance requirements. The unrelated outbound-only bounded verifier is not the adoption mechanism for authentication.

### Single owner decision required

One actual Project Owner decision must reference **this artifact and full SHA-256**, and do all of the following together:

1. Ratify/adopt only the seven operation contracts, named participant boundaries, policies and explicit reset-request 409 amendment in this subject. Retain canonical base and unrelated operations; no blanket frontend OpenAPI adoption.
2. Supply accountable actual scoped role assignments: Architect, Governor, one Governance Primary Writer, Code Quality Reviewer, Independent Reviewer, Verifier and Gatekeeper. Respect writer/reviewer and writer/Gatekeeper separation. AI task aliases are advisory tools, not authenticated independent principals. Governor ratification and Owner adoption must be separately recorded even if one authorized human holds both roles.
3. Separately authorize only the necessary canonical/control-plane adoption records and immutable adoption commit in an isolated checkout preserving existing dirty changes. Current instructions prohibit committing without this separate approval. No product/backend/frontend commit, push or merge is requested.
4. Permit governed implementation of the narrowly bounded extension-reader/index integration necessary for these seven operations; current lookup has no self-service shard and its existing bounded helper is outbound-only. No hand-edited integrity pins, approval labels or unrelated allowlist expansion may stand in for adoption. Any adapter change remains subject to the v1 review/verification stages.
5. Keep product acceptance/release blocked until its applicable formal assurance and real feature evidence pass. After valid slice admission, execute Change Password first and Password Reset second without waiting for invitation participants.

The actual human decision must then be recorded with its source/hash through that recognized route. A writer-created APPROVED JSON field or this report cannot substitute for it. The current follow-up request authorizes preparation and conditional implementation; it does not itself supply these exact-hash decisions, role assignments or separate commit permission. No canonical approval/ratification/adoption record was manufactured in this continuation.

| Requested functional result | Current result |
|---|---|
| `CHANGE_PASSWORD_FUNCTIONAL_PASS` | NOT_ACHIEVED — BLOCKED_ADMISSION; HTTP/SQL/Chromium positive journey not run |
| `PASSWORD_RESET_FUNCTIONAL_PASS` | NOT_ACHIEVED — BLOCKED_ADMISSION; HTTP/SQL/Chromium positive journey not run |
| `WORKSPACE_INVITATION_FUNCTIONAL_PASS` | NOT_ACHIEVED — BLOCKED_ADMISSION; HTTP/SQL/Chromium positive journey not run |

Completed independently: exact bounded package, canonical/frontend reconciliation, advisory review with resolved findings, deterministic package integrity/negative controls and the frontend fixes/evidence already recorded above. No frontend/runtime/backend product source, SQL data, canonical registry, policy, approval label, integrity pin or allowlist was changed in this continuation. No commit/push/merge occurred; the three features are not represented as functional PASS.
