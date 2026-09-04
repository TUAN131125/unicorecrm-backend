# Initial Workspace Provisioning Extension

## Authority

`PROJECT_EXTENSION_INITIAL_WORKSPACE_PROVISIONING`

The adopted frontend OpenAPI declares 270 operations and none of them creates a Workspace. The
Design Authority workflow catalog declares no workspace-creation workflow, and
`05-auth-session-workspace-tenancy.md` defines no first-workspace lifecycle. The existing
`provisionWorkspaceMember`, `inviteWorkspaceMember`, `acceptWorkspaceInvitation` and
`workspace-configuration` operations are administrative or invitation surfaces for an *existing*
Workspace and remain fail-closed `AUTHORITY_GAP` or `BLOCKED`.

Therefore an account that holds zero active Workspace memberships currently has no admitted path
to any Workspace, and the Initial Setup lifecycle cannot be satisfied by an existing operation.
This backend-local contract is introduced under explicit project-extension authority. It does not
claim historical OpenAPI or Design Authority provenance, and it does not change
`frontend/unicorecrm-web/docs/api/openapi.json`, whose verified SHA-256 remains
`8278547df0fd4be9a9af9b8a6d5f3e15ddad8d005d804c99a7c9248e0f402757`.

The extension admits exactly one operation. Nothing else is unblocked:

| Operation | Contract | Classification |
|---|---|---|
| `listMyWorkspaces` | `GET /workspaces` | unchanged B02 read; remains the lifecycle authority |
| `getWorkspaceBootstrap` | `GET /workspaces/{workspaceId}/bootstrap` | unchanged B02 read |
| `provisionWorkspaceMember` | `POST /access/members` | still `AUTHORITY_GAP`; not implemented |
| `inviteWorkspaceMember`, `acceptWorkspaceInvitation` | invitation surface | still `AUTHORITY_GAP`; not implemented |
| `getWorkspaceConfiguration` and every `workspace-configuration` mutation | WorkspaceConfig | still `DEFERRED`/`BLOCKED`; not implemented |
| `provisionInitialWorkspace` | contract below | `PROJECT_EXTENSION_INITIAL_WORKSPACE_PROVISIONING` |

## Lifecycle

`listMyWorkspaces` remains the sole lifecycle authority. No first-login flag, local storage value,
setup-screen state, product-space count, foreign entity count or `404` participates:

- zero active Workspace memberships: the frontend may enter Initial Setup;
- one or more active memberships: the frontend restores or selects an existing Workspace and
  provisioning must not run.

Initial Setup draft state is frontend-only. Saving or continuing a draft creates no backend state,
and abandoning Initial Setup before Finish or Skip creates nothing. Registration and sign-in never
create a Workspace.

Finish and explicit Skip are the same canonical business intent, `ProvisionInitialWorkspace`, sent
as one request. Skip is exactly the request that omits every optional value.

After provisioning, the returned Workspace is **not** trusted merely because creation returned its
identifier. Normal Workspace trust rules still apply: the client reloads memberships, selects the
Workspace, `getWorkspaceBootstrap` verifies active membership, TrustedWorkspace is established,
AccessControl evaluates effective authority, and workspace-required CRM requests continue to use
normal `X-Workspace-Id` trust resolution. No persisted "current workspace" is introduced into
IdentityAuth or the session.

## Wire contract

- Method/path: `POST /workspaces/initial-provisioning`.
- Operation name: `provisionInitialWorkspace`.
- Authentication: required. The endpoint is deliberately **not** workspace-required, because no
  trusted Workspace can exist for an account that holds zero memberships.
- Required headers:
  - `X-Request-Id`: 8–128 characters;
  - `Idempotency-Key`: 8–128 characters.
- Optional `X-Correlation-Id`: 8–128 characters; otherwise the server trace identifier is used. The
  resolved value is echoed in the `X-Correlation-Id` response header.

### Request

Every field is optional. An absent, empty, whitespace-only or JSON-`null` body is the Skip path, as is `{}`.

```json
{
  "name": "Northwind Trading",
  "logoText": "NT",
  "locale": "vi",
  "timeZone": "Asia/Saigon",
  "baseCurrency": "VND"
}
```

Accepted values match the shapes the current OpenAPI already declares for
`WorkspaceMembershipSummary` and `WorkspaceRuntimeConfiguration`:

| Field | Rule |
|---|---|
| `name` | 1–200 characters |
| `logoText` | 1–8 characters |
| `locale` | `en` or `vi` |
| `timeZone` | 1–100 characters |
| `baseCurrency` | `^[A-Z]{3}$` |

Unknown members are rejected by the endpoint's own strict serializer options rather than by ambient
host configuration, so the guarantee holds regardless of how the host is composed. The request body
is read from the stream and is never inferred from a declared `Content-Length`, so a chunked body is
validated exactly like a buffered one. Bodies larger than 8192 bytes are rejected. Every violation
returns `VALIDATION_FAILED` with per-field detail.

The caller **cannot** supply the creator account, the creator member, the membership status, the
Workspace aggregate identifier, the membership aggregate identifier, the Workspace key, a role, a
capability, an enabled module set or a product-space set. Those are server-owned.

### Server-owned defaults

Defaults are deterministic and documented. Skip and any omitted field resolve to them:

| Value | Default |
|---|---|
| `name` | `My Workspace` |
| `logoText` | the uppercase first alphanumeric character of the first two words of the resolved name, or `W` when none exists (`My Workspace` resolves to `MW`) |
| `locale` | `en` |
| `timeZone` | `UTC` |
| `baseCurrency` | `USD` |
| `enabledModuleKeys` | `["contacts", "leads", "deals", "tasks"]`, the canonical modules admitted for a newly provisioned CRM Workspace |
| `availableProductSpaces` | `["crm"]`; Studio and People remain deferred surfaces |
| `workspaceKey` | server-derived: a lowercase slug of the resolved name, truncated to 100 characters, plus a hyphen and eight hexadecimal characters from a server-generated value |

The Workspace key is never accepted from the caller. Deriving it server-side keeps the
contract-required `^[a-z0-9]+(?:-[a-z0-9]+)*$` pattern satisfied, keeps uniqueness off the caller's
control surface, and avoids a key-availability probe that would leak the existence of other
Workspaces.

### Response

`201 Created` when this call created the Workspace, `200 OK` when it converged on an existing
initial Workspace.

```json
{
  "commandId": "command_...",
  "correlationId": "corr-...",
  "outcome": "PROVISIONED",
  "workspaceId": "ws_...",
  "membershipId": "wsm_...",
  "workspace": {
    "membershipId": "wsm_...",
    "workspaceId": "ws_...",
    "workspaceKey": "northwind-trading-1a2b3c4d",
    "name": "Northwind Trading",
    "status": "active",
    "logoText": "NT"
  },
  "provisionedAt": "2026-08-24T13:04:55Z"
}
```

`outcome` is `PROVISIONED` or `REPLAYED`. The `workspace` member is exactly the admitted
`WorkspaceMembershipSummary` shape, which is everything the client needs to reload memberships and
select the Workspace by key. Authoritative configuration and capabilities are **not** returned
here; the client reads them from `getWorkspaceBootstrap`, which remains the single bootstrap
authority.

### Errors

| Code | Status | Meaning |
|---|---|---|
| `VALIDATION_FAILED` | 422 | header or body values violate the contract |
| `AUTHENTICATION_REQUIRED` | 401 | the principal carries no account/member identity |
| `ACCESS_DENIED` | 403 | the authenticated identity is unknown or not active in IdentityAuth |
| `IDEMPOTENCY_KEY_REUSED` | 409 | the same key was replayed with different effective values |
| `WORKSPACE_ALREADY_PROVISIONED` | 409 | the account already holds active Workspace access that initial provisioning did not create |
| `INTERNAL_ERROR` | 500 | unhandled failure |

## Ownership and workflow classification

`ProvisionInitialWorkspace` is a multi-owner mutation, so it lives in Workflows and not inside
Workspace. `ARCHITECTURE_SKELETON.md` reserves `Atomic/` for multi-owner mutations that must commit
or roll back together in one local database transaction, and `Durable/` for multi-owner work where
retry or progress has business meaning and completion cannot occur in one local transaction.

Provisioning writes through two owner-specific `DbContext` instances and therefore cannot commit or
roll back as one local transaction. It is implemented in **`UnicoreCRM.Workflows/Durable`**, and it
is the first durable workflow in the system. The workflow calls approved owner contracts only. It
holds no foreign `DbContext`, repository, Infrastructure type, EF entity or SQL surface, and it owns
no persistence of its own.

### IdentityAuth

Contract: `IAuthenticatedIdentityReferenceLookup.FindActiveAsync(accountId, memberId)`.

A structurally valid bearer token is not sufficient. Provisioning re-reads authoritative
IdentityAuth state and fails closed with `ACCESS_DENIED` unless the account exists, the global
member reference matches, and the account status is active. The contract exposes no credential,
session or profile state.

### Workspace

Contract: `IInitialWorkspaceProvisioning.EnsureInitialWorkspaceAsync(request)`.

Workspace remains the sole authority for Workspace identity and membership validity. In one
owner-local transaction on `WorkspaceDbContext` it decides and writes:

1. the `WorkspaceDefinition`, with a Workspace-assigned `WorkspaceId` and the server-derived key;
2. the `WorkspaceMembership` for the authenticated caller, with a Workspace-assigned
   `MembershipId` and `ACTIVE` status;
3. the initial configuration seed (below);
4. the account-scoped `InitialWorkspaceProvisioningRecord`, committed in the `AccessPending` state.

Workspace also owns the lifecycle decision and returns one of three outcomes: `Provisioned`,
`AlreadyProvisioned` (an initial Workspace already exists for this account) or
`RejectedExistingWorkspace` (the account already holds an active membership that initial
provisioning did not create).

Because the anchor carries durable progress, Workspace additionally owns two recovery-facing
operations on the same contract: `ListAccessPendingAsync`, the authoritative outstanding-work query,
and `CompleteInitialWorkspaceAsync`, the idempotent transition to `Completed`. Neither creates,
activates or otherwise mutates a Workspace or a membership.

### AccessControl

Contract: `IInitialWorkspaceAccessProvisioning.EnsureInitialWorkspaceAccessAsync(workspaceId, membershipId)`.

AccessControl remains the authority for roles and capabilities. The workflow supplies only the
Workspace and membership scalar references and can neither name the role nor choose a capability;
it never touches `AccessControlDbContext`.

The extension admits exactly one server-owned initial role, `Workspace Owner`, created in the new
Workspace and assigned to the creator membership. Its capability set contains only canonical
capabilities that current implementation authority already admits for implemented operations:

```text
contacts.read
deals.assign, deals.bulk, deals.close, deals.create, deals.delete, deals.read, deals.update
leads.create, leads.qualify, leads.read, leads.update
products.create, products.delete, products.edit, products.read
support.assign, support.create, support.read, support.update
tasks.assign, tasks.complete, tasks.create, tasks.read, tasks.update
workspace.context.resolve
```

`access.read`, `access.configure`, `studio.*` and `audit.*` remain deliberately excluded from this
server-owned initial role. The earlier reason that AccessControl administrative operations had no
callable route is superseded: `getWorkspaceAccessDirectory`, role create/replace/archive, and member
role-assignment replacement are now implemented. This extension nevertheless does not retroactively
widen its frozen seed, and whether initial Workspace provisioning should grant `access.read` and/or
`access.configure` requires a separate project-owner governance decision. `studio.*` and `audit.*`
remain outside the implemented product surface described here.

Initial provisioning still creates no role data-scope or field-security policy. That is a bounded
seed choice, not evidence that policy administration is deferred: `createAccessRole` and
`replaceAccessRole` now own and persist those policy collections. With no seeded data-scope policy,
implemented owner readers continue to default the resource to Workspace scope; this extension does
not change that pre-existing behavior.

An exact current capability set is already converged. The one admitted historical exception is the
exact immediately preceding server-owned pre-Contacts snapshot, and only when the creator
assignment still anchors the role inside AccessControl; that snapshot may converge by adding
`contacts.read`. An arbitrary partial set, any unexpected extra capability, or drift in the
server-owned role identity fails closed instead of being amended.

### Initial configuration

The current bootstrap read contract requires `locale`, `timeZone`, `baseCurrency`,
`enabledModuleKeys` and `availableProductSpaces`, and `getWorkspaceBootstrap` returns no document
at all unless a `WorkspaceBootstrapProjection` row exists for the Workspace. A Workspace with no
seed is therefore unusable.

WorkspaceConfig remains a `DEFERRED` Platform owner. This extension does **not** promote
`WorkspaceBootstrapProjection` to configuration authority, and it does not admit any configuration
read, write, publish, audit or administration operation. It admits only the minimal provisioning
configuration contract this use case requires:

- `InitialWorkspaceConfigurationSeed` is a creation-time value carried into the Workspace
  participant. It has no mutation surface and no endpoint.
- The seed is written once, inside the same Workspace-owned transaction that creates the Workspace,
  because the projection is Workspace-owned persistence and structurally required by the
  Workspace-owned bootstrap read.
- Existing values are never rewritten. Repeat provisioning converges and changes nothing.
- Expanding the server-owned defaults affects newly created anchors only. Existing bootstrap module
  JSON and the stored effective-value fingerprint are not configuration-upgrade surfaces; historical
  same-key requests whose effective defaults no longer match fail closed under the existing
  `IDEMPOTENCY_KEY_REUSED` rule, while a different valid key replays the stored Workspace unchanged.
- The legacy `CapabilitiesJson` column is seeded as an empty array. Since B03 the bootstrap
  capability projection is read from the AccessControl application boundary, so that column is not
  authority and provisioning does not fabricate a value for it.

When a WorkspaceConfig contract is admitted it supersedes these seeded values. Until then,
configuration change after provisioning remains an authority gap.

## Transaction, idempotency and concurrency design

The Workspace write and the AccessControl write are **separate owner-local transactions**. The
whole workflow is therefore **not** one atomic commit, and this extension does not claim it is.
That is exactly why the workflow is classified `Durable` rather than `Atomic`. Owner-specific
`DbContext` boundaries are preserved, and no distributed transaction, MSDTC promotion, event bus,
saga or microservice is introduced.

Correctness comes from durable progress plus convergence, anchored on durable uniqueness:

- `workspace.InitialProvisioningRecords` has `AccountId` as its primary key. At most one initial
  Workspace can ever exist per account.
- Step one writes the Workspace, the membership, the configuration seed and the anchor
  (`AccessPending`) in one transaction under `READ COMMITTED`. A losing writer's whole transaction
  rolls back, so no orphan Workspace, membership or configuration row can survive.
- Step two runs the AccessControl participant and then advances the anchor to `Completed`. Both are
  convergent. An exact current role and assignment are a no-op; the exact admitted pre-Contacts
  snapshot may add only `contacts.read` before reaching that no-op state.
- A losing or retrying caller re-reads the anchor and returns the winner's authoritative result.

### Partial-failure recovery

The only non-atomic window is: step one committed, step two did not. The account then holds one
active Workspace membership, so `listMyWorkspaces` reports it, the client legitimately skips Initial
Setup and never sends the provisioning intent again, and `getWorkspaceBootstrap` denies access
because the creator has no capability. Without recovery that account is permanently wedged.

Recovery is server-driven and deterministic:

- The `AccessPending` anchor is the authoritative outstanding-work record. It is Workspace-owned
  state about provisioning progress, not a first-login flag, not a client-held value and not a
  persisted current workspace.
- `InitialWorkspaceProvisioningResumeService`, a hosted service in `Workflows/Durable`, reads
  outstanding anchors and finishes them through the same owner contracts. It runs once at host
  start and then on a server-owned interval (default 30 seconds, configurable), so convergence does
  not depend on the client retrying, on a login event, or on any client state.
- The request path converges too: a provisioning intent that finds an `AccessPending` anchor runs
  step two before returning. Replaying a request whose anchor is `Completed` performs no further
  AccessControl write. Independently, server-owned startup convergence may scan provisioning-
  anchored completed Workspaces and converge an admitted historical AccessControl role. That scan
  never rewrites Workspace configuration or the stored effective-value fingerprint.
- Recovery never creates a second Workspace, membership, configuration seed, role or assignment,
  and it never mutates membership status.

`listMyWorkspaces` and `getWorkspaceBootstrap` are unchanged. Neither consults the anchor, and
neither gained recovery logic.

### Upgrade and migration history

**A published migration is immutable.** Once a migration ID may exist in any
`__EFMigrationsHistory` table, its file is never edited, renamed, reordered or reused. A database
that already ran it will not run it again, so editing the file in place would silently leave that
database in a state the repository no longer describes. Defects in a published migration are
repaired by a new migration, never by rewriting the old one.

The Workspace chain is therefore:

| Order | Migration | Kind |
|---|---|---|
| 1 | `20260823110217_InitialWorkspace` | schema |
| 2 | `20260824130455_InitialWorkspaceProvisioning` | schema: the provisioning anchor table |
| 3 | `20260824135117_InitialWorkspaceProvisioningRecovery` | schema: `State` and `CompletedAt`, plus a backfill that is now superseded |
| 4 | `20260824145451_InitialWorkspaceProvisioningRecoveryCorrection` | data-only correction |

Migration 3 added `State` and `CompletedAt` and backfilled every pre-existing anchor as
`State = 'Completed', CompletedAt = ProvisionedAt`. That backfill was wrong. The version that wrote
those anchors committed the Workspace, the membership, the configuration seed and the anchor in one
transaction and only then created the AccessControl assignment, so such an anchor proves nothing
about whether the assignment exists. Declaring it complete fabricated a fact the Workspace owner
cannot know, and any account whose assignment was in fact missing would have been left permanently
unable to bootstrap. Migration 3 is preserved exactly as published, and migration 4 repairs the rows
it fabricated.

Migration 4 is data-only; the model is unchanged and the snapshot is untouched. It rewrites only the
legacy signature:

```sql
UPDATE [workspace].[InitialProvisioningRecords]
SET [State] = 'AccessPending',
    [CompletedAt] = NULL
WHERE [State] = 'Completed'
  AND [CompletedAt] = [ProvisionedAt];
```

**The Workspace migration never inspects AccessControl persistence.** Workspace owns no
AccessControl state, so completion cannot be decided in a migration at all. All the correction does
is return ambiguous rows to outstanding work; the durable resume path is the only component allowed
to ask AccessControl, and it decides completion through the approved contract.

The statement is safe in every case:

- a **genuine completion** writes its own completion time in a later transaction, so `CompletedAt`
  differs from `ProvisionedAt` and the row is untouched;
- an anchor that is already `AccessPending` has a `NULL` `CompletedAt`, which the equality excludes;
- an anchor created after the correction ran is never affected;
- re-running the statement is a no-op, because repaired rows no longer match;
- on a fresh database the anchor table is empty and the correction does nothing.

`Down` is deliberately empty: reverting would have to re-fabricate the completion fact this
migration exists to remove.

On the first start after upgrading, the resume pass visits every anchor the correction returned to
outstanding work, and every anchor left outstanding by migration 3 on a database that had not yet
run it:

- if the assignment already exists, the AccessControl participant converges to the existing role and
  assignment without duplicating either; an exact admitted pre-Contacts role may receive only the
  missing `contacts.read` capability, and the anchor is marked `Completed`;
- if the assignment is missing, it is created exactly once and the anchor is marked `Completed`.

Either way the account ends with exactly one Workspace, membership, configuration seed, role,
assignment and anchor. Once the exact current capability set is present, later passes change
nothing.

In the residual case where a genuine completion happened inside the same clock tick as provisioning
and so matches the legacy signature, the correction is still safe rather than merely unlikely: the
anchor is returned to outstanding work and the next resume pass converges on the existing role and
assignment without creating duplicate state.

### Idempotency semantics

These are the exact supported semantics. Three rules govern precedence, in this order:

1. **Request validation precedes every replay rule.** Header and body validation runs first, so a
   request carrying values that violate the contract - an unsupported locale, an over-long name, an
   unknown member, an oversized body - returns `422 VALIDATION_FAILED` even when the account has
   already been provisioned. Replay semantics apply only to contract-valid requests.
2. **The account-scoped lifecycle decision precedes idempotency comparison.** An account whose
   Workspace access did not come from initial provisioning has no anchor, so it always receives
   `409 WORKSPACE_ALREADY_PROVISIONED` regardless of the supplied key or values.
3. **On any replay the stored provisioning is authoritative and the supplied setup values are
   ignored.** A replay never renames, reconfigures or otherwise rewrites the existing Workspace.

| Situation | Result |
|---|---|
| First call for a zero-workspace account | `201`, `PROVISIONED` |
| Request values that violate the contract, at any point | `422 VALIDATION_FAILED`, nothing created |
| Retry with the same key and the same effective values | `200`, `REPLAYED`, same Workspace |
| Retry with the same key and different effective values | `409 IDEMPOTENCY_KEY_REUSED` |
| Retry with a different key and any contract-valid setup values | `200`, `REPLAYED`, same Workspace, supplied values ignored |
| Retry while the anchor is `AccessPending` | step two runs, then `200`, `REPLAYED` |
| Concurrent double submit | exactly one `201`, every other request `200`, one Workspace |
| Account whose Workspace access came from elsewhere | `409 WORKSPACE_ALREADY_PROVISIONED`, nothing created |

The idempotency key and a SHA-256 fingerprint of the effective provisioning values are stored on the
anchor at creation and are never rewritten, so the comparison is always against the values that
actually produced the Workspace. The anchor, not the key, is the business invariant: even an
unrelated key cannot produce a second initial Workspace. The key is compared only against the value
stored on that account's own anchor; it is not a global reservation and reusing it on a different
account is not a conflict.

## Explicitly out of scope

Workspace administration, member invitation and acceptance, role CRUD, member access replacement,
member status change, WorkspaceConfig read/write/publish/audit, Studio setup APIs, additional
Workspace creation for an account that already has one, and any other deferred feature. None of
them is unblocked by this extension.

## Reproducible verification

`backend/scripts/verify-initial-workspace-provisioning.ps1 -DatabaseName <isolated database>` runs
the full lifecycle, including Development-only fault injection for the partial-failure recovery
path, against a real ApiHost and a real isolated database.

`backend/scripts/verify-initial-workspace-provisioning-upgrade.ps1 -DatabaseName <isolated database>`
runs the migration-chain and correction paths against real databases and a real ApiHost. It covers a
legacy fabricated anchor whose access assignment already exists, a legacy fabricated anchor whose
assignment is missing, a genuinely completed anchor that must not be reset or replayed, a fresh
database applying the whole chain, and a database that never ran the faulty migration and upgrades
across the whole chain at once.
