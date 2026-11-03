# Realistic demo workspace — executed results

## A. Environment

Core demo delivery: **PASS**. The existing local SQL database contains the dataset, real API/SQL
verification and the isolated Connected Mode browser journey passed. This is not a production
release approval or a formal control-system FROZEN/REVIEW_PASS claim. Outstanding items are
listed below rather than hidden behind the demo result.

| Item | Verified value |
|---|---|
| Backend baseline commit | `13cd39eba01ebc9dc238f8b0ef98e174c1d4d3f6` plus this uncommitted change |
| Frontend baseline commit | `814cc4685b6d8f8ead2fc945f4607a2d0168320d` plus isolated acceptance test/config |
| SQL identity | `DESKTOP-ICH225R / UnicoreCRM_Development`, SQL machine equals local computer |
| Dataset | `realistic-v1`, `SYNTHETIC_DEMO`, random seed `20261022` |
| Seed clock | `2026-10-22T05:00:00Z` / 12:00 Asia/Ho_Chi_Minh |
| Workspace | **UniCoreCRM Demo - Công ty Ánh Dương** |
| Workspace ID | `ws_f2332a12d56a421d8a722bd2f65c5efb` |
| Workspace key | `unicore-demo-anh-duong` |
| Control workspace ID | `ws_811689cfc4b446eab4f29dc109e2abf2` |
| Control workspace key | `unicore-demo-anh-duong-control` |
| Locale / timezone / currency | `vi` / `Asia/Ho_Chi_Minh` / `VND` |
| Test account | `demo.anhduong@example.com`; password comes from existing local bootstrap secret |

The user explicitly confirmed that the database contains only development/test data. No
connection string was changed; no database was created, migrated, reset or dropped. Both
pre-existing My Workspace tenants were preserved. There are two new test workspaces with real
active memberships and Workspace-scoped AccessControl roles, using one active test owner.
OWN/TEAM behavior across multiple people is **NOT_RUN**; no fake IdentityAuth users were inserted.

Start the usual backend Development profile and the frontend in Connected Mode. Open
`http://localhost:3000/#/w/unicore-demo-anh-duong/crm/deals?view=kanban` and sign in with the account
above. The test harness itself uses loopback ApiHost 5098 and a separate browser/server at 3039;
it shuts those down after verification. No personal browser window is controlled.

## B. Dataset summary

All numbers below are **workspace-scoped SQL counts**, independently followed by real API reads.
No UI arrays, response injection or direct business-table insertion were used.

| Module | Target | Created | SQL verified | Status |
|---|---:|---:|---:|---|
| Lead | 150 | 150 | 150 | PASS |
| Contact | 100 | 100 | 100 | PASS |
| Organization | 35 | 35 | 35 | PASS |
| Customer | 70 | 70 | 70 | PASS |
| Deal | 180 | 180 | 180 | PASS |
| Task | 160 | 160 | 160 | PASS |
| Manual Activity | 300 | 300 | 300 | PASS |
| Product | 30 | 30 | 30 | PASS |
| Support Case | 25 | 25 | 25 | PASS |
| Quote | 40 conditional | 0 | 0 seeded | BLOCKED_CONTRACT |
| Order / Won evidence | conditional | 0 | 0 seeded | BLOCKED_CONTRACT |

Total: **1,050 CRM records**, plus 100 real Contact–Organization affiliations, normal owner audit,
idempotency and outbox records. Identity, membership, permissions and Studio configuration are
owned by their canonical modules. This is a synthetic consulting/software services business with
35 fictional retail/logistics/travel/education/manufacturing companies. Email domains are
`example.com`; phone fields are omitted.

Deal distribution: Discovery **50**, Qualified **35**, Solution **30**, Proposal **25**,
Negotiation **25**, Lost **15**, Won **0**. The 15 planned Won Deals were moved to Discovery,
because Quote/Order write evidence is unavailable. Lost transitions use the actual outcome command.
Deal amounts, scores, close dates, interested products and next actions vary. There are overdue
and future Tasks, 40 completed Tasks, plus open Tasks with four real priorities. Lead work states
cover NEW/CONTACTING/VERIFYING and workflow-closed examples. Customers cover 40 NEW, 20 ACTIVE,
10 INACTIVE. Support covers five records each in new/in_progress/waiting_customer/resolved/closed.
No purchase/revenue history or payment has been invented to make these states appear commercial.

## C. Relationship evidence

Representative conversion journey, verified by API detail, query filters and SQL joins:

| Step | Persisted identity and relation |
|---|---|
| Lead | `lead_57964a846b894f01a06b9744d02eee88`, Trần Minh Anh; CLOSED/CUSTOMER; CustomerRef below |
| Contact | `contact_ffc2d862d825412583ead4a6c73feec3`, affiliated with Organization below |
| Organization | `organization_469c10a2a1aa4242885239cecf8e64c7`, Công ty Logistics Minh Phúc |
| Customer | `customer_094804570ce34a88924333e77b8ae40e`, ORGANIZATION_ACCOUNT subject equals that Organization |
| Deal | `deal_45b7d4b359a74495a6bd37bcbf084bc7`, buyerRef equals the Customer's Organization subject |
| Task | `task_c71083ef490b4b4a840bd789ddf23794`, recordRef points to that Deal |
| Activity | `activity_5d539a9cd3af4094ad27e983632a1ed9`, manually logged CALL on that Contact |

**PASS** means the relations above actually exist. Activity attaches to the Contact, whose
Organization is the Deal buyer; it is not a fabricated direct Deal activity. Backend currently
rejects manual Activity recordRef `deals`, so direct Deal Activity linkage is BLOCKED_CONTRACT.

A separate real opportunity qualification also passed:
`lead_44ad827e1b8a42e09319056f1aaeac1c` -> existing Contact ->
`deal_5527322c556f42c798db3a1ff6b2009d` through Workflows. It is included in the 180 Deals.
The Customer conversion used the real workflow and preserved conversion provenance.

SQL verification found zero orphans for affiliations, Customer subjects, Deal buyers, Deal
Products, Deal Tasks, manual Activity subjects and Support buyers. Reference queries always join
on WorkspaceId as well as record ID. No business table was directly edited.

## D. Executed verification

- **PASS**: ApiHost build, zero warnings/errors.
- **PASS**: 97 API/SQL assertions in the final dataset verification, plus a separately classified
  Product pagination limitation. Workspace list/bootstrap, module lists/details, pagination,
  Deal search/stage filter, Customer status counts and Support status filters were exercised.
- **PASS**: rerunning the complete seed path sent **0** seed commands; all persisted records
  retained the same IDs/counts. An independent real Deal command replay also returned REPLAYED
  with its original ID.
- **PASS**: intentional interruption after the second Product commit, before local receipt save;
  resuming returned REPLAYED for the same Product ID. No duplicate Product was created.
- **PASS**: count plus SHA-256 row-content snapshots for all populated business modules in both
  original workspaces match the pre-seed manifest. Initially there were 4 Leads, 2 Contacts,
  1 Deal and 4 Tasks across those workspaces; no original Organization/Customer/Product/Support.
- **PASS**: both new test workspaces have legitimate memberships. Control lists are empty,
  foreign Deal detail/mutation and Task reference are denied, search/forecast do not leak Deals.
  Product isolation checks the actual plain-array response contract.
- **PASS**: restart the owned ApiHost, authenticate again, read the same Deal, and compare all
  SQL counts/content hashes. Browser refresh and real logout/login also preserve data.
- **PASS**: deterministic dry-run/seed variation, Production rejection, required safety
  confirmation, configured database mismatch rejection, and pinned workspace ID mismatch
  rejection before bootstrap. No external provider is invoked.

Initial manifest: **22/10/2026 23:19:39 +07**. This included resumable implementation/validation
iterations. The final complete idempotency run, **23:42:16–23:43:05 +07**, took about **50 seconds**
including build, API/SQL, restart and the browser test; it sent zero seed commands. Browser
acceptance took about **28 seconds**. Local manifest retains per-run timestamps and receipts.

Two initial rejected development requests were corrected: unsupported conversion stakeholder and
an Organization relationshipRef accompanying a Deal Task recordRef. SQL proved zero corresponding
workflow anchors/Task receipts before correcting those uncommitted intents; original inputs remain
in local diagnostic provenance. No committed business row was patched to repair them.

## E. UI acceptance

The agent's isolated Chromium browser ran the actual frontend with `VITE_RUNTIME_MODE=connected`
and real Backend HTTP calls. No response routes or browser repositories were mocked.

| UI surface/journey | Result |
|---|---|
| Sign-in, real Workspace selection, populated Lead list | PASS |
| Contacts / Organizations / Customers / Products / Tasks / Support lists | PASS |
| Deal Kanban: seven columns, six populated, Won empty | PASS |
| Deal search and stage filter | PASS |
| Deal detail and its actual follow-up Task | PASS |
| Contact Activity timeline and Activity detail | PASS |
| Refresh, workspace switch/cache clearing, logout and login | PASS |
| Fresh Dashboard aggregate metrics | FAIL — existing Connected hydration limitation |

14 browser assertions passed. Screenshots and redacted HTTP metadata accompany the evidence.
Dashboard currently reads browser repository snapshots through `useDashboardReadModel`; on fresh
login those snapshots have not been hydrated by the module lists, so metrics show zero even
though SQL/API data exists. No mock or hardcoded metric was added to hide that behavior.

## F. Issues and scope limits

| Classification | Item |
|---|---|
| BLOCKED_CONTRACT | Quote/Order authoritative create and commercial evidence for Won |
| BLOCKED_CONTRACT | Direct manual Activity linkage to Deal; use buyer Contact history |
| NOT_SUPPORTED | Task IN_PROGRESS; contract supports OPEN/COMPLETED/CANCELLED |
| NOT_SUPPORTED | Historical createdAt/Activity occurredAt through public commands; server time preserved |
| NOT_SUPPORTED | Pagination on plain listProducts; separate selection search has a cursor contract |
| FAIL | Existing fresh Dashboard metrics are not an authoritative hydrated aggregate |
| FAIL | Current checkout's EndpointMetadata admission test flags existing unmapped registry entries for Lead Kanban, Contact summary, Communications, Outbound Webhooks and AI |
| NOT_RUN | Full repository release pipeline, multi-person OWN/TEAM acceptance, provider-backed AI generation |
| NOT_RUN | Formal isolated reviewer-principal attestation; informal adversarial code review performed |

The full EndpointMetadata group returned **3 passed / 1 failed**. The failed test reports existing
routes outside the seeder change; this task adds no HTTP routes or OpenAPI claims. The three
authorization/workspace/blocked-operation negative controls were rerun separately. No unrelated
authority manifest or test was modified to hide the failure.

AI context source inspection found `AiAdvisoryApplication` loads `AiContextComposer` under the
trusted Workspace before provider execution. No AI provider was configured/activated for this
dataset, and no OpenAI/Gemini call was made. Provider-backed AI and end-to-end AI context remain
**NOT_RUN**, not a simulated success. Demo read access is not extended across Workspace boundaries.

## G. Reproduction and evidence

Exact commands, credential/configuration expectations, safe resume behavior and normal UI access:
[`../development/realistic-demo-workspace.md`](../development/realistic-demo-workspace.md).

Local provenance lives in `artifacts/realistic-demo/realistic-v1/`. Sanitized executed verification
and browser results are preserved under `docs/verification/evidence/realistic-demo/`. The large
database-bound manifest/logs and browser build artifacts remain ignored/local. No secrets,
database files or broad generated assets were added to source. No commit, merge or destructive
cleanup was performed.
