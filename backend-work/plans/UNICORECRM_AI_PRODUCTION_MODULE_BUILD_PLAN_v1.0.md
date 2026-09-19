# UniCoreCRM — Production AI Module Build Plan

**Plan ID:** `UNICORECRM-AI-PRODUCTION-PLATFORM`  
**Version:** `1.0`  
**Milestone:** `CRM-AI-002 — Production Provider Platform`  
**Depends on:** `CRM-AI-001 — CRM-aware Read-only AI Chat`  
**Status:** READY FOR IMPLEMENTATION  
**Roadmap authority:** This is a subordinate execution plan. The existing UniCoreCRM master/roadmap plan remains authoritative for module ordering and roadmap scope.

## 1. Canonical starting state

Backend canonical baseline:

`cf1b9e08cfad3a41baf844a2e9a6cacec7b15bf7`

Frontend canonical baseline:

`1be2e15e3d6aa73c1e201ccc30da917309439a95`

The current local CRM-AI-001 vendor-neutral implementation must be preserved and incorporated into this milestone.

Do not rebuild CRM-aware AI Chat from scratch.

Current vendor-neutral foundation is considered implementation-ready based on the completed gates:

- six CRM contexts: Lead, Contact, Organization, Customer, Deal, Task;
- owner-authorized/field-filtered context;
- server-generated grounding evidence;
- bounded conversation;
- prompt-injection isolation;
- durable execution ledger;
- canonical OpenAPI/generated client;
- connected frontend;
- real ApiHost + SQL verifier;
- real connected browser E2E;
- clean/baseline-upgrade migration proof;
- bundle/build/typecheck/lint gates.

## 2. Product objective

Deliver a production-ready AI provider platform for UniCoreCRM where each Workspace can securely configure an admitted AI provider while preserving the existing CRM authorization and grounding model.

Initial admitted providers:

- `GEMINI`
- `OPENAI`

Initial deployment default:

- `GEMINI`

Provider configuration belongs to Workspace Settings / Studio and is not selectable from AI Chat.

Only Workspace Owner or a principal with the required AI configuration capability may manage provider configuration.

The completed runtime must follow:

```text
Authorized CRM context
        ↓
AI Orchestrator
        ↓
Workspace AI Policy
        ↓
Primary Provider
        ↓
optional same-provider retry
        ↓ transient eligible failure
Controlled Fallback Provider
        ↓
Validated grounded response
        ↓
Execution + Provider Attempt evidence
```

## 3. Locked AI semantics

AI remains read-only/advisory.

AI may:

- read authorized CRM context;
- summarize;
- explain;
- identify risks and attention points;
- suggest next actions;
- navigate users to CRM records.

AI must not:

- create records;
- update records;
- archive records;
- assign records;
- execute workflows;
- create Tasks;
- send email;
- send webhook;
- modify Lead/Deal/Customer lifecycle;
- impersonate a user.

Provider-platform work must not unblock existing AI mutation APIs.

## 4. CRM data authority

AI continues consuming owner-approved projections for:

- Lead
- Contact
- Organization
- Customer
- Deal
- Task

AI must not directly read owner DbContexts.

Each owner remains authoritative for:

- Workspace isolation;
- capability checks;
- record visibility;
- field security;
- lifecycle visibility;
- safe projection;
- owner-read audit evidence.

Authorization and field filtering must complete before provider invocation.

## 5. Grounding and conversation invariants

Server-generated grounding evidence remains authoritative.

Provider output must not be able to manufacture evidence identifiers.

Conversation remains bounded:

- maximum 12 messages;
- maximum 2,000 characters per message;
- maximum 8,000 characters total conversation context.

Conversation history is not CRM truth and cannot grant additional permission.

CRM strings embedded in provider prompts are untrusted data, not instructions.

Workspace transition/disposal must clear prior Workspace conversation/context.

## 6. Provider architecture

```text
CRM AI Chat
    ↓
AI Orchestrator
    ↓
WorkspaceAiPolicyResolver
    ↓
AiProviderExecutionController
    ↓
IAiProvider
    ├── GeminiAiProvider
    └── OpenAiProvider
```

Provider adapters own vendor-specific transport only.

They do not own:

- Workspace authorization;
- CRM context resolution;
- failover policy;
- business semantics.

## 7. Workspace configuration authority

Provider configuration is Workspace-level trusted configuration.

It exists only under:

```text
Settings / Studio
└── AI Configuration
```

AI Chat must not expose provider/model configuration controls.

Required capabilities:

- `ai.configuration.read`
- `ai.configuration.manage`

Semantics:

### `ai.configuration.read`

Allows viewing safe configuration state:

- provider;
- model;
- status;
- fallback policy;
- credential source/status;
- safe provider health/usage metadata.

Never exposes secret values.

### `ai.configuration.manage`

Allows:

- create/update AI configuration;
- change provider;
- change model;
- set/rotate credential;
- configure fallback;
- test connection;
- activate/deactivate configuration.

Workspace Owner has these permissions through the existing owner authority model.

Other roles require explicit capability assignment.

Frontend permission state is UX only; backend remains authoritative.

## 8. Workspace AI configuration model

Conceptual domain:

```text
WorkspaceAiConfiguration
├── WorkspaceId
├── Status
├── PrimaryProvider
├── PrimaryModel
├── PrimaryCredentialSource
├── PrimaryCredentialReference
├── FallbackEnabled
├── FallbackProvider
├── FallbackModel
├── FallbackCredentialSource
├── FallbackCredentialReference
├── RetryPolicy
├── FailoverPolicy
├── Version
├── CreatedAt
├── UpdatedAt
└── ActivatedAt
```

Suggested lifecycle:

```text
UNCONFIGURED
      ↓
DRAFT
      ↓ successful validation/test
ACTIVE
```

A failed pending configuration must not destroy the currently active configuration.

Configuration updates require optimistic concurrency.

## 9. Credential policy

Supported credential sources:

- `WORKSPACE`
- `DEPLOYMENT`

Resolution:

```text
Workspace credential
        ↓ if absent
Deployment credential
```

Requirements:

- no plaintext credential in ordinary configuration persistence;
- no secret returned from GET APIs;
- no secret in audit logs;
- no secret in AI execution logs;
- no secret in browser storage;
- credentials remain server-side;
- credential rotation is explicit;
- failed replacement does not invalidate current active credential.

Use existing security/protection facilities where appropriate.

## 10. Provider and model catalog

Backend owns the authoritative admitted provider/model catalog.

Initial provider identifiers:

- `GEMINI`
- `OPENAI`

Each provider contributes:

- provider ID;
- display name;
- admitted model list;
- model capability metadata needed by UniCoreCRM;
- safe availability/configuration metadata.

Workspace Settings may only select catalog entries.

Do not allow arbitrary free-text model IDs in normal Settings UX.

Model catalog changes must not require changing AI Core contracts.

## 11. Production provider adapters

Retain `IAiProvider` as the provider-neutral boundary.

Implement:

- `GeminiAiProvider`
- `OpenAiProvider`

Normalized provider result should expose safe metadata equivalent to:

```text
ProviderExecutionResult
├── Provider
├── Model
├── ProviderRequestId?
├── InputTokens?
├── OutputTokens?
├── Duration
├── Response
└── SafeDiagnostics
```

Normalize failures at least into:

- `UNAVAILABLE`
- `TIMEOUT`
- `RATE_LIMITED`
- `NETWORK_FAILURE`
- `AUTHENTICATION_FAILURE`
- `INVALID_REQUEST`
- `INVALID_RESPONSE`
- `SAFETY_REFUSAL`
- `CANCELLED`

Adapters must support cancellation and finite timeout.

They must not implement cross-provider fallback internally.

## 12. Gemini provider

Gemini is the initial default production provider.

Gemini adapter must support:

- server-side authentication;
- admitted model selection;
- structured output mapping;
- timeout;
- cancellation;
- rate-limit mapping;
- unavailable/5xx mapping;
- provider request ID when available;
- token usage when available;
- malformed response normalization;
- safe diagnostics.

Gemini must be usable as:

- deployment default;
- Workspace primary;
- Workspace fallback.

Credentials must never reach frontend.

## 13. OpenAI provider

OpenAI is the initial optional production provider.

Backend provider identity is:

`OPENAI`

Do not use `CHATGPT` as the provider/domain identifier.

OpenAI adapter must provide the same normalized capabilities as Gemini:

- server-side authentication;
- admitted model selection;
- structured output;
- timeout;
- cancellation;
- rate-limit mapping;
- unavailable mapping;
- provider request ID;
- token usage;
- malformed response normalization;
- safe diagnostics.

## 14. Provider resolution

Implement trusted Workspace provider resolution.

Conceptual service:

`WorkspaceAiProviderResolver`

Resolution order:

```text
Workspace ACTIVE policy
        ↓ if absent
Deployment default policy
        ↓ if unavailable/unusable
AI_PROVIDER_UNAVAILABLE
```

Resolve one immutable execution policy per logical request.

Conceptual result:

```text
ResolvedAiExecutionPolicy
├── PrimaryProvider
├── PrimaryModel
├── PrimaryCredential
├── RetryPolicy
├── FallbackEnabled
├── FallbackProvider?
├── FallbackModel?
└── FallbackCredential?
```

Do not re-resolve policy midway through an in-flight execution.

Configuration changes affect subsequent executions only.

AI advisory request bodies must not be able to override:

- provider;
- model;
- credential;
- provider URL;
- API key;
- fallback provider.

## 15. Primary/fallback policy

V1 supports:

- exactly one primary provider;
- zero or one fallback provider.

Fallback provider must differ from primary provider.

Example:

```text
Primary: Gemini
Fallback: OpenAI
```

Cross-provider failover is OFF by default.

Workspace Owner or authorized manager must explicitly enable it.

Enabling cross-provider failover means the Workspace explicitly permits AI-safe CRM context to be sent to the configured fallback provider.

This configuration change must be auditable.

## 16. Failover eligibility

Default failover-eligible transient failures:

- `NETWORK_FAILURE`
- `UNAVAILABLE`
- `TIMEOUT`

`RATE_LIMITED` is Workspace-policy controlled.

Never fail over for:

- `AUTHENTICATION_FAILURE`
- invalid client/business request
- CRM permission denial
- Workspace mismatch
- context not found
- unsupported context
- `SAFETY_REFUSAL`
- `CANCELLED`

Fallback must never bypass authorization or provider safety refusal.

## 17. Retry policy

V1 limits:

- same-provider retry: maximum 1;
- cross-provider failover: maximum 1.

Conceptual flow:

```text
Primary attempt
      ↓ transient eligible failure
Primary retry (max one)
      ↓ transient eligible failure
Fallback attempt (max one)
      ↓
success or terminal normalized failure
```

No unbounded retry.

No provider loop.

No retry/fallback after user cancellation.

## 18. Same-snapshot execution invariant

A logical AI execution resolves CRM context once:

```text
authorize
   ↓
resolve owner context
   ↓
apply field security
   ↓
build sanitized CRM snapshot
   ↓
build server grounding evidence
   ↓
provider attempts
```

All attempts for that execution use the same sanitized CRM snapshot and grounding basis.

Do not reread mutable CRM state between primary and fallback.

## 19. Circuit breaker

Implement provider health protection with:

- `CLOSED`
- `OPEN`
- `HALF_OPEN`

Behavior:

```text
CLOSED
  ↓ repeated eligible transient failures
OPEN
  ↓ cooldown
HALF_OPEN
  ↓ successful probe
CLOSED
```

While primary circuit is OPEN:

- skip primary;
- use fallback only if configured and allowed;
- otherwise return normalized provider unavailable response.

Circuit state is runtime operational state, not Workspace business configuration.

Circuit scoping must avoid unrelated Workspace/credential/model failures poisoning other scopes.

## 20. Execution and provider-attempt ledger

The existing logical AI execution ledger remains authoritative.

Extend it so one logical execution can contain multiple provider attempts.

Conceptual structure:

```text
AiExecution
├── ExecutionId
├── WorkspaceId
├── PrincipalId
├── StartedAt
├── CompletedAt
├── FinalStatus
├── PrimaryProvider
├── CompletedProvider
└── SafeEvidenceRefs

AiProviderAttempt
├── AttemptId
├── ExecutionId
├── AttemptNumber
├── Provider
├── Model
├── AttemptKind
│   ├── PRIMARY
│   ├── RETRY
│   └── FALLBACK
├── StartedAt
├── CompletedAt
├── Status
├── FailureCategory?
├── Duration
├── ProviderRequestId?
├── InputTokens?
└── OutputTokens?
```

Fallback remains part of the same logical execution.

Provider-attempt evidence should be append-only.

Do not persist raw prompts, raw CRM context, hidden values, raw credentials or raw provider responses.

## 21. Usage guardrails

V1 minimum operational guardrails:

- per-Workspace concurrent AI request limit;
- per-Workspace requests-per-minute limit;
- existing bounded conversation limits;
- context/prompt size bounds;
- output token limit where supported.

Optional:

- monthly soft usage threshold.

This milestone does not build billing.

## 22. AI Settings management API

Create backend management operations consistent with UniCoreCRM mutation conventions.

Required operations/capabilities:

### Read provider catalog

Return admitted providers/models and safe metadata.

### Read AI configuration

Return safe current Workspace configuration.

Never return secrets.

### Save/update draft

Must support optimistic concurrency and repository idempotency conventions.

### Set/rotate credential

Secret exists only in the write boundary and must not be echoed.

### Test configuration

Use safe synthetic/non-CRM input.

Do not send a CRM record during connection testing.

Return safe:

- status;
- provider;
- model;
- latency;
- provider request ID where appropriate.

### Activate configuration

Only validated configuration may become active.

### Disable configuration

Explicit operation.

### Read safe health/usage summary

Expose only Workspace-appropriate operational information.

## 23. Idempotency and concurrency

Configuration mutations follow existing UniCoreCRM conventions.

Required:

- stable idempotency key;
- same key + same request = replay;
- same key + different request = deterministic reuse conflict;
- stale expected version = version conflict;
- concurrent activation cannot create contradictory active configuration;
- credential rotation is concurrency-safe;
- successful Test Connection must not activate implicitly.

## 24. Audit

Audit at least:

- configuration creation;
- provider change;
- model change;
- credential set/rotation;
- enable/disable failover;
- fallback provider/model change;
- test connection safe result;
- activate;
- deactivate.

Audit contains:

- Workspace;
- actor;
- operation;
- safe before/after configuration;
- timestamp;
- correlation/request identifiers.

Audit excludes:

- secret;
- raw credential;
- raw CRM context;
- raw provider response.

## 25. Frontend Settings scope

Location:

```text
Settings / Studio
└── AI Configuration
```

Required connected UI:

### Status

- active/inactive;
- primary provider;
- primary model;
- safe connection/health state.

### Primary provider

- provider select;
- model select;
- credential source/status;
- credential set/rotate.

### Fallback

- enabled/disabled;
- fallback provider;
- fallback model;
- credential source/status;
- eligible transient failover reasons;
- explicit cross-provider consent.

### Validation/activation

- Save Draft;
- Test;
- Activate.

Failed test leaves existing active configuration untouched.

Frontend must use canonical/generated API boundaries.

No browser-owned AI configuration authority.

No secret in localStorage/sessionStorage.

## 26. Frontend permission UX

User without `ai.configuration.read`:

- no administrative AI configuration surface.

User with read but not manage:

- safe read-only configuration view.

User with manage:

- may edit/test/activate/rotate.

Backend authorization remains authoritative.

## 27. AI Chat frontend impact

Keep AI Chat changes minimal.

AI Chat sends:

- question;
- conversation;
- context references.

AI Chat must not send:

- provider;
- model;
- credential;
- fallback policy.

Safe response metadata may display:

- provider;
- model where appropriate;
- `fallback` indicator.

Example:

```text
OpenAI · fallback
```

This metadata is informational.

There is no provider selector in Chat.

## 28. Provider verification strategy

Mandatory CI/test correctness must not depend solely on live external paid services.

Use deterministic provider HTTP transport/server tests for:

- auth request composition;
- success parsing;
- token metadata;
- provider request ID;
- timeout;
- rate-limit;
- unavailable;
- malformed output;
- cancellation.

Optional controlled live smoke may validate real Gemini/OpenAI connectivity with server-managed credentials.

Live smoke is supplementary, not the only correctness proof.

## 29. Backend work packages

### `CRM-AI-PRV-010 — Configuration Domain`

Build:

- Workspace AI configuration;
- lifecycle;
- validation;
- versioning/concurrency;
- AccessControl capabilities.

Exit:

- Owner can manage;
- delegated capability can manage;
- ordinary member cannot;
- cross-Workspace inaccessible;
- stale update conflict proven.

### `CRM-AI-PRV-020 — Credential Protection`

Build:

- credential abstraction;
- Workspace/deployment resolution;
- protected persistence/reference;
- rotation semantics.

Exit:

- no plaintext secret persistence;
- no secret in read API/log/audit;
- failed replacement preserves active credential.

### `CRM-AI-PRV-030 — Provider/Model Catalog`

Build:

- Gemini catalog;
- OpenAI catalog;
- backend catalog API.

Exit:

- frontend only consumes admitted entries;
- unsupported provider/model fails closed.

### `CRM-AI-PRV-040 — Gemini Adapter`

Build Gemini production adapter and deterministic transport tests.

Exit:

- success;
- timeout;
- rate limit;
- unavailable;
- auth failure;
- malformed response;
- cancellation;
- safe usage/request metadata.

### `CRM-AI-PRV-050 — OpenAI Adapter`

Build OpenAI production adapter with the same normalized contract.

Exit:

same behavior matrix as Gemini.

### `CRM-AI-PRV-060 — Provider Resolver`

Build:

- Workspace active config resolution;
- deployment default resolution;
- immutable request execution policy.

Exit:

- Workspace override works;
- deployment default works;
- missing usable config fails closed;
- advisory request cannot override provider/model.

### `CRM-AI-PRV-070 — Retry and Failover`

Build:

- transient failure classifier;
- same-provider retry;
- fallback policy;
- same-snapshot guarantee.

Exit:

- eligible Gemini failure → OpenAI succeeds;
- ineligible failures never fail over;
- both failures return deterministic error.

### `CRM-AI-PRV-080 — Circuit Breaker`

Build:

- CLOSED;
- OPEN;
- HALF_OPEN;
- cooldown/probe/recovery.

Exit:

- unhealthy provider is skipped while circuit open;
- successful probe recovers primary traffic.

### `CRM-AI-PRV-090 — Provider Attempt Ledger`

Extend durable execution evidence.

Exit:

- one logical execution;
- ordered PRIMARY/RETRY/FALLBACK attempts;
- safe operational metadata;
- Workspace isolation;
- no raw prompt/context/secret persistence.

### `CRM-AI-PRV-100 — Management API`

Build:

- catalog;
- get config;
- update draft;
- credentials;
- test connection;
- activate;
- deactivate;
- safe health/usage.

Exit:

authorization/idempotency/concurrency/security pass.

## 30. Frontend work packages

### `CRM-AI-UI-010 — Canonical OpenAPI`

Update canonical OpenAPI and regenerate clients.

No competing handwritten wire contract.

### `CRM-AI-UI-020 — Application Layer`

Implement connected use cases for:

- load catalog;
- load configuration;
- edit draft;
- credential rotation;
- test;
- activate;
- deactivate.

### `CRM-AI-UI-030 — Settings UI`

Build Workspace AI Configuration page/section.

### `CRM-AI-UI-040 — Permission UX`

Support:

- hidden;
- read-only;
- manageable states.

### `CRM-AI-UI-050 — Failover Settings`

Support:

- enable/disable;
- fallback provider/model;
- credential status;
- eligible reasons;
- explicit cross-provider consent.

### `CRM-AI-UI-060 — Chat Runtime Metadata`

Display safe provider/fallback metadata where useful.

No Chat provider selector.

## 31. Required backend verification matrix

### Configuration

- Workspace Owner can read/manage.
- Explicitly authorized member can read/manage.
- Unauthorized member receives 403.
- Cross-Workspace config is inaccessible.
- stale version rejected.
- idempotency replay works.
- same key/different request rejected.

### Credentials

- raw secret absent from normal DB configuration.
- raw secret absent from GET.
- raw secret absent from logs/audit.
- rotation succeeds.
- failed replacement preserves active config.

### Catalog

- Gemini admitted.
- OpenAI admitted.
- unsupported provider rejected.
- unsupported model rejected.

### Gemini/OpenAI adapter matrix

Each:

- success;
- timeout;
- rate limited;
- unavailable;
- auth failure;
- malformed response;
- cancellation.

### Resolver

- Workspace active override.
- deployment default.
- unavailable when neither usable.
- body/browser provider override rejected or impossible by strict contract.

### Failover

- Gemini unavailable → OpenAI succeeds.
- timeout → fallback.
- network failure → fallback.
- 429 + policy ON → fallback.
- 429 + policy OFF → no fallback.
- invalid credential → no fallback.
- safety refusal → no fallback.
- cancellation → no fallback.
- both providers fail → deterministic public failure.

### Same snapshot

Primary and fallback attempts receive identical sanitized CRM context/evidence.

### Circuit breaker

- failure threshold opens.
- OPEN skips primary.
- cooldown → HALF_OPEN.
- successful probe → CLOSED.
- failed probe → OPEN.

### Ledger

- primary success = one attempt.
- retry = ordered attempts.
- fallback = ordered primary/retry/fallback attempts.
- safe usage/provider request IDs captured where supplied.
- no raw prompt/context/secret persisted.

## 32. Required frontend verification

- catalog loaded from backend;
- provider options not invented locally;
- model list changes with selected provider;
- Owner can edit;
- read-only user cannot edit;
- unauthorized user cannot administer;
- credential disappears after write;
- secret never re-rendered from GET;
- failed test does not activate;
- successful test + activate refetches authoritative state;
- Gemini primary configuration works;
- OpenAI primary configuration works;
- fallback configuration works;
- stale version conflict handled safely;
- mutation idempotency preserved through transport ambiguity;
- Chat never sends provider/model/secret;
- fallback response displays safe metadata only;
- no local browser authority for secrets/config.

## 33. Connected browser E2E

### E2E-A — Gemini primary

```text
Owner
→ Settings
→ select Gemini
→ configure credential
→ test
→ activate
→ open CRM record
→ AI Chat
→ grounded Gemini answer
```

Verify durable execution/provider metadata and no CRM mutation.

### E2E-B — OpenAI primary

Equivalent provider path using deterministic/controlled provider transport.

### E2E-C — Automatic failover

```text
Primary Gemini
Fallback OpenAI
Failover enabled

Gemini transient failure
→ OpenAI fallback
→ same CRM evidence
→ successful answer
```

Assert:

- one execution;
- expected ordered attempts;
- same evidence;
- fallback metadata;
- no CRM mutation.

### E2E-D — Fallback disabled

Gemini failure must not invoke OpenAI.

### E2E-E — Configuration permission

Ordinary user cannot modify Workspace AI configuration.

## 34. Migration plan

Expected persisted additions may include:

- Workspace AI configuration;
- provider attempt evidence;
- safe credential metadata/reference.

Rules:

- forward-only migrations;
- do not edit published migrations;
- clean migration PASS;
- upgrade migration PASS;
- pending-model checks PASS.

Upgrade proof must include the accepted baseline/current CRM-AI local migration sequence according to actual DbContext ownership.

## 35. Security closure checklist

Before acceptance prove:

- no secret in browser;
- no secret in OpenAPI responses;
- no plaintext secret in normal DB config;
- no secret in logs/audits;
- no provider override in advisory request;
- no cross-Workspace AI configuration access;
- no cross-Workspace credential access;
- no authorization bypass during failover;
- no CRM context reread between attempts;
- no safety-refusal failover;
- no fallback without explicit policy;
- no infinite retry;
- no unsafe provider diagnostics exposed.

## 36. Observability

Minimum operational metrics:

- AI request count;
- AI success rate;
- per-provider success rate;
- fallback count;
- failure-category counts;
- rate-limit count;
- timeout count;
- latency;
- input tokens;
- output tokens;
- circuit-open count.

Do not record raw CRM data for observability.

## 37. Explicitly out of scope

Do not implement in this milestone:

- autonomous agents;
- CRM mutation tools;
- AI Task creation;
- proactive AI;
- scheduled AI;
- persistent conversation database;
- vector database;
- generic RAG platform;
- generic SQL/database tool;
- provider selection per Chat question;
- multi-provider racing/fan-out;
- provider fallback to bypass safety refusal;
- billing engine;
- model fine-tuning.

## 38. Implementation order

Execute in this dependency order:

```text
Phase 0
Preserve current CRM-AI-001 local implementation

Phase 1
AccessControl + Workspace AI configuration domain

Phase 2
Credential storage / resolution

Phase 3
Provider + model catalog

Phase 4
Gemini provider adapter

Phase 5
OpenAI provider adapter

Phase 6
Workspace provider policy resolver

Phase 7
Retry + controlled failover

Phase 8
Circuit breaker

Phase 9
Provider-attempt ledger

Phase 10
Management API + canonical OpenAPI

Phase 11
Frontend AI Configuration Settings

Phase 12
Backend/frontend executable provider tests

Phase 13
Connected browser E2E

Phase 14
Regression + migrations + security closure

Phase 15
Commit + push only after all mandatory gates

Phase 16
Independent Controller source review
```

Do not start failover until individual provider adapters pass independently.

Do not create frontend provider authority before backend catalog/configuration contracts stabilize.

## 39. Mandatory regression gates

Backend:

- solution build with 0 warnings/errors;
- real AI verifier;
- production provider contract tests;
- resolver/failover/circuit-breaker tests;
- AccessControl regression;
- Workspace regression where affected;
- PlatformOperations regression;
- affected CRM owner regressions if owner code changes;
- Event/Webhook regression if PlatformOperations persistence/composition changes;
- clean migration;
- baseline upgrade;
- pending model checks.

Frontend:

- canonical OpenAPI/generated client freshness;
- AI contracts;
- Settings executable tests;
- connected AI tests;
- connected browser E2E;
- typecheck;
- lint;
- production build;
- bundle budget.

Existing proven baseline differential failures remain non-blocking only if unchanged and reproduced with the same command/environment.

## 40. Definition of Done

The module is implementation-complete only when:

- CRM-aware Chat remains permission-grounded;
- Gemini production adapter exists;
- OpenAI production adapter exists;
- Gemini is deployment default;
- Workspace may select Gemini or OpenAI;
- only Owner/authorized principals can configure;
- Chat cannot choose provider/model;
- credentials remain protected/server-side;
- backend provider/model catalog is authoritative;
- test-before-activate works;
- failed replacement preserves active config;
- optional single fallback exists;
- fallback defaults OFF;
- transient-only failover works;
- safety/auth failures never fail over;
- circuit breaker works;
- all attempts use one CRM snapshot;
- provider attempts are auditable;
- no CRM mutation is introduced;
- Settings works in connected mode;
- migrations pass clean + upgrade;
- backend regressions pass;
- frontend gates pass;
- browser E2E passes.

## 41. Commit strategy

Do not create WIP commits on canonical branches.

After every mandatory gate passes:

Backend suggested commit:

`feat(ai): add workspace production provider platform`

Frontend suggested commit:

`feat(ai): add workspace ai provider settings`

No force push/history rewrite.

## 42. Controller review

After push, independent Controller review must inspect actual source rather than accept the implementation report.

Controller review scope:

- ancestry;
- Workspace configuration ownership;
- AccessControl semantics;
- secret handling;
- Gemini adapter;
- OpenAI adapter;
- provider failure normalization;
- resolver semantics;
- retry/failover;
- circuit-breaker races;
- execution/attempt ledger;
- migration upgrade;
- canonical OpenAPI authority;
- frontend Settings authority;
- connected browser E2E;
- regression evidence.

Allowed final controller states:

- `REVIEW_PASS`
- `NEEDS_FIX`

## 43. Roadmap boundary

After `CRM-AI-002` reaches `REVIEW_PASS`, return to the agreed roadmap.

Next:

`Customer Health / Churn`

Then:

`Proactive AI / Reminder / Suggested Action`

Do not begin those modules as part of this plan.
