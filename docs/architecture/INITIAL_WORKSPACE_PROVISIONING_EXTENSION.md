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

Every field is optional. An absent, empty or omitted body is the Skip path.

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

Unknown members are rejected by the host serializer contract. Violations return
`VALIDATION_FAILED` with per-field detail.

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
| `enabledModuleKeys` | `["leads", "deals", "tasks"]`, the currently implemented CRM owners |
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

## Ownership

`ProvisionInitialWorkspace` is a multi-owner mutation, so it lives in
`UnicoreCRM.Workflows/Atomic` and not inside Workspace. The workflow calls approved owner contracts
only. It holds no foreign `DbContext`, repository, Infrastructure type, EF entity or SQL surface,
and it owns no persistence of its own.

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
4. the account-scoped `InitialWorkspaceProvisioningRecord`.

Workspace also owns the lifecycle decision and returns one of three outcomes: `Provisioned`,
`AlreadyProvisioned` (an initial Workspace already exists for this account) or
`RejectedExistingWorkspace` (the account already holds an active membership that initial
provisioning did not create).

### AccessControl

Contract: `IInitialWorkspaceAccessProvisioning.EnsureInitialWorkspaceAccessAsync(workspaceId, membershipId)`.

AccessControl remains the authority for roles and capabilities. The workflow supplies only the
Workspace and membership scalar references and can neither name the role nor choose a capability;
it never touches `AccessControlDbContext`.

The extension admits exactly one server-owned initial role, `Workspace Owner`, created in the new
Workspace and assigned to the creator membership. Its capability set contains only canonical
capabilities that current implementation authority already admits for implemented operations:

```text
workspace.context.resolve
tasks.read, tasks.create, tasks.update, tasks.assign, tasks.complete
leads.read, leads.create, leads.update, leads.qualify
deals.read, deals.create, deals.update, deals.assign, deals.close, deals.delete, deals.bulk
```

`access.read`, `access.configure`, `studio.*` and `audit.*` are deliberately excluded: their
administrative operations remain fail-closed and have no callable route, so granting them would
invent authority and would advertise a People or Studio product space that is not implemented. No
role data-scope or field-security policy is created; defining those remains the deferred
AccessControl administrative surface. With no data-scope policy the implemented owner readers
already treat resource access as workspace-scoped, which is the pre-existing behavior and is not
changed here.

If the stored `Workspace Owner` role for a Workspace ever differs from this frozen capability set,
provisioning fails closed instead of amending it.

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
- The legacy `CapabilitiesJson` column is seeded as an empty array. Since B03 the bootstrap
  capability projection is read from the AccessControl application boundary, so that column is not
  authority and provisioning does not fabricate a value for it.

When a WorkspaceConfig contract is admitted it supersedes these seeded values. Until then,
configuration change after provisioning remains an authority gap.

## Transaction, idempotency and concurrency design

The Workspace write and the AccessControl write are **separate owner-local transactions**. The
whole workflow is therefore **not** one atomic commit, and this extension does not claim it is.
Owner-specific `DbContext` boundaries are preserved, and no distributed transaction, MSDTC
promotion, event bus, saga or microservice is introduced. This matches the existing
`PROJECT_EXTENSION_INBOUND_LEAD_WEBHOOK` precedent, where Inbox and Leads also use separate
owner-local transactions and converge.

Correctness comes from convergence anchored on durable uniqueness:

- `workspace.InitialProvisioningRecords` has `AccountId` as its primary key. At most one initial
  Workspace can ever exist per account.
- The Workspace step writes the Workspace, the membership, the configuration seed and that record
  in one transaction under `READ COMMITTED`. A losing writer's whole transaction rolls back, so no
  orphan Workspace, membership or configuration row can survive.
- A losing or retrying caller re-reads the record and returns the winner's authoritative result.
- The AccessControl step is convergent and is re-run on every provisioning call, including
  replays. An attempt that committed the Workspace and then failed before the assignment is
  completed by the next call rather than duplicated.

Resulting semantics:

| Situation | Result |
|---|---|
| First call for a zero-workspace account | `201`, `PROVISIONED` |
| Retry with the same key and the same effective values | `200`, `REPLAYED`, same Workspace |
| Retry with the same key and different effective values | `409 IDEMPOTENCY_KEY_REUSED` |
| Retry with a different key | `200`, `REPLAYED`, same Workspace |
| Concurrent double submit | exactly one `201`, every other request `200`, one Workspace |
| Account whose Workspace access came from elsewhere | `409 WORKSPACE_ALREADY_PROVISIONED`, nothing created |

The idempotency key and a SHA-256 fingerprint of the effective provisioning values are retained on
the account anchor as execution evidence. The anchor, not the key, is the business invariant: even
an unrelated key cannot produce a second initial Workspace.

The known non-atomic window is exactly this: the Workspace commit succeeds and the AccessControl
assignment then fails. The account owns a Workspace whose bootstrap denies authorization until the
provisioning intent is sent again, which converges. No duplicate Workspace, membership,
configuration or assignment can result.

## Explicitly out of scope

Workspace administration, member invitation and acceptance, role CRUD, member access replacement,
member status change, WorkspaceConfig read/write/publish/audit, Studio setup APIs, additional
Workspace creation for an account that already has one, and any other deferred feature. None of
them is unblocked by this extension.

## Reproducible verification

`backend/scripts/verify-initial-workspace-provisioning.ps1 -DatabaseName <isolated database>` runs
the full lifecycle against a real ApiHost and a real isolated database.
