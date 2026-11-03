# Realistic Development demo workspace

## Discovery and design

Baseline: backend `13cd39eba01ebc9dc238f8b0ef98e174c1d4d3f6`, frontend
`814cc4685b6d8f8ead2fc945f4607a2d0168320d`. Both checkouts contain unrelated local changes.
The user confirmed that `DESKTOP-ICH225R / UnicoreCRM_Development` contains only development/test data.
Connection configuration must remain unchanged. Existing workspace records are never updated.

Implementation evidence is the module Contracts endpoints, their application handlers, validation,
owner persistence mappings and migrations, plus Workflows Atomic qualification/conversion contracts.
Frontend evidence: `demoApplicationServiceBundle.ts`, `connectedApplicationServiceBundle.ts`,
`docs/architecture/data-defaults-and-demo-runtime.md`, HTTP module runtimes and workspace context.
Frontend readiness labels are not write authority.

| Module | Real create/list/detail | Dependencies and restrictions | Seed plan |
|---|---|---|---|
| Workspace/Access | canonical Development bootstrap; GET workspaces/bootstrap | active IdentityAuth account, canonical capability names; workspace key accepts lowercase hyphenated words | two new workspaces, same active test member |
| Product | POST/GET products | unique SKU, admitted product type, money strings and billing cycle | 30 services/implementation/subscription products |
| Organization | POST/GET organizations | valid profile and workspace authority | 35 fictional companies |
| Contact | POST/GET contacts; POST organization relationships | active owner; relationship role vocabulary, expected version | 100 people, real affiliations |
| Lead | POST/GET leads; advance work state | NEW -> CONTACTING -> VERIFYING; owner/contact channel; source is bounded text, no closed server source enum | 150; website/referral/facebook/google_ads/event/direct |
| Qualification | POST workflows/lead-qualification/{id}/opportunity | VERIFYING, CONTACT EXISTING/NEW only; Workflows coordinates participants | representative Lead -> existing Contact -> Deal journey |
| Customer | POST/GET customers; PATCH lifecycle; lead-customer-conversion workflow | `customers.onboard_existing`; unique Contact/Organization subject; no fabricated purchase evidence | 70: 40 NEW, 20 ACTIVE, 10 INACTIVE; pre-purchase onboarding allowed |
| Deal | POST/GET deals, lost outcome | CONTACT/ORGANIZATION_ACCOUNT buyer, active owner, decimal strings, stage vocabulary; positive proposal amount; no line-item pricing contract | 180; 50 Discovery,35 Qualified,30 Solution,25 Proposal,25 Negotiation,15 Lost |
| Task | POST/GET tasks; complete | active assignee, UTC due date, recordRef deals supported; statuses OPEN/COMPLETED/CANCELLED only | 160, varied due dates/priorities and completed subset |
| Activity | POST/GET activities | CALL/MEETING/NOTE/MESSAGE/EMAIL; SYSTEM forbidden; only leads/contacts/organizations/customers recordRef | 300 manually logged interactions on buyers; Deal timeline direct linkage BLOCKED_CONTRACT |
| Quote/Order/Won | read only commercial HTTP surfaces | no authoritative Quote creation route; Won needs commercial evidence | BLOCKED_CONTRACT; 15 Won moved to Discovery |
| Support | POST/GET support/cases; transition command | restricted create category vocabulary, real buyer selected; foreign reference contract limited | 25 cases: five each new/in_progress/waiting_customer/resolved/closed |

No schema migration, production API, frontend data injection or business rule change is needed.
Dependency graph: bootstrap -> Products/Organizations/Contacts -> affiliations -> Leads ->
qualification/customer conversion/onboarding -> Deals -> Tasks -> Activities/Support -> read-back.
The qualification-created Deal counts toward 180, not an extra hidden record.

The manifest is local provenance (`SYNTHETIC_DEMO`), not business truth. A locked manifest pins
server/database, dataset version, random seed, clock, workspace keys, command payloads, expected
versions and responses. Each intent is atomically saved before HTTP; replay uses exactly that
intent and the backend's durable idempotency ledger. Existing target keys without a manifest
are rejected. Missing/deleted manifest must never be replaced to adopt an existing workspace.
No cleanup or reset command is provided.

Backend owns creation/audit timestamps. Public Activity contracts do not admit historical
`occurredAt`; therefore interactions are recorded at execution time, not backdated. Configurable
`AsOf` controls due/close/follow-up dates. In-progress Task is NOT_SUPPORTED by this contract.
All generated email addresses use example.com. Phone fields are omitted. No provider call is needed.

## Execution

Prerequisites: Windows PowerShell 7, .NET 10, restored backend packages, and the **existing** local
SQL Server Development database with current owner migrations. Node dependencies and Playwright
Chromium must already be available for UI acceptance. No database is created, migrated, reset or
selected by this script. It reads the same appsettings/Development/local/user-secrets/environment
connection configuration as ApiHost. A mismatching expected identity fails before mutation.

Set the local bootstrap password outside source using the existing
`DevelopmentDemoBootstrap:Password` user secret or `UNICORE_DEV_SEED_PASSWORD`. The script creates
the dedicated `demo.anhduong@example.com` account through IdentityAuth bootstrap, using that
password. No password/token is stored in the manifest or evidence. Preserve the manifest across
runs. It is intentionally ignored by Git because it is local database-bound provenance.

From the backend repository:

```powershell
# Pure preview: no database, credential, host or manifest is required.
pwsh -NoProfile -File scripts/seed-realistic-demo.ps1 -AsOf 2026-10-22T05:00:00Z

# Explicit mutation in the verified existing database. Use your exact configured identity.
pwsh -NoProfile -File scripts/seed-realistic-demo.ps1 -Apply `
  -ConfirmedSyntheticDevelopmentDatabase `
  -ExpectedServer DESKTOP-ICH225R -ExpectedDatabase UnicoreCRM_Development `
  -AsOf 2026-10-22T05:00:00Z -UiAcceptance

# Rerun: the first clock is pinned in the manifest, so AsOf can be omitted.
pwsh -NoProfile -File scripts/seed-realistic-demo.ps1 -Apply `
  -ConfirmedSyntheticDevelopmentDatabase `
  -ExpectedServer DESKTOP-ICH225R -ExpectedDatabase UnicoreCRM_Development -UiAcceptance

# Verification without bootstrap or seed commands.
pwsh -NoProfile -File scripts/seed-realistic-demo.ps1 -Apply -VerifyOnly `
  -ConfirmedSyntheticDevelopmentDatabase `
  -ExpectedServer DESKTOP-ICH225R -ExpectedDatabase UnicoreCRM_Development -UiAcceptance

pwsh -NoProfile -File scripts/test-realistic-demo.ps1
```

`-RandomSeed` defaults to `20261022`. `-StateDirectory` defaults to
`artifacts/realistic-demo/realistic-v1`. The initial clock defaults to execution time when omitted;
later runs use the saved value and reject an explicitly different clock or seed. A new dataset
must use new workspace identities through a separately designed dataset version; changing the
state directory is **not** a way to adopt or reset an existing workspace.

`-StopAfterCommit 2` deliberately interrupts after a committed command and before its receipt is
saved. Use only on the first execution when demonstrating crash recovery, then rerun without it.
Backend durable idempotency answers the pending intent. Never discard a pending intent after a
timeout. Validation corrections during initial development were made only after SQL proved no
workflow anchor/idempotency receipt existed; the original rejected inputs remain in the local
manifest as diagnostic history.

The script builds ApiHost and invokes canonical `--seed-demo` twice for two active memberships.
The Workspace owner now initializes Studio through the same `StudioDefaults.Create` initializer
as initial provisioning. Opt-in `EnsureStudioConfiguration` fills only a wholly missing pair on
the targeted fixtures; partial Studio state is rejected and existing configurations preserved.
Pinned workspace IDs are checked **before** this bootstrap. The control workspace's module flags
are set by the real Studio PATCH command. No existing unrelated workspace is targeted.

`--demo-seed-host` is a separate, explicit Development-only **host mode**, not a seed endpoint.
It binds only `http://127.0.0.1:5098`, keeps normal authentication/authorization/validation, and
removes registered UniCore hosted workers so the acceptance process cannot dispatch integration
work. It never seeds by itself. The harness stops only its own host and restores environment
variables in finally. No mail, SMS, Zalo, Telegram, webhook, payment or AI provider is invoked.

For normal interactive use after seeding, start the usual ApiHost Development profile at 5080.
In the frontend repository, select Connected Mode and start the development server:

```powershell
$env:VITE_RUNTIME_MODE = 'connected'
$env:VITE_API_BASE_URL = 'http://localhost:5080'
npm run dev
```

Sign in as `demo.anhduong@example.com` using the configured local bootstrap password. Select
**UniCoreCRM Demo - Công ty Ánh Dương**, or open
`http://localhost:3000/#/w/unicore-demo-anh-duong/crm/deals?view=kanban`.
The harness's own isolated browser uses port 3039 and does not control a personal browser.

Local evidence files: `manifest.json` (before counts/hashes, intents, receipts, runs),
`verification.json` (SQL/API checks and journey IDs), `http-evidence.json` (redacted metadata),
`ui-verification.json` (browser checks and redacted HTTP metadata). Screenshots live in the
frontend `test-results/realistic-demo` directory. Tokens, connection credentials and sign-in bodies
are excluded. See `docs/verification/realistic-demo-workspace-results.md` for actual results and
known limitations. No destructive cleanup command is provided.
