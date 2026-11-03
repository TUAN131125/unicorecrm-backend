# Gate B5 — Quote Document & PDF Issuance: policy and architecture decisions

Status: **APPROVED WITH USER ADJUSTMENTS — 2026-10-25.** Package A authority/security/SQL storage/versioning approved. Implementation complete; **Development Functional PASS** for issuance/storage/download and Connected acceptance. Full quality/release readiness is not claimed; see quote-document-issuance-acceptance.md. Entry observations below are historical baseline evidence.

## AnalysisRecord: verified source and SQL baseline

Backend master HEAD `13cd39eba01ebc9dc238f8b0ef98e174c1d4d3f6`; frontend feat/deal-compact-kanban-card HEAD `814cc4685b6d8f8ead2fc945f4607a2d0168320d`. Both dirty at entry; 2811 Backend and 2686 Frontend files hashed, one pre-existing frontend deletion preserved. Evidence: `task-runtime/quote-document-b5/*-entry-*`. Never overwrite prior local changes with a checkout.

Verified exact local Development database: 218 Quotes, 18 Workspaces, zero Orders and PaymentPlans. All original entry rows in 27 tables are hashed before any business mutation. Backend entry suite: 268 PASS / 4 pre-existing FAIL / 272 total. Frontend strict typecheck: 3/3 PASS. At this entry capture no B5 mutation had occurred; later synthetic-only mutations are itemized in the acceptance report.

### Root cause of Connected PDF blockage

1. `QuoteBuilderPreview.tsx` explicitly returns BLOCKED_CONTRACT in Connected mode before mounting the old preview. This protects the authority boundary.
2. The demo preview reads `getInvoiceSellerInformation` from `BrowserInvoiceConfigurationRepository`, a workspace-scoped browser storage repository with defaults such as Unicore Vietnam/billing@unicore.vn. Those values are not owner-verified issuer identity. The demo view also mixes editable sender fields, invoice preferences and Workspace data; none establishes an official seller snapshot.
3. `quotePdfExport.ts` rasterizes DOM with html2canvas/jsPDF. It has no Backend DocumentId, stable byte storage, immutable issuance snapshot, server hash, issuance receipt or protected download. It remains a demo utility, not the implementation basis for Connected issuance. Its preview money formatter fixes maximumFractionDigits to zero, which must not determine USD/EUR/KWD issuance precision.
4. Workspace owns legal business data in `StudioConfiguration.BusinessInformationJson`/`AddressesJson`, with shared aggregate Revision. `StudioService.GetConfigurationAsync` requires studio.read and exposes a broad configuration. The narrow existing Quote Workspace policy exposes enabled currencies, not seller identity. Granting Studio access to Quote users is not an acceptable fix.
5. SQL shape inspection finds **0/18 Workspaces with configured legal name, contact or address**, and no configured logos. Defaults initialize only display name. Consequently no existing Workspace can honestly pass the proposed complete-seller issuance checks. Do not populate demo legal identity from browser defaults or assumed company information. An administrator must configure real identity through the existing owner command; synthetic test identity belongs only to a new isolated Development Workspace.
6. `PublicationStatus` defaults to DRAFT; no implemented publication mutation was found. Requiring PublishedRevision would create an unrelated Studio publication dependency. Proposal below explicitly uses the latest saved owner revision without claiming it is published.
7. No reusable binary document/object storage service or server PDF renderer dependency is present. The only SQL table whose name matches document/storage/file/attachment is Products.ProductConfigurationDocuments, which stores Product configuration, not general immutable PDF bytes. Do not reuse it or access its persistence from Quotes.
8. Quote relationship verification returns buyer eligibility only. Existing Contact/Organization public participants can return current visible name/email/phone, subject to owner authorization. No persisted Quote buyer display snapshot exists. Product/Quote line snapshots also lack a unit snapshot; current UI derives generic units from type/billing cycle. This must not be represented as historical authoritative unit information.
9. At entry, `issueQuoteDocumentCommand` was not admitted. Issue/list/metadata/download and seller read authority require intentional owner/operation/OpenAPI admission. Existing generic acceptance/MarkWon admissions are unchanged.

## Decision package A — recommended

These decisions are explicitly approved by the user for B5, with the amendments below. Prior B1–B4 authority remains unchanged.

### A. Issuance authority and lifecycle

Add explicit canonical capability **quotes.issueDocument**, in addition to quotes.read. Do not alias quotes.update/quotes.approve/studio.configure to issuance or mass-grant the new capability to existing memberships. Existing AccessControl role management controls grants. It is a necessary intentional change to the protected capability inventory, to be admitted and regenerated only after approval.

Allow issuance from unarchived **DRAFT/REVIEW/SENT**, before or after first delivery, only while date-valid in authoritative Workspace timezone and while the **current B1 assessment** matches commercial fingerprint and policy version. Required approval must be independently APPROVED; otherwise the saved assessment must be NOT_REQUIRED. Unassessed Draft, expired effective validity, REJECTED/EXPIRED/ACCEPTED/archived states fail closed. No expiry mutation through GET and no approval auto-grant. Missing creator/approval evidence retains B1 fail-closed semantics.

Issuance is a document commitment only: it does not create a sales acceptance, legal digital signature, provider delivery, Order, Deal stage change or Won evidence. It does not change Quote lifecycle status or Delivery History. It increments Quote resource version once to serialize issuance against edits/lifecycle; commercial fingerprint and business revision remain unchanged. Capture **source resource version before** the mutation separately from **result version after** it.

Multiple documents may exist per Quote revision when the commercial content, saved seller configuration, buyer capture, template or effective document/approval policy changes. A document binding includes the Backend-owned commercial/seller/buyer semantic fingerprints and template/policy versions. Seller/buyer capture resource versions are stored for provenance but excluded from semantic hashes, so an unrelated owner edit or approval/Quote resource-version bump cannot accidentally create a second identical document. At most one ISSUED document per exact binding; two different-key concurrent requests do not silently duplicate it. Same key returns the same DocumentId/version/bytes; a new key for an existing identical binding returns 409 DOCUMENT_ALREADY_ISSUED (authorized existing identity may be returned). No force-reissue flag initially. For SENT, new documents require a changed binding and all current approval/policy checks; old documents remain valid historical artifacts, never overwritten or silently marked replaced.

### B. Seller authority and public metadata

Workspace owner provides a narrow public read/capture contract. It derives the trusted Workspace from authenticated context and checks quotes.read; issue additionally checks quotes.issueDocument at Quotes. It exposes only the seller allowlist, configuration version and readiness blockers. Editing remains studio.configure through the existing command. No Studio settings, secrets, integration configuration or policy internals are returned.

Required for issuance: **LegalName, DisplayName, exactly one active REGISTERED address with nonblank AddressLine1/CountryCode, and at least one valid Email or Phone**. Optional: TradingName, TaxId, Website, second address line, registered geographic codes/postal code, the other contact field and trusted logo. Do not choose arbitrarily between multiple registered addresses or fall back to operating/invoice address; readiness explains missing/ambiguous configuration. No tax ID or legal clause is fabricated.

Use latest saved Studio aggregate Revision, labelled sellerConfigurationVersion, even if PublicationStatus is DRAFT. This approval is specifically not a new publication workflow. SellerSnapshot records that exact version and field values. Later Studio changes cannot change issued bytes. Caller-supplied Quote sender fields are retained commercial source fields, but cannot override the authoritative legal issuer. PDF uses the verified issuer; commercial source and seller fingerprints remain distinct.

LogoReference alone is not trusted bytes. Do not fetch arbitrary URLs, file paths, data URLs or browser assets. Where a nonempty logo cannot be resolved by an admitted owner asset contract with validated type/size/hash, official issuance is BLOCKED_CONTRACT (or the user may explicitly approve omission as an alternative). Empty logo is allowed. Current SQL has no configured logos; do not claim configured-logo Connected support from that fact.

### C. Storage and retention decision

**Recommend SQL-backed storage owned by Quotes**, in the same database/transaction as document metadata, immutable snapshots, command receipt and audit/outbox. Store PDF bytes in a separate keyed `quotes.DocumentContents` table with varbinary(max), never selected by list/metadata queries; `quotes.Documents` stores metadata/snapshots and a unique immutable content identity. Composite Workspace/Document keys and FKs enforce relationship isolation. Application exposes an opaque DocumentId, never a storage path. No anonymous/public storage URL or filesystem storage beneath ApiHost.

Benefits: no new provider, atomic commit, no file/metadata split-success, straightforward backup and restart persistence. Costs: larger SQL backups/logs, per-document bounded memory/IO and operational retention growth. Initial technical limits: 500 lines (current Quote limit), 100 pages, 10 MiB PDF, bounded render concurrency and timeout/cancellation; fail explicitly without truncation. Exact limits may be tightened by measurement without fabricating documents. Encryption-at-rest/backup/key configuration belongs to deployment; do not claim it enabled without evidence.

Retain issued metadata, snapshots, bytes and command evidence **indefinitely within B5**, including after rejection, expiration, revision or archive. No issued-document delete/update/replace/restore route. This means no automatic retention purge, not a legal assertion about a statutory retention period. A future retention/purge policy is a separate decision. Database administrators/backups retain operational responsibilities; application immutability is not a digital signature or a claim of protection against privileged DBA tampering.

Alternative B: object storage with immutable identity, staging/finalize/recovery/orphan handling and credentials. Not recommended without existing infrastructure; larger operational scope. Alternative C: review/preview only, keep official issuance BLOCKED_CONTRACT until storage/permissions are approved. Never choose local temporary files as a shortcut to Functional PASS.

### D. Identity and version relationships

QuoteId identifies this commercial revision; RootQuoteId/QuoteRevision are immutable lineage. SourceResourceVersion identifies the consistent captured Quote before issuance; ResultResourceVersion identifies the command result. CommercialContentFingerprint reuses the Backend B1 commercial authority, not PDF bytes. DocumentId is Backend-generated; DocumentVersion increases transactionally under the Quote aggregate lock. New Quote revision never inherits DocumentId or document rows.

SellerConfigurationVersion/SellerSnapshotHash, BuyerCaptureVersion/Hash, DocumentPolicyVersion, ApprovalPolicyVersion/ApprovalFingerprint, TemplateVersion and FontAssetVersion are stored independently. PdfSha256 hashes the **actual stored bytes** after render; FileSize/MediaType are Backend-computed. StorageReference is server-only opaque identity. Every download reads stored bytes, verifies size/hash and returns them; never re-render on download or relabel a new file as old.

### E. Read/download and FLS

List/metadata/view/download require current quotes.read, trusted exact Workspace, accessible related Quote and the current FLS needed for **all fields rendered in that issued PDF**. Issue requires the extra issuance capability and record scope for the command. A previously authorized issuance/key is not an authorization bypass on replay. Replay authorizes the stored document's own rendered-field/source manifest, not merely the current Quote projection or a fresh buyer capture. Authorized same-key replay precedes new-issuance B1/date/lifecycle checks and returns the immutable historical receipt without rendering again; losing current access to any rendered field/source denies replay. Historical download similarly does not require current issuance lifecycle eligibility. Archived/historical Quotes remain readable by authorized users; current policy may deny a download, but does not erase its bytes.

Capture a server-owned field/source manifest with each document. Quote field FLS and Contact/Organization read scope/FLS for captured buyer identity are rechecked on issue and download. Metadata does not expose raw private snapshots, storage identity or fields hidden by current FLS. A PDF is indivisible: deny download rather than leak hidden commercial fields or silently redact/re-render an issued document. Narrow seller metadata read grants no seller editing rights.

Responses: authenticated download with application/pdf, sanitized Backend filename, Content-Length, byte hash, Content-Disposition and private/no-store cache policy. No client path, HTML, bytes, seller identity, document hash, approval flag or DocumentId accepted by Issue.

### F. Buyer capture and missing historical units

Use a narrow CRM owner document-party capture contract built on the existing Contact/Organization authorization participants; do not query CRM tables from Quotes. Snapshot only allowed buyer identity/contact fields at issue, with capture version. No N+1 Product queries and no lookup of current Product prices. Owner-unavailable/inaccessible party fails closed. Download repeats current access checks without changing the old capture.

For legacy lines lacking a recorded unit, propose PDF **“— (chưa ghi nhận)”**; never invent a unit or copy the current Product unit into historical data. A future UnitSnapshot contract is separate backlog, outside B5; historical lines remain unchanged. Manual lines without a saved unit use the same explicit missing-unit display. This missing-unit display was approved in package A; no UnitSnapshot contract or backfill is added in B5.

## ArchitectureRecord — implementation after approval

Quotes owns document domain/content storage/renderer, issue command and read APIs. Workspace owns seller capture; CRM owns buyer capture. Reuse trusted Workspace, AccessControl/record facts, B1 approval evaluator, strong If-Match, Quote aggregate SQL lock, immutable command records and outbox. No foreign table access.

Approved and implemented routes: POST `/quotes/{quoteId}/documents/issue`; GET `/quotes/{quoteId}/documents` (bounded cursor page); GET `/quotes/{quoteId}/documents/{documentId}` metadata; GET `/quotes/{quoteId}/documents/{documentId}/content`; narrow seller GET `/quotes/document-seller` through Workspace-owned contract/route admission. Exact names/routes will follow verified repository conventions before registration. Strict issue body is optional bounded reason only, so same-key changed payload is testable. No arbitrary template URL/HTML upload/force flags.

Lock flow: trusted authorization + policy → idempotency scope lock → canonical aggregate lock → fresh Quote load and current record/FLS → authorized historical replay or If-Match → B1/date/lifecycle → stable owner seller and buyer captures → immutable issuance snapshot → bounded render → bytes SHA256 → one SQL commit of Quote version/document/content/receipt/audit/outbox. Require owner-provided read leases/version fences held through the Quote commit for seller and buyer captures. A standalone version recheck before commit is insufficient because it leaves a check/commit race. Acquire leases in one documented global order and release after Quote commit/rollback; if an owner cannot provide this consistency guarantee, official issuance remains BLOCKED_CONTRACT. No foreign-table locks from Quotes. Rendering failure/cancellation leaves no success receipt/document/content/outbox. Download never changes Quote status or evidence.

SQL bytes and metadata commit together; there is no cross-store staging protocol. Fault injection must cover render failure, SQL rollback and disconnected download, followed by an honest idempotent retry. Storage errors never return issued success.

## PDF dependency review

At entry no Backend PDF dependency existed. Implemented dependency: **PDFsharp-MigraDoc 6.2.4 Core plus explicitly pinned PDFsharp 6.2.4**, not GDI/WPF. Official NuGet packages were downloaded in memory for metadata inspection only; both nuspec files declare MIT and net10.0 assets. Package hashes and sizes are in `renderer-package-inspection.json`. Pin both: MigraDoc's minimum-version dependency must not float into a different license series.

The vendor now documents AGPL/commercial dual licensing for **6.5+ and 7 preview 2+**, so “latest PDFsharp is MIT” would be incorrect. No license keys or commercial eligibility are assumed. Preserve notices for the exact pinned MIT artifacts and audit dependency/security compatibility before adding them. Core avoids Windows GUI; embed a versioned Vietnamese-capable, redistribution-licensed font rather than rely on server-installed fonts. Font candidate verified separately: NotoSans Regular/Bold from notofonts/noto-fonts commit ffebf8c1ee449e544955a7e813c54f9b73848eac, SIL OFL 1.1, with exact font/license byte hashes in task-runtime/quote-document-b5/font-preflight/manifest.json. The exact font bytes and OFL are embedded in the Backend worker. Actual issued PDF extraction and 15-page visual checks now PASS; see acceptance evidence. Render trusted object-model text/tables; no HTML execution or network fetch. Repeat table headers, page numbering, bounded row wrapping and long-description pagination; no silent row loss. Unicode extraction, actual page rendering and totals must pass, including VND/JPY/USD/EUR/KWD. Preview rendering is never the issued identity.

Primary sources checked on 2026-10-25:
- https://www.nuget.org/packages/PDFsharp-MigraDoc/6.2.4
- https://api.nuget.org/v3-flatcontainer/pdfsharp-migradoc/6.2.4/pdfsharp-migradoc.6.2.4.nupkg
- https://api.nuget.org/v3-flatcontainer/pdfsharp/6.2.4/pdfsharp.6.2.4.nupkg
- https://docs.pdfsharp.net/General/License/License.html

## Frontend plan and delivery isolation

Keep the existing Quote module; add a focused Document panel, commands/query ports/adapter and generated clients. Connected issuer data comes only from the narrow owner contract. Content preview, official issue and protected stored-file view/download have separate labels/actions. Demo DOM export stays explicitly demo. Builder Draft preview cannot claim issued authority. No new heavy frontend PDF renderer/main-chunk dependency; budget remains 500 KiB (B4 largest 496.93 KiB).

After Issue succeeds, refresh documents/Quote resource version. Historical documents remain attached to their original revision. Pending/dirty/stale/retry/denied/unavailable/download errors use established mutation outcomes and safe error presentation. Workspace switches abort requests and clear prior seller/document state.

B2 remains USER_CONFIRMED_SENT / COMMERCIAL_CONTENT_ONLY in B5. Download/issue/Gmail Compose does not add delivery evidence. Do **not** add a DocumentId field to existing delivery history or infer that an older delivery contained these bytes; a document-linked delivery binding can be proposed separately if required. Existing B2 fields/history are not rewritten.

## Approved decision set and bounded contract gap

| Claim | Type | Approved scope / gap |
|---|---|---|
| B5-AUTH-01 | Business/security | Add quotes.issueDocument; no implicit existing grants |
| B5-LIFE-01 | Business | DRAFT/REVIEW/SENT + current B1/date checks, no downstream transition |
| B5-SELLER-01 | Business | Required fields/one registered address; latest saved revision despite DRAFT publication flag |
| B5-RETENTION-01 | Long-term data strategy | SQL bytes in one transaction; indefinite retention/no delete in B5 |
| B5-VERSION-01 | Business | One document per binding; new identity/version only when binding changes |
| B5-LEGACY-01 | Business | Explicit missing-unit display; no historical backfill |
| B5-ASSET-01 | Contract | Configured logo without trusted asset bytes blocks issuance |
| B5-PARTY-01 | Contract/security | Narrow owner buyer capture + current owner access checks |
| B5-RENDER-01 | Implementation | Exact pinned MIT renderer + verified embedded font, implemented |

User approval with amendments below is the controlling business authority. No policy question remains pending. Configured logos remain BLOCKED_CONTRACT without trusted asset verification.

## Verification plan and current verdict

Run B5-01…34 against new isolated synthetic Development Workspace configured through official provisioning/Studio/AccessControl; no writes to B1–B4 fixtures or original demo. Valid synthetic company identity is labelled synthetic, never passed off as the user's legal entity. Snapshot source data and compare original row hashes after every mutation suite. Apply additive migrations only after schema inspection and approval of package A; no old migration edits.

Validate API/SQL/bytes/hash, status/no evidence side effects, missing seller/approval, policy changes, replay/key reuse, mixed-case IDs, concurrent issue/edit, restricted role/FLS/cross-Workspace, storage failures/recovery, historical revision/archive, restart, multi-currency Unicode multi-page PDF. Dedicated Chromium only, no mocks/personal browser. Extract PDF text and render pages for visual review; compare number/buyer/seller/revision/lines/totals/currency/hash, not just %PDF header. Report performance measurements as measured local values, never production-scale claims.

Current: **B5.0–B5.7 implemented and Development Functional PASS**. Real Studio/browser, API, SQL bytes, restart, revision/archive, concurrency, FLS and rollback evidence is in quote-document-issuance-acceptance.md. Baseline full-suite quality failures and formal legacy verifier limitations are disclosed separately.


## Independent B5.0 review

Read-only review confirmed the semantic-binding refinement and found two boundary issues, incorporated above: owner leases/fences must span commit (a late version recheck is insufficient), and replay must authorize the historical document's own field/source manifest before returning its identity/receipt. This initial review preceded the user approval below. Later read-only reviews found unit-price precision, omitted saved terms and invalid options handling; all three were fixed and verified. Final read-only review found no remaining material blocker in the reviewed deltas.


## Approved user amendments — controlling decision

The user approved implementation without another policy confirmation. Reuse existing Workspace/Studio business-information write/read contracts and existing Studio UI, extending the minimum configuration/readiness UX where required. Keep admin studio.configure separate from quotes.issueDocument; no automatic grants. Use a new synthetic Development Workspace and official provisioning/Studio/AccessControl only. No autofill/backfill of the 18 original Workspace identities.

Seller required legal/display names, registered address and one valid contact; Tax ID optional unless explicitly configured/required by an issuer's separate policy, never fabricated. Logo optional; a supplied logo must pass trusted asset validation, otherwise issuance fails closed. Snapshot all values. Use current B1 NOT_REQUIRED or independently APPROVED binding; PENDING never issues. Date/timezone checks remain Backend-owned.

SQL immutable bytes/metadata commit together; measured size/page/time/memory bounds, SHA256 from real bytes, idempotency and concurrency; no false success on rendering/storage failure. No application delete/overwrite/restore; indefinite retention is a B5 technical limitation, not a completed legal retention policy. Verify backup/recovery to the extent supported without destructive restore or fake proof; disclose unavailable backup validation.

Mandatory real Connected journey includes Studio configuration, issue, SQL readback, download hash, refresh/logout/login/host restart and repeat bytes; seller changes, old revision/archive documents, negative authorization/FLS/policy/concurrency/storage cases. No SENT/ACCEPTED/WON side effects, no B6/Gate C/Order/Payment workflows, no commit/push/merge/reset/reseed.
