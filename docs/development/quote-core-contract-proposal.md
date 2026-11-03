# Quote Core contract proposal

Status: user decision recorded 2026-10-24; implemented scope below supersedes the original proposal where stated.

The user approved OPEN/unarchived Deal sources, backend member ownership, SQL number allocation, the listed currency precision/HALF_UP policy and narrow owner contracts. Manual commercial lines are permitted under Quote write authorization and validation. Product-linked lines use Product-owned catalog snapshots and require current access. Quote creation does **not** confer catalog-edit or price-override permission: new linked lines must match authoritative catalog price/tax; unchanged captured prices remain historical during Draft editing. No automatic currency conversion. Both MarkWon source branches remain fail-closed until commitment verification exists. These instructions supersede the broad negotiated Product-price authorization originally proposed in item 2.

Initial evidence before the user decision: Checkpoint A required identifying missing cross-owner contracts. The global `design-authority/canonical-design/authority/authority-gaps.json` still lists `AG-PRODUCT-SNAPSHOT` for Quotes. `IProductSnapshotReader` deliberately excludes commercial price/tax/billing fields. `IDealSummaryReader` lacks the buyer and archived facts required to verify a Quote source. At that point Quote access facts had no canonical owner. Implemented ownerId facts and narrow commercial/source contracts are documented in the verification record. The global authority-gap entry is broader than this task and still includes Deal line items and direct Orders; this user decision resolves the Quote Draft leg only.

## Original proposal (historical; superseded by the user decision above)

1. **Sources:** enable Direct Sale when the Quotes workspace module is enabled and the actor holds `quotes.create`; require a readable, eligible Contact or Organization. Deal-based creation requires an unarchived OPEN Deal, readable buyer/source fields and current Deal version. Derive and lock buyer, Deal, Contact and Lead relationships from the Deal. Creating a Quote never changes the Deal.
2. **Commercial authorization:** `quotes.create`/`quotes.update` authorize manual lines and negotiated unit prices. No new capability. Product-linked lines require current `products.read` and active eligible Product. Capture Product-owned name, SKU, type, description and billing facts from an authorized commercial snapshot; preserve catalog price/tax/version provenance separately from negotiated Quote price/tax. Reject cross-currency catalog use until an explicit conversion policy exists. Do not modify historical snapshots when catalog changes.
3. **Ownership:** Quote `ownerId` is the authoritative Workspace member fact. Default to the trusted member; explicit owner must be active in the Workspace. Enforce create/update FLS and OWN scope; TEAM/CUSTOM stay closed unless AccessControl supplies authoritative facts. Legacy ownerless Quotes stay ownerless and fail OWN scope.
4. **Money:** Workspace owns allowed currencies. Introduce a server currency precision policy with initial VND/JPY=0, USD/EUR=2 and KWD=3, HALF_UP, input scale at most six. Unsupported currency fails validation until configured. This is a proposed business policy, not something inferred from UI/Intl or DEC-002. Reject negative quantity/price/adjustment and rates outside 0–100; allow zero price and zero total, require positive quantity. Specify and test line/document rounding order against the existing preview formula before admission.
5. **Identity:** SQL sequence issues globally unique non-reusable `Q-` plus at least ten digits; server issues QuoteId. QuoteRevision starts at 1 and remains 1 during Draft edits; ResourceVersion starts at 1 and increments for each accepted replacement. Sequence gaps after rollback are allowed.
6. **Draft replacement:** only unarchived DRAFT. Preserve source identity; replace commercial content atomically. Preserve line IDs when safely matched, otherwise server assigns them. Recalculate all totals and payment preview allocations; reject accepted/payment lifecycle claims in Draft input. Store actor, immutable command audit, outbox and idempotency response in the same SQL transaction.

## Minimal boundary proposal

- **Deals-owned source participant:** authorized trusted-workspace read returning buyer/source/owner/version/eligibility, with a transaction/version fence covering Quote creation. No Quotes access to Deals tables. Place any assembly bridge in the existing composition/workflow project to avoid a Crm↔Sales project-reference cycle.
- **Products-owned commercial snapshot reader:** separate from the frozen Lead reader. Return allowed commercial capture facts plus product version, catalog Money/tax and eligibility; enforce record/FLS authorization at the owner.
- **CRM-owned buyer validation participant:** wrap existing Contact/Organization readers under trusted context; no Quotes references to their persistence.
- **Workspace currency reader:** allowed currencies, precision policy and source configuration version; no client Workspace/actor trust.
- **Quote persistence:** reuse `quotes.Quotes`; additive evidence/idempotency/number allocation schema only. Baseline and after hashes before/after any migration. No database creation, seed, deletion, or connection-string edits.

## MarkWon disposition already authorized by the task

Both `QUOTE_ACCEPTED` and `ORDER_CONFIRMED` must refuse new success and historical unverified replay until an owner commitment-verification contract exists. Check resource permission, record scope, write FLS and expected version. The safe guard requires no new owner contract, migration or Quote Acceptance implementation.

The user decision closes only the listed draft-core choices and internal boundaries. Full acceptance, delivery, approval, Order and Payment remain outside Checkpoint A. Any unresolved wire change must be reviewed explicitly before generated contracts change.
