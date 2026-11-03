# Ordinary Quote issuance and automatic Order confirmation

The internal-approval prerequisites in this historical policy description are superseded by project-owner D1 on 2026-11-02. See [Direct Quote issuance and Studio currency](quote-direct-issuance-and-currency.md). Automatic Order eligibility, operational safeguards and O1/O2 transaction/recovery rules below remain applicable.

The project owner's 2026-11-02 request (`25cf3292-c051-4d59-8c44-6cec7bf41f77`) supplements O1/O2 through `PROJECT_EXTENSION_QUOTE_AUTOMATIC_CONFIRMATION`. Original admission pins and the unimplemented public legacy conversion operation remain unchanged. No conversion endpoint is added.

## Deployment policy

`QuoteSalesAcceptance:Workspaces:{workspaceId}` is the existing server deployment policy. The new settings are opt-in and must be configured for each intended Workspace:

```json
{
  "Version": "ordinary-sales-v2",
  "Enabled": true,
  "OrdinarySalesWithoutInternalApproval": true,
  "AutoConfirmOrders": true,
  "AutoConfirmLimits": { "VND": 100000000 },
  "AutoConfirmProductTypes": ["SERVICE", "DIGITAL"],
  "OrderDraftLimits": { "VND": 100000000 },
  "AllowCustomerLink": true,
  "AllowStaffRecorded": true,
  "RequireVerifiedSignerAuthority": false,
  "RequireStaffOperationalApproval": false,
  "RequirePaymentInstructions": false,
  "InvitationHours": 48
}
```

These amounts are an example deployment limit, not a product default or currency conversion. Existing Workspace module enablement, valid seller information, document policy, issuance capability, record scope and field access remain required. `OrderDraftLimits` retains its existing acceptance eligibility meaning despite its historical name. Automatic conversion checks both current acceptance and confirmation limits. Missing currency limits or product types block conversion.

Ordinary issuance skips persisted B1 assessment; it never writes fake `APPROVED`/`NOT_REQUIRED`. Explicit `QuoteApprovalPolicies:Workspaces:{id}:AlwaysRequireApproval=true` takes precedence over the ordinary bypass. Workspaces without the ordinary opt-in retain B1. Other deployments with special approval rules should leave the ordinary bypass disabled. PDF identity, immutable bytes/hash, expiry, parties and commercial binding still use B5.

The new policy is not enabled by this change in `UnicoreCRM_Development`. For rollout, stop/drain legacy recovery workers, apply the additive migrations, deploy the updated API and every recovery worker, then enable the desired Workspace policy. Do not let a legacy O1 worker consume new auto-confirm intents: its older binary cannot interpret the new frozen flag. The example does not grant any capability to a human actor.

## Transaction and recovery

New ordinary acceptance rows freeze `AutoConfirmRequested=true`. The migration defaults existing rows to `false`, preserving historical DRAFT conversion behavior. New intents with an unavailable automatic policy become BLOCKED; they do not fall back to durable DRAFT creation.

Acceptance remains immutable in the Quotes transaction. The existing recovery worker dispatches new intents to an internal orchestration in the same accepted-Quote workflow. It reuses:

1. O2's SERIALIZABLE shared SQL transaction and receipt lock.
2. Quotes validation/aggregate lock, current policy, immutable acceptance, issued PDF and commercial fingerprint.
3. Narrow Contact/Organization owner participants that return only current buyer eligibility, within the same transaction. These system reads do not create human permissions or expose buyer fields.
4. O1's authoritative `Order.FromAccepted` factory, source uniqueness, creation evidence and outbox.
5. Payments' existing draft factory/schedule invariants, staged inside that transaction.
6. O2's shared operational, credit and fulfillment guards and Payment Plan activation.
7. Orders confirmation, payment activation events and the immutable workflow receipt, committed together.

DRAFT is an uncommitted internal transition on the automatic path. A payment activation failure rolls back Order, Plan, schedules, creation evidence, events and receipt. Acceptance remains retryable. The Quote acknowledgement occurs after the shared commit; if lost, the deterministic acceptance receipt replays the original Order without recreating any owner data.

Automatic confirmations persist `ConfirmedBy=system:quote-policy`, `ConfirmationOrigin=WORKSPACE_POLICY` and the actual policy version. Acceptance retains its original staff or invitation-bearer actor. No human operational review is fabricated. The Order domain rejects a policy origin attributed to a human actor.

Recovery retries transient faults and re-evaluates blocked conditions. It does not create a DRAFT as a success substitute. A frozen requirement for human operational review remains blocked until genuine authority exists; simply removing the current setting does not forge that review.

## Preserved limits

Credit-consuming approval and payment-account instruction authority remain unavailable in the existing owner contracts. Postpaid/credit-required terms and required payment instructions fail closed. Physical goods still require actual recipient/delivery facts; the automatic interface does not invent them. Higher-assurance signer requirements retain B6 and cannot be satisfied by ordinary consent or Development simulation.

No payment collection, receipt of funds, provider success or Deal WON is inferred. Historical manual O2 remains available for genuine historical drafts and keeps its original authorization/version checks. No bulk state update is performed.

## Presentation

`ordinarySales` is a server policy projection, not a grant. Quote Detail uses the existing effective access and document readiness for its primary CTA. Commercial data uses server-projected line totals, and buyer labels use fresh owner reads with field access checks. Versions and higher-assurance tooling remain secondary. Dates use the Workspace timezone. The shared centered modal handles staff acceptance; React never sequences Order/Plan writes.

The OpenAPI supplement adds `ordinarySales`, `autoConfirmRequested`, `BLOCKED` conversion status, `confirmationOrigin` and `confirmationPolicyVersion`. Generated files are updated only with `npm run api:generate`.

## Reproducible verification

Run `backend/scripts/verify-quote-automatic-confirmation.ps1 -Browser`. It creates only a fresh allowlisted `UnicoreCRM_OrderConfirmationTest_*` database, provisions synthetic fixture identities before testing, and runs task-owned hidden ApiHost/Vite processes and headless Chromium. It stops its hosts in `finally`. It never migrates or writes the Development database.

SQL fault triggers and expired/FLS negative fixtures exist only in that isolated database. Public consent is real HTTP/Chromium consent against synthetic test data; it is not evidence of a real customer or live Gmail delivery. Logs, response statuses, SQL rows and screenshots are written under `task-runtime/quote-detail-auto-confirm/` without passwords or bearer tokens.
