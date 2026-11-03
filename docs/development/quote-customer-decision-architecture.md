# Quote customer decision architecture

Authority: explicit user approval of Option A on 2026-10-25, recorded in quote-customer-acceptance-policy-decisions.md. This admits employee reports and rejection only. Verified acceptance and atomic Quote/Deal acceptance remain BLOCKED_CONTRACT.

Quotes owns an append-only CustomerDecisionRecords table, keyed by Workspace/DecisionId and linked to the same Workspace/Quote. SQL constrains trust to UNVERIFIED and one ledger decision per resulting aggregate version. Commercial content uses the existing B1 canonical fingerprint; the record binds the pre-mutation ResourceVersion and resulting version separately. Quote, decision, command audit and outbox commit in one owner transaction under aggregate and idempotency locks. No other owner is written.

Exact-Workspace Backend configuration is versioned by label and semantic digest, with no fallback. Both commands require quotes.read/update, record scope and commercial/history FLS. Current authorization and policy are checked before replay. Duplicate identities return existing evidence only for the same actor-bound payload; changed identities/content require current If-Match. Terminal Quotes reject new decisions. Rejection appends UNVERIFIED provenance and retains earlier responses and delivery history.

The additive migration creates only the new ledger/checks/index/FK. No existing Quote, CRM, membership or pipeline row is migrated. Apply only to the verified local Development database through the Quotes DbContext factory; compare entry row hashes afterward. No seeder is used.

The strict new HTTP request exposes human report fields only. Actor, Workspace, BuyerRef, content fingerprint, revision, trust and server time are Backend-owned. Existing accept-and-close transport remains a negative-only guard; generic acceptance and MarkWon remain blocked. Frontend receives optional history/action hints through the generated client and a module-owned read projection; it never promotes trust or locally changes lifecycle.

Verification remains in progress; this record does not assert functional acceptance.
