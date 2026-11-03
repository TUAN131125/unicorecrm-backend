# Quote delivery — B2 authority and implementation proposal

2026-10-24. Status: APPROVED by the user on 2026-10-24, including the optional delivery response metadata. B1 policy is not reopened. Workspace timezone must be valid for every new delivery. recordedAt is server time; reportedSentAt is an unverified human claim. Version before/after mutation must remain distinct. No Studio permission expansion or provider/PDF-byte verification is admitted.

## Current evidence

Entry Git snapshots: `task-runtime/quote-delivery/`. Backend master13cd39e; Frontend feat/deal-compact-kanban-card814cc46; both dirty and preserved. Fresh SQL baseline:62 Quotes; complete original CRM/Quote/Pipeline/migration evidence is under `task-runtime/quote-lifecycle/b2-entry/`.

| Existing function | Contract | Backend | Frontend | Gap |
|---|---|---|---|---|
| Draft create/update and authoritative Product/pricing | Ready | Implemented | Connected | Preserve |
| B1 request/approve/request changes | Ready | Implemented/verified | Connected | Reuse fresh policy/fingerprint verification |
| recordQuoteSendEvidenceCommand POST /quotes/{quoteId}/send | Ready; quotes.update, If-Match, idempotency | Not implemented | Typed adapter and public command exist | Implement under Quotes transaction |
| Manual confirmation | USER_CONFIRMED_SENT admitted by B2 request | No persisted delivery command | Explicit modal with recipient/time/note and stable delivery identity | Recipient/expiry/resend/time semantics need explicit policy |
| Delivery read history | Closed read DTO: id/channel/sentAt/fingerprint plus optional evidenceType/recipient/note/actor/fileName | JSON column and read projection exist | History presentation exists | No explicit recorded-vs-reported time, resource version, revision or approval-policy metadata fields |
| PDF preview/export | DOM/html2canvas/jsPDF | No server PDF/byte/storage authority | Works only with authorized document configuration; minimal users see explicit unavailable notice | No narrow seller-document read boundary; never grant Studio to resolve it |
| Gmail compose/share/download | Client helper | No trusted provider participant | Helper opens compose/share/download | None proves send/provider delivery |
| Provider accepted/delivered | Enum only | No verified receipt/callback | Caller could select enum through HTTP | Must reject, never infer provider truth |

Canonical Quotes authority admits Draft/Review→Sent and immutable Sent commercial content. It does not define channel-specific recipient rules, date cutoff or actual resend semantics. Current browser behavior is supporting evidence, not business authority. Existing Workspace timezone reader can supply an authoritative timezone; no cross-owner table access is needed.

## Approved B2 policy

1. Manual send uses quotes.read +quotes.update, trusted active actor/Workspace, record scope and FLS. Only USER_CONFIRMED_SENT is admitted. First send accepts unarchived Draft/Review; a real resend accepts Sent with unchanged commercial content, new DeliveryId, current If-Match and current policy/approval binding. Terminal states are refused. Same DeliveryId/content is a replay; changed payload conflicts. A new DeliveryId is a distinct attested delivery, never inferred from an HTTP retry.
2. Sending requires the existing B1 assessment: current NOT_REQUIRED or valid independent APPROVED, matching server commercial fingerprint and current effective policy version. The send command re-evaluates authoritative totals/terms, but creates no approval decision and never substitutes the sender for the approver. Missing/stale B1 evidence blocks, including resend; an immutable Sent record cannot silently reapprove itself.
3. ValidUntil is inclusive through the Workspace local calendar date; expired means server-derived local date >ValidUntil. Null means no date expiry. Every new delivery requires a valid authoritative Workspace timezone; missing/invalid timezone fails closed even when ValidUntil is null. Dates use server clock, never client sentAt.
4. GMAIL/EMAIL require valid recipientEmail. If Quote already has a commercial recipientEmail, the delivery email must match it (case-insensitive); changing it requires a commercial edit and B1 reassessment. ZALO/CHAT_APP/SMS/OTHER/PDF require a nonblank recipient/destination up to320 characters. This is an authenticated human claim, not verified ownership/delivery of that destination. PDF means a manually delivered file, not local PDF export.
5. Optional client sentAt is an unverified reported time, stored separately and never used as verified delivery time or to bypass expiry. Reject reported future times greater than server-recorded time; historical claims remain claims. Server clock supplies the actual recorded time. UI labels both separately.

## Admitted narrow delivery read-contract extension

Keep the existing strict send request unchanged. Add optional metadata to QuoteDeliveryRecordReadModel only: recordedAt, reportedSentAt, quoteRevision, resourceVersion (the commercial version being attested, before delivery mutation), approvalContentFingerprint, approvalPolicyVersion, documentAuthority=COMMERCIAL_CONTENT_ONLY. Existing sentAt will be server-recorded time for new Backend evidence; it will not echo a client claim as verification. Existing legacy sentAt records stay readable and are not relabeled as verified provider evidence.

The extra metadata allows history to display the exact version and distinguish server recording from human-reported time without inventing provider truth. Internal immutable audit/outbox retains the full authoritative commercial snapshot, actor, both times and bindings. No actual PDF byte hash/storage proof is claimed. Use the existing Quote JSON+immutable command/outbox transaction if sufficient; no new table is required solely for history presentation.

## Document boundary / explicit limits

Review existing Workspace/Invoice seller contracts before any narrow read design. The default B2 scope may leave document issuance BLOCKED_CONTRACT while manual commercial-version evidence works. No grant of Studio or broad configuration read, no foreign Workspace cache/default fallback, and no fabricated seller identity. A new seller read contract, if necessary, needs its own explicit admission; it is not silently authorized by the optional delivery read metadata proposal.

## Implementation boundaries

Quotes owns validation, delivery identity uniqueness, append-only evidence, state/version mutation and SQL transaction. Reuse B1 assessment, auth/scope/FLS, aggregate lock, current-access replay projection, immutable command audit and outbox. Preserve all62 entry Quotes, B1 fixtures and existing CRM/Workspace/ACL/Pipeline data; create fresh B2 Quotes only through commands. Provider delivery, PDF byte authority, B3 acceptance and Deal Won remain blocked. No CRM seed, commit or merge.
