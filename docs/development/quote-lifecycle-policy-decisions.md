# Quote lifecycle — authority decisions required

2026-10-24. Status: **B1 POLICY APPROVED BY USER; FUNCTIONAL API/SQL/BROWSER ACCEPTANCE VERIFIED**. The user approved the B1 proposal in this chat, including strict `>` thresholds, Workspace versioned Backend configuration, no FX, independent approver excluding both creator and requester, no owner override, missing-approver blocking, legacy fail-closed decisions and immutable audit. B2/B3/C remain outside this approval and are not enabled. See `../verification/quote-approval-b1-results.md` for acceptance and remaining repository quality failures.

## Existing function / implemented Backend / contract only / blocked

| Function | Current local Backend | Authority / gap |
|---|---|---|
| Direct Sale / Deal Draft create, Draft update | Implemented, durable pricing/idempotency/SQL | Preserve existing owner contracts and commands |
| Quote list/detail | Implemented, typed connected reads, server paging | Preserve repaired list |
| Approval request / approve / request changes | B1 implemented and functionally verified | User-approved Workspace policy; real independent actors, SQL audit/outbox and connected browser PASS |
| Manual delivery evidence | Contract only | USER_CONFIRMED_SENT allowed by this task; approval integrity is a dependency |
| Provider delivery | No verified provider evidence participant | BLOCKED_CONTRACT; Gmail Compose is not provider success |
| Customer acceptance / accept-and-close-Deal | Contract only, strict empty request | BLOCKED_CONTRACT: no approved customer evidence source/input or owner verifier |
| Generic acceptQuote / generic status change | Blocked/retired | Must remain unavailable |
| Reject | Contract only | Independent single-Quote transition with reason/audit; never MarkLost |
| Revision / expiration / archive | Contracts only | Preserve immutable history; exact retention/validity policies must be verified before each positive transition |
| MarkWon Quote / Order evidence | Deliberate fail-closed | Keep guard until owner evidence and atomicity are proven |

## B1: verified facts

- `canonical-design/modules/quotes.md` admits lifecycle DRAFT/REVIEW/SENT/terminal and separate NOT_REQUIRED/PENDING/APPROVED/CHANGES_REQUESTED approval states; commercial changes invalidate approval.
- Approval request/approve/request-changes have ready OpenAPI routes, actor/scope/FLS, If-Match, idempotency, command audit and outbox requirements.
- Backend Workspace `QuoteWorkspacePolicy` currently contains only Enabled/EnabledCurrencies/Version. Studio Backend/OpenAPI has no approval-threshold or self-approval setting.
- Frontend defaults are supporting evidence: 10% line and total discount, 100,000,000 with a VND label, 30 postpaid days, custom terms require approval, alwaysRequireApproval=false. These have not been adopted as Backend commercial policy and cannot be applied equally to every currency.
- Quote has no CreatorId field. Core creation CommandRecords retain trusted ActorId; legacy author identity cannot be inferred from current OwnerId.

## Approved B1 policy (user decision 2026-10-24)

1. Initial policy: line discount >10% or total discount >10% requires approval; postpaid offset >30 days requires approval; custom/advanced PaymentAgreement requires approval; alwaysRequireApproval=false. Threshold equality is allowed without that threshold triggering approval. Total discount uses authoritative total commercial discount / pre-discount subtotal; zero subtotal with any discount requires review.
2. Grand-total threshold: VND 100,000,000. JPY/USD/EUR/KWD have no inferred equivalent; a missing per-currency threshold requires approval until the Workspace policy supplies one. No FX conversion or VND-number reuse across currencies.
3. Workspace owner supplies a versioned server-side policy explicitly keyed by WorkspaceId. Initial deployment configuration may supply the values under that owner boundary; no browser settings/cache become policy. Missing/invalid policy blocks approval and delivery instead of assuming NOT_REQUIRED. A future Studio policy editor requires an admitted owner configuration contract.
4. Separation of duties: an approver needs quotes.approve + record/FLS access and must differ from both the immutable creator and current approval requester. Owner reassignment does not replace creator evidence. No implicit administrative self-approval override. Legacy records without trusted creation evidence fail closed for approval until reconciled through an explicitly admitted process.
5. Fingerprint: Backend SHA-256 over canonical commercial content including buyer/source, identity/revision, exact decimal snapshots/totals, tax/adjustments, validity, recipient, commercial notes and PaymentAgreement command content. Exclude approval/delivery metadata and computed payment previews/ephemeral IDs. Bind approval to both fingerprint and current policy version. Commercial edit invalidates prior approval; no caller fingerprint is trusted.
6. Payment assessment: simple full remainder payable at ORDER_CONFIRMED/DELIVERY_CONFIRMED or invoice-relative postpaid maps to ordinary terms; fixed amounts, multiple installments, milestones, fixed dates, business-day/negative-offset/other advanced rules require review. This normalization is a proposed commercial rule, not an existing Backend fact.

These choices were approved by the user. Backend configuration supplies the explicit Workspace policy; no global fallback is installed. An absent/invalid configuration blocks decisions while historical reads remain available. The effective policy version contains the configured label plus a SHA-256 digest of the actual policy values, protecting against accidental label reuse. Initial values are documented in `quote-approval-configuration.md`.

## B2: integrity boundary

Only USER_CONFIRMED_SENT may be recorded without a provider participant, retaining the trusted actor, observation time, channel, recipient, note and server commercial fingerprint. Provider evidence remains blocked. The current request cannot carry a PDF byte hash/document-storage reference or caller content fingerprint. If the user chooses to bind actual exported PDF bytes, a narrow admitted transport/storage contract is required; otherwise delivery evidence can assert the selected Quote commercial version, not a server-issued immutable PDF artifact. Client DOM PDF export remains explicitly client-generated.

## B3/C: blocked evidence and atomicity decisions

`acceptQuoteAndCloseDeal` is admitted but not implemented. Its adopted request is strict `{}`: it carries no evidence reference/signature/manual-attestation fields. No approved verifier/source is present. This task explicitly requires leaving acceptance blocked in this situation. USER_CONFIRMED_SENT cannot be reused as customer acceptance.

The workflow's required SINGLE_BACKEND_TRANSACTION cannot be demonstrated by the existing forward-only lead qualification coordinator or the Quote create Deal read lease. Quotes and Deals have separate EF contexts/local transactions. A later admitted design must use owner-local participants enlisted in one same-SQL transaction, preserve independent authorization and prove rollback/replay/race recovery. Quotes must not reference CRM persistence. The Quotes-update-only workflow capability vs Deals-close authorization requires an explicit decision before any delegated Won success. ORDER_CONFIRMED stays independently blocked.

## Evidence and workflow

Fresh entry patches/statuses/hashes: `task-runtime/quote-lifecycle/`. Verified SQL entry baseline is 50 DRAFT Quotes, with exact current CRM/Deal/Quote/pipeline hashes and migration history in `entry/`; counts are measured, not copied from earlier reports.

The B1 authority gap is resolved. Gate B2 depends on verified B1. The user's latest instruction limits implementation to B1 until verification completes; no B2/B3 implementation is authorized in this step. Positive B3/C remain BLOCKED_CONTRACT.
