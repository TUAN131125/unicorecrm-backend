# UNICORECRM_PROACTIVE_AI_BUILD_PLAN_v1.0

**Plan ID:** `CRM-PA`  
**Feature:** Proactive AI / Attention / Suggested Action  
**Status:** READY FOR IMPLEMENTATION  
**Roadmap dependency:** AI Platform = REVIEW_PASS, Customer Health = REVIEW_PASS  
**Canonical backend baseline:** `6b18a2139dc10a2ffed469ccd3cf82deb6e6cac1` (`master`)  
**Canonical frontend baseline:** `50e41a336485c104e209412eae7b156a0d522571` (`main`)

---

## 1. PURPOSE

Proactive AI moves UniCoreCRM from:

```text
User asks AI
→ AI reads authorized CRM context
→ AI answers
```

to:

```text
CRM state/time changes
→ deterministic detector identifies attention-worthy condition
→ durable in-app Proactive Item
→ authorized user sees it without asking AI
→ user may request AI explanation / suggested next step
→ AI may prepare a Task draft
→ user explicitly confirms before any CRM mutation
```

V1 is **not an autonomous agent**.

Product goal:

> Help a user know **who needs attention, why, and what they may do next**, while preserving CRM owner authority, permissions, deduplication, human confirmation, and auditability.

---

## 2. ROADMAP AUTHORITY

This plan implements the existing roadmap task groups:

- `CRM-PA-001` Proactive trigger authority
- `CRM-PA-010` Rule/event trigger engine
- `CRM-PA-020` Dedup / suppression / scheduling
- `CRM-PA-030` Suggestion generation + explanation
- `CRM-PA-040` Confirmation-gated mutation tools
- `CRM-PA-050` Notification UX / preferences
- `CRM-PA-060` Evaluation: precision / noise / actionability
- `CRM-PA-070` Security / audit / reliability release gate

Roadmap exit condition remains authoritative:

> Proactive system is useful, does not spam, does not mutate beyond authority, and important actions have an audit trail.

---

## 3. CURRENT REPOSITORY FACTS

### Existing AI architecture

`UnicoreCRM.AI` already references:

- `UnicoreCRM.Crm`
- `UnicoreCRM.Operations`
- `UnicoreCRM.Platform`
- `UnicoreCRM.PlatformOperations`

Existing AI infrastructure already provides:

- provider-neutral AI orchestration;
- Gemini/OpenAI Workspace configuration;
- provider failover/runtime handling;
- CRM-safe context composition;
- permission-scoped AI grounding;
- AI execution/provider-attempt ledger.

### Existing AI operational persistence

`UnicoreCRM.PlatformOperations/AiExecution` already owns the `platform_ai` schema through `AiExecutionDbContext`.

Proactive AI operational state SHALL extend this existing persistence boundary.

Do not introduce `ProactiveAiDbContext`.

### Existing Customer Health

Customer Health is a feature of Customers and is calculated on read.

Authoritative v1 signal:

```text
PURCHASE_RECENCY
```

Authoritative bands:

```text
UNKNOWN
HEALTHY
WATCH
AT_RISK
CRITICAL
```

Proactive AI consumes a narrow Customers-owned projection. It must not query CommercialEvidence or Customers persistence directly.

### Existing Tasks authority

Tasks already owns:

- `tasks.create`;
- assignment validation;
- field security;
- idempotency;
- audit/outbox;
- Workspace isolation.

Tasks already contains a narrow workflow-participant pattern (`ILeadQualificationTaskParticipant`).

Proactive AI must use a similarly narrow Tasks-owned participant for confirmed Task creation. It must never write `TasksDbContext`.

---

# 4. LOCKED V1 SCOPE

V1 supports exactly one proactive trigger:

```text
CUSTOMER_HEALTH_RISK
```

Trigger mapping:

| Customer Health | Proactive behavior |
|---|---|
| `UNKNOWN` | no item |
| `HEALTHY` | no item; resolve existing risk item |
| `WATCH` | no item; resolve existing risk item |
| `AT_RISK` | create/update item with severity `HIGH` |
| `CRITICAL` | create/update item with severity `CRITICAL` |
| archived / no active assessment | resolve existing risk item |

V1 delivery:

```text
IN_APP_ONLY
```

V1 AI action:

```text
EXPLAIN + SUGGEST NEXT STEP + PREPARE TASK DRAFT
```

V1 confirmed mutation:

```text
CREATE FOLLOW-UP TASK
```

No other automatic or confirmed mutation is included.

---

# 5. EXPLICIT OUT OF SCOPE

Do NOT build in this milestone:

- autonomous Task creation;
- automatic email/SMS/push;
- Slack/Teams delivery;
- proactive webhooks;
- Customer mutation;
- owner mutation;
- automatic Deal/Support creation;
- autonomous multi-step agent loops;
- arbitrary AI tool execution;
- generic `IAiTool.Execute(any command)` framework;
- user-defined rule DSL;
- AI-selected trigger conditions;
- LLM scanning CRM records;
- stale Lead trigger;
- overdue Task trigger;
- Support/Payment/Deal risk triggers;
- AI ranking all Customers;
- ML prediction;
- Customer Health reimplementation;
- direct CommercialEvidence access from AI;
- direct Customer/Task DbContext access from AI.

---

# 6. OWNERSHIP

## 6.1 Feature ownership

Proactive AI is a feature of the existing:

```text
UnicoreCRM.AI
```

Do not create:

```text
UnicoreCRM.ProactiveAI
```

Suggested internal organization:

```text
UnicoreCRM.AI
└── Proactive
    ├── Application
    ├── Contracts
    ├── Domain
    └── Infrastructure
```

This is code organization, not a new bounded context.

## 6.2 Data ownership

- Customer + Customer Health truth: `Customers`
- Purchase evidence truth: `CommercialEvidence`
- Task truth: `Tasks`
- Workspace/timezone truth: `Workspace`
- Proactive operational state: existing `platform_ai` persistence
- Provider execution truth: existing AI execution/provider ledger

No owner may bypass another owner’s persistence.

---

# 7. CORE ARCHITECTURE

```text
CommercialEvidence
       ↓
Customers
       ↓
Customer Health
       ↓
Customers-owned proactive health projection
       ↓
Deterministic Proactive Trigger Engine
       ↓
platform_ai.ProactiveItems
       ↓
Authorized Attention Inbox
       │
       ├── deterministic WHY
       │
       └── User requests AI suggestion
                ↓
        Existing AI provider platform
                ↓
         Suggested next step
                ↓
            Task draft
                ↓
        USER EXPLICITLY CONFIRMS
                ↓
        Tasks-owned participant
                ↓
             Task created
```

The proactive detector MUST work when Gemini/OpenAI is unavailable.

---

# 8. PROACTIVE ITEM MODEL

Create a durable AI-owned operational entity conceptually equivalent to:

```text
ProactiveItem
- ItemId
- WorkspaceId
- OwnerMemberId
- TriggerType
- SubjectType
- SubjectId
- Severity
- ReasonCode
- TriggerFingerprint
- RiskCycleKey
- Status
- FirstDetectedAt
- LastDetectedAt
- SeenAt?
- SnoozedUntil?
- DismissedAt?
- ResolvedAt?
- SourceVersion?
- Version
- CreatedAt
- UpdatedAt
```

Exact vocabularies:

```text
TriggerType:
CUSTOMER_HEALTH_RISK

SubjectType:
CUSTOMER

Severity:
HIGH
CRITICAL

Status:
OPEN
SNOOZED
DISMISSED
RESOLVED
```

`SeenAt` is metadata, not a lifecycle status.

Do not persist raw Customer snapshots, order history, provider prompts/responses, or credentials.

---

# 9. RISK CYCLE + DEDUP

A stable Customer risk condition produces one durable item.

```text
Day 1  AT_RISK → create item A
Day 2  AT_RISK → refresh item A
Day 3  AT_RISK → refresh item A
```

Do not create A/B/C.

Risk cycle ends when Customer:

- becomes `WATCH`;
- becomes `HEALTHY`;
- has no active assessment;
- becomes archived.

Then item becomes `RESOLVED`.

If Customer later becomes `AT_RISK` again after recovery, create a new risk cycle.

`TriggerFingerprint` must be deterministic and must not depend on LLM text.

---

# 10. ESCALATION

```text
AT_RISK → CRITICAL
```

must:

- keep the same risk-cycle item;
- change severity to `CRITICAL`;
- refresh deterministic reason/evidence;
- re-surface as `OPEN`;
- clear weaker-state suppression when necessary;
- audit escalation.

A CRITICAL escalation may override previous snooze/dismiss because risk materially worsened.

---

# 11. OWNER SEMANTICS

V1 owner:

```text
ProactiveItem.OwnerMemberId = authoritative Customer.OwnerId
```

AI must never invent the owner.

If Customer has no owner:

```text
SUPPRESS USER-FACING ITEM
```

Record safe telemetry only, e.g. `UNOWNED_TRIGGER_SUPPRESSED`.

When owner changes while risk remains active:

- transfer to new authoritative owner;
- previous owner immediately loses visibility;
- new owner receives item as `OPEN`;
- previous owner’s Seen/Snooze/Dismiss cannot silently suppress the new owner.

---

# 12. BACKGROUND EVALUATION AUTHORITY

Background evaluation must NOT impersonate a Workspace Owner, Customer owner, or arbitrary member.

Introduce the minimum owner-safe system evaluation authority required.

AI scheduler may call only a narrow Customers-owned contract, conceptually:

```text
IProactiveCustomerHealthReader
```

It should expose only trigger facts such as:

```text
CustomerId
OwnerMemberId?
CustomerStatus
HealthBand?
ChurnRisk?
HealthReasonCode?
HealthAlgorithmVersion?
SourceVersion?
```

It must not expose raw CommercialEvidence.

Customers remains responsible for Health calculation.

Exact trusted-system context mechanism is implementation freedom, but fabricating a user/member identity is forbidden.

---

# 13. SCHEDULING

V1 correctness is time-driven.

Requirement:

```text
Each enabled Workspace is evaluated once per local day.
```

Worker may wake more frequently to find due Workspaces.

Scheduling must be:

- Workspace-isolated;
- timezone-aware;
- idempotent;
- multi-instance safe;
- lease-safe or equivalent;
- bounded-batch;
- resumable;
- retry-safe.

Store instants in UTC.

Workspace local-day calculation must use authoritative Workspace timezone.

Do not duplicate timezone inside Proactive configuration.

If no background timezone reader exists, add the smallest Workspace-owned reader contract required. AI must not query Workspace DbContext directly.

---

# 14. EVENT JOURNAL ROLE

`IIntegrationEventFeed` already exists, but Customer Health risk can worsen only because time passes.

Therefore:

```text
daily evaluation = correctness path
```

Integration events may later accelerate reevaluation, but event consumption is not required for v1 correctness.

Do not build a generic event rules engine in this milestone.

---

# 15. WORKSPACE POLICY

Minimal durable configuration:

```text
WorkspaceProactivePolicy
- WorkspaceId
- Enabled
- Version
- LastEvaluationAt?
- NextEvaluationAt?
- UpdatedBy
- UpdatedAt
```

Lease/claim fields may be added if required by implementation.

Default:

```text
Enabled = false
```

Proactive detection does not require active Gemini/OpenAI configuration.

Provider unavailable means Attention still works; only AI suggestion is unavailable/retryable.

---

# 16. CAPABILITIES

Add:

```text
ai.proactive.use
ai.proactive.manage
```

`ai.proactive.use` permits own inbox, detail, seen, snooze, dismiss, AI suggestion, and confirmed Task action subject to Task authority.

`ai.proactive.manage` permits Workspace enable/disable.

Do not create one capability per button.

---

# 17. CURRENT AUTHORIZATION MUST BE RE-EVALUATED

A stored item is not permanent read authority.

For Customer Health risk item visibility/action:

```text
ai.proactive.use
AND current Workspace membership
AND customers.view
AND current Customer record access
AND Customer health field readable
AND item OwnerMemberId == current member
```

If any fails, item must not leak.

Do not rely only on permissions from item creation time.

Prefer bounded Customers-owned batch visibility/projection for inbox pages; do not create N+1 Customer authorization/enrichment.

---

# 18. HEALTH FIELD SECURITY

If `customers.health` is hidden for current user, Proactive API/provider context must expose ZERO derived Health data:

- no score;
- no health band;
- no churn risk;
- no confidence;
- no purchase count;
- no last-purchase derived values;
- no cadence;
- no reason code;
- no algorithm version.

The item should be omitted from that user’s inbox.

This is a locked CRM-CH-001 regression requirement.

---

# 19. SNOOZE

Snooze stores `SnoozedUntil`.

Suggested UX:

- Tomorrow
- 3 days
- Next week
- Custom date/time

On expiry:

- re-evaluate current Customer Health;
- still risk → reopen/update;
- recovered/archived → resolve.

Do not blindly reopen stale state.

Snooze mutation must be idempotent/replay-safe.

---

# 20. DISMISS

Dismiss means: do not repeat the same stable condition.

```text
AT_RISK → dismissed → still AT_RISK
=> remain suppressed

AT_RISK → dismissed → CRITICAL
=> reopen/escalate

AT_RISK → dismissed → HEALTHY/WATCH
=> resolve

resolved → later AT_RISK
=> new cycle allowed
```

Dismiss must be audited.

---

# 21. DELIVERY V1

In-app only.

Preferred UI:

```text
AI
├── Chat
└── Attention
```

or equivalent existing shell convention.

Inbox shows:

- severity;
- safe Customer label;
- deterministic why;
- detected time;
- Open Customer;
- AI Suggestion;
- Snooze;
- Dismiss.

No email/SMS/push/webhook delivery.

---

# 22. SAFE CUSTOMER PRESENTATION

Do not persist Customer display name just for alert UX.

At read time, resolve safe current label/identifier through a Customers-owned authorized projection.

Stored item identity remains:

```text
SubjectType + SubjectId
```

This avoids stale/hidden data becoming a side channel.

---

# 23. DETERMINISTIC EXPLANATION

Inbox must remain useful without provider.

For `CUSTOMER_HEALTH_RISK`, deterministic explanation comes from Customer Health reason semantics.

Example:

```text
Customer Health is AT_RISK.
Purchase cadence is overdue relative to expected cadence.
```

Do not call Gemini/OpenAI just to render Attention list.

---

# 24. AI SUGGESTION ON DEMAND

Provider call occurs only after explicit authenticated user request.

```text
User request
→ re-authorize item
→ re-authorize Customer
→ load current Customers-owned AI-safe context
→ existing provider orchestration
→ validate structured output
→ return suggestion
```

No background LLM calls.

Use current Workspace AI provider configuration and existing provider guardrails/failover/usage ledger.

---

# 25. SUGGESTION OUTPUT

Constrained structure:

```text
ProactiveSuggestion
- Summary
- Why
- SuggestedNextStep
- TaskDraft
    - Title
    - Description
```

AI must not authoritatively choose:

- Customer Health;
- owner;
- actual assignee;
- due date;
- priority;
- whether a Task must be created.

Frontend may initialize ordinary values from deterministic current authority, but user may edit before confirm.

---

# 26. PROVIDER FAILURE

If provider is unavailable/rate-limited/invalid:

- item remains available;
- deterministic why remains available;
- Open Customer works;
- Snooze/Dismiss work;
- no CRM mutation occurs;
- suggestion endpoint returns existing safe AI failure semantics.

Never resolve/dismiss because AI failed.

---

# 27. CONFIRMATION-GATED TASK CREATION

Only v1 mutation:

```text
CREATE FOLLOW-UP TASK
```

Flow:

```text
AI prepares draft
→ frontend shows editable draft
→ user explicitly clicks Create Task
→ server re-authorizes
→ Tasks owner validates/creates
→ Proactive audit records acceptance
```

No Task may be created because scheduler ran, item exists, suggestion was generated, or item was opened.

---

# 28. TASK OWNER BOUNDARY

Add a narrow Tasks-owned contract following the existing participant pattern, conceptually:

```text
IProactiveTaskCreationParticipant
```

It must preserve normal Tasks semantics:

- `tasks.create`;
- Workspace authority;
- field security;
- active assignee validation;
- validation;
- idempotency;
- audit;
- outbox/integration event behavior.

AI must never call `TasksDbContext`.

Do not add a generic mutation tool framework.

Suggested provenance:

```text
TaskSourceReference.Type = PROACTIVE_AI
TaskSourceReference.Id = <ProactiveItemId>
```

Use existing repository vocabulary if an equivalent source type already exists.

---

# 29. TASK CONFIRMATION IDEMPOTENCY

Double-click/retry/replay must not duplicate Task.

Same idempotency key + same intent → same Task result.

Same key + changed intent → conflict.

Successful creation audits:

```text
TASK_SUGGESTION_ACCEPTED
```

with safe identifiers only.

---

# 30. API SURFACE

Suggested API:

```text
GET  /ai/proactive/items
GET  /ai/proactive/items/{itemId}

POST /ai/proactive/items/{itemId}/seen
POST /ai/proactive/items/{itemId}/snooze
POST /ai/proactive/items/{itemId}/dismiss
POST /ai/proactive/items/{itemId}/suggestion
POST /ai/proactive/items/{itemId}/confirm-task

GET  /ai/proactive/configuration
PUT  /ai/proactive/configuration
```

Exact route naming may follow current AI conventions; semantics are locked.

List default = `OPEN`, with bounded pagination.

Snoozed filter may be exposed.

Resolved history UI is not required v1.

---

# 31. CONFIGURATION MUTATION

`PUT /ai/proactive/configuration` follows existing AI configuration conventions:

- trusted Workspace authority;
- `ai.proactive.manage`;
- optimistic version;
- Idempotency-Key;
- durable replay;
- same key + different intent conflict;
- safe audit.

V1 configuration exposes only `Enabled` plus version metadata.

No custom Health thresholds or rule editor.

---

# 32. PERSISTENCE

Extend existing `platform_ai` persistence with minimum tables conceptually:

```text
ProactiveItems
WorkspaceProactivePolicies
ProactiveCommands
ProactiveAudits
```

`ProactiveCommands` supports durable idempotency/replay.

`ProactiveAudits` is lifecycle/security evidence, not raw business history.

No new DbContext.

No Customer Health table.

---

# 33. INDEXING

At minimum support indexes for:

```text
Workspace + Owner + Status + Updated/Detected time
Workspace + Subject + Trigger + active risk cycle
Workspace + Enabled + NextEvaluationAt
```

Active-cycle dedup must be enforced durably, not only in memory.

Exact SQL/index form is implementation freedom.

---

# 34. AUDIT

Required audit actions:

```text
PROACTIVE_ENABLED
PROACTIVE_DISABLED
ITEM_CREATED
ITEM_ESCALATED
ITEM_OWNER_CHANGED
ITEM_SEEN
ITEM_SNOOZED
ITEM_DISMISSED
ITEM_RESOLVED
AI_SUGGESTION_REQUESTED
AI_SUGGESTION_SUCCEEDED
AI_SUGGESTION_FAILED
TASK_SUGGESTION_ACCEPTED
```

Do not persist credentials, raw prompts, raw provider responses, hidden CRM fields, or arbitrary CRM snapshots.

Provider executions continue using existing AI execution/provider-attempt ledger.

---

# 35. OPERATIONAL METRICS

Instrument at minimum:

```text
items_created
items_escalated
items_resolved
items_snoozed
items_dismissed
unowned_triggers_suppressed
evaluation_workspaces
evaluation_customers
evaluation_duration
evaluation_failures
ai_suggestions_requested
ai_suggestions_succeeded
ai_suggestions_failed
suggested_tasks_confirmed
```

No analytics dashboard required v1.

---

# 36. CRM-PA-001 — PROACTIVE TRIGGER AUTHORITY

Implement and lock:

1. `CUSTOMER_HEALTH_RISK` is the only v1 trigger.
2. Customers is Health authority.
3. No direct CommercialEvidence/Customers persistence access from AI.
4. No LLM trigger decision.
5. No caller-supplied Health state.
6. Owner comes from current Customer owner.
7. No-owner suppression.
8. System evaluator does not impersonate user.

Acceptance:

```text
UNKNOWN → no item
HEALTHY → no item
WATCH → no item
AT_RISK → HIGH item
CRITICAL → CRITICAL item
ARCHIVED/no active assessment → no active item
```

---

# 37. CRM-PA-010 — RULE/TRIGGER ENGINE

Build a small deterministic trigger abstraction, conceptually:

```text
IProactiveTriggerProvider
└── CustomerHealthRiskTrigger
```

Do not build rules DSL.

Engine consumes bounded Customers-owned evaluation pages and reconciles durable items.

Requirements:

- deterministic replay;
- bounded pages;
- retry-safe;
- Workspace isolation;
- no provider call.

---

# 38. CRM-PA-020 — DEDUP / SUPPRESSION / SCHEDULING

Implement:

- daily Workspace-local evaluation;
- enable/disable;
- active-cycle dedup;
- snooze;
- dismiss;
- escalation resurfacing;
- recovery resolution;
- owner transfer;
- scheduler lease/claim safety.

Acceptance:

```text
same AT_RISK repeated → one item
AT_RISK → CRITICAL → same cycle, resurfaced
dismissed AT_RISK → still AT_RISK → suppressed
dismissed AT_RISK → CRITICAL → reopened
snoozed before due → hidden
snooze due + still risk → open
snooze due + recovered → resolved
recovered → later risk → new cycle
```

---

# 39. CRM-PA-030 — SUGGESTION + EXPLANATION

Implement:

- deterministic explanation without provider;
- on-demand AI suggestion;
- constrained structured output;
- current authorization;
- existing provider orchestration;
- existing AI execution ledger;
- no background generation.

Acceptance:

- provider down does not remove inbox item;
- hidden Health never reaches provider;
- denied Customer never reaches provider;
- authorized context matches Customers authority;
- provider cannot inject unsupported action types;
- suggestion creates no mutation by itself.

---

# 40. CRM-PA-040 — CONFIRMATION-GATED TASK MUTATION

Implement:

- editable Task draft;
- explicit confirmation;
- narrow Tasks-owned participant;
- normal Task authorization;
- idempotent confirmation;
- Proactive → Task provenance;
- acceptance audit.

Acceptance:

- opening item creates zero Task;
- requesting suggestion creates zero Task;
- only confirm creates Task;
- missing `tasks.create` denies mutation;
- invalid/deactivated assignee fails through Tasks;
- double confirm/retry does not duplicate;
- changed payload under same key conflicts;
- Task is Workspace-isolated;
- AI never writes Tasks persistence directly.

---

# 41. CRM-PA-050 — ATTENTION UX

Frontend:

### Attention page

- open count;
- severity;
- safe Customer label;
- deterministic why;
- detected time;
- Open Customer;
- AI Suggestion;
- Snooze;
- Dismiss.

### Suggestion dialog

- summary;
- why;
- suggested next step;
- editable Task draft;
- explicit Create Task button;
- unavailable/disabled action if current Task authority denies it.

### Configuration

```text
Proactive AI
Enabled / Disabled
```

Only `ai.proactive.manage` can change it.

Connected frontend must never manufacture proactive items locally.

No connected demo fallback.

---

# 42. CRM-PA-060 — PRECISION / NOISE / ACTIONABILITY CORPUS

Executable scenarios at minimum:

1. UNKNOWN → no alert;
2. HEALTHY → no alert;
3. WATCH → no alert;
4. AT_RISK → one HIGH item;
5. CRITICAL → one CRITICAL item;
6. repeated evaluation → no duplicate;
7. AT_RISK → CRITICAL escalation;
8. risk → WATCH recovery;
9. archived recovery;
10. recovered → later new risk cycle;
11. unowned Customer suppression;
12. owner change transfer/reopen;
13. snooze before due;
14. snooze expiry still risk;
15. snooze expiry recovered;
16. dismiss stable risk;
17. dismiss then escalation;
18. provider unavailable;
19. hidden Health field;
20. Customer record-scope denied;
21. missing `ai.proactive.use`;
22. missing `tasks.create`;
23. suggestion only → no mutation;
24. confirmed Task;
25. confirm replay;
26. Workspace isolation;
27. local-day/timezone boundary;
28. worker restart/retry;
29. concurrent workers / duplicate prevention;
30. disabled Workspace → no new evaluation.

Do not invent ML precision probabilities without a labeled dataset.

---

# 43. CRM-PA-070 — RELEASE GATE

Release requires proof of:

### Security

- Workspace isolation;
- owner-only v1 inbox;
- current membership reauthorization;
- Customer record access;
- Customer Health field security;
- `ai.proactive.use`;
- `ai.proactive.manage`;
- `tasks.create`;
- no hidden CRM data in provider context;
- no foreign DbContext access.

### Reliability

- scheduler multi-instance safety;
- durable dedup;
- crash/retry safety;
- idempotent mutations;
- provider outage isolation;
- migration upgrade safety;
- bounded evaluation;
- no N+1 inbox enrichment.

### Audit

Every material lifecycle/action transition has safe durable evidence.

---

# 44. IMPLEMENTATION ORDER

## Phase 0 — classify repository

Inspect current AI endpoints/application, `AiExecutionDbContext`, Workspace timezone authority, Customer Health contracts, Tasks participant pattern, AccessControl, frontend AI runtime/navigation, and command/idempotency conventions.

## Phase 1 — vocabulary + persistence

Implement exact vocabularies, lifecycle, policy, command/idempotency, audit, migration/indexes, invariants.

## Phase 2 — Customers proactive Health contract

Implement narrow background evaluation projection. Prove bounded pages, correct Health authority, owner, archived behavior, and no impersonation.

## Phase 3 — trigger reconciler

Implement AT_RISK/CRITICAL, dedup, escalation, recovery, owner change. No LLM.

## Phase 4 — policy + scheduler

Implement default disabled, enable/disable, local-day due calculation, lease/claim, bounded batch, retry/resume.

## Phase 5 — authorized inbox APIs

Implement list/detail/seen/snooze/dismiss with current reauthorization and safe Customer projection.

## Phase 6 — AI suggestion

Implement on-demand structured suggestion using current Customer grounding and existing provider runtime.

## Phase 7 — confirmed Task

Implement Tasks-owned participant, confirm endpoint, editable draft, idempotency, audit/provenance.

## Phase 8 — frontend

Implement Attention page/navigation, actions, suggestion dialog, Task confirmation, setting.

## Phase 9 — evaluation + connected E2E

Run PA-060/070 corpus and browser acceptance.

---

# 45. PERFORMANCE BOUNDS

Suggested defaults:

```text
evaluation Customer page <= 250
inbox page <= 100
```

Requirements:

- no unbounded Customer materialization;
- no N+1 CommercialEvidence;
- no N+1 Customer reauthorization/enrichment in inbox;
- no LLM per Customer during background scan;
- no repeated scan for disabled Workspace.

If existing repo has stricter canonical bounds, use those.

---

# 46. MIGRATIONS

Expected migration: existing `platform_ai` schema.

No Customer Health migration expected.

No Tasks schema change expected unless current source provenance constraints cannot represent the proactive source.

If a physical constraint blocks locked semantics, report `IMPLEMENTATION_CONSTRAINT` before widening semantics.

Mandatory DB checks:

- clean migration;
- accepted-baseline upgrade;
- pending-model;
- indexes/uniqueness;
- concurrent dedup;
- no duplicate active risk cycle.

---

# 47. OPENAPI / GENERATED CLIENT

Any new endpoint/contract requires:

- OpenAPI regeneration;
- generated frontend client refresh;
- freshness verifier pass.

Do not hand-maintain transport DTOs where generated client owns them.

---

# 48. BACKEND REQUIRED GATES

At minimum:

- solution build: 0 warnings / 0 errors;
- existing AI assistant verifier;
- AI provider verifier where affected;
- Customers read/core verifier;
- Customer Health corpus;
- Tasks verifier/regressions;
- AccessControl regressions where affected;
- new Proactive verifier;
- migration clean;
- baseline upgrade;
- pending-model;
- OpenAPI freshness;
- multi-instance/concurrency dedup proof.

---

# 49. FRONTEND REQUIRED GATES

At minimum:

- generated client freshness;
- AI contracts;
- Customer contracts;
- Customer Intelligence contracts;
- Task integration contracts where affected;
- typecheck;
- lint;
- production build;
- bundle budget;
- connected Proactive browser E2E.

Connected E2E path:

```text
browser
→ connected frontend
→ real ApiHost
→ real auth
→ Workspace
→ AccessControl
→ Customers / Customer Health
→ Proactive persistence
→ deterministic provider transport for suggestion
→ Tasks owner on explicit confirmation
```

Paid Gemini/OpenAI calls are not required for CI acceptance.

---

# 50. CONNECTED BROWSER ACCEPTANCE

### A — AT_RISK attention

Enabled Workspace + owned AT_RISK Customer → one HIGH item, deterministic why, Open Customer works.

### B — no spam

Run evaluation again → still one item.

### C — snooze

Snooze → leaves open inbox; before due stays hidden; due + still risk returns.

### D — recovery

Authoritative Health becomes non-risk → item resolves and disappears from open inbox.

### E — AI suggestion

Authorized user requests suggestion → deterministic provider returns validated suggestion → no Task yet.

### F — confirm Task

User edits/accepts draft and confirms → exactly one Task; retry does not duplicate.

### G — permission denial

Record/Health field denied → item does not leak; provider receives no Health context.

### H — provider unavailable

Item remains usable; suggestion fails safely; no mutation.

---

# 51. IMPLEMENTATION FREEDOM

Agent may choose internal class names, folder arrangement, scheduler wake interval, lease mechanism, query implementation, SQL/EF mechanics, UI decomposition, safe audit schema, and paging cursor representation, provided all locked semantics remain true.

---

# 52. IMPLEMENTATION CONSTRAINT RULE

If current repository/runtime makes a locked requirement impossible literally, stop only that decision and report:

```text
IMPLEMENTATION_CONSTRAINT

- requirement:
- repository/runtime evidence:
- why it cannot be implemented literally:
- smallest architecture-safe alternatives:
```

Do not silently impersonate users, bypass owner contracts, broaden AI authority, weaken permissions, or build generic autonomous tooling.

---

# 53. LIKELY BACKEND AREAS

```text
src/UnicoreCRM.AI/Proactive/
src/UnicoreCRM.AI/Gateway/
src/UnicoreCRM.PlatformOperations/AiExecution/
src/UnicoreCRM.Crm/Customers/
src/UnicoreCRM.Operations/Tasks/
src/UnicoreCRM.Platform/Workspace/
src/UnicoreCRM.Platform/AccessControl/
src/UnicoreCRM.ApiHost/
scripts/
```

Guidance only; do not touch unrelated code.

---

# 54. LIKELY FRONTEND AREAS

```text
src/modules/ai/
src/modules/customers/
src/modules/tasks/
src/platform/access-control/
generated API client
tests/e2e/
tests/quality/contracts/
```

Connected runtime remains backend-authoritative.

---

# 55. DEFINITION OF DONE

`CRM-PA` is DONE only when:

1. Workspace can explicitly enable/disable Proactive AI.
2. Disabled is default.
3. Customer Health remains trigger authority.
4. AT_RISK/CRITICAL are detected without LLM.
5. UNKNOWN creates no alert.
6. Stable risk cycle does not spam duplicates.
7. Escalation/recovery lifecycle is correct.
8. Snooze/Dismiss are durable.
9. Customer owner is explicit/authoritative.
10. Owner change does not leak/suppress incorrectly.
11. Current Customer permissions are rechecked on read/action.
12. Hidden Health cannot leak through Proactive/provider context.
13. Attention works without provider availability.
14. AI suggestion is user-requested only.
15. Suggestion is draft, not authority.
16. AI cannot mutate CRM by itself.
17. Only explicit confirmation can create v1 Task.
18. Tasks remains Task authority.
19. Task confirmation is idempotent.
20. Material transitions are audited.
21. Scheduler is Workspace/timezone aware, bounded, multi-instance safe.
22. No new Proactive project/DbContext/generic agent framework exists.
23. Mandatory backend/frontend gates pass.
24. Connected browser acceptance passes.
25. No unresolved Critical/High remains.

---

# 56. EXPECTED FINAL SHAPE

```text
Workspace policy (default OFF)
        ↓
Daily due evaluator
        ↓
Customers-owned Proactive Health Reader
        ↓
Customer Health authority
        ↓
CUSTOMER_HEALTH_RISK rule
        ↓
Durable ProactiveItem
        ↓
Authorized AI Attention Inbox
        ├── deterministic WHY
        ├── snooze
        ├── dismiss
        └── AI Suggestion (on demand)
                 ↓
            task draft
                 ↓
          explicit confirmation
                 ↓
         Tasks-owned participant
                 ↓
               Task
```

Intended product principle:

> **Detect proactively, explain safely, suggest helpfully, and mutate only after explicit human confirmation.**

---

# 57. FINAL AGENT REPORT FORMAT

Report must include:

- backend old SHA → new SHA;
- frontend old SHA → new SHA;
- commits;
- exact persistence shape;
- confirmation no new project/DbContext/generic tool framework;
- trigger authority evidence;
- no-background-LLM evidence;
- daily/timezone scheduler evidence;
- dedup/snooze/dismiss/escalation/recovery evidence;
- owner-change evidence;
- authorization/Health field-security evidence;
- provider-failure evidence;
- AI suggestion evidence;
- explicit Task confirmation evidence;
- Task idempotency/provenance evidence;
- migration/OpenAPI/generated-client status;
- backend/frontend gate results;
- connected E2E results;
- remaining operational risks.

Do not self-declare Controller REVIEW_PASS.

Finish exactly:

```text
CRM-PA READY FOR CONTROLLER REVIEW
```
