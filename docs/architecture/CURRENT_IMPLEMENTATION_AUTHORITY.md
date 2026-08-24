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

For every operation it declares, `frontend/unicorecrm-web/docs/api/openapi.json` controls the exact current HTTP wire contract. Its B00-computed SHA-256 is:

`8278547df0fd4be9a9af9b8a6d5f3e15ddad8d005d804c99a7c9248e0f402757`

This matches `frontend/unicorecrm-web/docs/api/openapi.sha256`. The verified file declares 270 operations: 236 contain a 2xx response contract and 34 contain no 2xx response contract. Operations without an admitted success contract remain fail-closed and must not be implemented as callable success paths.

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
- any provider/live-conformance behavior that requires external evidence not present in the repository.

These gaps do not block the independent B00 skeleton. They block only later implementation that depends on the missing semantics.

## B01 Identity/Auth implementation authority

The current Identity/Auth wire surface contains ten operations. B01 admits and implements the independently complete operations `registerAccount`, `signIn` (password/AAL1 path only), `getCurrentSession`, `refreshSession`, and `signOut`.

The following four operations remain fail-closed `AUTHORITY_GAP` despite having OpenAPI success schemas and registry readiness labels:

- `verifyMfa`: no current authority defines enrollment, authenticator/provider ownership, challenge issuance, attempt locking, or secret lifecycle. B01 does not fabricate MFA challenges or success.
- `verifyEmail`: no current authority defines verification-token issuance, delivery, hashing, expiry, or consumption semantics.
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
- `rotateManagedMemberPassword`: IdentityAuth owns credentials and no approved administrative credential contract/security semantics exists;
- `evaluateEffectiveRecordAccess`: the request cannot provide authoritative resource-owner/team/workspace facts, and no current business-owner record-fact contract exists.

Development bootstrap is configuration-only, Development-only, idempotent, and disabled by default. It creates no public provisioning endpoint and does not define production role names. Runtime verification on 2026-08-23 used isolated LocalDB databases and proved authorized context resolution, denial for an active member without the required capability, rejection of caller-supplied role/capability spoofing, foreign-Workspace isolation, and B01/B02 regressions. Therefore `B03 ACCESS CONTROL FOUNDATION: PASS`; the listed administrative omissions and authority gaps remain unimplemented and must not be invented.

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

IdentityAuth register/sign-in/session/refresh/sign-out, Workspace list/bootstrap, AccessControl context and Tasks/Leads regressions passed in the same run. The frozen inbound Lead webhook and AI advisory harnesses passed on the isolated `UnicoreCRM_Inbound_Regression_20260824_R2` and `UnicoreCRM_AI_Regression_20260824_R2` databases. Only `WorkspaceDbContext` gained migrations - `InitialWorkspaceProvisioning` and `InitialWorkspaceProvisioningRecovery` - and all affected models reported no pending changes.

`InitialWorkspaceProvisioningRecovery` backfills every pre-existing anchor as `AccessPending` and never as `Completed`. The version that wrote those anchors committed the Workspace, membership, configuration seed and anchor in one transaction and only then created the AccessControl assignment, so such an anchor proves nothing about whether the assignment exists; Workspace owns no AccessControl state and the migration must not read or write it, so completion cannot be decided there. `AccessPending` is the fail-safe value that hands the decision to the convergent resume path. Upgrade verification on 2026-08-24 used the isolated `UnicoreCRM_ProvisioningUpgrade_20260824` LocalDB database built at the schema state before the recovery migration. It seeded previous-version Workspace, ACTIVE membership, configuration seed and anchor state twice - once with an existing `Workspace Owner` role and creator assignment, once with no AccessControl assignment at all - applied only the recovery migration, and proved that both anchors migrated as `AccessPending` with no completion time. After starting the current host with the resume pass enabled and no client action, both anchors reached `Completed`; the pre-existing role and assignment identities were preserved rather than replaced or duplicated, the missing assignment was created exactly once against the creator membership, both accounts passed list, bootstrap, authorization-context and workspace-required Tasks/Leads/Deals reads, a further resume window changed nothing, and each account retained exactly one Workspace, membership, configuration seed, role, assignment and anchor. Reproducible upgrade checks are retained in `backend/scripts/verify-initial-workspace-provisioning-upgrade.ps1`. Therefore `INITIAL WORKSPACE PROVISIONING: PASS`; the deferred WorkspaceConfig, invitation, member-administration and Studio gaps above remain fail-closed. Reproducible runtime checks are retained in `backend/scripts/verify-initial-workspace-provisioning.ps1`.

## Never-invent rule

A missing or conflicting business contract is recorded as `AUTHORITY_GAP`. It is never repaired by convention, frontend behavior, folder names, common CRM behavior, or a speculative abstraction.
