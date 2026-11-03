# Direct Quote issuance and Studio currency

Authority: project-owner decisions D1/D2/D3 in the 2026-11-02 request `a4f93520-c606-47c3-8c99-e3abd57acc39`. This project extension supersedes the internal-approval prerequisite described in `quote-automatic-confirmation-policy.md`. Original admission pins, compatibility thresholds and operation registry dispositions remain unchanged.

## D1: commercial issuance without internal approval

Internal approval is no longer a prerequisite for any Workspace or Quote value. The retired `OrdinarySalesWithoutInternalApproval` opt-in and `AlwaysRequireApproval` exception cannot restore that prerequisite. No approval policy assessment, independent internal approver or persisted `APPROVED`/`NOT_REQUIRED` is needed for a new issuance.

The following paths consistently use this rule:

- B5 issuance and its action availability assessment.
- B2 recording of actual customer delivery.
- Staff-recorded acceptance and customer-link invitation/consent eligibility.
- Automatic Quote-to-Order binding validation inside the existing shared transaction.
- The commercial prerequisites of B6 readiness and manual documentary source checks.

B6 signing authority, independent evidence/source verification and production assurance remain separate. Development Simulation is still confined to its existing simulation boundary; no simulated source becomes production `VERIFIED` acceptance. Credit authority, required payment instructions, fulfillment details, operational approval, source enablement and acceptance/automatic-confirmation risk limits remain enforced.

B5 continues to authorize the issuer, record and rendered fields; require the expected resource version; validate Workspace, parties, currency, commercial snapshot, expiry/archive/revision state; hold the existing leases; render and persist immutable bytes/hash; and commit receipt/audit/outbox atomically. The document binding includes commercial fingerprint, seller, buyer, template and document policy. `direct-issuance-v1` identifies the new issuance policy. The existing document/delivery field named `ApprovalPolicyVersion` remains a compatible storage slot; this value is an issuance-policy identifier, not an approval decision.

Historical approval fields and audit history remain readable. Commercial draft replacement preserves those fields rather than clearing them; they are historical and never authorize issuance or acceptance. Since replacement no longer writes approval fields, it does not require write access to those retired fields. Commercial write/FLS checks remain. Legacy B1 contracts and explicitly invoked compatibility commands are retained; the active product UI and new commercial paths do not invoke them. No fake historical approval is synthesized.

No migration is required. Existing documents and commercial snapshots are not rewritten.

## D2: one workflow owner

Quote Detail has one progressive workflow for issuance, delivery, customer response and the linked Order. Completed steps collapse; the current step expands; future steps show their names. The workflow owns its single primary action and renders it into the header using a React portal. Document and acceptance bodies share one Workspace-scoped document query. Their previous independent card frames, duplicate primary actions and progress panel are removed.

Header actions open target-bound forms. A completed issuance/acceptance request is not replayed when its completed step remounts. Successful receipts resolve their local attempt; definitive validation rejection permits correction, while ambiguous failures retain the same idempotency key/version. Validation errors remain visible inside the active dialog.

The linked Order is freshly read through the Orders public query boundary. Its persisted number, state and exact total are displayed. An acceptance intent cannot label a historical `DRAFT` as `CONFIRMED`. The frontend does not sequence Order/Payment Plan commands. Existing O1/O2 transaction, recovery and frozen intent semantics remain intact; no Deal WON or payment collection is added.

The main table displays stored unit price, discount, tax and line total. Payment terms display the saved agreement. Activity times use the Workspace timezone; DateOnly validity remains a calendar date. Approval menus, bulk actions, badges, create-form assessment, handlers, obsolete feedback/progress components and bilingual guidance are cleaned up.

## D3: current Studio default; original commercial money

New Quote authoring waits for the current Workspace operational configuration and initializes from its Studio base currency before considering source Deal data. A source Deal in another currency retains its original display amount/ISO; its monetary lines are not copied without conversion authority. The user receives an explanation and selects compatible products or enters new prices. The catalog filters matching currencies and rejects a mismatched selection.

An accepted route-source change resets the selected Deal and reads its current authoritative detail, preventing a reused direct-create form from silently ignoring `dealId`. Workspace/configuration updates do not overwrite dirty monetary input.

An existing Quote keeps its original currency, exact line prices, totals and agreement. Currency changes with existing monetary inputs are refused rather than relabeling them. Detail keeps the original ISO and explains an unavailable conversion when Studio differs. Exact decimal strings from existing HTTP money DTOs are preserved alongside legacy numeric display aliases; the new commercial formatter uses BigInt/string formatting, with no FX or Number conversion. Order linkage also displays the owner's exact money DTO.

No FX engine, currency migration, localStorage fallback, alternate buyer lookup or mock-data fallback is added. Backend currency/precision, same-currency pricing and agreement validation remain authoritative.

## Verification boundaries

All write fixtures use fresh `UnicoreCRM_OrderConfirmationTest_*` SQL databases, real HTTP and headless Chromium. Development was used for read-only before capture and read-only preservation checks. The older P0 checksum baseline predates this task and includes a subsequent pre-task Quote edit; its strict mismatch report is retained. Current-task preservation is separately evidenced by captured Quote versions and owner journals. See the verification report for exact runs, hashes, screenshots, limits and gate results.
