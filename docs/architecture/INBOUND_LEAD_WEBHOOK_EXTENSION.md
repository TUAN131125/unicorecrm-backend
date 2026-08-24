# Inbound Lead Webhook Extension

## Authority

`PROJECT_EXTENSION_INBOUND_LEAD_WEBHOOK`

The adopted frontend OpenAPI has no inbound Lead webhook operation. This backend-local contract was introduced during B07 under explicit project-extension authority. It does not claim historical OpenAPI or Design Authority provenance and does not change the frontend OpenAPI.

The existing integration operation matrix remains separate:

| Operation | Contract | Classification |
|---|---|---|
| `listIntegrationConnections` | `GET /integrations/connections` | READY, implementation deferred; not required by inbound Core |
| `listIntegrationProviders` | `GET /integrations/providers` | READY, implementation deferred; not required by inbound Core |
| `createIntegrationConnection` | `POST /integrations/connections` | BLOCKED |
| `updateIntegrationConnection` | `PATCH /integrations/connections/{connectionId}` | BLOCKED |
| `disconnectIntegrationConnection` | `POST /integrations/connections/{connectionId}/disconnect` | BLOCKED |
| `verifyIntegrationConnection` | `POST /integrations/connections/{connectionId}/verify` | BLOCKED |
| `receiveGenericSignedLeadWebhook` | contract below | `PROJECT_EXTENSION_INBOUND_LEAD_WEBHOOK` |

## Wire contract

- Method/path: `POST /integrations/inbound/leads/{integrationId}`.
- Provider code: `generic-signed-json` only.
- Content type: JSON (`application/json`, including an optional charset).
- Maximum raw body: 65,536 bytes.
- Required headers:
  - `X-Unicore-Delivery-Id`: stable provider delivery identifier, 1–128 characters matching `[A-Za-z0-9._:-]` after an alphanumeric first character;
  - `X-Unicore-Timestamp`: canonical base-10 Unix timestamp in seconds;
  - `X-Unicore-Signature`: `sha256=` followed by 64 hexadecimal HMAC bytes.
- Optional `X-Correlation-Id`: 8–128 characters; otherwise the server trace identifier is used.

The exact signing material is the UTF-8 byte sequence:

```text
<X-Unicore-Timestamp>\n<X-Unicore-Delivery-Id>\n<exact raw request body bytes>
```

The signature is HMAC-SHA256 using the binding's externally configured secret. Comparison is constant-time. The accepted timestamp window is five minutes in either direction from server UTC. Timestamp replay-window validation and durable delivery dedupe are independent requirements.

The JSON object accepts only:

```json
{
  "displayName": "Example Lead",
  "source": "Partner form",
  "estimatedValue": { "amount": "1000.00", "currency": "USD" },
  "email": "lead@example.test",
  "phone": "+10000000000",
  "companyName": "Example Co",
  "description": "Optional"
}
```

`displayName`, `source`, and `estimatedValue` are required by the canonical Leads create validation. The optional properties above are the complete extension shape; unknown properties are rejected. The payload cannot carry trusted `workspaceId`, `memberId`, `ownerId`, capabilities, interested Products, or a Lead identifier. The binding's delegated member is used as the canonical Lead owner. Populated interested Products remain unavailable because the B05 Product snapshot authority gap remains fail-closed.

Successful first processing returns HTTP 200 with `integrationId`, `deliveryId`, server-assigned `leadId`, `outcome: "PROCESSED"`, and `correlationId`. An already processed identical delivery returns HTTP 200 with the same Lead identifier and `outcome: "REPLAYED"`.

Stable error classes are:

| Condition | Status/code |
|---|---|
| Invalid integration or delivery identifier | 400 `WEBHOOK_REQUEST_INVALID` |
| Malformed timestamp | 400 `WEBHOOK_TIMESTAMP_INVALID` |
| Missing/malformed/invalid signature | 401 `WEBHOOK_SIGNATURE_INVALID` |
| Timestamp outside five-minute window | 401 `WEBHOOK_TIMESTAMP_EXPIRED` |
| Unknown, disabled, or unsupported binding | 404 `INTEGRATION_NOT_AVAILABLE` |
| Same delivery identity with changed payload or changed binding authority | 409 `DELIVERY_ID_CONFLICT` |
| Malformed or unknown JSON shape | 400 `MALFORMED_PAYLOAD` |
| Canonical Lead validation failure | 422 `LEAD_VALIDATION_FAILED` |
| Invalid delegated membership or denied `leads.create` | 403 `INTEGRATION_AUTHORIZATION_DENIED` |
| Missing server-side secret/configuration | 503 `INTEGRATION_UNAVAILABLE` (retryable) |
| Other safe processing failure | 503 `INTEGRATION_PROCESSING_FAILED` (retryable) |
| Non-JSON body | 415 `UNSUPPORTED_MEDIA_TYPE` |
| Body over 65,536 bytes | 413 `PAYLOAD_TOO_LARGE` |

No error includes a secret, raw signature, raw payload, database diagnostic, or stack trace.

## Ownership and execution

Integrations owns the endpoint, `IntegrationId`, provider verification, the `integration.InboundBindings` record, normalization, delegated-principal construction, and orchestration. A binding contains provider code, authoritative Workspace reference, delegated member reference, opaque secret reference, enabled state, and timestamps. The signing secret is resolved from `Integrations:Secrets:{SecretReference}` and is never stored in SQL. The current principal model is a Delegated Integration Principal: the Integration executes through a server-resolved active Workspace member. It is not a first-class `ServicePrincipal`. The only provisioning mechanism is an idempotent Development-only bootstrap driven by external configuration; no public connection/Studio mutation is added.

Workspace resolves the binding's Workspace/member pair into an active trusted membership. AccessControl evaluates the member's actual server-side `leads.create` capability through a delegated internal authorization contract. The sender supplies none of these authority facts. The resulting actor is an `Integration` whose actor ID is the server-owned `IntegrationId`; `AuthorizedThroughMemberId` records the distinct delegation subject. No JWT is created or impersonated.

PlatformOperations owns the durable `ops.InboxMessages` record. `(IntegrationId, DeliveryId)` is unique. The record retains the raw-payload SHA-256 hash, provider, original bound Workspace/delegated member, status (`Received`, `Processed`, or `Failed`), attempt evidence, correlation, safe result code, and resulting Lead ID. It does not retain the raw payload or credentials.

Integrations invokes `IInboundLeadIngress`, a narrow, provider-neutral Leads-owned trusted inbound boundary. Leads performs the same canonical create validation and active-owner check, authorizes `leads.create` through AccessControl, derives no identity from external data, assigns the Lead ID, and commits its owner-local idempotency, audit, and outbox records. Its audit provenance records generic actor, delegated-subject, and source-reference values; for this adapter those values represent the Integration, delegated member, and delivery identifier. Integrations never accesses `LeadsDbContext` or calls the host over HTTP.

## Retry and failure semantics

The internal Lead idempotency key is SHA-256 over the namespace `PROJECT_EXTENSION_INBOUND_LEAD_WEBHOOK`, `IntegrationId`, and `DeliveryId`, prefixed with `inbound-lead-webhook_`. It is an idempotency value, never a Lead ID. A same-payload retry resumes a `Received`/`Failed` Inbox record or replays a `Processed` record. Reuse of a delivery ID with a different raw payload, provider, Workspace, or delegated member fails closed.

Inbox and Leads use separate owner-local transactions. If Leads commits and Inbox completion fails, the next retry uses the identical Leads idempotency key, receives the same authoritative Lead, and can converge the Inbox to `Processed`. No cross-DbContext transaction, event bus, worker framework, distributed lock, or Saga is introduced.

## Explicitly unsupported

This extension does not implement provider-specific adapters, OAuth/challenge flows, outbound webhooks, public integration configuration, Studio/WorkspaceConfig mutation, Lead qualification, WF-10, Lead-to-Deal, Lead-to-Task, Contact/Customer/Organization mutation, Quotes, Orders, AI, or frontend integration.
