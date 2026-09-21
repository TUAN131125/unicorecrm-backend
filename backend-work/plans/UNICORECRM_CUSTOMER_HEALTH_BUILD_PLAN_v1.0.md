# UniCoreCRM — Customer Health Build Plan

**Plan ID:** `UNICORECRM-CUSTOMER-HEALTH-V1`  
**Version:** `1.0`  
**Milestone:** `CRM-CH-001 — Customer Health / Churn Baseline`  
**Depends on:** accepted Customer baseline, Lead → Customer workflow, Domain Events/Webhook baseline, `CRM-AI-002` REVIEW_PASS  
**Status:** READY FOR IMPLEMENTATION  
**Roadmap authority:** This is a subordinate execution plan. `UNICORECRM_ROADMAP_EXECUTION_PLAN.md` remains authoritative for roadmap ordering and overall project direction.

---

## 1. Canonical starting state

Backend canonical baseline:

`d5b9dea8560097a6aaeb6921cf8ef56289734937`

Frontend canonical baseline:

`72858589011ea789bb3bd361d4f12f7fc41a3549`

Current accepted architectural facts:

- Customer Health is a **feature owned by the existing Customers module**.
- Do **not** create a new `UnicoreCRM.CustomerHealth` project.
- Do **not** create a new Customer Health bounded context or DbContext.
- Customers remains the authority for Customer identity, lifecycle, authorization, field security and Customer read projections.
- CommercialEvidence remains the authority for effective purchase evidence.
- AI remains a read-only consumer of owner-authorized CRM projections.
- Existing frontend already contains Customer Health / Customer assessment presentation code and local heuristic logic. That code is not backend authority and must not remain the connected-mode source of truth.

The implementation must preserve accepted Customer, CommercialEvidence, AI, AccessControl and frontend architecture.

---

## 2. Roadmap mapping

This plan implements the roadmap CH task groups in a deliberately small rule-based v1:

- `CRM-CH-001` — define Customer Health / churn-risk business meaning;
- `CRM-CH-010` — establish purchase-evidence authority and data-quality rules;
- `CRM-CH-020` — implement deterministic baseline rule engine;
- `CRM-CH-030` — implement offline scenario evaluation and regression corpus;
- `CRM-CH-040` — expose explainable Customer read/API/UI/AI projections;
- `CRM-CH-050` — add minimal non-PII runtime instrumentation and keep the evaluation corpus as the initial feedback/regression loop.

This milestone does **not** claim predictive churn probability or ML accuracy.

---

## 3. Product objective

Deliver a small, explainable Customer Health feature inside Customers that answers:

1. How healthy is this Customer relationship based on authoritative purchase recency?
2. What heuristic churn-risk level does that imply?
3. Why did the system produce that result?

The runtime shape is:

```text
Authorized Customer
        +
CommercialEvidence purchase signal
        +
server-owned current time
        ↓
CustomerHealthCalculator
        ↓
CustomerHealthAssessment
        ↓
Customer list / Customer 360 / Customer AI context
```

No Customer Health persistence is required in v1.

Health is calculated on read from authoritative inputs.

---

## 4. Explicit anti-overengineering boundary

The following are **not** part of v1:

- no Customer Health project;
- no Customer Health DbContext;
- no `CustomerHealthAssessments` table;
- no health-history table;
- no scheduled/background health worker;
- no backfill job;
- no Workspace Health Settings UI;
- no editable scoring weights;
- no manual health override workflow;
- no health-change domain/integration event;
- no runtime health cache as a correctness dependency;
- no support signal;
- no activity/engagement signal;
- no invoice/payment signal;
- no product-usage signal;
- no AI-generated score;
- no ML model;
- no churn probability;
- no automatic Task/reminder/email/workflow;
- no Customer lifecycle mutation caused by health.

Do not introduce any of these merely because they might be useful later.

---

## 5. Ownership and code placement

Customer Health is implemented through the existing Customers layers.

Expected shape, adjusted to repository conventions as needed:

```text
src/UnicoreCRM.Crm/Customers
├── Domain
│   └── Health
│       ├── CustomerHealthCalculator.cs
│       └── CustomerHealthAssessment.cs
├── Application
│   └── existing Customer read handlers / small Health helpers
├── Contracts
│   └── CustomerHealth contracts/projections
└── Infrastructure
    └── existing Customers infrastructure only
```

Do not create four new layers underneath a separate Health module.

CommercialEvidence receives only the narrow owner contract and query implementation required to supply safe purchase-health signals.

---

## 6. Locked business semantics

### 6.1 Customer Health is a heuristic CRM signal

Customer Health v1 is a deterministic business heuristic based on purchase recency.

It is not:

- a probability of churn;
- a machine-learning prediction;
- a Customer lifecycle status;
- an AI opinion.

### 6.2 Canonical assessment fields

A Customer Health assessment exposes at least:

```text
score: int?                  // 0..100, null when insufficient evidence
healthBand                   // UNKNOWN | HEALTHY | WATCH | AT_RISK | CRITICAL
churnRisk                    // UNKNOWN | LOW | MEDIUM | HIGH | CRITICAL
confidence                   // NONE | LOW | MEDIUM | HIGH
purchaseCount
lastPurchaseAt?
expectedPurchaseCadenceDays?
daysSinceLastPurchase?
reasonCode
algorithmVersion
evaluatedAt
```

V1 may expose one primary reason rather than a general multi-factor framework because there is only one admitted signal.

### 6.3 No purchase evidence

A Customer with zero effective purchase evidence must return:

```text
score = null
healthBand = UNKNOWN
churnRisk = UNKNOWN
confidence = NONE
reasonCode = NO_PURCHASE_EVIDENCE
```

Zero purchase evidence must never mean `CRITICAL`, `CHURNED`, or unhealthy by implication.

### 6.4 Archived Customer

Archived Customers are not actively assessed.

The Customer read projection may omit `healthAssessment` / return it as null for archived Customers.

Do not convert archived state to `CRITICAL` or churn risk.

### 6.5 Customer lifecycle remains independent

Customer Health must not change:

- `Customer.Status`;
- owner;
- tier;
- segment;
- archive state;
- any other Customer business field.

Existing lifecycle values must not be inferred from Health.

---

## 7. Existing Customer health fields

The repository already contains:

- `Customer.Health`;
- `CustomerProfile.CalculatedHealth`;
- `CustomerProfile.ManualHealthOverride`;
- `FirstPurchaseAt` / `LastPurchaseAt`;
- care-related fields.

For this milestone:

1. **Do not make persisted `Customer.Health` the new scoring authority.**
2. Do not write daily/on-read results back to `Customer.Health`.
3. Do not increment `Customer.Version` merely because time changed and a derived health result changed.
4. Do not use `ManualHealthOverride` in v1.
5. Do not treat `CalculatedHealth` as an independent authority.
6. Existing `GOOD/WATCH/RISK` storage/check-constraint semantics may remain for backward compatibility; connected v1 Customer Health uses the new assessment projection.

This prevents background/version churn and avoids a migration solely to redefine the legacy field.

---

## 8. Authoritative v1 signal

The only admitted v1 signal is:

`PURCHASE_RECENCY`

Source authority:

`UnicoreCRM.CommercialEvidence`

Customers must not read:

- Orders DbContext;
- Invoice DbContext;
- Payment DbContext;
- CommercialEvidence DbContext directly.

Customers consumes a narrow CommercialEvidence contract.

---

## 9. CommercialEvidence narrow contract

Add an owner-safe contract conceptually equivalent to:

```text
ICustomerPurchaseHealthSignalReader
```

The exact repository-conformant type name is implementation-level.

The contract must support both single-Customer and batch Customer reads without exposing full commercial documents.

Input authority:

- `TrustedWorkspaceContext` from server-side Workspace resolution;
- one or more Customer relationship/buyer references;
- server-owned `asOf` time or a value supplied internally by the Customers application service.

A returned signal snapshot should contain no more than is required for calculation, conceptually:

```text
BuyerRef
PurchaseCount
FirstPurchaseAt?
LastPurchaseAt?
RecentPurchaseTimestamps   // latest up to 6 effective purchases
```

Do not return:

- Order amount;
- invoice/payment details;
- product price;
- full Order documents;
- arbitrary source payload;
- hidden CRM fields.

### 9.1 Buyer mapping

Use the existing Customer relationship identity:

```text
CONTACT
ORGANIZATION_ACCOUNT
```

to the existing CommercialEvidence buyer reference.

Never assume CustomerId == ContactId or OrganizationId.

### 9.2 Evidence types

All effective purchase evidence types currently admitted by CommercialEvidence may count:

- order completed;
- confirmed external purchase;
- historical purchase import.

The CommercialEvidence owner remains responsible for the meaning of effective purchase evidence.

---

## 10. Time and data-quality semantics

All calculations use UTC `DateTimeOffset` semantics.

Public callers cannot provide an arbitrary `asOf` value to manipulate Health.

Production `asOf` comes from server-owned `TimeProvider`.

Tests may inject a deterministic clock.

Purchase evidence later than `asOf` must not influence the assessment.

CommercialEvidence reader/calculator behavior for future-dated evidence must fail safe by excluding it from the health input and emitting safe diagnostic/metric evidence where practical.

Do not silently reinterpret malformed foreign data as healthy or unhealthy.

---

## 11. Expected purchase cadence

V1 has no Workspace configuration UI.

The algorithm uses:

```text
DEFAULT_PURCHASE_CADENCE_DAYS = 90
```

This constant belongs to the versioned v1 business heuristic and must be explicit in code/tests.

If sufficient Customer purchase history exists, derive a Customer-specific cadence.

### 11.1 Customer-specific cadence

Use the latest up to 6 effective purchase timestamps.

If there are at least 3 effective purchases and enough positive intervals to estimate cadence:

1. sort timestamps chronologically;
2. calculate positive whole-day intervals between adjacent purchases;
3. use the median interval;
4. clamp result to `7..365` days.

If reliable intervals cannot be derived, use the 90-day default.

Multiple purchases on the same day must not produce a zero-day cadence.

---

## 12. Scoring algorithm

Locked version identifier:

`CUSTOMER_HEALTH_PURCHASE_RECENCY_V1`

For Customers with purchase evidence:

```text
daysSinceLastPurchase = whole elapsed UTC days
recencyRatio = daysSinceLastPurchase / expectedPurchaseCadenceDays
```

Score direction:

- `100` = healthiest under this heuristic;
- `0` = strongest purchase-recency risk under this heuristic.

Piecewise score:

| Recency ratio | Score |
|---:|---:|
| `<= 0.75` | `100` |
| `0.75 .. 1.0` | interpolate `100 .. 85` |
| `1.0 .. 1.25` | interpolate `85 .. 70` |
| `1.25 .. 1.5` | interpolate `70 .. 50` |
| `1.5 .. 2.0` | interpolate `50 .. 25` |
| `2.0 .. 3.0` | interpolate `25 .. 0` |
| `>= 3.0` | `0` |

Round the final score deterministically to an integer.

The pure calculator must satisfy:

```text
same input + same asOf + same algorithmVersion = same output
```

---

## 13. Health bands and churn-risk classification

Health bands:

| Score | Health band |
|---:|---|
| `80..100` | `HEALTHY` |
| `60..79` | `WATCH` |
| `30..59` | `AT_RISK` |
| `0..29` | `CRITICAL` |
| no evidence | `UNKNOWN` |

Churn-risk classification is a label derived from Health, not a separate prediction model:

| Health | Churn risk |
|---|---|
| `HEALTHY` | `LOW` |
| `WATCH` | `MEDIUM` |
| `AT_RISK` | `HIGH` |
| `CRITICAL` | `CRITICAL` |
| `UNKNOWN` | `UNKNOWN` |

Never expose the churn-risk label as a statistical probability.

---

## 14. Confidence

Confidence reflects evidence coverage, not model certainty.

V1 mapping:

| Effective purchase count | Confidence |
|---:|---|
| `0` | `NONE` |
| `1` | `LOW` |
| `2` | `MEDIUM` |
| `>= 3` | `HIGH` |

A `HEALTHY + LOW` result is valid.

UI and AI must not hide low confidence.

---

## 15. Explainability

Every non-archived assessment must contain a stable machine-readable reason code.

Minimum v1 reason vocabulary:

- `NO_PURCHASE_EVIDENCE`
- `PURCHASE_RECENCY_HEALTHY`
- `PURCHASE_WITHIN_EXPECTED_CADENCE`
- `PURCHASE_CADENCE_SLIPPING`
- `PURCHASE_OVER_EXPECTED_CADENCE`
- `PURCHASE_SEVERELY_OVERDUE`

The exact threshold-to-reason mapping must be deterministic and covered by tests.

The response may also include safe numeric facts already present in the assessment:

- days since last purchase;
- expected cadence days;
- purchase count.

Do not generate the authoritative explanation with Gemini/OpenAI.

Frontend localizes reason codes into Vietnamese/English display text.

---

## 16. Backend Customer read integration

Health is calculated only after normal Customer authorization succeeds.

### 16.1 Single Customer read

For authorized visible Customer reads:

```text
Customer authorization
→ Customer record access
→ Customer relationship ref
→ CommercialEvidence health signal
→ CustomerHealthCalculator
→ Customer health assessment projection
```

### 16.2 Customer list

No N+1 CommercialEvidence queries.

For one authorized Customer page:

```text
visible Customer page
→ collect relationship refs
→ one bounded batch CommercialEvidence read
→ calculate each assessment in memory
→ project list response
```

A list page must not perform one CommercialEvidence query per Customer.

### 16.3 Customer 360

`Customer360ReadModel` must expose the same authoritative Health assessment or equivalent safe metrics.

Do not independently reconstruct Health from frontend support/tasks/orders/timeline snapshots.

### 16.4 Mutation responses

Customer mutations do not need to synchronously calculate Health merely to populate mutation DTOs.

If `healthAssessment` is an optional property on a shared Customer document, mutation responses may omit it.

Do not make Customer write availability depend on CommercialEvidence health calculation.

---

## 17. Public API strategy

Prefer extending existing Customer read contracts rather than creating a separate Health service/API family.

V1 does not require a new root `/customer-health` API.

Expected connected contract direction:

- `GET /customers` — each returned visible Customer may include an authoritative `healthAssessment` summary;
- existing single Customer read — include the same assessment where appropriate;
- `GET /customers/{id}/360` — include authoritative Health assessment and safe reason facts;
- internal `ICustomerSummaryReader` — expose a safe Health summary for AI.

V1 explicitly does **not** require:

- health history endpoint;
- health configuration endpoint;
- whole-Workspace health summary endpoint;
- health-specific sorting/filtering contract.

Do not implement incorrect global sorting/filtering over only a paginated subset.

If an existing frontend Health route remains, it must use the same server-authoritative Customer page semantics and must not claim whole-Workspace completeness that the backend contract does not provide.

---

## 18. AI integration

AI consumes Customer Health through Customers-owned safe projection.

Expected flow:

```text
AI Customer context
    ↓
ICustomerSummaryReader
    ↓
Customers authorization / record security
    ↓
Customer Health assessment
```

AI must not:

- query CommercialEvidence directly for churn inference;
- query Customers persistence directly;
- calculate an authoritative Health score itself;
- override Health;
- create retention Tasks/workflows as part of this milestone.

AI may explain the authorized Health assessment and its safe reason facts.

If Health cannot be resolved safely, AI must not invent it.

---

## 19. Frontend current-state correction

The accepted frontend baseline already contains:

- `CustomerHealthPage`;
- `health-route.tsx`;
- `buildCustomerRelationshipAssessment`;
- Customer assessment signals based on Support, Tasks, Deals, Returns, Orders, Timeline and local Customer fields;
- recommended actions such as CREATE_TASK / CREATE_CARE;
- legacy `CustomerHealth = GOOD | WATCH | RISK` presentation types.

This existing code is **not the connected Customer Health authority** for this milestone.

### 19.1 Connected mode rule

Connected Customer Health must come from generated/adapted backend Customer contracts.

Connected mode must not derive canonical Health from:

- `getOrderListSnapshot`;
- `getSupportCasesSnapshot`;
- `getTaskActivitySnapshot`;
- local timeline state;
- local `buildCustomerRelationshipAssessment` scoring.

### 19.2 Demo mode

Existing local heuristic code may remain only if it is explicitly isolated to demo/local presentation and cannot become a connected fallback.

If retaining it causes ambiguity or dead code, remove or narrow it.

Do not silently reuse Support/Activity signals in connected Health because those authorities are not admitted in v1.

### 19.3 Legacy health type

Do not force the new assessment into the legacy persisted `GOOD/WATCH/RISK` field.

Introduce/adapt a connected assessment type matching the backend canonical values:

- `UNKNOWN`
- `HEALTHY`
- `WATCH`
- `AT_RISK`
- `CRITICAL`

Legacy demo types may be adapted separately.

---

## 20. Frontend surfaces

V1 requires only lightweight presentation changes.

### Customer list

Show authoritative Health band where available.

Do not add inaccurate whole-Workspace Health filters/sorting unless backend semantics support them correctly.

### Customer detail / Customer 360

Show a compact Health card containing:

- score;
- health band;
- churn-risk label;
- confidence;
- safe reason text;
- days since last purchase;
- expected cadence days.

Do not display fake precision or “X% chance of churn”.

### Existing Customer Health page

It may remain if converted to backend-authoritative data.

It must stop subscribing to Orders/Support/Task snapshots merely to recompute Health locally.

It must not expose automatic task/care actions as part of this milestone.

---

## 21. Performance and CommercialEvidence index

The current CommercialEvidence schema has source-identity indexes but no dedicated buyer/time lookup index suitable for repeated Health reads.

Add an owner-owned forward-only CommercialEvidence index migration for the narrow reader if confirmed by the implementation/query plan.

Expected index direction:

```text
WorkspaceId
BuyerRefType
BuyerRefId
OccurredAt
```

Exact naming/order/descending details may follow repository conventions.

Do not add Customers Health tables.

No cache is required for correctness in v1.

---

## 22. Offline evaluation — roadmap `CRM-CH-030`

Because v1 is a business heuristic rather than ML, offline evaluation means deterministic scenario validation, not predictive AUC/precision/recall claims.

Create a versioned scenario corpus or executable equivalent covering at least:

1. zero purchase evidence → UNKNOWN/NONE;
2. one recent purchase → HEALTHY with LOW confidence;
3. two purchases → MEDIUM confidence;
4. >=3 regular purchases → derived cadence and HIGH confidence;
5. same-day duplicate purchases do not produce zero-day cadence;
6. fallback 90-day cadence when history is insufficient;
7. 7-day lower clamp;
8. 365-day upper clamp;
9. exact score-band boundaries 80/60/30;
10. increasing time since last purchase never improves score when all other inputs stay constant;
11. future-dated evidence does not improve Health;
12. same input/asOf/version produces exactly the same result;
13. Workspace A evidence never affects Workspace B;
14. archived Customer does not receive an active Health assessment.

The scenario corpus is the v1 business acceptance/evaluation artifact.

No claim of real-world churn predictive accuracy is permitted.

---

## 23. Minimal monitoring — roadmap `CRM-CH-050`

Do not build a monitoring subsystem.

Add lightweight safe telemetry consistent with repository conventions, sufficient to observe:

- health calculation count;
- calculation latency;
- band counts;
- confidence counts;
- UNKNOWN rate;
- future/invalid evidence exclusions if implemented.

Do not log:

- Customer names;
- full purchase records;
- Order amounts;
- hidden fields;
- raw user data unnecessary for operations.

No persistent feedback table is required in v1.

The versioned offline scenario corpus plus runtime safe metrics form the initial feedback/regression loop.

---

## 24. Security invariants

Health must not become an authorization side channel.

Required:

1. existing `customers.view` and Customer record-access semantics remain mandatory;
2. no new health-read capability is required in v1;
3. denied Customer record → no Health result;
4. wrong Workspace → no Health result;
5. CommercialEvidence reader receives trusted Workspace authority, never request-supplied Workspace authority;
6. Health response contains only safe derived purchase-recency facts;
7. hidden commercial details are not exposed through reason/evidence payloads;
8. connected frontend does not bypass backend authority by recomputing Health from local snapshots.

---

## 25. Backend work packages

### `CRM-CH-001 — Business target and canonical vocabulary`

Implement/lock:

- assessment DTO/value vocabulary;
- Health bands;
- churn-risk labels;
- confidence labels;
- reason codes;
- algorithm version constant;
- no-purchase semantics.

### `CRM-CH-010 — Purchase signal authority`

Implement:

- narrow CommercialEvidence health-signal contract;
- batch owner implementation;
- Workspace isolation;
- recent timestamps / count projection;
- buyer/time index migration if needed;
- no foreign DbContext use by Customers.

### `CRM-CH-020 — Rule engine`

Implement pure deterministic calculator:

- cadence resolution;
- 90-day fallback;
- clamps;
- piecewise score;
- bands;
- risk mapping;
- confidence;
- reason code;
- deterministic clock input.

### `CRM-CH-030 — Offline evaluation`

Implement executable scenario corpus/tests from section 22.

### `CRM-CH-040 — Customer read/API/AI integration`

Integrate assessment into:

- authorized Customer list projection using batch signal read;
- single Customer read where applicable;
- Customer 360;
- CustomerSummaryReader for AI;
- canonical OpenAPI.

### `CRM-CH-050 — Minimal monitoring`

Add safe metrics/logging only.

Do not introduce persistence/history/settings/workers.

---

## 26. Frontend work packages

### `CRM-CH-UI-010 — Generated contract authority`

Regenerate/adapt canonical Customer API client after backend contract changes.

Generated DTOs remain transport-only.

### `CRM-CH-UI-020 — Connected health model`

Add frontend application/presentation model for backend Health assessment.

Do not make legacy `GOOD/WATCH/RISK` demo state the connected authority.

### `CRM-CH-UI-030 — Customer list/detail`

Render authoritative Health in existing Customer surfaces.

### `CRM-CH-UI-040 — Existing Health page cleanup`

Remove connected dependencies on Orders/Support/Tasks/local activity snapshots for Health calculation.

No connected demo fallback.

### `CRM-CH-UI-050 — AI context alignment`

Ensure frontend AI context does not independently rebuild Customer Health.

Backend Customer AI context remains authoritative.

---

## 27. Required backend verification matrix

### Calculator

- all score breakpoints;
- band boundaries;
- risk mapping;
- confidence mapping;
- cadence median;
- clamps;
- same-day purchases;
- no purchase;
- monotonic time decay;
- deterministic replay.

### CommercialEvidence reader

- single buyer;
- batch buyers;
- Contact buyer;
- Organization Account buyer;
- all admitted effective purchase evidence types;
- future evidence exclusion;
- Workspace isolation;
- no raw commercial details in projection;
- bounded recent timestamp projection;
- query is not N+1.

### Customer API

- authorized single Customer receives assessment;
- authorized Customer 360 receives same assessment semantics;
- Customer list performs bounded batch enrichment;
- no-purchase Customer is UNKNOWN;
- archived Customer has no active assessment;
- record-denied Customer does not leak Health;
- wrong Workspace does not leak Health;
- Customer mutation behavior/version remains unchanged by read-time Health calculation.

### AI

- authorized Customer AI context receives safe Health summary;
- Health values match Customers calculation;
- AI context does not query foreign persistence;
- denied Customer remains denied;
- no business mutation introduced.

---

## 28. Required frontend verification

Verify at least:

- generated client freshness;
- connected Customer Health uses backend assessment;
- no connected Health import/path depends on Support/Task/Order snapshots for scoring;
- no connected fallback to local heuristic;
- UNKNOWN state renders clearly;
- confidence renders clearly;
- score/risk wording does not claim probability;
- Customer detail/360 Health card works;
- Customer list Health indicator works;
- existing Health route, if retained, uses server authority;
- Customer AI UI receives backend-grounded Health context;
- typecheck;
- lint;
- production build;
- bundle budget;
- Customer quality contracts/regressions.

Existing client-side broader relationship intelligence may remain only if explicitly separated from canonical connected Customer Health semantics.

---

## 29. Connected browser E2E

Run at least one real connected browser path:

```text
browser
→ connected frontend
→ real ApiHost
→ authentication
→ Workspace
→ customers.view / record access
→ Customers
→ CommercialEvidence narrow reader
→ CustomerHealthCalculator
→ Customer UI
```

Required browser scenarios:

### E2E-A — No purchase evidence

Visible Customer with no purchase evidence:

- Health = UNKNOWN;
- no percentage churn claim;
- no demo-derived Support/Task/Order score appears.

### E2E-B — Recent purchase

Visible Customer with recent authoritative purchase evidence:

- assessment is non-UNKNOWN;
- expected score/band from deterministic fixture;
- correct confidence;
- Customer detail and Customer list agree.

### E2E-C — Overdue purchase

Seed/append authoritative purchase evidence old enough to cross the selected threshold:

- score decreases as expected;
- Health/Risk label matches calculator;
- reason shows safe purchase-recency facts.

### E2E-D — Permission isolation

Principal without Customer read/record access cannot obtain Health through Customer UI/API/AI.

### E2E-E — AI alignment

For an authorized Customer, connected AI receives the same server-authoritative Health band/risk/reason and does not invent a conflicting score.

Paid live AI-provider calls are not required; deterministic accepted provider transport is sufficient.

The browser test must not mock `/customers` or `/ai/advisories` when proving connected behavior.

---

## 30. Migration plan

Expected schema impact is intentionally small.

### Customers

No Customer Health table migration is planned.

Do not change persisted `Customer.Health` vocabulary merely to implement v1 derived Health.

### CommercialEvidence

A forward-only migration may add the buyer/time index required by the new narrow reader.

If a migration is added, mandatory evidence:

- clean database migrate;
- upgrade from accepted canonical baseline;
- pending-model check = none;
- no destructive data rewrite.

If implementation proves an index is unnecessary and no schema change occurs, report the evidence rather than creating a migration for ceremony.

---

## 31. Mandatory regression gates

Backend:

- solution build with 0 warnings / 0 errors;
- Customers verifier/regression;
- CommercialEvidence verifier/regression;
- AccessControl/customer authorization regressions if authorization code touched;
- AI real verifier Customer context;
- OpenAPI generation/freshness;
- migration gates when schema changes.

Frontend:

- Customer unit/contract/integration quality checks;
- Customer intelligence contract updated to distinguish canonical backend Health from local/demo intelligence;
- generated client freshness;
- typecheck;
- lint;
- production build;
- bundle budget;
- connected Customer browser E2E.

Known unrelated baseline differentials may remain non-blocking only when reproduced unchanged against the accepted baseline with the same command/environment.

---

## 32. Definition of Done

`CRM-CH-001` is implementation-complete only when all of the following hold:

- Customer Health remains a Customers-owned feature;
- no new Health project/DbContext/table/worker exists;
- purchase recency is the only admitted v1 signal;
- CommercialEvidence owns the purchase signal query;
- Customers does not use a foreign DbContext;
- no purchase means UNKNOWN;
- score is deterministic 0..100 or null;
- health bands match the locked thresholds;
- churn risk is classification only, never probability;
- confidence is visible and deterministic;
- 90-day fallback cadence and customer-specific cadence work;
- reason is explainable without AI;
- connected frontend no longer calculates canonical Health from local Support/Task/Order/activity snapshots;
- Customer list/detail/360 use backend-authoritative assessment;
- AI reads the Customers-owned Health summary and cannot override it;
- Customer lifecycle/version is not mutated by read-time Health;
- no proactive action is introduced;
- offline scenario corpus passes;
- safe monitoring exists;
- security/isolation tests pass;
- mandatory build/regression/browser gates pass.

---

## 33. Implementation order

Execute in this order:

1. inspect current Customer frontend health/assessment code and classify connected vs demo usage;
2. implement canonical Customer Health vocabulary/value objects;
3. implement pure calculator + unit/offline scenario corpus;
4. implement CommercialEvidence narrow batch signal reader;
5. add buyer/time index migration only if required;
6. integrate single Customer/Customer360 reads;
7. integrate Customer list with one batch health-signal read per page;
8. extend CustomerSummaryReader / AI Customer context;
9. update canonical OpenAPI/generated client;
10. replace connected frontend Health authority;
11. update Customer list/detail/existing Health route;
12. add minimal telemetry;
13. run backend verification;
14. run frontend verification;
15. run connected browser E2E;
16. run regressions/migration gates;
17. commit/push only after all required gates pass.

Do not start Proactive AI during this milestone.

---

## 34. Commit strategy

Do not create WIP commits on canonical branches.

Do not force-push or rewrite history.

After mandatory gates pass, suggested commits:

Backend:

`feat(customers): add authoritative customer health baseline`

Frontend:

`feat(customers): use server-authoritative customer health`

If CommercialEvidence is included in the backend commit, it remains an owner-contract support change for Customers Health and must not become a cross-owner persistence shortcut.

---

## 35. Controller review

After push, Controller independently reviews actual source and executable evidence.

Review scope:

- branch ancestry;
- no new unintended Health subsystem;
- Customer ownership;
- CommercialEvidence narrow contract;
- no foreign DbContext access;
- deterministic algorithm;
- threshold/boundary correctness;
- UNKNOWN semantics;
- cadence derivation;
- future evidence handling;
- batch/no-N+1 behavior;
- authorization / Workspace isolation;
- frontend removal of connected local heuristic authority;
- AI owner-safe projection;
- OpenAPI/generated client authority;
- migration/index correctness;
- connected browser evidence;
- regressions.

Allowed controller outcomes:

- `REVIEW_PASS`
- `NEEDS_FIX`

The implementation agent must not self-accept the milestone.

---

## 36. Roadmap boundary

After `CRM-CH-001` reaches Controller `REVIEW_PASS`, return to the agreed roadmap.

Next milestone:

`PA — Proactive AI / Reminder / Suggested Action`

Customer Health v1 only supplies an explainable read signal.

Do not implement reminders, alerts, retention Tasks, automated outreach or mutation side effects as part of this plan.
