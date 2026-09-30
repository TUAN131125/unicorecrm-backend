# UNICORECRM_PROACTIVE_AI_CONFIRMED_TASK_BUILD_PLAN_v1.0

**Feature:** `CRM-PA-040 — User-confirmed Task creation`  
**Backend baseline:** `3d69e398056b92ebefe479affdf6e887c522b628`  
**Frontend:** out of scope for this backend checkpoint.  
**Preconditions:** Authorized Attention API and `CRM-PA-030` are `REVIEW_PASS`.

---

## 1. Goal

Allow an authorized user to explicitly confirm a follow-up Task from an OPEN Proactive Attention item.

Flow:

```text
OPEN Attention item
        ↓
optional PA-030 AI suggestion
        ↓
user reviews/edits final Task intent
        ↓
POST confirm-task
        ↓
server re-authorizes current Attention + Customer
        ↓
Tasks-owned narrow participant
        ↓
ordinary createTask authority
        ↓
Task created with Tasks idempotency/audit/outbox
```

The only CRM mutation admitted in PA-040 is **CREATE FOLLOW-UP TASK**.

---

## 2. Locked semantics

### 2.1 Human confirmation is mandatory

No Task may be created by:

- proactive scheduler;
- Attention creation;
- Attention refresh;
- AI suggestion request;
- provider response;
- background worker.

The mutation happens only after an authenticated explicit user confirmation request.

### 2.2 Public operation

Add conceptually:

```http
POST /ai/proactive/items/{itemId}/confirm-task
```

Require `Idempotency-Key`.

Do **not** require `If-Match` for the Proactive item because this operation does not mutate the item. Current authorization/state is re-read before mutation.

### 2.3 Final user intent

Request contains only final Task fields the user controls:

```json
{
  "title": "Follow up with customer",
  "description": "Discuss current needs and record feedback.",
  "assigneeId": "member_...",
  "dueAt": "2026-09-30T03:00:00Z",
  "priority": "NORMAL"
}
```

Rules:

- `title`: authoritative Tasks validation, current max 300;
- `description`: optional, authoritative Tasks validation, current max 4000;
- `assigneeId`: explicit user choice, Tasks validates active Workspace member;
- `dueAt`: explicit user choice, Tasks validates the accepted UTC contract;
- `priority`: optional; Tasks vocabulary/default remains authority.

Do not accept:

- `taskId`;
- `customerId`;
- `ownerId`;
- `sourceRef`;
- `recordRef`;
- `relationshipRef`;
- `dedupeKey`;
- provider/model/credential;
- Health/Severity/ReasonCode.

The server constructs provenance.

### 2.4 AI is not Task authority

AI may have proposed only `Title` and `Description` in PA-030.

AI does not choose or authorize:

- assignee;
- due date;
- priority;
- whether Task is created.

The user may edit AI text before confirmation.

PA-040 does not require the final title/description to byte-match an AI output.

### 2.5 Suggestion history is not required

PA-030 suggestions are ephemeral and the current execution ledger does not expose a read-by-execution authority.

Therefore PA-040 does not invent a fake “suggestion validation” contract and does not require a `suggestionExecutionId`.

The security authority is:

```text
current user confirmation
+ current Attention authorization
+ current Customer authorization
+ Tasks create authority
```

---

## 3. Reauthorization before Tasks mutation

Before calling Tasks:

1. current authenticated/trusted Workspace;
2. `ai.proactive.use`;
3. current Proactive item exists in current Workspace;
4. `SubjectType == CUSTOMER`;
5. `TriggerType == CUSTOMER_HEALTH_RISK`;
6. `Status == OPEN`;
7. stored item owner equals current member;
8. Customers-owned current authorization succeeds;
9. `customers.view`;
10. current Customer record scope;
11. current Customer owner still equals current member;
12. Customer Health remains readable.

Any failure before Tasks means:

```text
Task create call count = 0
```

Use a Customers-owned narrow projection such as the already accepted `ICustomerAttentionReader` if it provides exactly the required current authorization/presentation facts.

Do not read `CustomersDbContext` from AI.

---

## 4. Tasks ownership boundary

Add a narrow Tasks-owned contract, conceptually:

```text
IProactiveTaskCreationParticipant
```

Contract lives in:

```text
UnicoreCRM.Operations.Tasks.Contracts
```

Implementation lives inside Tasks.

AI/Proactive must not reference:

```text
TasksDbContext
ITasksPersistence
Task domain entities
CreateTask.Handler internals directly across ownership
```

The participant internally delegates to the ordinary `CreateTask.Handler`.

---

## 5. Ordinary Tasks authority must remain unchanged

The participant must preserve the existing createTask behavior:

- `tasks.create`;
- Tasks field security;
- Tasks create validation;
- active Workspace assignee validation;
- serializable creation transaction;
- Tasks idempotency record;
- Tasks command audit;
- Tasks outbox `TASK_CREATED`;
- Tasks source-of-truth Task identity/version.

Do not duplicate these rules in AI.

Do not require `tasks.assign` unless existing `createTask` authority itself requires it. Current source shows assignment on create is governed by `tasks.create` plus field security/active-member validation.

---

## 6. Task provenance

The server constructs:

```text
TaskSourceReference.Type = PROACTIVE_AI
TaskSourceReference.Id   = ProactiveItemId
```

Do not let the client supply or override this provenance.

`SourceEvidence` should remain empty/null in v1 unless an existing Tasks contract requires a bounded safe value.

Do not persist AI raw output or prompt as Task source evidence.

Do not invent a new generic source-reference vocabulary.

---

## 7. Customer linkage

Do not invent a new Customer relationship semantic inside Tasks.

V1 guaranteed provenance is:

```text
SourceRef = PROACTIVE_AI / ProactiveItemId
```

If repository authority already has a canonical Customer `RecordRef.ModuleKey`, the implementation may also carry a scalar Customer record reference only after proving that canonical vocabulary from source.

Otherwise omit `RecordRef` rather than inventing a module key.

No foreign owner read from Tasks is allowed.

---

## 8. Idempotency

The HTTP confirmation requires an `Idempotency-Key` of the repository-standard bounded shape.

Pass that key into the ordinary Tasks create command.

Required behavior:

```text
same key + same final Task intent
→ same Task
→ Tasks response outcome REPLAYED

same key + changed final Task intent
→ idempotency conflict
→ no second Task
```

The final fingerprint must include the Tasks-owned normalized creation intent including server-owned Proactive source provenance.

Do not create a second idempotency store in AI for Task creation.

Different idempotency keys are independent user intents; do not invent a global “only one Task per Attention item ever” rule in v1.

---

## 9. Proactive item lifecycle

Successful Task creation does **not** automatically:

- mark Seen;
- Snooze;
- Dismiss;
- Resolve;
- change Severity;
- change ReasonCode;
- change RiskCycleKey;
- increment Proactive item Version.

Attention lifecycle stays deterministic and user-controlled under the existing semantics.

---

## 10. Response

Return a bounded Proactive confirmation response, conceptually:

```json
{
  "itemId": "proactive_...",
  "taskId": "task_...",
  "taskVersion": 0,
  "outcome": "COMMITTED"
}
```

Replay may return:

```text
outcome = REPLAYED
```

Do not duplicate the full Tasks read model through the AI API unless repository implementation requires it.

The authoritative Task can be read later from Tasks APIs under Tasks read authorization.

---

## 11. Error propagation

Preserve Tasks authority.

When the Tasks participant refuses creation, map the safe Tasks error semantics without pretending the failure came from AI.

Important cases:

- `tasks.create` denied;
- field-security write denied;
- invalid title/description;
- invalid/inactive assignee;
- invalid dueAt;
- invalid priority;
- idempotency key reused with changed intent.

Do not leak internal persistence details.

---

## 12. Audit and provenance

Tasks audit/outbox are the authoritative mutation evidence.

After successful COMMITTED or REPLAYED Task result, Proactive should ensure safe evidence:

```text
TASK_SUGGESTION_ACCEPTED
```

Safe metadata only:

- WorkspaceId;
- MemberId;
- ProactiveItemId;
- CustomerId;
- TaskId;
- Tasks outcome;
- CorrelationId;
- timestamp.

Never include:

- prompt;
- raw AI provider output;
- full Task description;
- credentials;
- hidden Customer fields.

Because Tasks and `platform_ai` are separate owner persistence boundaries, do not attempt a distributed transaction.

A Proactive audit failure after a committed Task must never roll back or cause duplicate Task creation.

Prefer an idempotent “record audit once” store primitive keyed by deterministic audit identity so a retry can repair missing Proactive evidence without duplicating it.

Tasks `TASK_CREATED` audit/outbox + `SourceRef(PROACTIVE_AI, itemId)` remain the authoritative mutation provenance.

---

## 13. Cross-owner failure semantics

### Before Task commit

If Attention/Customer/Tasks authorization or validation fails:

```text
no Task
no TASK_SUGGESTION_ACCEPTED
```

### Task committed, Proactive audit fails

The Task remains committed.

Do not return semantics that invite a non-idempotent second Task.

A retry with the same `Idempotency-Key` must replay the same Task and may repair the Proactive audit.

### Request cancellation

If cancellation occurs before Tasks commit:

```text
no Task
```

If Tasks already committed before transport cancellation, Tasks idempotency must make the next same-key retry converge on the existing Task.

---

## 14. No provider call in PA-040

`confirm-task` must not invoke Gemini/OpenAI.

No LLM is needed to create the Task.

PA-030 already produced advisory text.

The final request is user-controlled intent.

---

## 15. No new persistence model expected

Expected:

```text
no new DbContext
no new production project
no Tasks schema migration
no platform_ai schema migration
```

If implementation genuinely requires schema change, stop and report:

```text
IMPLEMENTATION_CONSTRAINT
```

with exact source/runtime evidence.

---

## 16. Expected implementation areas

Conceptual source touch map:

```text
src/UnicoreCRM.AI/Gateway/
    AiEndpoints.cs
    Proactive confirmation request/response contracts
    ProactiveTaskConfirmationApplication.cs

src/UnicoreCRM.Operations/Tasks/Contracts/
    ProactiveTaskCreationParticipant.cs

src/UnicoreCRM.Operations/Tasks/Application/
    Proactive Task participant implementation

src/UnicoreCRM.Operations/Tasks/
    TasksModule.cs

src/UnicoreCRM.PlatformOperations/AiExecution/
    only if a small idempotent Proactive audit-once primitive is needed

scripts/ProactiveVerifier/
    extend existing verifier only
```

Do not create another verifier project.

---

## 17. Mandatory executable scenarios

1. own OPEN item + valid final intent + all permissions -> one Task created.
2. same idempotency key + same intent -> same Task, `REPLAYED`.
3. same idempotency key + changed title -> conflict, no second Task.
4. same key + changed assignee -> conflict, no second Task.
5. same key + changed dueAt/priority -> conflict, no second Task.
6. other owner -> zero Tasks create.
7. Customer reassigned -> zero Tasks create.
8. missing `ai.proactive.use` -> zero Tasks create.
9. missing `customers.view` -> zero Tasks create.
10. Customer record scope denied -> zero Tasks create.
11. Customer Health hidden/masked -> zero Tasks create.
12. SNOOZED item -> zero Tasks create.
13. DISMISSED item -> zero Tasks create.
14. RESOLVED item -> zero Tasks create.
15. foreign Workspace item -> zero Tasks create.
16. missing `tasks.create` -> no Task.
17. Tasks title validation -> preserved.
18. Tasks description validation -> preserved.
19. inactive assignee -> preserved Tasks validation.
20. Tasks field security blocks required/optional create fields correctly.
21. priority vocabulary/default -> preserved Tasks behavior.
22. dueAt validation -> preserved Tasks behavior.
23. created Task SourceRef.Type == `PROACTIVE_AI`.
24. created Task SourceRef.Id == ProactiveItemId.
25. client cannot override SourceRef.
26. no provider call.
27. successful confirmation leaves Proactive item unchanged.
28. failed confirmation leaves Proactive item unchanged.
29. no Customer mutation.
30. Tasks command audit created once.
31. `TASK_CREATED` outbox created once.
32. Tasks idempotency record created once.
33. `TASK_SUGGESTION_ACCEPTED` contains safe identifiers only.
34. replay does not duplicate Task/outbox/Tasks audit.
35. replay does not duplicate Proactive accepted audit.
36. simulated Proactive accepted-audit failure after Task commit does not create a second Task on same-key retry.
37. cancellation/lost acknowledgment after commit converges to same Task.
38. no raw AI prompt/output is written to Task/proactive audit.
39. no migration/pending-model change.

---

## 18. Regression gates

Mandatory:

```text
full solution build: 0 warnings / 0 errors
Proactive scenario verifier
Proactive real SQL verifier
Tasks verifier/regression corpus
Customers real ApiHost / AccessControl security
Customer Health corpus
CommercialEvidence verifier
AccessControl affected verifier
AI advisory/provider regressions if touched
PlatformOperations pending-model check
Tasks pending-model check
Customers pending-model check
migration clean / accepted-baseline upgrade
git diff --check
```

---

## 19. Out of scope

PA-040 does not include:

- auto Task creation;
- recurring Tasks;
- task templates;
- email sending;
- SMS/push/Slack/Teams;
- automatic Dismiss/Resolve after Task creation;
- Customer owner changes;
- Customer Health changes;
- generic AI mutation tools;
- generic workflow engine;
- frontend Attention UX;
- Lead/Deal/Support proactive actions.

---

## 20. Definition of Done

PA-040 is complete only when:

```text
explicit user confirmation only
current Attention reauthorization
current Customer authorization
Tasks.create authority preserved
Tasks field security preserved
active assignee validation preserved
user controls assignee/dueAt/priority
AI controls none of those fields
server-owned PROACTIVE_AI provenance
durable Tasks idempotency
same-key replay converges
changed-intent reuse conflicts
Tasks audit/outbox preserved
safe Proactive acceptance evidence
no automatic Attention lifecycle change
no provider invocation
no Customer mutation
no schema migration
no new DbContext/project
required regressions pass
no unresolved Critical/High
```

---

## 21. Controller handoff

Implement from canonical baseline:

```text
3d69e398056b92ebefe479affdf6e887c522b628
```

Do not commit/push before controller review.

Produce one complete replacement source delta against that SHA, plus:

- changed files;
- insertions/deletions;
- SHA-256;
- forward/reverse apply validation;
- build result;
- Proactive/Tasks/Customers/AccessControl/Health/CommercialEvidence results;
- SQL idempotency/failure evidence;
- pending-model/migration checks;
- `git diff --check`;
- explicit confirmation of no provider call, no frontend, no new project/DbContext/migration.

Finish:

```text
CRM-PA-040 CONFIRMED TASK READY FOR CONTROLLER REVIEW
```
