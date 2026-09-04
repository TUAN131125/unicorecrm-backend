# Current Implementation Authority

## Authority precedence

B00 uses the following precedence:

1. decisions explicitly frozen by the B00 task;
2. the current verified OpenAPI;
3. current command/workflow registries when relevant;
4. verified M0-M12 invariants available in repository evidence;
5. the existing Design Authority where not superseded;
6. current frontend source as targeted read-only evidence.

Old source line numbers, repository fingerprints, frontend commit hashes, and implementation assumptions are historical evidence only.

## Design Authority historical-baseline rule

The extracted `design-authority/` is a historical canonical baseline produced before frontend M0-M12 hardening. It remains useful where it is not superseded by higher-precedence current evidence. `design-authority.zip` is ignored because the extracted directory is available.

## Frontend read-only evidence rule

The frontend is consumer and current-contract evidence only. It is read-only to backend implementation work. Frontend behavior, tests, comments, demo state, or inferred UI intent cannot create backend business authority or unblock a contract.

## Phase identifier naming rule

Implementation phase identifiers such as B00-B09 are planning and history metadata only. They must not become durable domain, API, contract, database, migration, configuration, script, runtime, or architecture-extension identifiers. Durable artifacts use business or technical semantic names; roadmap and historical implementation references may retain phase identifiers.

## Platform CI quality-gate authority

`PLAT-QA-01` admits and implements the repository-owned blocking Platform gate defined by
`docs/architecture/PLATFORM_CI_QUALITY_GATE.md`. The gate restores and builds the solution and runs
the required IdentityAuth, Workspace, initial-provisioning and AccessControl verification matrix
from a clean checkout. Its result is bound to the exact checked-out commit; `FAIL` and `NOT_RUN`
required checks both fail the workflow. Live Gmail conformance and operator-specific development
configuration checks remain explicitly external rather than being silently reported as passing.

The workflow creates a repository status check. Branch protection and required-status-check policy
remain repository-administrator configuration and are not claimed as enabled by this authority.

## Current OpenAPI authority rule

For every operation it declares, `frontend/unicorecrm-web/docs/api/openapi.json` controls the exact current HTTP wire contract. Its currently generated SHA-256 is:

`d98462853a5c529ce1695978d35541a8bc000dc25b2781a62fd8bf5e91cd6a57`

This matches `frontend/unicorecrm-web/docs/api/openapi.sha256`. It supersedes `fd079b2f6e189ffe391d555cee1d2acaa735cf532346cc74a02070862bd78792`, which was current until the Lead qualification Contact name-bound amendment (`DEC-LEAD-CONTACT-NAME-BOUND`; `LeadQualificationContactInput.displayName` `maxLength` 256 -> 200, recorded below), which in turn superseded `f3a0273e9d8847b5bcd8c673810e2a9e8d0e70031da12b4dc2a8dd338a2354b6`, current until the Support customer-enrichment amendment. The amendment was applied through the repository generator pipeline (`npm run api:generate`), which rewrote the contract hash and every derived artifact, and the `quality.api-contract` gate passes on the result. The verified file declares 270 operations: 236 contain a 2xx response contract and 34 contain no 2xx response contract. Operations without an admitted success contract remain fail-closed and must not be implemented as callable success paths.

Presence of a 2xx OpenAPI response contract does **not**, by itself, authorize backend implementation. OpenAPI controls the exact HTTP wire contract for declared operations; implementation readiness must still be reconciled with:

1. the current command registry;
2. the workflow ownership registry;
3. current implementation authority;
4. known M0-M12 amendments;
5. explicit `BLOCKED` or `AUTHORITY_GAP` state.

Therefore, `2xx response contract != PRODUCTION READY` by itself. An operation with a success schema may still be blocked when ownership or readiness authority says so. An operation without an admitted success contract remains fail-closed. Business readiness must never be inferred from HTTP schema existence alone.

OpenAPI authority applies only to operations and schemas actually declared. It does not authorize inferred business behavior outside those declarations.

## Deferred logical ownership

The current logical owner map includes:

```text
Platform
├── IdentityAuth
├── Workspace
├── AccessControl
└── WorkspaceConfig [DEFERRED]

WorkspaceStudio
└── Studio [DEFERRED]
```

`DEFERRED` or `BLOCKED` means the capability remains part of the architecture while its current mutation semantics are not admitted. It does not mean the canonical owner is absent. WorkspaceConfig remains a Platform owner, and WorkspaceStudio/Studio remains a logical bounded context and owner/capability. The initial physical topology may defer a dedicated WorkspaceStudio assembly, and B00-FIX does not create one. These deferred capabilities are not immediate B01-B09 phases.

## Task ID amendment

Current OpenAPI `CreateTaskRequest` has no `id` property and requires only business intent fields including title, assignee, and due time. Current hardened frontend evidence states that Task aggregate IDs are server-assigned.

Therefore:

- the Tasks owner assigns Task aggregate identity;
- `task_deal_*`, `task_deal_recycle_*`, and similar synthetic values are intent/dedupe evidence, not Task IDs;
- frontend, AI, webhook, workflow, and foreign modules must not fabricate Task IDs.

This amendment supersedes historical implementation assumptions that treated synthetic keys as Task aggregate identity.

## Known unresolved authority gaps

The following remain `AUTHORITY_GAP` and are not implemented or semantically defined by B00:

- WF-01 Contact Opportunity public/internal trigger and mutation contract;
- WF-04 Customer Commercial Actions public/internal trigger and mutation contract;
- WF-21 Work Activation public workflow contract;
- Contact, Customer, and Organization mutations whose current operations remain blocked, including unresolved mutation-result/relationship semantics;
- Studio/configuration writes whose current OpenAPI operations remain blocked; a folder or historical design description does not admit them;
- receivable collection activity ownership where no current canonical owner/command contract proves the mutation;
- any provider/live-conformance behavior that requires external evidence not present in the repository;
- Support SLA semantics: deadline rules, first-response event, breach, at-risk, pause, terminal behavior and the meaning of `not_applicable`, none of which current authority proves;
- the member display name that the optional Support activity and comment documents require. See the Support Core section for all three;
- `TEAM` record-access semantics: Workspace can project direct membership-team identifiers, but no authoritative relationship connects both a record and the requesting member to a team for AccessControl evaluation, so `TEAM` remains unresolved and fails closed wherever it is evaluated;
- `CUSTOM` record-access semantics: `createAccessRole` and `replaceAccessRole` admit and persist the normalized `allowedOwnerIds` representation, including an empty explicit deny-all set, but no authority admits an evaluator interpretation that could grant record access from that representation, so `CUSTOM` remains fail-closed;
- `TaskActivity` record-access semantics: no authority settles whether an Activity belongs to the `tasks` record scope or is an independent Workspace-scoped record with its own resource descriptor, so `listActivities` and `logActivity` fail closed outside `WORKSPACE` scope;
- `MASKED` field representation: the policy value is admitted for role creation/replacement and is persisted, but no authority defines a masked rendering; enforcement therefore withholds the value and reports the effective field result as `HIDDEN` rather than producing invented masked content;
- delegated inbound-Lead field security: current authority admits only the delegated `leads.create` capability evaluation for that path and defines no field-security concern for it, so interactive field policy is neither applied nor declared inapplicable;
- the mapping between the frontend field vocabulary (`subject`, `assigneeId`, `queueId`, `slaPolicyId`) and the Support wire field names. A field-security policy written against the frontend spelling cannot be enforced and fails the operation closed;
- an authoritative member-owner concept for Product. The aggregate carries no member reference of any kind, so `OWN` scope denies every Product record rather than ownership being invented for it.

These gaps do not block the independent B00 skeleton. They block only later implementation that depends on the missing semantics.

## B01 Identity/Auth implementation authority

The current Identity/Auth wire surface contains ten operations. B01 admits and implements the independently complete operations `registerAccount`, `signIn` (password/AAL1 path only), `getCurrentSession`, `refreshSession`, and `signOut`.

The following four operations remain fail-closed `AUTHORITY_GAP` despite having OpenAPI success schemas and registry readiness labels:

- `verifyMfa`: no current authority defines enrollment, authenticator/provider ownership, challenge issuance, attempt locking, or secret lifecycle. B01 does not fabricate MFA challenges or success.
- `verifyEmail`: no current authority defines verification-token issuance, delivery, hashing, expiry, or consumption semantics. **Superseded** by `PROJECT_EXTENSION_EMAIL_VERIFICATION_OTP` below, which admits a six-digit emailed code as the canonical credential and retires the token request body; `verifyMfa`, `requestPasswordReset` and `resetPassword` remain fail-closed.
- `requestPasswordReset` and `resetPassword`: no current authority defines reset-token issuance/delivery, hashing, expiry, consumption, or required session-revocation semantics.

`acceptWorkspaceInvitation` was routed to B02 for owner resolution and is now fail-closed `AUTHORITY_GAP`: its success requires a Workspace membership mutation, but current authority does not define an approved IdentityAuth/Workspace/AccessControl owner contract or the invitation issuance, token validation, expiry, target-binding, replay, and membership-mutation semantics. IdentityAuth and Workspace must not fabricate or duplicate that state. Resolution must be reconciled with the B03 AccessControl invitation producer.

These gaps supersede any inference that OpenAPI or historical readiness labels alone authorize the missing business/security semantics. They do not block the five independent B01 operations.

No relational provider was previously frozen. B01 selects SQL Server with EF Core for the initial single-database deployment and uses the `iam` logical schema. This is an implementation choice inside the existing one-relational-database architecture, not a topology redesign.

B01 uses the standard ASP.NET Core bearer stack with externally configured issuer, audience, and HS256 signing key. Access tokens contain only IdentityAuth-owned identity/session claims. Refresh credentials are cookie-assisted, rotated, and persisted only as hashes; raw access and refresh tokens are not persisted.

The `memberId` carried by the current Identity/Auth principal is an IdentityAuth-owned global authenticated-member identifier. It is not a Workspace membership identifier and does not establish Workspace authority. B02/B03 authority remains authenticated user plus requested Workspace plus verified membership plus permission evaluation.

### B01 core runtime verification

On 2026-08-23 the actual ApiHost started successfully against the isolated `UnicoreCRM_B01_Smoke` LocalDB database after correcting the B00 CommercialEvidence placeholder delegation. Runtime smoke verified `registerAccount`, password/AAL1 `signIn`, `getCurrentSession`, rotating `refreshSession`, and `signOut`, including invalid-credential, rotated-token, invalid-token, and revoked-session rejection. Therefore `B01 CORE FOUNDATION: PASS`; the four `AUTHORITY_GAP` operations and the B02-deferred invitation operation above remain unimplemented and must not be invented.

Refresh cookies remain `SameSite=Strict`. B09 must verify cookie behavior against the actual frontend/backend deployment origins before connected integration is accepted.

### PLAT-SEC-01 IdentityAuth abuse protection

`registerAccount`, `requestEmailVerification`, `verifyEmail`, password `signIn`, and
`refreshSession` now enforce independent fixed-window quotas for network origin and authentication
subject before their business handlers run. Email-subject partitioning is identical for existing and
nonexistent accounts. Refresh subject partitioning remains stable across token rotation by using the
credential's session identifier, while malformed credentials receive an opaque keyed partition.

Throttle responses use the admitted `429` / `RATE_LIMITED` ProblemDetails contract, set
`retryable = true`, include a positive integer `Retry-After`, disclose no account or credential
state, and are marked `Cache-Control: no-store`. Partition identifiers are HMAC protected and no
email, address, password, OTP, refresh credential, or partition digest is logged by the control.
Existing password, OTP-challenge, refresh rotation/hashing, session validation, idempotency, and
generic failure behavior remain unchanged.

The exact limits, configuration contract, and deployment boundary are recorded in
`IDENTITY_AUTH_ABUSE_PROTECTION.md`. Limiter state is process-local. Multi-instance production
deployment therefore requires equivalent or stricter aggregate controls at a trusted ingress or in
a distributed limiter and trustworthy client-origin propagation; this application change does not
claim those external controls already exist.

## B02 Workspace implementation authority

B02 admits and implements the two independently complete Workspace context reads:

- `listMyWorkspaces`: `GET /workspaces` returns the authenticated account's Workspace memberships with the contract-defined `active` or `suspended` status.
- `getWorkspaceBootstrap`: `GET /workspaces/{workspaceId}/bootstrap` returns the authoritative Workspace summary, capability projection, and runtime-configuration projection only after active membership is verified.

Workspace identity and membership are persisted by the owner-specific `WorkspaceDbContext` in the `workspace` logical schema. IdentityAuth remains authoritative for authentication; Workspace consumes only the authenticated `accountId`/global `memberId` reference and a narrow Development-only identity lookup contract. Workspace does not access `IdentityAuthDbContext` or duplicate accounts.

The implemented trust invariant is: authenticated identity plus requested Workspace plus an active Workspace-owned membership produces a request-scoped trusted `CurrentWorkspace`. A path or `X-Workspace-Id` value is requested context only and never authority by itself. The header carrier is activated only by explicit workspace-required endpoint metadata; workspace-independent operations are not forced through resolution. Workspace membership establishes association only. Roles, permissions, capability evaluation, and data/record/field scope remain B03 AccessControl responsibilities.

The bootstrap configuration and capability fields are read projections required by the current wire contract. B02 provides no public mutation for them and does not admit the deferred WorkspaceConfig or AccessControl mutation semantics.

Runtime verification on 2026-08-23 used an isolated LocalDB database and proved active-member listing/bootstrap success, uniform `ACCESS_DENIED` rejection for a foreign or unknown Workspace, and `WORKSPACE_MISMATCH` rejection for malformed Workspace input. Therefore `B02 WORKSPACE FOUNDATION: PASS`; `acceptWorkspaceInvitation` remains the fail-closed authority gap described above.

## B03 Access Control implementation authority

B03 admits and implements the core authorization operation `getCurrentAuthorizationContext`: `GET /access/context`. Its authority chain is authenticated B01 identity, then B02 trusted Workspace/membership, then AccessControl-owned assignments and policy state. The resulting effective context is evaluated at the application boundary through the AccessControl-owned `IAccessAuthorizer`; HTTP metadata or middleware is not a second permission authority.

AccessControl owns workspace-scoped roles, canonical capability assignments, membership-to-role assignments, data-scope policies, field-security policies, effective authorization evaluation, and authorization-decision audit records in the `access` logical schema. It stores Workspace and membership identifiers only as foreign authoritative scalar references and does not access IdentityAuth or Workspace persistence. Role capabilities are not accepted from JWTs, request headers, or frontend state. An absent capability denies access.

`WorkspaceAccessRecord` remains a Workspace/bootstrap read projection and is not authorization truth. The Workspace bootstrap capability projection now consumes the approved AccessControl application boundary after trusted Workspace resolution; AccessControl does not derive permission truth from the Workspace projection. `ICurrentWorkspace` remains limited to trusted Workspace/membership identity.

The following AccessControl administrative operations are admitted, callable, implemented, and runtime-verified. This current-state declaration supersedes the earlier minimal-B03 statement that they had no callable backend route:

| Operation | Route | Current authority |
|---|---|---|
| `createAccessRole` | `POST /access/roles` | AccessControl creates an active version-0 Workspace role and the submitted capability, data-scope, and field-security state in one owner-local serializable transaction. It requires `access.configure` and `Idempotency-Key`; creation is serialized by the Workspace directory-revision anchor. |
| `replaceAccessRole` | `PUT /access/roles/{roleId}` | AccessControl fully replaces an active role's mutable definition, capabilities, data scopes, and field security. It requires `access.configure`, `Idempotency-Key`, and strong quoted `If-Match` over `AccessRole.Version`; it never changes lifecycle state. |
| `archiveAccessRole` | `POST /access/roles/{roleId}/archive` | AccessControl exclusively owns `active -> inactive`. It requires `access.configure`, `Idempotency-Key`, and strong quoted `If-Match` over `AccessRole.Version`; no reactivation operation is admitted. Capabilities, policies, and assignments remain persisted but an inactive role grants no effective authority. |
| `replaceWorkspaceMemberAccess` | `POST /access/members/{membershipId}/access` | AccessControl fully replaces its `MembershipRoleAssignment` set for the Workspace-owned membership reference. It requires `access.configure`, `Idempotency-Key`, and strong quoted `If-Match` over AccessControl-owned `MemberAccessVersion`. Workspace-owned team links are not mutated; the currently admitted `teamIds` value is only `[]`. |
| `getWorkspaceAccessDirectory` | `GET /access/directory` | AccessControl returns the composed Workspace directory under `access.read`, after Trusted Workspace resolution, and appends AccessControl-owned read evidence only after a successful composition. It performs no business-state mutation and has no idempotency or `If-Match` requirement. |

Role create/replace owns role capability assignment and role data-scope/field-security policy persistence. Member-access replacement owns membership-role assignment only. Each fresh successful mutation writes one immutable `ACCESS_GOVERNANCE_COMMAND` audit, one operation-specific durable outbox event, and increments the AccessControl Workspace directory revision exactly once; committed replay writes none of those effects again. Role replace/archive use role version concurrency, while member-access replacement uses its distinct AccessControl-owned version. These operations access Workspace and IdentityAuth facts only through admitted read-only contracts and never through foreign persistence.

The four administrative mutations above also implement the narrow transport-security amendment
`PROJECT_EXTENSION_ACCESS_CONTROL_ADMIN_REQUEST_BODY_LIMITS`, frozen in
`ACCESS_CONTROL_ADMIN_REQUEST_BODY_LIMITS.md`. Each body is limited to 65,536 raw bytes and an
authorized oversized request returns `413 PAYLOAD_TOO_LARGE`. The boundary reads at most one byte
beyond the limit and does not expose the size result until after the handler's existing
application-level `access.configure` decision; required metadata and `If-Match` retain response
precedence over that result. The adopted OpenAPI's omission of 413 is superseded only for these four
operations. No AccessControl business semantic is broadened by this amendment.

Admitting policy persistence does not invent policy meaning. `TEAM` and `CUSTOM` remain denied by the evaluator, and `MASKED` remains enforced by withholding/reporting `HIDDEN` because no masked rendering is authoritative. `OWN` and `WORKSPACE` retain their already admitted evaluator behavior. Data-scope and field-security policies have no independent revision; the enclosing role version and Workspace directory revision are the controlling versions.

The following operations remain fail-closed `AUTHORITY_GAP`:

- `inviteWorkspaceMember`, `resendWorkspaceInvitation`, and `revokeWorkspaceInvitation`: invitation intent, token issuance/security lifecycle, target binding, expiry, replay protection, and cross-owner membership mutation are not sufficiently admitted;
- `acceptWorkspaceInvitation`: the same invitation-security gap plus Workspace-owned membership mutation remains unresolved;
- `provisionWorkspaceMember`: no approved atomic Workspace-membership and AccessControl-assignment contract exists;
- `changeWorkspaceMemberStatus`: Workspace owns membership validity, while required IdentityAuth session-revocation coordination is not admitted;
- `rotateManagedMemberPassword`: IdentityAuth owns credentials and no approved administrative credential contract/security semantics exists.

`evaluateEffectiveRecordAccess` is no longer an authority gap. The missing business-owner record-fact contract that blocked it now exists and the operation is implemented; see *AccessControl record access implementation authority* below.

Development bootstrap is configuration-only, Development-only, idempotent, and disabled by default. It creates no public provisioning endpoint and does not define production role names. Runtime verification on 2026-08-23 used isolated LocalDB databases and proved authorized context resolution, denial for an active member without the required capability, rejection of caller-supplied role/capability spoofing, foreign-Workspace isolation, and B01/B02 regressions. Later focused verification proves the five administrative operations above. The invitation, membership-lifecycle/session, member-provisioning, and managed-credential authority gaps remain unimplemented and must not be invented. No claim of a complete or production-ready AccessControl module follows from these narrower results.

## AccessControl record access implementation authority

AccessControl admits and implements `evaluateEffectiveRecordAccess`: `POST /access/records/evaluate`, and enforces the same decision at the business application boundary. It is not an administration surface. The later administrative operations are governed by the current B03 reconciliation above; the invitation, membership-lifecycle/session, member-provisioning, and managed-credential operations listed as `AUTHORITY_GAP` remain fail-closed.

The four concerns below are deliberately reported apart, because an earlier revision of this section conflated the first two and overstated what was protected.

### POLICY EVALUATION — `IMPLEMENTED`, `VERIFIED`

The chain is authenticated B01 identity, then B02 trusted Workspace/membership, then the AccessControl-owned capability authorization, then authoritative record facts from the owning module, then record-scope evaluation, then the field-security projection, then immutable AccessControl decision evidence. The trusted Workspace is taken only from `ICurrentWorkspace.Require()`; the response `workspaceId` is that trusted identifier and never a caller-supplied one. The request contract carries no Workspace, owner or team member and rejects unmapped members, so a caller-supplied `ownerId`, `workspaceId` or `teamId` is a `422 VALIDATION_FAILED` rather than a silently ignored field.

`resourceKey` is a request selector matched against the registered record-fact owners; the response echoes the owner's canonical key. `recordId` is an optional `EntityId`.

### BACKEND ENFORCEMENT — `IMPLEMENTED`, `VERIFIED`

**This is the correction.** The operation above only *reports* a decision. Reporting it protects nothing on its own: until this work, a caller who ignored the frontend could read and mutate a record the evaluation declared invisible, because Support authorized `support.read` plus trusted Workspace and stopped there. A consumer declining to draw a record is not a security boundary.

AccessControl therefore owns `IRecordAccessEvaluator`, an internal application contract - never an internal HTTP call - that business owners enforce against. `AuthorizeResourceAsync` performs the capability authorization exactly once per request and returns the caller's scope filter, per-field enforcement and effective capabilities. `AuthorizeRecordAsync` applies the frozen scope algorithm to facts the owner supplies from the record it already loaded, and writes the decision evidence. Facts supplied by an owner are trusted because the owner is authoritative for them; facts arriving in an HTTP request never are. The public operation uses the same evaluator, so what a consumer is told and what the server enforces are produced by one code path and cannot drift.

Support is the reference implementation and enforces at every boundary:

- `getSupportCase` refuses a record outside the caller's record scope with `RESOURCE_NOT_FOUND`, identical to an unknown record and to a foreign-Workspace one.
- `replaceSupportCaseProfile`, `assignSupportCase`, `transitionSupportCase`, `addSupportCaseReply` and `addSupportCaseInternalNote` each run the record guard inside the mutation transaction, **before the idempotency lookup**. Record scope is authorization rather than a business precondition, and capability authorization already runs ahead of that lookup, so a caller who no longer reaches a record cannot replay a committed command against it.
- `listSupportCases` resolves the scope once and pushes it into the owner query as a predicate ahead of the count, the ordering and the page, so a hidden row affects neither `totalCount` nor a page boundary. A denied scope returns an empty page. No list row is evaluated individually; a list request writes zero record decisions.

Business modules hold no scope rule and no field rule of their own. Support reads no AccessControl persistence and AccessControl reads no Support persistence; `UnicoreCRM.Platform` gains no project reference.

### FIELD ENFORCEMENT — `IMPLEMENTED`, `VERIFIED`, with one recorded limitation

Field access was previously presentation-only. It is now applied before serialization, on the detail read, the list projection, every mutation response and the replayed idempotency response - the last of these because stored evidence would otherwise leak a value the caller's current policy withholds.

Frozen representation rules, none of which invent a semantic:

- **Withheld on an optional wire field** - the property is omitted, which is the representation the contract already defines for an absent optional value.
- **Withheld on a required wire field** - the operation is refused with `ACCESS_DENIED`. No admitted absent or masked representation exists for a required field, so returning the value would break the policy and substituting a placeholder would break the contract. Refusal discloses nothing, because it is reached only after record scope has already granted the caller knowledge of the record.
- **A restrictive policy on a field the owner does not declare** - refused for the same reason: an unenforceable policy must not silently do nothing.
- **`MASKED`** - enforced by withholding the value, exactly as `HIDDEN`, and *reported* as `HIDDEN`. No masking representation is admitted anywhere, so none is invented, and reporting `MASKED` would promise a masked value that never arrives.
- **`READ_ONLY`** - readable, and any command that would change the value is refused. The check compares the requested value against the stored one, so rewriting a field with the value it already holds is not a write.
- A field is never more permissive than the record it belongs to.

`AUTHORITY_GAP`: the frontend requests field keys (`subject`, `assigneeId`, `queueId`, `slaPolicyId`) that are not the Support wire field names, and no authority maps the two vocabularies. A policy written against the frontend spelling is therefore unenforceable and fails the operation closed rather than being ignored.

### POLICY KEY CONSISTENCY — `IMPLEMENTED`, `VERIFIED`

Resource keys and field keys have one canonical comparison rule, `OrdinalIgnoreCase`, applied by effective-policy aggregation, the fact-provider registry, scope resolution, field resolution and owner-side enforcement alike. Aggregation previously grouped case-sensitively while resolution matched case-insensitively, so two roles spelling one key differently produced two effective entries and resolution could return either - a policy bypass rather than a cosmetic inconsistency. The emitted spelling is the ordinal-least member of the group, so the projection is deterministic and unchanged whenever the stored rows already agree.

### RECORD SCOPE — `WORKSPACE` and `OWN` `IMPLEMENTED`; `TEAM` and `CUSTOM` `AUTHORITY_GAP`

- `WORKSPACE`: any record the owner reports as present in the trusted Workspace.
- `OWN`: only when the owner member reference equals the caller's trusted `MemberId`. A record with no owner reference is denied.
- Absent policy row: `WORKSPACE`. The stored model is explicit-restriction - a row restricts, no row expresses no restriction - and the implemented `ITaskSummaryReader`, `ILeadSummaryReader` and `IDealSummaryReader` already apply this rule to the same rows. Reading absence as denial here would have created a second, contradictory authority over one set of rows.
- `TEAM`: **denied everywhere, evaluation and enforcement alike.** No authoritative team ownership or team membership exists in the backend; `Team` is an enum value in the policy table and a wire value in the projection, with nothing behind it.
- `CUSTOM`: **denied everywhere.** `RoleDataScopePolicy.AllowedOwnerIdsJson` exists in the model with no admitted semantics and no writer.
- There is no stored `NONE`/`DENY` value. Total denial is expressed by the absent capability and by the unsupported-scope denial.

### CAPABILITY INTERACTION AND RESOURCE-LEVEL SEMANTICS — `IMPLEMENTED`, `VERIFIED`

Record access is strictly additional and never restores a capability. A missing operation capability fails the endpoint closed with `403` before any record work; removing a resource read capability denies the record regardless of scope.

Resource-level and record-level questions are answered differently, deliberately. **With no record identifier the caller is asking what the resource permits, so a command is granted from its own capability alone** - `support.create` no longer requires `support.read`, which an earlier revision made it do by accident. Record scope is not applied and is reported as `RECORD_SCOPE_NOT_EVALUATED`; the response does not pretend a record was evaluated. With a record identifier the commands target that record, so they additionally require it to be readable and in scope.

Owner-declared capabilities are the owner's own vocabulary, never inferred from the resource key. Support declares read `support.read`, update `support.update`, the commands `support.create`, `support.update`, `support.assign` and the four transition commands mapped to `support.update`, and declares no delete, export or approval capability because no admitted operation exists behind them.

### SUPPORT OWNER ASSIGNMENT AUTHORITY — `IMPLEMENTED`, `VERIFIED`

Assignment authority is `support.assign`. `ReplaceSupportCaseProfileRequest` also carries `ownerId` and the aggregate writes it, so a caller holding only `support.update` could previously assign, reassign or clear the owner through a profile replacement and quietly acquire the assignment privilege. Support now requires `support.assign` whenever a command changes or clears the owner, and rewriting the owner with the value it already holds is not an assignment. The same rule is applied to `createSupportCase`: naming an owner at creation is an assignment, and `support.create` alone would otherwise be a second door to the identical escalation. `support.update` is not broadened, and the admitted `assignSupportCase` path is unchanged.

### AUDIT — `IMPLEMENTED`, `VERIFIED`

Every evaluation and every enforced record decision appends one immutable `access.RecordAccessDecisions` row carrying the trusted Workspace, the membership and member, the resource key, the record identifier, the gating capability, allow/deny, the evaluated scope, the decision code, the enforcement point, the decision-relevant field restrictions, a policy fingerprint, the request and correlation identifiers and the evaluation instant. The enforcement point distinguishes a reported evaluation from an enforced owner operation, which were previously indistinguishable.

The owner member identifier is deliberately not stored: it is foreign business data, and the derived `OwnerMatch` boolean is what the decision turned on. Field keys are policy identifiers, not values; no business field value is recorded.

No data-scope or field-security row has an independent policy revision. The fingerprint is a deterministic digest of the effective capabilities, data scopes and field policies used, which is the minimum that lets two decisions be compared - identical fingerprints mean identical effective policy. It is **not** a policy version and must not be treated as one. Policy persistence administration is now implemented through role create/replace; concurrency is governed by the enclosing role version and the Workspace access-directory revision.

### CONNECTED CONSUMER BEHAVIOR — `VERIFIED`, and not load-bearing

The frontend consumes the evaluation to decide what to draw. That is a usability behavior, not a security boundary, and the acceptance suite is written so it cannot be mistaken for one: two of its tests bypass the browser entirely and call the Support API directly.

### RUNTIME VERIFICATION

`backend/scripts/verify-access-control-record-access.ps1` provisions an isolated LocalDB database, starts a real ApiHost against it and reports `PASS=404 FAIL=0` at the time of writing (`141` when
this section was written; the suite has grown as owner enforcement was added). Beyond the evaluation checks it proves, by calling the business API directly: a hidden record is refused by `GET /support/cases/{caseId}` and is byte-indistinguishable from an unknown one; profile replacement, assignment, transition, reply and internal note against a hidden record all fail closed and mutate nothing; an `OWN` list returns only the caller's records and neither `totalCount` nor pagination counts hidden rows; `WORKSPACE` restores the other-owner record through the same path; `TEAM` and `CUSTOM` refuse the read and empty the list; a `HIDDEN` field is absent from the raw backend JSON on both the detail and the list; a `READ_ONLY` field reads but cannot be written, including when the policy row is spelled in different casing; a restrictive policy on a required field fails the read closed without ever emitting the value; `support.update` alone can neither reassign nor clear an owner while `support.assign` still assigns through the admitted path; mixed-case resource keys collapse to one effective entry; `support.create` survives losing `support.read` while record-level commands do not; owner enforcement writes its own decision evidence; a list request writes no per-row decision; and an enforced read authorizes exactly once. The suite now also exercises the Contacts owner described below. `dotnet ef migrations has-pending-model-changes` reports no pending AccessControl model change.

`verify-support-core.ps1` re-run unchanged reports `PASS=83 FAIL=0`, so Support's own domain, persistence, concurrency, idempotency, audit and outbox invariants are unaffected.

Data-scope and field-security policies have no admitted write operation, so the verifier seeds them directly into the AccessControl-owned tables. That exercises the stored policy the evaluator actually reads without inventing an administration surface this scope does not admit.

### DELIBERATELY DEFERRED

Record-fact providers now exist for Tasks, Leads, Deals, Products, Support, and Contacts, and all six enforce record access at their own application boundary - see *AccessControl enforcement retrofit for Tasks, Leads, Deals and Products* and *Contacts Read Core implementation authority*. The three summary readers no longer carry an inline copy of the record-scope and field-visibility rules; they were rewritten onto the canonical evaluator, so no second authorization authority remains anywhere in the backend.

Every other resource key - Customers, Organizations, Quotes, Orders and the rest - still has no registered fact owner and fails closed with `RESOURCE_FACT_AUTHORITY_UNAVAILABLE`. Those modules are unimplemented rather than unenforced.

## AccessControl enforcement retrofit for Tasks, Leads, Deals and Products

Support was the reference implementation of backend record-access enforcement. Tasks, Leads, Deals
and Products previously **evaluated** record access through `evaluateEffectiveRecordAccess` but
**enforced** only capability authorization plus Workspace isolation, so a caller who ignored the
frontend could read and mutate a record the evaluation declared invisible. All four now enforce the
same canonical decision through the same authority.

Three of them additionally carried their own inline copy of the record-scope and field-visibility
rules inside `ITaskSummaryReader`, `ILeadSummaryReader` and `IDealSummaryReader`. Those copies were a
second authorization authority over one set of stored policy rows. **They have been removed**; no
inline scope or field rule remains anywhere in the backend, and `IRecordAccessEvaluator` is the only
evaluator.

### Shared, per module

| | Tasks | Leads | Deals | Products |
|---|---|---|---|---|
| RECORD FACT PROVIDER | `IMPLEMENTED`, `VERIFIED` | `IMPLEMENTED`, `VERIFIED` | `IMPLEMENTED`, `VERIFIED` | `IMPLEMENTED`, `VERIFIED` |
| RESOURCE DESCRIPTOR | `tasks` | `leads` | `deals` | `products` |
| WORKSPACE SCOPE | `IMPLEMENTED`, `VERIFIED` | `IMPLEMENTED`, `VERIFIED` | `IMPLEMENTED`, `VERIFIED` | `IMPLEMENTED`, `VERIFIED` |
| OWN SCOPE | `IMPLEMENTED`, `VERIFIED` (assignee) | `IMPLEMENTED`, `VERIFIED` (owner) | `IMPLEMENTED`, `VERIFIED` (owner) | `AUTHORITY_GAP`, fails closed |
| TEAM | `AUTHORITY_GAP`, fails closed | `AUTHORITY_GAP`, fails closed | `AUTHORITY_GAP`, fails closed | `AUTHORITY_GAP`, fails closed |
| CUSTOM | `AUTHORITY_GAP`, fails closed | `AUTHORITY_GAP`, fails closed | `AUTHORITY_GAP`, fails closed | `AUTHORITY_GAP`, fails closed |
| DETAIL ENFORCEMENT | `IMPLEMENTED`, `VERIFIED` | `IMPLEMENTED`, `VERIFIED` | `IMPLEMENTED`, `VERIFIED` | `IMPLEMENTED`, `VERIFIED` |
| LIST ENFORCEMENT | `IMPLEMENTED`, `VERIFIED` | `IMPLEMENTED`, `VERIFIED` | `IMPLEMENTED`, `VERIFIED` | `IMPLEMENTED`, `VERIFIED` |
| MUTATION ENFORCEMENT | `IMPLEMENTED`, `VERIFIED` | `IMPLEMENTED`, `VERIFIED` | `IMPLEMENTED`, `VERIFIED` | `IMPLEMENTED`, `VERIFIED` |
| FIELD ENFORCEMENT | `IMPLEMENTED`, `VERIFIED` | `IMPLEMENTED`, `VERIFIED` | `IMPLEMENTED`, `VERIFIED` | `IMPLEMENTED`, `VERIFIED` |

### Owner facts, and what was deliberately not substituted for one

**Tasks — `TaskItem.AssigneeId`.** `PROVEN`. It is the only member reference the Task aggregate
records, it is validated on write through the narrow Workspace active-member contract, and the
already-implemented Tasks summary reader has treated it as the OWN-scope subject since B04. Neither
the activity actor nor the audit actor is substituted for a record owner.

**Leads — `LeadProfile.OwnerId`.** `PROVEN`. Required by the profile and validated on write through
the same narrow Workspace contract.

**Deals — `DealProfile.OwnerId`.** `PROVEN`. Required, validated on write, and the subject of the
admitted `assignDealOwner` operation.

**Products — none.** `AUTHORITY_GAP`. The Product aggregate carries no member reference of any kind:
no owner, no assignee, no steward. Nothing - creator, last editor, category or supplier - is
substituted for one, so the fact provider reports a Product with no owner reference and **OWN scope
denies every Product record**, in the detail read and in the list alike. Product ownership was not
invented to make the scope evaluable.

### A persistence change the pushdown required

Leads and Deals persist their whole profile as a single JSON column, so `Profile.OwnerId` cannot
appear in a SQL predicate - the query simply fails to translate. The record scope has to be pushed
into the query rather than filtered in memory, so both aggregates gained a `ScopeOwnerId` column
that is a **queryable projection of the owner already inside the profile**, maintained wherever the
profile is assigned. It is derived state, never an independent fact: the profile remains the source
of truth, and the migrations backfill existing rows from that same JSON rather than leaving them
empty, because an empty value would make OWN deny every pre-existing record.

This is a persistence and queryability change. No aggregate semantic, lifecycle rule or wire contract
was altered to make the security evaluable.

### List enforcement

Every list resolves the scope **once** and applies it as a query predicate before the count, the
ordering and the page, so a hidden row affects neither `totalCount` nor a page boundary. A denied
scope returns an empty page rather than a filtered view. No list evaluates a record decision per row:
the runtime verifier asserts that a list request writes **zero** record decisions and performs
**exactly one** capability authorization, for each of the four modules.

`getDealForecastSummary` aggregates deal amounts, so the same predicate is applied there: a deal
outside the caller's record scope reaches neither the list nor the forecast totals.

Batch operations (`archiveDealsBatch`, `archiveProductsBatch`, `restoreProductsBatch`) authorize the
resource once and then authorize each record the caller explicitly named. That is one decision per
named record, which is inherent to a batch, not an N+1 over a list. Until the hardening below they
performed that authorization **after** the idempotency lookup, so a committed batch could be
replayed after the caller lost access to a named record; see *BATCH ENFORCEMENT*. The Products batch
additionally loaded every named Product before testing its Workspace - see *A. PRODUCT WORKSPACE
ISOLATION* under *AccessControl final hardening*.

**Recorded, pre-existing and unchanged:** `listDeals` and `getDealForecastSummary` still perform
their search, ordering and paging in memory after loading the Workspace's deals. The security
predicate is now in SQL, so hidden rows are never materialised, but the remaining in-memory work is a
pre-existing performance characteristic of those two operations and was deliberately not refactored
here.

### Mutation enforcement

Every existing-record mutation runs the record guard inside the mutation transaction and **before the
idempotency lookup**. Record scope is current authorization rather than a business precondition, so a
caller who has lost access to a record cannot replay a previously committed command against it. The
existing owner-local idempotency guarantee is otherwise untouched: mutable business preconditions
still run only on the new-execution path, so a member deactivated after a commit still cannot
invalidate that command's replay.

Creation is unguarded by **record scope** in every module: there is no prior record to
authorize. It is **not** unguarded by field-write policy - see *FIELD WRITE ENFORCEMENT* under
*AccessControl system-wide enforcement hardening*, which supersedes the earlier wording here.

### Field enforcement and vocabulary

Each owner declares the field keys it can actually enforce, generated from its own wire record so the
vocabulary cannot drift from what the module projects:

- Tasks — the 19 `TaskReadModel` properties.
- Leads — the 55 `LeadDocument` properties. The wire schema declares further properties
  (`notes`, `relationshipRef`, `dealRef`, `qualifiedDealId`, `archivedAt`, the merge/consent family)
  that Leads does not project at all.
- Deals — the 34 `DealReadModel` properties.
- Products — the 24 `ProductDocument` properties.

A policy naming a field the returned representation makes required cannot be honoured and **fails
the operation closed** rather than being silently ignored. A key outside a module's vocabulary is
**not readable and not writable** - see *UNKNOWN-FIELD FAIL-CLOSED* below, which supersedes the
earlier statement that an undeclared key failed the whole operation closed.
`MASKED` is enforced by withholding the value and reported as `HIDDEN`, because no masking
representation is admitted anywhere. `READ_ONLY` is readable and refuses writes before any business
state changes. These are the rules already frozen for Support, applied unchanged.

`AUTHORITY_GAP`: no authority maps the frontend form vocabulary onto these backend field names, so a
policy written against a frontend spelling remains unenforceable and fails closed.

### Policy key consistency

Resource keys and field keys use one canonical `OrdinalIgnoreCase` rule end to end - aggregation,
provider registry, scope resolution, field resolution and owner-side enforcement. The verifier proves
a `LEADS`-spelled field policy and a `SUPPORT`-spelled scope policy are still enforced against the
lowercase resource keys the modules declare.

### Runtime verification

`backend/scripts/verify-access-control-record-access.ps1` reported **`PASS=211 FAIL=0`** against an
isolated database and a real ApiHost when this section was written, and reports **`PASS=309 FAIL=0`**
after the hardening below. Beyond the Support coverage it already carried, it proves for
the retrofitted modules, by calling the business API directly with no browser involved: an OWN-scoped
caller reads its own Task, Lead and Deal and receives `404` for another member's; the hidden record
leaks no title, display name or deal name; direct `complete`, `disqualify` and `archive` calls against
a hidden record are refused and mutate nothing; OWN lists exclude hidden rows from both the items and
`totalCount` and do not pad pagination; the Deal forecast excludes a hidden deal's amount; `WORKSPACE`
restores every record through the same path; `TEAM` and `CUSTOM` fail closed in all four modules for
both detail and list; Products fails OWN closed for having no owner concept; a `HIDDEN` field is
absent from the raw backend JSON; a restrictive policy on a required field fails the read closed
without emitting the value; losing a read capability denies the record regardless of scope; each of
the four lists writes zero per-row decisions and authorizes exactly once; and enforcement decision
evidence is written for every module.

It also covers the three rewritten summary readers through their only consumer, `POST /ai/advisories`:
all three references resolve, a record hidden by OWN scope is not summarised for AI, and the caller's
own records remain summarisable. That coverage was added because `verify-ai-assistant.ps1` could not
run at the time; it runs again now, and both harnesses cover the readers.

`verify-support-core.ps1` re-run unchanged reports **`PASS=83 FAIL=0`**.

### Two stale harness fixtures corrected, not weakened

`verify-initial-workspace-provisioning.ps1` and `verify-initial-workspace-provisioning-upgrade.ps1`
both failed before any change in this work, on a capability set that is correct. Both carry a copy of
`InitialWorkspaceAccessPolicy.Capabilities`, and neither was updated when the Products Core and
Support Core tasks added `products.*` and `support.*` to that policy: the first asserted 21 against an
actual 25, and the second seeded a legacy role with 17, which the convergent participant then refused
to recognise. Both lists were realigned with the policy they exist to mirror. No assertion was relaxed.

### Environment-blocked, not failed

**Superseded.** `verify-products-core.ps1` and `verify-ai-assistant.ps1` were blocked by a harness
defect, not an environment one, and both now run and pass - see *Two blocked verifiers repaired and
run* under *AccessControl system-wide enforcement hardening*. `verify-inbound-lead-webhook.ps1`
was not in scope of that repair and its status is unchanged.

## AccessControl system-wide enforcement hardening

The retrofit above put Tasks, Leads, Deals and Products on the canonical evaluator. A source review
of that result found six enforcement defects that were still reachable through the business API
itself, plus two unsafe defaults inside AccessControl. All eight are closed here. This section is the
authority for **which enforcement layer does what**, because the earlier sections stated some of it
in aggregate and, in three places, overstated it.

The layers are deliberately reported apart. A `PASS` on one is not a `PASS` on another.

### CAPABILITY EVALUATION — `IMPLEMENTED`, `VERIFIED`

One authoritative evaluation per record-access request, of the **actual business capability**.

`RecordAccessEvaluator.AuthorizeResourceAsync` previously authorized `workspace.context.resolve` to
obtain a context and then tested `requiredCapability` against the returned capability set. The
decision was correct, but the `access.AuthorizationDecisions` evidence recorded the context
capability rather than the capability the operation actually required, and the policy was loaded for
a question nobody asked. `IAccessContextAuthorizer.AuthorizeWithContextAsync` now loads the effective
policy once, evaluates the supplied business capability, audits *that* capability, and returns the
effective context from the same evaluation. The context is returned on a denial too, because a denied
caller is still a resolved membership of the trusted Workspace and the projection has to be able to
answer "denied, for this Workspace" without a second policy load that could observe different state.

`POST /access/records/evaluate` keeps its own registry-declared operation capability,
`workspace.context.resolve`, and authorizes it explicitly. That is a second decision for that one
endpoint, and it is deliberate: being told what one *may* do with a record is a context question,
while whether that record may actually be read is a resource question, and the two are audited under
the capability each really evaluated. Every business owner operation performs **exactly one**
authorization, of its own capability.

Verified: `completeTask` writes one decision naming `tasks.complete`; `getLead` one naming
`leads.read`; `archiveProduct` one naming `products.delete`.

### RECORD ENFORCEMENT — `IMPLEMENTED`, `VERIFIED`

Unchanged from the retrofit except for the read rule below: `WORKSPACE` and `OWN` are enforced,
`TEAM` and `CUSTOM` fail closed, and a record outside scope is reported as not found.

#### The read-versus-command rule is now frozen, and identical on both sides

**Rule A is canonical: a record-targeting mutation requires the resource read capability, the
operation capability and record scope.** It is taken from the canonical design baseline, which states
that a record read requires workspace match, the resource read capability and an allowed
ownership/data-scope decision, and that *mutating a permitted field still requires the command
capability* — the command capability is additional to readability, not a substitute for it.

Before this work the public evaluation applied Rule A while direct backend enforcement did not: a
caller holding `tasks.assign` but not `tasks.read` was refused by the evaluation and accepted by the
endpoint. `AuthorizeRecordAsync` now requires the owner-declared read capability alongside the
operation capability and record scope, so both sides are produced by the same code path. A record
decision denied for a missing read capability is audited as `RECORD_READ_CAPABILITY_DENIED` and is
reported to the caller exactly as any other record denial, so the code discloses nothing.

Resource-level semantics are unchanged: with no record identifier a command is still granted from its
own capability alone, so `support.create` and `tasks.create` do not require a read capability.

Verified as a matrix, per cell, twice — once as the evaluation reports it, once as the business API
enforces it: read=yes/command=yes allows, and read=no/command=yes, read=yes/command=no and
read=no/command=no all deny, with report and enforcement asserted equal.

### FIELD READ ENFORCEMENT — `IMPLEMENTED`, `VERIFIED`

> The representation override introduced here was an ad hoc runtime collection. It is now a closed
> declared type owned by the operation - see *E. REPRESENTATION-SPECIFIC FIELD WITHHOLDING* under
> *AccessControl final hardening*.

Unchanged in rule. One correction to which representation the required-field rule governs:

**Required-ness is a property of the representation being returned, not of the resource.** The
minimized summary contracts behind `ITaskSummaryReader`, `ILeadSummaryReader` and
`IDealSummaryReader` declare every field optional, so a withheld value has an admitted representation
there even where the module's full read model makes the same field required. Those three operations
now declare the fields they can return absent, and a restrictive policy on one of them withholds the
value instead of failing the read closed. Every other operation is unchanged: a restrictive policy on
a field its own representation makes required still fails closed with `ACCESS_DENIED`.

### FIELD WRITE ENFORCEMENT — `IMPLEMENTED`, `VERIFIED`

Two gaps are closed.

**Creation.** Creation has no prior record and therefore no record scope, but field policy still
governs writes. The ordering stated here was corrected afterwards: the create-time field-write guard
now runs on the new-execution path only - see *B. IDEMPOTENT REPLAY* under *AccessControl final
hardening*. `createTask`, `createLead`, `createDealCommand` and `createProduct` now refuse a
request that supplies a field the caller may not write — `HIDDEN`, `MASKED` or `READ_ONLY` alike —
rather than silently dropping the value, because silently dropping it would return a record that does
not match the request the caller believes it made. A field the create contract makes mandatory always
counts as written, so a non-writable required create field fails the creation closed. Create responses
and their replays are projected through current field security. This is the rule already frozen for
`createSupportCase`, applied unchanged.

The retrofit section's statement that *"creation is unguarded by design in every module"* was
correct only about record scope and is superseded here: creation is unguarded by **record scope**,
and is governed by **field-write policy**.

**Full-profile replacement.** `replaceLeadProfile`, the Deals `updateDealCommand` profile replacement
and `replaceProduct` ran the record guard without declaring which fields the replacement would
actually change, so a `READ_ONLY` field could be rewritten through a whole-profile PUT. All three now
compare the requested profile against the stored aggregate and check `CanWrite` only for the fields
whose value actually changes. Repeating a value unchanged is not a write and is not refused. This is
Support's `GuardProfileWrite` pattern, applied unchanged. Slice mutations already declared their
written fields and are untouched.

Verified per module: a forbidden field supplied at creation is refused with `403 ACCESS_DENIED` and
writes no audit record, no outbox message and no idempotency evidence; the same request without that
field still creates; a `READ_ONLY` field changed through a profile replacement is refused with `403`
and leaves the resource version unchanged; and the same `READ_ONLY` value repeated is accepted.

### BATCH ENFORCEMENT — `IMPLEMENTED`, `VERIFIED`

`archiveDealsBatch`, `archiveProductsBatch` and `restoreProductsBatch` authorized the resource, opened
the transaction and then performed the idempotency lookup **before** loading and authorizing the named
records. A caller who had access when a batch committed could therefore replay it after losing that
access and read the stored projection back.

The frozen order is now: resource capability authorization → transaction → load every explicitly named
record in the trusted Workspace → current record-access guard for every named record → idempotency
lookup → replay or conflict → business preconditions and version checks → mutation. Record scope is
current authorization and gates the replay; mutable business preconditions stay **after** the lookup,
so a committed batch still replays when access holds.

Batch responses now pass through `DealFieldSecurity.Project` and `ProductFieldSecurity.Project` on
both the committed and the replayed path, so a newly `HIDDEN` field cannot leak through old
idempotency evidence.

Verified: a batch committed under `WORKSPACE` scope is denied on replay once the caller's scope
narrows to `OWN` and a named record falls outside it, and the denial returns none of the stored
projection; the same key still replays while access holds and reports `REPLAYED`; a `HIDDEN` field is
absent from both the committed and the replayed batch response, value and key alike.

### IDEMPOTENCY AUTHORIZATION ORDER — `IMPLEMENTED`, `VERIFIED`

One order, everywhere: current authorization → record guard → idempotency lookup → replay or conflict
→ mutable business preconditions, for a new execution only → version check and mutation → commit. For
creation: capability and field-write authorization → idempotency lookup → replay → mutable external
and member validation, for a new execution only → create.

Tasks, Leads and Deals evaluated the active-assignee and active-owner preconditions *before* the
idempotency lookup in their shared mutation execution and in their create paths, so a member
deactivated after a command committed turned that command's replay into a validation failure. The
preconditions moved after the lookup, matching Support.

Verified: `assignTask` commits, the assignee is then suspended, and the same `Idempotency-Key` still
replays; the equivalent owner-validation replays for `replaceLeadProfile` and `assignDealOwner` also
survive a suspended owner; and a genuinely new command naming the suspended member is still refused,
so the durability is confined to replay.

### UNKNOWN-FIELD FAIL-CLOSED — `IMPLEMENTED`, `VERIFIED`

> See also *F. FIELD VOCABULARY* under *AccessControl final hardening*, which states the unknown-key
> rule and the required-field rule strictly apart after stale comments conflated them.

`RecordAccessAuthorization` resolved a field key with no enforcement entry to `READ_WRITE`. On an
internal security decision that meant a typo widened access: `assigneId` was writable because it was
unrecognised.

A field key the owner does not declare is now **neither readable nor writable**: `CanRead` and
`CanWrite` are both false, and the public projection reports `HIDDEN`. Owners must declare every field
they want enforced, which they already do from their own wire records. This matches the canonical
design baseline, which states that the connected projection is intentionally fail-closed and must not
copy the demo `READ_WRITE` default.

An undeclared key is **not** reported as an unenforceable policy: the owner never projects it, so
there is nothing for the operation to fail closed over. `UnenforceableFieldKeys` is now exactly the
fields the owner declares as required by the representation it is returning and which carry a
restrictive policy — the case where refusal is the only admitted answer.

Case-insensitivity is unaffected. Verified: `assigneId` and `subject` resolve `HIDDEN` for `tasks`
while `assigneeId`, `ASSIGNEEID`, `AsSiGnEeId` and `assigneeid` all resolve to the declared field, and
a policy row stored as `AsSiGnEeId` still restricts it.

### ACTIVITY ENFORCEMENT — `AUTHORITY_GAP`, fails closed

> **Superseded and split.** This section reported one gap. There are two - record scope and field
> security - and both are reported apart, with the reachability gate widened accordingly, in
> *C. TASKACTIVITY* under *AccessControl final hardening*.

`listActivities` and `logActivity` authorize `tasks.read` and `tasks.update` and operated
Workspace-wide regardless of the caller's effective `tasks` record scope, while a `TaskActivity`
carries `subject`, `body`, `actorId`, a relationship reference, a record reference for **any** module,
a record label and source evidence.

Neither admitted resolution can be proven from current authority:

- **Activities are inside the Tasks record scope** — not provable. A `TaskActivity` carries no task
  reference at all, so no Activity can be attributed to a Task. Its `actorId` is the actor, not one of
  the four admitted ownership attributes (`ownerId`, `assigneeId`, `createdBy`, `assignedTo`), so
  there is no owner an `OWN`, `TEAM` or `CUSTOM` scope could be evaluated against.
- **Activities are independent Workspace-scoped records** — not provable either. The operation
  registry gives both operations `resourceScope: WORKSPACE`, but it gives `listTasks` the same value
  while `listTasks` is `OWN`-filterable, so the field does not distinguish them. The module document's
  "activities are append-only evidence scoped to a workspace and record/customer reference" describes
  tenancy, which is true of every record in the system, not record-access scope. And both operations
  piggyback on the `tasks` capability and `tasks` resource key while declaring no resource descriptor
  or field vocabulary of their own.

**Resolution: `AUTHORITY_GAP`, failing closed rather than leaking.** Activities are reachable only
when the caller's effective `tasks` data scope is `WORKSPACE`. Under `OWN`, `TEAM` or `CUSTOM`,
`listActivities` returns an empty page and `logActivity` is refused with `403 ACCESS_DENIED`. No
ownership attribute, scope fact, resource key or field vocabulary was invented for Activities.

Freezing this requires a business decision that does not exist yet: either an authoritative Activity
ownership/scope fact, or an explicit `activities` resource descriptor with its own capability and
field vocabulary. Until then the restriction stands.

Verified: `WORKSPACE` logs and lists an activity; `OWN`, `TEAM` and `CUSTOM` each return zero
activities, leak no activity subject, and refuse `logActivity` with `403`.

### AUDIT EVIDENCE — `IMPLEMENTED`, `VERIFIED`

`access.AuthorizationDecisions` now names the capability the operation actually required, per the
first section above. `access.RecordAccessDecisions` is unchanged in shape and gains one decision code,
`RECORD_READ_CAPABILITY_DENIED`, distinguishing a record denied for want of the read capability from
one denied by scope. The distinction exists only in the evidence; the caller sees one answer.

The policy fingerprint remains a digest, **not** a policy version. Policy persistence administration
is implemented through role create/replace; no independent policy-row version was introduced.

### QUERY PERFORMANCE — `IMPLEMENTED`

Three indexes back the enforced security predicate, each chosen from the query the module actually
issues rather than added speculatively:

- `tasks.Tasks (WorkspaceId, AssigneeId, UpdatedAt, TaskId)` — `listTasks` narrows by Workspace and
  the AccessControl scope assignee before counting and paging, and its default order is `UpdatedAt`
  then `TaskId`, so the index covers the predicate, the count and the ordered page.
- `leads.Leads (WorkspaceId, ScopeOwnerId, UpdatedAt, LeadId)` — `listLeads` narrows by Workspace and
  scope owner and orders by `UpdatedAt` then `LeadId`.
- `deals.Deals (WorkspaceId, ScopeOwnerId, UpdatedAt, DealId)` — `readDealsAsync` narrows by Workspace
  and scope owner; Deals then orders and pages **in memory**, so here `UpdatedAt` and `DealId` are
  carried for covering only and the leading two columns are what turns the security predicate from a
  Workspace scan into a seek. The pre-existing in-memory ordering of `listDeals` and
  `getDealForecastSummary` is recorded above and was again not refactored.

Migrations `TaskOwnScopeIndex`, `LeadOwnScopeIndex` and `DealOwnScopeIndex` add them.
`dotnet ef migrations has-pending-model-changes` reports no pending change for `TasksDbContext`,
`LeadsDbContext`, `DealsDbContext` or `AccessControlDbContext`.

### RUNTIME VERIFICATION

`backend/scripts/verify-access-control-record-access.ps1` reported **`PASS=309 FAIL=0`** against an
isolated LocalDB database and a real ApiHost when this section was written, and reports
**`PASS=380 FAIL=0`** after the final hardening below. It keeps every module-specific assertion it already
carried and adds the batch-replay, batch-projection, create-write, profile-write, replay-durability,
read-versus-command matrix, capability-audit, Activity fail-closed and unknown-field cases described
above, plus a repeat of the no-N+1 and single-authorization assertions for all four modules and for
`/activities`.

`verify-support-core.ps1` re-run unchanged reports **`PASS=83 FAIL=0`**.

### Two blocked verifiers repaired and run

Both were blocked by harness defects in Windows PowerShell 5.1, not by API semantics. No API behavior
was changed to accommodate either.

- `Send-Json` in `verify-products-core.ps1` and `verify-ai-assistant.ps1` tested `$null -ne $body`. An
  unbound `[string]` parameter arrives as an empty string, so every `GET` attached an empty JSON body,
  which the .NET Framework `HttpClient` refuses. The test is now
  `-not [string]::IsNullOrEmpty($body)`, matching the record-access harness.
- `verify-ai-assistant.ps1` additionally never loaded `System.Net.Http`, which Windows PowerShell 5.1
  does not resolve on demand, so `[System.Net.Http.HttpClient]` did not exist. It now loads it, as
  every other verifier in that directory already did.

`verify-products-core.ps1` reports **`PASS`** with no failed check, including the batch-replay and
capability-denial cases. `verify-ai-assistant.ps1` reports **`PASS`** across all 35 checks, including
the field-level context filtering that the required-field correction above unblocked. The retrofit
section's "Environment-blocked, not failed" note is superseded for both scripts.

Recorded harness limitation, unchanged: `verify-ai-assistant.ps1` neither creates nor drops its
database, so it must be given a freshly created one. A previous failed run leaves its seeded field
policy behind and the next run fails on that residue rather than on a real defect.

### What this section does NOT claim

- `TEAM` remains `AUTHORITY_GAP`, denied everywhere.
- `CUSTOM` remains `AUTHORITY_GAP`, denied everywhere.
- `MASKED` remains `AUTHORITY_GAP` as a **representation**: it is enforced by withholding the value
  and reported as `HIDDEN`. It is **not** implemented masking, and must not be described as such.
- Product `OWN` scope remains `AUTHORITY_GAP`: the Product aggregate carries no member reference and
  none was invented, so `OWN` denies every Product.
- `TaskActivity` record-access semantics remain `AUTHORITY_GAP`, failing closed.
- Policy persistence administration is implemented through `createAccessRole` and
  `replaceAccessRole`: both can create normalized data-scope and field-security state, and replace
  can change/remove it under the enclosing role version. This does not resolve `TEAM`, `CUSTOM`, or
  `MASKED` rendering semantics; those remain fail-closed exactly as stated above.
- No claim of `ACCESSCONTROL FULL MODULE: PASS` is made. The claim supported by this evidence is
  narrower: **AccessControl system-wide enforcement for the currently implemented business modules —
  AccessControl, Tasks, Leads, Deals, Products and Support — is `PASS`.**


## AccessControl final hardening

A source review of the hardening above found five further defects, two of which were security defects
rather than inconsistencies. All five are closed here. This section is the authority for the five
semantics it names, and it supersedes any earlier statement that conflicts with them.

Status vocabulary used below, kept strictly apart: **PROVEN** means current authority settles the
business semantic; **IMPLEMENTED** means the backend enforces it; **VERIFIED** means runtime evidence
exists; **AUTHORITY_GAP** means no authority settles it and the system fails closed; **DEFERRED**
means admitted but deliberately not built. A semantic can be IMPLEMENTED and VERIFIED while the
question it answers is still an AUTHORITY_GAP - that combination means "we fail closed, provably".

### A. PRODUCT WORKSPACE ISOLATION — `IMPLEMENTED`, `VERIFIED`

**The defect.** Products loaded by global identifier and decided Workspace ownership afterwards:
`ReadProductAsync(productId)`, `LoadProductAsync(productId)`, `LoadProductsAsync(productIds)`, then
`ProductResource.ValidateOwned` answered `404 RESOURCE_NOT_FOUND` for an unknown identifier and
`403 WORKSPACE_MISMATCH` for a real Product of another Workspace. That difference is an existence
oracle: a caller who could guess an identifier could tell a real foreign Product from one that never
existed. The batch path had the same shape and leaked it for a whole batch at once.

**The fix is at the persistence query boundary, not after materialisation.** All three lookups now
take the trusted Workspace and constrain it in SQL - `WHERE WorkspaceId = @trusted AND ProductId = @id`,
and `AND ProductId IN (...)` for the batch. A foreign Product is never loaded, so nothing downstream
can inspect it, and `ProductResource.ValidateOwned` was replaced by `ProductResource.Resolve`, which
only turns "no row" into not found. `ProductErrors.WorkspaceMismatch` survives for exactly one
purpose that is not a record question: the AccessControl trusted-Workspace resolution failure.

Unknown, foreign-Workspace and scope-hidden Products are now externally indistinguishable across the
detail read, both derived projections, every mutation, both batch operations and the public record
evaluation.

Verified by direct HTTP with byte comparison of normalised payloads, and by asserting that a refused
foreign mutation changes no version, writes no audit record and writes no idempotency evidence. A
batch mixing a reachable Product with a foreign one is refused without revealing which was the
problem and archives nothing. The Products verifier assertion that previously pinned
`403 WORKSPACE_MISMATCH` was **replaced by the stronger anti-leak assertion**, not relaxed.

**Index coverage.** No index was added. The Products primary key is `ProductId`, so a
Workspace-scoped point lookup is a key seek plus a single-row predicate, and the batch is the same
seek repeated. Adding a `(WorkspaceId, ProductId)` index would duplicate the key without changing the
plan shape, so it would be speculative.

### B. IDEMPOTENT REPLAY — `IMPLEMENTED`, `VERIFIED`

**The defect.** Field-write authorization ran *before* the idempotency lookup in most modules,
because it was bundled into the record guard. A committed command therefore stopped replaying once a
field it had already written turned `READ_ONLY` or `HIDDEN` - even though a replay writes nothing.
Support's profile and transition paths checked inside the mutation callback and so were already
correct, which is exactly the inconsistency the review found.

**One frozen order now applies to every mutation path in every module:**

| Step | Applies to |
|---|---|
| 1. authenticate, resolve trusted Workspace | all |
| 2. authorize the current business capability | all |
| 3. begin transaction | all |
| 4. load the target record **inside the trusted Workspace** | existing-record mutations |
| 5. current record-access guard | existing-record mutations |
| 6. idempotency lookup | all |
| 7a. committed matching key → fingerprint check, project stored result through **current field-read** policy, return | replay |
| 7b. new key → **field-write** authorization for the fields this execution will change | new execution |
| 8. mutable business preconditions (active assignee, active owner) | new execution |
| 9. expected version and lifecycle | new execution |
| 10. mutate, stage audit/outbox/idempotency, commit, project through current field-read policy | new execution |

The rule in one sentence: **current capability and current record scope gate a replay; the current
field-read policy is applied to what a replay returns; the current field-write policy is required
only for a new execution, because a replay performs no write.**

Structurally, `EnforceRecordAsync` is now record scope only in all four retrofitted modules, and
field-write authorization moved to a separate `EnforceFieldWrite` applied after the replay branch.
Support's create path moved its create-write guard and its `support.assign` owner-privilege check to
the new-execution path for the same reason - both authorize a write that a replay does not perform.

Verified end to end: replay denied after record-scope loss and after capability loss (including the
resource read capability), replay still succeeding after `READ_WRITE → READ_ONLY` with the value
still readable, replay still succeeding after `READ_WRITE → HIDDEN` with the value and its key absent
from the projection, a new execution refused under either restriction while changing nothing, and a
committed replay surviving a suspended assignee or owner.

### C. TASKACTIVITY — two distinct gaps, both failing closed

The previous section reported one gap here. There are two, and they are reported apart.

| | Status |
|---|---|
| TASK ACTIVITY RECORD-SCOPE SEMANTICS | `AUTHORITY_GAP` |
| TASK ACTIVITY SAFE RECORD-SCOPE ENFORCEMENT | `IMPLEMENTED`, `VERIFIED` |
| TASK ACTIVITY FIELD-SECURITY SEMANTICS | `AUTHORITY_GAP` |
| TASK ACTIVITY FIELD READ ENFORCEMENT | `NOT IMPLEMENTED`; fails closed |
| TASK ACTIVITY FIELD WRITE ENFORCEMENT | `NOT IMPLEMENTED`; fails closed |

**Record scope is unprovable.** A `TaskActivity` carries no task reference, so no Activity can be
attributed to a Task. Its `actorId` is the actor, not one of the four admitted ownership attributes
(`ownerId`, `assigneeId`, `createdBy`, `assignedTo`), so there is no owner an `OWN`, `TEAM` or
`CUSTOM` scope could be evaluated against. The operation registry gives both Activity operations
`resourceScope: WORKSPACE`, but gives `listTasks` the same value while `listTasks` is OWN-filterable,
so the field does not distinguish them.

**Field security is separately unprovable.** No authority anywhere defines field security for
Activities - the capability matrix carries no field-security section for any resource, and Activities
declare no resource descriptor, no capability of their own and no field vocabulary. Nothing is
inherited from Tasks: an Activity does not have the `TaskReadModel` vocabulary, and Task field policy
is not Activity field policy.

**What is implemented is a reachability gate, and it is not field security.** `TaskActivitySecurity`
makes Activities reachable only when both hold:

1. the caller's effective `tasks` data scope is `WORKSPACE`; and
2. **no** restrictive field policy applies to `tasks` at all.

Condition 2 exists because an Activity carries `subject`, `body`, `recordLabel` and source evidence -
free text plus a label for a referenced record of *any* module - so an Activity can quote a value some
field policy withholds elsewhere, and no authority says which policy governs it. Refusing under any
restriction is conservative, not enforcement: it does not prove which Activity field a policy governs,
only that the caller is under some restriction that Activity content cannot be shown to respect.

`listActivities` returns an empty page and `logActivity` returns `403 ACCESS_DENIED` when either
condition fails. Verified for `OWN`, `TEAM`, `CUSTOM` and for `HIDDEN`, `READ_ONLY` and `MASKED`
policies, including that no refused `logActivity` was persisted.

Freezing real semantics needs a business decision that does not exist: either an authoritative
Activity ownership/scope fact, or an explicit `activities` resource descriptor with its own capability
and field vocabulary.

### D. DELEGATED LEAD INGRESS — capability/proof `IMPLEMENTED`, `VERIFIED`; field security `AUTHORITY_GAP`

**The authority question, answered honestly.** The inbound-webhook extension states that
"AccessControl evaluates the member's actual server-side `leads.create` capability through a delegated
internal authorization contract". That admits exactly one authorization concern for this path - the
capability - and says nothing about field security. The payload is a closed extension shape that
cannot carry a Workspace, member, owner or capability, and the owner comes from the binding rather
than the sender. Whether the delegated subject's field-security policy should additionally govern this
path is therefore an **`AUTHORITY_GAP`**. It is deliberately not answered: applying interactive field
policy would silently change admitted integration behaviour, and declaring the path exempt would be an
equally unproven claim. Current behaviour is preserved.

**DELEGATED LEAD CAPABILITY AUTHORIZATION: PROVEN / IMPLEMENTED / VERIFIED.** AccessControl performs
one canonical delegated evaluation for exactly `leads.create`, using the `TrustedWorkspaceContext`
resolved from the Integration binding's server-owned Workspace and delegated-member values. The
allowed and denied evaluations retain the canonical AccessControl decision audit with Workspace,
membership, capability, correlation and allow/deny evidence.

**DELEGATED SUBJECT SOURCE: server-resolved binding, not sender authority.** The signed provider
payload has no Workspace, owner, member, membership, capability, authorization-decision or admission-
proof field. Unknown payload fields are refused. `InboundIntegrationBinding.WorkspaceId` and
`DelegatedMemberId` are persisted server authority; Workspace resolves that pair to the active
`TrustedWorkspaceContext`, and the delegated subject must equal its `MemberId`. Sender headers do not
participate in that resolution.

**The first API defect, which is separate and is fixed.** The shared create execution took
`LeadAccess? access`, where `null` meant "skip interactive field-security enforcement". A nullable
parameter that doubles as a security switch makes forgetting to pass a decision indistinguishable from
deliberately skipping enforcement, and any future internal caller could have disabled enforcement by
omission.

That is replaced by a closed `LeadCreateAdmission` with exactly two sealed, private-constructed cases:

- `Interactive(LeadAccess)` - the full interactive model: the AccessControl decision governs which
  fields may be written and projects the response.
- `DelegatedIngress(DelegatedLeadIngressAuthorization)` - the admitted integration model.

**DELEGATED LEAD PROOF BOUNDARY: IMPLEMENTED / VERIFIED.** The later source review found that the
private proof constructor was not sufficient while `FromAllowedDecision` accepted the public,
caller-constructible `AccessAuthorizationDecision`. That generic decision does not bind its evaluated
capability and therefore could not prove `leads.create`; the factory also compared only `MemberId`, not
the exact Workspace and membership. `FromAllowedDecision` is removed. `InboundLeadIngress` now depends
on `IDelegatedLeadCreateAuthorizer`, whose contract exposes no `AccessRequirement` and whose sole
implementation hard-codes `LeadCapabilities.Create`. The implementation validates delegated subject
equality and the allowed decision context's exact `WorkspaceId`, `AccountId`, `MemberId` and
`MembershipId`. It is nested inside `DelegatedLeadIngressAuthorization` and owns the only invocation of
that proof's private constructor. Consequently an arbitrary generic decision, a decision for another
capability, or authority for another Workspace/member/membership cannot be converted to delegated Lead
admission.

The proof is an internal immutable application object with no HTTP or serialization contract. It is
created immediately after the one AccessControl evaluation, consumed by the same scoped ingress call,
and is not persisted, emitted to an outbox, or placed in a business payload. `LeadCreateAdmission`
remains closed to exactly `Interactive(LeadAccess)` and
`DelegatedIngress(DelegatedLeadIngressAuthorization)`; `LeadCreateExecution` has no nullable or boolean
authorization path. Before a new write, delegated admission additionally verifies that both the Lead
owner and execution provenance's delegated subject equal the member bound into the proof; the exact
trusted context carried by the proof supplies the execution Workspace. Delegated provenance and Lead
audit behavior are unchanged.

Verified: the interactive path still refuses a forbidden create field; allowed and denied delegated
`leads.create` evaluations are audited exactly once; a denied evaluation mutates no Lead, Lead audit,
Lead outbox or Lead idempotency state; invalid/mismatched binding authority fails closed; sender payload
and headers cannot choose Workspace or delegated subject; and the existing replay, changed-fingerprint,
owner assignment, recovery and concurrent-delivery behavior remains covered by the inbound harness.

**DELEGATED LEAD FIELD SECURITY: `AUTHORITY_GAP`.** Nothing in this proof hardening applies
interactive `LeadFieldSecurity` to delegated ingress or declares the Integration path exempt. The
existing admitted behavior remains unchanged until explicit authority resolves that separate question.

### E. REPRESENTATION-SPECIFIC FIELD WITHHOLDING — `IMPLEMENTED`, `VERIFIED`

**The concern.** The previous section let an operation pass an arbitrary runtime collection of field
keys it could return absent. The concept is right - required-ness belongs to the representation, not
the resource - but an ad hoc collection is a shape in which an operation could casually declare a
required field optional.

**The structural property that makes this safe, and why it was always narrow.** A representation is
consulted at exactly one place: whether a field belongs in `UnenforceableFieldKeys`. It never reaches
`CanRead`, `CanWrite` or the wire projection. The strongest thing a false declaration can achieve is
to turn "refuse the whole operation" into "withhold this value". **It can never turn a withheld value
into a returned one**, so it cannot disclose anything.

**The hardening makes that structural rather than incidental.** The ad hoc collection is replaced by
`RecordAccessRepresentation`, a closed public type built through a validating factory, declared as a
`static readonly` field on the operation that owns the representation, and passed through
`IRecordAccessEvaluator.AuthorizeResourceAsync` so AccessControl - not the module - decides.
A key the owner does not declare in its enforceable vocabulary is ignored, so an operation cannot
relax a policy on a field the owner never admitted. Every operation returning a full read model passes
`RecordAccessRepresentation.Full`, which overrides nothing; only the three minimized summary readers
declare one, and every property of `TaskSummaryProjection`, `LeadSummaryProjection` and
`DealSummaryProjection` except the identifier is nullable in the contract, so each declared field is
genuinely optional there.

Verified as a pair: under a `HIDDEN` policy on `tasks.title`, the summary contract succeeds with the
title absent, while `GET /tasks/{id}` and `GET /tasks` under the *same* policy still fail closed with
`403` and never emit the value.

### F. FIELD VOCABULARY — two rules, stated apart

Earlier comments said an unknown field key "fails the whole operation closed", which contradicted the
implemented behaviour. The two rules are distinct and both are now stated that way in every owner:

- **A key outside the owner's declared vocabulary** is not readable and not writable, and the public
  evaluation reports it `HIDDEN`. It does **not** by itself refuse the operation, because the owner
  never projects it and so has nothing to withhold.
- **A key inside the vocabulary that the representation being returned makes required**, carrying a
  restrictive policy, cannot be honoured at all and refuses the operation.

Confirmed per resource - `support`, `tasks`, `leads`, `deals`, `products` - for canonical resource
key, read capability, command capability mapping, enforceable field vocabulary, required/optional
status, write vocabulary, `OrdinalIgnoreCase` canonicalization and unknown-field behaviour. The
frontend form vocabulary (`subject`, `assigneeId`, `queueId`, `slaPolicyId`) is **not** accepted:
no authoritative mapping exists, so it remains an `AUTHORITY_GAP` and those keys fail closed.

### RUNTIME VERIFICATION

| Harness | Result |
|---|---|
| `verify-access-control-record-access.ps1` | RUN, **PASS=380 FAIL=0** |
| `verify-support-core.ps1` | RUN, **PASS=83 FAIL=0** |
| `verify-products-core.ps1` | RUN, **PASS**, 71 checks |
| `verify-ai-assistant.ps1` | RUN, **PASS**, 35 checks |
| `verify-inbound-lead-webhook.ps1` | RUN, **PASS**, 30 checks |
| `verify-initial-workspace-provisioning.ps1` | RUN, **PASS**, 109 checks |
| `verify-initial-workspace-provisioning-upgrade.ps1` | RUN, **PASS**, 111 checks |

`verify-inbound-lead-webhook.ps1` was repaired the same way the other two were - it attached a body to
`GET`, never loaded `System.Net.Http`, and used `[Convert]::ToHexString` and `SHA256.HashData`, both
.NET 5+ APIs absent from the .NET Framework runtime behind Windows PowerShell 5.1. The replacements
are exact equivalents. No assertion, input or expected value was relaxed in any harness; the one
Products assertion that changed was **strengthened** from pinning the leak to asserting its absence.

`dotnet ef migrations has-pending-model-changes` reports no pending change for `TasksDbContext`,
`SupportDbContext`, `LeadsDbContext`, `DealsDbContext`, `ProductsDbContext` or
`AccessControlDbContext`. No migration was added by this work. The backend builds with zero errors and
zero warnings under `-warnaserror`.

### G. REMAINING AUTHORITY GAPS AND DEFERRALS

| Gap | Status |
|---|---|
| `TEAM` data scope | `AUTHORITY_GAP` - denied everywhere |
| `CUSTOM` data scope | `AUTHORITY_GAP` - denied everywhere |
| `MASKED` representation | `AUTHORITY_GAP` - enforced by withholding, reported as `HIDDEN`; **not** implemented masking |
| Product member ownership | `AUTHORITY_GAP` - no member reference in the aggregate; `OWN` denies every Product |
| TaskActivity record-scope semantics | `AUTHORITY_GAP` - fails closed |
| TaskActivity field-security semantics | `AUTHORITY_GAP` - field enforcement `NOT IMPLEMENTED`, fails closed |
| Delegated Lead ingress field security | `AUTHORITY_GAP` - capability-only admission preserved, now typed |
| Frontend/backend field-vocabulary mapping | `AUTHORITY_GAP` - unmapped keys fail closed |
| AccessControl policy persistence administration | `IMPLEMENTED` through `createAccessRole` and `replaceAccessRole`; `TEAM`, `CUSTOM`, and masked-rendering semantics remain unresolved and fail-closed |

### H. WHAT MAY AND MAY NOT BE CLAIMED

**May be claimed:** *AccessControl system-wide enforcement for the current implemented core record
resources - Support, Tasks, Leads, Deals and Products - is `PASS`.* No Product foreign-Workspace
existence leak remains; replay semantics are consistent across all five modules; current capability
and current record scope cannot be replay-bypassed; a replay cannot leak a currently hidden field;
field-write policy cannot be bypassed on a new execution; unknown fields cannot widen access; no
nullable authorization context acts as a generic bypass; direct-HTTP negative controls pass; and all
five modules share the one canonical evaluator.

**May not be claimed:** *ACCESSCONTROL FULL MODULE: PASS.* The gaps and the deferral in section G
remain. *ACCESSCONTROL SYSTEM-WIDE FIELD SECURITY: PASS* may not be claimed either, because TaskActivity
is an implemented record type with no authoritative field-security semantics. The honest pair is
*field security for the current implemented core record resources: `PASS`* and *TaskActivity field
security: `AUTHORITY_GAP`*.


## B04 Tasks implementation authority

B04 admits and implements the ten current standalone Operations/Tasks operations: `listTasks`, `createTask`, `getTask`, `archiveTask`, `assignTask`, `cancelTask`, `completeTask`, `rescheduleTask`, `logActivity`, and `listActivities`. Every operation consumes B01 authentication, B02 trusted Workspace authority, and B03 application-boundary authorization before Tasks persistence. Canonical capabilities are `tasks.read`, `tasks.create`, `tasks.update`, `tasks.assign`, and `tasks.complete` as declared by current operation authority.

Tasks owns `TasksDbContext` and the `tasks` logical schema. Its aggregate identity is generated only by Tasks; intent, source, dedupe, idempotency, correlation, and historical `task_deal_*` values are never accepted as Task aggregate IDs. Assignee identifiers are scalar global member references and creation/assignment uses the narrow Workspace-owned active-member validation contract for the trusted Workspace. Relationship, record, and source references remain scalar evidence without EF navigation, database foreign key, foreign repository access, or inferred foreign-owner validity.

The implemented lifecycle is limited to the admitted `OPEN`, `COMPLETED`, and `CANCELLED` states. Completion and cancellation are terminal; assignment and rescheduling require `OPEN`; archival records retention metadata without inventing another status. Declared Task mutations use quoted `If-Match` resource versions, owner-local idempotency records, immutable command audit evidence, and atomic Tasks-owned outbox staging records. Activities are append-only.

Runtime verification on 2026-08-23 used isolated LocalDB databases and proved all ten operations, authoritative server-assigned identity, replay without duplicate creation, idempotency-key conflict, stale-version conflict, terminal-transition rejection, paging/filtering, active-member assignee validation, foreign-Workspace rejection, and application-boundary denial for a trusted member without Task capability; the denied database retained zero Tasks. B01 sign-in, B02 Workspace/bootstrap, and B03 authorization-context regressions passed. Therefore `B04 TASKS CORE: PASS`.

WF-21 Work Activation remains `AUTHORITY_GAP` and unimplemented. Tasks does not coordinate Deal or other foreign-owner mutation. Foreign business-reference validation beyond structural scalar validation remains deferred until an authoritative owner contract is admitted. The optional `dedupeKey` is preserved as scalar evidence only because current authority does not define its collision scope or replay behavior; canonical header idempotency remains enforced independently. These gaps do not block standalone Tasks core.

## B05 Leads Core implementation authority

B05 admits and implements the independently complete Leads operations `listLeads`, `getLead`, `createLead`, `replaceLeadProfile`, `advanceLeadWorkState`, `disqualifyLead`, and `reopenDisqualifiedLead`. Every operation consumes B01 authentication, B02 trusted Workspace authority, and B03 application-boundary authorization before owner-local persistence. Canonical capabilities are `leads.read`, `leads.create`, `leads.update`, and `leads.qualify`.

Leads owns `LeadsDbContext` and the `leads` logical schema. Lead aggregate identity is generated only by Leads. Source, external, webhook, dedupe, idempotency, correlation, campaign, and foreign record identifiers are not accepted as Lead identity. Create persists the trusted Workspace identifier and validates the required owner identifier through the narrow Workspace active-member reference contract; it does not access Workspace or AccessControl persistence.

The implemented lifecycle is limited to the admitted `NEW`, `CONTACTING`, `VERIFYING`, and `CLOSED` work states and the Lead-owned `DISQUALIFIED` outcome. Active progression is `NEW -> CONTACTING -> VERIFYING`; disqualification closes any active Lead; only a disqualified closed Lead may reopen to `CONTACTING`. Profile replacement cannot mutate lifecycle, score, identity-resolution, or audit state. Declared mutations use owner-local idempotency records, quoted `If-Match` versions where required, immutable command audit records, and atomic owner-local outbox staging.

The generic `qualifyLead` operation remains blocked and has no route. Positive qualification operations remain deferred to the admitted multi-owner Lead Qualification workflow; B05 does not create Contact, Organization, Deal, Task, Quote, or Order state. Lead handover and follow-up operations that mutate Tasks are likewise deferred to Workflow. Other current owner-local Lead operations outside the minimal independently usable B05 core remain fail-closed and have no route.

Populated `interestedProducts` profile writes remain fail-closed in B05. The current request carries only `productId`, while the authoritative Lead response requires a backend-owned product-name snapshot, and no implemented Products owner reference/snapshot contract is available. B05 does not fabricate that snapshot or claim foreign-record validation; empty or omitted interested-product collections remain supported.

Runtime verification on 2026-08-23 used isolated LocalDB databases and proved authenticated create/list/get/replace, server-assigned identity, idempotent replay, changed-payload key rejection, stale-version rejection, lifecycle advance/disqualify/reopen, active local owner validation, foreign-Workspace denial, application-boundary capability denial with zero persisted Leads, and B01/B02/B03/B04 targeted regressions. Therefore `B05 LEADS CORE: PASS`; the explicit workflow deferrals and interested-product snapshot authority gap above remain fail-closed.

## B06 Deals Core implementation authority

B06 admits and implements the thirteen independently complete Deals-owned operations `listDeals`, `createDealCommand`, `getDeal`, `archiveDealCommand`, `changeDealStageCommand`, `markDealLostCommand`, `markDealWonCommand`, `updateDealCommand`, `getDealForecastSummary`, `archiveDealsBatch`, `assignDealOwner`, `updateDealForecast`, and `updateDealNextAction`. Every operation consumes B01 authentication, B02 trusted Workspace authority, and B03 application-boundary authorization before owner-local persistence. Canonical capabilities are `deals.read`, `deals.create`, `deals.update`, `deals.assign`, `deals.close`, `deals.delete`, and `deals.bulk`.

Deals owns `DealsDbContext`, the `deals` logical schema, and server-assigned Deal aggregate identity. Buyer, Contact, source Lead, interested Product, next-action Task, accepted Quote, and confirmed Order identifiers are admitted only as structurally valid scalar references or evidence where the current Deal contract declares them. They do not become Deal identity and create no cross-owner EF navigation or persistence access. Deal owner creation and assignment validate the global member reference through the narrow Workspace-owned active-member contract for the trusted Workspace.

The implemented default lifecycle supports open `DISCOVERY`, `QUALIFIED`, `SOLUTION`, `PROPOSAL`, and `NEGOTIATION` stages plus terminal `WON` and `LOST` outcomes. Open-to-open changes use the typed stage command; terminal stage codes are not accepted through that generic command. `WON` requires accepted-Quote or confirmed-Order scalar evidence, and `LOST` requires a reason and recycle decision, with a revisit time for recyclable outcomes. No reopen operation is admitted. Current authority says stage configuration is Workspace-configurable, but no implemented WorkspaceConfig projection contract supplies custom stage definitions to Deals; B06 therefore supports the canonical default definitions and rejects unknown custom stages rather than fabricating configuration.

Monetary and probability wire values remain exact decimal strings with at most six fractional digits; authoritative calculations use scaled arbitrary-precision integers and perform no currency conversion. Non-empty Deal line-item writes remain fail-closed because the response requires Product-owned name/pricing snapshots and no admitted Products snapshot contract is implemented. Empty line-item collections and scalar interested-Product references remain supported.

All declared mutations use owner-local idempotency records, immutable audit records, and atomic owner-local outbox staging. Existing-aggregate mutations use quoted `If-Match` versions; create and batch archive use serializable owner transactions, and batch archive validates every supplied aggregate version before committing any mutation. Reads are Workspace-scoped and audited.

The Quotes-owned `createQuoteForDeal` operation is not implemented by Deals. `markDealLostAndPlanRecycle` and WF-09 Deal Recycle remain blocked and have no route. WF-10 Lead Qualification is Workflow-owned and may use a future narrow Deals-owned participant boundary, but B06 implements no coordinator and mutates no Lead, Contact, Task, Quote, or Order. WF-01 Contact Opportunity and WF-21 Work Activation remain blocked. WF-13 Order Confirmation and WF-16 Quote Acceptance remain workflow-owned cross-owner coordination; WF-22 Accepted Quote Conversion consumes a Deal reference without transferring Deal ownership. These workflow deferrals do not block independent Deals Core.

Runtime verification on 2026-08-23 used the isolated `UnicoreCRM_B06_Verification_20260823` LocalDB database and proved all thirteen operations, server-assigned identity, exact forecast arithmetic, idempotent replay, changed-payload key rejection, stale-version rejection, win/loss evidence rules, terminal-state rejection, single and batch archival, active local owner validation, foreign-Workspace denial, and application-boundary denial for a trusted member without Deal capability; the denied Workspace retained zero Deals. B01/B02/B03 plus targeted B04 Task and B05 Lead regressions passed. The Deals migration was discovered, applied, scripted idempotently, and reported no pending model changes. Therefore `B06 DEALS CORE: PASS`; the explicit workflow, configurable-stage, and Product snapshot gaps above remain fail-closed.

## Products Core implementation authority

Products Core admits and implements the ten independently complete Products-owned operations `listProducts`, `getProduct`, `getProductAvailability`, `getProductPriceProjection`, `createProduct`, `replaceProduct`, `archiveProduct`, `restoreProduct`, `archiveProductsBatch`, and `restoreProductsBatch`. Every operation consumes IdentityAuth authentication, trusted Workspace authority, and AccessControl application-boundary authorization before Product-owned behavior. Canonical capabilities are `products.read`, `products.create`, `products.edit`, and `products.delete`.

Products owns `ProductsDbContext`, the `products` logical schema, and server-assigned Product aggregate identity. SKU uniqueness is Workspace-scoped and case-insensitive. Mutations use owner-local idempotency records, immutable command audits, and atomic owner-local outbox staging. Existing-aggregate mutations use quoted `If-Match` versions, and both batch operations validate every supplied Product version before committing one atomic owner transaction.

Until an admitted WorkspaceConfig read model supersedes it, the immutable/effective base currency established during Workspace provisioning is the admitted runtime currency source for Product validation. Workspace exposes only `IEffectiveWorkspaceBaseCurrencyReader`, returning the effective base currency and its source version. This narrow owner-provided contract does not expose Workspace persistence, does not make `WorkspaceBootstrapProjection` a general configuration model, does not transfer canonical WorkspaceConfig ownership to Workspace, and admits no Workspace configuration read or mutation beyond the provisioned base-currency fact. When WorkspaceConfig obtains an admitted canonical base-currency read contract, that contract supersedes this temporary provider without changing Product ownership.

Product money is persisted and returned as normalized exact decimal strings with uppercase currency. Maximum authoritative scale is six and the rounding mode is `HALF_UP`; no binary floating point, currency-specific minor units, conversion, or implicit rounding inside generic multiplication is authoritative. Pricing first computes the exact unrounded subtotal as unit price multiplied by quantity, then explicitly rounds the reported subtotal to scale six. For exclusive tax, tax is computed from the exact subtotal multiplied by the rate and divided by 100, rounded once to scale six, then total is the explicitly rounded sum of reported subtotal plus tax. For inclusive tax, total is the reported subtotal and tax is computed from the exact subtotal multiplied by the rate and divided by `100 + rate`, rounded once to scale six. For no tax, tax is zero and total is the reported subtotal. Thus exclusive `total = subtotal + taxAmount`, inclusive `total = subtotal`, and none `taxAmount = 0` and `total = subtotal`.

Create and replace idempotency fingerprints represent normalized stable client business intent only. They exclude effective-currency source version, later Workspace configuration, current time, generated Product identity, and current database state. Replace intent additionally binds the target Product and expected version. A matching committed key and stable intent replays its stored authoritative result before later effective-currency validation; reuse with different stable intent returns the canonical idempotency-key-reused error. New keys are validated against the current effective base currency normally.

Successful `getProductAvailability` and `getProductPriceProjection` reads append one immutable Products-owned `READ` audit record containing the operation, trusted Workspace, authenticated member, Product aggregate, request and correlation identifiers, and occurred time. This evidence is independent of AccessControl authorization-decision audit. A successful projection read changes neither Product aggregate state nor resource version and emits no Product business outbox event solely for the read.

Product availability is an owner-local status projection: only `ACTIVE` is sellable; `INACTIVE`, `DRAFT`, and `ARCHIVED` remain unavailable. No browser inventory state or generic inventory subsystem is consulted. Price projection uses the Product price snapshot plus the effective base-currency source version and supports only positive decimal-string quantity.

Archive and restore are strict Product lifecycle commands. Archive requires a reason and moves a non-archived Product to `ARCHIVED`; restore accepts an optional reason, applies only to an archived Product, clears archive evidence, and returns it to `ACTIVE`. Replacement cannot mutate an archived Product. No broader transition graph is inferred.

Quoted `If-Match` is required on `getProductAvailability` and `getProductPriceProjection`. The server either evaluates the projection for exactly that Product resource version or returns canonical `412 VERSION_CONFLICT`. The adopted OpenAPI, current operation/concurrency metadata, generated commercial client, Product application port, Product HTTP adapter, and connected Product detail runtime all carry this rule. The connected detail supplies the `resourceVersion` from the authoritative `ProductDocument`; it does not calculate authoritative commercial projection values in the browser.

### Product Commercial Reference / Snapshot Authority

Classification: `PARTIALLY_RESOLVED`. Current evidence resolves ownership, live-reference identity, lifecycle use, and historical-immutability rules.

**Amended 2026-09-03 by `DEC-PRODUCTS-LEAD-INTERESTED-PRODUCT-SNAPSHOT`.** The **Leads** leg is now closed: a narrow Products-owned reader supplies exactly `productId`, `name`, `sku`, `productType`, `status` and `version` for a set of distinct identifiers in one owner-local batch read, and Leads captures them as an immutable snapshot. The full record is `design-authority/canonical-design/authority/products-lead-snapshot-authority.md`. It closed because the open questions in this section are entirely commercial - which price, tax and billing fields a command captures, and how `pricingVersion` binds them - and the Lead contract requires none of them: `LeadInterestedProductReadModel` requires only `productNameSnapshot`, declares no price, tax or billing field, and its one `Money` field is the caller-supplied `expectedBudget` echoed from the request rather than a Product price.

The **Deals, Quotes and direct Order** legs are unchanged and remain fail-closed, so `AG-PRODUCT-SNAPSHOT` stays open for them and Deals continues to reject non-empty `lineItems`. No Products, Quotes or Orders behavior is changed, and no runtime integration is implemented by that record.

#### LIVE PRODUCT REFERENCE

A canonical live Product reference is the scalar pair of trusted `WorkspaceId` plus `ProductId`; only `ProductId` is carried as business input because trusted Workspace context supplies the scope. The identifier is not a snapshot, does not grant Product read authority, and cannot reconstruct or validate Product-owned name, SKU, type, description, unit price, tax attributes, billing attributes, lifecycle, availability, or current version. A new action that claims to use a catalog Product must resolve that Product through an admitted Products-owned boundary in the same Workspace. Any claim that the Product is currently sellable must use Products-owned availability for the exact Product resource version: only `ACTIVE` is sellable, while `DRAFT`, `INACTIVE`, and `ARCHIVED` are unavailable. The current availability contract explicitly serves Order eligibility; current authority does not separately decide whether a future Quote draft may reference an unavailable Product. No cross-owner resolution boundary is admitted by this decision, so all downstream Product-backed creation remains fail-closed rather than treating a caller-supplied identifier or Product-shaped payload as authoritative.

Archiving retains the Product and `getProduct` can resolve its current archived master state under the normal Products read boundary; archival is not deletion. An archived Product is unavailable for new Order/direct-sale eligibility. Whether a future Quote draft may reference an archived Product remains unresolved and is currently fail-closed with the absent cross-owner contract. Restore may make the current Product sellable again only after current, exact-version availability validation. Neither archive nor restore changes an already captured foreign historical snapshot.

#### IMMUTABLE COMMERCIAL SNAPSHOT

Current authority proves the pattern `Products current truth -> owning commercial command -> owner-local immutable snapshot`: Products supplies current facts only through a future admitted narrow contract; the Quote or Order command validates and consumes those facts; Quotes persists Quote historical line truth and Orders persists Order historical line truth. Products does not own or persist Quote/Order line snapshots. Accepted Quote-to-Order conversion copies the accepted Quote's immutable commercial lines, adjustments, payment agreement, and source references into the new Order; Orders owns the copied Order snapshot, and later Product changes cannot rewrite it.

No exact Product-supplied snapshot field set is admitted yet. Current wire models prove that downstream documents carry fields such as Product identity, product-name snapshots, optional SKU/type/description/billing snapshots, quantities, unit prices, discounts, tax inputs, and calculated totals, but they do not prove which of those are copied from Product master, which are commercial-owner inputs or negotiated terms, or which Products operation supplies them. `ProductId` is admitted only as the live scalar reference, not as sufficient snapshot evidence. Consequently no foreign owner may populate Product-owned name, SKU, type, description, price, tax, billing, lifecycle, or availability facts from caller or browser state and label them authoritative.

#### PRODUCT PRICING PROJECTION

`getProductPriceProjection` is a Products-owned current calculation/read projection evaluated for a caller-supplied quantity at an exact Product resource version. It returns Product-derived unit price, subtotal, tax amount, total, `pricingVersion`, and evaluation time under the frozen Products decimal policy. It is not Quote pricing, Order pricing, or a historical commercial snapshot contract. Quote and Order commands own their discounts, adjustments, repricing, approval-sensitive commercial terms, totals, and historical line state. Current authority does not prove whether a future commercial command must consume the Product projection, which projection fields it must capture, whether `pricingVersion` is persisted as evidence, or how Product defaults reconcile with client-supplied or negotiated Quote/Order inputs; these remain `AUTHORITY_GAP`.

#### HISTORICAL COMMERCIAL TRUTH

Once captured by its owning commercial command, a Quote or Order snapshot is not rehydrated from mutable Product master state. Later changes to Product name, SKU, price, tax attributes, type, description, billing attributes, archive state, or restore state do not silently change the historical Quote or Order. Quote sent and terminal versions are content-immutable and require a revision for changed commercial content; confirmed Order commercial content is immutable. A new revision or draft is a new commercial decision and must use whatever Product-validation contract is current and admitted at that time.

#### DOWNSTREAM OWNERSHIP

Quotes owns Quote roots, versions, line snapshots, adjustments, pricing results, approval evidence, and delivery evidence. Orders owns Order roots, line and adjustment snapshots, payment-agreement snapshots, and Order lifecycle evidence. Products remains the sole owner of Product master/catalog truth and exposes no persistence implementation. Deals may continue storing scalar Product identifiers where its admitted contracts already allow them, but its populated Product-name/pricing snapshot path remains blocked: Deals rejects non-empty `lineItems` until an admitted Products-owned commercial contract supplies the required current facts. The Leads `interestedProducts` path is no longer blocked as authority - `DEC-PRODUCTS-LEAD-INTERESTED-PRODUCT-SNAPSHOT` admits its reader - though the Leads runtime still refuses non-empty input until that integration is implemented. No Leads, Deals, Quotes, or Orders behavior is changed by this authority decision.

#### ARCHIVED PRODUCT BEHAVIOR

Historical Quote and Order snapshots remain readable and unchanged after Product archival, restoration, or any other master change. Historical rendering and audit use the commercial owner's stored snapshot rather than requiring the Product to remain sellable. New Order/direct-sale use of an archived Product is prohibited by Products availability; future Quote-draft behavior remains an explicit gap and is currently fail-closed. Current authority does not admit deletion or loss of historical Product reference evidence.

#### REMAINING AUTHORITY GAPS

The following decisions remain unresolved and block a safe Products-to-**commercial**-owner contract: the exact Product-owned fields supplied for Deals, Quotes and direct Orders; whether and how `pricingVersion` binds historical price inputs; and reconciliation of caller-supplied Quote/Order name, price, tax, type, description, billing, and fulfillment fields with Product authority. Until those decisions are closed, no Products-owned commercial snapshot DTO is introduced and no foreign module may access Products persistence or fabricate Product-owned facts.

Four items previously listed here are now closed **for the Leads consumer only** by `DEC-PRODUCTS-LEAD-INTERESTED-PRODUCT-SNAPSHOT`: the intent-specific contract operation and its approved consumer; the exact Product-owned field set for Leads; that capture is one owner-local batch read rather than a composed availability-plus-pricing read; and the version evidence, which is captured and persisted owner-locally with no caller-supplied expected version, because the pinned Lead input declares no field for one. Concurrent Product change during capture is likewise resolved for that leg: a single batch read, the read version recorded as provenance, an accepted and named capture window, and explicitly no distributed transaction.

Quotes has many independently admitted owner-local operations and owns the commercial terms that Orders later consume, while accepted Quote conversion explicitly copies immutable Quote truth into an Order. Orders additionally depends on Quote conversion, payment agreement/confirmation, fulfillment and invoice eligibility, credit approval, and workflow authority; `WF-12` Order Closing still requires reconciliation and `WF-14` generic Order Creation remains blocked. Nevertheless, current connected Quote and direct-Order inputs can carry Product-shaped names/prices/tax/type data without an admitted Products source or Product-version binding. Therefore neither a complete Product-backed Quotes Core nor a complete direct-Order Core can safely be selected next. The dependency decision is `NEITHER — AUTHORITY/DEPENDENCY GAP FIRST`: close the narrow Products commercial-facts/version/pricing capture contract before implementing either core. Once closed, Quotes precedes Orders because Quotes owns the accepted commercial terms copied by the Quote-to-Order workflow.

Runtime verification was re-run on 2026-08-26 against the isolated `UnicoreCRM_ProductAuthorityClosure_20260826` LocalDB database and proved all ten operations, server-assigned identity, Workspace currency enforcement, create and replace replay after a later effective-currency/source-version change, changed-intent key rejection, exact-first scale-six `HALF_UP` boundaries below/equal/above five for exclusive/inclusive/no-tax calculations, independent Products-owned projection read audits, unchanged Product versions and unchanged outbox counts on reads, quoted `If-Match` enforcement, stale-version rejection, strict archive/restore, atomic batch rollback/replay, cross-Workspace rejection, application-boundary capability denial with no Product write, applied migration discovery, and no pending Product model changes. The repository-declared Playwright Chromium runtime also drove the real frontend against that real backend and database: list and detail loaded, both projections succeeded with the current Product version, archive increased the version, a stale projection returned `412 VERSION_CONFLICT`, reload acquired the new version, and the new-version projection succeeded. Therefore `PRODUCTS BACKEND RUNTIME: PASS`, `PRODUCTS AUTHORITY CONFORMANCE: PASS`, `PRODUCTS CONTRACT CONFORMANCE: PASS`, and `PRODUCTS CONNECTED ACCEPTANCE: PASS` for the verified Product scope. This is task-specific evidence, not independent release attestation or external-provider conformance.

Product configuration mutations, import, export, demo-data reset, inventory, purchasing, promotions, tax configuration, currency conversion, Quotes, and Orders remain unimplemented and fail closed.

## Contacts Read Core implementation authority

Contacts Read Core admits and implements exactly the two canonical Contacts-owned reads `listContacts` (`GET /contacts`) and `getContact` (`GET /contacts/{contactId}`). Both operation rows are `PRODUCTION_CONTRACT_READY`; both require authenticated IdentityAuth context, a trusted active Workspace membership, and the canonical `contacts.read` capability. No Contact create, replace, archive, restore, merge, import, export, relationship-summary, or other mutation/read operation is introduced by this slice. The operation registry still marks Contact commands blocked, and this implementation does not reinterpret a ready read contract as mutation authority.

Contacts remains the canonical owner inside `UnicoreCRM.Crm`. It owns `ContactsDbContext`, the `contacts` logical schema, the durable `contacts.Contacts` read-state table, and the immutable `contacts.ReadAuditRecords` read-evidence table. The persisted key is the Contact-owned `ContactId`; every row also carries its trusted `WorkspaceId`, optional Workspace-member `OwnerId`, required name/status/version/timestamps, and the remaining optional Contact profile as Contacts-owned JSON state. The migrations create Workspace-first indexes for the Workspace and owner list paths plus Workspace/time and Contact/time read-audit indexes. This read-only slice creates no public or internal Contact mutation surface; controlled verifier fixtures are inserted directly into the owner table.

The wire contract is the adopted OpenAPI `ContactDocument`, unchanged. Both list and detail use the same projection and field vocabulary: `id`, `workspaceId`, `salutation`, `jobTitle`, `department`, `roleAtCompany`, `workEmail`, `personalEmail`, `mobilePhone`, `workPhone`, `otherPhone`, `zaloId`, `facebook`, `preferredContactChannel`, `address`, `addressDetails`, `source`, `ownerId`, `consent`, `doNotCall`, `doNotEmail`, `doNotSms`, `doNotZalo`, `doNotContact`, `doNotContactReason`, `decisionRole`, `relationshipLevel`, `painPoint`, `needSummary`, `notes`, `tags`, `organizationRelationships`, `status`, `version`, `createdAt`, `updatedAt`, `fullName`, and `displayName`. The contract-required fields are `id`, `workspaceId`, `fullName`, `status`, `version`, `createdAt`, and `updatedAt`; every other declared field is optional. A hidden optional field is omitted before serialization. A restrictive policy on a declared required field refuses the operation with `ACCESS_DENIED`, because no absent required-field representation is admitted. By contrast, an unknown field key is outside the Contacts-owned vocabulary: `CanRead` and `CanWrite` are false and public evaluation reports `HIDDEN`, but it does not by itself refuse the Contact read because Contacts never projects that key. It cannot widen access and no value is returned. `READ_ONLY` and `READ_WRITE` remain readable on this read-only surface. `MASKED` has no admitted representation and is enforced as withheld/`HIDDEN`, not as fabricated masking.

The canonical AccessControl resource key is `contacts`. Contacts registers its own `IRecordAccessFactProvider`, declares only `contacts.read`, and supplies authoritative record facts from its own Workspace-scoped persistence lookup. The provider declares no command, update, delete, export, or approval capability. Contact ownership is `PROVEN`: the canonical Contacts model names `ownerId`, and `ContactDocument` exposes it as the Contact's optional Workspace-member owner reference. Consequently `OWN` means exact ordinal owner-member equality; an unassigned Contact is outside `OWN`. `WORKSPACE` admits all rows in the trusted Workspace. `TEAM` and `CUSTOM` remain `AUTHORITY_GAP` and deny/return an empty list because neither team membership nor custom-owner semantics is authoritative.

Enforcement occurs at the Contacts application boundary through the canonical `IRecordAccessEvaluator`; Contacts does not read AccessControl persistence and does not duplicate the evaluator. Each request performs resource authorization exactly once. Detail loads by `(trusted WorkspaceId, ContactId)`, then applies the canonical record decision and field projection. A foreign-Workspace, unknown, or record-scope-denied identifier returns the same canonical `RESOURCE_NOT_FOUND` response and discloses no Contact value. List resolves scope once and pushes the trusted Workspace predicate and, for `OWN`, the owner predicate into SQL before ordering and materialization. `TEAM`, `CUSTOM`, and denied scope do not query/materialize Contact rows. A list writes no per-row record decisions, so authorization work is constant with row count and has no N+1 evaluator pattern. Each successful list and detail appends one Contacts-owned `READ_ACCESS_LOG` carrying the operation, trusted Workspace and member, request and correlation identifiers, occurrence time, and, for detail, Contact identity/version. Denied, unknown, and foreign detail attempts create no Contacts-owned read record; AccessControl retains its separate authorization-decision evidence.

The adopted list contract is a plain array and declares no query, filter, sort, page, cursor, count, or page-metadata parameter. Contacts therefore invents none. The implementation uses deterministic `createdAt` descending then `contactId` ordering, but that order is not elevated to a wire-contract promise. Because no count or pagination exists, hidden-row count/page-boundary behavior is not applicable; scope exclusion before materialization is nevertheless proven directly. `CONTACTS LIST CURRENT CONTRACT: PASS`; `CONTACTS LIST PAGINATION: NOT ADMITTED`; `CONTACTS LIST SCALE LIMIT: KNOWN CONTRACT LIMITATION`; `PERFORMANCE BENCHMARK: NOT MEASURED`. The current list is consequently unbounded, but changing its wire shape requires separate contract authority and is not silently treated as an implementation defect.

Contacts reads no Customer, Organization, Product, Support, or other owner's persistence and performs no foreign enrichment. `organizationRelationships` is persisted only as the declared Contacts-owned effective-dated relationship ledger containing scalar Organization references. No narrow `IContactReferenceReader` is added because no current consumer contract proves one. `getContactRelationshipSummary` is outside this read-core scope and remains deferred pending its admitted relationship-composition dependencies.

Runtime verification on 2026-08-28 used isolated LocalDB databases and a real ApiHost. `scripts/verify-contacts-read-core.ps1` reported `PASS=67 FAIL=0`: real initial Workspace provisioning, the exact Contacts-enabled module set and provisioned `contacts.read` before the first positive Contacts request; authentication and capability denial; trusted Workspace isolation; list/detail shape; foreign/unknown/scope-denied non-disclosure; `WORKSPACE`, `OWN`, fail-closed `TEAM`/`CUSTOM`; hidden/required/unknown/masked/read-only/read-write field policy behavior; spoof-resistant owner facts; SQL scope pushdown; zero per-row and exactly one resource authorization; Contacts-owned read audits and separate access decisions; absence of mutation routes and mutation side effects; indexes; migration discovery; and no pending Contacts model change. The expanded canonical record-access suite reported `PASS=404 FAIL=0`. Unchanged Support (`83/0`), Products, AI Assistant, Inbound Lead Webhook, initial provisioning, provisioning upgrade, and email-verification OTP verifiers all passed. The solution build completed with zero warnings and zero errors.

Therefore `CONTACTS AUTHORITY RECONCILIATION: PASS`, `CONTACTS MODULE/PERSISTENCE: PASS`, `CONTACTS WORKSPACE/CROSS-WORKSPACE: PASS`, `CONTACTS LIST/DETAIL/CONTRACT: PASS`, `CONTACTS CAPABILITY/RECORD/FIELD ACCESS: PASS`, `CONTACTS OWN: PASS`, `CONTACTS SQL SCOPE PUSHDOWN/NO N+1/DIRECT BYPASS: PASS`, `CONTACTS EF MIGRATION/REGRESSION: PASS`, `CONTACTS TEAM/CUSTOM/MASKED: AUTHORITY_GAP`, and `CONTACTS MUTATIONS: BLOCKED / NOT IMPLEMENTED`. This is task-specific executable evidence, not a Control 1.2 independent-review attestation or release freeze.

## Organizations Read Core implementation authority

Organizations Read Core implements exactly the two owner-local canonical reads `listOrganizations`
(`GET /organizations`) and `getOrganization` (`GET /organizations/{organizationId}`). Both are
`PRODUCTION_CONTRACT_READY`, require an authenticated principal, trusted active Workspace context,
and `organizations.read`, and carry `READ_ACCESS_LOG`. The separately admitted
`getOrganizationOverview` operation is a composed cross-owner projection and is outside this
owner-local read-core slice. No Organization create, update, link, archive, delete, merge,
enrichment, overview, relationship-composition, or other mutation/read route is exposed.

Organizations remains the canonical owner inside `UnicoreCRM.Crm`. It owns
`OrganizationsDbContext`, the `organizations` logical schema, `organizations.Organizations` durable
read state, and `organizations.ReadAuditRecords` read evidence. Organization identity is the
Workspace-scoped pair `(WorkspaceId, OrganizationId)`, represented on the wire by the trusted
`workspaceId` and Organization-owned `id`; the composite persistence key does not invent global
cross-Workspace uniqueness. Required document scalars are stored as columns and the optional
Organizations-owned profile is JSON. The only list index is
`(WorkspaceId, CreatedAt, OrganizationId)`, matching the admitted Workspace list path; no owner
index exists because Organization ownership is unresolved. Audit indexes support Workspace/time and
Organization/time evidence queries.

The unchanged adopted `OrganizationDocument` vocabulary is `id`, `workspaceId`, `displayName`,
`legalName`, `taxCode`, `domain`, `website`, `industry`, `sizeBand`, `employeeCount`,
`annualRevenue`, `email`, `phone`, `address`, `addressDetails`, `source`, `ownerId`,
`primaryContactId`, `contactRefs`, `relationshipLevel`, `notes`, `status`, `externalRef`, `version`,
`createdAt`, and `updatedAt`. Required fields are `id`, `workspaceId`, `displayName`, `status`,
`version`, `createdAt`, and `updatedAt`; all others are optional. `READ_WRITE` and `READ_ONLY` are
readable. Optional `HIDDEN` and `MASKED` values are withheld and omitted, because no rendered mask
is admitted. A restrictive policy on a required field refuses the operation with `ACCESS_DENIED`.
An unknown key is outside the Organizations vocabulary, so `CanRead` and `CanWrite` are false and
public evaluation reports `HIDDEN`; it never widens or creates a projected value.

The canonical AccessControl resource key is `organizations` and its sole declared operation
capability is `organizations.read`. Organizations registers one `IRecordAccessFactProvider` and
uses the existing `IRecordAccessEvaluator`; it reads no AccessControl persistence and implements no
second algorithm. Organization `ownerId` is an admitted optional record value, but current authority
does not establish it—or any account manager, creator, assignee, or relationship manager—as a
Workspace-member AccessControl ownership fact. The provider therefore reports a found record with
no owner fact. `WORKSPACE` is implemented normally. `OWN` is `AUTHORITY_GAP` and fails closed even
when the stored `ownerId` happens to equal the caller. `TEAM` and `CUSTOM` remain
`AUTHORITY_GAP` and fail closed.

Each request performs resource/capability/scope/field authorization once at the Organizations
application boundary. Detail validates the canonical `EntityId`, executes the SQL lookup with both
trusted `WorkspaceId` and requested `OrganizationId`, then applies the canonical record decision and
field projection. Unknown, foreign-Workspace, and same-Workspace scope-hidden identifiers all return
the same `RESOURCE_NOT_FOUND` representation and disclose no Organization business value in the
body, owner audit, AccessControl evidence, or host logs. List translates only `WORKSPACE` into an
Organizations query; unresolved `OWN`, `TEAM`, `CUSTOM`, and denied scopes return no rows without
querying or materializing Organization state. The Workspace predicate is applied in SQL before
ordering/materialization. There is no per-row evaluator and no N+1 authorization path.

`OrganizationList` is a plain array. The wire admits no filter, search, sort, page, cursor, count, or
page metadata, so none is invented and pagination/count boundary behavior is not applicable. The
query uses deterministic `createdAt` descending then `organizationId` ordering as an implementation
choice only, not a wire promise. `ORGANIZATIONS LIST PAGINATION: NOT ADMITTED`; the resulting
unbounded list is a known contract limitation, not authority to change the response shape.

`primaryContactId` and `contactRefs` are persisted and projected only as declared scalar references.
Organizations never opens `ContactsDbContext` or any other foreign persistence, performs no Contact
validation/join/enrichment, creates no reference-reader contract, and does not equate Organization
identity with Customer identity. The current initial Workspace defaults remain exactly
`contacts`, `leads`, `deals`, and `tasks`, and the initial role does not gain
`organizations.read`: the capability and module key are admitted generally, but no authority makes
Organizations a default newly provisioned CRM module. `ORGANIZATIONS INITIAL WORKSPACE ENABLEMENT:
AUTHORITY_GAP`.

Runtime verification on 2026-08-28 used the isolated
`UnicoreCRM_OrganizationsReadCore_Composite_20260828` LocalDB database and a real ApiHost.
`scripts/verify-organizations-read-core.ps1` reported `PASS=71 FAIL=0`, covering authentication,
capability denial, Workspace isolation, foreign/unknown/scope-hidden non-disclosure, fail-closed
`OWN`/`TEAM`/`CUSTOM`, complete field-security behavior, spoof rejection, one list authorization,
zero per-row decisions, owner-local read evidence, no business values in denial logs/audits, absent
mutation and overview routes, migration/index discovery, preserved initial Workspace defaults, and no
pending Organizations model changes. Therefore `ORGANIZATIONS READ CORE: PASS`,
`ORGANIZATIONS TRUSTED-WORKSPACE ISOLATION/LIST/DETAIL/FIELD SECURITY/SQL PUSHDOWN/NO N+1/EF MODEL: PASS`,
`ORGANIZATIONS OWN/TEAM/CUSTOM/MASKED/INITIAL WORKSPACE ENABLEMENT: AUTHORITY_GAP`, and
`ORGANIZATIONS FULL MODULE: NOT COMPLETE`. This is task-specific executable evidence, not release
freeze or authority for the omitted composed/mutation surfaces.

### Organizations access-integration authority gate

The adopted OpenAPI permits `organizations` as one value in
`WorkspaceRuntimeConfiguration.enabledModuleKeys`, and the capability matrix admits
`organizations.read` for the Organization read operations. Neither fact defines the server-owned
defaults for a newly provisioned Workspace or the capability set of its initial `Workspace Owner`.
The canonical Workspace-flags inventory retains the literal uncertainty marker `organizations?`,
and no approved decision, owner document, registry row, or provisioning extension independently
promotes Organizations into the default new-Workspace set. Frontend route visibility is supporting
consumer evidence only and creates no provisioning or authorization authority.

Consequently the production defaults remain exactly `contacts`, `leads`, `deals`, and `tasks`, and
the exact current initial role remains unchanged without `organizations.read`. The Organizations
verifier's controlled SQL capability grant remains test-fixture setup after it first proves that
initial provisioning grants no Organizations authority; it is not a hidden production default.
Development likewise remains unchanged because it mirrors production policy rather than defining it.

No new capability snapshot exists, so historical AccessControl convergence for
`organizations.read` is not applicable. No Workspace configuration convergence is admitted:
historical `WorkspaceBootstrapProjection.EnabledModuleKeysJson`, `ConfigurationVersion`, stored
provisioning fingerprint, and stored effective intent remain untouched. Existing replay and
fingerprint behavior is unchanged: current defaults govern a fresh request; the same idempotency key
with changed effective defaults fails closed; and a different valid key replays the existing
provisioning anchor with its stored Workspace and configuration unchanged. This preservation is not
an Organizations configuration upgrade.

Therefore `ORGANIZATIONS DEFAULT NEW-WORKSPACE MODULE: AUTHORITY_GAP`,
`ORGANIZATIONS INITIAL OWNER CAPABILITY: AUTHORITY_GAP`,
`ORGANIZATIONS HISTORICAL ACCESS CONVERGENCE: NOT APPLICABLE`, and
`EXISTING WORKSPACE ORGANIZATIONS MODULE CONFIG UPGRADE: AUTHORITY_GAP / NOT IMPLEMENTED`.

### Organizations reconciled readiness gate

The Organizations read implementation and the new-Workspace product path are separate gates. The
owner-local read core is proven, and an explicitly configured Workspace whose caller is independently
authorized for `organizations.read` may use it. A normal newly provisioned Workspace receives neither
the `organizations` module key nor `organizations.read`, so it cannot use the read core without a
separate, authority-approved configuration and access change. No broader readiness may be inferred
from the verifier's controlled grant.

Therefore `ORGANIZATIONS READ CORE: PASS`,
`ORGANIZATIONS EXPLICITLY CONFIGURED/AUTHORIZED WORKSPACE READ: PASS`,
`ORGANIZATIONS NORMAL NEWLY-PROVISIONED WORKSPACE READ: NOT READY`,
`ORGANIZATIONS DEFAULT NEW-WORKSPACE MODULE: AUTHORITY_GAP`,
`ORGANIZATIONS INITIAL OWNER CAPABILITY: AUTHORITY_GAP`, and
`ORGANIZATIONS END-TO-END PRODUCT READINESS: NOT READY`.

## CONTACTS READ CORE INTEGRATION HARDENING

The server-owned `InitialWorkspaceAccessPolicy` now includes exactly `contacts.read` for Contacts;
no Contact create, update, delete, export, merge, assign or other mutation capability is granted. A
newly provisioned Workspace therefore creates its single `Workspace Owner` role with the current
exact canonical capability set and can call the admitted Contacts reads through normal IdentityAuth,
trusted Workspace and AccessControl evaluation without a direct capability seed.

Capability expansion does not weaken the existing drift rule. The only in-place upgrade admitted by
this hardening is the exact previously server-owned pre-Contacts snapshot to the exact current
server-owned snapshot. It adds only `contacts.read`. An arbitrary partial set, a set missing multiple
capabilities, a set with any unexpected extra capability, or a role whose canonical metadata has
drifted fails closed; no unexpected capability is silently preserved, deleted or reclassified and no
caller can supply the target set. Future expansion requires another explicitly frozen historical
snapshot rather than treating "current minus something" as server-owned evidence.

The strongest identity available without inventing an `IsSystemRole` column is used. The operation is
reachable only through the server-owned durable provisioning anchor, which supplies the Workspace and
creator membership identifiers. Within AccessControl the role must be the unique exact
`(WorkspaceId, "Workspace Owner")` row, carry the canonical description, have no source template, be
active at the initial version, and already be assigned to that creator membership before a capability
upgrade is allowed. A similarly named custom role or an unrelated custom role is not upgraded. The
current exact role may still complete a missing creator assignment through the existing partial-failure
convergence path. This composite identity is the admitted limitation; no new schema identity is
invented.

The capability addition and any owner-local provisioning writes use the existing AccessControl
transaction. `(RoleId, Capability)` and `(WorkspaceId, MembershipId, RoleId)` uniqueness remain
enforced. A uniqueness collision is treated only as a concurrent convergence signal: the whole local
attempt rolls back, state is re-read, and retry reaches the same role, assignment and exact capability
set. Re-runs create no role, assignment or capability duplicate, and custom roles are unchanged.

Existing completed Workspaces are not left dependent on a client replay. When durable provisioning
recovery is enabled, its hosted service performs a bounded, account-ordered startup scan of the
Workspace-owned initial provisioning anchors and asks AccessControl to converge each corresponding
server-owned role. This scan changes no Workspace anchor state or completion time; pending-anchor
completion remains the separate periodic recovery path. Multiple hosts may scan concurrently because
AccessControl convergence and its uniqueness handling are idempotent. A drifted role is logged and
left unchanged while the scan continues to other Workspaces.

The Development demo operator remains a local fixture rather than production authority. Its mirrored
capability list now includes only the admitted Contacts addition `contacts.read`, its admitted module
keys include `contacts`, and the provisioning verifier compares the fixture capability set with the
canonical initial set so later implemented reads cannot silently disappear from Development. The
production capability authority remains `InitialWorkspaceAccessPolicy`; ApiHost gains no second
business authority or Platform-to-CRM dependency.

`scripts/verify-contacts-read-core.ps1` now provisions its primary Workspace through the real initial
provisioning endpoint and proves that the first successful Contacts list uses the provisioned
`contacts.read`; it no longer inserts that grant before the positive path. Direct deletion and exact
restoration remain only for the negative 403 capability test. The initial and upgrade provisioning
verifiers assert one `contacts.read`, no unsupported Contacts capability, role/assignment identity,
exact-set convergence, idempotent and concurrent retry, unexpected-drift rejection, arbitrary-partial
rejection, and custom-role isolation. Unknown-field verification remains separate from the required
declared-field 403 case.

Final task evidence on 2026-08-28 reported `PASS=67 FAIL=0` for Contacts and `PASS=404 FAIL=0`
for the canonical record-access suite. The new-provisioning and upgrade verifiers each returned
`Status: PASS`; the upgrade run included a normally completed pre-Contacts anchor, a pending
pre-Contacts anchor processed by two simultaneously launched hosts, three invalid drift controls and
an unrelated custom role. Support reported `83/0`; Products, AI Assistant, Inbound Lead Webhook and
Email Verification OTP returned `Status: PASS`. Both affected EF models reported no pending changes,
`git diff --check` passed, and the solution built with zero warnings and zero errors.

This is Contacts Read Core integration only. The exact OpenAPI list remains an unpaginated plain array
with the scale limitation recorded above. Contact mutations, relationship-summary composition,
`TEAM`, `CUSTOM`, masked rendering, Organizations and generic AccessControl administration remain
outside this hardening and keep their prior blocked or `AUTHORITY_GAP` status. It does not establish
`CONTACTS FULL MODULE: PASS`.

### Contacts Workspace module integration

`contacts` is an admitted Workspace module key: it is a canonical CRM Workspace flag, its list and
detail routes are adopted, and Contacts Read Core is implemented under `contacts.read`. The
server-owned initial provisioning defaults therefore enable exactly
`["contacts","leads","deals","tasks"]` for every newly provisioned Workspace. The caller still
cannot supply or alter module keys. Development configuration contains the same admitted Contacts
availability for its local fixture, but production authority remains `ProvisioningDefaults`.

`EnabledModuleKeys` participates in the provisioning fingerprint. A new anchor stores the new exact
effective intent. Replays continue to use the stored anchor as authority and never rewrite its
fingerprint, Workspace, membership or bootstrap projection; account-scoped uniqueness still prevents
a second Workspace. A historical same-key request is compared against the effective defaults of the
current request and therefore fails closed as `IDEMPOTENCY_KEY_REUSED` if those effective values no
longer match, while a different valid key still replays the stored Workspace and ignores supplied
values. No compatibility rule silently treats changed server-owned intent as identical.

No authority admits WorkspaceConfig mutation or configuration convergence for completed Workspaces.
The AccessControl startup convergence pass may add the separately admitted `contacts.read` capability,
but it does not rewrite `WorkspaceBootstrapProjection.EnabledModuleKeysJson`, its configuration
version, or the stored provisioning fingerprint. Upgrade fixtures prove that pre-Contacts
`["leads","deals","tasks"]` bootstraps and stored fingerprints remain unchanged while access
capability convergence completes. Therefore
`ACCESSCONTROL INITIAL ROLE CAPABILITY UPGRADE: PASS FOR ADMITTED PROVISIONING-ANCHORED HISTORICAL SNAPSHOT`,
`NEW WORKSPACE CONTACTS MODULE ENABLEMENT: PASS`, and
`EXISTING WORKSPACE CONTACTS MODULE CONFIGURATION UPGRADE: NOT IMPLEMENTED / AUTHORITY_GAP`.

## Support Core implementation authority

Support Core admits and implements the eight Support-owned operations `listSupportCases`, `getSupportCase`, `createSupportCase`, `replaceSupportCaseProfile`, `assignSupportCase`, `transitionSupportCase`, `addSupportCaseReply`, and `addSupportCaseInternalNote`. Support remains an independent canonical owner inside the Operations bounded context and stays inside `UnicoreCRM.Operations`; no separate `UnicoreCRM.Support` assembly is created. Every operation consumes IdentityAuth authentication, trusted Workspace authority, and AccessControl application-boundary authorization before Support-owned behavior. Canonical capabilities are `support.read`, `support.create`, `support.update`, and `support.assign`. `support.complete`, `support.delete`, and `support.export` exist in the canonical capability matrix but no admitted Support operation requires them, so Support references none of them.

Support owns `SupportDbContext`, the `support` logical schema, server-assigned SupportCase aggregate identity, and the human-readable case number. Case numbers are allocated per trusted Workspace and per calendar year from a Support-owned durable sequence inside the SERIALIZABLE create transaction and are unique per Workspace. Neither the frontend nor any foreign module may fabricate a SupportCase identity or case number. Every mutation runs inside one SERIALIZABLE transaction matching the declared `SINGLE_SUPPORT_CASE_TRANSACTION` boundary and stages owner-local idempotency, immutable command audit and outbox evidence atomically with the Support state change.

### Concurrency

Five Support commands operate on an existing aggregate and require a quoted `If-Match` resource version: `replaceSupportCaseProfile`, `assignSupportCase`, `transitionSupportCase`, `addSupportCaseReply`, and `addSupportCaseInternalNote`. A stale version returns canonical `412 VERSION_CONFLICT` and mutates nothing; a missing or malformed `If-Match` is rejected before the use case is reached.

`createSupportCase` is the sixth command and is deliberately exempt: it creates the aggregate, so there is no existing resource version to match. Its operation row declares `concurrencyPolicy: NOT_APPLICABLE`, and no existing-aggregate `If-Match` contract is added to it.

### Idempotency

All six Support commands declare `idempotencyPolicy: REQUIRED` and enforce a fixed semantic order:

```text
authenticate -> trusted Workspace -> authorize -> normalize request
-> stable client-intent fingerprint -> idempotency lookup
```

A committed key whose stored fingerprint matches replays the committed result immediately from Support-owned evidence and reports `REPLAYED`. A committed key with a different stable intent returns canonical `IDEMPOTENCY_KEY_REUSED`. Only when the key is genuinely new does the command evaluate current mutable owner/member state, load the aggregate, enforce `If-Match`, mutate, and stage evidence.

The lookup deliberately precedes every mutable-state check. Validating a Workspace member before the lookup would let a member suspended *after* a command committed turn that command's replay into a validation failure, which would break the replay guarantee for a client that is simply retrying a request it already succeeded with. Runtime evidence covers this directly: with the owner member suspended, the original committed request still replays, returns the same aggregate at the same version, and creates neither a second SupportCase nor a second outbox message, while a *new* command naming the same suspended owner is still correctly rejected.

Fingerprints cover normalized stable client business intent only - the validated profile or command payload, the target case, and the client-declared expected version. They exclude generated Support identity, allocated case numbers, current time and current database state.

### Lifecycle

The implemented SupportCase lifecycle is transcribed verbatim from the canonical Support design baseline `design-authority/canonical-design/modules/support.md`: `new` to `in_progress`, `waiting_customer`, or `cancelled`; `in_progress` to `waiting_customer`, `waiting_internal`, `resolved`, or `cancelled`; `waiting_customer` and `waiting_internal` to `in_progress`, `resolved`, or `cancelled`; `resolved` to `closed` or `reopened`; `closed` to `reopened`; `cancelled` to `reopened`; `reopened` to `in_progress`, `waiting_customer`, or `resolved`. Same-state replay is admitted. Resolve and close stamp `resolvedAt` and `closedAt`; reopen stamps `reopenedAt` and clears both. Every pair absent from that table fails closed with the canonical Support-owned `SUPPORT_CASE_INVALID_TRANSITION` error, which the canonical error catalog assigns to `transitionSupportCase` at HTTP 409. No transition graph is inferred from generic ticketing software, and no admitted authority makes assignment change the lifecycle, so `assignSupportCase` records only the owner reference.

Creation is restricted to the seven `SupportCaseCreateCategory` values; replacement accepts the full twelve-value `SupportCaseCategory` vocabulary so an existing case carrying one of the five legacy categories can still be replaced. `replaceSupportCaseProfile` is total replacement: an omitted optional profile field clears the stored value, and status, resolution timestamps, assignment history, and the case number are not part of the profile. The transition `reason` carries no read-model field and is retained only as Support command audit evidence.

A reply and an internal note are immutable append-only Support conversation evidence; no admitted operation edits or deletes either. Both requests carry only a body, and every admitted Support command runs under an authenticated Workspace member holding `support.update`, so a reply is stored as an agent reply that is not internal and an internal note is stored as internal. The reserved customer-reply kind stays unreachable because no admitted operation ingests a customer reply. Support emits no customer-facing notification and exposes no customer-facing channel, so an internal note cannot leak outward. Appending either advances the case resource version so the declared `If-Match` contract stays meaningful for the next command.

Support records `contactId`, `relatedOrderId`, `relatedProductId`, `relatedOwnedProductId`, and `relationshipRef` as unvalidated caller-declared scalar references and echoes them back. Support asserts nothing about the foreign record and reads no foreign persistence, because no admitted Contacts, Customers, Organizations, Orders, or Products reference contract exists. `ownerId` is the one exception: an owner is a Workspace member, so `createSupportCase`, `replaceSupportCaseProfile`, and `assignSupportCase` verify it through the existing narrow `IWorkspaceMemberReferenceValidator` contract and reject a non-member on a new command.

### SUPPORT SLA AUTHORITY_GAP

SLA projection is unresolved and fails closed. This is a reconciled finding, not a default. Each element the projection needs was searched for across current implementation authority, the verified OpenAPI, the operation/command/query/workflow registries, the Design Authority and read-only frontend evidence:

- **Deadline rules — not provable.** The canonical Support module doc names the `SUPPORT_CASE_SLA_RULES` and `calculateSupportCaseSla` symbols but states no durations, and no other Design Authority document supplies any. The only concrete durations exist in frontend source, which the frontend read-only evidence rule forbids from creating backend authority. That frontend deadline calculator is additionally dead code: nothing calls it, and the frontend create command passes caller-supplied due timestamps straight through.
- **First-response semantics — not provable.** `firstRespondedAt` is declared in the read model and `first_response` is declared in the activity vocabulary, but no authority names the event that satisfies a first response.
- **Breach rule — not provable.** Only frontend source compares the current time against a due timestamp, and its choice to prefer the resolution deadline over the first-response deadline appears in no authority.
- **At-risk rule — not provable.** The sole evidence is a frontend heuristic, the greater of one hour or twenty percent of the resolution limit, described in its own comment as approximate.
- **Pause rule — not provable.** `paused` appears in the declared enum and in no behavioral evidence anywhere in the repository.
- **Terminal behavior — not provable.** Frontend source maps resolved, closed and cancelled to `not_applicable` but leaves `reopened` evaluated. No authority states whether a terminal or reopened case suspends its SLA clock.
- **Meaning of `not_applicable` — not defined.** The single implementation that exists already overloads it for two different situations, a terminal case and a case with no deadlines.

Because none of the seven is provable, Support computes no deadline, never sets `firstRespondedAt`, asserts no compliance state, and reports the one declared value that makes no compliance claim, `not_applicable`. Caller-declared `firstResponseDueAt` and `resolutionDueAt` are stored and returned verbatim, so no client-supplied fact is lost and the projection can be implemented later without a data migration. The declared `slaStatus` list filter is still validated against the declared vocabulary, so an undeclared value is rejected; a declared value other than `not_applicable` asks a question Support cannot answer and matches nothing rather than returning cases whose SLA state Support has not determined.

### Support customer enrichment — RESOLVED by contract amendment

`relationshipRef` is the canonical SupportCase relationship identity and is required. `customerId` and `customerName` are **optional** in `SupportCaseReadModel`.

The amendment was made because the previous required-ness was unsatisfiable, not merely unimplemented: a Customer aggregate exists only once effective purchase evidence has been recorded, so a Support Case raised against a pre-purchase Contact or Organization Account has no Customer at all, and the admitted Support category and source vocabularies (`consultation`, `onboarding`, `request`, `complaint`, `web_form`, `chat`) make that an ordinary case rather than an exception. The full reconciliation is recorded in the Support customer identity reconciliation section.

Support therefore omits both fields, and that omission is now **contract-conformant**. Support performs no Customer lookup, holds no Customer persistence, and does not map `customerId` to `relationshipRef.id`: Contact/Organization identity and Customer identity are distinct owners' identities, and substituting one for the other, or presenting an identifier as a display name, would fabricate CRM data.

If a Customers reference contract is admitted later, both fields become a read-time projection, never a stored Support-owned copy, per the minimum contract shape already frozen below.

### SUPPORT MEMBER DISPLAY NAME AUTHORITY_GAP

`SupportCaseActivityDocument` requires `actorName` and `SupportCaseCommentDocument` requires `authorName`. Both are member profile facts owned by IdentityAuth, whose only narrow cross-owner contract, `IAuthenticatedIdentityReferenceLookup`, deliberately exposes no profile state. Both `activities` and `comments` are optional in the read model, so Support omits both projections rather than fabricating a name.

Replies and internal notes remain persisted as Support-owned evidence with their Support-owned `AuthorId`, so the comment projection can be enabled without a data migration once a member display-name contract is admitted. Support creates no separate activity table: the Support-owned command audit already records the operation, actor, prior and new version and occurred time that an activity document would restate.

The read model additionally omits `contactName`, `contactEmail`, `contactPhone`, `relatedOrderNumber`, `relatedProductName`, `ownerName`, `team`, `internalSummary`, and `firstRespondedAt`. All are optional, none is Support state, and no admitted contract supplies them, so these omissions do not affect contract conformance.

### Ownership boundaries

Support depends on Tasks in neither direction. It never touches `TasksDbContext`, a Tasks repository, a Tasks EF entity, Tasks Infrastructure, or a Tasks table, and it shares no table with Tasks. SupportCase status is not Task status: completing a Task does not resolve or close a SupportCase, and closing a SupportCase does not complete a foreign Task. Any future Support-originated Task creation, escalation, completion, reassignment, or cancellation is a multi-owner mutation that belongs to Workflows; no such workflow is currently admitted, so it remains `WORKFLOW REQUIRED`. Support introduces no email ingestion, support mailbox, webhook ingestion, notification engine, customer portal, knowledge base, checklist, attachment storage, SLA configuration administration, or event bus.

Rejected Support commands write no Support audit record, because audit evidence is staged inside the mutation transaction that a rejection rolls back. This matches the current verified Leads, Deals, and Tasks behavior and is not a Support-specific divergence.

### Verification

`backend/scripts/verify-support-core.ps1` is the reproducible Support verifier. It provisions an isolated database, starts ApiHost against it, exercises all eight operations, and drops the database afterwards. It is Windows PowerShell 5.1 compatible and defaults to `(localdb)\MSSQLLocalDB`.

The run on 2026-08-26 reported `PASS=83 FAIL=0`, covering: unauthenticated rejection; empty, filtered and invalid-filter listing; server-assigned identity and `CASE-2026-0001` sequence allocation; committed replay while the owner member is suspended, with no duplicate SupportCase and no duplicate outbox message, alongside correct rejection of a *new* command naming that suspended owner; changed-intent key rejection; stale and missing `If-Match` rejection with version and title left unchanged; total profile replacement clearing an omitted optional field; legacy-category acceptance on replace and rejection on create; non-member owner rejection; assignment leaving the lifecycle unchanged; the full admitted transition path with correct timestamp stamping and clearing; rejection of `new` to `resolved` and `reopened` to `closed` with no mutation; same-state replay; reply and internal-note append with version advance and correct internal separation in persistence; cross-Workspace and unknown-identifier rejection; single-Workspace scoping of all Support state; command audit and read-access-log presence; no undeclared outbox event type and exactly one event per committed mutation; no shared table with Tasks; seven previously verified module reads plus a committed `createTask`; and no pending Support EF model changes.

The idempotency ordering fix is backed by a differential run: against the pre-fix ordering, where the mutable owner/member precondition executed before the idempotency lookup, the same verifier reported `PASS=79 FAIL=4`, with the four failures all in the committed-replay-after-member-suspension scenario. Evidence is retained in `backend/artifacts/SupportCore_runtime_evidence.json` and `backend/artifacts/SupportCore_idempotent.sql`.

**Connected acceptance.** The repository Playwright Chromium runtime drove the real frontend in connected mode against a real ApiHost and a real database (`backend/../frontend/unicorecrm-web/playwright.support-connected.config.ts` with `tests/e2e/support-connected.spec.ts`, run with `UNICORECRM_TEST_API_BASE_URL` pointing at the live host). Two seeded pre-purchase Support Cases - `relationshipRef` of type `CONTACT`, no Customer anywhere in the system - were listed, rendered and mutated. Proven in the browser: `GET /support/cases` returns 200 with `relationshipRef` present and `customerId`/`customerName` absent on every item; the list renders each case by its Support-owned case number and title; the customer column renders the neutral placeholder and the rendered page contains the relationship identifier nowhere, so no Contact identifier is substituted for a customer display name; selecting a lifecycle value on the list card issued `POST /support/cases/{caseId}/transition`, which returned 200 `COMMITTED` with `status` `in_progress`, `relationshipRef` preserved and both customer fields still absent; and the page raised no uncaught error. Backend state confirms the browser-driven mutation was durable: the case advanced from status `new` to `in_progress`, its resource version advanced from 0 to 1, exactly one `SUPPORT_CASE_STATUS_CHANGED` outbox message was staged, and one committed `transitionSupportCase` audit record was written.

The Support **detail** and **form** routes were originally `NOT ATTEMPTABLE`: both are wrapped in `EffectiveRecordAccessBoundary`, which calls `evaluateEffectiveRecordAccess` (`POST /access/records/evaluate`), and AccessControl mapped only `GET /access/context`, so the call returned 404 and the routes rendered an access-verification error instead of the record. The acceptance suite pinned that 404 so the gate would fail the moment AccessControl implemented the operation.

**That blocker is now closed.** AccessControl implements the operation (see *AccessControl record access implementation authority*), the pinned assertion was replaced by real coverage, and the suite was re-run against the real frontend, a real ApiHost and a real database: 6 of 6 passed. Proven in the browser, with an `OWN` data-scope policy and `MASKED`/`HIDDEN` field policies seeded in the AccessControl-owned tables - the Support detail route issues `POST /access/records/evaluate`, receives 200 with `authority` `backend`, the trusted `workspaceId`, `canRead` true and `fieldAccess` of `description` `MASKED`, `slaPolicyId` `HIDDEN` and `subject` `READ_WRITE`, and renders the record with the boundary reporting `data-access-authority="backend"`; the detail route for a case owned by a different member receives 200 with `canRead` false, no allowed command and a `DENY` reason, renders the permission panel instead of the record, and issues **no** `GET /support/cases/{caseId}` at all, so a denied record is never read; the create form, which supplies no record identifier, receives 200 with `canRead` true, `support.create` allowed and the `RECORD_SCOPE_NOT_EVALUATED` reason; every decision is attributed to the backend rather than computed in the client; and no route raised an uncaught error. The previously proven list and lifecycle path was re-run unchanged and still passes. Support business semantics were not modified by this work.

Two of the six tests deliberately bypass the browser and call the Support API directly with the same credentials, because a suite that only showed the frontend declining to issue a request would prove nothing about the server. They prove that `GET /support/cases/{caseId}` for the hidden record returns 404 with the same error code as an unknown case and without the record's title; that a transition aimed at it also returns 404; that the scoped list neither contains it nor counts it in `totalCount`; and that a field withheld by policy is absent from the raw response bytes rather than merely undrawn.

Because an `OWN` data-scope policy is in force for the acceptance fixture, the previously proven list and lifecycle path now legitimately sees only the caller's own case.

One environment note, recorded because it is not a defect in either side: the connected Playwright configuration serves the frontend on `http://127.0.0.1:3000`, while the shared Development configuration allows only the `http://localhost:3000` browser origin, so the run needs `Frontend__AllowedOrigins__1=http://127.0.0.1:3000` exported to the host. Without it every browser API call is blocked by CORS and the whole suite times out at sign-in.

Therefore `SUPPORT CONNECTED ACCEPTANCE: PASS` for the Support list and lifecycle path, for the detail and form routes, and for the direct-API bypass attempts.

Gate status for the verified scope: `SUPPORT DOMAIN/LIFECYCLE: PASS`, `SUPPORT PERSISTENCE: PASS`, `SUPPORT SECURITY: PASS`, `SUPPORT CONCURRENCY: PASS`, `SUPPORT IDEMPOTENCY: PASS`, `SUPPORT AUDIT/OUTBOX: PASS`, `SUPPORT REGRESSION: PASS`, `SUPPORT BACKEND RUNTIME: PASS`, `SUPPORT SLA AUTHORITY: AUTHORITY_GAP`. `SUPPORT CONTRACT CONFORMANCE` is `PASS` following the customer-enrichment amendment recorded above: Support's projection now matches every required field of the amended `SupportCaseReadModel`. This is task-specific evidence, not independent release attestation, external-provider conformance, or browser acceptance.

## Support customer identity reconciliation

This section freezes the reconciliation of `Support.relationshipRef -> Customer identity -> customerId / customerName`. It is an authority decision only. It changes no Support implementation, builds no CRM persistence, and admits no new operation.

Provenance is stated per finding. `PROVEN` means current authority establishes the semantic. `AUTHORITY_GAP` means it does not, and the item stays fail-closed.

### 1. BuyerRef and RelationshipRef are the same value space — PROVEN

The verified OpenAPI declares `BuyerRef` and `RelationshipRef` as structurally identical objects: a required `type` restricted to `CONTACT` or `ORGANIZATION_ACCOUNT`, plus a required `EntityId`. `BuyerType` and the inline `RelationshipRef.type` enum carry the same two members.

The canonical Customer creation path closes the identification: `ensureCustomerFromPurchaseEvidence` takes the buyer reference from purchase evidence and stores it directly as the Customer's `relationshipRef`. `CustomerDocument.relationshipRef` is declared as `RelationshipRef`, and Commercial Evidence owns the `buyerRef` that becomes it.

Support's `SupportCaseReadModel.relationshipRef` is declared as `BuyerRef`. It therefore addresses the same relationship value space that keys a Customer. This does **not** make `relationshipRef.id` equal to `customerId`; it makes `relationshipRef` a valid *lookup key* for a Customer.

### 2. Resolution rule — PROVEN

A Customer is keyed by the pair `(workspaceId, relationshipRef)`. The canonical relationship key is `customerRelationshipKey(workspaceId, relationshipRef)`, and the canonical reverse lookup is `findCustomerByRelationshipRef(workspaceId, relationshipRef)`. Both are Customers-owned semantics named in the canonical Customers module baseline.

The frozen rule is therefore:

```text
(trustedWorkspaceId, relationshipRef)  ->  at most one Customer
```

At most one, never guaranteed one. See finding 6.

No admitted HTTP operation performs this reverse lookup. The query registry classifies `findCustomerByRelationshipRef` as a composed read model with an empty `operationIds` list, and `resolveCustomerRelationshipContext` as frontend-local. Any backend resolution must therefore be an internal narrow owner contract, not a wire operation.

### 3. `customerId` semantics — PROVEN: live canonical Customer reference, not a snapshot

`customerId` is the Customers-owned `CustomerDocument.id` aggregate identifier. It is a live reference to a Customer record whose lifecycle Customers owns; Support must never own, assign, derive, or infer it.

`customerId` is specifically **not** `relationshipRef.id`. `relationshipRef.id` identifies a Contact or an Organization Account, whose identity Customers explicitly does not own. A Customer is a separate aggregate that *links* one relationship to a care lifecycle. Current frontend evidence resolves in the forward direction only — customerId to Customer to relationshipRef — and never treats the two identifiers as interchangeable.

Current frontend source additionally documents `relationshipRef` as "canonical relationship identity" and states that `customerId` "is retained only for route/display compatibility." That is read-only consumer evidence and does not by itself demote the field, but it is consistent with every higher-precedence source: the verified wire contract already requires `relationshipRef` on the Support read model, and the canonical Customers baseline keys the Customer on the relationship rather than the reverse.

### 4. `customerName` semantics — PROVEN: composed live projection; any stored copy is a historical Support-owned fallback

`CustomerDocument` declares **no name field of any kind**. A customer display name is not Customer-owned scalar state.

The canonical display name is `Customer360Identity.displayName`, required with `minLength` 1, returned by the `getCustomer360` operation and described in the contract as a "backend-composed, permission-filtered" projection. Its composition, per current frontend evidence, is:

```text
relationshipRef.type = CONTACT              -> Contact full name, else Customer.customerCode
relationshipRef.type = ORGANIZATION_ACCOUNT -> Organization display name, else Customer.customerCode
```

The `customerCode` fallback is what guarantees the non-empty contract.

Current consumer evidence treats a Support-stored `customerName` as a **historical fallback snapshot, not a live value**: the Support presentation resolver looks the live Customer up by `customerId` and returns the freshly composed display name, falling back to the stored `customerName` only when the live Customer cannot be resolved, and to the raw `customerId` after that.

The frozen semantic is therefore: `customerName` is a live composed projection at read time where the Customer is resolvable, and a Support-owned historical snapshot only as a fallback. Support does not own the name and must never treat a stored copy as authoritative.

### 5. Permission model — PROVEN by precedent: producer capability required

The established cross-owner narrow read contracts in this repository - `ILeadSummaryReader`, `IDealSummaryReader` and `ITaskSummaryReader` - all follow one pattern. Each requires a resolved trusted Workspace, evaluates the **producer owner's** read capability against the calling principal through `IAccessAuthorizer`, applies record and field scope, and collapses foreign or invisible records into a not-found result that leaks no existence. The cross-owner contract map records their authorization behavior as requiring `leads.read`, `deals.read` and `tasks.read` respectively.

Support therefore may **not** consume Customer identity under `support.read` alone. The caller must additionally hold the producer owner's read capability. The canonical capability is `customers.view`; there is no `customers.read` in the capability matrix and none is invented here. Where the display name is composed from a relationship, `contacts.read` or `organizations.read` applies to that leg for the same reason.

No counter-precedent exists. No current authority admits an unauthenticated, capability-free or Support-scoped-only owner-fact contract.

### 6. Customer existence is conditional — PROVEN, and this is the blocking finding

The canonical Customers baseline states that "Customer creation from purchase evidence requires an existing canonical relationship." `CustomerDocument` requires both `firstPurchaseAt` and `lastPurchaseAt`, and carries `createdFromEvidenceId`. The sole creation path, `ensureCustomerFromPurchaseEvidence`, admits only effective purchase evidence - completed order, confirmed external purchase, or imported historical purchase - and returns nothing otherwise.

Every Customer mutation operation in the current registry is `BLOCKED`: `onboardExistingCustomer`, `completeCustomerOnboarding` and `updateCustomerLifecycle`. No admitted operation creates a Customer.

Therefore **a Contact or Organization Account that has not purchased has no Customer, and no contract can resolve one for it.** This is not an implementation limitation; it is the canonical business meaning of Customer.

Support cases are legitimately raised against pre-purchase relationships: the admitted `SupportCaseCreateCategory` vocabulary includes `consultation`, `onboarding`, `request` and `complaint`, none of which implies a completed purchase, and the admitted `SupportCaseSource` vocabulary includes `web_form` and `chat`. A Support case referencing a relationship with no Customer is ordinary, not exceptional.

### 7. Frozen consequence for the Support contract — AUTHORITY_GAP, closable only by contract amendment

`SupportCaseReadModel` declares `customerId` and `customerName` as **required**. Finding 6 proves that requirement is unsatisfiable in general: for a pre-purchase relationship there is no Customer, so there is no `customerId` and no composed `customerName`, and neither can be produced without inventing a Customer that canonical authority says must not exist.

No cross-owner contract, however well specified, can close this. The residual gap is a **contract defect**, not a missing implementation:

- Making the two fields optional in `SupportCaseReadModel` closes it immediately and makes the current fail-closed Support implementation conformant without any CRM work.
- Forcing Support to reject a case whose relationship has no Customer would invent a Support business rule that contradicts the admitted category and source vocabularies, and is not admitted.
- Fabricating a placeholder identifier or echoing `relationshipRef.id` as `customerId` is prohibited by findings 1 and 3.

**This amendment has been executed.** `SupportCaseReadModel` no longer lists `customerId` or `customerName` as required; both remain declared optional properties and `relationshipRef` remains required. The change was applied through the repository generator pipeline, which rewrote the contract hash to `fd079b2f6e189ffe391d555cee1d2acaa735cf532346cc74a02070862bd78792` and regenerated every derived artifact, and the `quality.api-contract` gate passes on the result. Support's existing fail-closed omission is therefore contract-conformant with no Support behavior change.

### 8. Minimum narrow contract, if and when Customers is implemented

Should a Customers reference slice be admitted later, the minimum contract shape that current authority supports is an internal Customers-owned reader, modelled exactly on `ILeadSummaryReader`:

```text
Input :  relationshipRef (type + id), request/correlation identifiers
         trusted Workspace is taken from ICurrentWorkspace, never from the caller
Output:  typed status + optional projection { canonicalCustomerId, customerDisplayName }
```

Frozen behaviors:

- **Workspace isolation.** The trusted Workspace is resolved by Platform and never supplied by the consumer. Lookup is confined to `(trustedWorkspaceId, relationshipRef)`.
- **Unknown reference.** Returns a not-found status. It is not an error, because finding 6 makes "no Customer" a normal outcome.
- **Foreign workspace.** Indistinguishable from unknown. No existence, name, or count is leaked across a Workspace boundary.
- **Permission.** Requires `customers.view` on the calling principal, plus `contacts.read` or `organizations.read` for the display-name leg, evaluated through `IAccessAuthorizer` at the producer boundary. Record and field scope apply; a masked field is omitted rather than substituted.
- **Live vs snapshot.** `canonicalCustomerId` is a live reference. `customerDisplayName` is a live composed projection at call time. Support may retain a copy only as an explicitly historical fallback and must never present it as current.
- **No persistence exposure.** The contract exposes no DbContext, repository, EF entity, table, or SQL surface, and creates no EF navigation across owners.

This shape is recorded as the target. It is **not admitted for implementation** by this task.

### 9. Existing SupportCase rows — frozen: no backfill

Existing SupportCases carry `relationshipRef` and no customer identity. The frozen decision is that they are **left as they are**:

- A deterministic backfill is impossible. Finding 6 means the correct value for a pre-purchase relationship is "no Customer," so a backfill would either write nothing or invent identity.
- A live lookup on read is premature. It would require the Customers reference contract, and would still return nothing for pre-purchase relationships, so it cannot make a required field present.
- The contract amendment in finding 7 makes backfill unnecessary: with the fields optional, existing rows are already conformant.

If a Customers reference slice is later admitted, resolution is a **read-time projection**, never a stored Support-owned copy of `customerId`, because `customerId` is live Customers-owned state.

### 10. Dependency classification

| Owner | Classification | Basis |
|---|---|---|
| Customers | `REFERENCE_CONTRACT_REQUIRED` | Sole owner of `customerId`; owns the relationship-keyed lookup and the `customerCode` display fallback. |
| Contacts | `REFERENCE_CONTRACT_REQUIRED` | Supplies the display name for a `CONTACT` relationship. Reads are already `PRODUCTION_CONTRACT_READY` under `contacts.read`. |
| Organizations | `REFERENCE_CONTRACT_REQUIRED` | Supplies the display name for an `ORGANIZATION_ACCOUNT` relationship. Reads are already `PRODUCTION_CONTRACT_READY` under `organizations.read`. |
| Commercial Evidence | `WORKFLOW_REQUIRED` | Not needed to *read* a Customer, but it is the only admitted path by which a Customer can *exist*. Without admitted purchase evidence the reference contract resolves nothing, so it is a prerequisite for any non-empty result rather than for the contract itself. |

Contacts is not a prerequisite of Customers: the two are independent owners, and Customers is the only owner that can supply `customerId`. Equally, Customers cannot be usefully materialized on its own, because its creation path runs through Commercial Evidence and ultimately Orders.

### 11. Residual AUTHORITY_GAPs

- **Support customer identity — CLOSED.** The contract has been amended: `customerId`/`customerName` are optional and Support's omission is conformant. What remains is not a gap but a deferred capability: no admitted Customers reference contract exists, so the two fields stay absent until one is.
- **Support SLA semantics.** Unchanged; see the Support Core section.
- **Member display name.** Unchanged; see the Support Core section.
- **Customer creation.** Every Customer mutation operation remains `BLOCKED`, and `ensureCustomerFromPurchaseEvidence` has no admitted operation, owner assignment, or workflow contract.

## Relationship / Customer identity authority foundation

This section reconciles Contact, Organization and Customer identity and relationship authority. It is
an authority record only. It introduces no mutation, composition, persistence, route, capability,
provisioning, migration, verifier or cross-owner interface.

The evidence order is the precedence stated at the top of this document: the verified current
OpenAPI, current operation/command/query/workflow registries, the canonical module baseline where it
is not superseded, current backend model/contracts, then current frontend source as supporting
read-only evidence. Frontend snapshots, compatibility aliases and deterministic demo identifiers are
not server authority.

### Canonical entity authority table

| Entity | Owner | Identity | Workspace scope | Creation authority | Mutation authority | Read authority | Relationship key | Cross-owner references | Lifecycle | AccessControl resource | Capabilities | Ownership fact | Current status |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Contact | Contacts | Contact-owned `contactId`; durable/read identity is `(workspaceId, contactId)` and wire identity is `ContactDocument.id` inside trusted Workspace scope. | Required trusted active Workspace; foreign Workspace is indistinguishable from unknown. | `createContact` is `BLOCKED`; the read core admits no creation path. | Contact mutations and the effective-dated Organization-relationship commands are `BLOCKED`. | `listContacts` and `getContact` are admitted and implemented; `getContactRelationshipSummary` has an admitted wire but is not composition-ready. | A Contact↔Organization ledger entry has its own Contacts-owned relationship `id` and an `organizationAccountId`; Customer lookup uses `{ type: CONTACT, id: contactId }`. | Contacts stores Organization identifiers as scalar references in `organizationRelationships`; it stores no authoritative `customerId`. | `active`, `needs_follow_up`, `in_consulting`, `has_open_opportunity`, `inactive`, `do_not_contact`, `archived`; no transition graph is admitted. | `contacts` | `contacts.read` admitted; `contacts.create` and `contacts.update` blocked; remaining Contacts capabilities have no operation authority. | Optional `ownerId` is a proven Workspace-member record-owner fact; unassigned is outside `OWN`. | `ADMITTED` for identity and implemented read core; mutations `BLOCKED`. |
| Organization | Organizations | Organization-owned `organizationId`; durable/read identity is `(workspaceId, organizationId)` and wire identity is `OrganizationDocument.id`. `OrganizationAccount` is the canonical business term in the baseline; the current wire uses Organization. | Required trusted active Workspace; the read core is usable only when the Workspace is explicitly configured and the caller is independently authorized. | `createOrganization` is `BLOCKED`; the read core admits no creation path. | `updateOrganization` and `linkContactToOrganization` are `BLOCKED`; WF-02 is also blocked. | `listOrganizations` and `getOrganization` are admitted and implemented; `getOrganizationOverview` has an admitted wire but is not composition-ready. | The Organization aggregate key is `organizationId`. `primaryContactId` and `contactRefs` are Organizations-owned scalar references, not the authoritative effective-dated relationship ledger. Customer lookup uses `{ type: ORGANIZATION_ACCOUNT, id: organizationId }`. | Organizations stores Contact identifiers as unvalidated scalar references and stores no authoritative `customerId`. | `prospect`, `active`, `strategic`, `inactive`, `archived`; no transition graph is admitted. | `organizations` | `organizations.read` admitted; `organizations.create` and `organizations.update` blocked. | No AccessControl ownership fact is admitted. Stored `ownerId` is not established as record-owner authority, so `OWN`, `TEAM` and `CUSTOM` fail closed. | `ADMITTED` for identity and explicitly configured/authorized read core; mutations `BLOCKED`; normal new-Workspace product use `NOT READY`. |
| Customer | Customers | Customers-owned `customerId`; aggregate access is `(workspaceId, customerId)`. It is independent from `Contact.id`, `Organization.id` and `relationshipRef.id`. | Required trusted active Workspace; all customer lookup and uniqueness rules include Workspace. | The business trigger is an existing canonical relationship plus unreversed effective purchase evidence. The admitted effective types are completed Order, confirmed external purchase and imported historical purchase. The operational creator/workflow, server identity assignment, transaction/idempotency/concurrency/outbox behavior and reversal consequences remain `AUTHORITY_GAP`; customer onboarding mutations are `BLOCKED`. | `updateCustomerLifecycle`, `completeCustomerOnboarding` and `onboardExistingCustomer` are `BLOCKED`; no other mutation is admitted. | `listCustomers` and `getCustomer` are admitted and implemented owner-local reads. `getCustomer360` is an admitted wire but its cross-owner composition is not ready. | The Customers-owned unique/reverse-lookup key is `(workspaceId, relationshipRef)`; the aggregate is still addressed by `customerId`. | Required `relationshipRef` points to exactly one Contact or Organization Account identity. Customers owns the link and lifecycle, not the referenced identity. | Customer: `NEW`, `ACTIVE`, `AT_RISK`, `INACTIVE`, `CHURNED`, `DO_NOT_CONTACT`, `ARCHIVED`; onboarding: `PENDING`, `COMPLETED`; no complete transition graph is admitted. | `customers` | `customers.view` admitted and implemented for reads; `customers.edit` and `customers.onboard_existing` blocked; `customers.create_system`, `customers.assign` and `customers.archive` have no backend operation authority. | `careOwnerId` exists in the document but is not established as the AccessControl record-owner fact; `OWN`, `TEAM` and `CUSTOM` semantics remain `AUTHORITY_GAP`. | `ADMITTED` for independent identity and implemented owner-local list/detail Read Core; creation workflow `AUTHORITY_GAP`; mutations and Customer 360 composition remain incomplete. |

### Authoritative reference map

```text
Contact (Contacts-owned contactId)
  -- Contacts-owned ContactOrganizationRelationship.organizationAccountId -->
Organization (Organizations-owned organizationId)

Organization
  -- Organizations-owned scalar primaryContactId/contactRefs only -->
Contact
  [not the effective-dated relationship ledger; no synchronization authority]

Customer (Customers-owned customerId)
  -- Customers-owned relationshipRef { type: CONTACT, id: contactId } --> Contact
  -- Customers-owned relationshipRef { type: ORGANIZATION_ACCOUNT, id: organizationId } --> Organization

(trustedWorkspaceId, relationshipRef)
  -- Customers-owned reverse lookup --> zero or one Customer
```

There is no authoritative Contact- or Organization-owned forward `customerId`. There is no identity
equivalence in either direction. A Contact↔Organization relationship-entry `id`, an Organization
`organizationId`, a Contact `contactId`, a Customer `customerId`, and a typed `relationshipRef` are
different values with different owners.

### Explicit semantic resolutions

1. **What is a Customer?** A Customers-owned relationship-level commercial/care lifecycle aggregate
   linking exactly one canonical Contact or Organization Account reference to purchase-derived
   Customer state. It does not own the referenced person or business identity.
2. **Independent identity?** Yes. `customerId` is a distinct Customers-owned aggregate identifier.
   It is never inferred from `contactId`, `organizationId` or `relationshipRef.id`.
3. **What creates one?** The business eligibility rule is an existing canonical relationship plus
   unreversed effective purchase evidence. Reads never create Customers. The operational creation
   workflow remains `AUTHORITY_GAP` because WF-05 Customer Conversion and the related identity,
   onboarding and integrity workflows are blocked and no admitted owner contract creates the row.
4. **Is an Organization automatically a Customer?** No. Without qualifying purchase evidence it is
   only an Organization.
5. **Is a Contact automatically a Customer?** No. Without qualifying purchase evidence it is only a
   Contact.
6. **Canonical relationship reference?** `RelationshipRef`, the closed discriminated value
   `{ type: CONTACT | ORGANIZATION_ACCOUNT, id: EntityId }`, interpreted inside trusted Workspace
   scope. `BuyerRef` is the same value space in the verified wire.
7. **`organizationAccountId` meaning?** It is an Organizations-owned aggregate identifier stored as
   a scalar foreign reference on a Contacts-owned `ContactOrganizationRelationship`. It is neither
   the relationship-entry identifier nor a Customer identifier. The referenced Organization must be
   resolved in the same trusted Workspace by an owner contract when validation/enrichment is needed;
   no such mutation contract is admitted now.
8. **`relationshipRef` meaning?** It selects one canonical relationship identity: either the Contact
   itself or the Organization Account itself. It does not identify Contact↔Organization membership
   and contains no presentation or Customer identity.
9. **Customer key?** Public aggregate lookup uses `customerId`; uniqueness and reverse resolution use
   the Customers-owned `(trustedWorkspaceId, relationshipRef)` key. Neither Contact nor Organization
   alone is a Customer key, and one Customer cannot cover both references at once.
10. **Contact↔Organization relationship owner?** Contacts owns the effective-dated membership ledger
    and its relationship-entry identity. Organizations owns only its account identity and its own
    scalar representative-reference projection. Cross-owner synchronization and primary
    representative uniqueness belong to WF-02, which is blocked.
11. **Who may read relationship facts?** Contacts may read/project its ledger under `contacts.read`
    with Workspace, record and field enforcement. Organizations may read only its own scalar
    reference fields under `organizations.read`; those fields do not replace Contacts authority. A
    foreign consumer needs a narrow Contacts-owned reader and the producer capability; none is
    currently admitted for these compositions.
12. **Who may mutate relationship facts?** No callable mutation is admitted. The Contacts-owned
    upsert/end/set-primary commands, the Organizations-owned `linkContactToOrganization` wire, and
    WF-02 are all blocked or unresolved. No owner may currently mutate or synchronize the bridge.
13. **`getContactRelationshipSummary` requirements?** Contacts supplies the Contact and
    `organizationIds` from its own ledger. `customerIds` requires a Customers-owned relationship
    lookup. Every required linked-record count and any `linkedRecords` item requires a narrow
    producer-owned, permission-filtered reader from its source owner. `allowedActions`, projection
    version composition, partial visibility/count behavior and exact Customer membership rules are
    not defined. It is `AUTHORITY_GAP`.
14. **`getOrganizationOverview` requirements?** Organizations supplies its aggregate. Authoritative
    representative identifiers and optional primary Contact require a Contacts-owned reader. Deal
    and Order owners must supply the defined counts and monetary metrics; any linked record must come
    from its owner. `allowedActions`, projection version composition, partial visibility/count
    behavior, money aggregation/currency rules and the authoritative precedence between Organization
    scalar references and Contacts ledger facts are unresolved. It is `AUTHORITY_GAP`.
15. **Independent implementability?** Neither composition is independently implementable. The
    owner-local Customer list/detail read core is independently implementable without either
    composition and without a foreign reader.

### Composition readiness and narrow owner boundaries

| Slice | Readiness | Minimum owner-boundary requirement | Blocking authority |
|---|---|---|---|
| `getContactRelationshipSummary` | `AUTHORITY_GAP` | Contacts stays coordinator; Customers would provide a Workspace-confined relationship-to-Customer lookup; each linked-record owner would provide only permission-filtered counts/references needed by the exact response. | Exact `customerIds` membership, producer set, count visibility, `linkedRecords`, `allowedActions`, projection version and owner-contract rows are absent. |
| `getOrganizationOverview` | `AUTHORITY_GAP` | Organizations stays coordinator; Contacts would provide visible Organization-contact facts; Deals and Orders would provide only their admitted metrics; any other linked-record owner would provide minimized references. | Relationship precedence, metric/currency rules, producer set, visibility/count behavior, `linkedRecords`, `allowedActions`, projection version and owner-contract rows are absent. |
| Customer owner-local read core: `listCustomers` + `getCustomer` | `PASS` | None. Customers reads only Customers-owned state, treats `relationshipRef` as a scalar typed reference, performs no validation/enrichment, and applies trusted Workspace, `customers.view`, record/field scope and read audit at its own boundary. | Implemented and executable. `careOwnerId` is not promoted into an ownership fact; unresolved `OWN`/`TEAM`/`CUSTOM` fail closed. |

The conceptual boundaries above are requirements, not interfaces. The current cross-owner contract map
contains none of the Contacts/Organizations/Customers readers needed by the two compositions, and the
exact response semantics are not closed. No interface is introduced until those gaps are resolved and
an immediate implementation slice is admitted. The Customer owner-local read core needs no
cross-owner contract and must not introduce one speculatively.

### Remaining foundation AUTHORITY_GAPs

- WF-02 does not define which command mutates the Contacts ledger, how Organizations scalar
  references converge, how primary-representative uniqueness is enforced, or the transaction,
  concurrency, idempotency, compensation and audit semantics.
- Customer creation eligibility is known, but no admitted workflow defines the producer event,
  Customers-owned server identifier/code assignment, relationship validation boundary, uniqueness
  race handling, reversal behavior, transaction, idempotency, concurrency, outbox or audit semantics.
- No current authority establishes Customer `careOwnerId` or Organization `ownerId` as an
  AccessControl ownership fact.
- The two composed reads lack admitted cross-owner contracts and exact partial-visibility, metric,
  linked-record, action and composed-version semantics.
- Normal new-Workspace enablement/capability authority remains absent for Organizations. No change to
  provisioning is admitted by this foundation.

Foundation gates: `CUSTOMER IDENTITY: PASS`,
`CUSTOMER CREATION SEMANTICS: AUTHORITY_GAP`,
`CONTACT ↔ ORGANIZATION RELATIONSHIP OWNER: PASS — CONTACTS`,
`CONTACT ↔ ORGANIZATION RELATIONSHIP MUTATION: AUTHORITY_GAP`,
`CONTACT ↔ ORGANIZATION CROSS-OWNER SYNCHRONIZATION: AUTHORITY_GAP`,
`WF-02: AUTHORITY_GAP / BLOCKED`,
`RELATIONSHIP REFERENCE SEMANTICS: PASS`,
`GET CONTACT RELATIONSHIP SUMMARY: AUTHORITY_GAP`,
`GET ORGANIZATION OVERVIEW: AUTHORITY_GAP`,
`CUSTOMER READ CORE: PASS`,
`CROSS-OWNER CONTRACTS REQUIRED: AUTHORITY_GAP`, and
`RELATIONSHIP / CUSTOMER IDENTITY FOUNDATION: AUTHORITY_GAP`.

The previously identified safe implementation slice—Customers owner-local `listCustomers` plus
`getCustomer`—is now implemented as recorded below. No next Customer mutation, Customer 360, Contact
relationship summary, or Organization overview slice is independently admitted; the safe next
implementation slice from this foundation is therefore `NONE` until its named authority gaps close.

## Customers Read Core implementation authority

### Reproducible wire evidence

The 2026-08-29 release-hardening task started from frontend commit
`bfcabd0f44e93f7e9f15dacef9829d9d7666f546`, equal to `origin/main`, and committed the exact contract
artifact plus its required generated `docs/api` metadata in the dedicated local frontend commit
`c12a182f4df86976b018b09d2d9080d0ab46b722` (`docs(api): freeze current customer contract`). The
OpenAPI SHA-256 is `fd079b2f6e189ffe391d555cee1d2acaa735cf532346cc74a02070862bd78792`, exactly matching
`docs/api/openapi.sha256`. Every `/customers*` path and its transitive schemas are deeply unchanged
from the frontend baseline; the whole-file checksum changed only for two Product read concurrency
annotations and Support Customer-reference optionality. `CUSTOMER WIRE LOCAL CHECKSUM: PASS` and
`CUSTOMER WIRE DRIFT: NONE`.

The frontend contract commit was not pushed; remote `origin/main` remains at the previous checksum.
The official frontend `quality.api-contract` gate passes in the current working tree, whose existing
generated frontend source and generator changes are outside this task's allowed scope. A clean
checkout of the contract-only commit therefore does not reproduce that wider generated-source state.
`CUSTOMER WIRE REMOTE REPRODUCIBILITY: NOT READY`; no repository-wide frontend generation claim is
made from the local contract commit.

The pinned wire admits exactly `listCustomers` (`GET /customers`) and `getCustomer`
(`GET /customers/{customerId}`) for this slice. Both are `PRODUCTION_CONTRACT_READY`, require trusted
Workspace context and `customers.view`, and require `READ_ACCESS_LOG`. `CustomerList` is a plain,
unpaginated array with no admitted query, filter, search, sort, page, cursor, count, or metadata.
Internal `createdAt` descending then `customerId` ordering is deterministic implementation behavior,
not a wire promise. `CUSTOMERS LIST PAGINATION: NOT ADMITTED`.

The required `CustomerDocument` fields are `id`, `workspaceId`, `customerCode`, `type`,
`relationshipRef`, `status`, `health`, `firstPurchaseAt`, `lastPurchaseAt`, `version`, `createdAt`,
and `updatedAt`. Optional fields are `calculatedHealth`, `manualHealthOverride`, `onboardingStatus`,
`onboardingCompletedAt`, `createdFromEvidenceId`, `conversionPolicyVersion`,
`conversionCorrelationId`, `sourceSystem`, `externalCustomerRef`, `tier`, `serviceLevel`,
`careCadenceDays`, `careOwnerId`, `segment`, `tags`, `nextCareAt`, and `lastCareAt`. `type` is `B2C`
or `B2B`; `relationshipRef.type` is `CONTACT` or `ORGANIZATION_ACCOUNT`; status is `NEW`, `ACTIVE`,
`AT_RISK`, `INACTIVE`, `CHURNED`, `DO_NOT_CONTACT`, or `ARCHIVED`; health is `GOOD`, `WATCH`, or
`RISK`; onboarding status is `PENDING` or `COMPLETED`; tier is `STANDARD`, `SILVER`, `GOLD`,
`PLATINUM`, or `STRATEGIC`; service level is `STANDARD`, `PRIORITY`, `PREMIUM`, or `ENTERPRISE`.
Entity/Workspace identifiers use the canonical one-to-128-character EntityId form, resource version
is a non-negative integer, and all timestamps are projected as UTC `Z` date-times.

### Identity, persistence, access and read behavior

Customers owns `CustomersDbContext`, the `customers` logical schema, `customers.Customers`, and
`customers.ReadAuditRecords`. Durable identity is the composite `(WorkspaceId, CustomerId)` primary
key. `customerId` is never inferred from `contactId`, `organizationId`, or `relationshipRef.id`.
`RelationshipRef` remains a Customers-owned typed scalar. Persistence normalizes it into
`RelationshipType` and `RelationshipId`, with a unique index on
`(WorkspaceId, RelationshipType, RelationshipId)`. The same typed reference may occur in different
Workspaces but never twice in one Workspace. No Contact or Organization existence validation,
enrichment, join, synchronization, or foreign persistence read occurs.

Required wire state is stored in explicit columns. Migration
`20260829053302_CustomersRequiredEnumConstraints` adds the Customers-owned relational constraints
`CK_Customers_Type`, `CK_Customers_RelationshipType`, `CK_Customers_Status`, and
`CK_Customers_Health`. Each constraint admits only the exact closed values above using binary
comparison plus exact byte length, so the default case-insensitive SQL Server collation and
space-padded comparison cannot admit a value that would violate the wire. The migration adds no
default and rewrites no row; pre-existing invalid state blocks migration rather than being normalized.
Once applied, these required values cannot be persisted and therefore cannot be serialized outside
the exact contract.

Optional current read state remains in the Customers-owned profile JSON. No minimal trustworthy
relational mechanism was found for enforcing its closed enum fields without introducing a bespoke
JSON-validation system whose semantics could diverge from `System.Text.Json`.
`CUSTOMER OPTIONAL PROFILE ENUM PERSISTENCE: KNOWN HARDENING LIMITATION`; controlled verifier
fixtures use only admitted `CustomerHealth`, `OnboardingStatus`, `Tier`, and `ServiceLevel` values.
The slice adds no lifecycle transition, onboarding command, creation workflow, purchase conversion,
reversal behavior, identifier/code generator, or speculative domain mutation. Migration
`20260828141857_CustomersReadCore` creates only the owner state and immutable successful-read
evidence. Query-driven indexes are
`(WorkspaceId, CreatedAt, CustomerId)`, `(WorkspaceId, OccurredAt)`, and
`(WorkspaceId, CustomerId, OccurredAt)` in addition to the relationship reverse unique index.

The one canonical AccessControl descriptor is resource `customers`, read capability
`customers.view`, using `IRecordAccessEvaluator`. The provider returns a found record with no member
owner fact: `careOwnerId`, Contact owner, Organization owner, relationship target, Deal owner, and
creator are not inherited as Customer ownership. `WORKSPACE` reads are supported. `OWN`, `TEAM`, and
`CUSTOM` remain `AUTHORITY_GAP` and fail closed before list row loading; detail returns the same 404
as an unknown identifier. Field vocabulary is exactly the pinned top-level document vocabulary.
`READ_WRITE` and `READ_ONLY` are readable; optional `HIDDEN` and `MASKED` values are withheld and
omitted. No rendered mask is admitted. A forbidden required field refuses the representation with
`ACCESS_DENIED`; an unknown field remains unreadable/unwritable without widening the operation.

List performs authentication, trusted Workspace resolution, one `customers.view` resource decision,
SQL Workspace pushdown, projection, and one Customers successful-read audit. It performs no per-row
record decisions and no in-memory post-load security filtering. Detail validates the EntityId and
queries with both `WorkspaceId` and `CustomerId`; it never loads globally then compares. Unknown,
foreign-Workspace, malformed, and same-Workspace scope-hidden Customer identifiers disclose the same
404 representation and no Customer value through response, Customers audit, AccessControl evidence,
or host log. Successful audit identity is Workspace-scoped and includes actor, request, correlation,
operation, version when applicable, and UTC occurrence time.

Only the two GET routes are mapped. There is no Customer create/update/delete/onboarding/conversion
route, no Customer 360, no relationship composition, no generic cross-owner reader, no internal HTTP
authorization, no foreign DbContext, and no Customers business state in BuildingBlocks. The initial
Workspace module defaults remain exactly `contacts`, `leads`, `deals`, and `tasks`; the initial owner
role is unchanged without `customers.view`. No provisioning or convergence change is admitted.
Therefore `CUSTOMERS DEFAULT NEW-WORKSPACE MODULE: AUTHORITY_GAP`,
`CUSTOMERS INITIAL OWNER CAPABILITY: AUTHORITY_GAP`, and
`CUSTOMERS END-TO-END NORMAL-WORKSPACE READ: NOT READY` even though an explicitly configured and
authorized Workspace can use the owner-local read core. The decisions are independent: module and
capability registration plus an implemented read surface establish neither a new-Workspace default
nor an initial-role grant, and the separately admitted Contacts precedent does not widen Customers.
Historical AccessControl roles, including custom roles, are unchanged and no capability snapshot or
convergence path is added. Historical `EnabledModuleKeysJson`, `ConfigurationVersion`, stored
provisioning fingerprint, and effective provisioning intent are unchanged. Existing replay and
fail-closed fingerprint behavior are therefore preserved without a new code path.
`EXISTING WORKSPACE CUSTOMERS MODULE CONFIG UPGRADE: AUTHORITY_GAP / NOT IMPLEMENTED`.

### Executable evidence and gates

On 2026-08-29, `scripts/verify-customers-read-core.ps1` ran against an isolated LocalDB database and
real ApiHost and reported `PASS=117 FAIL=0`. In addition to the preserved Read Core evidence, it
proved all four constraints present/enabled/trusted; lowercase and padded invalid values rejected at
the persistence boundary with no row left behind; every admitted required enum value persisted and
projected exactly; every admitted optional profile enum used by controlled fixtures; the exact two
GET operation names; required-field completeness; no unexpected response field; unchanged reverse
uniqueness/cross-Workspace allowance; and no pending Customers EF changes. Unchanged regressions
reported AccessControl Record Access `PASS=404 FAIL=0`, Contacts Read Core `PASS=67 FAIL=0`, and
Organizations Read Core `PASS=71 FAIL=0`.

`dotnet build UnicoreCRM.slnx --no-restore` completed with zero warnings and zero errors. GitHub
published zero status checks and zero workflow runs for backend commit
`06efd938e1f9323f8f8c0b65afb026cbc04c1122`; the local frontend contract commit is not published.
`GITHUB CI EVIDENCE: NONE`; local executable evidence is not reported as CI.

Therefore `CUSTOMER IDENTITY: PASS`, `CUSTOMER RELATIONSHIPREF: PASS`,
`CUSTOMER REVERSE UNIQUENESS: PASS`, `CUSTOMERS MODULE BOUNDARY/PERSISTENCE/WORKSPACE ISOLATION/
CROSS-WORKSPACE NON-LEAKAGE/LIST/DETAIL/READ CAPABILITY/RECORD ACCESS/WORKSPACE SCOPE/FIELD SECURITY/
SQL SCOPE PUSHDOWN/NO N+1/READ AUDIT/EF MODEL: PASS`, `CUSTOMERS OWN/TEAM/CUSTOM: AUTHORITY_GAP`,
`CUSTOMER REQUIRED ENUM PERSISTENCE: PASS`,
`CUSTOMER OPTIONAL PROFILE ENUM PERSISTENCE: KNOWN HARDENING LIMITATION`,
`CUSTOMERS MASKED: AUTHORITY_GAP` for any rendered mask beyond safe withholding,
`CUSTOMER CREATION SEMANTICS: AUTHORITY_GAP`, `CUSTOMER 360: AUTHORITY_GAP`,
`CUSTOMERS READ CORE: PASS`, `CUSTOMERS END-TO-END NORMAL-WORKSPACE READ: NOT READY`, and
`CUSTOMERS FULL MODULE: NOT COMPLETE`.

## PurchaseEvidence Aggregate Identity Frozen Authority

### Decision provenance and scope

On 2026-08-29, the PurchaseEvidence Aggregate-ID Scheme authority task created the decisions in this
section as **NEW FROZEN TECHNICAL AUTHORITY**. Existing authority already established
CommercialEvidence as the canonical owner of the append-only `PurchaseEvidence` aggregate, required
trusted Workspace scope, and separated source/correlation concepts from aggregate identity. It did
not establish the exact durable aggregate key, allocation character, collision behavior, or reversal
ID namespace. No higher-precedence source was found that contradicts the technical decisions below.
These decisions are explicitly frozen by this task; they are not attributed to OpenAPI, the
registries, Design Authority, frontend behavior, or an existing runtime implementation.

This section decides only PurchaseEvidence aggregate identity. It does not decide the producer,
source namespace or source-key tuple; source-specific replay transport; reversal authorization or
complete durable row shape; effective-evidence policy; audit/outbox schemas; a consumer contract;
Customer conversion; or WF-05 execution. It admits no operation, route, command, query, event,
cross-owner contract, persistence model, migration, or implementation slice.

### Aggregate key and ID allocation

The canonical durable PurchaseEvidence identity is `(workspaceId, evidenceId)`, represented by the
future relational key `(WorkspaceId, EvidenceId)`. Workspace is part of the aggregate identity and
`evidenceId` is unique only within one Workspace; it need not be globally unique. Every owner-local
lookup or mutation must use a trusted Workspace context plus `evidenceId`. Caller-supplied Workspace
data is never authority, lookup by `evidenceId` alone is insufficient, a foreign-Workspace collision
is legal and must not disclose existence, and the same `evidenceId` may identify different records in
different Workspaces.

CommercialEvidence alone allocates `evidenceId`. It is an opaque, immutable, server-generated owner
identifier with no business meaning beyond identifying the canonical record. It is not supplied by
the frontend, Orders, Workflows, Customers, or another source owner and is not derived from
`orderId`, an external transaction identifier, an import row identifier, `correlationId`, BuyerRef,
or any future source key. The identifier satisfies the repository's canonical EntityId envelope:
one to 128 characters matching `^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$`. This authority does not mandate
Guid, UUID, ULID, Snowflake, a prefix, or another generator technology.

Aggregate identity and producer/source identity are distinct. The aggregate key identifies the
CommercialEvidence-owned record. A future Workspace-qualified source key will support idempotency,
replay, provenance uniqueness, and resolution to the existing canonical `evidenceId`; it must not
replace the aggregate key or deterministically fabricate `evidenceId`. BuyerRef and `correlationId`
are neither aggregate identity nor source identity.

### Non-reuse, collision and reversal namespace

After a PurchaseEvidence or reversal record is successfully appended, its `evidenceId` is never
reused within that Workspace. Reversal and retention do not release it, and deletion is not part of
the canonical model. A generated candidate that fails before canonical append creates no durable ID
reservation unless separately admitted later.

A generated-ID collision with an already persisted `(WorkspaceId, EvidenceId)` is not source replay,
is not success, grants no authority to return the unrelated existing record, and never permits that
record to be mutated. CommercialEvidence may generate another opaque candidate internally before a
successful append result is exposed. The exact bounded retry algorithm and retry count are deferred
implementation details; if internal allocation cannot establish uniqueness, the future operation
must fail closed.

A reversal is a separate immutable CommercialEvidence record in the same `evidenceId` namespace as
original PurchaseEvidence. Its durable identity is also `(workspaceId, evidenceId)`; it receives a
new opaque `evidenceId`, shares the original's trusted Workspace boundary, and separately references
the original through `reversalOfEvidenceId`. It never reuses the original ID. This freezes no other
reversal row field and does not decide BuyerRef/source/evidence-type duplication, policy provenance,
reason, authorization, multiple reversals, reversal-of-reversal, concurrency, or Customer effects.

### Conceptual relational and security consequences

The conceptual future PurchaseEvidence primary key is `(WorkspaceId, EvidenceId)`. No alternate or
unique source constraint is admitted by this section. Trusted Workspace is mandatory for future
owner-local reads and writes; source-key lookup must also become Workspace-qualified when its exact
namespace is frozen. No public API is created by these decisions.

| Concern | Frozen decision | Authority type | Remaining gap |
|---|---|---|---|
| Aggregate owner | CommercialEvidence | Existing | None |
| Aggregate key | `(workspaceId, evidenceId)` | NEW FROZEN TECHNICAL AUTHORITY | None |
| ID allocation owner | CommercialEvidence | Existing | None |
| ID character | Opaque server-generated EntityId | NEW FROZEN TECHNICAL AUTHORITY | Exact generator implementation |
| Source-derived ID | No | NEW FROZEN TECHNICAL AUTHORITY | None |
| ID non-reuse | Yes after canonical append | Existing + frozen clarification | None |
| Collision | Internal re-allocation or fail closed | NEW FROZEN TECHNICAL AUTHORITY | Bounded retry implementation |
| Reversal namespace | Same `evidenceId` namespace | NEW FROZEN TECHNICAL AUTHORITY | Reversal operation and complete row semantics |
| Aggregate/source identity separation | Required | NEW FROZEN TECHNICAL AUTHORITY | Exact Workspace-qualified source tuple |

The following invariants are frozen `PASS`:

1. PurchaseEvidence canonical identity is `(WorkspaceId, EvidenceId)`.
2. CommercialEvidence alone allocates `evidenceId`.
3. `evidenceId` is opaque.
4. `evidenceId` is not derived from source identity.
5. `evidenceId` is immutable.
6. A successfully appended `evidenceId` is never reused in that Workspace.
7. Generated-ID collision is not source replay.
8. Collision never returns an unrelated existing evidence record.
9. Reversal receives its own `evidenceId`.
10. Reversal uses the same `evidenceId` namespace.
11. Reversal and original share the trusted Workspace boundary.
12. Aggregate identity and source identity are separate concepts.
13. Source replay resolves through a future source key, not by fabricating `evidenceId`.
14. BuyerRef is not aggregate identity.
15. `correlationId` is not aggregate identity.
16. The same `EvidenceId` may exist in another Workspace without identity conflict or disclosure.

### Deferred authority and readiness

The exact `sourceType` vocabulary, `sourceSystem` semantics, `sourceId` meaning for Order, external,
and historical evidence, Workspace-qualified source identity tuple, source uniqueness, participation
of `evidenceType` in that key, producer contracts, replay execution, reversal operation and complete
reversal durable shape remain `AUTHORITY_GAP`. `EVIDENCE REVERSAL DURABLE RECORD MODEL: PARTIALLY
FROZEN`, `EVIDENCE REVERSAL OPERATIONAL SEMANTICS: AUTHORITY_GAP`, and WF-05 Customer Conversion
remains `BLOCKED`.

Therefore `PURCHASE EVIDENCE AGGREGATE-ID AUTHORITY FREEZE: PASS`,
`PURCHASE EVIDENCE AGGREGATE IDENTITY: PASS`, `PURCHASE EVIDENCE IDENTITY MODEL: READY`,
`PURCHASE EVIDENCE SOURCE-KEY MODEL: AUTHORITY_GAP`,
`EVIDENCE REVERSAL COMPLETE DURABLE MODEL: AUTHORITY_GAP`,
`COMMERCIALEVIDENCE DURABLE MODEL: AUTHORITY_GAP`,
`COMMERCIALEVIDENCE OWNER-LOCAL PERSISTENCE: NOT YET ADMITTED`, and
`SAFE NEXT IMPLEMENTATION SLICE: NONE`. The exact next authority task is
**PurchaseEvidence Source Namespace and Source-Key Authority Freeze**.

## PurchaseEvidence Source Namespace and Source-Key Frozen Authority

### Decision provenance and scope

On 2026-08-29, the PurchaseEvidence Source Namespace and Source-Key authority task created the
decisions in this section as **NEW FROZEN TECHNICAL AUTHORITY**. The current local working-tree
version of this document, including the preceding uncommitted PurchaseEvidence Aggregate Identity
Frozen Authority, was the Level-1 authority input and is preserved unchanged. The adopted OpenAPI,
canonical registries, architecture invariants, non-superseded Design Authority, backend skeleton and
targeted frontend supporting evidence were then applied in that order. No higher-precedence source
was found that contradicts the source taxonomy or source-key decisions below. These decisions are
not attributed to OpenAPI, Design Authority, frontend behavior or a runtime implementation.

This section freezes only source classification, namespace, identifier meaning, exact identity
equality, uniqueness, replay semantics, source/evidence cardinality and the conceptual relational
source-key shape for canonical original PurchaseEvidence. It does not admit or define a producer,
command/event/transport contract, operation-level idempotency, source-truth validation, transaction
choreography, reversal operation, effective-evidence policy, audit/outbox schema, consumer contract,
Customer lifecycle effect or WF-05 execution. It creates no operation, route or implementation
slice.

### Source taxonomy and exact source identities

The admitted original `evidenceType` vocabulary remains exactly `ORDER_COMPLETED`,
`EXTERNAL_PURCHASE_CONFIRMED` and `HISTORICAL_PURCHASE_IMPORTED`. No Payment, Invoice, manual or
generic purchase evidence kind is admitted. `sourceType` is a separate provenance classification
whose exact vocabulary is now `ORDER`, `EXTERNAL_PURCHASE` and `HISTORICAL_IMPORT`.

| Evidence type | `sourceType` | `sourceSystem` | `sourceId` semantic | Exact source identity | `evidenceType` in key? | Authority |
|---|---|---|---|---|---|---|
| `ORDER_COMPLETED` | `ORDER` | Not part of Order source identity | Canonical Orders-owned `orderId` | `(workspaceId, ORDER, orderId)` | No | PASS |
| `EXTERNAL_PURCHASE_CONFIRMED` | `EXTERNAL_PURCHASE` | Required canonical external namespace | Immutable purchase-fact ID inside `sourceSystem` | `(workspaceId, EXTERNAL_PURCHASE, sourceSystem, sourceId)` | No | PASS |
| `HISTORICAL_PURCHASE_IMPORTED` | `HISTORICAL_IMPORT` | Required original historical namespace | Immutable original purchase-fact ID inside `sourceSystem` | `(workspaceId, HISTORICAL_IMPORT, sourceSystem, sourceId)` | No | PASS |

`ORDER` means the canonical source fact is an Orders-owned Order. Its `sourceId` is the canonical
`orderId`; `sourceSystem` is not required and does not participate in Order source identity. One
exact `(workspaceId, ORDER, orderId)` source may produce zero or one canonical `ORDER_COMPLETED`
PurchaseEvidence. Repeated handling of that source is replay, not a second evidence fact. This does
not decide who invokes CommercialEvidence or when an Order is eligible to close.

`EXTERNAL_PURCHASE` means the source fact originates from an identified external source system.
`sourceSystem` is required as its opaque canonical namespace, and `sourceId` is the immutable
identifier of the purchase fact within that namespace. Identical native identifiers from two source
systems are distinct source identities. No provider value, registration model, integration owner or
confirmation-validity rule is frozen here.

`HISTORICAL_IMPORT` means the source fact is an original historical purchase from an identified
source system. `sourceSystem` is the required original namespace and `sourceId` is the immutable
identifier of the original purchase fact inside it. Import batch ID, import execution ID, upload or
session ID and row number are not canonical source identity. Re-importing the same original
`sourceSystem` plus `sourceId` in another batch resolves to the same source identity. Future import
execution and validation authority remain unresolved.

### Source namespace, identifier and equality semantics

`sourceSystem` is an opaque canonical namespace identifier where the native `sourceId` is not
sufficient by itself. It is immutable provenance and participates in External and Historical source
identity; it is not `evidenceId`, WorkspaceId, BuyerRef, `correlationId` or a display label. This task
does not define a global source-system registry, provider onboarding, physical storage or maximum
length.

`sourceId` identifies the business/source fact being deduplicated: canonical `orderId` for Order,
the external purchase-fact identifier within its external namespace, or the original historical
purchase-fact identifier within its historical namespace. An API request, import execution, upload,
batch, row, correlation, aggregate evidence or buyer identifier cannot substitute for `sourceId`.

Two source identities are equal only when every component of the applicable source-type-specific key
is exactly equal. There is no fuzzy matching and equality is not inferred from BuyerRef, amount,
occurrence time, note, document or `correlationId`. Database collation and normalization are deferred
implementation details and must preserve this exact canonical equality. A changed source-key
component denotes a different source identity; it must not be silently normalized into an existing
one.

`evidenceType` does not participate in the current source key. Each admitted `sourceType` maps to one
admitted evidence kind: `ORDER` to `ORDER_COMPLETED`, `EXTERNAL_PURCHASE` to
`EXTERNAL_PURCHASE_CONFIRMED`, and `HISTORICAL_IMPORT` to `HISTORICAL_PURCHASE_IMPORTED`. The key may
not be widened to let one current source fact manufacture multiple evidence kinds. Future authority
that admits such a case must explicitly amend this rule.

### Source uniqueness and replay

Within one Workspace, one exact source identity corresponds to at most one canonical original
PurchaseEvidence. The same source components may coexist in another Workspace. BuyerRef is not
unique; multiple distinct source identities may reference the same BuyerRef, and no PurchaseEvidence
uniqueness rule may use `(workspaceId, BuyerRef)`.

When the same exact source identity is presented with the same canonical immutable PurchaseEvidence
payload, the existing canonical PurchaseEvidence identity is resolved and reused. No evidence row is
created, no replacement `evidenceId` is allocated and the existing record is not mutated. This is a
durable semantic rule only; response, status, command DTO, acknowledgement and retry transport are
not defined.

When the same exact source identity is presented with a different canonical immutable payload, the
attempt fails closed. The existing record remains unchanged; no merge, normalization, second
PurchaseEvidence, correction, supersession or reinterpretation as a new purchase is admitted. A
different BuyerRef, occurrence time, `evidenceType` or other required immutable fact is therefore a
conflict for that source identity. The exact internal or public error contract is deferred.

`correlationId` remains immutable provenance/workflow correlation and does not participate in source
identity. Changing it cannot manufacture a new source fact; where a persisted canonical correlation
would differ under the same source identity, the changed-payload fail-closed rule applies. BuyerRef
also remains excluded from source identity. Aggregate identity remains the separately frozen
`(workspaceId, evidenceId)`; a source key is an alternate identity/idempotency boundary that resolves
to an aggregate and never generates or replaces its `evidenceId`.

### Conceptual relational source-key model

The aggregate primary key remains `(WorkspaceId, EvidenceId)`. The following are semantic future
uniqueness constraints for original evidence only:

- `ORDER`: unique within Workspace on `(SourceType = ORDER, SourceId)`;
- `EXTERNAL_PURCHASE`: unique within Workspace on
  `(SourceType = EXTERNAL_PURCHASE, SourceSystem, SourceId)`;
- `HISTORICAL_IMPORT`: unique within Workspace on
  `(SourceType = HISTORICAL_IMPORT, SourceSystem, SourceId)`.

A future implementation may use filtered unique indexes, discriminator-aware constraints or another
relationally equivalent mechanism. No physical representation, collation, index technology, EF
model or migration is admitted here. `EvidenceType`, BuyerRef and `CorrelationId` must not widen or
replace these current semantic source keys.

### Ownership boundary, decision table and invariants

Orders owns Order truth. CommercialEvidence owns PurchaseEvidence and canonical PurchaseEvidence
source uniqueness. External/historical producer and source-truth ownership remain unresolved. This
authority permits neither CommercialEvidence access to Orders, Payments, Invoices, Contacts or
Organizations persistence nor source-owner writes to CommercialEvidence persistence. The production
model remains `DEFERRED / AUTHORITY_GAP`; no command or cross-owner contract is admitted.

| Concern | Frozen decision | Authority type | Remaining gap |
|---|---|---|---|
| `sourceType` vocabulary | `ORDER` / `EXTERNAL_PURCHASE` / `HISTORICAL_IMPORT` | NEW FROZEN TECHNICAL AUTHORITY | None |
| Order `sourceId` | Canonical `orderId` | NEW FROZEN TECHNICAL AUTHORITY | Producer execution |
| External `sourceSystem` | Required namespace | NEW FROZEN TECHNICAL AUTHORITY | Provider registration |
| External `sourceId` | Source-system purchase ID | NEW FROZEN TECHNICAL AUTHORITY | Producer validation |
| Historical `sourceSystem` | Required original namespace | NEW FROZEN TECHNICAL AUTHORITY | Import producer |
| Historical `sourceId` | Original purchase-fact ID | NEW FROZEN TECHNICAL AUTHORITY | Import validation |
| `evidenceType` in source key | No | NEW FROZEN TECHNICAL AUTHORITY | Future explicit amendment only |
| Identical replay | Reuse existing evidence | Existing + frozen clarification | Transport |
| Changed replay | Fail closed | Existing + frozen clarification | Error contract |
| Aggregate/source identity separation | Required | Existing frozen authority | None |

The following invariants are frozen `PASS`:

1. Every canonical source identity is Workspace-qualified.
2. Order source identity uses canonical `orderId`.
3. External source identity requires `sourceSystem`.
4. Historical source identity requires `sourceSystem`.
5. Historical import batch is not source identity.
6. Historical import row number is not source identity.
7. BuyerRef is not source identity.
8. `correlationId` is not source identity.
9. `evidenceType` is not part of the current source key.
10. One exact source identity yields at most one canonical original PurchaseEvidence.
11. The same source identity components may coexist in different Workspaces.
12. Multiple source identities may reference the same BuyerRef.
13. Identical replay reuses existing canonical evidence.
14. Changed-payload replay cannot mutate canonical evidence.
15. Changed-payload replay cannot create correction or supersession.
16. Source identity does not replace aggregate identity.
17. `sourceSystem` is a namespace identity component, not a display label.
18. Source owners do not own canonical PurchaseEvidence persistence.

### Deferred authority and readiness

Reversal source type/system/identifier, reversal uniqueness, authorization, multiplicity,
reversal-of-reversal, reason and operational idempotency remain outside this source-key authority.
`EVIDENCE REVERSAL COMPLETE DURABLE MODEL: AUTHORITY_GAP`. `PURCHASE EVIDENCE POLICY VERSION:
AUTHORITY_GAP`. Effective state remains derived from immutable evidence/reversal facts, while
`EFFECTIVE PURCHASE EVIDENCE COMPLETE PREDICATE: AUTHORITY_GAP` because source-specific validity and
complete reversal semantics are not closed. WF-05 Customer Conversion remains `BLOCKED`.

Therefore `PURCHASE EVIDENCE SOURCE NAMESPACE AUTHORITY FREEZE: PASS`,
`PURCHASE EVIDENCE SOURCE TYPE VOCABULARY: PASS`, `PURCHASE EVIDENCE SOURCE IDENTITY: PASS`,
`PURCHASE EVIDENCE SOURCE UNIQUENESS: PASS`, `PURCHASE EVIDENCE RELATIONAL SOURCE-KEY MODEL: PASS`,
`PURCHASE EVIDENCE IDENTITY MODEL: READY`, `PURCHASE EVIDENCE SOURCE-KEY MODEL: READY`,
`COMMERCIALEVIDENCE DURABLE MODEL: AUTHORITY_GAP`,
`COMMERCIALEVIDENCE OWNER-LOCAL PERSISTENCE: NOT YET ADMITTED`, and
`SAFE NEXT IMPLEMENTATION SLICE: NONE`. The exact next authority task is
**Reversal Durable Record and Policy Provenance Authority Freeze**.

## PurchaseEvidence Reversal Durable Record and Policy Provenance Frozen Authority

### Decision provenance and scope

On 2026-08-29, the Reversal Durable Record and Policy Provenance Authority Freeze task created the
decisions in this section as **NEW FROZEN TECHNICAL AUTHORITY**. The authority hierarchy actually
used was: the explicit Level-0 decisions in that task; the current local working-tree version of this
document, including both preceding uncommitted PurchaseEvidence freezes; the exact adopted OpenAPI;
the canonical registries; architecture and tenancy invariants; non-superseded Design Authority;
backend skeleton evidence; and frontend implementation as supporting evidence only. No
higher-precedence source was found that contradicts the technical decisions below. They are not
attributed to OpenAPI, Design Authority, frontend behavior or a current runtime implementation.

This section freezes only the conceptual immutable original/reversal record family, structural
reversal identity and cardinality, minimum durable provenance, `policyVersion` meaning, evidence-level
unreversed-state derivation and conceptual owner-local relational invariants. It does not admit a
producer, reversal operation, authorization rule, public contract, source-truth validation,
transaction/concurrency behavior, audit/outbox/inbox schema, effective-evidence consumer contract,
Customer lifecycle effect or WF-05 execution. It creates no operation, route, command, query, event,
cross-owner interface, persistence model, migration or implementation slice.

### Immutable durable record family

CommercialEvidence durable history has two conceptual immutable variants sharing the same
`(workspaceId, evidenceId)` identity envelope: original PurchaseEvidence and reversal evidence. This
is a structural model, not a requirement to use one physical table or a stored discriminator column.

| Concern | Original PurchaseEvidence | Reversal Evidence | Authority |
|---|---|---|---|
| `workspaceId` | Required | Required | PASS |
| `evidenceId` | Required | Required own ID | PASS |
| `evidenceType` | Exact admitted purchase type | Not admitted | PASS |
| `buyerRef` | Required | Derived through original | PASS |
| Source identity | Required applicable frozen source key | Not copied from original | PASS |
| `reversalOfEvidenceId` | Null | Required | PASS |
| `occurredAt` | Purchase occurrence | Reversal occurrence | PASS |
| `policyVersion` | Required | Required | PASS |
| `correlationId` | Required | Required own provenance | PASS |
| Effective/status | Derived; not stored | Not applicable | PASS |

An original is structurally identified by `reversalOfEvidenceId = null`; a reversal is structurally
identified by `reversalOfEvidenceId != null`. The original `evidenceType` vocabulary remains exactly
`ORDER_COMPLETED`, `EXTERNAL_PURCHASE_CONFIRMED` and `HISTORICAL_PURCHASE_IMPORTED`. No `REVERSAL`
evidence type, reversal `sourceType`, mutable `status = REVERSED`, or mutable `effective` flag is
admitted.

The minimum canonical durable facts for an original are `workspaceId`, `evidenceId`, `evidenceType`,
BuyerRef, the applicable frozen source identity, `occurredAt`, `policyVersion` and `correlationId`.
The minimum canonical durable facts for a reversal are `workspaceId`, `evidenceId`,
`reversalOfEvidenceId`, `occurredAt`, `policyVersion` and `correlationId`. No `amount`, `currency`,
`documentRef`, `note`, `createdBy`, `status`, `effective`, `recordedAt`, `createdAt`, mutable resource
`version`, or `reversalReason` is admitted by this authority.

### Reversal identity, target and cardinality

A reversal is a separate append-only, immutable CommercialEvidence record. It receives its own new
opaque `evidenceId` from the same namespace as originals; it never reuses the original ID. Its
canonical identity is `(workspaceId, evidenceId)`. The owner-local target identity
`(reversal.workspaceId, reversal.reversalOfEvidenceId)` must resolve to exactly one canonical
original `(original.workspaceId, original.evidenceId)` in the same trusted Workspace.

A reversal may not reference unknown evidence, foreign-Workspace evidence, itself, another reversal,
or another owner's record. The target must have `reversalOfEvidenceId = null`; reversal-of-reversal,
undo reversal, restore reversal and effective-state toggling are not admitted. One original has zero
or one canonical reversal. If one already exists, another distinct reversal record must not be
appended. This freezes only the durable result; command response, returned identity, retry and
transport semantics remain deferred.

Both the original and reversal remain physically and semantically intact after append. Neither is
editable, deletable, converted into the other or rewritten by the reversal. A reversal does not own
an independent copied BuyerRef and does not copy the original `sourceType`, `sourceSystem`, `sourceId`
or `evidenceType` as canonical truth. BuyerRef and original purchase-source provenance are resolved
through the CommercialEvidence-owned original record. No foreign-owner lookup or persistence access
is thereby admitted.

The current source-key authority applies only to original `ORDER`, `EXTERNAL_PURCHASE` and
`HISTORICAL_IMPORT` facts. A reversal producer/source namespace, source key and operational
idempotency model remain `AUTHORITY_GAP`; no `REVERSAL`, `REVERSAL_COMMAND` or `REVERSAL_EVENT`
`sourceType` is created.

### Temporal, correlation and policy provenance

`occurredAt` on an original is the time the purchase fact occurred. `occurredAt` on a reversal is the
time the reversal fact occurred and is not copied from the original. System processing,
`recordedAt` and `createdAt` timestamps are not frozen or added here.

Every reversal carries its own immutable `correlationId` provenance/workflow correlation. It need
not equal the original's `correlationId` and is not evidence identity, reversal target identity,
source identity, an idempotency key, permission or Customer identity. Its generation mechanism is
deferred.

`policyVersion` is the immutable identifier of the CommercialEvidence-owned evidence policy under
which a durable evidence fact was admitted/interpreted at append time. It records the
CommercialEvidence policy context associated with that immutable fact. It is not a Customer
lifecycle, Customer conversion, CustomerState aggregate, Order, source-system, API, schema, workflow
or AccessControl policy version.

`policyVersion` is required and immutable on both original and reversal records. An original records
the evidence-policy version applied when the original fact was admitted; a reversal records the
evidence-policy version applied when the reversal fact was admitted. A reversal does not
automatically copy the original version, and the two may differ because they are distinct facts
admitted at different times. Policy changes affect future admitted facts and do not rewrite existing
records absent separate explicit migration authority. Policy registry/storage and actual policy
content, including monetary thresholds, allowed providers or source states, import validation,
BuyerRef validation, authorization and conversion requirements, remain deferred.

| Policy concern | Decision | Authority |
|---|---|---|
| Policy owner | CommercialEvidence | PASS |
| Original `policyVersion` | Required | PASS |
| Reversal `policyVersion` | Required | PASS |
| Immutable | Yes | PASS |
| Copied from original | No requirement | PASS |
| Customer lifecycle version | No | PASS |
| Source-system version | No | PASS |
| API/schema version | No | PASS |
| Actual policy content | Deferred | DEFERRED |

### Derived reversal state and ownership guard

A canonical original with zero canonical reversal is `UNREVERSED`; an original with one canonical
reversal is `REVERSED` at evidence level. The original remains intact. A reversed original cannot
satisfy the unreversed-PurchaseEvidence portion of future effective-evidence eligibility. This
durable unreversed predicate is complete, but the full effective PurchaseEvidence predicate remains
`AUTHORITY_GAP` because producer/source-truth validity, BuyerRef existence validation, policy content
and the consumer boundary are not closed. Effective/reversed state is derived from immutable facts,
not written as mutable state on the original.

Customers owns the Customer aggregate and Customer lifecycle truth. CommercialEvidence owns
PurchaseEvidence truth, reversal evidence truth and derived evidence effectiveness/projection policy;
it acquires no authority to mutate Customer lifecycle state because evidence is reversed. Customer
deletion, archival, deactivation, rollback, relationship removal, onboarding change or other
consequence of reversal remains `AUTHORITY_GAP`.

The frozen source-key model deliberately keeps
`(workspaceId, EXTERNAL_PURCHASE, sourceSystem, sourceId)` and
`(workspaceId, HISTORICAL_IMPORT, sourceSystem, sourceId)` as distinct source identities. Whether
such records can represent the same underlying business purchase is
`CROSS-SOURCE-TYPE PURCHASE-FACT EQUIVALENCE: AUTHORITY_GAP`; this authority neither merges them nor
declares them distinct business purchases.

### Conceptual owner-local relational invariants

The conceptual aggregate primary key remains `(WorkspaceId, EvidenceId)`. A reversal's owner-local
reference `(WorkspaceId, ReversalOfEvidenceId)` targets `(WorkspaceId, EvidenceId)` and the target
must be an original. At most one reversal may target an original, conceptually equivalent to a unique
`(WorkspaceId, ReversalOfEvidenceId)` constraint for reversal records only. A physical implementation
may use separate original/reversal tables, a filtered unique index, discriminator-aware constraints
or another relationally equivalent mechanism; no physical model is frozen.

The reversal-to-original reference is entirely CommercialEvidence-owned. No relational foreign key
to Contacts, Organizations, Orders or Customers is admitted. Trusted Workspace context is mandatory,
caller-supplied Workspace data is never authority, and foreign-Workspace target existence must not be
observable.

### Frozen invariants

The following invariants are frozen `PASS`:

1. Original and reversal records share the `(WorkspaceId, EvidenceId)` identity envelope.
2. A reversal receives a new `evidenceId`.
3. A reversal references exactly one original through `reversalOfEvidenceId`.
4. Original and reversal share the trusted Workspace.
5. A reversal cannot reference itself.
6. A reversal cannot target another reversal.
7. Reversal-of-reversal is not admitted.
8. One original has at most one canonical reversal.
9. A second distinct reversal cannot be appended for the same original.
10. The original remains immutable after reversal.
11. The reversal remains immutable after append.
12. A reversal does not require an independently duplicated BuyerRef.
13. A reversal does not duplicate original purchase source identity as canonical truth.
14. Reversal `occurredAt` is its own fact occurrence time.
15. Every original carries CommercialEvidence `policyVersion`.
16. Every reversal carries CommercialEvidence `policyVersion`.
17. `policyVersion` is immutable CommercialEvidence provenance.
18. `policyVersion` is not Customer lifecycle or conversion version.
19. `correlationId` is provenance, not source identity.
20. No mutable effective/status flag is written onto the original.
21. Zero reversal means unreversed.
22. One canonical reversal means reversed at evidence level.
23. Reversed evidence cannot satisfy an unreversed-evidence requirement.
24. Customer lifecycle consequences are outside CommercialEvidence authority.
25. The reversal-to-original reference is owner-local only.

### Deferred authority and readiness

`COMMERCIAL EVIDENCE PRODUCTION MODEL: AUTHORITY_GAP`, `REVERSAL PRODUCER SOURCE IDENTITY:
AUTHORITY_GAP`, `EVIDENCE REVERSAL OPERATIONAL SEMANTICS: AUTHORITY_GAP`, `EFFECTIVE PURCHASE
EVIDENCE COMPLETE PREDICATE: AUTHORITY_GAP`, `EFFECTIVE EVIDENCE CONSUMER MODEL: AUTHORITY_GAP`,
`CUSTOMER CONSEQUENCES OF EVIDENCE REVERSAL: AUTHORITY_GAP`, and `WF-05 EXECUTABLE ENTRY CONDITION:
AUTHORITY_GAP`. WF-05 Customer Conversion remains `BLOCKED`.

Therefore `PURCHASE EVIDENCE REVERSAL/POLICY AUTHORITY FREEZE: PASS`,
`PURCHASE EVIDENCE IDENTITY MODEL: READY`, `PURCHASE EVIDENCE SOURCE-KEY MODEL: READY`,
`EVIDENCE REVERSAL DURABLE MODEL: READY`, `PURCHASE EVIDENCE POLICY PROVENANCE MODEL: READY`,
`PURCHASE EVIDENCE DURABLE RECORD MODEL: READY`, and `COMMERCIALEVIDENCE DURABLE MODEL: READY`.
Durable-model readiness is not runtime admission: `COMMERCIALEVIDENCE OWNER-LOCAL RUNTIME: NOT YET
ADMITTED` and `SAFE NEXT IMPLEMENTATION SLICE: NONE` because no authoritative producer/append
boundary, reversal operation boundary, local transaction/concurrency semantics, audit/outbox behavior
or effective-evidence consumer contract is frozen. The exact next authority task is
**CommercialEvidence Owner-Boundary Authority Freeze**.

## CommercialEvidence Owner-Boundary Frozen Authority

### Decision provenance and scope

On 2026-08-29, the CommercialEvidence Owner-Boundary Authority Freeze task created the decisions in
this section as **NEW FROZEN TECHNICAL AUTHORITY**. The hierarchy actually used was: the explicit
Level-0 task decisions; the current local working-tree authority including all three preceding
uncommitted PurchaseEvidence freezes; the adopted OpenAPI; canonical operation, command, query,
workflow, capability, owner and cross-owner registries; architecture and trusted-Workspace
invariants; non-superseded Design Authority; backend skeleton evidence; and frontend behavior as
supporting evidence only. No higher-precedence contradiction was found inside the authorized
technical envelope.

This section freezes only the CommercialEvidence-owned internal application boundary, field
ownership, replay/concurrency/transaction behavior, reversal request boundary, successful-append
audit, event/outbox/inbox posture, CommercialEvidence-owned effectiveness predicate and a narrow
effective-evidence read boundary. It does not create a public API, name a C# interface, implement a
producer or Workflow, admit a permission, define foreign source business validity, create an event,
or authorize Customer conversion or lifecycle mutation.

The freeze is `PARTIAL`: Orders and WF-12 establish the `ORDER_COMPLETED` producer mapping, while
authoritative producers for external, historical and reversal facts remain `AUTHORITY_GAP`. The
internal original boundary is independently implementable without wiring those unresolved producers;
the internal reversal boundary is structurally closed but has no admitted caller.

### Canonical owner application boundary and source-truth split

Only CommercialEvidence owns and mutates canonical PurchaseEvidence persistence. Foreign owners may
neither access a CommercialEvidence DbContext/repository nor allocate `evidenceId`, bypass source
uniqueness, append a reversal directly or participate in a cross-DbContext transaction. A multi-owner
flow is coordinated by Workflows through narrow owner application boundaries, and each owner commits
only its own state.

CommercialEvidence owns two narrow internal application boundaries: one accepts authoritative
original source facts and returns an `APPENDED`, `REPLAYED` or `CONFLICT` decision with the canonical
evidence identity; the other accepts a request to reverse a target original. Neither boundary is
public HTTP, a generic repository gateway, frontend mutation, direct persistence exposure or an
admission of a specific C# interface name.

CommercialEvidence validates evidence shape, admitted evidence/source vocabulary, source-key shape,
source uniqueness, canonical replay equality, immutable persistence, policy provenance, reversal
target structure and reversal truth. It does not query Orders, Integrations, Contacts or
Organizations persistence to rediscover whether an Order is completed, an external purchase is
confirmed, a historical purchase is legitimate or a BuyerRef currently exists. Those authoritative
source/business facts must arrive through an upstream owner and Workflow boundary.

CommercialEvidence validates BuyerRef only structurally: type is exactly `CONTACT` or
`ORGANIZATION_ACCOUNT`, and ID satisfies the canonical EntityId envelope. Existence validation is a
cross-owner Workflow/owner responsibility where the consuming workflow requires it. Persisting a
BuyerRef creates no Contacts/Organizations foreign lookup or foreign relational key.

| Evidence/operation | Source truth owner | Coordinator | CommercialEvidence boundary | Foreign DB read by CE? | Authority |
|---|---|---|---|---|---|
| `ORDER_COMPLETED` | Orders | WF-12 Order Closing | Original append | No | PASS |
| `EXTERNAL_PURCHASE_CONFIRMED` | `AUTHORITY_GAP` | `AUTHORITY_GAP`; WF-07 is a blocked baseline only | Original append | No | AUTHORITY_GAP |
| `HISTORICAL_PURCHASE_IMPORTED` | `AUTHORITY_GAP` | `AUTHORITY_GAP`; WF-07 is a blocked baseline only | Original append | No | AUTHORITY_GAP |
| Reversal | `AUTHORITY_GAP` | `AUTHORITY_GAP` | Reversal request | No | AUTHORITY_GAP |

Orders owns canonical Order completion truth. The canonical WF-12 Order Closing sequence coordinates
Orders, Payments, Shipping and CommercialEvidence, completes the Order and appends idempotent
PurchaseEvidence. This establishes the producer mapping, but WF-12 remains not implemented and
`CONTRACT_READY_REQUIRES_RECONCILIATION`; this section does not implement or otherwise admit its
runtime wiring. CommercialEvidence never queries Orders persistence to re-prove completion.

WF-07 names external/historical onboarding evidence but remains `BLOCKED`, and neither its registry
nor another higher authority establishes the exact upstream source-truth owner/integration path.
Therefore external and historical producer models remain `AUTHORITY_GAP`; frontend demo behavior
cannot close them. No authoritative reversal producer or coordinator is registered, so reversal
producer authority also remains open.

### Append field ownership and outcomes

The internal original append boundary receives trusted server-derived Workspace context plus
authoritative `evidenceType`, BuyerRef, applicable `sourceType`, `sourceSystem` where required,
`sourceId`, `occurredAt` and `correlationId`. CommercialEvidence validates/uses that context,
generates `evidenceId`, evaluates the canonical source key, owns replay and durable-append decisions,
and resolves/assigns the current CommercialEvidence `policyVersion`. The caller cannot supply
`evidenceId`, choose whether another row exists, override uniqueness or authoritatively select
`policyVersion`.

| Fact | Caller/Workflow | CommercialEvidence | Authority |
|---|---|---|---|
| Trusted Workspace | Supplies server-derived context | Validates and uses | PASS |
| `evidenceId` | No | Generates | PASS |
| `evidenceType` | Supplies authoritative intent | Validates admitted value | PASS |
| BuyerRef | Supplies | Structural validation only | PASS |
| Source identity | Supplies authoritative source fact | Validates and deduplicates | PASS |
| `occurredAt` | Supplies authoritative fact time | Persists | PASS |
| `correlationId` | Supplies provenance | Persists initial provenance | PASS |
| `policyVersion` | No | Resolves and assigns | PASS |
| Replay decision | No | Owns | PASS |
| Reversal `evidenceId` | No | Generates | PASS |
| Reversal target | Supplies original `evidenceId` | Validates owner-local target | PASS |

An original append has exactly three conceptual results. `APPENDED` means one new canonical original
and its owner-assigned `evidenceId`; `REPLAYED` means an identical exact source identity resolves to
the existing canonical `evidenceId` with no new record; `CONFLICT` means the source identity already
exists but canonical immutable purchase facts differ, so no record is created and existing evidence
is unchanged. No transport status or DTO is frozen.

Canonical replay comparison includes only facts whose difference changes immutable original
PurchaseEvidence truth, including `evidenceType`, BuyerRef, exact source identity and `occurredAt`.
Owner-generated `evidenceId` is excluded. The current policy version is excluded: replay does not
re-admit or rewrite an existing fact, and its stored `policyVersion` remains historical provenance.
Request/workflow `correlationId` is also excluded from purchase-fact equality: a retry with a new
correlation remains `REPLAYED` and does not mutate the original correlation provenance.

Within this authorized replay-comparison envelope, the preceding source-key section's statement that
a different later correlation would invoke changed-payload conflict is narrowly superseded. Its
source-key exclusion and immutable initial correlation provenance remain authoritative. No other
part of the previous section is changed.

### Concurrency, local transaction and cross-owner convergence

For concurrent attempts with the same exact original source identity, CommercialEvidence persistence
uniqueness is the final race authority; no check-before-write alone is sufficient. At most one
original becomes canonical. A loser reloads the canonical evidence and returns `REPLAYED` when the
canonical immutable payload matches or `CONFLICT` when it differs. A generated aggregate-ID
collision is separate: CommercialEvidence internally reallocates an opaque ID and never returns an
unrelated row. A source-key race resolves the source identity and performs replay comparison.

For every newly appended original or reversal, the canonical evidence and required
CommercialEvidence-owned successful append audit commit atomically in one CommercialEvidence-local
transaction. No foreign owner persistence participates. Replay never rewrites existing evidence.

There is no distributed or cross-DbContext transaction between Orders, Integrations, Customers or
another owner and CommercialEvidence. If an upstream owner commits and the evidence step is
incomplete, Workflow retries/converges that same CommercialEvidence step using the same source
identity. If evidence committed but the response was lost, retry resolves `REPLAYED` with the
existing ID. Retry must not insert CommercialEvidence state directly, roll back a foreign owner,
manufacture a new source identity or broaden transaction scope. Timing and retry counts remain
implementation details.

| Concern | Transaction owner | Atomic? | Cross-owner transaction? |
|---|---|---|---|
| Original evidence + success audit | CommercialEvidence | Yes | No |
| Reversal + success audit | CommercialEvidence | Yes | No |
| Orders + evidence | Separate owners / Workflow coordination | No distributed transaction | Prohibited |
| External source + evidence | Separate owners / Workflow coordination | No distributed transaction | Prohibited |
| Historical import + evidence | Separate owners / Workflow coordination | No distributed transaction | Prohibited |

### Reversal request boundary

The internal reversal boundary identifies its target by trusted Workspace plus original
`evidenceId`. A caller may supply target original ID, reversal `occurredAt` and `correlationId` only.
CommercialEvidence owns original lookup, same-Workspace and original-only validation, the 0..1
reversal invariant, reversal `evidenceId`, current `policyVersion` and append decision. The caller may
not provide reversal ID, copied BuyerRef/source identity, policy version, mutable status or reversal
reason.

The durable owner-boundary idempotency identity is `(workspaceId, originalEvidenceId)`. If no reversal
exists, one may be appended. If a canonical reversal exists, it is resolved and no second record is
created. Different retry correlation or payload does not mutate its immutable `occurredAt`,
`policyVersion` or `correlationId`; the existing reversal wins. Whether a caller is told "already
reversed" or "replayed" is deferred transport semantics.

Concurrent reversal attempts are resolved by the owner-local unique target constraint. Exactly one
canonical reversal may exist; losing attempts resolve it without appending or mutating another
record. These idempotency/concurrency semantics are frozen, but no authoritative reversal producer,
permission or cross-owner caller is admitted. Consequently the reversal application boundary is
closed while the reversal owner-local runtime core remains `AUTHORITY_GAP`.

### Successful append audit and event delivery posture

CommercialEvidence owns immutable successful mutation audit for newly appended canonical evidence.
At minimum each successful audit is attributable to Workspace, canonical `evidenceId`, operation kind
`ORIGINAL_APPEND` or `REVERSAL_APPEND`, correlation ID, system decision/audit time and resulting
evidence `policyVersion`. It is CommercialEvidence technical evidence, not Customer audit truth. The
new evidence and successful audit are atomic: neither may commit without the other. Physical audit
identity/table design is deferred; durable replay/conflict audit is not required by this freeze and
operational telemetry remains deferred.

No current OpenAPI operation, registry entry, cross-owner contract or non-superseded design source
admits a CommercialEvidence outbound event. Therefore no domain/integration event or outbox payload
is created for the first owner-local core. If a future event is explicitly admitted, CommercialEvidence
must stage its outbox record atomically with owner-local state. The synchronous internal application
boundary requires no inbox; an inbox must not be invented merely to avoid an application call.

### CommercialEvidence effectiveness and Workflow read boundary

A canonical original PurchaseEvidence is CommercialEvidence-effective when it exists as an admitted
canonical original under its stored CommercialEvidence policy version and has no canonical reversal:
`admitted canonical original + no canonical reversal`. CommercialEvidence does not continuously
query or poll source owners after append. A later source change affects evidence only through an
admitted reversal mechanism. This predicate proves CommercialEvidence evidence effectiveness, not
BuyerRef existence, Customer existence/non-existence, conversion eligibility or Customer lifecycle.

CommercialEvidence exposes a narrow internal application-level read boundary for Workflows, resolved
by trusted Workspace plus `evidenceId`. It never exposes a DbContext, repository or persistence
entity. Missing evidence, a reversal record or a reversed original yields no effective-evidence
snapshot. An effective snapshot contains only `workspaceId`, `evidenceId`, `evidenceType`, BuyerRef,
`occurredAt` and `policyVersion`. Source identity is not required for WF-05 unless later workflow
authority proves otherwise.

The snapshot proves only canonical CommercialEvidence identity, semantic evidence type, recorded
BuyerRef, occurrence time, policy provenance and current owner-local effectiveness. It does not prove
Contact/Organization existence, Customer existence or absence, Customer conversion eligibility or
Customer lifecycle. No generic `IEntityReader`, `ICrossModuleReader`, `IRepositoryGateway` or foreign
persistence reader is authorized.

CommercialEvidence must not merge distinct external and historical source identities merely because
their namespace/identifier text matches. Whether they represent the same business purchase remains
`CROSS-SOURCE-TYPE PURCHASE-FACT EQUIVALENCE: AUTHORITY_GAP`.

| Scenario | Canonical outcome | New evidence? | Existing mutation? |
|---|---|---:|---:|
| New original source | `APPENDED` | Yes | No |
| Identical original replay | `REPLAYED` | No | No |
| Changed-payload original replay | `CONFLICT` | No | No |
| `evidenceId` generation collision | Internal re-allocation | Eventually one | No |
| Concurrent identical source | One `APPENDED` plus replay resolution | One total | No |
| First valid reversal | `REVERSAL APPENDED` | Yes | Original unchanged |
| Duplicate reversal target | Existing reversal wins | No | No |
| Concurrent reversal | One canonical reversal | One total | No |

### Frozen invariants

The following invariants are frozen `PASS` except I29, which remains `AUTHORITY_GAP`:

1. Only CommercialEvidence allocates canonical `evidenceId`.
2. Only CommercialEvidence mutates canonical PurchaseEvidence state.
3. Foreign owners never access CommercialEvidence persistence directly.
4. Cross-owner mutations are coordinated by Workflow.
5. CommercialEvidence does not query Orders persistence to re-prove Order truth.
6. CommercialEvidence does not query Contacts/Organizations merely to persist BuyerRef.
7. Source-key uniqueness is enforced inside CommercialEvidence.
8. Identical replay returns/resolves existing evidence.
9. Changed-payload replay fails closed.
10. `correlationId` difference alone does not create another purchase fact.
11. Current `policyVersion` difference alone does not invalidate identical replay.
12. Concurrent source append creates at most one canonical original.
13. Source uniqueness races resolve after persistence uniqueness enforcement.
14. Generated `evidenceId` collision is distinct from source replay.
15. Owner-local append does not participate in a foreign DbContext transaction.
16. Workflow retries owner steps rather than using a distributed transaction.
17. New evidence and required successful append audit are atomic owner-local state.
18. No speculative outbound event/outbox payload is invented without an admitted event.
19. The reversal application boundary is CommercialEvidence-owned.
20. Reversal retry identity is the target original inside the Workspace.
21. Concurrent reversal creates at most one canonical reversal.
22. Existing reversal remains immutable on duplicate request.
23. CommercialEvidence-effective evidence is an admitted canonical original with no reversal.
24. CommercialEvidence does not continuously revalidate source-owner state.
25. Effective-evidence consumers read through a narrow owner application boundary.
26. The consumer boundary does not expose CommercialEvidence persistence.
27. Its result does not prove Customer conversion eligibility.
28. Customer lifecycle remains Customers-owned.
29. Cross-source-type purchase equivalence remains unresolved (`AUTHORITY_GAP`).

### Deferred authority and readiness

Exact external-purchase, historical-import and reversal source-truth owners/coordinators remain
`AUTHORITY_GAP`, as do reversal permission/caller admission, cross-source-type business equivalence,
Customer consequences of evidence reversal and WF-05 executable entry semantics. WF-05 remains
`BLOCKED`. Replay/conflict durable audit is `DEFERRED`. There is no currently admitted
CommercialEvidence outbound event, outbox requirement or inbox requirement for the owner-local core.

Therefore `COMMERCIALEVIDENCE OWNER-BOUNDARY AUTHORITY FREEZE: PARTIAL`, `COMMERCIALEVIDENCE OWNER
BOUNDARY: PASS`, `ORDER_COMPLETED PRODUCER MODEL: PASS`, `EXTERNAL PURCHASE PRODUCER MODEL:
AUTHORITY_GAP`, `HISTORICAL PURCHASE PRODUCER MODEL: AUTHORITY_GAP`, `REVERSAL AUTHORITATIVE
PRODUCER: AUTHORITY_GAP`, `COMMERCIALEVIDENCE EFFECTIVE-EVIDENCE PREDICATE: PASS`, and
`EFFECTIVE-EVIDENCE INTERNAL READ BOUNDARY: PASS`.

The five durable-model gates remain `READY`. `ORIGINAL EVIDENCE OWNER-LOCAL CORE: READY` because the
Order/WF-12 producer mapping, append ownership, replay, concurrency, policy assignment, local
transaction, audit and no-event posture are closed. `REVERSAL OWNER-LOCAL CORE: AUTHORITY_GAP`
because no authoritative producer/caller is admitted. Consequently `COMMERCIALEVIDENCE OWNER-LOCAL
RUNTIME: PARTIALLY ADMITTED`.

The exact safe implementation slice is **CommercialEvidence Owner-Local Original Evidence Core**,
limited to owner-local original persistence, source uniqueness, owner-generated ID, the internal
append boundary, `APPENDED`/`REPLAYED`/`CONFLICT`, concurrency-safe replay, atomic successful-append
audit and effective-evidence-by-ID internal read. The only admitted producer-facing original path in
that slice is `ORDER_COMPLETED`; external and historical append paths must remain unreachable until
their producer authority is frozen. The slice excludes public HTTP, Workflow wiring, reversal
runtime, external/historical ingestion, Customer mutation, WF-05, outbound events and speculative
outbox/inbox.

Because unresolved CommercialEvidence producer/reversal gaps must not be skipped, the exact next
authority task is **CommercialEvidence External/Historical/Reversal Producer Authority Freeze**.
WF-05 Customer Conversion Authority Freeze may follow the original-core implementation and closure
of the remaining producer/reversal authority required by that workflow.

## CommercialEvidence Owner-Local Original Evidence Core Implementation Authority

On 2026-08-29, backend baseline `39df722941ae39e8f9ede6f4f969e4232ee9f367` implemented and locally
verified the authority-admitted **CommercialEvidence Owner-Local Original Evidence Core**. This is
implementation evidence under the four preceding PurchaseEvidence/CommercialEvidence frozen
authority sections; it neither rewrites those decisions nor creates new producer, reversal,
Workflow, Customer or HTTP authority.

CommercialEvidence now owns `CommercialEvidenceDbContext`, the `commercial_evidence` schema and
migration `20260829164625_CommercialEvidenceOriginalCore`. The immutable original record is stored in
`commercial_evidence.PurchaseEvidence` under composite primary key `(WorkspaceId, EvidenceId)`; no
global EvidenceId unique index exists. The named unique source index is exactly
`(WorkspaceId, SourceType, SourceSystem, SourceId)`, has no nullable-column filter, and uses
`Latin1_General_100_BIN2` column equality so the SQL race authority and ordinal application equality
agree for the admitted Order path. Check constraints fail closed for the exact EvidenceType,
SourceType and BuyerRefType vocabularies and enforce each frozen source-to-evidence mapping, including
`ORDER` requiring `ORDER_COMPLETED` with null SourceSystem. There is no foreign-owner foreign key,
DbContext or persistence access.

The only callable original append contract is the CommercialEvidence-specific internal application
boundary for `ORDER_COMPLETED`. It receives the existing trusted Workspace carrier plus Orders-owned
`orderId`, BuyerRef, `occurredAt` and correlation provenance; the caller cannot supply EvidenceId,
policyVersion, SourceType or EvidenceType. CommercialEvidence generates the opaque ID, assigns the
module-owned policy token and returns typed `APPENDED`, `REPLAYED` or `CONFLICT` results. Replay
compares the frozen immutable source facts using ordinal equality after exact UTC timestamp
canonicalization; retry correlation and the provider's current policy token do not participate.
Identical and correlation-only replay preserve the original row, EvidenceId, correlation and policy;
a newer current policy still replays the historical record; changed BuyerRef or occurrence time
conflicts without mutation.

Database uniqueness is the final concurrent source authority. A named source-index race reloads the
winner and resolves to `REPLAYED` or `CONFLICT`; a named aggregate-primary-key collision is not replay
and causes bounded internal opaque-ID reallocation; unrelated persistence failures fail closed. A new
original and exactly one immutable `ORIGINAL_APPEND` CommercialEvidence audit record are staged in one
DbContext and one SaveChanges transaction. The audit contains Workspace, canonical EvidenceId,
original correlation, system decision time and resulting policyVersion. Replay/conflict durable audit,
outbox, inbox and outbound event records remain unimplemented as frozen. Failure injection against the
audit insert proved that neither an evidence-only nor audit-only commit survives.

The narrow `IEffectivePurchaseEvidenceReader` reads by trusted Workspace plus EvidenceId and returns
only WorkspaceId, EvidenceId, EvidenceType, BuyerRef, OccurredAt and PolicyVersion. Foreign Workspace
and unknown evidence both resolve to no snapshot. For this original-only slice every canonical
original has no admitted reversal and is therefore effective; no mutable status/effective/reversed
column was introduced. The implementation exports no generic reader or persistence surface.

Executable verification used the real DI, EF Core model and SQL Server persistence against repeated
fresh LocalDB databases. `scripts/verify-commercial-evidence-original-core.ps1` reported
`passed=91 failed=0`, including schema/key/index metadata, closed-value rejection, exact case-sensitive
source equality, server-owned identity/policy fields, UTC round trip, identical/correlation/policy
replay, changed-payload conflict, identical and changed-payload concurrency races, aggregate-ID
collision reallocation, cross-Workspace source and EvidenceId coexistence, successful audit evidence,
audit rollback and Workspace-scoped effective reads. The migration applied repeatedly and
`dotnet ef migrations has-pending-model-changes` reported no model changes. `dotnet build
UnicoreCRM.slnx --no-restore` completed with zero warnings and zero errors. Unchanged regression
verifiers reported AccessControl `404/0`, Contacts `67/0`, Organizations `71/0` and Customers `117/0`.
These are local executable results, not GitHub CI evidence or independent release attestation.

No CommercialEvidence HTTP route, Workflow/WF-12 wiring, external/historical append path, reversal
runtime, Customer mutation, WF-05 execution, event, outbox or inbox was added. Therefore the original
owner-local core is `IMPLEMENTED_AND_VERIFIED`, while `COMMERCIALEVIDENCE OWNER-LOCAL RUNTIME` remains
`PARTIALLY_IMPLEMENTED`; external purchase producer, historical import producer and authoritative
reversal producer remain `AUTHORITY_GAP`; reversal runtime and WF-12 wiring remain not implemented;
WF-05 remains blocked. The exact next authority task remains **CommercialEvidence
External/Historical/Reversal Producer Authority Freeze** and no next implementation slice is admitted.

## CommercialEvidence External and Historical Producer Frozen Authority

### Decision provenance and scope

On 2026-08-30, the CommercialEvidence External/Historical Producer Authority Freeze task evaluated
only the producer/source-truth boundaries for `EXTERNAL_PURCHASE_CONFIRMED` and
`HISTORICAL_PURCHASE_IMPORTED`. The hierarchy actually used was: the task's explicit Level-0
decision envelope; the current working-tree version of this document, including all preceding
PurchaseEvidence and CommercialEvidence freezes and original-core implementation evidence; the
adopted OpenAPI; the canonical operation, command, query, workflow, capability, owner-context and
cross-owner-contract registries; architecture, ownership, trusted-Workspace and Workflow invariants;
non-superseded Design Authority; current backend implementation as evidence only; and frontend
behavior as supporting evidence only.

This is an **authority decision and freeze**, not runtime admission. Statements below are marked as
**EXISTING AUTHORITY**, **NEW FROZEN TECHNICAL AUTHORITY**, or **AUTHORITY_GAP**. No conflict with a
higher-precedence source was found. The result is `PARTIAL`: field ownership, attested-fact,
validation, convergence, transaction and namespace-isolation boundaries are closed, but no exact
source-truth owner, executable coordinator, namespace-registration owner, BuyerRef existence
validator or transport is admitted for either producer kind.

The existing CommercialEvidence Owner-Local Original Evidence Core remains
`IMPLEMENTED_AND_VERIFIED` with its recorded `91/0` owner verifier, zero-warning/zero-error build and
AccessControl `404/0`, Contacts `67/0`, Organizations `71/0` and Customers `117/0` local regression
evidence. This task neither reran those suites nor changes or reinterprets that runtime evidence.

### Common attested-fact and owner boundary

The following is **NEW FROZEN TECHNICAL AUTHORITY**, consistent with existing ownership laws:
CommercialEvidence receives an authoritatively attested purchase fact; it does not discover or prove
foreign business truth. The admitted upstream producer/coordinator must establish that the
external/historical fact is authorized for submission. CommercialEvidence then owns only admitted
shape validation, exact source-key validation, source uniqueness, replay comparison, owner-generated
`evidenceId`, owner-assigned `policyVersion`, immutable persistence and successful-append audit.

CommercialEvidence must not read an Integrations, Customers, Contacts, Organizations, Orders or
historical-import DbContext/repository/table to rediscover source truth or BuyerRef existence. It
consumes only a future admitted application-boundary fact. Direct foreign writes to CommercialEvidence
persistence and cross-owner/cross-DbContext distributed transactions remain prohibited. A
coordinator retries owner-local steps using the same canonical source identity; it never invents a
new `sourceId`, changes `sourceSystem`, allocates `evidenceId` or persists CommercialEvidence state
directly.

Therefore `COMMERCIALEVIDENCE ATTESTED-FACT BOUNDARY: PASS`, `EXTERNAL/HISTORICAL FOREIGN DB READ BY
CE: PROHIBITED`, `EXTERNAL/HISTORICAL FIELD OWNERSHIP: PASS`, `EXTERNAL/HISTORICAL CE CONVERGENCE:
PASS`, and `EXTERNAL/HISTORICAL CROSS-OWNER TRANSACTION: PROHIBITED`.

### External purchase authority

The exact external source-truth owner is **AUTHORITY_GAP**. Integrations owns provider verification,
bindings and ingress orchestration in general, but the owner-context and cross-owner-contract maps
admit no external-purchase truth or CommercialEvidence producer contract. Integration-connection
mutation operations remain blocked. Integrations must not be promoted from transport/configuration
owner to purchase-truth owner by inference; Customers and CommercialEvidence also do not own this
foreign fact.

WF-07 Customer Onboarding conceptually names external/historical evidence, but its registry status is
`BLOCKED`, backend admission is `BLOCKED`, implementation is `NOT_IMPLEMENTED`, its transaction
boundary is `UNRESOLVED_BLOCKED`, and both mapped onboarding operations are `NOT_ADMITTED`. The
command registry contains no corresponding producer command and the cross-owner map contains no
CommercialEvidence producer contract. Consequently the exact external coordinator/application
caller and ingestion transport are **AUTHORITY_GAP**; the blocked WF-07 baseline is not executable
producer authority and no event/inbox contract is inferred.

The external validation split is **NEW FROZEN TECHNICAL AUTHORITY** and `PASS`: a future admitted
upstream owner/coordinator owns source authenticity, provider validity, business meaning of
"confirmed", provider-specific security checks and mapping of the provider-native purchase-fact ID.
CommercialEvidence validates only admitted `sourceType`, required `sourceSystem` and `sourceId`
shape, BuyerRef structure, `occurredAt` structure, source uniqueness, replay and persistence. This
freeze does not define what confirmation means, which provider is trusted or any callback/security
protocol.

The `EXTERNAL_PURCHASE` source key and namespace semantics remain **EXISTING AUTHORITY**:
`(workspaceId, EXTERNAL_PURCHASE, sourceSystem, sourceId)`, with `sourceSystem` required. The namespace
is part of CommercialEvidence source-identity semantics, but issuance/registration must belong to a
future authoritative upstream configuration/integration boundary. No such exact owner is currently
admitted, so `EXTERNAL SOURCE SYSTEM REGISTRATION OWNER: AUTHORITY_GAP`; arbitrary display strings do
not become canonical namespaces merely by being submitted.

The authoritative upstream producer supplies immutable `sourceId` as the purchase-fact identifier
inside `sourceSystem`. CommercialEvidence persists and enforces it and does not replace it with a
request, correlation, webhook-delivery or other transport ID absent later explicit authority. This is
`EXTERNAL SOURCE ID PROVENANCE: PASS` as **NEW FROZEN TECHNICAL AUTHORITY** within the existing source
key.

CommercialEvidence validates BuyerRef structure only. Neither WF-07 nor another admitted contract
assigns responsibility for proving the Contact/Organization relationship exists before external
append, so `EXTERNAL BUYERREF EXISTENCE VALIDATION: AUTHORITY_GAP`. The combined source-truth,
coordinator, namespace-registration, BuyerRef-existence and transport gaps make `EXTERNAL PURCHASE
PRODUCER MODEL: AUTHORITY_GAP` and `EXTERNAL PURCHASE OWNER-LOCAL PRODUCER PATH: AUTHORITY_GAP`.

| Concern | Decision | Authority source | Readiness |
|---|---|---|---|
| evidenceType | `EXTERNAL_PURCHASE_CONFIRMED` | EXISTING AUTHORITY: frozen source namespace/model | PASS |
| sourceType | `EXTERNAL_PURCHASE` | EXISTING AUTHORITY: frozen source namespace/model | PASS |
| source-truth owner | `AUTHORITY_GAP` | No registry/cross-owner contract assigns it | GAP |
| coordinator | `AUTHORITY_GAP`; blocked WF-07 is not executable authority | Workflow/operation registries | GAP |
| sourceSystem required | YES | EXISTING AUTHORITY | PASS |
| sourceSystem registration owner | `AUTHORITY_GAP` | No admitted provider-namespace registration contract | GAP |
| sourceId provenance | Authoritative upstream purchase-fact ID | NEW FROZEN TECHNICAL AUTHORITY | PASS |
| BuyerRef structure | CommercialEvidence validates | EXISTING AUTHORITY | PASS |
| BuyerRef existence | `AUTHORITY_GAP` | No admitted Workflow/owner contract | GAP |
| replay owner | CommercialEvidence | EXISTING AUTHORITY | PASS |
| persistence owner | CommercialEvidence | EXISTING AUTHORITY | PASS |
| transport | `AUTHORITY_GAP` | No admitted application/event producer contract | GAP |
| producer model | `AUTHORITY_GAP` | Derived readiness gate | GAP |

### Historical purchase authority

The exact historical source-truth owner and the owner authorized to admit a historical purchase for
import are both **AUTHORITY_GAP**. No migration/import authority or cross-owner contract defines CSV
trust, mapping/approval policy, duplicate-batch handling or legacy-source correctness. A file,
frontend importer, batch/job or CommercialEvidence itself is not source-truth authority.

The historical import admission boundary is **NEW FROZEN TECHNICAL AUTHORITY** and `PASS` as a
separation rule: a historical fact may reach CommercialEvidence only after a future authoritative
admission boundary has accepted it. CommercialEvidence does not perform file/import trust,
provider/legacy-source verification or admission policy. The exact admission owner remains
`AUTHORITY_GAP`.

WF-07 is also only a blocked conceptual baseline for historical onboarding, so the historical
coordinator/application caller and ingestion transport remain **AUTHORITY_GAP**. No synchronous
application call, event or inbox transport is admitted merely from that blocked workflow.

The `HISTORICAL_IMPORT` source key and namespace semantics remain **EXISTING AUTHORITY**:
`(workspaceId, HISTORICAL_IMPORT, sourceSystem, sourceId)`, with `sourceSystem` required and denoting
the original historical source namespace. It is not a batch, filename, tenant, user, row, import job
or frontend session. No exact owner currently registers/validates those namespaces, so `HISTORICAL
SOURCE SYSTEM REGISTRATION OWNER: AUTHORITY_GAP`.

The future authoritative historical producer supplies immutable `sourceId` as the original purchase
fact's identifier within that original namespace. It is not a batch ID, row number, upload ID,
execution ID or correlation ID. CommercialEvidence owns uniqueness/replay only after receipt. This is
`HISTORICAL SOURCE ID PROVENANCE: PASS` as **NEW FROZEN TECHNICAL AUTHORITY**.

Historical BuyerRef existence responsibility remains **AUTHORITY_GAP**; CommercialEvidence performs
only structural validation and does not query Contact/Organization persistence. Same
`(workspaceId, HISTORICAL_IMPORT, sourceSystem, sourceId)` is the same source fact regardless of
batch, upload, execution or correlation: matching canonical payload returns `REPLAYED`, differing
canonical payload returns `CONFLICT`, and existing evidence remains immutable. This is
`HISTORICAL REIMPORT CONVERGENCE: PASS`; import-batch idempotency itself is not defined.

The combined source-truth, admission-owner, coordinator, namespace-registration, BuyerRef-existence
and transport gaps make `HISTORICAL PURCHASE PRODUCER MODEL: AUTHORITY_GAP` and `HISTORICAL PURCHASE
OWNER-LOCAL PRODUCER PATH: AUTHORITY_GAP`.

| Concern | Decision | Authority source | Readiness |
|---|---|---|---|
| evidenceType | `HISTORICAL_PURCHASE_IMPORTED` | EXISTING AUTHORITY: frozen source namespace/model | PASS |
| sourceType | `HISTORICAL_IMPORT` | EXISTING AUTHORITY: frozen source namespace/model | PASS |
| source-truth owner | `AUTHORITY_GAP` | No registry/cross-owner contract assigns it | GAP |
| import admission owner | `AUTHORITY_GAP` | No admitted import/admission boundary | GAP |
| coordinator | `AUTHORITY_GAP`; blocked WF-07 is not executable authority | Workflow/operation registries | GAP |
| sourceSystem required | YES | EXISTING AUTHORITY | PASS |
| sourceSystem registration owner | `AUTHORITY_GAP` | No admitted historical namespace registry | GAP |
| sourceId provenance | Original historical purchase-fact ID | NEW FROZEN TECHNICAL AUTHORITY | PASS |
| batch/row identity | Excluded | EXISTING AUTHORITY plus frozen clarification | PASS |
| BuyerRef structure | CommercialEvidence validates | EXISTING AUTHORITY | PASS |
| BuyerRef existence | `AUTHORITY_GAP` | No admitted Workflow/owner contract | GAP |
| replay owner | CommercialEvidence | EXISTING AUTHORITY | PASS |
| re-import convergence | Matching payload replays; changed payload conflicts | EXISTING AUTHORITY | PASS |
| transport | `AUTHORITY_GAP` | No admitted application/event producer contract | GAP |
| producer model | `AUTHORITY_GAP` | Derived readiness gate | GAP |

### Responsibility, transport and cross-source-type decisions

| Responsibility | External | Historical | CommercialEvidence |
|---|---|---|---|
| source fact authenticity | Upstream owner `AUTHORITY_GAP` | Upstream owner `AUTHORITY_GAP` | NO |
| provider/import validation | Upstream boundary `AUTHORITY_GAP` | Upstream boundary `AUTHORITY_GAP` | NO |
| sourceSystem semantics | Frozen namespace semantics | Frozen namespace semantics | Validates shape/identity |
| sourceSystem registration | Upstream owner `AUTHORITY_GAP` | Upstream owner `AUTHORITY_GAP` | NO |
| sourceId assignment | Future authoritative upstream | Future authoritative upstream | NO |
| BuyerRef structural validation | Supplies | Supplies | YES |
| BuyerRef existence | Workflow/owner `AUTHORITY_GAP` | Workflow/owner `AUTHORITY_GAP` | NO |
| evidenceId | NO | NO | YES |
| policyVersion | NO | NO | YES |
| source uniqueness | NO | NO | YES |
| replay decision | NO | NO | YES |
| persistence | NO | NO | YES |

No generic `AppendAnyPurchaseEvidence` application contract, generic original-evidence producer API,
public CommercialEvidence producer route, new capability, outbound event, inbox or outbox is admitted.
If a producer is later admitted, its semantic boundary must fix its own evidence/source types and may
allow only authoritative BuyerRef, `sourceSystem`, `sourceId`, `occurredAt` and correlation
provenance; the caller never chooses `evidenceId`, `policyVersion` or replay outcome. Exact C# names
remain implementation details. The current core has `EXTERNAL/HISTORICAL CE OUTBOX REQUIREMENT: NONE`.

`EXTERNAL_PURCHASE` and `HISTORICAL_IMPORT` remain distinct source-identity classes. Equal
`sourceSystem`/`sourceId` text across them does not collide because SourceType participates in the
source key. Auto-merge is prohibited and whether such records represent one underlying business
purchase remains **AUTHORITY_GAP**. That gap does **not** block a future owner-local producer append
path: existing authority intentionally treats the source identities as distinct, permits multiple
distinct evidence records for one BuyerRef, and no admitted producer invariant requires cross-type
business deduplication. It does block any later consumer policy that assumes business-level
equivalence or single counting until that policy is expressly frozen. Therefore `SOURCE-TYPE
NAMESPACE ISOLATION: PASS`, `CROSS-SOURCE-TYPE AUTO-MERGE: PROHIBITED`, `CROSS-SOURCE-TYPE BUSINESS
EQUIVALENCE: AUTHORITY_GAP`, and `CROSS-SOURCE-TYPE PRODUCER BLOCKER: NO`.

### Frozen invariants and readiness

The following invariants are frozen/evaluated `PASS`:

1. External/historical producers do not allocate `evidenceId`.
2. External/historical producers do not choose `policyVersion`.
3. CommercialEvidence does not prove external-provider business truth.
4. CommercialEvidence does not prove historical-import business truth.
5. CommercialEvidence does not query foreign persistence for BuyerRef existence.
6. External `sourceSystem` is required.
7. Historical `sourceSystem` is required.
8. External `sourceId` identifies the source-system purchase fact.
9. Historical `sourceId` identifies the original historical purchase fact.
10. Historical import batch is not source identity.
11. Historical row number is not source identity.
12. Retry keeps the same source identity.
13. Re-import keeps the same source identity.
14. CommercialEvidence owns replay and source uniqueness.
15. No distributed transaction is admitted.
16. No direct foreign persistence write to CommercialEvidence is admitted.
17. No generic CommercialEvidence producer operation is admitted.
18. No public CommercialEvidence producer route is admitted.
19. No speculative inbox/event is created.
20. Cross-source-type source identities remain distinct.
21. Cross-source-type business equivalence is not invented.
22. Reversal authority remains outside this task.
23. Customer conversion remains outside this task.

The frozen caller-field and convergence rules are independently useful, but neither producer reaches
its all-or-nothing readiness gate. Therefore `COMMERCIALEVIDENCE EXTERNAL/HISTORICAL PRODUCER
AUTHORITY FREEZE: PARTIAL`, `EXTERNAL PURCHASE PRODUCER MODEL: AUTHORITY_GAP`, `HISTORICAL PURCHASE
PRODUCER MODEL: AUTHORITY_GAP`, and both owner-local producer paths remain `AUTHORITY_GAP`.

Reversal producer/actor/authorization/source and operation semantics remain `AUTHORITY_GAP`; reversal
runtime remains not implemented. Customer consequences remain unresolved and WF-05 remains
`BLOCKED`. No runtime implementation slice is admitted: `SAFE NEXT IMPLEMENTATION TASK: NONE`.

The exact next authority task is **External/Historical Purchase Source-Truth and Import-Admission
Owner Authority Decision**. It must designate, without implementation, the authoritative external
purchase source-truth owner and historical source-truth/import-admission owner before coordinator,
namespace-registration, BuyerRef-existence and transport authority can be closed. This task does not
automatically advance to WF-05 or reversal authority.

## B07 Inbound Lead Webhook implementation authority

B07 introduced one backend-local `PROJECT_EXTENSION_INBOUND_LEAD_WEBHOOK` contract because the adopted frontend OpenAPI declares no inbound Lead webhook. The exact contract is frozen in `INBOUND_LEAD_WEBHOOK_EXTENSION.md`; it is not historical OpenAPI or Design Authority behavior. The six existing integration-configuration operations remain unchanged: the two read contracts are deferred from B07 Core and the four mutation contracts remain blocked.

The extension admits only `POST /integrations/inbound/leads/{integrationId}` for the neutral `generic-signed-json` provider. It verifies HMAC-SHA256 over the timestamp, delivery identifier, and exact raw JSON bytes; enforces a five-minute UTC replay window and a 65,536-byte body limit; resolves secrets only through opaque external configuration references; and accepts no caller-supplied Workspace, member, permission, owner, or Lead identity authority.

Integrations owns `IntegrationsDbContext` and `integration.InboundBindings`. A server-owned `IntegrationId` binds the provider to one Workspace, one delegated member, one secret reference, and an enabled state. Workspace resolves that pair to an active membership, and the Leads-owned dedicated delegated-create authorizer performs server-side `leads.create` evaluation for the resolved membership through AccessControl. Its API accepts no arbitrary capability, and its issued proof is bound to the exact trusted Workspace, account, member and membership. The current model is a Delegated Integration Principal, not a first-class `ServicePrincipal`: the actual actor remains the Integration and authorization is delegated through the active member. Lead audit evidence records generic execution provenance with `ActorType = Integration`, `ActorId = IntegrationId`, `DelegatedSubjectId = delegated member`, and `SourceReference = delivery ID`. No JWT impersonation or request-scoped human identity is fabricated.

PlatformOperations owns `InboxDbContext` and `ops.InboxMessages`. Durable uniqueness is `(IntegrationId, DeliveryId)` and the Inbox retains a SHA-256 raw-payload hash plus original binding authority evidence. Identical retries replay/resume safely; a changed payload or changed binding authority under the same delivery identity fails closed. Raw payloads and credentials are not persisted.

Leads owns the public internal `IInboundLeadIngress` application boundary. It is a provider-neutral trusted inbound creation boundary with distinct delegated authorization and execution-provenance semantics, not a webhook DTO wrapper. It reuses the canonical B05 create validation, active-owner validation, `leads.create` authorization, server-assigned Lead identity, owner-local idempotency, audit, persistence, and outbox behavior. The deterministic internal idempotency key derives from the semantic inbound Lead webhook namespace plus Integration and delivery identity and never becomes a Lead ID. Inbox and Leads deliberately use separate owner-local transactions; replay after a Lead commit and before Inbox completion converges through Leads idempotency without a duplicate Lead.

Runtime verification on 2026-08-23 used the isolated `UnicoreCRM_B07_Verification_20260823220444` LocalDB database and proved valid signed ingestion, missing/invalid/tampered signature rejection, stale timestamp rejection, same-delivery replay, changed-payload conflict, Workspace spoof rejection, AccessControl denial, invalid delegated membership, disabled/unknown binding rejection, server-assigned Lead identity, controlled post-Lead-commit Inbox recovery, and concurrent duplicate delivery with one Inbox authority and one Lead. B01-B06 compact regressions passed, all three B07-related models reported no pending changes, and ApiHost startup completed. Therefore `B07 INBOUND LEAD WEBHOOK CORE: PASS`.

B07 introduced no provider-specific behavior, public integration administration, outbound webhooks, event bus, background processor, Saga, Workflow, Lead qualification, Deal/Task/Contact mutation, AI, or frontend integration. Reproducible runtime checks are retained in `backend/scripts/verify-inbound-lead-webhook.ps1`; this authority section states durable implemented semantics only.

## AI Assistant implementation authority

The adopted frontend OpenAPI and current command/query registries declare no AI operation. The frontend connected-runtime operation names remain blocked proposals under `DEC-AI-ASSISTANT-API`, and WF-21 AI-suggestion-to-Task activation remains blocked. The backend therefore introduces the one narrow `PROJECT_EXTENSION_AI_ASSISTANT` contract documented in `AI_ASSISTANT_EXTENSION.md`: authenticated `POST /ai/advisories` returns a validated read-only advisory summary for one to three explicitly referenced Lead, Deal, or Task records. No conversation, governance-decision, action-execution, or business-mutation operation is admitted.

The authority chain is B01 authenticated human identity, B02 trusted Workspace/membership, then B03 authorization at each owner context boundary. No canonical AI-entry capability currently exists, so none is invented. Leads, Deals, and Tasks respectively own `ILeadSummaryReader`, `IDealSummaryReader`, and `ITaskSummaryReader`. Each contract enforces `leads.read`, `deals.read`, or `tasks.read`, queries only the trusted Workspace, fails closed for foreign or unsupported restrictive record scope, removes hidden/masked fields before returning, exposes a fixed minimized projection, and records owner-read audit evidence. AI references only those public application contracts and has no access to an owner DbContext, repository, Infrastructure type, EF entity, or SQL surface.

The AI allowlist contains only `lead.summary.read`, `deal.summary.read`, and `task.summary.read`. All are bounded owner reads selected in application code; neither the caller nor provider can add a tool. Prompt construction separates policy, user question, and explicitly delimited untrusted CRM data. Context is limited to one record per owner and excludes full aggregates, activities, notes, descriptions, buyer/contact details, assignee identity, authorization internals, and credentials. Provider output is untrusted strict JSON and must satisfy the bounded summary, optional suggested-next-action, and attention-point schema before it is returned.

`IAiProvider` is provider-neutral. Provider/model selection and the one-to-sixty-second timeout are server-owned configuration. No external production provider is currently authoritative or implemented. The explicitly configured `DevelopmentDeterministic` provider runs only in the Development host environment and supplies normal, unavailable, timeout, and malformed-output smoke modes. Other environments fail closed with `AI_PROVIDER_UNAVAILABLE`; no credentials are accepted on the assistant request. Safe execution metadata and context field names are logged without persisting prompts, CRM values, or provider responses. AI owns no DbContext, schema, table, or migration.

An advisory result remains non-authoritative. Its suggested next action does not create a Task; it does not qualify or mutate a Lead; it does not change a Deal; it invokes no Workflow and is not triggered by inbound webhooks. A future separately admitted mutation tool can depend on a narrow owner command contract without AI persistence access, but no mutation tool is implemented here.

Multi-context requests required B03's request-scoped authorization accessor to accept repeated resolution only when Workspace, account, member, membership, roles, capabilities, product spaces, data scopes, and field-security projections remain identical; evaluation time alone may differ. A changed effective authority still fails closed, and a denied later capability never reaches the provider.

Runtime verification on 2026-08-24 used isolated LocalDB databases. `UnicoreCRM_AI_Assistant_Verification_20260824_02` proved authenticated positive advisory composition through all three owner contracts, structured output validation, two-Workspace isolation for foreign Lead/Deal/Task references, strict Workspace/tool input rejection, missing Deal capability denial before an unavailable provider, hidden-field removal before provider invocation, prompt-injection code boundaries, unavailable/malformed/timeout provider behavior, ApiHost health after failure, owner-read audit evidence, and unchanged Lead/Deal/Task aggregate counts. Normal owner create/get regressions passed. The separate `UnicoreCRM_Inbound_Regression_After_AI_20260824_01` run passed the frozen inbound Lead webhook harness after the shared accessor change. Therefore the AI Assistant Core project extension is verified for Development runtime; real external-provider runtime remains not verified.

## Initial Workspace Provisioning implementation authority

The adopted frontend OpenAPI declares no Workspace-creation operation and the Design Authority declares no workspace-creation workflow, so an account holding zero active Workspace memberships previously had no admitted path to any Workspace. The backend therefore introduces the one narrow `PROJECT_EXTENSION_INITIAL_WORKSPACE_PROVISIONING` contract documented in `INITIAL_WORKSPACE_PROVISIONING_EXTENSION.md`: authenticated `POST /workspaces/initial-provisioning` (`provisionInitialWorkspace`) exposes exactly one provisioning intent. `/workspaces` and `/workspaces/{workspaceId}/bootstrap` wire behavior is unchanged, and `provisionWorkspaceMember`, the invitation operations, the `workspace-configuration` operations and the Studio surface remain fail-closed exactly as recorded above.

`listMyWorkspaces` remains the sole lifecycle authority: zero active memberships admit Initial Setup, one or more active memberships require restore/select and forbid provisioning. Registration and sign-in create no Workspace. Initial Setup draft state is frontend-only, and abandoning it before Finish or Skip creates nothing. Finish and explicit Skip send the same canonical intent; Skip is exactly the request that omits every optional value. No first-login flag, local storage value, product-space count, foreign entity count, `404` or setup-screen state participates, and no persisted "current workspace" is added to IdentityAuth or the session.

The mutation is multi-owner, so it is implemented in Workflows and calls approved owner contracts only; it holds no foreign DbContext, repository, Infrastructure type or EF entity. It writes through two owner-specific DbContexts and therefore cannot commit or roll back in one local transaction, so per `ARCHITECTURE_SKELETON.md` it is a `Durable` workflow and not an `Atomic` one. It is implemented in `UnicoreCRM.Workflows/Durable` and is the first implemented workflow in the system. IdentityAuth owns `IAuthenticatedIdentityReferenceLookup` and fails the workflow closed unless the authenticated account is currently active. Workspace owns `IInitialWorkspaceProvisioning` and assigns the Workspace identifier, the server-derived Workspace key, the ACTIVE creator membership identifier, the configuration seed and the account-scoped provisioning anchor. AccessControl owns `IInitialWorkspaceAccessProvisioning` and creates the one server-owned `Workspace Owner` role plus the creator assignment; the workflow can neither name the role nor choose a capability. The current admitted initial capability set contains only canonical capabilities already admitted for implemented operations - `workspace.context.resolve`, `contacts.read`, the five `tasks.*`, the four `leads.*`, the seven `deals.*`, the four `products.*` and the four `support.*`. `access.*`, `studio.*`, `audit.*` and every unsupported Contacts capability are excluded because their administrative or mutation operations remain fail-closed, and no data-scope or field-security policy is created. Exact pre-Contacts roles converge only under the narrow identity and snapshot rules recorded in *CONTACTS READ CORE INTEGRATION HARDENING*; all other drift still fails closed.

The caller supplies optional `name`, `logoText`, `locale`, `timeZone` and `baseCurrency` values matching the shapes the current OpenAPI already declares for `WorkspaceMembershipSummary` and `WorkspaceRuntimeConfiguration`. The request body is read strictly by the endpoint's own serializer options rather than by ambient host configuration: unknown members are rejected, the body is read from the stream instead of being inferred from a declared `Content-Length` so a chunked body is validated identically, and bodies above 8192 bytes are rejected. An absent, empty, whitespace-only or JSON-`null` body is the Skip path. It cannot supply the creator account, creator member, membership status, Workspace aggregate ID, membership aggregate ID, Workspace key, role, capability, enabled module set or product-space set. Server-owned deterministic defaults are `My Workspace`, derived logo initials, `en`, `UTC`, `USD`, `["contacts","leads","deals","tasks"]` and `["crm"]`.

WorkspaceConfig remains a `DEFERRED` Platform owner and `WorkspaceBootstrapProjection` is **not** promoted to configuration authority. The extension admits only the minimal `InitialWorkspaceConfigurationSeed` creation-time contract, written once inside the Workspace-owned transaction because the projection is Workspace-owned persistence that the Workspace-owned bootstrap read structurally requires. It has no endpoint and no mutation surface, existing values are never rewritten, and the legacy `CapabilitiesJson` column is seeded empty because B03 made the AccessControl application boundary the bootstrap capability authority. Configuration change after provisioning remains an authority gap until a WorkspaceConfig contract is admitted.

The Workspace write and the AccessControl write are separate owner-local transactions, so the workflow is deliberately **not** claimed to be one atomic commit; owner-specific DbContexts are preserved and no distributed transaction, event bus, saga or microservice is introduced. Correctness comes from durable progress plus convergence. `workspace.InitialProvisioningRecords` keys on `AccountId`, so at most one initial Workspace can ever exist per account. Step one commits the Workspace, the ACTIVE membership, the configuration seed and the anchor in the `AccessPending` state in one transaction, and rolls all of them back on conflict. Step two runs the AccessControl participant and then advances the anchor to `Completed`; both are convergent. A completed anchor is never returned to pending or rewritten by policy expansion, but its corresponding AccessControl-owned initial role may be reconciled by the bounded server-start policy scan recorded in *CONTACTS READ CORE INTEGRATION HARDENING*.

The only non-atomic window is a committed Workspace whose access assignment did not complete. That state is not self-correcting from the client, because the account then lists an active membership, legitimately skips Initial Setup and never resends the intent, while bootstrap denies authorization. Recovery is therefore server-driven: the `AccessPending` anchor is the authoritative outstanding-work record, and `InitialWorkspaceProvisioningResumeService` in `Workflows/Durable` finishes outstanding anchors through the same owner contracts at host start and then on a server-owned interval. The request path converges as well. Recovery never creates a second Workspace, membership, configuration seed, role or assignment and never mutates membership status. It introduces no first-login flag, no persisted current workspace and no client-held lifecycle state, and `listMyWorkspaces` and `getWorkspaceBootstrap` are unchanged and gained no recovery logic.

The exact admitted idempotency semantics, in precedence order, are: request validation precedes every replay rule, so contract-invalid header or body values return `VALIDATION_FAILED` even for an already-provisioned account and replay semantics apply only to contract-valid requests; then the account-scoped lifecycle decision precedes idempotency comparison, so an account with foreign-origin Workspace access always receives `WORKSPACE_ALREADY_PROVISIONED` regardless of key or values; then, on any replay, the stored provisioning is authoritative and the supplied setup values are ignored. Within that order the same key with different effective values fails closed with `IDEMPOTENCY_KEY_REUSED`; a different idempotency key with any contract-valid setup values returns `200 REPLAYED` for the same Workspace with the supplied values ignored; and a retry against an `AccessPending` anchor completes the outstanding step before replaying. The stored key and value fingerprint are written once at creation and never rewritten, and the key is compared only against that account's own anchor.

The new Workspace is not trusted because creation returned its identifier. Normal trust rules still apply: the client reloads memberships, selects the Workspace, `getWorkspaceBootstrap` verifies active membership, TrustedWorkspace is established, AccessControl evaluates effective authority, and workspace-required CRM requests continue to use normal `X-Workspace-Id` resolution.

Runtime verification on 2026-08-24 used the isolated `UnicoreCRM_InitialProvisioning_Recovery_20260824` LocalDB database and a real ApiHost. It proved zero memberships for a new account, an abandoned setup that created no Workspace/membership/anchor/assignment across a later session, a Finish call producing exactly one Workspace, one ACTIVE creator membership, one configuration seed and one access assignment with the exact server-owned capability set, the new Workspace appearing in `listMyWorkspaces`, successful bootstrap, authorization context and workspace-required Tasks/Leads/Deals reads, a Skip call applying every documented default, an identical retry replaying without duplication, a reused key with changed values failing closed, an unrelated key converging on the same Workspace, six concurrent submits producing exactly one `201` and one Workspace/membership/assignment/role, and a `409 WORKSPACE_ALREADY_PROVISIONED` rejection that created nothing for an account whose Workspace access came from elsewhere. It also proved request-contract strictness: an unknown request member is rejected with `VALIDATION_FAILED`, the same unknown member is still rejected when the body arrives chunked with no declared length, an oversized body is rejected, and none of the rejected requests created state.

Partial-failure recovery was proved by Development-only fault injection rather than by argument. With the AccessControl participant forced to fail and the resume pass disabled, provisioning returned a server error while the Workspace, the ACTIVE membership and the configuration seed were committed, the anchor recorded `AccessPending`, no access assignment existed, `listMyWorkspaces` already reported one Workspace, and bootstrap returned `ACCESS_DENIED` - the exact wedge. After restarting the host without fault injection and with no client action, the durable resume pass completed the anchor; the account then listed the same Workspace, bootstrap, authorization context and workspace-required Tasks/Leads/Deals reads all succeeded, and exactly one Workspace, membership, configuration seed, role, assignment and anchor remained. A later provisioning intent replayed onto the same Workspace without creating a second membership, and the run ended with zero outstanding anchors.

IdentityAuth register/sign-in/session/refresh/sign-out, Workspace list/bootstrap, AccessControl context and Tasks/Leads regressions passed in the same run. The frozen inbound Lead webhook and AI advisory harnesses passed on the isolated `UnicoreCRM_Inbound_Regression_20260824_R2` and `UnicoreCRM_AI_Regression_20260824_R2` databases. Only `WorkspaceDbContext` gained migrations, and all affected models reported no pending changes. The Workspace chain is `InitialWorkspace`, `InitialWorkspaceProvisioning`, `InitialWorkspaceProvisioningRecovery` and `InitialWorkspaceProvisioningRecoveryCorrection`, in that order.

A published migration is immutable: once a migration ID may exist in any `__EFMigrationsHistory` table it is never edited, renamed, reordered or reused, because a database that already ran it will not run it again and would silently diverge from the repository. Defects in a published migration are repaired by a new migration.

`InitialWorkspaceProvisioningRecovery` added `State` and `CompletedAt` and backfilled every pre-existing anchor as `State = 'Completed', CompletedAt = ProvisionedAt`. That backfill was wrong: the version that wrote those anchors committed the Workspace, membership, configuration seed and anchor in one transaction and only then created the AccessControl assignment, so such an anchor proves nothing about whether the assignment exists, and any account whose assignment was in fact missing would have been left permanently unable to bootstrap. That migration is preserved exactly as published. The data-only `InitialWorkspaceProvisioningRecoveryCorrection` repairs the rows it fabricated, rewriting only `State = 'Completed' AND CompletedAt = ProvisionedAt` to `State = 'AccessPending', CompletedAt = NULL`. It introduces no model change and leaves the snapshot untouched, its `Down` is deliberately empty because reverting would re-fabricate the removed completion fact, and it is idempotent: repaired rows no longer match, genuine completions carry a later completion time, already-outstanding anchors carry a `NULL` completion time, and a fresh database has no rows at all. The Workspace migration never inspects AccessControl persistence; it only returns ambiguous rows to outstanding work, and the durable resume path decides completion through the approved AccessControl contract.

Upgrade verification on 2026-08-24 used three isolated LocalDB databases. `UnicoreCRM_ProvisioningCorrection_20260824_Fresh` proved the whole chain applies to an empty database, produces the current anchor schema and leaves no anchors. `UnicoreCRM_ProvisioningCorrection_20260824` was built at the schema state that had already applied the faulty migration and seeded three accounts: a legacy fabricated anchor with an existing `Workspace Owner` role and creator assignment, a legacy fabricated anchor with no AccessControl assignment, and a genuinely completed anchor whose completion time is later than its provisioning time. Applying only the corrective migration returned both legacy anchors to `AccessPending` with no completion time and left the genuine anchor `Completed` with its completion time intact. Starting the current host with the resume pass enabled and no client action completed both corrected anchors while never replaying the genuine one; the pre-existing role and assignment identities were preserved rather than replaced or duplicated, the missing assignment was created exactly once against the creator membership, all three accounts passed list, bootstrap, authorization-context and workspace-required Tasks/Leads/Deals reads, a further resume window changed nothing, and each account retained exactly one Workspace, membership, configuration seed, role, assignment and anchor. `UnicoreCRM_ProvisioningCorrection_20260824_Chain` proved the path for a database that never applied the faulty migration: a pre-recovery anchor upgraded across the whole chain ends as outstanding work, converges to `Completed`, and reaches the same single-record runtime state. Reproducible upgrade checks are retained in `backend/scripts/verify-initial-workspace-provisioning-upgrade.ps1`. Therefore `INITIAL WORKSPACE PROVISIONING: PASS`; the deferred WorkspaceConfig, invitation, member-administration and Studio gaps above remain fail-closed. Reproducible runtime checks are retained in `backend/scripts/verify-initial-workspace-provisioning.ps1`.

## Email Verification OTP implementation authority

`verifyEmail` was recorded above as a fail-closed `AUTHORITY_GAP` because no current authority
defined verification-token issuance, delivery, hashing, expiry or consumption. That gap is now
resolved under explicit project authority by `PROJECT_EXTENSION_EMAIL_VERIFICATION_OTP`, frozen in
`EMAIL_VERIFICATION_OTP_EXTENSION.md`. The admitted decision is that a six-digit one-time code
delivered by email is the canonical email-verification credential. This section supersedes the
`verifyEmail` entry in the B01 gap list; every other operation listed there — `verifyMfa`,
`requestPasswordReset`, `resetPassword` and `acceptWorkspaceInvitation` — remains fail-closed and
unimplemented.

The extension admits one new operation, `requestEmailVerification` (`POST
/auth/email-verification-requests`), and retires the token-based request body of the canonical
`verifyEmail` operation. `POST /auth/email-verifications` keeps its path, operation name, `200`
`UserAccountDocument` response and header contract, but its request body is now `{ email, code }`.
The historical `VerifyEmailRequest` carrying an opaque `token` is retired, is not implemented, and is
rejected as `VALIDATION_FAILED` because the host rejects unmapped members. No verification link,
emailed URL or token issuance exists anywhere in the implementation. As with the inbound Lead
webhook, AI assistant and initial Workspace provisioning extensions, the Design Authority baseline
was not edited and remains at SHA-256
`8278547df0fd4be9a9af9b8a6d5f3e15ddad8d005d804c99a7c9248e0f402757`. The current generated
frontend OpenAPI was later reconciled for Product projection concurrency and is now SHA-256
`f3a0273e9d8847b5bcd8c673810e2a9e8d0e70031da12b4dc2a8dd338a2354b6`; it still does not declare
the email-verification extension. For these two email-verification operations the extension document
controls the implemented backend, and the divergence from the adopted public OpenAPI is deliberate
and recorded.

IdentityAuth owns the whole feature. `registerAccount` keeps its wire contract and still creates a
`PENDING_VERIFICATION` account with a server-assigned identifier, still provisions no Workspace and
still performs no AccessControl mutation; it now also persists the first verification challenge and
dispatches its code inside the same serializable registration transaction. `signIn`,
`getCurrentSession`, `refreshSession`, `signOut`, the `HttpOnly` `SameSite=Strict` refresh cookie and
the existing `EMAIL_NOT_VERIFIED` refusal for a non-active account are unchanged. Workspace and
AccessControl ownership, registration provisioning semantics and the Initial Workspace Provisioning
lifecycle are untouched.

`iam.EmailVerificationChallenges` is the new IdentityAuth-owned persistent state: account reference,
keyed code digest, creation, expiry, resend availability, attempt count, the attempt ceiling captured
at issuance, and the consumption and supersession markers. The plaintext code is never persisted,
audited, logged by the application outside the Development sender, or returned on the wire; only a
purpose-separated HMAC-SHA256 digest bound to the owning account is stored, and comparison is
fixed-time. Codes are drawn from a cryptographic generator across the whole six-digit range without
modulo bias. A challenge is usable only while it is unconsumed, unsuperseded, unexpired and below its
ceiling. A wrong code commits its attempt increment before responding; an exhausted ceiling refuses
even the correct code; issuing a new challenge supersedes every outstanding one, so a resend
immediately invalidates the previous code; and successful verification consumes the challenge and
sets `Status = Active` with `EmailVerifiedAt = now` in one serializable transaction. Only a
`PENDING_VERIFICATION` account can be activated this way, so email verification never reinstates a
suspended account.

Account existence is not disclosed beyond what the flow requires. A contract-valid verification
request returns the same `202` acceptance for an unknown address, an already active account, a
suspended account and an account still inside its resend cooldown; the cooldown is enforced by
silently issuing nothing rather than by a distinguishable rejection. Verification failures collapse
to one `TOKEN_INVALID` answer for an unknown address, a non-pending account, a missing, superseded or
consumed challenge and a wrong code. Only an expired code and an exhausted ceiling are reported
distinctly, because the caller must be able to act on them and both require an outstanding challenge
the caller already holds. A configured-but-failing email boundary returns `INTEGRATION_UNAVAILABLE`
rather than claiming a code was sent.

`IIdentityEmailSender` is the IdentityAuth-owned provider-neutral outbound boundary, and it answers
two separate questions: `EnsureConfigured` asks whether this host could ever deliver mail, without a
network call and on the request path, while `SendEmailVerificationCodeAsync` performs the remote call
and is reached only from the outbox dispatcher. Four senders exist. The fail-closed
`UnavailableIdentityEmailSender` is the default in every environment and the fallback for every
unrecognised kind. The console `DevelopmentLoggingIdentityEmailSender` is registered only when the
running host environment is Development and the sender kind is explicitly `DevelopmentLog`. The
`SimulatedFailingIdentityEmailSender`, gated identically under the kind `DevelopmentFailing`, always
fails with an error string that deliberately echoes the recipient, the full subject and the live
code back at the caller; it exists only so verification can prove that provider-authored text
reaches neither persisted delivery evidence nor a log.
`GmailSmtpIdentityEmailSender` performs real delivery through Gmail's submission service using the
configured account and a Google App Password, and is available to any environment. There is no no-op
or pretend-success sender: a host with no usable sender configuration fails registration and
verification requests closed with `INTEGRATION_UNAVAILABLE` rather than creating accounts nobody can
activate.

`GmailSmtpIdentityEmailSender` is the only type in the solution that touches an SMTP or MIME type. It
uses MailKit, and no MailKit or MimeKit type appears in IdentityAuth's Domain, Application or
Contracts layer or in any other module. Its transport is always encrypted - STARTTLS on the
submission port, or implicit TLS when STARTTLS is disabled - with no plaintext fallback. It logs
nothing, and no provider-authored text leaves it at all. `Username`, `AppPassword` and `FromAddress`
are absent from every tracked configuration file and are supplied only from untracked local
configuration or a deployment secret store.

Provider error text is classified, never quoted. A failed send is mapped onto one of IdentityAuth's
own bounded values - `SMTP_AUTH_FAILED`, `SMTP_CONNECT_FAILED`, `SMTP_TIMEOUT`,
`SMTP_PROTOCOL_ERROR`, `SMTP_COMMAND_FAILED`, `SMTP_RECIPIENT_REJECTED`,
`SMTP_PROVIDER_UNAVAILABLE` or `UNKNOWN_DELIVERY_FAILURE` - by inspecting only the exception's type,
and the exception itself is discarded. Redacting known credentials out of a provider message was a
denylist and could not be complete: SMTP error text quotes server dialogue, and that dialogue echoes
the recipient address, the headers and a `Subject` line that in this product contains the
verification code itself. The complete vocabulary that may reach `iam.EmailOutboxMessages.LastError`
is the `EmailOutboxReasons` constant set, which adds `EMAIL_SENDER_UNAVAILABLE`,
`PAYLOAD_UNREADABLE`, `CODE_EXPIRED_BEFORE_DELIVERY`, `CHALLENGE_SUPERSEDED`, `CHALLENGE_CONSUMED`,
`CHALLENGE_EXPIRED` and `CHALLENGE_NOT_DELIVERABLE` for the outcomes the dispatcher decides itself.
Logs carry those same codes with identifiers and attempt counts, and never a recipient, a code, a
credential or provider text. MailKit's compile assets are private to `UnicoreCRM.Platform`, so no
consuming project can bind to a MailKit or MimeKit type even by accident, while its runtime assets
still flow to the host that has to run the sender.

Remote SMTP is never performed inside the serializable issuing transaction, because a network call
would hold locks for its whole duration and a provider outage would roll back an account. Issuance
checks sender configuration, then commits the challenge and exactly one `iam.EmailOutboxMessages` row
together. `IdentityEmailOutboxDispatcher`, an IdentityAuth-owned hosted service, delivers afterwards;
a committed transaction signals it so it runs immediately, and a dropped signal only delays a message
because the idle pass finds the same durable rows. PlatformOperations owns an `Outbox` module, but it
is an empty placeholder with no approved cross-owner contract, and LAW-04/LAW-05 forbid reaching into
another owner's persistence, so this is deliberately the smallest IdentityAuth-owned durable
mechanism: one table and one hosted service, with no broker, queue product or other distributed
infrastructure.

The outbox never stores the code in the clear. It holds AES-GCM ciphertext under a purpose-separated
key derived from the identity pepper, with the owning challenge identifier as associated data, and
clears the payload the moment a message reaches any terminal state. Verification never reads that
path; it still compares against the one-way digest on the challenge.

A queued message carries a credential, so it is deliverable only for as long as its challenge is, and
no code may be delivered once its challenge is superseded, consumed or expired. Delivering a revoked
code is worse than delivering nothing, because the holder would enter it, fail, and spend an attempt
of the challenge that actually is active. Two redundant mechanisms enforce this. The serializable
transaction that revokes a code also retires the message carrying it, setting the terminal
non-deliverable `Cancelled` status, dropping the payload and recording `CHALLENGE_SUPERSEDED` or
`CHALLENGE_CONSUMED`; the message is deliberately not recorded as `Sent`, because nothing was sent.
Independently, the dispatcher re-reads the challenge immediately before every send and cancels the
message with no network call if it is no longer eligible, which also covers an expiry that simply
elapsed and any future writer that omits the first step.

A code that is already being delivered cannot be revoked, because the send may already have reached
the provider. `LeasedUntil` is the durable signal that separates a queued message from one whose
delivery attempt is claimed and unresolved, and the claim commits it before any network call. An
issuing transaction reads it and, when the account's current message is in flight, raises
`IdentityEmailDeliveryInFlightException` and issues nothing rather than creating the forbidden state
in which the old code is invalid, the new code is active and the old email still arrives.
`requestEmailVerification` answers with its usual uniform `202` and an `ACCEPTED_DELIVERY_IN_FLIGHT`
audit outcome, does not restart the cooldown, and the caller may simply ask again; registration
cannot reach this path because a new account has no outstanding challenge. The two transactions
serialize on the same outbox row, so either the claim commits first and issuance declines, or the
cancellation commits first and the message is no longer claimable. No SMTP call moved into the
issuing transaction to achieve this.

Claims are per message, never per batch. A pass reads a bounded set of due candidates without locking
or claiming anything, then re-reads, re-checks and claims each one in its own small serializable
transaction immediately before that message's own send. A batch-wide claim could not hold the
invariant: messages are delivered sequentially, so one timestamp shared across a batch is already
stale by the time the later messages start, and with the shipped defaults a later send could begin
after the claim covering it had expired - a send in flight with no live claim, which is exactly the
state a resend is entitled to assume cannot exist. The effective lease is never shorter than the
sender timeout plus a thirty-second safety margin, and every send is capped at the remaining time on
its own claim; if that remaining time is not positive the sender is not called at all, the claim is
released, and a later pass claims the message afresh. No path falls back to the sender's own timeout
once the durable claim has lapsed. Because eligibility is re-checked inside the claim transaction and
before the attempt is counted, a message retired without ever being sent no longer spends a delivery
attempt.

`iam.EmailOutboxMessages` carries a `rowversion` concurrency token as defence in depth behind those
rules. A delivery outcome written against a stale row image raises `DbUpdateConcurrencyException`
rather than winning silently; the dispatcher reloads the row, preserves whatever the other writer
committed, and logs the conflict with the status it preserved. It never retries the write, because a
retry would be a finished send stamping `Sent` over a row that has since been cancelled - the
database asserting that a revoked code was delivered.

Claiming a message is a lease: the claim counts the attempt and pushes the next attempt out inside a
serializable transaction that commits before any network call, so a dispatcher that dies mid-send
releases that message by expiry and two passes never send the same message at once, because an
already-claimed candidate is skipped. Delivery is
therefore at-least-once, and a repeat can only ever resend the same code, because the message is
keyed one-to-one to its challenge by a unique index and creates no account or challenge state of its
own; a transient provider failure can never produce a duplicate account or a duplicate OTP challenge.
Failures record a scrubbed reason and reschedule with capped exponential backoff, and a message is
abandoned once its delivery ceiling is reached or once its code expires before delivery, leaving the
account `PENDING_VERIFICATION` and free to request a new code. A host whose sender configuration is
unusable holds every message untouched instead of burning attempts.

One consequence is deliberate and is recorded here rather than left implicit: registration now fails
closed only on misconfiguration. A transient provider failure no longer fails registration - the
account is created `PENDING_VERIFICATION`, the message stays queued and delivery is retried - which
is the trade for keeping SMTP out of the transaction.

Only `IdentityAuthDbContext` gained migrations: `20260825013815_IdentityEmailVerification`,
`20260825031648_IdentityEmailOutbox`, the additive `20260825060515_IdentityEmailOutboxSupersession`,
which adds the single nullable `LeasedUntil` column, and the additive
`20260825072836_IdentityEmailOutboxConcurrencyToken`, which adds the `rowversion` column that SQL
Server populates for existing rows. Runtime
verification on 2026-08-25 used the isolated `UnicoreCRM_EmailOtp_Verification_20260825` LocalDB
database and a real ApiHost, and proved: registration producing a `PENDING_VERIFICATION` account with
exactly one challenge and one dispatched code; the plaintext code absent from persistence with a
64-character digest stored instead; `EMAIL_NOT_VERIFIED` before verification; `VALIDATION_FAILED` for
a five-digit, non-numeric or malformed-address request; a wrong code rejected with its attempt
increment committed; a resend inside the cooldown accepted while issuing nothing and leaving the
usable code intact; a resend after the cooldown superseding the previous code and immediately
refusing it; the attempt ceiling refusing even the correct code with `RATE_LIMITED`; an exhausted
challenge buying no new code inside the resend cooldown; an expired code refused with
`TOKEN_EXPIRED`; the correct code activating the account exactly once with the
verification stamp set, one consumed challenge and none outstanding; the consumed code refused on
reuse; a request for an already active account issuing nothing; sign-in succeeding after
verification; an unknown address answered with the same acceptance while creating no account and no
challenge; idempotent replay returning the same acceptance and a reused key with a different address
failing closed; a challenge and its code surviving a host restart and verifying afterwards; and both
fail-closed sender paths — an unconfigured sender and a non-Development host that tries to select the
Development sender — returning `INTEGRATION_UNAVAILABLE` while creating no account and leaving no
orphaned challenge. All six owner models reported no pending changes.

Regressions passed in the same session on isolated LocalDB databases:
`UnicoreCRM_OtpRegression_Provisioning_20260825` passed the frozen Initial Workspace Provisioning
harness including its IdentityAuth register/sign-in/session/refresh/sign-out and Tasks/Leads/Deals
checks, and `UnicoreCRM_OtpRegression_Upgrade_20260825` passed the migration-chain upgrade harness.
Therefore `EMAIL VERIFICATION OTP: PASS`. Reproducible runtime checks are retained in
`backend/scripts/verify-email-verification-otp.ps1`.

Gmail SMTP delivery and the outbox were verified on 2026-08-25. The isolated
`UnicoreCRM_Outbox_Verification_20260825` LocalDB database and a real ApiHost re-proved every OTP
behaviour above and additionally proved: registration staging exactly one outbox message whose
payload never contains the plaintext code; delivery after commit clearing that payload in one
attempt; a resend staging and delivering a second message; registration still succeeding with
`PENDING_VERIFICATION` while the provider is unreachable, with the message left pending, its scrubbed
failure reason recorded, its payload retained for retry and no credential anywhere in the host log; a
host restart resuming the pending message and creating no second account, challenge or message; and
the recovered boundary delivering the original message so its original code still verified and signed
in. The isolated `UnicoreCRM_GmailAuth_20260825` database proved the real transport end to end
against `smtp.gmail.com:587`: MailKit completed the STARTTLS handshake and Gmail answered
`535 5.7.8 Username and Password not accepted` for deliberately wrong credentials, after which the
message stayed pending with one attempt, the recorded reason contained neither the username nor the
app password, and neither appeared in the host log. Delivery to a real inbox with valid credentials
is confirmed separately by the repository owner, because it needs a live Gmail account. The frozen
Initial Workspace Provisioning harness passed on
`UnicoreCRM_OutboxRegression_Provisioning_20260825`, and all six owner models reported no pending
changes.

The supersession and error-recording semantics above were verified on 2026-08-25 on the isolated
`UnicoreCRM_OtpSupersession2_20260825` LocalDB database and a real ApiHost, which re-proved every OTP
and outbox behaviour already recorded and additionally proved the two review findings closed.

For the stale-message case it reproduced the reviewed scenario exactly: an account registered while
the provider was unreachable, its message left retrying with its payload intact, the resend cooldown
elapsed, a resend issued, and the provider then recovered. A resend attempted while the message's
delivery claim was unresolved issued nothing, left the challenge unrevoked and left the message
uncancelled. Once the claim resolved, the resend superseded the challenge and drove the stale message
to `Cancelled` with its payload cleared, `SentAt` still null and `CHALLENGE_SUPERSEDED` recorded.
After the provider recovered, only the currently valid message was delivered, exactly one code was
ever handed to a sender for that account, the live challenge had spent no attempt, and that one code
verified and signed in. The dispatcher's own gate was proved independently on a second account whose
challenge was superseded directly in the database, leaving its message `Pending` and still holding a
deliverable payload: the dispatcher retired it as `Cancelled` with `CHALLENGE_SUPERSEDED`, dropped
the payload, never recorded it as sent, and never handed the code to the console sender that would
have logged it.

For the error-recording case a Development-only simulated provider failed with an error string
containing the exact recipient, the full subject, the live six-digit code and the configured SMTP
username, writing that string to its own transcript so the assertions ran against the real values.
`UNKNOWN_DELIVERY_FAILURE` was persisted instead, and none of the recipient, the code, the subject,
the username or the fabricated text appeared in `LastError` or in any host log. A whole-run sweep
confirmed every persisted delivery reason was an application-owned value.

The real transport was re-proved on the isolated `UnicoreCRM_GmailTransport_20260825` database
against `smtp.gmail.com:587`: MailKit completed the STARTTLS handshake and Gmail rejected
deliberately wrong credentials, which classified as `SMTP_AUTH_FAILED` in exactly one attempt with
nothing reported as delivered, and neither the username, the app password, the recipient, the Gmail
response text nor its enhanced status code reached `LastError` or the host log. That run sends no
mail and is retained as `backend/scripts/verify-gmail-transport.ps1`.

End-to-end delivery to a real inbox was then verified on 2026-08-25 on the isolated
`UnicoreCRM_GmailInbox_20260825` database, with the repository owner reading the codes out of the
mailbox, using the real credentials from untracked local configuration and plus-addressed
recipients. Registration returned `201` `PENDING_VERIFICATION` with exactly one challenge and one
outbox message; the message was accepted by Gmail on its first attempt with its payload cleared and
no error recorded; the code arrived in the real mailbox; submitting it returned `200` `ACTIVE` with
`emailVerifiedAt` set and exactly one challenge consumed and none left outstanding; sign-in then
returned `200` with an access token; and the consumed code was refused on reuse.

The supersession path was exercised against the live provider on a second account. Its first message
was staged while the submission endpoint was unreachable, so it stayed `Pending` with its payload
intact after three attempts, each recorded as `SMTP_CONNECT_FAILED`. After the resend cooldown had
genuinely elapsed, a resend superseded the first challenge and drove that message to `Cancelled`
with its payload cleared, `SentAt` still null and `CHALLENGE_SUPERSEDED` recorded. With the real
submission endpoint restored, only the currently valid message reached Gmail, the superseded message
stayed terminal, the live challenge had spent no attempt, and the newly delivered code verified to
`ACTIVE` and signed in. Across the whole live run no message ever retained a payload, every recorded
delivery reason was a bounded application-owned value, and no configured credential, Gmail response
text, enhanced status code or delivered six-digit code appeared in any of the six host logs - whose
only email lines carry a message identifier, an account identifier, an attempt number and a reason
code.

Per-message claiming and the concurrency token were verified on 2026-08-25 on the isolated
`UnicoreCRM_H3Batch2_20260825` database, whose 198 checks include every behaviour recorded above.

The batch dimension is covered explicitly, because a harness that delivers one due message at a time
cannot see the defect at all. Five accounts were staged against an unreachable submission endpoint so
their messages ended up queued together, then released into a single dispatcher batch behind a sender
that took nine seconds per send, with the effective lease reduced to thirty-one seconds. All five were
claimed individually, each with its own distinct claim expiry equal to the full effective lease, and
every claim was still in the future while its own send was running. The decisive assertion is that the
last send began *after* the first message's claim had already expired while still holding a live claim
of its own: under a batch-wide claim that send would have been running unprotected, which is exactly
the state that let a resend revoke a code already on its way. No claim outlived its send, and every
delivered message recorded a clean outcome.

The in-flight semantics were asserted from inside that window: with the resend cooldown deliberately
cleared beforehand, a resend issued while a send was blocked in the sender returned the uniform `202`,
superseded nothing, cancelled nothing and staged neither a new challenge nor a new message; once the
send resolved, a resend for the same account in the same cooldown state did issue, which proves the
refusal came from the live claim rather than from a cooldown.

The concurrency token was proved end to end rather than in isolation. A message was retired to
`Cancelled` by a second writer while its send was still running; the row's token advanced, and when
the finished send tried to record its outcome the write failed instead of winning. The row remained
`Cancelled` with a null `SentAt`, its reason code intact and its payload cleared, and the dispatcher
logged that another writer had committed `Cancelled` first and that state was preserved. The stale
message regression is unchanged and still passes.

The real Gmail path was re-verified after the change on `UnicoreCRM_GmailInbox_20260825`, with the
repository owner reading the code out of the mailbox: registration returned `201`
`PENDING_VERIFICATION` with exactly one challenge and one outbox message, the message was accepted by
Gmail on its first attempt with its payload cleared and no error recorded, the code arrived, verifying
it returned `200` `ACTIVE` with `emailVerifiedAt` set and one challenge consumed, sign-in returned
`200`, the consumed code was refused on reuse, and no credential, Gmail response text or delivered
code appeared in any host log.

The frozen Initial Workspace Provisioning harness passed on
`UnicoreCRM_H3Regression_Provisioning_20260825`, the local developer configuration contract
passed on `UnicoreCRM_H3LocalConfig_20260825` - which now runs against a synthetic content root and
leaves the developer's own `appsettings.Development.Local.json` byte-for-byte untouched - and all
owner models reported no pending changes. Reproducible checks are retained in
`backend/scripts/verify-email-verification-otp.ps1`,
`backend/scripts/verify-development-local-configuration.ps1` and
`backend/scripts/verify-gmail-transport.ps1`.

The current frontend still routes email verification through a `token` query parameter and the
retired contract, so the connected frontend cannot complete verification until it is aligned to the
OTP contract. That is separate frontend work and is not performed here. Password reset, MFA, admin
verification override, outbound provider integration and background cleanup of spent challenges
remain out of scope and fail-closed.

## Lead to Contact qualification and Contact identity frozen authority

On 2026-09-02 the Lead to Contact Qualification and Identity Authority Closure task froze the
**Contact leg** of WF-10 Lead Qualification. The full record is
`design-authority/canonical-design/authority/lead-contact-qualification-authority.md`, authority ID
`DEC-LEAD-CONTACT-QUALIFICATION-CLOSURE`. This section summarises what backend implementation may and
may not now assume. It admits no operation, adds no route, adds no wire field, adds no error code and
changes no admission row. `qualifyLeadForNurture`, `qualifyLeadForOpportunity` and
`qualifyLeadForDirectSale` remain `ADMITTED_NOT_IMPLEMENTED`; `createContact` and `updateContact`
remain `BLOCKED`; `qualifyLead` remains `RETIRED`.

**Business boundary.** Positive qualification resolves a relationship identity and closes the Lead.
It never creates a Customer: `LeadQualificationCreatedResource.resourceType` admits
`CONTACT, ORGANIZATION_ACCOUNT, TASK, DEAL, QUOTE, ORDER` and has no `CUSTOMER` member, and Customer
creation is WF-05, which is `BLOCKED`. There is no admitted operation whose only effect is
Lead to Contact; the Contact leg is the shared first step of all three typed operations, so freezing
it does not by itself make any of them implementable.

**Preconditions.** Authenticated principal; trusted active Workspace membership derived server-side;
`leads.qualify` at the Leads boundary, fail closed; `RESOURCE` record decision on the Lead with
unknown and foreign indistinguishable; `Idempotency-Key` present; `If-Match` exactly equal to
`Lead.Version`; `leadWorkState == VERIFYING`; progressive profile complete; relationship input valid;
`contacts` module enabled. Only `VERIFYING` is qualifiable and positive qualification is terminal -
`Lead.Reopen` requires `QualificationOutcome == Disqualified`.

The progressive profile **must be re-evaluated at command time**. `replaceLeadProfile` does not
re-check `HasProgressiveProfile()`, so a `VERIFYING` Lead can be edited into an incomplete state;
`WorkState == VERIFYING` is not proof of completeness. `LEAD_PROGRESSIVE_PROFILE_INCOMPLETE` is not in
the three operations' closed `x-error-codes`, so the failure reports `LEAD_INVALID_TRANSITION` (409).

**Terminal Lead state.** `leadWorkState = CLOSED`; `qualificationOutcome` in
`NURTURE | OPPORTUNITY | DIRECT_SALE`; the conversion reference is
`relationshipRef = { type: "CONTACT", id: contactId }`. `LeadDocument` declares **no** `contactId`,
`qualifiedAt` or `qualifiedBy` property and is `additionalProperties: false`, so none is added:
qualification time and actor are authoritative in the Leads-owned immutable command audit record and
in `LeadQualificationWorkflowResponse.occurredAt`. The asymmetry with `disqualifiedAt` /
`disqualifiedBy` is contract-driven and must not be "fixed" by inventing aggregate columns; doing so
requires a `PROJECT_EXTENSION_*`. `qualifiedDealId` is a separate older property that no lifecycle
invariant references and qualification does not write it. The Leads aggregate cannot express this
state today - `LeadQualificationOutcome` has only `Disqualified` and `Lead` carries no relationship
reference - so extending both is admitted Leads-owned work.

**Contact identity resolution.** The decision is **caller-declared and backend-validated, never
backend-discovered**. `mode=EXISTING` with `selectedId` links; `mode=NEW` with `contact` creates.
**Automatic duplicate matching is NOT ADMITTED**, so `CONFLICT / REQUIRES_HUMAN_RESOLUTION` is not
reachable: the 200 result requires a resolved relationship, the closed error vocabulary carries no
ambiguity code, `listContacts` declares no filter parameter, and the Contact profile is one JSON
column with no identity index and no unique constraint. `mode=NEW` must never be silently resolved to
a link, and `mode=EXISTING` must never fall back to creating. Identity comparison is confined to the
one trusted Workspace; `selectedId` equality is ordinal; and delivery/idempotency identity
(`Idempotency-Key`, `X-Request-Id`, `X-Correlation-Id`, `(IntegrationId, DeliveryId)`, `leadId`,
`campaignId`) is never person identity. Email and phone equality semantics remain `AUTHORITY_GAP`
because no value-based matching runs; `email` and `phone` in `LeadQualificationContactInput` are
Contact content, not match keys. Duplicate Contacts from two independent `NEW` qualifications are an
accepted consequence of the admitted contract (residual `R-1`).

**Data transfer.** No Lead profile fact is copied. `LeadQualificationContactInput` is
`additionalProperties: false` with exactly `{ displayName, email?, phone?, title? }`, supplied by the
caller, and the coordinator passes it through unchanged without reading `Lead.Profile`. Landing
fields are `displayName -> fullName`, `email -> workEmail`, `phone -> mobilePhone`,
`title -> jobTitle`, derived by elimination within each vocabulary. Contacts assigns `id`,
`workspaceId`, `version` and timestamps; `status` is `active`; `ownerId` is
`Lead.Profile.OwnerId` - the single admitted Lead to Contact transfer, and a record-access fact rather
than a profile fact, because a null owner would make the new Contact invisible to every `OWN`-scoped
member including the one who qualified it. Consent and every do-not-contact flag are **not**
transferred (gate `G-1`, which blocks public exposure of the three operations but not the participant
implementation). Qualification deletes nothing: the Lead row, profile, audit, outbox and idempotency
records are all retained.

**Contacts participant boundary.** A narrow Contacts-owned internal application boundary, not public
HTTP and not an admitted C# interface name. It accepts trusted Workspace context, the resolution
intent, `selectedId` or contact facts, the resolved `ownerId` and the conversion key; it returns
`CREATED | LINKED | REPLAYED | REJECTED` plus the authoritative `contactId` and `version`. Contacts
assigns `contactId` (LAW-08). Workflows never opens `ContactsDbContext` and Contacts never opens
`LeadsDbContext` (LAW-04, LAW-05). `LINKED` **never mutates the Contact** - no field, no version, no
`updatedAt` - which is what keeps the workflow clear of the `BLOCKED` `updateContact` surface,
including the `contact` object the schema requires even in `EXISTING` mode. Contacts writes its own
command audit and stages its own `CONTACT_CREATED` outbox message in its own transaction;
`contacts.AuditRecords` and a Contacts outbox table do not exist today and creating them is admitted
Contacts-owned work.

**Capabilities.** `leads.qualify` alone authorizes the command. `contacts.create` is **not** required
- it is `BLOCKED`, is absent from the server-owned `InitialWorkspaceAccessPolicy`, and requiring it
would make positive qualification permanently unreachable; the Contact is a server-owned consequence
of an authorized qualification, on the `provisionInitialWorkspace` precedent. `contacts.read` **is**
required for the `EXISTING` path so that a caller cannot probe Contact existence or link to a record
outside their record scope; its failure reports `LEAD_QUALIFICATION_RELATIONSHIP_INVALID`, not
`ACCESS_DENIED`, so denied and unknown stay indistinguishable.
`LEAD_QUALIFICATION_DOWNSTREAM_CAPABILITY_REQUIRED` stays reserved for the Quote/Order legs the
contract names.

**Coordination is DETERMINISTIC CONVERGENT, not atomic.** The pinned
`x-transaction-boundary: BACKEND_ORCHESTRATED_TRANSACTION_*` and the "Atomically" summaries are not
achievable and are not claimed: the CommercialEvidence Owner-Boundary Frozen Authority forbids a
foreign owner participating in a cross-DbContext transaction and requires each owner to commit only
its own state; every owner has its own `DbContext` with no ambient or distributed transaction
primitive admitted; WF-10's own `executionClassification` is `ATOMIC_OR_OWNER_LOCAL_AS_DECLARED`; and
`provisionInitialWorkspace`, the one implemented multi-owner workflow, is durable-convergent. The
frozen model commits the **Contact first**, writes a durable conversion anchor keyed by
`(trustedWorkspaceId, operationId, leadId, Idempotency-Key)` holding the resolved `contactId`, then
closes the Lead in a separate owner-local transaction. Contact-first is the only order in which every
individually committed state is valid, because a Lead closed first would carry a `relationshipRef`
pointing at a `contactId` that does not exist. Recovery is forward-only with **no compensation and no
Contact deletion**; a retry under the same idempotency key adopts the same `contactId`. An abandoned
attempt leaves an orphan Contact, which is accepted and named (`R-2`), not compensated. `If-Match`
applies to `Lead.version` only; no Contact version is required or asserted. Replay returns the same
`contactId` and `createdResources` with `outcome: "REPLAYED"` and does not advance `Lead.version`,
and the idempotency check precedes the lifecycle check so a replay is served even though the Lead is
now `CLOSED`. Each owner writes its own audit and stages only its own outbox leg; every step is
confined to the one trusted Workspace.

**Not closed by this freeze.** The Task, Deal, Quote and Order participants; the
`ORGANIZATION_ACCOUNT` relationship kind (`createOrganization`, `linkContactToOrganization` and WF-02
are `BLOCKED`); consent transfer (`G-1`); server-side duplicate detection, which would need a
filterable Contacts read, a typed ambiguity error and an indexed identity column; `qualifiedAt` /
`qualifiedBy` as Lead fields; and the `qualifiedDealId` versus `dealRef` redundancy. Two contract
divergences are recorded and not reconciled: `LeadRelationshipRef.type` is `CONTACT | ORGANIZATION`
while `RelationshipRef.type` is `CONTACT | ORGANIZATION_ACCOUNT` (harmless for the Contact leg), and
`operation-registry.json` plus `owner-context-map.json` still classify the Contacts reads as not
implemented although Contacts Read Core is implemented and runtime-verified.

Therefore `LEAD TO CONTACT QUALIFICATION AUTHORITY: FROZEN`,
`CONTACT IDENTITY RESOLUTION: FROZEN`,
`LEAD TO CONTACT DATA TRANSFER: FROZEN`,
`CONTACT PARTICIPANT BOUNDARY: FROZEN`,
`WORKFLOW / IDEMPOTENCY / CONCURRENCY: FROZEN`,
`CONSENT TRANSFER: AUTHORITY_GAP`,
`EMAIL / PHONE EQUALITY SEMANTICS: AUTHORITY_GAP`, and
`WF-10 PUBLIC OPERATIONS: ADMITTED_NOT_IMPLEMENTED`. This is an authority freeze only; no runtime
behavior was implemented or verified, and it is not a Control 1.2 independent-review attestation or a
release freeze.

### Amendment 2026-09-02 - Contact duplicate policy and minimum executable path

`DEC-LEAD-CONTACT-DUPLICATE-POLICY` closes the two gaps the freeze above left open. It is recorded in
`lead-contact-qualification-authority.md` sections 9 and 10, amends its sections 4.2, 4.3, 4.4 and 8,
and reopens none of the accepted decisions. It admits no operation, no route, no wire field and no
error code.

**Contact duplicate policy for `mode=NEW`.** Before Contacts creates a Contact it evaluates one
predicate inside its own owner-local `SERIALIZABLE` transaction: `normalize(e) = e.Trim().ToUpperInvariant()`,
compared for exact ordinal equality against the union of the Contact's normalized `workEmail` and
`personalEmail`, `WorkspaceId`-scoped and **record-scope-independent**. Absent email means no key.
**Zero matches create; one or more matches reject** with 422 `LEAD_QUALIFICATION_RELATIONSHIP_INVALID`
at field `relationship.contact.email`. One match and several matches are **indistinguishable**, and the
response carries no matched `contactId`, no count and no Contact field value - the scan deliberately
sees Contacts outside the caller's record scope, so any of those would be a disclosure channel, and
withholding them follows the no-result-cardinality rule already frozen in
`PROJECT_EXTENSION_READ_AUDIT_SEMANTICS`. Detection **never** resolves an identity: it cannot link,
cannot convert `NEW` into `EXISTING`, and never appears in a 2xx. `EXISTING` + `selectedId` is
untouched. Fuzzy matching of any kind is prohibited, and delivery/idempotency identity remains
excluded from the key.

The normalization is **not invented** - it is the rule IdentityAuth already uses as its account
uniqueness and lookup key (`IdentityAccount.NormalizedEmail`, `Trim().ToUpperInvariant()`, unique
index, runtime-verified across registration, sign-in and email verification). Adopting a second
email-equality rule in the same system would have been the invention. Case folding therefore covers
the whole address including the local part; no plus-address stripping, dot removal, IDN folding or
alias expansion is admitted.

**Smallest persistence requirement.** Detection cannot run against the single JSON profile column.
Admitted, Contacts-owned, `contacts` schema only: two persisted projection columns
`NormalizedWorkEmail` and `NormalizedPersonalEmail` (`nvarchar(254)`, nullable) plus non-unique
indexes `(WorkspaceId, NormalizedWorkEmail)` and `(WorkspaceId, NormalizedPersonalEmail)`. They are
derived state kept in step with the profile and never an independent fact - the same rule and the
same reason already applied to `Lead.ScopeOwnerId`. No wire contract, schema, operation, capability or
admission row changes, and `ContactDocument` never projects them. A **UNIQUE** constraint is **not**
admitted: no authority makes email a Contact uniqueness invariant, the field is optional so many
keyless Contacts must coexist, and a constraint would bind every future Contact path. The concurrent-
create race is closed instead by evaluating the predicate and inserting in the same Contacts-owned
`SERIALIZABLE` transaction, whose range lock blocks a concurrent insert into the same key range. This
is owner-local and does not affect the convergence model.

**Phone is excluded from the duplicate key - `AUTHORITY_GAP`, requirement stated.** No phone
normalization precedent exists anywhere in the repository. Digits-only is deterministic but splits
`0912345678` from `+84912345678`, missing the most common real duplicate; E.164 requires a default
calling region that no authority supplies, and inferring one from free-text `Lead.Country` would
fabricate business truth. Admitting phone requires all three of: a Workspace-owned default calling
region (blocked today behind `RC-STUDIO-WORKSPACE-CONFIG`); a frozen canonical form plus extension and
parse-failure behaviour, where parse failure must mean *no key*; and the projection-column shape above
repeated over `mobilePhone`, `workPhone` and `otherPhone`. Until then a phone-only person is not
duplicate-checked. Residual `R-1` is otherwise largely retired: it now survives only where no email is
supplied on either side, or where the same person uses two different addresses.

**Minimum executable path.** `qualifyLeadForNurture` (`POST /workflows/lead-qualification/{leadId}/nurture`).
`qualifyLeadForOpportunity` requires `interestedProductIds` against the open `AG-PRODUCT-SNAPSHOT`
gap, which both Leads and Deals already record as fail-closed; `qualifyLeadForDirectSale` requires
Quotes or Orders, every operation of which is `ADMITTED_NOT_IMPLEMENTED`. The NURTURE follow-up Task
is **required, not optional** - `x-transaction-boundary: ..._RELATIONSHIP_TASK_AND_LEAD`,
`x-event-outbox-expectation: LEAD_QUALIFIED_FOR_NURTURE_AND_FOLLOW_UP_TASK_CREATED`, and required
`revisitAt` + `reason` have no other consumer. `taskId` is optional in the result only because that
result type is shared by all three outcomes.

**Participants on the minimum path.** `Leads: REQUIRED`, `Contacts: REQUIRED`, `Tasks: REQUIRED`;
`Deals: NOT REQUIRED`, `Quotes: NOT REQUIRED`, `Orders: NOT REQUIRED`, `Organizations: NOT REQUIRED`.
Four of the seven historical WF-10 participants are not required. Tasks Core is `ADMITTED_IMPLEMENTED`
and runtime-verified (B04) and `CreateTaskRequest` already carries `relationshipRef` (`BuyerRef`,
admits `CONTACT`) and `sourceRef` as scalar evidence, so the Contact and the source Lead are
expressible with no new schema; only a narrow Tasks-owned create participant is needed.

**Capability split.** `tasks.create` **is** required of the caller at the Tasks participant boundary,
reported as `LEAD_QUALIFICATION_DOWNSTREAM_CAPABILITY_REQUIRED` (403); `contacts.create` remains **not**
required. The distinction is **grantability**: `tasks.create` is `ADMITTED_IMPLEMENTED` and is in the
frozen `InitialWorkspaceAccessPolicy` set, so requiring it deadlocks nothing and is exactly the case
that error code exists to express, whereas `contacts.create` is `BLOCKED` and appears in no role. The
duplicate scan is an internal Contacts predicate that discloses nothing, so it neither requires nor
consumes `contacts.read`.

**Frozen implementation order.** Each step is owner-local, independently committable and leaves the
system releasable: (1) Contacts identity projection columns and indexes; (2) Contacts conversion
participant with its first command-audit and outbox tables, enforcing the duplicate predicate;
(3) Leads terminal-qualification aggregate state, close command and durable conversion anchor;
(4) Tasks conversion participant; (5) Workflows nurture coordinator and route. Steps 1-4 are mutually
independent except that 2 depends on 1. Step 5 is the only step that needs all of them and the only
one that creates a public surface. Gate `G-1`, the consent-transfer decision, still blocks public
exposure at step 5 and blocks nothing in steps 1-4.

Therefore `CONTACT DUPLICATE POLICY: FROZEN`, `EMAIL NORMALIZATION: FROZEN`,
`PHONE IN DUPLICATE KEY: AUTHORITY_GAP`, `CONTACTS IDENTITY PROJECTION: ADMITTED`,
`MINIMUM EXECUTABLE PATH: FROZEN - qualifyLeadForNurture`, and `IMPLEMENTATION ORDER: FROZEN`. No
runtime behavior was implemented or verified by this amendment.

## Contacts Lead qualification participant implementation authority

Implemented 2026-09-03. This slice implements steps 1 and 2 of the frozen implementation order in
`lead-contact-qualification-authority.md` section 10.4: the Contacts identity projection and the
Contacts-owned conversion participant. It introduces **no public Contact mutation surface**.
`createContact` and `updateContact` remain `BLOCKED` and route-less, the public Contacts surface
remains exactly `listContacts` and `getContact`, and the three `qualifyLeadFor*` operations remain
`ADMITTED_NOT_IMPLEMENTED` because their coordinator does not exist yet.

### Internal boundary

Contacts exposes one narrow internal application boundary, `IContactQualificationParticipant`, in
`Contacts/Contracts`. It is a public C# type because the Workflows coordinator is a different
assembly; it is not public HTTP and maps no route. It accepts the trusted Workspace context, the
caller-declared mode, `SelectedContactId` or the four caller-supplied Contact facts, the resolved
`OwnerId`, a coordinator-supplied conversion key, and the request/correlation identifiers. It returns
only `CREATED | LINKED | REPLAYED | REJECTED` plus the authoritative `contactId` and version. It
exposes no `ContactsDbContext`, no persistence type and no Contact document.

`EXISTING` validates and returns; it writes nothing. The `contact` object the wire schema requires
even in `EXISTING` mode is ignored for identity and is deliberately never applied, because applying
it would be the BLOCKED `updateContact` mutation under another name. `NEW` creates. Neither mode ever
becomes the other.

Authorization follows the frozen split. `EXISTING` requires `contacts.read`, evaluated through the
canonical `IRecordAccessEvaluator` at this owner's boundary, followed by the record decision using
the existing Contacts fact provider; ownership and record-access semantics are unchanged. `NEW`
requires no `contacts.create` - it is BLOCKED, ungrantable and absent from
`InitialWorkspaceAccessPolicy`, so requiring it would make positive qualification permanently
unreachable; the Contact is a server-owned consequence of an authorized qualification. The
participant additionally fails closed if an ambient trusted Workspace is resolved and disagrees with
the one the coordinator supplied.

Every `EXISTING` failure - unknown identifier, foreign Workspace, record-scope denial, missing
capability - collapses into one indistinguishable rejection carrying no identifier and no version,
so the boundary is never an existence oracle. The `ContactQualificationRejection` enum is diagnostic
only and is documented as never projectable onto the wire: all values map to the single admitted
public error `LEAD_QUALIFICATION_RELATIONSHIP_INVALID`.

### Persistence

`contacts.Contacts` gains two nullable derived projections, `NormalizedWorkEmail` and
`NormalizedPersonalEmail` (`nvarchar(320)`), computed by the frozen rule
`value.Trim().ToUpperInvariant()` - the same normalization IdentityAuth already uses as its account
uniqueness key. They are derived state kept in step with the profile, never independent facts, and
are never projected onto the wire: `ContactDocument` is unchanged. This is the same reason and the
same rule as `Lead.ScopeOwnerId`. Two non-unique Workspace-scoped detection indexes are added. **No
UNIQUE constraint is created**, as frozen.

Three Contacts-owned tables are added: `contacts.AuditRecords` (immutable command evidence, distinct
from the existing `contacts.ReadAuditRecords`), `contacts.OutboxMessages`, and
`contacts.ConversionRecords`, the owner-local map from a conversion key to the Contact this owner
produced for it. The migration backfills the two projections for pre-existing rows through
`JSON_VALUE`; T-SQL `UPPER()` is collation-sensitive while the runtime rule is `ToUpperInvariant()`,
which is recorded in the migration as an accepted one-time difference for rows written before the
projection existed.

### Duplicate guard and concurrency

For `NEW`, replay lookup, duplicate scan and insert run inside one Contacts-owned `SERIALIZABLE`
transaction. Replay is evaluated first, so a re-drive of a conversion this owner already completed
returns the same Contact instead of being rejected as a duplicate of itself.

The guard compares the normalized candidate against the union of `NormalizedWorkEmail` and
`NormalizedPersonalEmail` for the trusted Workspace, and applies **no record-scope predicate**:
uniqueness is a Workspace fact, and a scope-filtered scan would let an `OWN`-scoped member create
exactly the duplicate the guard exists to prevent. It is implemented as two single-column seeks
rather than one `OR`, and both always execute, so both key-range locks are always taken; an `OR`
could be satisfied by a scan and would make the locking behaviour depend on the optimizer.
Zero matches create; one or more reject. The persistence contract returns only a boolean, so no
identifier, field value or cardinality of an unreadable Contact can reach the caller.

Concurrency is closed by the SERIALIZABLE range lock rather than a constraint. Two concurrent creates
of the same address contend on the same key range; SQL Server resolves the lock-upgrade cycle by
choosing a deadlock victim, which committed nothing. That victim re-drives once, observes the
winner's committed row, and rejects cleanly. The retry is bounded at one attempt and a deadlock is
never itself read as proof of a duplicate - key-range locks cover gaps, so adjacent keys can contend -
which is why the decision is left to the re-drive rather than inferred from the exception.

### Verification

`scripts/verify-contact-qualification-participant.ps1` with
`scripts/ContactQualificationParticipantVerifier` applies the real Contacts and AccessControl
migrations to an isolated LocalDB database, provisions real initial Workspace access through the
production `IInitialWorkspaceAccessProvisioning` contract, and drives the internal boundary through
production DI. It reported `PASS=75 FAIL=0`.

It proves: the physical model including both projections, both detection indexes, the absence of any
non-primary unique index, and all three new tables; `EXISTING` links only within the trusted
Workspace and mutates neither name nor version and writes neither audit nor outbox; unknown and
foreign-Workspace identifiers are byte-identical rejections disclosing nothing; `NEW` creates exactly
one Contact with `fullName` from the trimmed `displayName`, `workEmail` from `email`, `mobilePhone`
from `phone`, `jobTitle` from `title`, `ownerId` from the Lead owner and `status` `active`, with
consent, do-not-contact and source absent; the normalized projection uses the frozen rule; an exact
normalized duplicate blocks `NEW` and commits nothing; a duplicate owned by another member blocks
identically and indistinguishably; a `personalEmail` match blocks a `workEmail` candidate; a
plus-addressed variant is **not** treated as a duplicate; two Contacts without an email do not
collide; the same address in another Workspace does not block; replay is deterministic across three
calls with no second Contact, audit or outbox message, while a different conversion key does not
replay; two concurrent `NEW` conversions of one address yield exactly one commit, one clean rejection,
one row and no escaped concurrency failure; and a static scan proves no HTTP mutation verb, no
foreign DbContext and no Workflows reference inside the Contacts owner, with exactly the two admitted
`MapGet` reads. The wrapper additionally proves no pending Contacts EF model change and no public
Contact mutation route.

Regressions on 2026-09-03: `verify-contacts-read-core.ps1` `passed=67 failed=0`, unchanged from its
recorded figure; `verify-access-control-record-access.ps1` `PASS=404 FAIL=0`;
`verify-inbound-lead-webhook.ps1` passed; the full solution builds with zero warnings and zero errors.

### Pre-existing verifier drift observed, not introduced

`verify-contacts-read-core.ps1` failed before this change for a reason unrelated to it:
`RoleDataScopePolicy` and `RoleFieldSecurityPolicy` gained a required `WorkspaceId` with composite
foreign keys to `access.Roles(RoleId, WorkspaceId)`, and the script's three fixture inserts predate
that model change. Its inserts were corrected to supply `WorkspaceId`; no assertion was changed,
removed or weakened, and the suite returns its original `67/0`.

The same drift remains in the fixture inserts of `verify-customers-read-core.ps1`,
`verify-orders-read-core.ps1`, `verify-organizations-read-core.ps1`, `verify-quotes-read-core.ps1`,
`verify-payments-read-core.ps1`, `verify-payment-intents-read-core.ps1`,
`verify-payment-records-read-core.ps1`, `verify-payments-read-audit.ps1`,
`verify-invoices-read-core.ps1` and `verify-invoices-read-audit.ps1`. Those owners are outside this
slice and were not modified.

The previously recorded `verify-initial-workspace-provisioning.ps1` configuration-reader drift is
superseded by `PLAT-QA-01`: the verifier now removes only full-line comments admitted by .NET JSON
configuration before passing the remaining JSON to Windows PowerShell 5.1 `ConvertFrom-Json`. The
assertions and provisioning semantics are unchanged, and that verifier is now part of the required
Platform CI gate. The unrelated business-owner fixture drifts listed above remain outside this slice.

Therefore `CONTACTS IDENTITY PROJECTION: PASS`, `CONTACT QUALIFICATION PARTICIPANT: PASS`,
`CONTACT DUPLICATE GUARD: PASS`, `CONTACT CONVERSION CONCURRENCY: PASS`,
`CONTACT PARTICIPANT REPLAY: PASS`, and `PUBLIC CONTACT MUTATIONS: NONE / STILL BLOCKED`. This is
task-specific executable evidence, not a Control 1.2 independent-review attestation or a release
freeze.

## NURTURE Lead Qualification workflow core implementation authority

Implemented 2026-09-03. This slice implements steps 3, 4 and 5 of the frozen implementation order in
`lead-contact-qualification-authority.md` section 10.4: the Leads terminal-qualification state and
close participant, the Tasks conversion participant, and the Workflows NURTURE coordinator with its
durable anchor. It reopens none of the frozen decisions.

**No public qualification route exists.** `qualifyLeadForNurture`, `qualifyLeadForOpportunity` and
`qualifyLeadForDirectSale` remain `ADMITTED_NOT_IMPLEMENTED` as public operations, the retired
generic `qualifyLead` still has no route, and public exposure remains blocked by gate `G-1`, the
consent-transfer decision. The workflow is reachable only through an internal application boundary.

### Leads

`LeadQualificationOutcome` gains `Nurture`. `Opportunity` and `DirectSale` are deliberately **not**
added: their workflows have no implemented downstream participant, and an unreachable member would
misrepresent what this owner can produce. `Lead` gains the scalar `RelationshipType` /
`RelationshipId` pair projected as the adopted `LeadDocument.relationshipRef`; they create no EF
navigation, no foreign key and no Contacts persistence access. **No `QualifiedAt`, `QualifiedBy` or
`ContactId` column was added**, as frozen - qualification time and actor stay authoritative in the
Leads command audit record, and the verifier asserts the absence of all three columns.

`Lead.QualifyForNurture` is the terminal transition: it requires `VERIFYING` and re-evaluates
`HasProgressiveProfile()` rather than inferring completeness from the work state, because
`replaceLeadProfile` can leave a `VERIFYING` Lead incomplete. It is terminal - `Lead.Reopen` still
admits only a DISQUALIFIED closed Lead - so a positively qualified Lead can never be reopened.

`ILeadQualificationParticipant` exposes two operations. `PrepareAsync` validates every frozen
precondition - `leads.qualify`, the record decision, existence, exact `If-Match`, `VERIFYING` and the
progressive profile - and mutates nothing; it exists so an unknown, foreign, scope-denied, stale or
incomplete Lead can never leave a Contact behind. `CloseForNurtureAsync` runs through the ordinary
`LeadMutationExecution`, so the owner's idempotency, record guard, `If-Match` check, immutable
command audit and atomic outbox staging apply unchanged; the resolved `contactId` is part of the
command fingerprint, so replaying a key against a different relationship is a genuine idempotency
conflict rather than a silent re-point. The event type is `LEAD_QUALIFIED_FOR_NURTURE`.
`relationshipRef` is added to the Leads field-security vocabulary and is now projected.

### Tasks

`ILeadQualificationTaskParticipant` accepts only the facts the frozen NURTURE contract carries and
composes the follow-up Task itself; it is not a generic Task creation gateway and Tasks authority is
not broadened. It delegates to the existing `createTask` execution, so `tasks.create` is enforced at
the Tasks application boundary and Tasks' own validation, idempotency, audit and outbox are reused
rather than re-implemented. `revisitAt` becomes the due date; the Contact and the source Lead are
carried as the scalar `relationshipRef` and `sourceRef` the public `createTask` contract already
admits, so no new Task schema was needed. The NURTURE `reason` is recorded by
`DEC-LEAD-NURTURE-REASON-HOME`, below, and supersedes the earlier statement that an over-long reason
is truncated to the Task title limit.

### Workflows

Workflows gains its first persistence: `WorkflowsDbContext` over the `workflow` logical schema,
holding only `workflow.LeadQualificationAnchors`. It holds no business state of any owner, and the
assembly opens no foreign `DbContext` and no foreign Domain or Infrastructure type - proven by a
static scan in the verifier.

The anchor's primary key **is** the frozen workflow identity: a hash of the trusted `WorkspaceId`,
the workflow operation, the `leadId` and the caller's `Idempotency-Key`. Two concurrent requests
carrying the same key therefore contend on the insert rather than both starting an execution. It
retains the effective-intent `Fingerprint`, the `ExpectedLeadVersion`, a forward-only `Stage`
(`Started -> ContactResolved -> TaskCreated -> Completed`), and the resolved `ContactId`, `TaskId`
and terminal `LeadVersion`. A stage is entered only after the participant commit it names has
actually committed, so an interrupted coordinator leaves a stage that is true rather than optimistic.
The anchor is Workflows-owned coordination state and is explicitly **not** the Contacts participant's
conversion record: Contacts owns its own replay state for its own aggregate and neither substitutes
for the other.

Progression is: validate and authorize the Lead gate, resolve the Contact, persist the anchor with
the resolved `contactId`, create the follow-up Task, persist the `taskId`, close the Lead, mark the
anchor complete. The order is forced - closing the Lead first would commit a Lead whose
`relationshipRef` points at a Contact that does not exist, violating the frozen lifecycle invariant,
so Contact-first is the only order in which every individually committed state is independently
valid. Intent is compared before anything runs, so replaying a key with a changed intent can never
resolve a second Contact or create a second Task. A completed workflow is answered from the anchor,
never from Lead state, because after a successful close the Lead is legitimately CLOSED and would
fail its own precondition.

The Contact conversion key and the Task idempotency key are both derived from the anchor identity, so
each participant's own idempotency converges independently of whether the anchor update survived.
There is **no distributed transaction and no compensation**: a committed Contact or Task is never
deleted, and recovery only moves the anchor forward. Concurrent duplicates that lose a deadlock
committed nothing in the step they lost, so the coordinator re-drives up to three times and resumes
from durable state; exhaustion returns the admitted `INTERNAL_ERROR`. Every reported error uses an
already-declared code: no new code was minted.

### Verification

`scripts/verify-lead-nurture-qualification.ps1` with `scripts/LeadNurtureQualificationVerifier`
applies the real Workspace, AccessControl, Leads, Contacts, Tasks and Workflows migrations to an
isolated LocalDB database, seeds real Workspace and active-membership rows, provisions real access
through the production `IInitialWorkspaceAccessProvisioning` contract, and drives the coordinator
through production DI. It reported `PASS=125 FAIL=0`.

Proven: the anchor physical model and that its primary key is the workflow identity; VERIFYING + NEW
contact yields one Contact owned by the Lead owner, one Task, and a Lead that is CLOSED / NURTURE /
`relationshipRef` CONTACT with exactly one version advance, one command audit and one
`LEAD_QUALIFIED_FOR_NURTURE` outbox message; VERIFYING + EXISTING contact creates no Contact, does not
advance the Contact version or change its name, and still produces one Task and a closed Lead;
replay across three executions returns `REPLAYED` with the same Contact, Task and Lead version and
creates no second anything; recovery when the Contact committed but no anchor was ever written adopts
that Contact through the conversion key; recovery when Task and close committed but completion was not
recorded converges through the Leads idempotency record; recovery when the anchor holds a Contact but
the Task was refused converges once the capability is restored; changed intent under the same key is
`IDEMPOTENCY_KEY_REUSED` and creates nothing; a stale `If-Match` is `VERSION_CONFLICT` and creates no
Contact or Task; a NEW-state Lead and a profile-incomplete VERIFYING Lead are both
`LEAD_INVALID_TRANSITION` with nothing created; unknown and foreign-Workspace Leads are
`RESOURCE_NOT_FOUND` and byte-identical to each other; a duplicate address and an unresolvable
`selectedId` both collapse to `LEAD_QUALIFICATION_RELATIONSHIP_INVALID` and are indistinguishable;
missing `tasks.create` returns `LEAD_QUALIFICATION_DOWNSTREAM_CAPABILITY_REQUIRED` while the
already-committed Contact survives undeleted and the Lead stays open; the same address in another
Workspace qualifies independently; three concurrent duplicates produce one Contact, one Task, one
anchor and one Lead version advance with every caller agreeing on the identifiers; no `deals`,
`customers`, `quotes` or `orders` row is written; and static scans prove Workflows touches no foreign
persistence type and that no lead-qualification or generic qualify route is mapped anywhere.

One defect was found and fixed by this verification: under three concurrent duplicates a SQL deadlock
escaped the coordinator as an unhandled `DbUpdateException`. The bounded contention re-drive
described above was added in response, and the concurrency case then converged.

Regressions on 2026-09-03: `verify-contacts-read-core.ps1` `passed=67 failed=0`;
`verify-contact-qualification-participant.ps1` `PASS=75 FAIL=0`;
`verify-access-control-record-access.ps1` `PASS=404 FAIL=0`; `verify-inbound-lead-webhook.ps1`
passed; the full solution builds with zero warnings and zero errors; and
`dotnet ef migrations has-pending-model-changes` reports none for `WorkflowsDbContext`,
`LeadsDbContext` or `ContactsDbContext`. No existing assertion was changed, removed or weakened.

Therefore `LEADS TERMINAL QUALIFICATION STATE: PASS`, `LEAD QUALIFICATION PARTICIPANT: PASS`,
`TASKS NURTURE PARTICIPANT: PASS`, `WORKFLOW DURABLE ANCHOR: PASS`,
`NURTURE COORDINATOR CONVERGENCE: PASS`, `CRASH RECOVERY: PASS`, `CONCURRENCY: PASS`,
`PUBLIC QUALIFICATION ROUTE: NONE`, and `G-1 CONSENT TRANSFER: STILL BLOCKED`. This is task-specific
executable evidence, not a Control 1.2 independent-review attestation or a release freeze.

## Consent transfer closure and NURTURE qualification public exposure

Implemented 2026-09-03. Phase A closed gate `G-1` as `DEC-LEAD-CONTACT-CONSENT-TRANSFER`, recorded in
`lead-contact-qualification-authority.md` section 11. Phase B exposed the admitted operation
`qualifyLeadForNurture` at `POST /workflows/lead-qualification/{leadId}/nurture`. The workflow core
was not redesigned.

### Consent authority

The implemented Lead carries exactly three communication facts: `DoNotCall`, `DoNotEmail` and
`PreferredChannel`. `LeadProfile` has no consent ledger, no `DoNotSms` and no `DoNotZalo`, and
`recordLeadConsent` has no implementation anywhere, so there is no consent ledger and no SMS or Zalo
restriction to transfer - not by rule, but because no such value exists. The channel and decision
vocabularies are in fact **identical** across the two owners (`CALL | EMAIL | SMS | ZALO` and
`GRANTED | DENIED | WITHDRAWN | UNKNOWN`), so a ledger transfer would be expressible; it is not
performed only because the source is never populated.

**The frozen rule, for `mode=NEW` only:** `Lead.doNotCall == true` writes `Contact.doNotCall = true`
and `Lead.doNotEmail == true` writes `Contact.doNotEmail = true`. A `false` or absent flag **omits**
the Contact field rather than writing `false`, because `doNotEmail: false` asserts that this person
may be emailed and deriving that from a Lead-stage default would fabricate affirmative consent.
Absence is unknown. Only a restriction is ever written, so qualification is monotone by construction
and has no expressible way to widen a permission. `preferredChannel`, `doNotSms`, `doNotZalo`,
`doNotContact`, `doNotContactReason`, `lawfulBasis` and the whole consent ledger are not transferred,
and no ledger entry is synthesized - a `CommunicationConsentLedgerEntry` would have to assert a
channel, a decision, a source and an instant for a consent event that never occurred. Provenance is
the existing Leads and Contacts command audit records, correlated by `correlationId`.

**`mode=EXISTING` mutates the Contact in no way at all** - no consent field, no restriction, no
version, no `updatedAt`. `updateContact` is BLOCKED and no consent-mutation contract is admitted.

`G-1` is closed. The narrower residual **`R-3`** replaces it and does not block exposure: a
restriction newly learned at Lead stage does not propagate onto an existing Contact, because writing
it would be an unadmitted Contact mutation. That is a non-propagation, not a weakening - the existing
Contact keeps every restriction it already held - so the security invariant holds.

### Public operation

`POST /workflows/lead-qualification/{leadId}/nurture`, `operationId` `qualifyLeadForNurture`, mapped
by Workflows with `RequireAuthorization()` and `RequireTrustedWorkspace()`. It conforms to the pinned
contract exactly: the adopted `QualifyLeadNurtureRequest` in, a single 200
`LeadQualificationWorkflowResponse` out, `X-Workspace-Id`, `X-Request-Id`, `X-Correlation-Id`,
`Idempotency-Key` and `If-Match` all required, and only already-declared error codes. No wire field,
success shape, error code or header was added. `qualifyLeadForOpportunity`,
`qualifyLeadForDirectSale` and the retired generic `qualifyLead` remain unmapped, and `createContact`
and `updateContact` remain BLOCKED.

The endpoint is a thin transport adapter. It parses headers and the body, maps the closed
relationship vocabulary, and delegates; it takes no precondition, authorization, idempotency,
concurrency or convergence decision, so no second authority is created. Both `COMMITTED` and
`REPLAYED` are 200, with the distinction carried in the response body as the contract declares. An
unadmitted `kind` - including `ORGANIZATION_ACCOUNT`, whose owner has no admitted mutation contract -
is a clean `LEAD_QUALIFICATION_RELATIONSHIP_INVALID` rather than a parse failure.

Everything else is reuse: authentication, trusted Workspace resolution, `leads.qualify`, Lead record
access, the caller's `Idempotency-Key` and `If-Match`, the workflow durable anchor, and the Contacts,
Tasks and Leads participants, all unchanged.

### Participant changes

Minimal and consent-driven. `LeadQualificationPreparation` now returns the Lead's two restriction
flags, because only Leads may read Lead state; the coordinator forwards them and never interprets
them. `ResolveQualificationContactCommand` takes them as server-derived parameters kept deliberately
separate from the caller-supplied contact content, and Contacts writes only `true`.
`LeadQualificationClosure` now surfaces the Leads command identity, instant and evidence identifiers
so the wire response reports what that owner actually committed rather than anything the coordinator
composed; `ResolveQualificationContactResult` returns the Contact display name and
`LeadNurtureTaskResult` the Task version, both required by the wire result. The anchor gained a
`ResponseJson` column so a replay returns the original response verbatim with only its outcome
relabelled, rather than one recomposed from partial state. The request-level `ownerId` assigns the
follow-up Task; the Contact's record owner is always the Lead owner.

### Verification

`scripts/verify-lead-nurture-qualification-api.ps1` drives the real route over HTTP against a real
ApiHost and an isolated database, building Lead fixtures through the public Leads API and Contact
fixtures with controlled SQL. It reported `passed=95 failed=0`.

Proven end to end: unauthenticated refusal; NEW qualification returning 200 COMMITTED with the Lead as
`aggregateId`, `NURTURE`, a CONTACT `relationshipRef`, both created resources, a real `commandId` and
an emitted Lead event; exactly one Contact, one NURTURE Task and one Lead terminal transition; the
qualified Lead reading back CLOSED/NURTURE with `relationshipRef` and with **no** `contactId`,
`qualifiedAt` or `qualifiedBy` field on the wire; `doNotCall` and `doNotEmail` transferring for a
restricted Lead while no consent ledger, `doNotSms`, `doNotZalo` or `preferredContactChannel` is
invented; an unrestricted Lead leaving both flags unset rather than `false`; EXISTING linking without
creating a Contact and leaving the stored Contact byte-identical; replay returning REPLAYED with the
same Contact and Task and no second Task or version advance; changed intent under the same key as
`IDEMPOTENCY_KEY_REUSED`; a missing `Idempotency-Key` refused; stale `If-Match` as `VERSION_CONFLICT`
with nothing created; a NEW-state Lead and a profile-incomplete VERIFYING Lead both
`LEAD_INVALID_TRANSITION` with nothing created; a duplicate address refused without disclosing any
Contact identity; unknown and foreign-Workspace Leads indistinguishable at 404; a lost completion
resuming over the public route and converging on the same Contact and Task; missing `leads.qualify`
denied with nothing created; missing `tasks.create` returning
`LEAD_QUALIFICATION_DOWNSTREAM_CAPABILITY_REQUIRED` with the committed Contact undeleted and the Lead
still open, then converging on the same Contact once restored; no `deals`, `customers`, `quotes`,
`orders` or `products` row written; and the sibling qualification operations, the retired generic
operation and `createContact` all still unexposed.

Regressions on 2026-09-03: `verify-lead-nurture-qualification.ps1` `PASS=128 FAIL=0`;
`verify-contact-qualification-participant.ps1` `PASS=75 FAIL=0`; `verify-contacts-read-core.ps1`
`passed=67 failed=0`; `verify-access-control-record-access.ps1` `PASS=404 FAIL=0`;
`verify-inbound-lead-webhook.ps1` passed; the full solution builds with zero warnings and zero
errors.

One assertion in the internal workflow verifier was **strengthened**, not weakened. It previously
asserted that no lead-qualification route existed anywhere, which encoded the pre-exposure state. It
now asserts that exactly one such route exists, that it is the nurture route, and that the
opportunity, direct-sale and generic-qualify routes are all still absent.

Therefore `CONSENT TRANSFER: FROZEN`, `G-1: CLOSED`,
`NURTURE PUBLIC QUALIFICATION: IMPLEMENTED_AND_RUNTIME_VERIFIED`,
`qualifyLeadForNurture: ADMITTED_IMPLEMENTED`,
`qualifyLeadForOpportunity / qualifyLeadForDirectSale: ADMITTED_NOT_IMPLEMENTED`, and
`createContact / updateContact: BLOCKED`. This is task-specific executable evidence, not a Control
1.2 independent-review attestation or a release freeze.

## Lead interested products implementation authority

Implemented 2026-09-03. This slice implements the runtime admitted by
`DEC-PRODUCTS-LEAD-INTERESTED-PRODUCT-SNAPSHOT` (`products-lead-snapshot-authority.md`). Non-empty
`interestedProducts` is now accepted on `createLead` and `replaceLeadProfile`; the previous
fail-closed branch is gone. No wire contract changed, and no Lead qualification, Customer, Deal,
Quote, Order or AI behaviour changed.

### The Products-owned reader

Products exposes `IProductSnapshotReader` in `Products/Contracts`. It is public C# because Leads is a
different assembly; it maps no route and widens no public Products surface. It takes a set of
identifiers and returns, per identifier, `Resolved` with the frozen six-field projection - `productId`,
`name`, `sku`, `productType`, `status`, `version` - or `NotResolvable`, or `NotEligible`. It returns
no `ProductDocument` and no price, tax, billing, description, category, unit, tag or archive fact.

Products decides eligibility, not Leads: only `ACTIVE` is capturable, reusing the frozen availability
predicate. Leads never inspects a Product status and never opens `ProductsDbContext`.

`products.read` is evaluated once at the Products application boundary through the canonical
evaluator. A refusal returns no entry at all, so a denied consumer learns nothing about any Product -
not even whether the identifiers it supplied exist. The record scope is applied once as a set-level
decision with zero per-row evaluations; because Products has no member-owner fact, a restrictive
scope resolves uniformly to "no Product visible" and every identifier becomes indistinguishably
unresolvable. The backing read is one batch query whose trusted-Workspace predicate is in the query
itself, so a foreign-Workspace Product is never materialised and unknown and foreign are the same
outcome by construction rather than by a comparison that could drift.

### Leads

`LeadInterestedProduct` gains `SkuSnapshot`, `ProductTypeSnapshot` and `ProductVersionSnapshot`. The
first two populate the already-declared optional `skuSnapshot` and `productTypeSnapshot` wire fields;
the version is **persisted but never projected**, because `LeadInterestedProductReadModel` is
`additionalProperties: false` and declares no version field. No migration was needed - the Lead
profile is persisted as JSON. No Product price, tax or billing fact is persisted.

`LeadValidation.TryProfile` now returns caller **intents** rather than snapshots, and validates
structure only: entity-id shape, interest level, quantity range, note length, budget shape, the
500-entry cap, and duplicate `productId` rejection. Duplicates are refused because `productId` is the
entry identity that decides preserve-versus-recapture on replace, so a duplicate would make that rule
non-deterministic. Product existence and eligibility are left to Products.

**Create.** After the idempotency replay branch, the interactive admission captures every entry
through the reader, then the field-write guard and the active-member check run against the captured
profile. One unresolvable or ineligible entry fails the whole command and writes no Lead: there is no
partial commit and no silently dropped entry.

**Replace.** The submitted collection is the desired state. An identifier the Lead already carries
keeps its captured snapshot - same entry id, name, SKU, type, version and `createdAt` - and takes only
the caller's own `interestLevel`, `estimatedQuantity`, `expectedBudget` and `note`. An identifier the
Lead did not carry is a fresh capture at the current Product version with a new entry id. An
identifier no longer submitted is dropped. A retained entry is never revalidated, so archiving a
Product after capture cannot make an unrelated Lead edit fail, and no captured name is ever silently
refreshed. Resolution runs in the command's own precondition hook, inside its serializable
transaction and after its replay branch.

**Delegated inbound ingress stays fail-closed.** `LeadCreateAdmission` now carries the capture
decision, because capture reads Products-owned facts and is therefore an authorization question. The
interactive model captures normally; the delegated Integrations ingress refuses a non-empty
collection, since its admitted authorization concern is exactly one delegated `leads.create`
evaluation and no delegated `products.read` is admitted for that path. An empty or omitted collection
is unaffected, so webhook behaviour is unchanged.

### Idempotency and concurrency

The command fingerprint covers the caller's **intents**, never the resolved snapshots. Binding
snapshots into the key would make a replay after a Product rename compute a different fingerprint and
conflict against its own original command - the same reason the frozen Products rule excludes current
database state from create/replace fingerprints. A replay is therefore answered from stored evidence
alone and calls Products not at all, so a Product renamed or archived after commit cannot turn a
replay into a failure. A changed interested-product intent under the same key still returns
`IDEMPOTENCY_KEY_REUSED`. `If-Match`, audit, outbox and Workspace isolation are untouched, and no
cross-DbContext transaction exists: Products reads in its own pass and Leads commits in its own
owner-local transaction.

### Errors and disclosure

Only the already-admitted `VALIDATION_FAILED` (422) and `ACCESS_DENIED` (403) are used; no error code
was minted. Unknown, foreign-Workspace and structurally invalid identifiers produce one identical
message. `NotEligible` produces a different message, which discloses nothing new because the caller
holds `products.read` and could read the status through `getProduct` anyway. Field errors are indexed
to the caller's own entry, which reveals only what the caller sent.

### Verification

`scripts/verify-lead-interested-products.ps1` drives Lead create and replace over HTTP against a real
ApiHost and an isolated database, using real Products created through the public Products API. It
reported `passed=52 failed=0`.

Proven: create with one and with several interested Products, with name, SKU and type snapshots and
the caller's own fields preserved; the capture version persisted owner-locally and absent from the
wire; no Product price, tax, billing or cycle fact anywhere in the Lead response; duplicate
`productId` rejected naming the offending entry; unknown Product rejected disclosing no Product fact;
a real foreign-Workspace Product rejected byte-identically to an unknown one; an archived Product
rejected as ineligible, with a message distinct from unresolvable; a mixed-validity batch rejected
all-or-nothing with no Lead written by any rejection path; missing `products.read` denied with no Lead
written and no Product fact disclosed, while a Lead without interested products still succeeds without
that capability; replace removing an omitted Product; replace preserving a retained entry's id and
name while taking its new interest level, quantity and note; a Product rename after capture leaving
both the stored Lead and the original create response unchanged; an archived retained Product not
blocking an unrelated Lead edit; a re-added Product capturing a fresh snapshot with a new entry id; a
newly added archived Product refused; replay after a Product rename returning the original snapshot
with `REPLAYED` and no second Lead; changed interested-product intent under the same key returning
`IDEMPOTENCY_KEY_REUSED`; stale `If-Match` still `412`; and no Lead operation - create, rejection,
replay, conflict or stale command - moving the Products command-audit count.

Regressions on 2026-09-03: `verify-products-core.ps1` passed; `verify-lead-nurture-qualification.ps1`
`PASS=128 FAIL=0`; `verify-access-control-record-access.ps1` `PASS=404 FAIL=0`;
`verify-inbound-lead-webhook.ps1` passed; the full solution builds with zero warnings and zero errors.

Therefore `LEAD INTERESTED PRODUCTS: PASS`, `PRODUCTS SNAPSHOT READER: PASS`,
`LEAD SNAPSHOT IMMUTABILITY: PASS`, `REPLACE PRESERVE SEMANTICS: PASS`,
`COMMERCIAL PRODUCT FACTS: NOT INCLUDED`, and `AG-PRODUCT-SNAPSHOT (Leads leg): IMPLEMENTED`. The
Deals, Quotes and Orders legs remain fail-closed and unchanged. This is task-specific executable
evidence, not a Control 1.2 independent-review attestation or a release freeze.

## Lead qualification contract closure - Contact name bound and complete request validation

Closes the last two defects the independent Lead final review raised against the NURTURE
qualification operation. Authority: `DEC-LEAD-CONTACT-NAME-BOUND`
(`lead-contact-qualification-authority.md` section 12) and `DEC-LEAD-CONTACT-DUPLICATE-POLICY`
section 9.4. No route, operation, capability, error code or admission row was added, and Task 8A
authorization ordering and Task 8B recovery semantics are unchanged.

### G1 - the Contact canonical name bound

`LeadQualificationContactInput.displayName` declared `maxLength` 256 while `ContactDocument.fullName`
and its read-only `displayName` projection, both BLOCKED Contacts mutation requests, and the
`contacts.Contacts.FullName` column all declared 200. The frozen transfer stores the qualification
display name **verbatim after trimming** into `Contact.fullName`, so a 201-256 character input had no
lossless destination; the runtime already refused it, as a relationship error, by a bound the public
contract never published.

**Frozen: the qualification input adopts the Contact bound - `minLength` 1, `maxLength` 200.**
Widening Contact to 256 was not available: `fullName` is a *required* field of the response schema of
the implemented, runtime-verified `getContact` and `listContacts`, so a longer name would make those
operations emit contract-invalid responses, and closing that would mean rewriting a Contacts-owned
schema, both BLOCKED Contacts requests and Contacts persistence for a capacity no authority asks for.
Exactly one rule leaves every existing frozen statement true, so this is a derivation rather than a
preference. `LeadQualificationOrganizationInput.displayName` stays at 256; the ORGANIZATION_ACCOUNT
leg remains out of scope and BLOCKED.

The live contract was amended through the repository generator pipeline
(`npm run api:generate -- --accept-breaking-baseline`), which rewrote the contract hash and every
derived artifact and recorded the tightening in the reviewed breaking-change baseline;
`quality.api-contract` passes on the result. The contract SHA-256 is now
`d98462853a5c529ce1695978d35541a8bc000dc25b2781a62fd8bf5e91cd6a57`.
`design-authority/contracts/openapi.json` is deliberately unedited: `contract-authority.md` declares
it dated `PINNED_FRONTEND_WIRE_EVIDENCE`, already one amendment behind, and rewriting provenance to
match a later decision would destroy the evidence it exists to preserve.

The number now has one home per owner. `ContactNameBound.MaxLength` is the Contacts-owned constant
used by both the qualification participant and the EF column configuration;
`NurtureRequestValidation.DisplayNameMaxLength` is the coordinator's copy of the same published wire
bound. Contacts keeps its own identical last word on its own aggregate, because an owner must not
depend on a coordinator to protect its invariant.

### F6 - the complete request contract, enforced before the first owner mutation

The coordinator is **deterministic convergent and never compensates**: it commits Contact, then Task,
then the Lead close in three owner-local transactions, and a committed Contact is never deleted to
tidy up a later refusal. A field admitted by a partial check could therefore only be refused after a
Contact already existed. The review proved exactly that: a 1001-character `reason` committed, because
the Tasks participant truncates the reason into the follow-up Task title, and an invalid `revisitAt`
failed only at the Tasks boundary, after the Contact.

**Prevention, not compensation.** `NurtureRequestValidation` is the single authority for the adopted
`QualifyLeadNurtureRequest` and runs as the coordinator's first statement, before Workspace
resolution, before the `leads.qualify` gate, before the anchor is read and before any participant is
called. It is purely request-shaped - it reads no Lead, Contact or anchor state - so running it ahead
of the Task 8A gate discloses nothing. It accumulates every field error rather than short-circuiting.

Enforced, all from the pinned schema: `relationship.contact` is required for **both** modes, because
the schema requires it and accepting a contract-invalid body merely because EXISTING ignores that
object would make the wire contract advisory; `relationship.organization` is refused on a CONTACT
relationship, since it is declared only for the unadmitted ORGANIZATION_ACCOUNT kind and discarding it
would silently drop a caller assertion; EXISTING requires a `selectedId` that is a valid `EntityId`,
and NEW requires its **absence**, because NEW asserts the person does not exist and naming an existing
Contact in the same breath is a contradiction the backend may not resolve by picking a limb;
`displayName` 1-200, `email` `maxLength` 320 under the same `MailAddress` round-trip rule Leads
already applies to its own `format: email` fields, `phone` 1-64, `title` 0-160, `reason` 1-1000,
`note` 0-4000, `ownerId` as an `EntityId`, and `revisitAt` as a UTC date-time ending in `Z` under the
identical rule Tasks applies to `dueAt` - so a value that passes here cannot fail there after a
Contact has committed. Bounds are applied to the trimmed value, because the trimmed value is what is
stored. The wire record types remain `JsonUnmappedMemberHandling.Disallow`, so an undeclared member is
still a closed-schema refusal.

To make this checkable the intent record now carries whether the body declared a `contact` object and
whether it declared an `organization` object. Neither fact survives a projection to nullable strings,
and without them the coordinator could not validate the contract it enforces. The transport adapter
stays a mapper: it sets the two flags and takes no decision. The idempotency fingerprint is unchanged
- it enumerates its fields explicitly - so Task 8B intent identity is untouched.

Every structural refusal is `VALIDATION_FAILED` (422) with the exact field pointer, which is the code
already used for this operation's header and relationship-shape refusals and is `fieldLevelApplicable`
in the error catalog. `LEAD_QUALIFICATION_RELATIONSHIP_INVALID` keeps its existing meaning: an
unadmitted `kind`/`mode` vocabulary, and Contacts-owned resolution failures.

### Error mapping - the frozen duplicate pointer

`DEC-LEAD-CONTACT-DUPLICATE-POLICY` section 9.4 freezes `LEAD_QUALIFICATION_RELATIONSHIP_INVALID`,
422, field pointer `relationship.contact.email` for a duplicate address. That pointer had been lost:
every Contacts rejection collapsed into one pointer-less error. It is restored for `DuplicateEmail`
only; every other rejection stays pointer-less, so an unresolvable EXISTING identifier remains
indistinguishable from a record the caller may not read. This discloses nothing new - on a NEW request
the caller already learns duplicate-versus-created from the refusal itself, which is what section 9.1
froze - and it still carries no `contactId`, no match count and no Contact field value. The source
comment that forbade projecting *any* rejection detail was written before section 9.4 and is corrected
in place.

### Authority reconciliation

Two stale statements from the earlier tasks are narrowed, neither reopening an implementation.
`workflow-registry.json` WF-10 said `ADMITTED_NOT_IMPLEMENTED` / `NOT_IMPLEMENTED` for the whole
workflow; it now records `ADMITTED_PARTIALLY_IMPLEMENTED` with a per-operation breakdown -
`qualifyLeadForNurture` implemented, `qualifyLeadForOpportunity` and `qualifyLeadForDirectSale`
not - matching `operation-registry.json`, which was already correct. No generic or commercial
qualification outcome is admitted by that change. `products-lead-snapshot-authority.md` section 7 said
`products.read` was required for *any* command carrying a non-empty `interestedProducts`; it now says
what sections 5.2 and 6.2 already froze and what the code already does - the capability attaches to a
**new capture**, and a retained entry, a removal, a committed replay and an empty list each perform no
Products call and require nothing.

### Verification

`scripts/verify-lead-nurture-qualification-api.ps1` `passed=231 failed=0` and
`scripts/verify-lead-nurture-qualification.ps1` `PASS=334 FAIL=0`, both on 2026-09-03, each against a
real isolated database and, for the API suite, a real ApiHost over the real route.

The maintained regression cases added by this task, each proving refusal **and** that the global
owner-effect snapshot - Contacts, Contact audit, Contact outbox, conversion receipts, Tasks, Task
audit, Task outbox, Lead audit, Lead outbox, workflow anchors, and the summed Lead versions and work
states - is byte-identical afterwards: invalid email; over-limit and empty `reason`; malformed and
non-UTC `revisitAt`; over-limit `note`, `title` and `phone`; over-limit `displayName`; malformed NEW
without a contact object and without a `displayName`; malformed EXISTING without a `selectedId`,
without a contact object, and with a non-identifier `selectedId`; inconsistent NEW carrying a
`selectedId`; an unadmitted `organization` on a CONTACT relationship; an invalid `ownerId`; and an
undeclared request member. The G1 boundary is proven from both sides - a 200-character name qualifies
and is returned and stored **whole**, which is what rules out a silent truncation, and 201 is refused
with `relationship.contact.displayName` and zero effects. The duplicate refusal is proven to carry
`relationship.contact.email` and nothing else, and the unresolvable EXISTING identifier to carry no
pointer at all while staying indistinguishable in status and code. Valid NEW, valid EXISTING and the
completed replay all still succeed.

Nothing was weakened. The one fixture change is the internal verifier's unresolvable-EXISTING case,
which supplied no contact object at all; it now supplies a contract-valid one, so it still proves the
unresolvable identifier rather than a malformed body, which is proven separately.

Task 8A and Task 8B regressions all pass unchanged: 57 of 57 `8A:`/`8B:` assertions in the public
suite, covering current `leads.qualify` before anchor disclosure, byte-for-byte replay and conflict
non-disclosure under revoked capability and under OWN/TEAM/CUSTOM scope, canonical Lead record access,
`TaskOwnerId` in the immutable intent, `If-Match` excluded from the intent fingerprint,
refreshed-token partial recovery, durable participant result adoption, stable Contact and Task
identities and the convergent completed response.

Adjacent regressions on 2026-09-03: `verify-contact-qualification-participant.ps1` passed;
`verify-lead-interested-products.ps1` `passed=52 failed=0`;
`verify-access-control-record-access.ps1`, which hosts the maintained Lead lifecycle and field-write
suite, `PASS=556 FAIL=0`; `verify-inbound-lead-webhook.ps1` passed with no failed check; the
`quality.api-contract` gate PASS on the amended contract (270 operations, 236 ready, 34 blocked); the
full solution builds with zero warnings and zero errors; and `WorkflowsDbContext`, `LeadsDbContext`,
`ContactsDbContext`, `DealsDbContext` and `ProductsDbContext` each report no pending EF model changes.
**No migration was created**: the only persistence touch replaced the literal `200` with the constant
that already equals it, so the model is unchanged.

### Named residuals

**The Task follow-up owner is validated for shape here and for membership at the Tasks boundary.** An
`ownerId` that is a well-formed `EntityId` but is not an active member of the trusted Workspace is
refused by Tasks after the Contact has committed. That is a state-dependent business validation owned
by Tasks, evaluated only on a genuinely new command so a replay cannot be retroactively invalidated,
and re-deciding it in the coordinator would duplicate Tasks' authority over its own aggregate. Task
8B's forward-only recovery already covers exactly this shape - the same partial state the
`tasks.create` capability case produces - and a re-drive converges on the same Contact. Recorded, not
silently accepted.

**`file-inventory.json` remains a dated fingerprint, not a maintained integrity ledger.** Eleven
authority artifacts already diverged from it before this task, including `operation-registry.json` and
`supersession-ledger.json`; `workflow-registry.json`, `lead-contact-qualification-authority.md` and
`products-lead-snapshot-authority.md` now join them. Re-hashing it is a registry-reconciliation task
of its own and is deliberately not done here, on the same reasoning that keeps the pinned OpenAPI
provenance unedited.

Therefore `G1: CLOSED`, `F6: FIXED`, `NURTURE REQUEST CONTRACT: ENFORCED PRE-EFFECT`,
`DUPLICATE FIELD POINTER: RESTORED`, `TASK 8A: NO REGRESSION` and `TASK 8B: NO REGRESSION`. This is
task-specific executable evidence, not a Control 1.2 independent-review attestation or a release
freeze.


## NURTURE final correctness - reason preservation and transient contention semantics

Two correctness defects found by the independent review of the completed NURTURE implementation. Both
are narrow corrections to already-implemented behaviour; no Lead feature is added and the Task 8A,
8B and 8C closures are unchanged.

### FN-1 - `DEC-LEAD-NURTURE-REASON-HOME`: where the qualification reason lives

**The defect.** `QualifyLeadNurtureRequest.reason` is admitted at `minLength` 1 / `maxLength` 1000.
The Tasks participant derived the follow-up Task title from it and `createTask.title` stops at 300, so
every accepted reason of 301-1000 characters was silently cut to 300: the caller received 200, the
Lead closed, and part of an admitted business fact was destroyed with no refusal, no warning and no
other copy. The Lead qualification aggregate retains no reason of its own, so nothing recovered it.

**The bounds do not permit a free choice.** The admitted sinks in `CreateTaskRequest` are `title`
(1-300), `description` (0-4000) and `sourceRef.evidence` (0-1000). The admitted sources are `reason`
(1-1000) and `note` (0-4000). `description` already carries `note` and cannot also hold `reason`:
4000 + 1000 exceeds the one field's declared bound, so composing them would reintroduce the same
silent loss under a different name. `sourceRef.evidence` is bounded at exactly the reason's own 1000
and is already written by this participant, whose `sourceRef` carries the source Lead as declared
provenance evidence.

**Frozen.** The complete accepted `reason` is written to the Lead source reference's
`sourceRef.evidence`; the Task **title** is a *bounded derived summary* of it. Both are already
admitted fields of the pinned `createTask` contract and both are projected by `TaskReadModel`, so:

1. no public contract is widened - `reason` keeps its 1-1000 bound and `title` keeps its 300;
2. no persistence field is invented - `tasks.Tasks.SourceEvidence` is `nvarchar(1000)` since
   `InitialTasks`, and `dotnet ef migrations has-pending-model-changes` reports none;
3. no business fact is duplicated - the reason has exactly one home, and the title is a declared
   derivation of it, not a second copy of the fact;
4. no cross-owner Lead copy is introduced - Leads persists nothing new;
5. `sourceRef` is already one field-security key covering evidence, so no new write is unguarded.

The derivation is a pure function of immutable caller intent, so a re-drive after a lost
acknowledgment composes byte-identical Task input, reaches the Tasks idempotency record and returns
the originally committed Task. The workflow fingerprint is unchanged - it already enumerated
`Reason` - so Task 8B intent identity and the F3/F4/F5 closures are untouched.

This supersedes the earlier recorded statement that "an over-long reason is truncated to the Task
title limit". That statement described a lossy derivation with no canonical basis; the canonical
design authority nowhere authorises discarding an admitted caller fact, and the bound conflict is the
same shape as `G1`, which was resolved by reconciling the contracts rather than by silent truncation.

### FN-2 - transient Contacts contention is not a relationship verdict

**The defect.** Contacts already classifies its own conditions correctly: exhausting its bounded
deadlock retry returns the typed `ContactQualificationRejection.ConcurrentConflict`, deliberately
distinct from `DuplicateEmail`, `ContactNotResolvable` and `InvalidInput`. The coordinator collapsed
*every* unsuccessful resolution into `LEAD_QUALIFICATION_RELATIONSHIP_INVALID` (422), so a transient
database contention was reported to the caller as permanent invalid input - a `retryable: false`
validation verdict on a command that was valid and would have succeeded on a retry. The coordinator's
own bounded contention retry is exception-driven and therefore never observed the condition at all.

**Frozen owner boundary.** Contacts classifies; Workflows recognises only *that* the classification
was transient. The coordinator raises its own `ParticipantContentionException` on that one typed
outcome and feeds it into the single bounded retry it already runs for provider contention. Workflows
parses no SQL error owned by Contacts, and Contacts exposes no provider detail: the exception is
Workflows-owned, carries the participant name only, and is never thrown for a permanent duplicate,
unresolvable or invalid outcome, which stay answered rather than retried.

**Bounded behaviour.** `MaxAttempts` stays 3 in the coordinator and `MaxResolutionAttempts` stays 2
inside Contacts, so a request performs at most six Contact resolutions and terminates. Exhausting the
bound answers with `INTERNAL_ERROR` (500) - the code this operation already returns for exhausted
contention and an admitted member of its `x-error-codes` - never with the 422 relationship refusal.
No new public error code is introduced: of the admitted codes for this operation only `RATE_LIMITED`
and `INTEGRATION_UNAVAILABLE` are catalogued `retryable: true`, and neither is true of this
condition - nothing throttled the caller and Contacts is not an integration. Whether a distinct
retriable public code should be admitted for internal owner contention is a contract question for the
authority owner and is recorded here rather than answered by picking a status.

**Forward-only recovery is preserved.** Contention before a Contact commit leaves the anchor at
`Started` with no `ContactId`, so a retry resolves through the same conversion key and cannot create a
second Contact; Contacts' audit and outbox rows are written exactly once, by the attempt that
committed. A committed Contact is still never deleted or compensated, and the
`Contact -> Task -> Lead close` order is unchanged.

### Error mapping - still distinct

| Condition | Result |
|---|---|
| Duplicate email on NEW | `LEAD_QUALIFICATION_RELATIONSHIP_INVALID` 422, pointer `relationship.contact.email` |
| Unknown / inaccessible EXISTING Contact | `LEAD_QUALIFICATION_RELATIONSHIP_INVALID` 422, no pointer |
| Malformed request | `VALIDATION_FAILED` 422 from F6, before any effect |
| Transient Contacts contention | bounded retry; on exhaustion `INTERNAL_ERROR` 500 |

No Contact identifier, match count, database error or foreign-resource fact appears in any of them.


## Never-invent rule

A missing or conflicting business contract is recorded as `AUTHORITY_GAP`. It is never repaired by convention, frontend behavior, folder names, common CRM behavior, or a speculative abstraction.
