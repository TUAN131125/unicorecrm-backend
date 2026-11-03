# Customer Quote acceptance and automatic Order draft

Owner directive: the request attached on 2026-10-30 (SHA256 `83d74fdf772810dedf2e9f45f24b833043f1f453bc69306588c1e3b1c0c66873`). This explicitly permits ordinary sales acceptance and automatic draft creation alongside the unchanged B6 verified-signatory path. It does not admit a new manual `convertAcceptedQuoteToOrderDraft` endpoint, confirm Orders, or close Deals as WON.

## Business boundary

Internal approval, ordinary customer acceptance, and VERIFIED acceptance are separate facts. The new sources are `CUSTOMER_LINK` and `STAFF_RECORDED`, both with `ORDINARY_SALES_UNVERIFIED` assurance. A Workspace requiring verified signer authority fails closed. B6 identity, signer authority, source admission, independent review and Development Simulation retain their existing meaning.

The five narrow extension operations are `recordQuoteSalesAcceptance`, `createQuoteAcceptanceInvitation`, `listQuoteAcceptanceInvitations`, `previewCustomerQuoteAcceptance`, and `consentCustomerQuoteAcceptance`. The registry records this explicit owner extension separately from the original conversion operation.

Staff commands require trusted session and Workspace, record scope, capabilities, commercial field access, strong If-Match and an idempotency key. Acceptance also requires `orders.create`. A protected proof reference, when supplied, must belong to the existing document case, be readable, and have current CLEAN content. No client-supplied actor, price, buyer or total is accepted.

Acceptance requires a current SENT Quote, valid Workspace-local expiry, independent internal approval when required, authoritative repricing and payment agreement, and the exact issued PDF and commercial fingerprint. Invitations may also be sent from an eligible issued Draft/Review; only actual Gmail success records delivery and SENT. Opening the public link or previewing it never accepts the Quote. Consent requires an explicit checkbox/POST and the bound terms version. Tokens are random, expire, are persisted as hashes, and bind the Workspace, Quote version, issued document and commercial snapshot. Delivery links are encrypted with ASP.NET Data Protection. Persist Data Protection keys across deployment instances/restarts.

The authenticated history endpoint returns delivery outcomes without token URLs. Gmail uses Workspace-specific credentials and PDF attachment; no IAM credentials are reused. Missing or failed provider configuration yields FAILED, never SENT. An authorized copy-link invitation is explicitly NOT_SENT.

## Persistence and recovery

Quotes owns invitation, acceptance, command audit, outbox and durable pending conversion state. Acceptance and pending intent commit in the same owner transaction. The recovery worker calls an internal Orders owner contract; Orders writes the DRAFT, immutable commercial/payment/acceptance snapshot, creation audit and outbox in one SQL transaction. It allocates ID, number and version on the server. Source Quote, Deal and fingerprint are retained.

This is an idempotent recovery boundary, not a distributed transaction. A SQL lock and unique Workspace/source-Quote index allow exactly one Order per accepted Quote; a new commercial revision is a separate Quote aggregate. If Order commit succeeds but acknowledgment fails, retry verifies the immutable receipt and reuses that Order. If Order creation fails, its complete local transaction rolls back and the pending acceptance remains recoverable. The worker rechecks enabled Workspace and policy, quote status and binding before creating an Order. DRAFT does not imply operational confirmation.

## Configuration

Ordinary acceptance is disabled unless explicitly configured. Example configuration, with a real Workspace ID and deployment-approved values:

```json
{
  "QuoteSalesAcceptance": {
    "PublicBaseUrl": "https://crm.example.com",
    "Workspaces": {
      "WORKSPACE_ID": {
        "Version": "ordinary-sales-v1",
        "Enabled": true,
        "AllowCustomerLink": true,
        "AllowStaffRecorded": true,
        "RequireVerifiedSignerAuthority": false,
        "RequireStaffOperationalApproval": true,
        "InvitationHours": 48,
        "OrderDraftLimits": { "VND": 100000000 }
      }
    }
  }
}
```

Public origin must be HTTPS (loopback HTTP is allowed for local tests). Invitation lifetime is 1–168 hours. Missing currency limits fail closed. For higher assurance Workspaces leave `RequireVerifiedSignerAuthority: true`; this ordinary path is denied.

Gmail: `QuoteGmail:Workspaces:WORKSPACE_ID` has `Enabled`, `Username`, `AppPassword`, `FromAddress`. Store the password in the deployment secret provider. Delivery uses Gmail SMTP 587 with STARTTLS, a stable message ID and an issued PDF. Provider/network failures can be ambiguous; inspect the provider before retrying an uncertain email. No claim of exactly-once external email delivery is made.

Apply the additive Quotes/Orders migrations using the existing deployment process. No migration or seed should target Development for connected verification. No default capability grants are added.

## Reproducible connected verification

`backend/scripts/verify-sales-acceptance-order.ps1 -DatabaseName UnicoreCRM_AcceptanceTest_UNIQUE -Browser` refuses preexisting databases and only accepts the isolated test name prefix. It migrates, seeds a synthetic identity, starts the real host, issues real PDFs, uses HTTP and SQL, and launches real Chromium through `verify-sales-acceptance-browser.mjs`.

Evidence is written under `task-runtime/customer-acceptance-order-20261030/connected`. It includes sanitized HTTP results, SQL invariants, browser results/screenshots and fault-injection recovery results. Tokens and synthetic passwords stay in process memory. Live Gmail success requires separately configured credentials and an authorized recipient; absent these, external delivery is NOT_RUN. The suite verifies the real missing-configuration failure path.

## Order Confirmation follow-up

`confirmOrderWithPaymentPlan` is ADMITTED but NOT_IMPLEMENTED at `/workflows/order-confirmation/{orderId}/confirm-with-payment-plan`. Implement its workflow with Order version/concurrency, operational actor authority, ordinary acceptance review when required, physical-goods recipient/delivery requirements, payment and fulfillment gates, and atomic audit/outbox. Automatic conversion intentionally stops at DRAFT. Deal WON requires its own admitted business operation and authority.
