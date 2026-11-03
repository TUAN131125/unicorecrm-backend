# Quote customer decision — B3 policy decision proposal

2026-10-25. Status: **APPROVED OPTION A** by explicit user decision on 2026-10-25. Reported agreement remains UNVERIFIED/SENT; Reject requires the current Workspace customer-decision policy. Positive ACCEPTED/VERIFIED and Deal Won remain blocked. No positive ACCEPTED transition is authorized by this document. Existing B1/B2 decisions remain approved and are not reopened.

## Current authority and evidence

Entry SQL contains 141 Quotes. Original CRM demo contains 1,050 records/180 Deals. Entry Git/status/SHA-256 inventories are in `task-runtime/quote-customer-decision/`; SQL summary is in `task-runtime/quote-lifecycle/b3-entry/`.

| Function | Contract | Backend | Frontend | Gap |
|---|---|---|---|---|
| Core, B1 approval, B2 manual delivery | Admitted | Implemented/verified | Connected | Preserve |
| rejectQuoteCommand | POST /quotes/{quoteId}/reject, quotes.update, If-Match, idempotency, QUOTE_REJECTED | Not implemented | Adapter exists; Connected action blocked | Implement independently |
| acceptQuoteAndCloseDeal | POST /workflows/quote-acceptance/{quoteId}/accept-and-close-deal | Not implemented | Named workflow, local Demo simulation only | Strict request is {}; no evidence or Deal version input; no atomic owner participants |
| Generic acceptQuote | Deprecated/BLOCKED | Not enabled | Not a substitute | Must remain blocked |
| Customer response/evidence ledger | No current public command/read schema | No Quote consent verifier/store | No authoritative customer-decision history | Narrow admission needed |
| MarkWon QUOTE_ACCEPTED / ORDER_CONFIRMED | Verification guard | Fail closed | Must not simulate closure | Gate C |

Authority: `design-authority/canonical-design/modules/quotes.md` admits SENT→REJECTED and terminal immutable content. Exact rejection request: reason 1–500 characters and optional note up to 2,000. It does not require fresh approval or an unexpired validity date for rejection. EXPIRED is a terminal state and cannot be rejected. Rejection never means Deal Lost.

## A. Customer identity

Quote already binds an authoritative BuyerRef through official Contacts/Organizations owner contracts. These establish CRM record visibility/eligibility, not customer consent, identity authentication or legal signing power.

- Matching email is not identity/signing-authority proof.
- A Contact link or IsPrimaryRepresentative does not prove authority to bind an Organization.
- No admitted Buyer↔authenticated signer↔Quote revision/content consent verifier was found.
- CommercialEvidence currently supports completed-purchase evidence, not Quote consent. Generic webhook signature verification authenticates transport; it has no admitted Quote acceptance meaning. Communications text is not verified contractual assent.
- No secure retained signed-document/e-sign/portal evidence owner contract was found.

Recommendation: copy BuyerRef from the authoritative Quote into every recorded decision. Do not accept client-supplied BuyerRef, signer-verification flags, verifiedBy or acceptedAt. Customer names/source descriptions remain clearly labeled human reports. No verifier role is inferred from quotes.update or quotes.approve.

## B. Evidence types and trust

| Evidence type | Initial treatment | May make Quote ACCEPTED now? |
|---|---|---|
| USER_REPORTED_ACCEPTANCE | Authenticated employee reports agreement; UNVERIFIED | No |
| CUSTOMER_DOCUMENT_EVIDENCE | Source/integrity/representation unverified without a proper storage/verifier boundary | No; BLOCKED_CONTRACT |
| PROVIDER_VERIFIED_ACCEPTANCE | Requires authenticated provider, retained proof and exact Buyer/Quote/version mapping | No current integration; BLOCKED_CONTRACT |
| CUSTOMER_PORTAL_VERIFIED_ACCEPTANCE | Requires authenticated customer and authoritative representation/consent policy | No current portal contract; BLOCKED_CONTRACT |
| USER_REPORTED_REJECTION | Authenticated employee reports refusal with reason; UNVERIFIED provenance | May commit REJECTED under the independently admitted rejection rules; never Deal Lost |

Proposed trust vocabulary: UNVERIFIED, PENDING_VERIFICATION, VERIFIED, REJECTED_EVIDENCE. B3's proposed human-report command can create only UNVERIFIED. The other trust levels cannot be selected by callers and have no promotion operation until an owner verifier is admitted. This is not a new general approval system.

## C. Manual attestation options

**Option A — recommended: truthful response capture plus rejection; acceptance stays blocked.**

Allow an active actor with quotes.read + quotes.update, current record scope and FLS to record a reported acceptance on an unarchived SENT Quote. Save an immutable UNVERIFIED customer-response record, without changing Quote status/AcceptedAt, Deal, Order or financial commitment. It carries the server's current Buyer/Quote revision/content fingerprint and resource version. A reported response is not a delivery receipt or internal approval.

No document is required merely to capture an unverified claim. No independent reviewer can turn this claim into VERIFIED in B3. There is no value threshold exemption: human reports at every value remain nonbinding. Unavailable proof/storage is reported explicitly. A later verification procedure must establish identity, representation, integrity, revocation/reuse and commercial binding before any promotion.

Rejection uses the existing SENT→REJECTED command, required reason and trusted actor/time. It preserves approval/delivery/commercial content and records the human-report provenance. It does not invent a new positive-approval/expiry prerequisite or change Deal stage.

**Option B — rejection only.**

Implement reasoned rejection and its read history. Do not admit a reported-acceptance command. Keep all acceptance mutations blocked until a proper verified-evidence contract is supplied. Smaller scope; cannot retain a customer's reported agreement in the Quote owner.

**Option C — verified acceptance programme, requires further decisions/contracts.**

Specify a trusted portal/provider/e-sign or controlled independent document-verification process. Decide signer identity/representation, secure immutable proof storage, verifier capability and independence, applicable value/risk limits, revocation/reuse and policy versioning. A staff checkbox or a second staff approval alone is insufficient. This option is not implementation-ready today and cannot be achieved by weakening A.

## D. Proposed narrow admission for Option A

New Quotes-owned operation: `recordQuoteCustomerDecisionCommand`, POST `/quotes/{quoteId}/customer-decisions`, quotes.update with quotes.read also enforced, If-Match and Idempotency-Key required, single Quote transaction and immutable command/outbox. Proposed event: QUOTE_CUSTOMER_RESPONSE_RECORDED. This event represents a human report, never QUOTE_ACCEPTED.

Strict request: decisionId, evidenceType=USER_REPORTED_ACCEPTANCE, sourceChannel (existing supported human-contact channels), sourceDescription (nonblank human account of source), optional reportedCustomerDecisionAt UTC-Z and note. No verified/accepted booleans, Buyer/workspace/actor/fingerprint/version-of-content/verifier fields or upload/external-proof authority are accepted from the client. Unknown fields are rejected. Future reported time is rejected; server recorded time is separate.

The server owns decision ID uniqueness/retry semantics within the Workspace, Quote/revision/Buyer/content binding, immutable provenance and BackendRecordedAt. Reusing an existing evidence/decision identity for another Quote or changed payload conflicts. Same command retries cannot duplicate the decision. A committed report increases resource version but leaves SENT unchanged.

Customer response permission/enablement is owned/versioned by Backend configuration per exact Workspace, with no Demo/default-Workspace fallback. A missing/disabled report policy fails closed. No new verifier capability or Studio UI is implied. Approval policy version is retained separately from customer-response policy version.

Optional Quote read extension: customerDecisionHistory with decision ID/type/evidence type/trust/source, required reason for rejection, BuyerRef, QuoteId/revision, content fingerprint, pre-mutation resource version, recorded/reported times, authenticated actor, policy version without any verifier or trust-promotion fields. Add optional reject/report action hints while accept remains false. FLS covers history and each changed lifecycle field; callers must be able to read the bound commercial content.

Persistence: use a small Quote-owned append-only decision ledger with an additive migration if needed, rather than overwriting notes, approval or delivery data. Include immutable commercial snapshot/provenance in audit/outbox. No new generic evidence framework or cross-owner writes. Existing rejection request stays unchanged; its reason/note remain durable and readable.

## E. Decision gate and limits

The user selected A and authorized the narrow command/read/history persistence additions. Every customer decision command requires a configured, enabled, versioned exact-Workspace policy and current authorization/FLS. Neither reported agreement nor rejection permits ACCEPTED, VERIFIED or Deal Won. Positive Direct Sale acceptance remains BLOCKED_CONTRACT until a verified-evidence command can prove all required preconditions. Linked acceptance additionally remains blocked until Gate C admits atomic owner orchestration and Deal concurrency/authority semantics.

The approved decision is implemented only within Option A. No positive Accepted mutation is activated.

## Approved conflict handling

Each report appends an immutable record. Multiple agreement reports do not overwrite one another. A later rejection may transition SENT to REJECTED while retaining all previous reports and delivery history. Terminal state blocks new reports/decisions; authorized retries may replay their historical receipt under the current enabled policy. No contradictory response is promoted to VERIFIED or resolves itself to ACCEPTED.
