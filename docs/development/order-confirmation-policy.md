# Order confirmation with Payment Plan

This implements the admitted `confirmOrderWithPaymentPlan` operation at `POST /workflows/order-confirmation/{orderId}/confirm-with-payment-plan`, under the explicit owner request attached on 2026-11-01. That request excludes automatic Deal WON and collection; it supersedes the earlier operation description's optional Deal closure. Original pins are retained. A narrow record in the existing project-extension registry documents the request supplement and participant edges.

## Analysis and invariants

The entry tree had persisted O1 Orders and read-only Payment Plans, with no confirmation writer or transaction participants. Accepted commercial and payment snapshots remain authoritative. A new public Direct Order form is unnecessary. Internal approval, ordinary customer acceptance, operational approval, and production B6 VERIFIED remain distinct.

The authenticated actor requires `orders.confirm`, a valid trusted Workspace, readable commercial/acceptance fields, writable confirmation fields, and mutable record scope. A read-only record scope does not become writable merely because it is visible. The Orders record descriptor publishes only the implemented confirmation command; unrelated editing operations remain unavailable.

The request names `paymentPlanId` and `expectedPaymentPlanVersion`; strong `If-Match` binds the Order version. Operational review uses `operationalReviewConfirmed` and bounded review notes, recorded by the trusted session member. Client actor, buyer, totals, receipts and inline credit overrides are rejected. Optional fulfillment fields let the confirmer supply the recipient/phone/address for physical goods, subject to field security and persisted-document validation. These fields do not change the commercial snapshot.

Ordinary STAFF_RECORDED acceptance requires this review when its frozen requirement or current Workspace policy requires it. The confirmer's existing capability is the authority for this operational review; no independent B6 review, verified identity or signer authority is inferred. CUSTOMER_LINK remains ordinary/unverified. Workspaces requiring verified signer authority fail closed on the ordinary path.

## Architecture and transaction decision

Workflows owns the confirmation receipt. Orders, Quotes and Payments each expose a narrow participant contract. One local SQL connection and SERIALIZABLE transaction are created by Workflows infrastructure; each owner enlists only its own EF context using that connection/transaction. There is no foreign-table write, distributed transaction or pair of independent commits.

The transaction obtains idempotency and Order locks, authenticates and checks record/field authority, checks the immutable receipt, then checks Order version/state, Quote/acceptance/buyer binding, commercial/payment snapshot equality, operational review, fulfillment and credit blockers. Orders writes CONFIRMED, UTC `confirmedAt`, session `confirmedBy`, review notes, version and outbox while still uncommitted. Payments then validates the exact DRAFT Plan version, agreement, buyer/currency/kind and schedule binding; it activates the Plan and schedule, advances versions and writes its event. The workflow receipt commits last in the same transaction.

A participant failure rolls everything back, including review data, schedule updates, audit receipt and outbox. An acknowledged or lost-acknowledgment retry returns the original receipt as REPLAYED. Changed payload/version with the same key is rejected. Unique Workspace/Order receipt and current-plan indexes, application locks and concurrency tokens protect competing intents. Event IDs in the mutation response identify actual persisted Order and Plan events; audit evidence identifies the persisted receipt scope.

O1 recovery now ensures a Payment Plan DRAFT before acknowledging the new Order. A bounded keyset recovery scan also prepares a missing plan for existing acknowledged O1 drafts. Preparation is idempotent under a Plan lock and does not confirm the Order, change acceptance, collect money or create receipts. Confirmation still revalidates all requirements independently.

## Payment semantics and limits

The Plan copies the immutable Payment Agreement and creates server-owned schedule IDs. Agreement revision is distinct from Plan resource version. Activation advances Plan/schedule versions without satisfying any amount or recording payment evidence. ORDER_CONFIRMED calendar-day due dates use the Workspace's local business date. Missing timezone or unsupported business-calendar authority is blocked.

This slice supports confirmation without a credit blocker. Required credit approval, future/postpaid credit exposure, and supplied credit-approval references fail closed until an authoritative Credit Approval consuming participant exists. The read-only approval summary on an Order is not sufficient authority. No fake approval is created or consumed. A supplied payment-account reference is likewise blocked until the account owner can validate and create real instructions. The response accurately returns `paymentInstructionCreated: false` for the supported path.

Invoice, shipping execution, returns/refunds, payment collection, real Gmail and automatic Deal WON are outside this implementation. No simulation supplies production B6 authority.

## Connected verification

`backend/scripts/verify-order-confirmation.ps1 -DatabaseName UnicoreCRM_OrderConfirmationTest_UNIQUE -Browser` requires a fresh isolated test database and never targets Development. It reuses O1 helper functions but creates new Quotes, issued PDFs, delivery/acceptance and Orders for O2. It uses the existing explicit Development owner bootstrap to establish separate predeclared synthetic fixture identities/roles before business commands. No runtime role is elevated after a failed assertion, and no RBAC or successful confirmation/approval state is written with SQL.

Seller/features are configured through Studio HTTP operations. SQL mutations are confined to named negative corruption/fault fixtures in that isolated database, including real activation triggers and missing-plan recovery. All successful confirmations and plan activation/recovery occur through the actual implementation. Chromium uses the connected UI and forcibly drops an upstream committed response; the real HTTP retry must obtain the original REPLAYED receipt.

Apply the additive Orders, Payments and Workflows migrations through the normal deployment procedure. This task applies them only to isolated test databases. No commit, push, reset, merge, rebase or graduation ZIP overwrite is authorized.
