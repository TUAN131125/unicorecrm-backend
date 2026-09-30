# O3 single Lead owner assignment — local acceptance

O3 changes only owner/profile scope, version and command evidence. It does not transfer Tasks or change Activities. Claim remains unchanged; Bulk Assign and Handover stay unavailable. O3 is not frozen and needs controller source review after delivery.

## Analysis and architecture

The frozen generated contract already declares POST /leads/{leadId}/assign with ownerId/reason, leads.assign, If-Match and Idempotency-Key. The dedicated handler applies current resource/read/record authorization before replay or state disclosure, validates a real active same-workspace member through the Workspace contract, and writes only ownerId/ScopeOwnerId via AssignOwner. OWN unassigned read authority does not allow Assign. No permission policy changes.

Assign reuses the existing Claim UPDLOCK/HOLDLOCK loader under Serializable isolation; Claim source and its special scope exception remain unchanged. Version validation precedes same-owner/lifecycle mutation so a stale loser receives 412. Fresh same-owner intent returns typed 409 LEAD_OWNER_ALREADY_ASSIGNED without mutation. Normalized owner/reason and expected version form the fingerprint. Current authorization gates replay.

Existing audit rows lack ownership reason/previous/new owner. A generated Leads-owned migration adds nullable EvidenceJson; old rows remain compatible. Assign writes complete evidence to audit and outbox atomically with owner/idempotency. Other commands leave audit EvidenceJson null. No foreign schema writes.

## Reproduction

- `dotnet build --no-restore`
- `scripts/verify-lead-owner-assign.ps1 -DatabaseName UnicoreCRM_O2Claim_O3_<isolated-suffix>` runs frozen O1 + Claim regressions, then dedicated Assign cases and both real HTTP/SQL races.
- `scripts/verify-access-control-record-access.ps1 -DatabaseName <isolated-database> -KeepDatabase`: 567 PASS / 0 FAIL.
- `dotnet ef migrations has-pending-model-changes --project src/UnicoreCRM.Crm --context LeadsDbContext --no-build`: no pending changes.
- `git diff --check`

## Evidence and limits

See `lead-owner-assignment.json` for exact actors, Lead IDs, initial/final versions, statuses and durable counts. Logs are local in D:/Project_All/UnicoreCRM-o3-evidence. No tokens or test database files are committed. Source review is independent read-only review without a governed reviewer-principal attestation; it does not authorize FROZEN.

Frontend gate quality.lead-owner-assign proves dedicated HTTP transport, required reason, pending duplicate suppression, no optimistic mutation, authoritative projection, exact selection removal, Queue refresh, stable ambiguous retry and explicit new-version confirmation. Dialog warning reads authoritative scoped OPEN Tasks; Tasks and Activities never mutate. Frozen Claim, pagination, saved-view behavior and the 350px Work Panel structure remain intact.

Remaining Products effective-access and Lead export assertions reproduce on the exact frontend starting SHA d586604bf0d34d583c060bc1919473b5729db5f7. They are PRE_EXISTING and were not repaired. Full suite stops at the export assertion; this report does not claim a green full suite.

## Final source-review follow-up

Independent backend review requested typed race loser codes, successful-command owner matching, and replay after capability/scope loss. Permanent real-host checks now cover them. Frontend review requested Task observation before Confirm, retained ambiguity across close/reopen and authority loading, and applying version observations received while pending. The implementation and real JSDOM Action fixture now cover these paths; a completed draft waits for Tasks, OPEN Tasks do not block once observed, and the same key/version survives authority reload plus dialog closure. All inspected review findings were resolved. This is source review, not governed freeze attestation.

Additional repository-mandated gates were run: guidance contracts/runtime fail on the unchanged AI route; presentation responsibility fails on ContactDetail; mutation authority fails on its unchanged pinned inventory. Backend readiness passes on a checkout without ignored test output but reproduces the current failure on the exact starting SHA when the four pre-existing ignored error-context Markdown files (last written 2026-09-27) are copied into the isolated baseline. User artifacts are preserved. `npm run verify` stops at architecture (current 6/318, baseline 6/317); all 13 current violations are contained in the 35 baseline violations. These are PRE_EXISTING. No global repairs are included.
