# UniCoreCRM — Roadmap Execution Plan

**Plan ID:** `UNICORECRM-ROADMAP-CONTACT-TO-PROACTIVE-AI`  
**Version:** `1.0`  
**Status:** `ACTIVE`  
**Ngày lập:** `2026-09-08`  
**Điểm bắt đầu thực thi:** `C0 — Contact Write & Access Stabilization`  
**Phạm vi:** Frontend `TUAN131125/unicorecrm-web`, Backend `TUAN131125/unicorecrm-backend`, và bộ quy tắc `.unicore-ai` trong `UnicoreCRM_AI_Engineering_Control_System_v1.2.2_Multi_Agent_Installable.zip`.

---

## 1. Mục tiêu của plan

Plan này chuyển roadmap hiện tại thành một kế hoạch thực thi có thể giao việc, review, test và đóng milestone theo bằng chứng.

Điểm xuất phát **không phải Foundation và không phải Lead**. Lead đã là baseline có sẵn; dự án hiện đang ở **Contact**. Vì vậy thứ tự chính thức của plan là:

```text
Lead baseline (đã đi qua)
        │
        ▼
C0  Contact Write & Access Stabilization   ← CURRENT
        │
        ▼
C1  Contact Create
        │
        ▼
C2  Contact Read
        │
        ▼
C3  Contact Update
        │
        ▼
C4  Contact Delete / Archive
        │
        ▼
C5  Contact UX Completion
        │
        ▼
C6  Contact Relationships
        │
        ▼
O   Organization
        │
        ▼
CU  Customer
        │
        ▼
WF  Lead → Customer Workflow
        │
        ▼
EV  Domain Events / Webhook
        │
        ▼
AI  CRM-aware AI Chat
        │
        ▼
CH  Customer Health / Churn
        │
        ▼
PA  Proactive AI / Reminder / Suggested Action
```

Plan này có bốn mục tiêu quản trị chính:

1. Không quay lại xây lại những phần đã có chỉ vì roadmap lý thuyết bắt đầu từ đầu.
2. Không sửa UI theo kiểu che triệu chứng khi nguyên nhân nằm ở quyền truy cập, runtime capability hoặc contract.
3. Mọi thay đổi có mutation phải dựa trên authority/contract đã xác nhận, có test và evidence.
4. Mỗi milestone chỉ được coi là hoàn tất khi qua exit gate, không dựa trên cảm giác “đã code xong”.

---

## 2. Bằng chứng trạng thái hiện tại của repo

### 2.1 Contact là milestone hiện tại

Frontend đã có module Contact và luồng list/controller thực tế. Hai path đã được review trực tiếp:

- `src/modules/contacts/presentation/views/ContactListView.tsx`
- `src/modules/contacts/presentation/hooks/useContactListController.tsx`

Trong `ContactListView.tsx`, nút **Thêm** đã tồn tại và được nối với `model.onCreateContact`:

```tsx
<button
  type="button"
  onClick={model.onCreateContact}
  disabled={!model.canCreateContact}
  hidden={!model.canCreateContact}
>
  <Plus size={18} aria-hidden />
  <span>Thêm</span>
</button>
```

Do đó, task hiện tại **không phải “tạo thêm nút Thêm”**. Nút đã tồn tại. Vấn đề cần chứng minh và sửa là lý do `model.canCreateContact` đang false trong tình huống người dùng mong đợi được create.

Controller hiện sử dụng các write hooks cho Contact, bao gồm Create/Update/Delete. Điều này cho thấy module đang ở giai đoạn chuyển từ read-only/partial-write sang CRUD có kiểm soát.

Ngoài ra, `ContactListTopActionMenu` hiện được truyền trạng thái write bị khóa cứng ở một điểm qua `writesAvailable={false}`. Đây là tín hiệu cần điều tra trong C0; **không được tự động đổi thành `true`** nếu chưa chứng minh runtime capability và authorization hợp lệ.

### 2.2 Kết luận kỹ thuật từ bằng chứng

Đường điều tra C0 phải tách ít nhất hai lớp:

```text
Không thấy / không dùng được “Thêm”
                 │
       ┌─────────┴─────────┐
       ▼                   ▼
Runtime write API      Effective access
không available        không cho create
       │                   │
       ▼                   ▼
FE hook / contract     FE access state
wiring                 hoặc BE capability
```

Không được giả định trước thủ phạm là frontend hay backend. Cần tạo proof để xác định layer sai rồi mới sửa.

### 2.3 Quy tắc chống stale plan

Repo có thể thay đổi sau thời điểm lập plan. Trước mỗi Task Packet:

- ghi `baseCommit` thực tế;
- xác nhận branch mục tiêu;
- xác nhận file path còn đúng;
- xác nhận authority/contract hiện hành;
- nếu baseline khác đáng kể, task phải quay về `AUTHORITY_GAP` hoặc điều chỉnh scope trước khi edit.

---

## 3. Nguyên tắc bắt buộc từ `.unicore-ai`

Plan này tuân thủ Execution Control System v1.2.2.

### 3.1 One task, one writer

Mỗi Task Packet được admit chỉ có **một `primaryWriter`**. Không có hai AI/agent cùng sửa chồng lấn file trong cùng một task. Specialist mặc định advisory/review; chỉ được write nếu packet cho phép scope không chồng lấn.

### 3.2 Lifecycle của task

```text
PROPOSED
  → ADMITTED
  → IN_PROGRESS
  → IMPLEMENTED
  → VERIFIED
  → REVIEW_PASS
  → FROZEN
```

Failure/block states:

- `NEEDS_FIX`
- `AUTHORITY_GAP`
- `ARCHITECTURE_BLOCK`
- `PENDING_EXTERNAL_CONFORMANCE`

Builder không tự tuyên bố task `FROZEN`.

### 3.3 Trước khi edit

Mỗi task phải xác minh:

1. baseline;
2. operation admission;
3. canonical owner;
4. allowed write scope;
5. persistence assignment nếu có mutation;
6. cross-owner edges.

### 3.4 Sau khi edit

Bắt buộc ghi:

- changed files;
- exact test commands;
- kết quả test;
- migrations/public surface nếu có;
- deviations: `zero` hoặc liệt kê rõ;
- evidence để reviewer tái kiểm tra được.

### 3.5 Authority > implementation

Thứ tự ưu tiên:

1. canonical operation/owner authority;
2. approved module architecture;
3. approved domain design;
4. approved use-case design;
5. validation/persistence/API/security/reliability decisions;
6. component/file/method spec;
7. approved test design;
8. Task Packet;
9. source code là **implementation evidence**, không tự động trở thành requirement authority.

Nếu source hiện tại mâu thuẫn canonical authority, không được “copy behavior hiện tại” chỉ vì code đang chạy.

### 3.6 Không được invent

Agent không được tự bịa:

- API hoặc endpoint;
- request/response field;
- permission/capability name;
- relationship cardinality;
- duplicate rule;
- lifecycle/state transition;
- delete vs archive semantics;
- AI mutation permission;
- event/webhook contract.

Thiếu authority thì dừng ở `AUTHORITY_GAP`, ghi đúng câu hỏi cần owner/architect quyết định.

---

# 4. Milestone Map và Exit Gate

| ID | Milestone | Objective | Dependency | Exit gate chính |
|---|---|---|---|---|
| C0 | Contact Write & Access Stabilization | Chứng minh và sửa đúng nguyên nhân write bị khóa | Contact baseline | Quyền/runtime/UI/API nhất quán, permission tests pass |
| C1 | Contact Create | Tạo Contact end-to-end theo contract | C0 | Create happy/error/permission path được verify |
| C2 | Contact Read | List/detail/query states ổn định | C1 hoặc song song có kiểm soát | Loading/empty/error/read-only phân biệt đúng |
| C3 | Contact Update | Update có authorization, validation, concurrency rule theo authority | C0, C2 | Update regression + unauthorized rejection pass |
| C4 | Contact Delete/Archive | Hoàn thiện lifecycle removal đúng domain | Domain decision | Delete/archive semantics được quyết định và proof đầy đủ |
| C5 | Contact UX | Hoàn thiện action/filter/import/export/bulk/view states | C1–C4 | UX không lộ action trái capability; critical flows pass |
| C6 | Contact Relationships | Contact liên kết các entity CRM theo contract | C1–C5 | Relationship cardinality + integrity tests pass |
| O | Organization | CRUD + relationship Organization | C6 | Organization production-ready |
| CU | Customer | CRUD/lifecycle/relationships Customer | O | Customer production-ready |
| WF | Lead→Customer Workflow | Conversion transactional/idempotent/auditable | Lead + Contact + Org + Customer | Workflow integration/E2E pass |
| EV | Events/Webhook | Reliable integration event delivery | Stable workflow | Outbox/retry/idempotency/security evidence pass |
| AI | CRM-aware AI Chat | AI đọc CRM có grounding + permission | Stable data/contracts | Permission-scoped answer + audit + safety pass |
| CH | Health/Churn | Health/risk signal dựa trên dữ liệu đáng tin | Customer history + events | Model/rule evaluation + explainability accepted |
| PA | Proactive AI | Reminder/alert/suggested action an toàn | AI + events + health | Confirmation gate/audit/dedup/escalation pass |

---

# 5. C0 — Contact Write & Access Stabilization

**Status:** `CURRENT`  
**Mục tiêu:** xác định chính xác vì sao Contact write action không available/visible trong một role/context đáng lẽ được phép, sau đó sửa đúng layer mà không bypass security.

## 5.1 Scope in

- `canCreateContact` và các gate tạo ra giá trị này.
- Contact create/update/delete runtime availability.
- Effective access của module Contact.
- Backend authorization/capability contract liên quan Contact mutation.
- Wiring giữa access context và presentation model.
- `writesAvailable={false}` ở top action menu.
- Regression tests cho read-only vs writable roles/context.

## 5.2 Scope out

- Redesign toàn bộ permission system.
- Tạo thêm một button `Thêm` thứ hai.
- Hardcode `canCreateContact = true`.
- Bỏ backend authorization vì frontend đã ẩn nút.
- Refactor unrelated modules.
- Xây lại Lead.

## 5.3 Task breakdown

### CRM-C0-001 — Trace `canCreateContact` end-to-end

**Objective:** tạo một dependency trace từ UI button đến nguồn runtime/access cuối cùng.

**Actions:**

1. Pin frontend `baseCommit`.
2. Xác định nơi build `model.canCreateContact`.
3. Trace runtime availability của `useCreateContact()` đến API adapter/contract.
4. Trace `access.canPerform(...)` hoặc equivalent đến access provider/context.
5. Ghi rõ tất cả Boolean gates theo dạng expression thực tế.
6. Không sửa code trong task này nếu chưa có proof root cause; nếu task packet định nghĩa diagnostic-only thì giữ diagnostic-only.

**Evidence required:**

- file + symbol path;
- expression tạo `canCreateContact`;
- runtime state quan sát được;
- access state quan sát được;
- reproduction role/context;
- kết luận root cause với confidence.

**Exit:** một reviewer khác có thể đọc evidence và chỉ ra chính xác gate nào đang false.

---

### CRM-C0-002 — Verify backend Contact capability/authorization contract

**Objective:** xác nhận server có contract/capability đúng cho Contact Create/Update/Delete hay không.

**Actions:**

1. Tìm canonical authority/OpenAPI/operation owner trước.
2. Xác định endpoint/operation thật, không suy đoán từ frontend.
3. Xác định policy/capability/role/workspace requirements.
4. Kiểm tra server-side rejection cho actor không được phép.
5. Xác nhận access/context endpoint hoặc mechanism nào cung cấp effective permissions cho frontend, nếu authority có quy định.
6. Nếu authority thiếu hoặc conflicting → `AUTHORITY_GAP`, không invent.

**Test proof tối thiểu:**

- authorized actor mutation path;
- unauthorized actor mutation path;
- workspace/tenant isolation nếu áp dụng;
- contract status codes/body shape theo authority.

**Exit:** backend contract được chứng minh hoặc authority gap được ghi chính xác.

---

### CRM-C0-003 — Fix access/runtime propagation at the proven layer

**Objective:** sửa **một root cause đã chứng minh**, không sửa dự phòng nhiều layer cùng lúc.

**Possible layers, chỉ chọn layer có evidence:**

- frontend write hook availability;
- frontend access hydration/cache;
- backend capability provisioning/reconciliation;
- backend access serialization;
- operation registration/contract wiring.

**Constraints:**

- không `true` hardcode;
- không đổi permission semantics ngoài Contact nếu task không cho phép;
- không làm UI hiển thị action mà server vẫn reject với role được kỳ vọng;
- không nới server authorization chỉ để UI chạy.

**Exit:** failing proof trước fix → passing proof sau fix, nếu kỹ thuật cho phép.

---

### CRM-C0-004 — Unlock Contact top actions safely

**Objective:** xử lý `writesAvailable={false}` dựa trên capability thực tế.

**Actions:**

1. Chứng minh semantics của prop `writesAvailable`.
2. Map từng top action với permission + runtime capability tương ứng.
3. Thay hardcoded state chỉ sau khi mapping được xác minh.
4. Actions không available phải có behavior UX thống nhất: ẩn/disable theo design authority, không ngẫu nhiên giữa các màn hình.

**Exit:** không còn hardcoded write enablement trái authority; menu phản ánh đúng capability.

---

### CRM-C0-005 — Contact permission/write regression matrix

**Objective:** đóng C0 bằng test, không bằng screenshot đơn lẻ.

| Runtime create | `contacts.create` effective | Expected UI | Expected API |
|---:|---:|---|---|
| false | false | Không cung cấp create action | Reject/unavailable theo contract |
| false | true | Không cho mutation; trạng thái unavailable phải rõ | Không gọi endpoint không tồn tại/không available |
| true | false | Không cho create | Server reject unauthorized |
| true | true | Cho thấy/enable Create | Server accept valid request |

Bổ sung matrix cho Update/Delete nếu cùng root cause hoặc cùng capability pipeline.

**Exit gate C0:**

- role/context được phép thấy và dùng đúng Contact create action;
- role/context không được phép không thể mutation dù gọi trực tiếp API;
- runtime unavailable không bị nhầm với permission denied;
- `writesAvailable` không còn hardcoded trái contract;
- test/evidence/review pass;
- không tạo duplicate `Thêm` button.

---

# 6. C1 — Contact Create

**Mục tiêu:** hoàn thiện Create Contact từ contract → form → API → persisted result → refreshed UI.

## 6.1 Contract gate trước UI

Trước khi sửa form, Task Packet phải pin:

- operation ID/endpoint canonical;
- request schema;
- required/optional fields;
- normalization;
- server validation;
- authorization;
- workspace/tenant scope;
- duplicate semantics nếu có;
- idempotency requirement nếu có;
- response schema;
- audit/event requirement nếu có.

Nếu duplicate semantics không có authority, **không tự thêm “email must be globally unique”** hoặc rule tương tự.

## 6.2 Task breakdown

### CRM-C1-001 — Verify Create Contact contract

Deliverable: Contract Note hoặc authority evidence references dùng trực tiếp trong Task Packet.

### CRM-C1-002 — Implement/complete Create form

Bao gồm:

- field mapping đúng schema;
- required validation;
- accessibility labels;
- loading/submitting state;
- cancel behavior;
- keyboard/focus behavior theo design hiện hành;
- không thêm field không có contract.

### CRM-C1-003 — Create mutation and state synchronization

Bao gồm:

- request mapping;
- success handling;
- error mapping;
- list/detail refresh strategy theo architecture hiện hành;
- duplicate response handling nếu canonical contract có;
- no double-submit;
- telemetry/audit hook nếu authority yêu cầu.

### CRM-C1-004 — Create tests and evidence

Test classes ưu tiên:

- Unit: pure form mapping/validation invariant.
- Contract: HTTP request/response schema.
- Integration: persistence + authorization + duplicate/idempotency nếu cần.
- E2E: user có quyền → mở form → tạo → thấy Contact mới.

**Exit gate C1:**

1. authorized create pass;
2. invalid request bị reject đúng;
3. unauthorized create bị reject server-side;
4. UI không double submit;
5. result xuất hiện đúng sau mutation;
6. lỗi không làm user tưởng đã save;
7. review pass + evidence attached.

---

# 7. C2 — Contact Read

**Mục tiêu:** Contact list/detail/query có semantics rõ và không trộn lẫn application state với permission state.

## 7.1 Invariants bắt buộc

```text
No data       ≠ Read-only
API error     ≠ Read-only
Loading       ≠ Read-only
Permission    có thể => Read-only
Runtime write unavailable ≠ Read API unavailable
```

## 7.2 Scope

- list;
- detail nếu module có route/authority;
- pagination;
- search;
- sort;
- filter;
- columns;
- loading;
- empty;
- error;
- access denied/read-only;
- refresh;
- stale request/race handling nếu architecture cần.

## 7.3 Task breakdown

- `CRM-C2-001` Verify list/detail query contracts.
- `CRM-C2-002` Normalize list state model.
- `CRM-C2-003` Search/sort/filter/pagination contract alignment.
- `CRM-C2-004` Loading/empty/error/read-only visual-state separation.
- `CRM-C2-005` Read regression and pagination/sorting invariants.

**Exit gate C2:**

- không có empty state giả thành access denied;
- query params map đúng backend contract;
- pagination/sort stable theo contract;
- error có thể retry/diagnose;
- permission scope được giữ xuyên suốt list/detail.

---

# 8. C3 — Contact Update

**Mục tiêu:** update Contact có authorization, validation, concurrency semantics và UI state chính xác.

## 8.1 Authority questions cần pin

- PUT/PATCH semantics thực tế;
- editable vs immutable fields;
- partial update rules;
- concurrency/version token nếu có;
- stale-write behavior;
- audit/event behavior;
- relation edits có thuộc cùng operation không.

## 8.2 Tasks

- `CRM-C3-001` Verify update contract and editable field set.
- `CRM-C3-002` Implement/complete edit UX.
- `CRM-C3-003` Mutation + validation/error/concurrency handling.
- `CRM-C3-004` Authorized/unauthorized/stale update tests.

**Exit gate C3:**

- update đúng fields;
- immutable field không bị sửa lén từ client;
- unauthorized actor bị server reject;
- stale/concurrency behavior đúng authority;
- UI chỉ báo success sau khi mutation thực sự thành công.

---

# 9. C4 — Contact Delete / Archive

**Mục tiêu:** chốt và implement lifecycle removal đúng business authority.

## 9.1 OPEN DECISION — không được invent

**Quyết định bắt buộc:** Contact dùng:

- hard delete;
- soft delete;
- archive/deactivate;
- hoặc kết hợp theo lifecycle.

Owner/Architect phải xác nhận bằng canonical authority. AI không được tự chọn chỉ vì “CRM thường nên soft delete”.

## 9.2 Questions phải được quyết định

- Contact có historical/audit retention không?
- Contact đang được Customer/Organization/Activity tham chiếu thì sao?
- Restore có được hỗ trợ không?
- Unique constraints xử lý archived record thế nào?
- GDPR/privacy erasure khác archive thế nào?
- Event phát ra là `Deleted`, `Archived`, `Deactivated` hay khác?

## 9.3 Tasks

- `CRM-C4-001` Resolve lifecycle deletion authority.
- `CRM-C4-002` Implement backend transition/delete contract.
- `CRM-C4-003` Implement confirmation UX.
- `CRM-C4-004` Referential integrity, restore/retention tests nếu áp dụng.

**Exit gate C4:** semantics đã được owner quyết định; data integrity và audit/privacy tests pass.

---

# 10. C5 — Contact UX Completion

**Mục tiêu:** hoàn thiện các interaction của màn Contact mà không làm privilege model phân mảnh.

## 10.1 Candidate features

Chỉ implement feature đã có design/authority tương ứng:

- Search;
- Filter;
- Column chooser;
- Statistics;
- Refresh;
- List/Grid;
- Import;
- Export;
- Bulk actions;
- overflow/top action menu.

## 10.2 Capability rule

Mỗi action phải có mapping rõ:

```text
Action
  → UI permission/capability
  → runtime availability
  → server authorization
  → audit/event nếu cần
```

Không dùng một biến `writesAvailable` quá rộng nếu authority cần phân biệt create/import/export/delete/bulk permissions.

## 10.3 Tasks

- `CRM-C5-001` Action capability matrix.
- `CRM-C5-002` Search/filter/column UX alignment.
- `CRM-C5-003` Import contract + validation + partial failure strategy.
- `CRM-C5-004` Export authorization/data-scope/privacy verification.
- `CRM-C5-005` Bulk action transaction/error semantics.
- `CRM-C5-006` Design QA + accessibility + regression.

**Exit gate C5:** mọi visible action đều có capability source; không có privileged action chỉ được bảo vệ bằng UI hiding.

---

# 11. C6 — Contact Relationships

**Mục tiêu:** tạo nền dữ liệu quan hệ để Organization, Customer và conversion workflow sử dụng an toàn.

## 11.1 Candidate relations cần authority

```text
Contact ↔ Organization
Contact ↔ Customer
Contact ← Lead origin/conversion provenance
Contact ↔ Activities
```

Không tự quyết định cardinality. Các câu hỏi phải được authority hóa:

- một Contact có thể thuộc nhiều Organization hay chỉ một?
- một Customer có thể có nhiều Contact role không?
- Contact là Customer hay Contact chỉ là person/contact point của Customer?
- Lead conversion tạo Contact mới hay reuse Contact đã có?
- duplicate/matching key là gì?
- Activity owner/reference lifecycle ra sao?

## 11.2 Tasks

- `CRM-C6-001` Relationship authority/cardinality matrix.
- `CRM-C6-002` Persistence integrity + indexes/constraints.
- `CRM-C6-003` Relationship API operations.
- `CRM-C6-004` Contact relationship UI.
- `CRM-C6-005` Cross-entity authorization.
- `CRM-C6-006` Integrity/concurrency/E2E proof.

**Exit gate C6:** relationships không chỉ hiển thị được mà còn có ownership, integrity, authorization và lifecycle behavior được test.

---

# 12. O — Organization Milestone

**Mục tiêu:** đưa Organization lên cùng chuẩn production với Contact, sử dụng Contact relationship đã ổn định.

## 12.1 Scope

- Create/Read/Update/Delete-or-Archive theo domain authority;
- search/filter/pagination;
- Contact membership/association;
- Customer relation nếu domain cho phép;
- authorization/data scope;
- audit/event hooks chuẩn bị cho EV.

## 12.2 Recommended task groups

- `CRM-O-001` Baseline + gap analysis.
- `CRM-O-010` Create.
- `CRM-O-020` Read.
- `CRM-O-030` Update.
- `CRM-O-040` Lifecycle removal.
- `CRM-O-050` Contact relationships.
- `CRM-O-060` UX/access hardening.
- `CRM-O-070` Release regression.

**Exit:** Organization CRUD + relationship production-ready, không còn blocker cho Customer.

---

# 13. CU — Customer Milestone

**Mục tiêu:** xây Customer như aggregate/business entity có lifecycle rõ, không đơn thuần copy Contact CRUD.

## 13.1 Authority phải chốt

- Customer identity là person, organization, account hay aggregate nào?
- trạng thái lifecycle;
- Contact/Organization relation;
- owner/assignee/team/workspace scope;
- segmentation/tags nếu có;
- merge/duplicate behavior;
- retention/privacy;
- audit/event requirements.

## 13.2 Task groups

- `CRM-CU-001` Customer domain/contract authority gap closure.
- `CRM-CU-010` Create.
- `CRM-CU-020` Read/query.
- `CRM-CU-030` Update/lifecycle transitions.
- `CRM-CU-040` Contact/Organization relationships.
- `CRM-CU-050` access/data-scope hardening.
- `CRM-CU-060` audit/event-ready surface.
- `CRM-CU-070` E2E and release evidence.

**Exit:** Customer có lifecycle và relationships ổn định để Lead conversion có target rõ.

---

# 14. WF — Lead → Customer Workflow

**Mục tiêu:** conversion không phải chuỗi nhiều API call “best effort”, mà là workflow có invariant, retry/idempotency và audit rõ.

## 14.1 Workflow questions cần authority

- Lead đủ điều kiện convert khi nào?
- Conversion tạo mới hay reuse Contact/Organization/Customer?
- duplicate resolution strategy?
- nếu tạo Contact thành công nhưng Customer thất bại thì rollback hay resume?
- conversion có idempotency key không?
- một Lead convert nhiều lần được không?
- provenance từ Lead được lưu ở đâu?
- permissions nào cho phép convert?

## 14.2 Invariants gợi ý để authority xác nhận

Các invariant sau **chỉ là candidate**, phải được canonical hóa trước khi code:

- cùng một conversion command retry không tạo duplicate effects;
- partial failure không để dữ liệu ở trạng thái không thể recover;
- unauthorized actor không thể convert;
- conversion result truy vết được về source Lead;
- audit ghi actor, time, source, created/reused entities.

## 14.3 Task groups

- `CRM-WF-001` Conversion authority + state machine.
- `CRM-WF-010` Duplicate/matching contract.
- `CRM-WF-020` Transaction/idempotency architecture.
- `CRM-WF-030` Backend workflow implementation.
- `CRM-WF-040` Frontend conversion UX.
- `CRM-WF-050` failure/retry/recovery tests.
- `CRM-WF-060` E2E conversion evidence.

**Exit:** conversion deterministic, repeat-safe theo authority, auditable và có failure recovery proof.

---

# 15. EV — Domain Events / Webhook

**Mục tiêu:** tạo event boundary đủ tin cậy cho integration và AI/proactive layers.

## 15.1 Candidate event families

Tên event cụ thể phải theo authority, nhưng scope có thể bao gồm:

- Contact lifecycle changed;
- Organization lifecycle changed;
- Customer lifecycle changed;
- Lead converted;
- relationship changed;
- activity created/updated.

## 15.2 Reliability requirements phải chốt

- event owner;
- event schema/versioning;
- transaction boundary;
- outbox requirement;
- delivery guarantee;
- retry/backoff;
- dead-letter/replay;
- webhook signature/auth;
- idempotency/dedup;
- ordering requirement;
- data minimization/privacy;
- observability.

## 15.3 Task groups

- `CRM-EV-001` Event catalog + ownership.
- `CRM-EV-010` Outbox/transaction design.
- `CRM-EV-020` Dispatcher + retry/dedup.
- `CRM-EV-030` Webhook subscription/delivery contract.
- `CRM-EV-040` signature/security/privacy.
- `CRM-EV-050` observability/replay/support tooling.
- `CRM-EV-060` deterministic failure/concurrency tests.

**Exit:** business mutation và emitted effect không bị “DB success, event lost” theo reliability target đã duyệt.

---

# 16. AI — CRM-aware AI Chat

**Mục tiêu:** AI hỗ trợ người dùng trên dữ liệu CRM nhưng không trở thành đường bypass authorization hoặc nguồn hallucinated business facts.

## 16.1 Phase 1 AI stance: read-first

Ban đầu ưu tiên:

- query CRM data theo quyền hiện tại;
- summarize Contact/Organization/Customer/Lead history;
- answer grounded với record/evidence được phép;
- generate drafts/suggestions nhưng chưa tự mutation.

## 16.2 Safety/authorization principles

1. AI chỉ nhìn thấy data mà actor được phép truy cập.
2. Tool/API gọi bởi AI phải chịu server authorization như UI bình thường.
3. Không dùng prompt làm authorization layer.
4. Record IDs/workspace scope phải được validate server-side.
5. Câu trả lời phải phân biệt CRM fact, inference và suggestion.
6. Mutation capability nếu thêm sau này phải có explicit confirmation theo policy.
7. Audit phải cho biết action được AI đề xuất hay AI/tool thực thi.

## 16.3 Task groups

- `CRM-AI-001` AI use-case/authority boundary.
- `CRM-AI-010` permission-scoped CRM retrieval.
- `CRM-AI-020` grounding/citation-to-record strategy.
- `CRM-AI-030` chat orchestration and failure handling.
- `CRM-AI-040` prompt injection/data exfiltration controls.
- `CRM-AI-050` audit/observability.
- `CRM-AI-060` evaluation set and permission regression.

**Exit:** AI không trả dữ liệu vượt quyền, không giả CRM facts, có trace/evaluation đủ để review.

---

# 17. CH — Customer Health / Churn

**Mục tiêu:** cung cấp health/risk signal có thể giải thích và đánh giá, chỉ sau khi Customer data/event history đủ tin cậy.

## 17.1 Không bắt đầu quá sớm

Không xây churn score chỉ vì đã có AI Chat. Cần tối thiểu:

- customer identity ổn định;
- event/activity history đủ chất lượng;
- outcome/label hoặc business heuristic authority;
- time semantics đúng;
- tenant/data isolation;
- evaluation metric được owner chấp nhận.

## 17.2 Possible implementation tracks

### Rule-based trước

Phù hợp nếu data lịch sử chưa đủ cho model:

- inactivity;
- unresolved support/sales signals;
- engagement drop;
- lifecycle warnings.

Rule cụ thể phải do business authority xác nhận.

### Statistical/ML sau

Chỉ khi có:

- labeled outcomes;
- leakage-safe features;
- training/evaluation split hợp lệ;
- monitoring drift;
- model versioning;
- explainability requirement.

## 17.3 Task groups

- `CRM-CH-001` Define health/churn business target.
- `CRM-CH-010` Data quality + feature authority.
- `CRM-CH-020` Baseline/rule engine.
- `CRM-CH-030` Offline evaluation.
- `CRM-CH-040` API/UI explanation.
- `CRM-CH-050` monitoring/feedback loop.

**Exit:** score/signal có business meaning, evaluation và explanation; không chỉ có “AI-generated percentage”.

---

# 18. PA — Proactive AI / Reminder / Suggested Action

**Mục tiêu:** chuyển từ AI trả lời khi được hỏi sang AI chủ động phát hiện và đề xuất việc cần làm, có kiểm soát noise và mutation risk.

## 18.1 Candidate capabilities

- reminder;
- stale lead/customer follow-up alert;
- health-risk alert;
- suggested next action;
- suggested email/task draft;
- escalation suggestion.

## 18.2 Guardrails bắt buộc

- dedup/suppression;
- frequency limits;
- timezone-aware delivery;
- permission/data scope;
- explicit owner;
- explain “why this alert”;
- feedback/dismiss/snooze;
- audit;
- mutation confirmation nếu action làm thay đổi CRM hoặc gửi external side effect.

## 18.3 Task groups

- `CRM-PA-001` Proactive trigger authority.
- `CRM-PA-010` Rule/event trigger engine.
- `CRM-PA-020` dedup/suppression/scheduling.
- `CRM-PA-030` suggestion generation + explanation.
- `CRM-PA-040` confirmation-gated mutation tools.
- `CRM-PA-050` notification UX/preferences.
- `CRM-PA-060` evaluation: precision/noise/actionability.
- `CRM-PA-070` security/audit/reliability release gate.

**Exit:** proactive system hữu ích, không spam, không tự mutation vượt authority và mọi action quan trọng có audit trail.

---

# 19. Task Packet chuẩn cho mọi implementation task

Mỗi task implementation phải được tạo từ `.unicore-ai/templates/TASK_PACKET.yaml`.

Một packet tối thiểu phải pin các nhóm thông tin sau:

```yaml
schemaVersion: "4.2.1"
kind: UnicoreCRMBackendTaskPacket

identity:
  taskId: CRM-C0-003
  operationId: <canonical-operation-id>
  canonicalOwner: <owner>
  area: contacts
  status: PRODUCTION_CONTRACT_READY
  primaryWriter: <one-writer>
  reviewer: <reviewer>
  baseCommit: <exact-sha>
  baseline:
    canonicalDesignVersion: <version>
    openApiVersion: <version>
    openApiSha256: <sha256>

risk:
  tier: <LOW|MEDIUM|HIGH|CRITICAL>
  score: <score>
  specialists: []
  reasons: []

scope:
  inScope: []
  outOfScope: []
  allowedWritePaths: []
  forbiddenWritePaths:
    - design-authority/**

authorityEvidence:
  - claimId: AUTH-001
    classification: CANONICAL_FACT
    claim: <exact-authority-claim>
    sourceFile: <authority-source>
    locator: <exact-locator>
    sourceSha256: <sha256>

policies:
  authorization: <resolved-policy-or-null>
  idempotency: <resolved-policy-or-null>
  concurrency: <resolved-policy-or-null>
  transactionBoundary: <resolved-policy-or-null>
  audit: <resolved-policy-or-null>
  eventOutbox: <resolved-policy-or-null>
  retentionPrivacy: <resolved-policy-or-null>

verification:
  externalConformance:
    required: false

stopConditions:
  - AUTHORITY_GAP
```

**Quy tắc:** không điền placeholder bằng suy đoán. Nếu không biết canonical operation/owner/policy, task chưa đủ điều kiện `ADMITTED`.

---

# 20. Test Strategy chung

Theo Test Intelligence Policy, test phải chứng minh behavior cần thiết, không chỉ implementation shape.

## 20.1 Chọn loại test nhỏ nhất đủ chứng minh property

| Property | Test ưu tiên |
|---|---|
| Pure validation/invariant | Unit |
| DB/persistence/transaction | Integration |
| HTTP/event/schema | Contract |
| Dependency/ownership/public surface | Architecture |
| Critical user journey | E2E |
| Stable invariants qua nhiều input | Property-based |
| Validation/parser/security bounds | Bounded fuzzing |
| High-risk logic có test thực sự bắt lỗi | Selective mutation testing |

## 20.2 Areas cần tăng test intelligence

- Authorization.
- Tenant/workspace isolation.
- State transitions.
- Idempotency.
- Concurrency.
- Duplicate effects.
- Event/outbox delivery.
- Lead conversion.
- AI permission/data leakage.
- Proactive action dedup/confirmation.

## 20.3 Defect fix rule

Mỗi confirmed defect nên có proof:

```text
fails before fix
→ implementation fix
→ passes after fix
```

Nếu không khả thi, phải ghi lý do trong evidence.

---

# 21. Evidence và Review Standard

Mỗi finding/defect quan trọng phải ghi:

- defect ID;
- operation/module;
- category;
- expected behavior;
- actual behavior;
- failure scenario;
- impact;
- severity;
- confidence;
- reproduction/proof method;
- code paths;
- canonical authority source;
- recommended regression proof.

## 21.1 Evidence hierarchy

Ưu tiên từ mạnh đến yếu:

1. deterministic failing test/reproduction;
2. direct static proof against canonical requirement;
3. controlled integration/concurrency reproduction;
4. production trace/log/DB/provider evidence;
5. property/mutation evidence.

Không coi code smell hoặc “best practice chung” là bug nếu không có authority/behavior evidence.

---

# 22. Definition of Done cho một milestone

Một milestone chỉ `DONE/FROZEN` khi thỏa tất cả mục phù hợp:

### Authority

- [ ] operation/owner authority được pin;
- [ ] không còn unresolved authority gap trong scope;
- [ ] public contracts không bị invent.

### Implementation

- [ ] scope implementation hoàn tất;
- [ ] không có unrelated refactor;
- [ ] changed files nằm trong allowed scope;
- [ ] migrations/public surface được kê khai.

### Authorization & data

- [ ] authorized path pass;
- [ ] unauthorized path reject server-side;
- [ ] tenant/workspace/data scope pass nếu áp dụng;
- [ ] privacy/retention rule pass nếu áp dụng.

### Reliability

- [ ] idempotency/concurrency/transaction requirements pass nếu áp dụng;
- [ ] retry/recovery path có proof nếu operation có side effect;
- [ ] no duplicate effect theo requirement.

### UX

- [ ] loading/empty/error/denied/success states phân biệt đúng;
- [ ] actions phản ánh capability;
- [ ] accessibility/design QA pass cho scoped UI.

### Testing

- [ ] exact test commands được ghi;
- [ ] required tests pass;
- [ ] omitted test được giải thích;
- [ ] defect regression proof được thêm khi khả thi.

### Review

- [ ] reviewer xem stable diff/commit;
- [ ] deviations = zero hoặc explicit;
- [ ] `REVIEW_PASS` trước khi đóng;
- [ ] builder không tự declare `FROZEN`.

---

# 23. Open Decision Register

Các mục sau phải được đóng bằng owner/architect authority trước milestone tương ứng.

| Decision ID | Question | Must close before | Status |
|---|---|---|---|
| DEC-C0-001 | Exact source of Contact create/update/delete capability and access semantics | C0 close | OPEN until verified |
| DEC-C1-001 | Contact duplicate semantics / identity rules | C1 behavior relying on duplicates | OPEN unless authority exists |
| DEC-C4-001 | Hard delete vs soft delete/archive lifecycle | C4 implementation | OPEN |
| DEC-C6-001 | Contact↔Organization cardinality | C6 | OPEN |
| DEC-C6-002 | Contact↔Customer semantic/cardinality | C6 | OPEN |
| DEC-WF-001 | Lead conversion create-vs-reuse semantics | WF | OPEN |
| DEC-WF-002 | Conversion transaction/recovery/idempotency rules | WF | OPEN |
| DEC-EV-001 | Event delivery guarantee/outbox semantics | EV | OPEN |
| DEC-AI-001 | AI can only read or may mutate in first release | AI | Default plan = read-first; authority required for mutation |
| DEC-CH-001 | Business definition of churn/health target | CH | OPEN |
| DEC-PA-001 | Which proactive actions require confirmation | PA | OPEN |

**Rule:** `OPEN` không có nghĩa agent được tự chọn phương án thuận tiện nhất.

---

# 24. Risk Register

| Risk | Impact | Mitigation |
|---|---|---|
| Fix UI nhưng backend permission vẫn sai | Security/correctness | C0 trace + server unauthorized test |
| Hardcode capability để unblock demo | Privilege bypass / future drift | Ban hardcode; authority-based gate |
| Repo baseline thay đổi trong khi task chạy | Wrong diff / regression | Pin `baseCommit`, one-task-one-writer |
| Contact domain semantics chưa chốt | Rework | Authority gap protocol |
| Copy CRUD từ Lead sang Contact/Customer | Domain corruption | Verify each operation contract |
| Delete semantics sai | Data loss | DEC-C4-001 before implementation |
| Lead conversion partial failure | Duplicate/inconsistent data | transaction/idempotency/recovery design |
| Webhook emitted ngoài transaction | Lost/duplicate effects | outbox/reliability proof |
| AI bypass permissions | Data leak | server-side authorization on every tool call |
| AI invent CRM facts | User trust/data correctness | grounded retrieval + evidence + eval |
| Churn score thiếu data quality | Misleading decisions | data gate + baseline evaluation |
| Proactive AI spam | Product rejection | dedup/suppression/frequency/feedback |

---

# 25. Rollback / Mitigation Principles

Mỗi mutation task phải có rollback plan phù hợp:

- code rollback về stable commit;
- feature flag nếu architecture hiện có hỗ trợ;
- reversible migration khi có thể;
- data repair plan nếu migration không reversible;
- disable external delivery nếu webhook/provider lỗi;
- preserve outbox/audit để không mất trace;
- AI/proactive capability có kill switch/config gate nếu architecture cho phép.

Không dùng “rollback = revert code” cho task có irreversible data effect mà không nêu cách xử lý dữ liệu đã ghi.

---

# 26. First Execution Batch — bắt đầu ngay từ repo hiện tại

Đây là batch ưu tiên để chuyển plan thành coding work thực tế.

## Batch C0-A — Diagnostic & Proof

**Order:**

1. `CRM-C0-001` — Trace `canCreateContact`.
2. `CRM-C0-002` — Verify Contact backend capability/authorization contract.
3. Tạo defect/evidence record nếu hai nguồn không khớp.

**Output:** root cause được chứng minh, không sửa mò.

## Batch C0-B — Correct Layer Fix

4. `CRM-C0-003` — sửa đúng access/runtime/capability layer.
5. `CRM-C0-004` — xử lý `writesAvailable={false}` sau khi mapping capability rõ.

**Output:** Contact write availability không còn bị hardcode/mis-propagated.

## Batch C0-C — Closure

6. `CRM-C0-005` — permission/write regression matrix.
7. Reviewer kiểm stable diff.
8. C0 → `REVIEW_PASS` → đủ điều kiện freeze theo governance.

## Batch C1-A — Create Contact

Chỉ bắt đầu sau khi C0 không còn blocker:

9. `CRM-C1-001` — pin Create contract.
10. `CRM-C1-002` — form.
11. `CRM-C1-003` — mutation/state sync.
12. `CRM-C1-004` — contract/integration/E2E evidence.

---

# 27. Suggested Sprint View

Không khóa plan theo số ngày cứng khi chưa biết team capacity. Dùng scope-based sprint:

### Sprint / Execution Window 1

**Goal:** Contact write capability thật sự hoạt động và được bảo vệ đúng.

- C0-001
- C0-002
- C0-003
- C0-004
- C0-005

**Ship condition:** C0 Exit Gate pass.

### Sprint / Execution Window 2

**Goal:** Create + Read Contact production-ready.

- C1-001..004
- C2-001..005

### Sprint / Execution Window 3

**Goal:** Update + lifecycle removal.

- C3-001..004
- C4-001..004

**Dependency:** DEC-C4-001 phải đóng trước C4 implementation.

### Sprint / Execution Window 4

**Goal:** Contact UX + Relationships; đóng Contact milestone.

- C5-001..006
- C6-001..006

Sau đó mới mở Organization milestone như scope phát triển chính.

---

# 28. Progress Tracking Format

Cập nhật file này hoặc project tracker sau mỗi task merge bằng format:

```text
Task: CRM-C0-003
Status: VERIFIED
Base commit: <sha>
Implementation commit: <sha>
Primary writer: <agent/person>
Reviewer: <agent/person>
Changed files:
  - ...
Tests:
  - <exact command> → PASS
Evidence:
  - <link/path>
Authority:
  - <source + locator>
Deviation:
  - zero
Open follow-up:
  - ...
```

Milestone progress dùng trạng thái:

- `NOT_STARTED`
- `IN_PROGRESS`
- `BLOCKED_AUTHORITY`
- `BLOCKED_ARCHITECTURE`
- `VERIFYING`
- `REVIEW_PASS`
- `FROZEN`

---

# 29. Current Progress Snapshot

| Milestone | Status | Note |
|---|---|---|
| Lead baseline | BASELINE / NOT RESTARTED | Chỉ quay lại khi conversion delta yêu cầu |
| C0 Contact Write & Access | **IN_PROGRESS / CURRENT** | Root cause của write visibility/capability cần proof |
| C1 Contact Create | QUEUED | Sau C0 |
| C2 Contact Read | QUEUED | Có baseline nhưng cần production-state audit |
| C3 Contact Update | QUEUED | Sau access stabilization |
| C4 Contact Delete/Archive | BLOCKED_DECISION | Phải chốt lifecycle semantics |
| C5 Contact UX | QUEUED | Capability matrix trước privileged actions |
| C6 Contact Relationships | QUEUED | Cardinality/ownership authority required |
| Organization | NOT_STARTED in this plan | Sau Contact exit gate |
| Customer | NOT_STARTED | Sau Organization |
| Lead→Customer WF | NOT_STARTED | Sau entity foundations |
| Events/Webhook | NOT_STARTED | Sau workflow/contracts ổn định |
| AI Chat | NOT_STARTED | Read-first after stable CRM data surface |
| Health/Churn | NOT_STARTED | Sau data/event history đủ tin cậy |
| Proactive AI | NOT_STARTED | Sau AI + events + health |

---

# 30. Success Criteria toàn roadmap

Roadmap đạt mục tiêu khi:

1. Contact, Organization, Customer có contract/authorization/lifecycle rõ và được test.
2. Lead conversion tạo/reuse entity theo workflow deterministic và recoverable.
3. Domain events/webhooks có reliability + security proof.
4. AI Chat truy xuất CRM theo đúng quyền và grounded vào dữ liệu thật.
5. Health/churn signal có business definition và evaluation, không chỉ là output AI không kiểm chứng.
6. Proactive AI đưa ra reminder/suggestion có explainability, dedup và confirmation gate cho side effects.
7. Mỗi milestone có evidence trace từ authority → implementation → test → review.

---

# 31. Plan Maintenance Rule

File này là **execution roadmap**, không phải canonical business authority.

Cập nhật plan khi:

- một canonical decision được đóng;
- repo architecture thay đổi;
- milestone exit gate thay đổi do authority;
- một blocker mới được chứng minh;
- một task merge làm thay đổi current position.

Không sửa plan để hợp thức hóa implementation sai. Nếu implementation và authority xung đột, ưu tiên authority và mở task correction/architecture decision.

---

## Appendix A — C0 Review Checklist

- [ ] Pin frontend and backend `baseCommit`.
- [ ] Confirm current branch.
- [ ] Locate exact `canCreateContact` composition.
- [ ] Capture runtime `useCreateContact().available` or equivalent.
- [ ] Capture effective access for Contact create.
- [ ] Locate canonical Contact create operation.
- [ ] Confirm server authorization policy.
- [ ] Confirm expected role/workspace used in reproduction.
- [ ] Reproduce Add-button hidden/disabled state deterministically.
- [ ] Identify one proven root cause.
- [ ] Create narrow Task Packet with one writer.
- [ ] Implement only allowed paths.
- [ ] Run unauthorized API test.
- [ ] Run authorized create availability test.
- [ ] Verify no duplicate Add button.
- [ ] Verify top action capability state.
- [ ] Attach exact test commands/results.
- [ ] Reviewer reviews stable diff.

---

## Appendix B — Contact Critical Journey Checklist

### Create

- [ ] User with permission sees Create.
- [ ] User without permission cannot create via UI.
- [ ] User without permission cannot create via direct API.
- [ ] Required fields validated.
- [ ] Server validation displayed correctly.
- [ ] Submit cannot unintentionally duplicate.
- [ ] Success shows persisted record.

### Read

- [ ] Initial loading.
- [ ] Empty list.
- [ ] API failure.
- [ ] Search no result.
- [ ] Pagination boundaries.
- [ ] Sorting/filtering stable.
- [ ] Read-only state distinct from errors.

### Update

- [ ] Authorized edit.
- [ ] Unauthorized rejection.
- [ ] Validation failure.
- [ ] Concurrency/stale write behavior if applicable.
- [ ] Updated record reflected in list/detail.

### Delete/Archive

- [ ] Confirmation.
- [ ] Authorized lifecycle action.
- [ ] Unauthorized rejection.
- [ ] Relationship integrity.
- [ ] Restore/retention/privacy behavior if applicable.

---

## Appendix C — “Do Not Do” List

- Không quay roadmap về Phase 0/Lead khi current milestone là Contact.
- Không tạo thêm nút `Thêm` vì nút hiện tại đã có.
- Không đổi `hidden={...}` thành luôn visible để che lỗi permission/runtime.
- Không đổi `writesAvailable={false}` thành `true` mà chưa có capability proof.
- Không coi frontend hiding là authorization.
- Không đoán endpoint/field/permission/cardinality.
- Không copy Lead CRUD semantics sang Contact/Customer nếu chưa xác nhận authority.
- Không chọn hard-delete/soft-delete theo “best practice” khi domain chưa quyết định.
- Không bắt đầu AI mutation trước khi permission/tool/audit/confirmation boundary rõ.
- Không đánh dấu merge-ready/FROZEN khi thiếu test evidence.

---

**END OF PLAN**
