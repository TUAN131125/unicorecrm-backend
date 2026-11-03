# Control-plane extension and Manual Quote continuation

Date 2026-10-29. The project owner explicitly approved a scope-limited control-plane change in the current conversation; this is distinct from the previously approved manual-acceptance business policy. Results: **CONTROL_PLANE_EXTENSION_PASS: NOT ACHIEVED**; **MANUAL_QUOTE_OPERATION_ADMITTED: NO**; **MANUAL_QUOTE_ACCEPTANCE_FUNCTIONAL_PASS: NO**. Local safety tooling is implemented and tested, but a complete authenticated adoption workflow and required review are not established.

## Source and SQL

Fresh entry inventories under `task-runtime/control-extension-manual-20261029/` contain HEAD/branch, NUL-delimited status and Unicode-safe file hashes, plus patches for backend, frontend and control. Existing dirty work was retained. Local Development SQL entry/final: 117,348 original PK rows / 160 tables, zero changed/missing, zero additions, zero migration changes (`sql-preservation-final.json`). No SQL business mutation, role grant, migration, reset or reseed. Historical SQL mutation provenance remains PARTIALLY_ATTRIBUTED.

## Reconciliation of 281 and 312

Current pinned OpenAPI has 281 operations and current index has 312 keys; scratch baseline generation previously omitted 31. For every one of those 31, `extension-inventory.json` records the exact shard authority, matching `project-extensions.json` entry, decision/verification evidence when declared, source operation method/path and status. All 31 have a matching authorityId/project-extension listing and appear in frontend OpenAPI. They group into seven project-extension records: manual-source workflow 11, acceptance quarantine 7, document issuance 5, independent review 3, trusted scanner 2, readiness 2 and customer response 1. Twenty-three lack a `decisionEvidence` field in the extension row; this inventory does not establish those operations as cryptographically verified or authorize bulk admission. Preserve the index/shards; do not silently delete or automatically trust them.

## Changes actually implemented

1. `.unicore-ai/tools/generate_operation_admission.py`: reject duplicate/unsafe operation IDs and any generation that omits existing indexed operations or existing shard filenames, **before writing output**. It no longer deletes stale shards. This is a fail-closed safety repair to the pre-existing generator, not an extension adoption mechanism; it does not automatically merge unverified entries, atomically publish multiple outputs, or establish new authority.
2. `.unicore-ai/tools/inspect_contract_proposal.py`: read-only preflight verifies the supplied canonical baseline SHA and reports missing/moved/replaced/new operations, modified canonical operation objects and indexed operations absent from candidate. Output always says `PROPOSAL_ONLY_NOT_ADMITTED`, never writes or issues an approval. It does not perform complete OpenAPI, component schema, authorization or external trust validation.
3. Six generator and four preflight regression tests in sibling test files. These are local tests, not formal independent review or trusted attestation.

The current frontend-based draft has 361 operations: 80 new vs canonical, 20 modified canonical operation objects, 0 missing/moved IDs and 0 indexed IDs missing (`proposal-preflight.json`). It cannot be adopted wholesale merely to add the manual operation. Path-level inherited metadata and referenced component changes also require separate review.

## Validation

- Generator regression: 6 PASS, including preservation of indexed extension, orphan shard, duplicate/unsafe ID and checksum failure.
- Proposal preflight regression: 4 PASS, including unapproved proposal remains not admitted and no file writes, canonical hash mismatch and duplicate ID.
- Existing bounded verifier suite: 22 PASS; unchanged and still rejects unknown operations/untrusted approval claims.
- Generator run against a task-owned copy of the real index/shards: refused with `UNRECONCILED_EXISTING_OPERATIONS`; all 314 copied files remained hash-identical (`real-index-rehearsal.json`). No live registry was generated over.
- Existing pinned `markDealWonCommand` provenance remains CURRENT_VERIFIED. `confirmManualQuoteAcceptance` remains AUTHORITY_INVALID (`INTEGRITY_ENTRY_MISSING`, `OUTSIDE_BOUNDED_ALLOWLIST`); authoritative lookup still has no operation. No capability, API endpoint, role grant, migration, UI or ledger was added.
- Python compile for touched tools/tests PASS. No application HTTP/SQL success, browser journey or functional regression for new functionality is claimed.

## Outstanding adoption and review

The owner authorized the control-plane work; this does not by itself authenticate an exact adopted API baseline or create an independent reviewer/attestation. The active `solo-development-v2.json` calls for formal Code Quality and Independent Review plus trusted evidence for security-sensitive/critical changes; `_bounded_operation_authority.py` states that a bounded-owner authentication policy is not admitted and refuses to treat a local signature-shaped claim as owner authority. Required next work is a reviewed, policy-compatible exact contract/adoption mechanism and authenticated owner adoption of the actual reconciled subject, including all current extension provenance, schemas, security/error/FLS and integrity pins. Do not widen the bounded allowlist, treat a proposal as admission, use reviewer keys as owner keys or invent signer identities. The project owner's approval specifically directs identification of any separate adoption act; no adoption act has been claimed here.

Once the new operation has validated, review-backed canonical admission and CURRENT_VERIFIED provenance, create the Quote implementation task and implement the owner-local ledger/command/capability and connected UI, followed by real HTTP/SQL/Chromium acceptance. Deal WON and B6 operational verification remain out of scope.

No commit, push, merge, reset or cleanup of pre-existing dirty work. Final source delta is in `source-delta.json`.
