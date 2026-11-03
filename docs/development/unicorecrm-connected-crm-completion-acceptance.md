# UniCoreCRM — Connected CRM completion acceptance

Verdict: **CONNECTED_CRM_CORE_FUNCTIONAL_PASS** for the requested Development scope.

Executed 27–28 October 2026 (Asia/Saigon). Evidence directory: `review-artifacts/connected-crm-completion-20261027/` from the workspace root. All identities, seller data, CRM records and PDFs introduced by this acceptance are synthetic Development fixtures. No production application implementation change was necessary: the existing owner contracts support this journey once the isolated actors and seller are correctly provisioned.

## A. Git / SQL baseline

| Item | Entry |
|---|---|
| Backend branch / HEAD | `master` / `13cd39eba01ebc9dc238f8b0ef98e174c1d4d3f6` |
| Frontend branch / HEAD | `feat/deal-compact-kanban-card` / `814cc4685b6d8f8ead2fc945f4607a2d0168320d` |
| SQL machine / database | `DESKTOP-ICH225R` / `UnicoreCRM_Development` |
| Entry PK snapshot | **103,971 rows / 160 tables** |
| Final PK snapshot | **106,087 rows / 160 tables** |

Entry dirty work is recorded in `backend-status-before.txt`, `frontend-status-before.txt`, both `*-before.patch` files and `*-hashes-before.txt`. This task uses the current local trees, including uncommitted B1–B6.4 implementation. The previous 101,951-row baseline is not used for this comparison. `sql-before.json` was captured before fixture mutation; `sql-after.json` was captured after final actor login/read and host shutdown. Migration IDs and product versions across 21 schemas are recorded in `sql-business-evidence.json`; automatic migration application was disabled.

## B. Existing contracts reused

Reused Identity/Workspace Development provisioning, initial Workspace HTTP provisioning, AccessControl role/member commands, Workspace business-information configuration, canonical CRM lifecycle/conversion, default Dynamic Pipeline, Deal-linked Quote creation, B1 request/approve, B5 issue/protected download, B2 manual send evidence and B3 employee-reported decision. Existing generated clients, HTTP adapters, server action availability and Connected screens were exercised without adding endpoints, capability catalog entries or client overrides. Current operation admission and owner implementations were inspected before fixture writes. `quality.api-contract.json` verifies the current frontend contract gate.

The existing B1 policy was retained: VND grand total limit 100,000,000, 10% discount limits, postpaid 30 days and the existing advanced-condition rule. `AlwaysRequired` remains false. B5 uses the existing `b5-adjusted-package-a-v1` policy. No approval threshold was lowered for the test.

## C. Official Workspace and actor provisioning

Primary A: `ws_1bc9ffc204514d4d81cbe8d623d2d287`, key `synthetic-crm-completion-a-56f9121f-1651eb29`.
Isolation B: `ws_b34243ed1676423b81497e6c60ccce75`, key `synthetic-crm-completion-b-56f9121f-422574cc`.

Two new setup administrator identities were created through the existing selective Identity Development bootstrap. Its historical executable command is `--seed-demo`; **`UNICORE_DEV_SEED_ENABLED=false` disabled the demo composition**. Only the required owner bootstrap was enabled per invocation. This was insert-only provisioning of absent synthetic identities/memberships, not a demo reseed or database reset. Existing synthetic business identities were reused with new memberships in A; Sales also has a valid B membership. Workspace bootstrap used `EnsureStudioConfiguration=false`. No original membership/configuration was overwritten.

Both Workspace owners were established by real `POST /workspaces/initial-provisioning` (201). All business role creation and assignment used AccessControl HTTP owner commands (`POST /access/roles`, `PUT /access/members/{id}/access`; issuer role refinement used `PUT /access/roles/{id}`). AccessControl bootstrap did not grant business permissions. SQL was read-only for verification, including the authoritative member-access version used by `If-Match`.

| Actor | Member ID | Effective role in A |
|---|---|---|
| Sales | `mem_04871ff80a854233a9e8559026a307f0` | CRM creation/update, Quote read/create/update; no approve or issue |
| Independent Approver | `mem_52418261ec6c4feb986f712cee08bf78` | context resolve, Quote read/approve; Workspace Quote scope |
| Document Issuer | `mem_167d47b3be6a43278964691afae23d5d` | context resolve, Quote read/issueDocument, Contact read; Workspace Quote/Contact scopes |
| Restricted | `mem_09ce560d91ee45eeaae15101c32f21ea` | Quote/Deal read with hidden Quote grandTotal and Deal notes/contactId/sourceLeadId; no approve/issue |

Issuer Contact read is required by the existing B5 buyer party reader. It has no Quote update or Studio management capability. Separate setup owners configure roles/seller; they do not perform commercial approval/issuance. A temporary Sales approve grant was used only to prove creator self-approval is independently rejected (`SELF_APPROVAL_FORBIDDEN`), then the original new-fixture assignment was restored. The temporary OWN approver scope was also restored. Full capabilities/scopes are recorded in `setup-manifest.json` and `effective-*.json`.

Credentials and authentication tokens were process-only; evidence excludes auth bodies and secrets.

## D. Seller configuration

The new A seller was saved through the Workspace business-information API by its setup owner. Required data: display/legal name, one active REGISTERED address with line1/country, and email. Optional tax, phone and logo were not fabricated as mandatory requirements. The legal name and address explicitly say SYNTHETIC / DEVELOPMENT TEST ONLY / NOT AN OPERATIONAL ISSUER.

Initial ready seller configuration was revision 2. A negative test temporarily blanked only this new seller's legal name, received 409 `SELLER_LEGAL_NAME_REQUIRED`, and restored it through the same owner API. Final public readiness after another fresh host start is true at revision 4 (`seller-readiness-final.json`, `http-final-read.json`). The main documents retain their immutable revision-2 seller snapshot. No application seller hardcoding was added.

## E. Independent Quote approval

| Branch | Quote ID | Amount / assessment | Final state |
|---|---|---|---|
| LOW | `quote_e8465c7cb0b042fcbbf298d4b72de7af` | 12,345 VND / NOT_REQUIRED | DRAFT, version 3, no approvedBy/approvedAt |
| HIGH | `quote_a2cb3b12ea0e42709aa3ba622ba1e722` | 150,000,000 VND / REQUIRED (`GRAND_TOTAL_LIMIT`) | SENT, APPROVED, version 6 |

Sales created both through Deal forms and requested assessment through the real UI. HIGH became PENDING. A separately logged-in approver reviewed the authoritative detail and approved through the existing UI. SQL records requester Sales and approver `mem_52418261ec6c4feb986f712cee08bf78` at `2026-10-27T16:14:50.3254392Z` (UTC), with policy version and commercial fingerprint. Creator/requester and approver differ. HIGH approval fingerprint is `C2984FFF71AC07F8EA2B17A6CE8AF5D8560C9DFE5568DF5AC498E3E5364A8572`.

Refresh and subsequent new host sessions retained the decision. This is B1 internal commercial approval; no B6 source verification was created. LOW has a real NOT_REQUIRED assessment, without a fabricated approval decision.

## F. Authoritative PDF issuance and download

| Branch | Document ID | Bytes | SHA-256 |
|---|---|---:|---|
| LOW | `qdoc_d88a4b5b36fe4fae949ef5d30386997c` | 51,452 | `B6EE2F149044F5B72EDE7057EDE6719D291597930564634F55558E640CCBC28B` |
| HIGH | `qdoc_d18de388d6e1417b96ab4908829f9b62` | 51,276 | `F4144B0B9409D96220FFAB7AF336CA112F7F7226F1D7FEE19281EA0E89F8280D` |

Issuer submitted the real issue form for each Quote (201). Both are revision 1 / root ID equal to their Quote ID, document version 1. LOW source/result resource versions are 2→3; HIGH 3→4. SQL metadata, commercial fingerprint, binding hash, seller revision 2, issuer, timestamp, receipt and outbox are recorded in `sql-business-evidence.json`. SQL `HASHBYTES(SHA2_256, Bytes)` and `DATALENGTH(Bytes)` match stored SHA/size and Playwright-downloaded bytes.

The PDFs came from Backend B5 storage and protected download, not the DOM or frontend export. Both downloads were repeated through the UI after stopping and starting ApiHost and logging in again. Initial/restart hashes and lengths are identical (`business-manifest.json`, `browser-documents.json`, `browser-persistence.json`, four `*-issued.pdf` files). Restricted download is denied. Old document bytes were not updated.

## G. Full linked CRM journey

The business run is `56f9121ff3b24537aff86c9bfd709bd9`.

| Entity | ID / relationship |
|---|---|
| Lead | `lead_9167ed5ae01e49d28dc603b0e12d349f` |
| Canonical Contact | `contact_4931720fd08e43f9a897c22fa39e2cb3` |
| Organization | `organization_be9ccea0e9dc4b6b961130b278dede27` |
| Affiliation | `contact_org_relationship_0d420e38bc6f4eaba207557ee2fb487f` |
| Customer | `customer_8f58d305af644a309e7e15d6b228e046`; CONTACT relationship to the same Contact |
| Nurture / Customer tasks | `task_e0c56c72963d4f1781716b6b3b4cafd8` / `task_f38df720be334011ad4e7719c174d7eb` |
| Manual activity | `activity_2c0470e5548041d4bfce804acef253ee` |
| Deal | `deal_7bab07c70e6e4a3a856c53c693d23510`; buyer is the same Contact |
| Default Dynamic Pipeline | `dpl_656083500f5c215966b8c7603509d7f0`; final stage DISCOVERY |
| Both Quotes | `SourceDealId` is this Deal, buyer is this Contact |

Lead nurture/contact creation, affiliation, official conversion to Customer, Customer task and manual NOTE used real API business commands, explicitly distinct from browser operations. Lead, Deal and both Deal-linked Quotes used browser forms. The Deal form has no sourceLead selector: `sourceLeadId` is null. Lineage is proven through official conversion and the shared canonical Contact/Customer/Deal buyer/Quote source Deal; no nonexistent direct Lead foreign key is claimed. SQL joins return both linked Quotes. Customer 360 related Deal and Deal-related Quotes were revisited after restart.

HIGH manual delivery was recorded via UI as EMAIL / `USER_CONFIRMED_SENT` / `COMMERCIAL_CONTENT_ONLY`, revision 1, same approved commercial fingerprint. It has one delivery. B2 does not bind a DocumentId or PDF SHA in this contract: this evidence does **not** prove provider delivery of the issued PDF. B5 issuance and downloaded immutable bytes are proven separately. The employee-reported acceptance response is `USER_REPORTED_ACCEPTANCE`, verification `UNVERIFIED`, same buyer/revision/fingerprint. Quote remains SENT, Deal DISCOVERY. History was re-read after refresh, separate login and ApiHost restart.

## H. Connected browser form acceptance

All seven final stages PASS in Codex-owned headless Chromium against task-owned Vite 3044 and ApiHost 5098, real SQL, no intercepted/mocked backend responses. Each stage records actor, step results, HTTP exchanges and screenshots in `browser-{stage}.json`. API read/setup contexts are separate from the browser cookie/session state.

| Stage | Actual browser operations |
|---|---|
| lead | Sales login/workspace, create Lead form |
| create | Create Deal form, two Deal-linked Quote forms; readonly default pipeline |
| approval | Two request forms; separate Approver login and HIGH approval; refreshed authoritative status |
| documents | Separate Issuer login, both issue forms, protected downloads and SHA checks |
| delivery | Sales manual SENT form, employee customer-response form, UNVERIFIED/history |
| security | Restricted data/action denial; actual sidebar A→B→A with valid memberships; stale issue failure and refresh |
| persistence | Fresh host/login; Customer/Deal/Quote relationships; both PDF downloads; approval/SENT/UNVERIFIED persist |

There are **11 successful browser business writes**: Lead 1, Deal 1, Quotes 2, approval requests 2, approval 1, issue 2, send 1, response 1. The intentional stale issue form receives 412 and shows no document before/after refresh. That test uses a separately labelled official Sales API command to create the version race; it does not mock the issue response.

Screenshots include `browser-approval-high-independent-approval-real-form.png`, both `browser-documents-*-issue-download-real-form.png`, `browser-delivery-high-unverified-response-real-form.png`, `browser-security-real-stale-issue-412-refresh-no-false-success.png` and `browser-persistence-relationships-after-restart.png`. The initial security harness timeout is retained separately; its selector waited for an arbitrary Kanban window instead of the actual Deal stage. The final harness waits for the authoritative Deal stage and verifies trusted Workspace response headers. No product change was made to hide the harness error.

## I. Security and authorization

`negative-results.json`: **28/28 PASS**. Coverage includes missing approve/issue capabilities (403), creator self-approval with explicit temporary capability (403 SELF_APPROVAL_FORBIDDEN), foreign nonmember Workspace (403), valid B membership reading A record (404), foreign write denial (403), OWN record scope (404), required-field Quote FLS and protected PDF denial (403), wrong Quote/Document binding (404), forged PDF-hash input (422), pending approval issuance (409), invalidated approval issuance (409), missing seller (409), hidden Deal fields omitted and hidden-only search returning zero vs Sales one, forged verified response (422), manual SYSTEM activity (422), forged MarkWon (422 DEAL_WIN_EVIDENCE_INVALID) and blocked atomic acceptance (409 QUOTE_ACCEPTANCE_BLOCKED / ATOMIC_ACCEPTANCE_CONTRACT_UNAVAILABLE).

The existing Deal FLS projection/search protections remain in effect. There is no policy relaxation or client capability injection. The 14 existing B6 REVIEW_RECORDED rows remain DEVELOPMENT_SIMULATION with actualSourceVerification=false and zero ACCEPTED Quotes in that reviewed set. No operational verifier or B6.5/Gate C was enabled.

## J. Idempotency, concurrency and recovery

Concurrent approval returns 200/412 with exactly one receipt and one approval outbox event. Concurrent issue returns 201/412 with exactly one document metadata row and byte content. Stale approval returns 412. Exact browser approval replay returns 200 REPLAYED; issue replay returns 201 REPLAYED with the same DocumentId. Same-key/different-payload returns 409. Editing an approved guard Quote invalidates its approval; further issue is blocked while its already issued PDF remains downloadable.

`rollback-results.json` PASS: the existing bounded DevelopmentDocumentCommitProbe injected an exception after the real SQL storage flush, scoped to new Workspace A. Issue returned 500 INTERNAL_ERROR. Quote version and document/content/receipt/outbox counts were unchanged (all four counts zero). After restart with the probe disabled, the exact same key/body/version returned 201 COMMITTED with one complete document, one content, one receipt and one event; another replay returned the same document. This proves transaction rollback/recovery under injected flush interruption, not an actual SQL Server outage. No new fault infrastructure or request bypass was added.

Fixture mistakes were corrected in the harness only: UTF-8 response-byte decoding; reading MemberAccessVersions instead of the Directory member version; and the Kanban predicate described above. First-attempt artifacts are retained. Their guard rejections were not disabled.

## K. SQL preservation

`sql-preservation.json` compares every entry PK + SHA-256 against the final snapshot: **zero missing or changed original rows** across all 103,971 original rows / 160 tables. Final count is 106,087: **2,116 new rows**, including isolated fixtures and normal command/audit/session records. The new-row table breakdown separates additions from the original set. No new Order, Payment or Invoice table rows appear in the delta. Original IAM, Workspace, demo CRM, pipeline, Quote ledgers, B5 PDF bytes, encrypted proof content and scan/review history are included in the unchanged row-hash set.

Both main PDFs additionally pass direct SQL-byte checks. New A/B have zero ACCEPTED Quotes; the main Deal remains DISCOVERY. No VERIFIED, WON, Order or Payment was produced by the journey. No SQL INSERT/UPDATE/DELETE, reset/reseed or ledger cleanup was performed by acceptance scripts.

## L. Backend / frontend regression

| Check | Fresh result |
|---|---|
| Backend build | PASS; 0 warnings / 0 errors |
| Backend focused approval/document/customer/conversion/Deal security tests | 74 PASS / 0 FAIL |
| Backend entry full suite | 408 PASS / 4 FAIL / 412 |
| Backend final full suite after build | 408 PASS / 4 FAIL / 412 |
| Frontend lint/global TS/strict-region/type budget | 4/4 PASS |
| Frontend API contract, Quotes, Deals, Customers gates | PASS each |
| Connected Chromium acceptance | All seven final stages PASS |
| Production build / bundle | PASS; entry 297.26 KiB, largest 481.10 / 500 KiB |

The four backend failures are the same baseline architecture tests: canonical layer/workflow dependencies; endpoint route/operation/authorization admission; owner mapping/cross-owner edges; durable context/migration metadata. Entry/final TRX and logs preserve exact names and diagnostics.

The previous full frontend run remains **286 PASS / 42 FAIL / 328**; it is historical evidence from the stabilization report, not a newly rerun full frontend suite. This task ran the fresh focused checks above. Source hash comparison establishes no frontend source change since task entry. The pre-existing 42 frontend failures and four backend architecture failures remain unresolved and outside this task. No tests were removed, budgets raised or unsupported contracts marked READY. No new regression was observed in the exercised scope; this is not a claim that all quality gates are green.

## M. Remaining blockers

No blocker remains for the requested core Development journey. Operational verified acceptance and atomic Quote→Deal WON remain blocked by their existing contract and intentionally outside scope. Manual B2 evidence remains a user assertion about commercial content; it cannot establish provider delivery of specific PDF bytes. These limits do not replace or weaken the separately proven B1/B5 workflow.

## N. Final functional verdict

**CONNECTED_CRM_CORE_FUNCTIONAL_PASS**. Isolated provisioning, seller readiness, independent REQUIRED approval, NOT_REQUIRED branch, authoritative B5 issue/download/SHA, browser form writes, linked CRM persistence, SENT/UNVERIFIED semantics, authorization/FLS, replay/concurrency/rollback and original SQL preservation are verified. Existing quality debt and operational acceptance limits are explicitly retained.

## O. Next priorities

1. Separate quality reconciliation for the four backend architecture failures and 42 frontend failures, with owner/operation evidence.
2. Admit the real operational evidence verification and atomic acceptance/Gate C contract in a separate task before VERIFIED/ACCEPTED/WON workflows.
3. If exact-PDF delivery proof is required, specify an admitted document-to-delivery/provider evidence contract; current manual B2 semantics do not supply that proof.

## P. Git status

No commit, push, merge, rebase, reset or source checkout was performed. All entry dirty work is preserved. The task adds this acceptance report in Backend; production Backend/Frontend implementation sources are unchanged. Final tracked/untracked manifests and differences are `*-hashes-final.txt`, `*-status-final.txt` and `source-delta.json`. Harnesses/evidence live outside both repositories. Only task-owned ApiHost/Vite processes were started; personal desktop/browser sessions were not used.

## Actual HTTP evidence index

Extracted from final browser-stage records. Authority explicitly separates real browser traffic from the API command that creates the stale-version race. All rows below use Workspace A; the separate sidebar scenario verifies A→B→A with valid memberships and trusted Workspace headers. Full sanitized records retain request IDs, bodies and stage results.

| Stage | Authority | Actor | Method | Path | Status | Correlation ID |
|---|---|---|---|---|---:|---|
| lead | REAL_BROWSER_HTTP | sales | POST | `/leads` | 201 | `050b105c-df8f-4b55-8830-d246706b0d67` |
| create | REAL_BROWSER_HTTP | sales | POST | `/deals` | 201 | `4ebc493c-68b6-43d9-b749-1a72658cb684` |
| create | REAL_BROWSER_HTTP | sales | POST | `/deals/deal_7bab07c70e6e4a3a856c53c693d23510/quotes` | 201 | `b55146a7-670c-491e-9cf3-7f1774f90c41` |
| create | REAL_BROWSER_HTTP | sales | POST | `/deals/deal_7bab07c70e6e4a3a856c53c693d23510/quotes` | 201 | `5cbc1b46-1cb7-423a-9f2f-da978f9a2456` |
| approval | REAL_BROWSER_HTTP | sales | POST | `/quotes/quote_e8465c7cb0b042fcbbf298d4b72de7af/request-approval` | 200 | `369ab26b-a4c7-41bf-b29a-7ea6ca5b6727` |
| approval | REAL_BROWSER_HTTP | sales | POST | `/quotes/quote_a2cb3b12ea0e42709aa3ba622ba1e722/request-approval` | 200 | `eaa69a4f-c9d3-4956-9f07-5bcde2d28113` |
| approval | REAL_BROWSER_HTTP | approver | POST | `/quotes/quote_a2cb3b12ea0e42709aa3ba622ba1e722/approve` | 200 | `d694d12e-f632-4da5-979c-dd79ad0435df` |
| documents | REAL_BROWSER_HTTP | issuer | POST | `/quotes/quote_e8465c7cb0b042fcbbf298d4b72de7af/documents/issue` | 201 | `049c95fe-cd45-4d90-b1c0-1b877a3eca80` |
| documents | REAL_BROWSER_HTTP | issuer | GET | `/quotes/quote_e8465c7cb0b042fcbbf298d4b72de7af/documents/qdoc_d88a4b5b36fe4fae949ef5d30386997c/content` | 200 | `c34285a3-ce69-46e1-8f1b-74f757029c7d` |
| documents | REAL_BROWSER_HTTP | issuer | POST | `/quotes/quote_a2cb3b12ea0e42709aa3ba622ba1e722/documents/issue` | 201 | `0495e7f9-6a62-4b10-95d2-0bf8e1c3f565` |
| documents | REAL_BROWSER_HTTP | issuer | GET | `/quotes/quote_a2cb3b12ea0e42709aa3ba622ba1e722/documents/qdoc_d18de388d6e1417b96ab4908829f9b62/content` | 200 | `7399cc75-7131-47dc-9cac-18dfac31a39f` |
| delivery | REAL_BROWSER_HTTP | sales | POST | `/quotes/quote_a2cb3b12ea0e42709aa3ba622ba1e722/send` | 200 | `03237261-d640-40fd-b095-513346cccefa` |
| delivery | REAL_BROWSER_HTTP | sales | POST | `/quotes/quote_a2cb3b12ea0e42709aa3ba622ba1e722/customer-decisions` | 200 | `7dcfb96f-b7a7-408a-a901-84f3f5d4056a` |
| security | OFFICIAL_API_BUSINESS_COMMAND | sales | POST | `/quotes/quote_f1a3a25eb39e423f9bbf9d215cc858cc/request-approval` | 200 | `f55bc8b3-dbd1-49c3-91bd-d1b21ec2f0d5` |
| security | REAL_BROWSER_HTTP | issuer | POST | `/quotes/quote_f1a3a25eb39e423f9bbf9d215cc858cc/documents/issue` | 412 | `ff871058-f92e-4c0b-9eca-ae9f3dac2ca1` |
| persistence | REAL_BROWSER_HTTP | sales | GET | `/quotes/quote_e8465c7cb0b042fcbbf298d4b72de7af/documents/qdoc_d88a4b5b36fe4fae949ef5d30386997c/content` | 200 | `4d8400ae-d06d-4a8a-b303-c44822bef927` |
| persistence | REAL_BROWSER_HTTP | sales | GET | `/quotes/quote_a2cb3b12ea0e42709aa3ba622ba1e722/documents/qdoc_d18de388d6e1417b96ab4908829f9b62/content` | 200 | `a255d337-f34b-48ee-bef5-db640aa84cb4` |
