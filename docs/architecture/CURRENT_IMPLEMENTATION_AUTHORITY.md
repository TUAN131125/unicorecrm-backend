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

## Current OpenAPI authority rule

For every operation it declares, `frontend/unicorecrm-web/docs/api/openapi.json` controls the exact current HTTP wire contract. Its currently generated SHA-256 is:

`fd079b2f6e189ffe391d555cee1d2acaa735cf532346cc74a02070862bd78792`

This matches `frontend/unicorecrm-web/docs/api/openapi.sha256`. It supersedes `f3a0273e9d8847b5bcd8c673810e2a9e8d0e70031da12b4dc2a8dd338a2354b6`, which was current until the Support customer-enrichment amendment recorded below. The amendment was applied through the repository generator pipeline (`npm run api:generate`), which rewrote the contract hash and every derived artifact, and the `quality.api-contract` gate passes on the result. The verified file declares 270 operations: 236 contain a 2xx response contract and 34 contain no 2xx response contract. Operations without an admitted success contract remain fail-closed and must not be implemented as callable success paths.

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
- team ownership and team membership: no owner records a team for a record and no module records a member's teams, so the `TEAM` data scope has no semantics and fails closed wherever it is evaluated;
- `CUSTOM` data-scope semantics: `RoleDataScopePolicy.AllowedOwnerIdsJson` exists in the AccessControl model but no authority defines how an allowed-owner list is written or interpreted, so the scope fails closed;
- administrative writes for AccessControl data-scope and field-security policy: the policies are read and enforced, but no admitted operation creates or changes one.

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

The current contracts mark `getWorkspaceAccessDirectory`, `createAccessRole`, `replaceAccessRole`, `archiveAccessRole`, and `replaceWorkspaceMemberAccess` as ready administrative operations. They are intentionally not part of the minimal B03 core implementation and have no callable backend route yet. Their absence is fail-closed and does not authorize inferred behavior.

The following operations remain fail-closed `AUTHORITY_GAP`:

- `inviteWorkspaceMember`, `resendWorkspaceInvitation`, and `revokeWorkspaceInvitation`: invitation intent, token issuance/security lifecycle, target binding, expiry, replay protection, and cross-owner membership mutation are not sufficiently admitted;
- `acceptWorkspaceInvitation`: the same invitation-security gap plus Workspace-owned membership mutation remains unresolved;
- `provisionWorkspaceMember`: no approved atomic Workspace-membership and AccessControl-assignment contract exists;
- `changeWorkspaceMemberStatus`: Workspace owns membership validity, while required IdentityAuth session-revocation coordination is not admitted;
- `rotateManagedMemberPassword`: IdentityAuth owns credentials and no approved administrative credential contract/security semantics exists.

`evaluateEffectiveRecordAccess` is no longer an authority gap. The missing business-owner record-fact contract that blocked it now exists and the operation is implemented; see *AccessControl record access implementation authority* below.

Development bootstrap is configuration-only, Development-only, idempotent, and disabled by default. It creates no public provisioning endpoint and does not define production role names. Runtime verification on 2026-08-23 used isolated LocalDB databases and proved authorized context resolution, denial for an active member without the required capability, rejection of caller-supplied role/capability spoofing, foreign-Workspace isolation, and B01/B02 regressions. Therefore `B03 ACCESS CONTROL FOUNDATION: PASS`; the listed administrative omissions and authority gaps remain unimplemented and must not be invented.

## AccessControl record access implementation authority

AccessControl now admits and implements `evaluateEffectiveRecordAccess`: `POST /access/records/evaluate`. The operation answers one question - what may this membership do with this record, and which of the requested fields may it see or write - and it answers it with AccessControl's own state plus authoritative facts from the record's business owner. It is not an administration surface: no other AccessControl administrative operation is implemented by this work, and every one listed as `AUTHORITY_GAP` above remains fail-closed.

**Authority chain.** `IMPLEMENTED` and `VERIFIED`. The chain is authenticated B01 identity, then B02 trusted Workspace/membership, then the AccessControl-owned `IAccessAuthorizer` for the operation capability `workspace.context.resolve`, then authoritative record facts from the owning module, then record-scope evaluation, then the field-security projection, then immutable AccessControl decision evidence. The trusted Workspace is taken only from `ICurrentWorkspace.Require()`; the response `workspaceId` is that trusted identifier and never a caller-supplied one. The HTTP layer performs transport validation and status mapping only and is not a second permission authority.

**Resource identity.** `IMPLEMENTED`. `resourceKey` is the same vocabulary the `/access/context` `dataScopes[].resourceKey` and `fieldSecurity[].resourceKey` projections already use. It is a request selector, matched case-insensitively against the registered record-fact owners; the response echoes the owner's canonical key, not the caller's spelling. `recordId` is an optional `EntityId`. The request contract carries no Workspace, owner or team member at all and rejects unmapped members, so a caller-supplied `ownerId`, `workspaceId` or `teamId` is a `422 VALIDATION_FAILED` rather than a silently ignored field.

**Record-fact contract.** `IMPLEMENTED`. AccessControl declares `IRecordAccessFactProvider` in its public contracts and a business owner implements it over its own persistence. Input is the trusted Workspace, the record identifier and the request/correlation context; output is `FOUND`/`NOT_FOUND` plus an optional owner member reference. The owner queries only the trusted Workspace, so a record belonging to another Workspace is reported as not found and foreign existence never reaches AccessControl. A provider runs no capability check of its own - AccessControl has already authorized the caller before calling it, and a second check would duplicate both the authority and the decision audit - and writes no business state, no owner audit record and no outbox message, because an authorization evaluation is not a business read. AccessControl holds no foreign `DbContext`, repository, Infrastructure type or EF entity; `UnicoreCRM.Platform` gains no project reference.

Registration is validated at composition: two owners claiming one resource key is an error, not a race. A resource key with no registered owner has no authoritative facts, so it is denied with the reason `RESOURCE_FACT_AUTHORITY_UNAVAILABLE`.

Support is the only registered owner in this scope. `Support` declares resource key `support`, read capability `support.read`, update capability `support.update`, and the command capabilities `support.create`, `support.update`, `support.assign` plus the four transition commands `support.resolve`, `support.close`, `support.reopen` and `support.cancel`, all mapped to `support.update` because `transitionSupportCase` is what enforces them. It declares no delete, export or approval capability, because Support has no admitted operation behind any of them.

**Effective record scope.** `WORKSPACE` and `OWN` are `IMPLEMENTED` and `VERIFIED`; `TEAM` and `CUSTOM` are `AUTHORITY_GAP` and fail closed.

- `WORKSPACE`: any record the owner reports as present in the trusted Workspace is in scope.
- `OWN`: in scope only when the owner member reference equals the caller's trusted `MemberId` under ordinal comparison. A record with no owner reference is denied.
- Absent policy row: `WORKSPACE`. This is not a new decision. The stored AccessControl policy model is explicit-restriction - a `RoleDataScopes` row restricts, and no row expresses no restriction - and the already-implemented `ITaskSummaryReader`, `ILeadSummaryReader` and `IDealSummaryReader` readers apply exactly this rule to the same stored state. Initial Workspace provisioning deliberately creates no data-scope policy. Reading absence as denial here would have created a second, contradictory authorization authority over one set of rows.
- `TEAM`: denied. No authoritative team ownership or team membership exists anywhere in the backend; `Team` is only an enum value in the policy table and a wire value in the projection. The scope is therefore not implemented, and it is not silently widened to `WORKSPACE`.
- `CUSTOM`: denied. `RoleDataScopePolicy.AllowedOwnerIdsJson` exists in the model but has no admitted semantics and no writer.
- There is no stored `NONE`/`DENY` scope value. Total denial is expressed by the absent capability and by the unsupported-scope denial, not by a scope enum member that does not exist.
- When no `recordId` is supplied the question is resource-level: record scope is deliberately not applied and the response carries the `LIMIT` reason `RECORD_SCOPE_NOT_EVALUATED`. This is what the create form asks, and answering it with a record rule would be answering a different question.

**Capability interaction.** `IMPLEMENTED` and `VERIFIED`. Record access is strictly additional. A missing operation capability fails the endpoint closed with `403 ACCESS_DENIED` before any record work. `canRead` requires the owner-declared read capability *and* an in-scope record; `canUpdate`, `canDelete`, `canExport` and `canApprove` each additionally require `canRead` and their own declared capability, and are `false` when the owner declares none. A requested command is allowed only when the owner declares it and the membership holds the mapped capability, so an undeclared command name can never be granted. Record access never restores a capability the caller does not hold: removing `support.read` denies the record outright regardless of scope.

**Field security.** `IMPLEMENTED` and `VERIFIED`. Only fields the caller requested are projected; the policy vocabulary is never enumerated back. Precedence, most restrictive first, is `HIDDEN` < `MASKED` < `READ_ONLY` < `READ_WRITE`; across roles the minimum wins, which `EffectiveAuthorizationPolicy` already applies. An absent `RoleFieldSecurity` row carries no restriction, for the same explicit-restriction reason as the data-scope model and matching the implemented readers. The record decision then caps the result: every requested field is `HIDDEN` when `canRead` is false, and `READ_WRITE` is demoted to `READ_ONLY` when `canUpdate` is false, because a field can never be more permissive than the record it belongs to.

**Existence leakage.** `IMPLEMENTED` and `VERIFIED`. An unknown record, a record owned by another Workspace and a record hidden by scope collapse onto one external result: HTTP `200` with a fully denied `EffectiveRecordAccessDocument` - `canRead` false, no allowed command, every requested field `HIDDEN`, and the single `DENY` reason `RECORD_ACCESS_DENIED` with one fixed message. The runtime verifier compares the serialised payloads and requires them to be identical apart from `evaluatedAt` and the caller's own echoed `recordId`. Denial is a successful evaluation rather than an error because the operation's job is to report effective access; `RESOURCE_SCOPE_DENIED` and `RESOURCE_NOT_FOUND` are consequently declared in the operation's error catalogue but deliberately never emitted, since emitting either would reintroduce exactly the distinction the collapse removes. `401`, `403 ACCESS_DENIED`/`WORKSPACE_MISMATCH` and `422 VALIDATION_FAILED` remain reserved for the cases that are not about the record.

**Decision audit.** `IMPLEMENTED` and `VERIFIED`. Every evaluation appends one immutable `access.RecordAccessDecisions` row carrying the trusted Workspace, the membership and member, the resource key, the record identifier, the gating capability, allow/deny, the evaluated scope, the decision code, the request and correlation identifiers and the evaluation instant. The owner member identifier is deliberately not stored - it is foreign business data, and the derived `OwnerMatch` boolean is what the decision actually turned on, so the row reproduces the decision without copying the owner's state. This is in addition to, not instead of, the existing capability `access.AuthorizationDecisions` evidence, which the operation still writes.

**Runtime verification.** `backend/scripts/verify-access-control-record-access.ps1` provisions an isolated LocalDB database, starts a real ApiHost against it and reports `PASS=88 FAIL=0`. It proves: unauthenticated `401`; foreign and unknown Workspace fail closed with `403 ACCESS_DENIED`; the missing operation capability returns `403`; `WORKSPACE` scope allows a same-Workspace record including one owned by another member; `OWN` scope allows the caller's own record and denies another member's; `TEAM` and `CUSTOM` fail closed; a physically present foreign-Workspace record is byte-identical to an unknown record and to a scope-hidden record; a caller-supplied `ownerId`, `workspaceId` or `teamId` is rejected `422`; seeded `MASKED`, `HIDDEN` and `READ_ONLY` policies are honoured while an unlisted field stays `READ_WRITE` and the most restrictive role wins across two roles; six evaluations including denials leave the Support audit count, outbox count, case count and record version unchanged; allowed, denied and own-match decision rows are persisted with request, correlation and membership evidence, no decision is recorded against a foreign Workspace, and the table has no owner-identity column; `GET /access/context` and the Tasks, Leads, Deals, Products and Support reads stay green; and `dotnet ef migrations has-pending-model-changes` reports no pending AccessControl model change.

Data-scope and field-security policies have no admitted write operation, so the verifier seeds them directly into the AccessControl-owned tables. That is deliberate: it exercises the stored policy the evaluator actually reads without inventing an administration surface this scope does not admit.

**Deliberately deferred.** Record-fact providers for Tasks, Leads, Deals, Products and every other module are not implemented here; those resource keys therefore fail closed with `RESOURCE_FACT_AUTHORITY_UNAVAILABLE`, which is a truthful deny-by-default answer and not a regression, because the operation previously returned `404` for all of them. The three existing summary readers still carry their own inline copy of the record-scope and field-visibility rules; they are proven and unchanged by this work, but they should later delegate to the AccessControl-owned evaluator so one authority owns the rule. Team ownership, `CUSTOM` allowed-owner semantics, and any administrative write for data-scope or field-security policy remain `AUTHORITY_GAP`.

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

Classification: `PARTIALLY_RESOLVED`. Current evidence resolves ownership, live-reference identity, lifecycle use, and historical-immutability rules. It does not admit a cross-owner Products application contract or an exact Product-supplied commercial snapshot field set. The machine-readable `AG-PRODUCT-SNAPSHOT` / Product snapshot-reference `AUTHORITY_GAP` therefore remains correct and fail-closed.

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

Quotes owns Quote roots, versions, line snapshots, adjustments, pricing results, approval evidence, and delivery evidence. Orders owns Order roots, line and adjustment snapshots, payment-agreement snapshots, and Order lifecycle evidence. Products remains the sole owner of Product master/catalog truth and exposes no persistence implementation. Leads and Deals may continue storing scalar Product identifiers where their admitted contracts already allow them, but their populated Product-name/pricing snapshot paths remain blocked: Leads rejects non-empty `interestedProducts`, and Deals rejects non-empty `lineItems`, until an admitted Products-owned contract supplies the required current facts. No Leads, Deals, Quotes, or Orders behavior is changed by this authority decision.

#### ARCHIVED PRODUCT BEHAVIOR

Historical Quote and Order snapshots remain readable and unchanged after Product archival, restoration, or any other master change. Historical rendering and audit use the commercial owner's stored snapshot rather than requiring the Product to remain sellable. New Order/direct-sale use of an archived Product is prohibited by Products availability; future Quote-draft behavior remains an explicit gap and is currently fail-closed. Current authority does not admit deletion or loss of historical Product reference evidence.

#### REMAINING AUTHORITY GAPS

The following decisions remain unresolved and block a safe Products-to-commercial-owner contract: the intent-specific contract operation and approved consumers; the exact Product-owned fields supplied for Leads, Deals, Quotes, and direct Orders; whether capture is one atomic Products read or a composed availability-plus-pricing read; the required expected-version input and snapshot Product-version evidence; whether and how `pricingVersion` binds historical price inputs; treatment of concurrent Product changes during capture; and reconciliation of caller-supplied Quote/Order name, price, tax, type, description, billing, and fulfillment fields with Product authority. Until those decisions are closed, no Products-owned snapshot DTO is introduced and no foreign module may access Products persistence or fabricate Product-owned facts.

Quotes has many independently admitted owner-local operations and owns the commercial terms that Orders later consume, while accepted Quote conversion explicitly copies immutable Quote truth into an Order. Orders additionally depends on Quote conversion, payment agreement/confirmation, fulfillment and invoice eligibility, credit approval, and workflow authority; `WF-12` Order Closing still requires reconciliation and `WF-14` generic Order Creation remains blocked. Nevertheless, current connected Quote and direct-Order inputs can carry Product-shaped names/prices/tax/type data without an admitted Products source or Product-version binding. Therefore neither a complete Product-backed Quotes Core nor a complete direct-Order Core can safely be selected next. The dependency decision is `NEITHER — AUTHORITY/DEPENDENCY GAP FIRST`: close the narrow Products commercial-facts/version/pricing capture contract before implementing either core. Once closed, Quotes precedes Orders because Quotes owns the accepted commercial terms copied by the Quote-to-Order workflow.

Runtime verification was re-run on 2026-08-26 against the isolated `UnicoreCRM_ProductAuthorityClosure_20260826` LocalDB database and proved all ten operations, server-assigned identity, Workspace currency enforcement, create and replace replay after a later effective-currency/source-version change, changed-intent key rejection, exact-first scale-six `HALF_UP` boundaries below/equal/above five for exclusive/inclusive/no-tax calculations, independent Products-owned projection read audits, unchanged Product versions and unchanged outbox counts on reads, quoted `If-Match` enforcement, stale-version rejection, strict archive/restore, atomic batch rollback/replay, cross-Workspace rejection, application-boundary capability denial with no Product write, applied migration discovery, and no pending Product model changes. The repository-declared Playwright Chromium runtime also drove the real frontend against that real backend and database: list and detail loaded, both projections succeeded with the current Product version, archive increased the version, a stale projection returned `412 VERSION_CONFLICT`, reload acquired the new version, and the new-version projection succeeded. Therefore `PRODUCTS BACKEND RUNTIME: PASS`, `PRODUCTS AUTHORITY CONFORMANCE: PASS`, `PRODUCTS CONTRACT CONFORMANCE: PASS`, and `PRODUCTS CONNECTED ACCEPTANCE: PASS` for the verified Product scope. This is task-specific evidence, not independent release attestation or external-provider conformance.

Product configuration mutations, import, export, demo-data reset, inventory, purchasing, promotions, tax configuration, currency conversion, Quotes, and Orders remain unimplemented and fail closed.

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

**That blocker is now closed.** AccessControl implements the operation (see *AccessControl record access implementation authority*), the pinned assertion was replaced by real coverage, and the suite was re-run against the real frontend, a real ApiHost and a real database: 4 of 4 passed. Proven in the browser, with an `OWN` data-scope policy and `MASKED`/`HIDDEN` field policies seeded in the AccessControl-owned tables - the Support detail route issues `POST /access/records/evaluate`, receives 200 with `authority` `backend`, the trusted `workspaceId`, `canRead` true and `fieldAccess` of `description` `MASKED`, `slaPolicyId` `HIDDEN` and `subject` `READ_WRITE`, and renders the record with the boundary reporting `data-access-authority="backend"`; the detail route for a case owned by a different member receives 200 with `canRead` false, no allowed command and a `DENY` reason, renders the permission panel instead of the record, and issues **no** `GET /support/cases/{caseId}` at all, so a denied record is never read; the create form, which supplies no record identifier, receives 200 with `canRead` true, `support.create` allowed and the `RECORD_SCOPE_NOT_EVALUATED` reason; every decision is attributed to the backend rather than computed in the client; and no route raised an uncaught error. The previously proven list and lifecycle path was re-run unchanged and still passes. Support business semantics were not modified by this work.

One environment note, recorded because it is not a defect in either side: the connected Playwright configuration serves the frontend on `http://127.0.0.1:3000`, while the shared Development configuration allows only the `http://localhost:3000` browser origin, so the run needs `Frontend__AllowedOrigins__1=http://127.0.0.1:3000` exported to the host. Without it every browser API call is blocked by CORS and the whole suite times out at sign-in.

Therefore `SUPPORT CONNECTED ACCEPTANCE: PASS` for the Support list and lifecycle path, and now also for the detail and form routes.

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

## B07 Inbound Lead Webhook implementation authority

B07 introduced one backend-local `PROJECT_EXTENSION_INBOUND_LEAD_WEBHOOK` contract because the adopted frontend OpenAPI declares no inbound Lead webhook. The exact contract is frozen in `INBOUND_LEAD_WEBHOOK_EXTENSION.md`; it is not historical OpenAPI or Design Authority behavior. The six existing integration-configuration operations remain unchanged: the two read contracts are deferred from B07 Core and the four mutation contracts remain blocked.

The extension admits only `POST /integrations/inbound/leads/{integrationId}` for the neutral `generic-signed-json` provider. It verifies HMAC-SHA256 over the timestamp, delivery identifier, and exact raw JSON bytes; enforces a five-minute UTC replay window and a 65,536-byte body limit; resolves secrets only through opaque external configuration references; and accepts no caller-supplied Workspace, member, permission, owner, or Lead identity authority.

Integrations owns `IntegrationsDbContext` and `integration.InboundBindings`. A server-owned `IntegrationId` binds the provider to one Workspace, one delegated member, one secret reference, and an enabled state. Workspace resolves that pair to an active membership, and AccessControl performs server-side `leads.create` evaluation for the resolved membership through a narrow delegated authorization contract. The current model is a Delegated Integration Principal, not a first-class `ServicePrincipal`: the actual actor remains the Integration and authorization is delegated through the active member. Lead audit evidence records generic execution provenance with `ActorType = Integration`, `ActorId = IntegrationId`, `DelegatedSubjectId = delegated member`, and `SourceReference = delivery ID`. No JWT impersonation or request-scoped human identity is fabricated.

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

The mutation is multi-owner, so it is implemented in Workflows and calls approved owner contracts only; it holds no foreign DbContext, repository, Infrastructure type or EF entity. It writes through two owner-specific DbContexts and therefore cannot commit or roll back in one local transaction, so per `ARCHITECTURE_SKELETON.md` it is a `Durable` workflow and not an `Atomic` one. It is implemented in `UnicoreCRM.Workflows/Durable` and is the first implemented workflow in the system. IdentityAuth owns `IAuthenticatedIdentityReferenceLookup` and fails the workflow closed unless the authenticated account is currently active. Workspace owns `IInitialWorkspaceProvisioning` and assigns the Workspace identifier, the server-derived Workspace key, the ACTIVE creator membership identifier, the configuration seed and the account-scoped provisioning anchor. AccessControl owns `IInitialWorkspaceAccessProvisioning` and creates the one server-owned `Workspace Owner` role plus the creator assignment; the workflow can neither name the role nor choose a capability. The admitted initial capability set contains only canonical capabilities already admitted for implemented operations - `workspace.context.resolve`, the five `tasks.*`, the four `leads.*` and the seven `deals.*`. `access.*`, `studio.*` and `audit.*` are excluded because their administrative operations remain fail-closed, and no data-scope or field-security policy is created.

The caller supplies optional `name`, `logoText`, `locale`, `timeZone` and `baseCurrency` values matching the shapes the current OpenAPI already declares for `WorkspaceMembershipSummary` and `WorkspaceRuntimeConfiguration`. The request body is read strictly by the endpoint's own serializer options rather than by ambient host configuration: unknown members are rejected, the body is read from the stream instead of being inferred from a declared `Content-Length` so a chunked body is validated identically, and bodies above 8192 bytes are rejected. An absent, empty, whitespace-only or JSON-`null` body is the Skip path. It cannot supply the creator account, creator member, membership status, Workspace aggregate ID, membership aggregate ID, Workspace key, role, capability, enabled module set or product-space set. Server-owned deterministic defaults are `My Workspace`, derived logo initials, `en`, `UTC`, `USD`, `["leads","deals","tasks"]` and `["crm"]`.

WorkspaceConfig remains a `DEFERRED` Platform owner and `WorkspaceBootstrapProjection` is **not** promoted to configuration authority. The extension admits only the minimal `InitialWorkspaceConfigurationSeed` creation-time contract, written once inside the Workspace-owned transaction because the projection is Workspace-owned persistence that the Workspace-owned bootstrap read structurally requires. It has no endpoint and no mutation surface, existing values are never rewritten, and the legacy `CapabilitiesJson` column is seeded empty because B03 made the AccessControl application boundary the bootstrap capability authority. Configuration change after provisioning remains an authority gap until a WorkspaceConfig contract is admitted.

The Workspace write and the AccessControl write are separate owner-local transactions, so the workflow is deliberately **not** claimed to be one atomic commit; owner-specific DbContexts are preserved and no distributed transaction, event bus, saga or microservice is introduced. Correctness comes from durable progress plus convergence. `workspace.InitialProvisioningRecords` keys on `AccountId`, so at most one initial Workspace can ever exist per account. Step one commits the Workspace, the ACTIVE membership, the configuration seed and the anchor in the `AccessPending` state in one transaction, and rolls all of them back on conflict. Step two runs the AccessControl participant and then advances the anchor to `Completed`; both are convergent, and a `Completed` anchor performs no further AccessControl write.

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

## Never-invent rule

A missing or conflicting business contract is recorded as `AUTHORITY_GAP`. It is never repaired by convention, frontend behavior, folder names, common CRM behavior, or a speculative abstraction.
