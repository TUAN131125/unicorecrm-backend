# Authentication and Workspace invitation admission proposal

Date: 2026-10-29 (Asia/Saigon). Status: **PROPOSAL — NOT ADOPTED; no runtime admission granted.**

This isolated review artifact requests one bundled project-owner decision for the seven operations below. It changes no canonical registry, integrity pin, generated client, runtime route or database. Proposed policies and participant names below are implementation/admission candidates, not existing authority or verification evidence.

This same proposal is now bound into `auth-workspace-admission-bundle.json`, an unsigned review subject containing exactly seven OpenAPI operations, their required component closure, named participant boundaries and independent password-slice dependencies. It is not a second proposal or an adoption record. The acceptance report records its final SHA-256 and the single required owner action.

## Current adoption route and exact missing actions

Active Solo Development v2 explicitly excludes authority changes from ordinary development authorization (`governance/policies/solo-development-v2.json`, `developmentAuthorization.authorityChanges` and `futurePolicyChanges`; enforced by `LOCAL_SCOPE_CANNOT_AUTHORIZE_AUTHORITY_CHANGES`). The retained `governance-change-control-v1.json` recognizes a scoped proposal, Architect review, Governor ratification/scope lock, one writer admission, an immutable control-plane subject, separated reviews/trusted verification and Gatekeeper acceptance. Its canonical records are content-addressed; conversation text alone is not canonical authority. Governor ratification and Project Owner adoption are distinct recorded acts.

Missing for this subject: a hash-bound owner mandate, scoped actual role assignments, Architect/Governor records, writer admission, canonical adoption and permission for its narrowly scoped immutable control-plane commit. Current instructions forbid committing without separate approval. Existing historical owner-directed records are precedent for honest local records with the actual human mandate/hash and explicit local assurance, not reusable approval for authentication. No AI agent may label itself a separately authenticated reviewer or sign for the owner.

`manifests/principal-attestation-policy.json` and `tools/record_review.py` require formal independent/code-quality review to run against a clean immutable subject with reviewer credentials isolated from the writer. Advisory subagent review is useful source review only; it is not that formal attestation. The existing bounded-owner verifier is restricted to outbound-webhook operations and is not a general authentication-adoption mechanism. No new signer, policy bypass, integrity change or fabricated approval is proposed here.

The one owner decision must bind the final bundle hash, ratify/adopt only its seven operations and participant/policy scope, assign accountable actual principals for the applicable governance stages, authorize only the necessary canonical adoption records and immutable control-plane commit in an isolated subject, and retain acceptance/release blocks until required assurance passes. Password change and reset have no invitation participant dependency and must be admitted/executed separately as soon as their own prerequisites are valid.

## Exact blocker and baseline discrepancy

The command `python .unicore-ai/tools/lookup_operation.py <operationId>` returns `PRODUCTION_CONTRACT_READY` for each of the six existing reset/invitation operations. The active canonical registry instead separates frontend wire readiness from backend admission: each has `frontendContractStatus: PRODUCTION_CONTRACT_READY`, `backendAuthorityStatus: NOT_ADMITTED`, `backendImplementationStatus: NOT_IMPLEMENTED`, and `finalStatus: AUTHORITY_GAP`. `changePassword` is absent from that lookup and has no declared self-service wire operation. Neither a generated client nor a successful lookup establishes backend admission.

Sources relative to the workspace root:

- `design-authority/canonical-design/authority/operation-registry.json`: exact rows for all six existing operation IDs.
- `design-authority/canonical-design/authority/precedence-and-architecture.md`, final precedence and PLAT-GOV-01: frontend 2xx is wire authority only; invitation coordination remains an authority gap; multi-owner mutations belong to Workflows.
- `backend/docs/architecture/CURRENT_IMPLEMENTATION_AUTHORITY.md`, B01/B02/B03: reset operations remain fail-closed; invitation acceptance requires an approved IdentityAuth/Workspace/AccessControl boundary and token/target/replay/membership semantics.
- `design-authority/canonical-design/authority/create-access-role-authority.md`, section describing directory provider requirements: Workspace owns invitation records and memberships; that decision does not admit invitation mutations.
- `design-authority/canonical-design/authority/cross-owner-contract-map.json`: current provisioning participants create an initial Workspace and its fixed Owner access; no invitation mutation participant is admitted.
- `design-authority/canonical-design/BASELINE.json` and `design-authority/contracts/openapi.json`: adopted wire reference is `0.23.21-contract.0`, SHA-256 `4bcde6f6c52bf96545106cff5273aff1681b66c8ee239c91cbef921edfd70880`. This is the recorded canonical baseline, not a claim that dirty local frontend contracts match it.

Focused comparison against current frontend `0.24.0-contract.0` finds all six existing operation objects, path metadata and security requirements unchanged. Their transitive schema closures differ only in the shared `ErrorCode` enum, where frontend has additive unrelated codes. The bounded subject retains the canonical enum and every other canonical component; it does not import those frontend additions or adopt the full frontend document. It adds only the proposed `changePassword` operation and its two DTO schemas.

One explicit existing-wire amendment is requested: `requestPasswordReset` already advertises `IDEMPOTENCY_KEY_REUSED` but declares no 409 response. Add only HTTP 409 with the existing `ProblemDetails` schema for that condition; retain its request and 202 success unchanged. The bundle labels this amendment separately and binds exact condition/code/status mappings, including acceptance-wire mappings that use only its existing declared error vocabulary. No undeclared `EMAIL_NOT_VERIFIED`, `MEMBER_ALREADY_EXISTS` or `LIFECYCLE_CONFLICT` response code is introduced on invitation acceptance.

## Requested bundled decision

Approve or amend the concrete policies, ownership and participant boundaries below, adopt the missing self-service wire contract, and reconcile the six existing registry rows and their applicable command/transaction/dependency/persistence records through the authorized control process. Only then admit implementation packets. Do not merely change a readiness label: the owner boundaries, security lifecycle, transaction/replay rules and verification obligations must become approved authority together.

| Operation | Existing/proposed wire | Admission requested |
|---|---|---|
| `changePassword` | **Proposed** `POST /auth/password-changes` | New authenticated IdentityAuth self-service operation; no target account supplied |
| `requestPasswordReset` | Existing `POST /auth/password-reset-requests` | IdentityAuth reset issuance, eligibility, privacy and delivery policy |
| `resetPassword` | Existing `POST /auth/password-resets` | IdentityAuth token consumption, credential replacement and session policy |
| `inviteWorkspaceMember` | Existing `POST /access/invitations` | AccessControl transport/admission with explicit Workflows orchestration and Workspace invitation participant |
| `acceptWorkspaceInvitation` | Existing `POST /auth/invitations/accept` | IdentityAuth transport/principal check with explicit Workflows orchestration and Workspace/AccessControl participants |
| `resendWorkspaceInvitation` | Existing `POST /access/invitations/{invitationId}/resend` | Authorized token rotation and outbox replacement under the same invitation ownership |
| `revokeWorkspaceInvitation` | Existing `POST /access/invitations/{invitationId}/revoke` | Authorized terminal revocation under the same invitation ownership |

`getWorkspaceAccessDirectory` and existing member-access operations remain their own admitted operations. This proposal does not broaden them into invitation mutation or credential authority. `rotateManagedMemberPassword` is not a self-service substitute.

## Proposed password contracts and policy

`changePassword` request: `{ "currentPassword": string, "nextPassword": string }`, reject unknown properties. Both strings are required; `nextPassword` uses the existing registration password policy and existing hasher. Its wire length limits must agree with the approved policy; the current reset wire declares 8–1024 characters. No `accountId`, actor, member or Workspace field is accepted. Confirmation stays a frontend validation concern.

Proposed response: HTTP 200 `{ "completedAt": UtcDateTime, "sessionsRevoked": true }`. Require a valid authenticated session and the existing request/correlation/idempotency metadata. Proposed stable errors: authentication/session failures, `INVALID_CREDENTIALS` for incorrect current password, `VALIDATION_FAILED`, `IDEMPOTENCY_KEY_REUSED`, `RATE_LIMITED`, `INTEGRATION_UNAVAILABLE`, and `INTERNAL_ERROR`. Adopt the exact success/error schemas and statuses before runtime mapping.

Use the trusted principal's account/session; recheck account/session validity in the serialized command. Verify current password before replacing the credential hash. Commit credential `UpdatedAt`, immutable audit/security event and all session revocations in one IdentityAuth transaction. Concurrent credential mutations must serialize so an old-password check cannot race a reset and overwrite its result.

**Unified proposed session policy:** successful password change or reset revokes every existing session, including the current session, and makes every old refresh token unusable. Existing JWT session validation must reject revoked sessions immediately. The frontend clears in-memory auth state and returns to Login after success; the server expires the refresh cookie when present. No new session is issued by either operation. The client never displays success on a failed API call.

Reset retains the existing request/success wire, with the single 409 error-declaration amendment noted above:

- Request `{ "email": string }`; HTTP 202 `{ "requestId": EntityId, "acceptedAt": UtcDateTime }`. `requestId` is an opaque receipt, never a reset credential.
- Completion `{ "token": string, "nextPassword": string }`; HTTP 200 `{ "completedAt": UtcDateTime }`. Use **`nextPassword`**, not a newly invented `newPassword` field. Preserve the existing declared token length range 16–4096 and password range 8–1024.

Proposed reset eligibility: only active, email-verified accounts with an existing local credential. Unknown, suspended, unverified and otherwise ineligible addresses receive the same accepted response shape and no reset email. Apply origin and normalized-subject rate limits equally before account-specific work; avoid account-dependent HTTP failures. If the delivery/outbox infrastructure cannot accept work at all, return the admitted integration failure consistently, not only for known addresses. Accepted means durable acceptance, not external SMTP delivery success.

Generate a 256-bit CSPRNG secret and a separate opaque challenge identifier. Hash/HMAC the secret with purpose separation; persist no plaintext secret. Proposed lifetime: **30 minutes**, single use. Challenge state belongs to IdentityAuth, distinct from email-verification challenges. A new issuance revokes previous outstanding reset challenges and cancels their queued email payloads when safe. Completion checks expiry, pending state, account eligibility and password policy, then atomically consumes the challenge, replaces the credential, invalidates all other outstanding reset challenges, revokes sessions, and writes audit/security evidence.

Idempotency keys scope to operation and normalized subject/challenge; fingerprints use the existing keyed protection, never plaintext passwords/tokens or bare low-entropy password digests. A successful exact replay returns only the original non-secret completion receipt and performs no second mutation, subject to the operation's authentication boundary: a revoked session cannot replay the authenticated change operation. Different payload reuse fails. A consumed token under a fresh key fails. Invalid/expired/used inputs never produce credential writes. Concurrent consumption produces at most one business effect; retry handling must not turn serialization failures into false success.

## Proposed email delivery handling

Extend the existing Gmail SMTP transport, outbox dispatcher and bounded failure classification for distinct reset and invitation templates. Do not reinterpret registration OTPs as reset or invitation tokens. Stage the email with the issuing owner transaction. The challenge stores only its hash/HMAC; the queued mail may carry the token only inside an authenticated encrypted, purpose-bound payload using the existing protected-outbox mechanism, cleared after successful or terminal delivery. No plaintext token/password in SQL, logs, audit, diagnostics or retained test artifacts.

Use a configured, allowlisted HTTPS frontend origin for links; never construct the origin from a caller-controlled Host/redirect. Email messages must identify their purpose and expiry. Sender availability must not become an account enumeration oracle. Delivery retries cannot resurrect superseded/consumed/revoked challenges. An already in-flight message may arrive, but its token must be unusable after revocation.

Deterministic delivery is Development-only and tested separately from Gmail. A task-owned in-memory Development capture can supply browser fixtures without persisting token-bearing logs. External Gmail delivery remains `NOT_VERIFIED` until an actual recipient delivery is observed; accepted HTTP/outbox status does not prove external delivery.

## Proposed invitation ownership and lifecycle

Workspace owns invitation identity, normalized recipient, inviter reference, intended role/team IDs, status/version, timestamps, challenge hash and membership identity/lifecycle. AccessControl owns role validity/grant decisions, role assignments, directory revision and governance evidence. IdentityAuth owns active verified identity, account email and email transport. Workflows owns the explicit invitation orchestration; no owner directly writes another owner's DbContext/table/entity.

Proposed narrow participant contracts, **new and requiring adoption**:

- IdentityAuth identity/profile read verifies the authenticated account/session and returns the verified normalized email. Extend the existing active-identity read only through approved contract change; do not export credentials or tokens.
- Workspace invitation participant creates, reads, rotates, revokes and consumes invitations under serialized lifecycle/version rules; reserves or creates a non-active membership and activates it only after role assignment completion.
- AccessControl invitation access participant validates proposed Workspace-local active roles and permitted grants, assigns the approved roles idempotently, and returns a durable completion receipt.
- IdentityAuth delivery participant stages purpose-bound protected invitation email through existing transport. Its owner transaction and orchestration receipt must be explicit so no committed invitation silently loses its only delivery intent.

Do not reuse initial Workspace provisioning participants: those create a new Workspace and fixed Owner role, not membership in an invited Workspace. Do not introduce a generic cross-owner UnitOfWork or direct cross-schema SQL to make invitation acceptance appear atomic.

Proposed execution model: a durable-convergent Workflows operation with owner-local transactions and a persisted workflow anchor. Each participant has a stable workflow/idempotency reference. A crash between invitation creation and delivery staging leaves resumable work. A crash between membership reservation and assignment leaves a non-active membership. Only after assignment commits may Workspace atomically activate that membership and mark the invitation accepted. No success response is returned before final completion. Concurrent accept/revoke/resend must serialize through Workspace's invitation claim/version; while an acceptance claim is in progress, resend/revoke fail with the adopted lifecycle conflict rather than racing its token rotation. Recovery is mandatory, not best-effort client orchestration.

Concrete reservation refinement: reserve the future membership ID in invitation-owned state; create **no** active or suspended `WorkspaceMembership` row at that point. The existing membership enum and public membership lifecycle remain unchanged. AccessControl assigns only against the verified reservation reference; Workspace inserts the active membership and consumes the invitation together after the assignment receipt. On permanent failure, the adopted AccessControl compensation participant may remove only this workflow's staged assignments after verifying that the reservation is still non-active. Workspace releases the claim only after proof of no remaining assignment; an unknown outcome stays claimed for recovery. No timeout alone releases a potentially committed assignment. The existing directory invitation DTO's required `membershipId` carries the reserved ID and does not imply an active member.

The bundle includes receipt-bound compensation and claim-release methods, a read-only Workspace delivery-eligibility contract explicitly consumed by the IdentityAuth dispatcher, and `RecordInvitationGovernanceCommit` in AccessControl for issue/resend/revoke. Those governance receipts are required before returning the existing `AccessMutationResponse` directory, so directory revision/audit/outbox promises have an explicit owning participant. These are proposed new boundaries; their presence in this review package grants no runtime permission.

Invitation states: `PENDING`, `ACCEPTED`, `REVOKED`, `EXPIRED`. Proposed expiry: seven days. Tokens use separate 256-bit CSPRNG secrets with purpose-bound hash/HMAC storage. No active membership is created merely by sending an invitation. One pending, unexpired invitation per Workspace/normalized email; duplicates return `INVITATION_ALREADY_PENDING`. Resend of a pending invitation rotates the token atomically, invalidates old queued payloads, increments version and renews expiry; revoke is terminal. Repeated authenticated acceptance by the same recipient may replay the original receipt without duplication. Already-member recipients reached under a fresh invitation return the adopted already-member failure; they do not receive role upgrades implicitly.

Sending/resending/revoking requires a trusted active Workspace membership and actual `access.configure`. All role IDs must belong to that Workspace and remain active at acceptance. Proposed grant ceiling: each proposed role's capabilities must be a subset of the inviter's effective capabilities at issuance and still be grantable at acceptance; its effective data scope and field access must also be no broader than the inviter's corresponding grant authority. A capability-subset test alone is insufficient. Unresolved `TEAM`/`CUSTOM` grant semantics fail closed; do not infer privileged role status from display names. Acceptance revalidates the recorded grant authorization through the AccessControl participant, including changes to the inviter's membership/access. Until a canonical team grant model exists, require `teamIds: []` and reject non-empty values explicitly. The existing invitation request still requires `displayName`, `email`, `roleIds`, `teamIds`; do not silently drop required wire fields.

The proposed delegation comparator is explicit: inviter `WORKSPACE` may grant `WORKSPACE` or `OWN`; inviter `OWN` may grant only `OWN`; `TEAM`/`CUSTOM` deny. Field delegation permits `READ_WRITE` → `READ_WRITE`/`READ_ONLY`/`HIDDEN`, `READ_ONLY` → `READ_ONLY`/`HIDDEN`, and `HIDDEN` → `HIDDEN`. Resolve `MASKED` as `HIDDEN` through the existing evaluator. Compare resolved effective rights for every relevant entity/field; unknown or missing projections deny, rather than inferring defaults from absent rows. Require at least one distinct active role, with no implicit default grant. These are new proposed delegation rules to adopt, not a retroactive reinterpretation of existing role administration.

Acceptance requires an authenticated active account with verified email matching the invitation's normalized recipient, a live Workspace, current role validity and a pending valid token. Wrong-account, unverified, expired, revoked and invalid-role attempts produce no active membership. Define final wire mapping for recipient mismatch and account eligibility using the existing `ACCESS_DENIED`, `INVITATION_INVALID` and `TOKEN_EXPIRED` vocabulary; do not invent undeclared success or conflict schemas.

## Proposed registration and connected UI rules

Carry the validated internal invitation return URL through Login, Register and email verification. Preserve it in navigation memory/history state only as needed; do not persist invitation/reset secrets in browser storage. Reject external, protocol-relative, backslash/control-character and noncanonical return destinations through the existing safe redirect resolver. Clear token-bearing URL/history entries promptly while retaining the token only in component memory.

Registration remains account registration; it does not accept the invitation or create a Workspace. After verification and sign-in, resume invitation acceptance before entering WorkspaceSelection or invoking automatic initial provisioning. If acceptance fails, remain in the invitation flow with a safe retry/account-switch path. Ordinary registration without an invitation retains its current initial-provisioning and Quick Setup behavior.

After backend acceptance commits, reload authoritative memberships and resolve/enter the actual returned Workspace using connected Workspace APIs. Never use `DEVELOPMENT_WORKSPACES` or localStorage membership state as connected authority. Members and pending invitations use the admitted SQL-backed access-directory read. Disable duplicate submits; classify errors without displaying raw backend diagnostics. UI permission checks supplement application authorization.

## Verification required after adoption and implementation

Focused tests must cover wrong password/account/session, policy violations, revoked-session JWT/refresh denial, reset expiry/reuse/concurrent consumption and secret-free evidence. Invitation tests must cover cross-Workspace actors, missing capability, role escalation, foreign/fake/inactive role IDs, unsupported teams, mismatched/unverified recipients, already-member, expired/revoked tokens, concurrent acceptance, resend/old-token races, Workspace suspension/deletion and partial workflow recovery. Prove invited signup never provisions a personal Workspace.

Use synthetic task-owned fixtures, a separate ApiHost and controlled Chromium. Read back credential timestamps, challenge consumption, invitation/membership/assignment state and audit/outbox; prove restart persistence and preserve existing data. Run real connected browser journeys and distinguish deterministic delivery from Gmail delivery. No reset/reseed, direct SQL privilege grants, authority edits, self-approved review or PASS claim is authorized by this proposal.
