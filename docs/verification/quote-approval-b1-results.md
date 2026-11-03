# Gate B1 — authoritative Quote approval

2026-10-24. **B1 functional acceptance: PASS.** Local implementation and verification record; no release/freeze, commit or merge. Repository-wide quality is not green. This report covers B1 only. B2/B3 and Deal Won remain gated.

## A. Git and baseline

Backend: master, HEAD `13cd39eba01ebc9dc238f8b0ef98e174c1d4d3f6`. Frontend: feat/deal-compact-kanban-card, HEAD `814cc4685b6d8f8ead2fc945f4607a2d0168320d`. Both were already dirty. Entry patches, statuses, file hashes and SQL snapshots are under `task-runtime/quote-lifecycle/`. No checkout replacement, reset, stash, commit, push or merge.

## B. Domain and contracts

Reused the admitted `/quotes/{quoteId}/request-approval`, `/approve`, `/request-approval-changes` routes and strict note-only requests. No new workflow engine or generic status mutation. Workspace owns a narrow versioned policy reader; AccessControl owns an availability check that evaluates active membership, independent actor, quotes.read/approve, scope and read/write FLS. Quotes queries only its own immutable command history to resolve creator evidence. No foreign owner persistence or new schema migration.

## C. Approval policy and behavior

The user approved initial thresholds: line/total discount >10%, postpaid >30 calendar days, advanced/custom payment terms, authoritative Grand Total >VND100M. Equality does not trigger. Currency without a configured limit requires approval; no FX. Configuration is explicit per Workspace, with configured version label plus content digest. Missing/invalid configuration fails closed; historical reads remain usable. See `docs/development/quote-approval-configuration.md`.

Required requests enter REVIEW/PENDING. An independent actor can approve or request changes; both creator and requester are excluded even when they hold quotes.approve. No valid approver returns an explicit blocking reason. Unknown/ambiguous legacy creator evidence blocks decisions. No owner override. Decisions bind Backend commercial SHA-256 and effective policy version. Draft/Review commercial replacement returns Draft, clears approval and records the old binding in immutable outbox evidence. Request changes returns CHANGES_REQUESTED/Draft with mandatory note.

## D. Delivery

NOT_IMPLEMENTED in this step. Sending/provider evidence/PDF authority remain gated. No Gmail Compose success or browser PDF has been represented as provider delivery or server PDF.

## E. Customer acceptance / rejection

NOT_IMPLEMENTED in this step. Positive customer acceptance remains BLOCKED_CONTRACT; generic accept remains retired/blocked. Internal approval never sets ACCEPTED.

## F. Revision / expiration / archive

NOT_IMPLEMENTED in this step. Original lifecycle retention, numbering and date-policy gaps remain separately documented in the lifecycle policy decisions. No revision/data migration was performed.

## G. Deal Won

NOT_IMPLEMENTED in this step. Existing Quote/Order evidence fail-closed guards remain; B1 mutates no Deal stage and creates no Order or Payment.

## H. Frontend

Connected List/Detail expose the three B1 commands through the existing generated client and typed adapter. Later lifecycle actions remain gated. Missing approval metadata displays unassessed. Safe bilingual feedback distinguishes missing independent approver, forbidden self-approval, missing legacy evidence and unavailable policy. Callbacks retain the version visible when an action began, including across the request-changes dialog. Review editing is allowed and commercial replacement invalidates approval. Connected builder does not use browser policy/fingerprint as authority.

Minimal-permission browser testing found a real render defect: Quote Detail eagerly mounted its optional document preview, whose full Workspace configuration read was denied. The error escaped to the whole route despite a successful Quote GET. The preview now uses an explicit optional resource with loading/error UI and no default/cached-authority fallback; configuration failure blocks preview/PDF while retaining Quote content and approval. Builder editing uses Bootstrap currency context and optional document metadata. No Studio or document-read capability was added to the synthetic approvers. Late/switch/logout currency-authority tests still pass.

## I. Actual API / SQL / browser evidence

Existing synthetic Workspace tests: `task-runtime/quote-lifecycle/b1-api/`. Actual 200 assessment at exact VND100M; stored idempotency replay, reused-key conflict, self denial, strict unknown-field rejection, concurrent request one200/one412, edit invalidation and missing policy503 with read200. Initial browser attempt reached assessment+refresh/login and failed a test-only misuse of a success-response runtime validator for ProblemDetails; the negative assertion was corrected without changing the API contract.

The user subsequently authorized separate official Development fixtures: `unicore-quote-approval-test`, creator and two independent approvers at example.test. The existing owner bootstrap requires `unicore-quote-approval-isolation` as a distinct companion. No real email is sent. Credentials are derived from an existing local secret into process memory and are not stored in source/evidence. Exact capabilities and fixture/preservation evidence are under `task-runtime/quote-lifecycle/b1-synthetic/`.

The official existing maintenance entrypoint is `ApiHost --seed-demo`, with default demo composition explicitly disabled (`UNICORE_DEV_SEED_ENABLED=false`), only the exact new IdentityAuth/Workspace/AccessControl owner fixtures enabled, Integrations disabled, and no migration command. It invokes the existing owner bootstrap actions, not CRM data seeding. Subsequent API hosts start normally with all bootstraps disabled.

| New fixture | Before | After |
|---|---:|---:|
| Target Workspace / companion Workspace | 0 /0 | 1 /1 |
| Target members / roles / Quotes / buyer Contacts | 0 /0 /0 /0 | 3 /3 /7 /1 |
| Companion members / roles / commercial records | 0 | 0 |

Creator has quotes.read/create/update, contacts.create/read and workspace.context.resolve; a separate one-capability quotes.approve role was granted only for the mandatory negative self-approval case. Both approvers share the minimal quotes.read/approve +workspace.context.resolve role. No human Studio/access-administration, Product or Deal capabilities were granted. SQL fixture counts are in `b1-fixture-counts.json`.

Synthetic API verified **25 real cases**: successful independent approval, missing capability, creator self-denial even after a dedicated quotes.approve grant, replay, stale versions, concurrent different actors one200/one412 and exactly one SQL decision/outbox, commercial invalidation and policy-change rejection after restart. The initial missing-capability run is retained in `b1-synthetic/history/c3563e3e56bf47679e099fd761e3f955/result.json`; later targeted runs do not relabel that case as rerun.

Connected isolated Chromium **PASS1/1**, no API mocks: **12 Quote HTTP responses**, actual authoritative assessment, forbidden creator self-approval, successful independent approver, persistence through refresh/login, approved Review edit and authoritative Draft/approval invalidation. Browser evidence is `b1-synthetic/browser/result.json`; screenshots include assessed-after-login, approved-after-refresh and approval-invalidated. Expected HTTP401/403 diagnostics exist for unauthenticated probes/denied optional configuration; there are no browser page errors or handled render failures after the fix.

The request-changes route also passed a separate real API/SQL run under `b1-synthetic/changes/`: mandatory note422, independent CHANGES_REQUESTED/Draft200, stored replay200, other-actor stale412 and exactly one additional decision audit/outbox per run. The targeted fixture is rerunnable through a fresh authoritative approval request, retaining earlier decisions. An attempted one-shot rerun initially refused its already-decided precondition without mutation; its evidence is archived before the successful rerun.

**57 actual responses** across normal policy/no-approver, synthetic API/browser and request-changes evidence validate against declared OpenAPI success/ProblemDetails schemas (`b1-schema-validation.json`). Final SQL contains62 Quotes: all50 entry Quotes unchanged, five additional normal-API test Quotes and seven new isolated synthetic Quotes. The three synthetic accounts, target Workspace, required empty-member companion, synthetic roles and one buyer Contact were created only through official Development owner provisioning/API. Exact retained records and row hashes are in fixture SQL/preservation evidence.

## J. Security / concurrency / preservation

Resource and record authorization precede replay; historical response values are projected under current read FLS. New mutations enforce approval field writes and commercial field reads. Trusted Workspace/actor, strict input, aggregate application lock, expected version, actor-scoped idempotency, transactional immutable command/audit/outbox are retained. Independent review identified and corrected a UI opening-version race across the changes dialog. No role/membership changes were made to existing demo Workspaces.

`b1-preservation.json` proves every original CRM Workspace/module count+hash, the original **1,050 demo records including180 Deals**, all **50 entry Quote rows**, and both Pipeline tables are unchanged. Synthetic fixture preservation additionally checks all preexisting Workspace/role/membership/credential rows. Browser/changes reruns exclude only their explicitly authorized fixture Quote IDs; no unrelated original row is exempted. Credential scans found zero derived-password, JWT or raw-auth matches; the final changes scan also matches current exact in-memory tokens with zero hits. No credentials or tokens are published. Host5098 is stopped after acceptance.

## K. Quality / baseline comparison

- Focused Backend B1/core/security/pricing/owner tests: PASS90/90, no skips.
- Full Backend:185 PASS /4 FAIL /189 total. Failure messages are byte-for-byte equivalent sets to entry except four QuoteCore owner-map diagnostics were removed:553→549. Layer/workflow138, endpoint55 and durable-owner15 messages are unchanged. `b1-diagnostic-delta.json` discloses the exact comparison; no newly added diagnostics.
- The owner repair adds only exact/dot-descendant prefixes for the two QuoteCore participants plus the new AC availability participant. Default deny and cross-owner edge allowlists are unchanged; positive/sibling/unknown regression checks pass.
- Frontend strict typecheck group: PASS3/3. Focused Quote/presentation/API-boundary checks: PASS. Currency-context lifecycle test: PASS. Final production build: PASS, entry290.98KiB, largest494.60KiB under500KiB budget.
- Global frontend tsc: the same three pre-existing test diagnostics remain: nullable model in deal-quote-transition-outcome.test.mts:42; optional strings in studio-first-visit.spec.ts:50/51. No new TypeScript diagnostics.
- An additional current connected-platform configuration gate fails on the existing developer-webhook unavailable-operation mapping. This gate and its relevant operation/refusal source are unchanged from entry; it was not executed at entry, so no exact before/after PASS is inferred. This unrelated failure remains disclosed (`frontend-b1-platform-authority.log`).
- Repository-wide release/frozen acceptance is not claimed.

## L. Remaining work and gate

B1 functional API/SQL/browser acceptance is PASS. No B2 implementation starts under the current instruction. Every Workspace must explicitly configure its own policy; the harness installs no global/production defaults and restores its process configuration. Document configuration remains unavailable to minimal Quote actors; preview shows that failure explicitly and later document integration needs its own authority/read boundary. Positive customer acceptance and atomic Deal Won are still BLOCKED_CONTRACT, independent of B1 approval. Global quality failures above prevent a repository-wide release/frozen PASS.

## M. Final Git state

Local changes remain reviewable in the existing checkouts. `task-runtime/quote-lifecycle/source-delta.json` and final statuses distinguish this task's changes from preserved dirty entry files. Git HEADs are unchanged. No commit, merge, reset or demo-data cleanup.

There are12 modified entry Backend files and15 new Backend files;10 modified entry Frontend files and4 task-created Frontend files. No inventoried entry file is missing. The entry inventory omitted five existing Vietnamese-named evidence screenshots; their modification dates predate task entry, and they are disclosed as inventory gaps rather than claimed task-created or byte-compared to an unavailable entry hash. The other2,727 Backend /2,655 Frontend inventoried entry files remain byte-identical.

Reproduce full synthetic acceptance with fresh additional fixture Quotes (existing fixtures are retained): `pwsh -NoProfile -File backend/scripts/verify-quote-approval-synthetic.ps1 -Browser`. Targeted changes proof is rerunnable with `-ChangesOnly`; browser-only mode is for its original still-fresh UI fixtures and does not reset consumed Quotes. Initial missing-capability evidence remains separately retained after the deliberate test grant.
