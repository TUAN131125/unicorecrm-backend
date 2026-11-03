# Gate B4 — Policy decisions and architecture proposal

2026-10-25, Asia/Saigon. **Status: PACKAGE A APPROVED BY USER on 2026-10-25, including the additional conditions recorded in this chat.** Implement within the approved boundaries; missing downstream proof remains BLOCKED_CONTRACT.

## 1. Measured baseline / AnalysisRecord

- Backend master HEAD `13cd39eba01ebc9dc238f8b0ef98e174c1d4d3f6`; Frontend feat/deal-compact-kanban-card HEAD `814cc4685b6d8f8ead2fc945f4607a2d0168320d`. Existing dirty tracked/untracked changes preserved; the Frontend entry already contains one deleted tracked file. No restore/reset/stash.
- Entry inventories: `task-runtime/quote-lifecycle-b4/{backend,frontend}-entry-{status,head,hashes,missing}.*`. Raw patches are not captured to avoid copying existing credentials.
- Verified local SQL: DESKTOP-ICH225R / UnicoreCRM_Development. **154 Quotes, 154 roots, max QuoteRevision=1, 0 archived, 0 duplicate root/revision groups; 0 Orders**. Zero Orders is observed data, not an absence-of-commitment contract.
- Snapshot includes row hashes of **27 tables**, now including B3 CustomerDecisionRecords. CRM demo and original Deals/Pipelines remain protected. SQL inspection: `task-runtime/quote-lifecycle/b4-entry/`; lineage counts: `task-runtime/quote-lifecycle-b4/lineage-retention-entry.json`.
- Backend entry build/full tests: **231/235 PASS, 4 failures**. Frontend strict **3/3 PASS**. These are baseline results, not B4 functional acceptance.
- Read-only independent review agrees that the wire contracts are admitted but the following business choices are incomplete.

## 2. Authority already established — not reopened

| Classification | Established rule | Evidence |
|---|---|---|
| CANONICAL_FACT | DRAFT/REVIEW mutable; SENT and terminal commercial content immutable; SENT → EXPIRED; no DRAFT/REVIEW → EXPIRED transition established | design-authority/canonical-design/modules/quotes.md |
| CANONICAL_FACT | Revise: POST /quotes/{quoteId}/revise; quotes.create; If-Match/idempotency; new ID and DRAFT; same root; immutable prior revision; HTTP 201 / QUOTE_REVISION_CREATED | exact operation lookup + current OpenAPI |
| CANONICAL_FACT | Expire: POST /quotes/{quoteId}/expire; quotes.update; If-Match/idempotency; HTTP 200 / QUOTE_EXPIRED | exact operation lookup + current OpenAPI |
| CANONICAL_FACT | Archive: POST /quotes/{quoteId}/archive; quotes.delete; required reason 1–500; If-Match/idempotency; HTTP 200 / QUOTE_ARCHIVED; current wire summary says **mutable Quote** | exact operation lookup + current OpenAPI |
| CANONICAL_FACT | Revise/Expire request: strict object, optional reason ≤500; Archive strict reason required | ReviseQuoteRequest / ExpireQuoteRequest / ArchiveQuoteRequest |
| CANONICAL_FACT | ValidUntil inclusive through end of Workspace local date; server time and authoritative timezone; invalid timezone fail-closed; null date has no date expiry | approved quote-delivery-policy-decisions.md |
| CANONICAL_FACT | Approval must not be inherited across commercial versions; USER_REPORTED_ACCEPTANCE stays UNVERIFIED; no automatic ACCEPTED/Won/Order | approved B1/B2/B3 decisions and this B4 task |
| DERIVED_FACT | Reusing a root QuoteNumber across rows conflicts with current UNIQUE(WorkspaceId, QuoteNumber) | QuotesDbContext and existing migrations |
| DERIVED_FACT | There is no root/revision unique constraint yet; no root-scoped revision writer exists | QuotesDbContext, EfQuotesPersistence, current endpoints |
| AUTHORITY_GAP | Source statuses, branching, working-draft limit, numbering, owner inheritance and Deal eligibility for revision | not resolved by readiness or Demo functions |
| AUTHORITY_GAP | Archive SENT/terminal eligibility, downstream owner verification and exact visibility policy | mutable-only wire summary; no authoritative retention participant found |

Quotes HTTP Adapter/generated client already contain all three calls. Backend currently maps Core/B1/B2/B3 only. Demo behavior cannot authorize these choices. Existing lifecycle B1 decision document did not approve B4.

## 3. Recommended package A — Revision decisions needing approval

1. **Sources:** unarchived SENT, REJECTED or EXPIRED only. Reject DRAFT/REVIEW (edit existing Draft instead), ACCEPTED and archived source. Keep positive acceptance blocked.
2. **Working revision:** at most **one unarchived DRAFT or REVIEW per root**. Both count as a working revision, including PENDING approval. No fixed cap on retained historical versions. Do not implicitly expire/supersede/archive any older SENT version; a newer revision does not itself cancel an earlier offer.
3. **Lineage:** allow an eligible older source to be revised when no working revision exists. RootQuoteId remains the original root; RevisionOfQuoteId is the exact selected source. The new QuoteRevision is the next monotonically increasing root-wide integer, including archived rows. It need not equal selectedSource.QuoteRevision+1 when revising an older source. No silent latest-only restriction.
4. **Identity:** new server QuoteId and a **new globally allocated Q-number from the existing SQL sequence for every revision**. Root lineage, not a reused QuoteNumber, groups versions. QuoteRevision increases only when creating a revision; new ResourceVersion=1 and Draft edits increase only ResourceVersion. Sequence gaps after rollback remain allowed.
5. **Copy:** preserve Buyer/source relationships, owner, title, recipient, currency, ValidUntil, commercial notes, line snapshots, exact captured prices/tax/adjustments and ProductSnapshotsJson provenance. No automatic catalog reprice. Backend validates and recalculates exact totals with the approved precision/HALF_UP rules; inconsistent/unsupported legacy snapshots fail closed rather than being silently repaired.
6. **Payment:** preserve commercial amount/due/method terms, regenerate new-Quote-owned identities/previews and clear acceptance/commitment metadata. Never relabel the source payment snapshot as accepted for the new Quote.
7. **Reset:** DRAFT with no approval decision/binding, delivery/customer response history, sent/accepted/rejected/expired/archive timestamps/reason. Source row and every source evidence row stay byte-for-byte unchanged. The new creator is the actor who requested revision, backed by immutable creation audit, independently of inherited owner. B1 creator lookup must recognize revision creation evidence.
8. **Current eligibility:** buyer and inherited owner must still be valid through owner contracts; owner must satisfy current create record scope. Currency must remain enabled for the Workspace. Do not auto-reassign an inactive owner.
9. **Deal-linked:** require the source Deal still OPEN and unarchived and verify its relationships still match the source Quote. Reuse `IQuoteRelationshipVerifier.AcquireDealAsync` read lease until Quote commit. Closed/archived/changed/unreadable Deal blocks revision; no implicit Direct Sale conversion, Buyer replacement or Deal mutation. The revise body has no Deal version input; the owner lease binds the server-read current Deal version, while If-Match binds the source Quote version.
10. **Permissions:** quotes.read + quotes.create, current source record scope/commercial FLS and prospective created-row FLS. No permission to modify Product pricing is inferred. Product capture is copied as historical evidence; changing/adding Product lines later continues to require existing authoritative Product contracts and current pricing permissions.

Alternative revision policies can be chosen explicitly: latest-only source, multiple working drafts, shared display number or closed-Deal revision. None will be inferred from Demo. Shared QuoteNumber would require a deliberate uniqueness/transport redesign and is not package A.

## 4. Expiration proposal — reuse existing authority

- Explicit `expireQuoteCommand` only; unarchived SENT, nonnull ValidUntil, authoritative Workspace local date strictly greater than ValidUntil. Today/equality is still valid; null date is not expirable. DRAFT/REVIEW/ACCEPTED/REJECTED/EXPIRED refuse a new mutation. Historical legitimate replay does not append another event.
- Server supplies ExpiredAt. Retain all commercial/approval/delivery/customer response evidence; do not create customer rejection or Deal Lost.
- No new scheduler/worker and no GET-side business writes. After downtime, reads recompute effective validity and an authorized explicit command can persist EXPIRED. B2 already rejects fresh send/resend when overdue, regardless of persisted status. B3 UNVERIFIED reports/rejection remain human-history operations; do not silently revoke the B3 rule allowing reports/rejection on SENT solely because ValidUntil passed.
- Optional read metadata separates `status` from Backend-evaluated validity (`VALID`, `OVERDUE`, `NO_EXPIRY`, `TIMEZONE_UNAVAILABLE`), with evaluatedAt and authoritative timezone. It is not browser-clock truth and never overwrites persisted status. FLS must suppress validity derived from hidden ValidUntil.
- Test boundaries through an isolated Development test host's TimeProvider only; never change Windows/SQL clocks or mutate fixtures from earlier gates.

## 5. Archive package A — explicit extension requested

**This is a requested extension of the current mutable-only description, not an existing canonical rule.**

1. Allow unarchived **DRAFT, REVIEW, REJECTED, EXPIRED**. Block SENT while its offer remains active and block ACCEPTED. Archive only adds metadata; REJECTED/EXPIRED keep their original lifecycle status. Do not invent an ARCHIVED QuoteStatus enum value.
2. Require quotes.read + quotes.delete, current record scope/FLS, required meaningful reason 1–500, If-Match/idempotency, current Workspace B4 policy. Persist ArchivedAt, actor in immutable command evidence, ArchiveReason, UpdatedAt and ResourceVersion; retain all snapshots/lineage/evidence/relationships/audit/outbox.
3. A link to an OPEN Deal alone does not block archive and does not change Deal stage. Closed/archived Deal is not itself a reason to delete or rewrite Quote history. Archiving a Quote does not archive its root, siblings or Deal.
4. **Any authoritative downstream reference blocks archive**, including retained/cancelled/archived Order references, until a narrower business exception is separately approved. Quote reads or a permission-filtered Orders list returning zero cannot establish safety.
5. Admit the minimum **owner-owned opaque retention eligibility contract**. Each relevant owner verifies the exact Workspace/Quote reference under a stable read lease held through Quote commit. Orders currently has SourceQuoteId but no such participant; other direct-reference owners must be audited before asserting completeness. This is verification only: no Order/Payment command, new commitment or financial lifecycle behavior is added.
6. Purpose authorization must explicitly permit retention verification for a trusted actor with quotes.read/delete in the same Workspace; return only eligible/blocked/unavailable, never downstream details. It does not grant general Orders/Payments read/write permissions. No direct foreign-owner table access from Quotes. Missing/incomplete/unstable owner authority keeps the affected archive path **BLOCKED_CONTRACT**; do not bypass it because SQL currently has zero Orders.
7. Active list default excludes archived; add explicit `archived=ACTIVE|ARCHIVED|ALL`. Existing quotes.read + record scope/FLS governs archived history/detail/lineage; no broad admin grant. Archived direct detail remains readable if permitted. No restore, purge, hard delete, automatic retention cutoff or evidence deletion.

**Package B alternative:** keep archive strictly DRAFT/REVIEW to match current wire scope. Retain the same downstream checks, reason/audit/visibility/no-restore requirements; REJECTED/EXPIRED archive remains BLOCKED_CONTRACT until a future extension. Revision and expiration use the same package A proposals above. This is narrower, not a substitute for approving revision semantics.

## 6. Narrow read/public contract changes requested

After approval, extend `listQuotes` with exact rootQuoteId and archived filters; default ACTIVE. Preserve stable pagination/search/sort and Workspace/record scope; require read access for filter fields. A root-filtered, paginated projection provides revision history without loading all histories or using one GET per revision. Archived filters do not widen permission scope.

Extend QuoteReadActions with optional revise/expire/archive hints and optional server-evaluated validity metadata in QuoteReadModel. Hints remain advisory; commands independently reauthorize. Do not add restore or enable generic status/acceptance mutations. Existing three request bodies and success envelopes remain unchanged.

Workspace-owned B4 configuration is versioned (label + effective policy digest), explicit per Workspace and fail-closed if missing/invalid/disabled. No global default or Studio rebuild. Store the effective version in command audit/outbox; expired historical evidence remains readable after a policy version changes. No B1/B2/B3 policy is silently replaced.

## 7. ArchitectureRecord — conditional on policy approval

- Owner: Quotes for commercial versions, retention metadata, audit/outbox. Deals/Workspace/Product/Orders expose only narrow owner contracts. No foreign persistence references in Quotes.
- Revision locks: idempotency scope → root-wide revision lock → source aggregate; re-read source If-Match and root working-revision eligibility before allocating. SQL unique `(WorkspaceId,RootQuoteId,QuoteRevision)` is the final invariant. Computing the next ordinal is allowed only inside root serialization and the database transaction; never unprotected MAX+1. Both contenders may observe the immutable source's same version, so source If-Match alone is insufficient.
- One Quotes transaction atomically inserts the new Quote, trusted creator evidence, source/revision bindings, command receipt and QUOTE_REVISION_CREATED. Source Quote is not updated merely to serialize revision creation. Distinguish source ResourceVersion from result new ResourceVersion in audit/receipt.
- Expire/archive: existing aggregate locks + strong If-Match/idempotency, authorization/FLS before replay, current configured policy, one transaction and immutable audit/outbox. Related-owner verification leases must stay stable through commit; unavailable proof cannot become a success.
- New additive migration only where needed for root revision uniqueness/indexes. Verify legacy root integrity and duplicates first; never repair history or modify applied migrations to make an index install.
- Product snapshots and commercial fingerprints stay authoritative; copying historical captures is not catalog authorization or automatic reprice.
- Frontend reuses existing List/Detail/Builder. New revision routes to authoritative returned DRAFT; archived view retrieves Backend data; badges distinguish persisted Quote status and effective validity. Opening version, stable retry identity, unsaved work, pending guard and Workspace binding remain mandatory.
- No worker, generic evidence framework, direct owner SQL, synthetic verified acceptance, Gate C, Order/Payment creation, provider delivery or PDF issuance.

## 8. Risk and verification plan

HIGH risk: immutable commercial history, numbering races, new archive scope and cross-owner retention verification. Business selection A is approved; implementation and verification are in progress.

After approval, follow B4.1 Revision → B4.2 Expiration → B4.3 Archive → UI → Connected acceptance → independent review/data audit. Use fresh B4 synthetic Workspace/Quotes with official Development provisioning, no CRM seeding or identity credential rewrite. Preserve every entry row (including 154 Quotes and B3 decision ledger).

Execute B4-01…B4-30 with explicit PASS/FAIL/BLOCKED/NOT_RUN, strict API schema validation, SQL/audit/outbox counts, source byte hashes, concurrent calls and stale/replay negatives. Product historical-price tests must prove no implicit reprice. Browser uses dedicated Chromium against real API/SQL, including revision Draft save and old-source readback, isolated-clock expiry, archive history, logout/login/restart, restricted actor, wrong Workspace, FLS and dirty/pending protection. Measure bundle delta against B3 largest 495.43 KiB; never raise the 500 KiB budget.

Current B4 functional test matrix: **COMPLETED** — see `quote-lifecycle-b4-acceptance.md` for separate Functional PASS verdicts, measured evidence, baseline quality failures and explicitly unexecuted Connected cases. Verified Acceptance/MarkWon/Document Issuance/provider delivery retain their prior BLOCKED_CONTRACT status.


## Admitted retention participants and access boundary (implementation detail)

The user-approved narrow verification extension resolves to three actual typed direct-reference owners: Orders.SourceQuoteId, Payments.PaymentPlans AgreementSnapshotJson.sourceQuoteId, and Deals' retained QUOTE_ACCEPTED WinEvidenceSourceId. Invoices have no direct typed Quote commitment; generic Task annotations remain retained and are not treated as financial commitments. Each adapter resolves Quote owner facts through the registered fact-provider contract, requires trusted quotes.read/delete and current record/FLS access, and returns no downstream data. An indexed SERIALIZABLE owner-local read lease covers absence as well as presence through the Quote commit.

Exact newly admitted contract edges: Orders.Infrastructure → Quotes.Contracts.IQuoteRetentionVerifier / IQuoteRetentionLease; Payments.Infrastructure → same; Deals.Infrastructure → same. Existing default-deny and foreign Domain/Infrastructure restrictions remain. Billing references Sales solely for the Quotes-owned public verification port; Quotes does not reference Billing or CRM. Additive owner migrations supply a Payments JSON source projection/index and a Deals evidence-reference index to avoid table-wide retention scans. They do not create or change Payment/Deal workflow behavior. Missing adapters or database proof stay blocked.
