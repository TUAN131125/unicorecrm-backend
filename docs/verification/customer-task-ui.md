# Connected Customer → Task UI — kết quả 2026-10-21

**UI-only journey PASS: 12/12 bước.** Customer Create Task đã được nối vào connected route bằng modal/command hiện có. Không có endpoint, migration, Task form hoặc adapter thứ hai. Chưa stage, commit hay tạo checkpoint ACCEPTED. Production release vẫn chưa được nghiệm thu vì còn quality gate baseline FAIL và các giới hạn bên dưới.

Backend local và remote HEAD: `7b734f98a7173af2e24e094392826a20b6e11359`. Frontend local và remote HEAD: `99db1773c03ce4a4b820db7e793da6eca872f862`; đối chiếu bằng `git ls-remote origin HEAD`. Working tree có sẵn nhiều thay đổi P0; đã lưu status/diff trước sửa, giữ nguyên công việc cũ. [Task-only diffs, source hashes và evidence](evidence/customer-task-ui-20261021/changes.json) xác định chính xác working tree được kiểm thử.

## Findings

| ID | Severity | Root cause / vị trí | Impact | Fix | Status |
|---|---|---|---|---|---|
| CUSTOMER-TASK-UI | P0 acceptance blocker | `customers/detail-route.tsx` chọn CustomerConnectedWorkspace. Customer360Page có TaskCreateModal nhưng chỉ dùng ở nhánh khác. CustomerRelationshipPanel connected chỉ nối Meeting. | Không thể tạo Task thường từ Customer bằng UI. | Nối TaskCreateModal public vào Work panel, truyền recordRef Customer và CUSTOMER_360 sourceRef, không truyền relationshipRef subject. | PASS trên UI/HTTP/SQL |
| TASK-STATUS-UI | P1, chặn Complete Task acceptance | LegacyUiTranslationBridge ghi nhớ text node “Đang mở”; sau React cập nhật, bridge khôi phục literal cũ. SQL và GET đã COMPLETED nhưng màn hình vẫn OPEN. | Hiển thị sai kết quả xử lý ngay sau complete. | Vùng nội dung TaskDetail vốn đã song ngữ dùng cơ chế `data-i18n-skip` hiện có. Không sửa global bridge hay state machine. | Red UI → Green UI |
| TASK-FORM-VERIFIER | Verification debt có trước | Gate task-create-form-contracts chỉ đọc TaskCreateModal.tsx, đòi createTaskCommand nằm trong file này, trong khi command đã thuộc useTaskCreateController.ts. | Gate đỏ dù chuẩn form/controller được tái sử dụng. | Giữ gate/expected nguyên vẹn; ghi lỗi ownership/inventory thay vì đưa command về component để làm xanh test. | FAIL tồn tại trước task |

Các Backend CreateTask/ListTasks/GetTask/endpoints và CustomerTaskReferenceAuthority đã được đọc. CreateTask hiện hỗ trợ một wire shape cho Customer: `recordRef={moduleKey:'customers',recordId:...}`, không kèm relationshipRef; Customer owner authority kiểm Workspace, read/scope và trạng thái NEW/ACTIVE/INACTIVE. Không cần sửa Backend nghiệp vụ.

## Files changed

1. `frontend/src/modules/customers/presentation/detail/CustomerRelationshipPanel.tsx`: nút Tạo công việc/Create task, quyền tasks.create + Customer canRead/view + lifecycle; modal chuẩn, thông báo thành công và refresh query Work. Modal tự giữ draft/error, stable intent và uncertain retry. Assignee do controller chuẩn cung cấp và Backend xác minh membership active.
2. `frontend/src/modules/customers/presentation/detail/CustomerConnectedWorkspace.tsx`: chuyển identity/code từ projection được phép vào label modal, key panel theo Customer để retire draft/callback cũ khi đổi target. Route hiện có đã key theo Customer/session/Workspace authority.
3. `frontend/src/modules/tasks/presentation/pages/TaskDetailPage.tsx`: bỏ legacy DOM translation cho vùng đã tự chọn nhãn theo locale, tránh trạng thái hoàn thành bị ghi đè.
4. `frontend/src/guidance/content/crm/extendedScreens.ts`: guidance song ngữ và version cho Customer Create Task, Task completion.
5. `frontend/tests/e2e/crm-ui-real.spec.ts`: tạo Task, complete, refresh, logout/login qua UI; lưu sanitized Task request/response và status GET. Dùng date-picker thật thay vì fill một button. Không gọi API client để thay thao tác UI.
6. `frontend/tests/contract/customer-task-create-session.test.mts`: test double submit, unknown acknowledgement retry cùng payload/key, draft/error, callback, đổi Customer và Workspace. Đây là test in-process protocol, không phải connected evidence.
7. `frontend/tests/contract/customer-relationship-panel.test.mts`: bổ sung Customer label; yêu cầu nút mới bị disabled khi thiếu record authority. Assertion cũ “không tồn tại nút tạo Task” được thay bằng assertion quyền của tính năng mới; không bỏ negative check.
8. `backend/scripts/verify-activity-security.ps1`: thêm CustomerTaskReview, version header cho complete, SQL kiểm chứng UI Task và restart/reauth. Không đổi SQL Activity authorization path, AccessControl, TEAM hay schema.

Tên path frontend ở danh sách trên tính từ repository `frontend/unicorecrm-web`; manifest/diff dùng path repository chuẩn. Patches `*-task-only.patch` là delta so với working tree trước task, không gộp lại P0 cũ. Thư mục `before` và `beforeSha256` hỗ trợ review/rollback độc lập.

## Verification thực thi

Môi trường: Frontend connected, Playwright Chromium, ASP.NET Core ApiHost, .NET SDK 10.0.401, SQL Server localhost 17.0.1000.7. Mỗi invocation có DB GUID riêng; không dùng InMemory SQL hoặc database người dùng. Không xoá DB test sau chạy.

| Test | Environment / run | Expected | Actual | Status |
|---|---|---|---|---|
| Full UI-only journey | DB `UnicoreCRM_ActivityTest_f812bf23acdc4e628a6171db82bc67f6`, 10:50:30–10:51:47 UTC | Hoàn tất mọi bước qua control UI | Playwright 1/1 test; 12/12 bước; 11/11 HTTP/SQL/restart assertions | PASS |
| Activity Security, replay, FLS, OWN/WORKSPACE, TEAM fail-closed, count/search/paging, both race barriers, ACT-PERF và Customer Task API cases | DB `…81619fa3ec4f4acdaf98c201ddc8ef68`, 10:44:19–10:45:41 UTC | Không regression; không N+1 | 553 assertions, 0 FAIL; 107 benchmark requests, mỗi request đúng 2 SQL Activity | PASS |
| Customer Task + SALES OWN thật + denied user; canonical Contact/Organization Customer; browser projection/history; affiliation; reauth/restart | DB `…a6b7b64871eb4868b7d8068b9e5639d7`, 10:48:13–10:49:21 UTC | Quyền/quan hệ/persistence đúng | 108 assertions, 0 FAIL | PASS |
| Task create session protocol | JSDOM, canonical modal + command port | Double submit một request; retry giữ key; stale draft không submit | PASS, gồm Customer/Workspace retirement | PASS, in-process |
| Customer relationship panel regression | JSDOM | Scope/FLS, paging, error, late response, disabled create khi thiếu quyền | PASS | PASS, in-process |
| Broader Task/commercial form integration | In-process | Tất cả workflow trong file | FAIL Quote A assertion dòng 214; snapshot trước task cũng FAIL đúng dòng này. Phần Task chưa phải toàn bộ file PASS. | FAIL baseline/pre-task |

Customer Task HTTP negatives đã chạy: invalid assignee 422; relationshipRef subject kèm Customer 422; missing/foreign/archived Customer 404; receipt replay sau archive hoặc mất Customer OWN 404; không có tasks.create 403; description Hidden không được ghi và không có trong GET; Task ngoài assignee OWN get 404/list 0; complete 200 và GET COMPLETED. Tài khoản SALES OWN riêng đăng nhập thật, create/get/list Task của Customer mình được phép; mất owner thì create/replay 404. User không có capability create/get bị 403.

Benchmark trọng yếu `/activities?limit=1`, distinct targets (total pipeline commands / elapsed ms):

| Targets | WORKSPACE | SALES OWN | SQL Activity/request |
|---|---|---|---:|
| 100 | 32 / 352,75 | 30 / 289,51 | 2 |
| 1.000 | 32 / 354,51 | 36 / 321,04 | 2 |
| 5.000 | 37 / 637,77 | 32 / 319,41 | 2 |

Cũng chạy same-target 100/1.000/5.000, limit 1/50/next, record-scoped/global/search/mixed owner. Đây là regression run trên máy test, có verification đồng thời; không phải benchmark production SLA/p95/throughput. Plans, parameters, memory observations và mọi request nằm trong `security-performance`.

## UI-only từng bước và bằng chứng dữ liệu

| Step | Actual | Status |
|---|---|---|
| Login | Đăng nhập bằng form thật | PASS |
| Workspace | Workspace test được chọn | PASS |
| Create Lead | Workspace có 0 Lead; Source rỗng, POST /leads 201 | PASS |
| Create Contact | Form Contact, POST /contacts 201 | PASS |
| Create/Link Organization | Form Organization và affiliation UI | PASS |
| Conversion | UI submit Contact subject, conversion thật | PASS |
| Customer | Mở connected Customer authoritative | PASS |
| Create Task | Modal chuẩn; POST /tasks 201; tự gắn Customer, không có relationshipRef; xuất hiện trong Work sau reload | PASS |
| Complete Task | Mở Task từ Work, nhập outcome, click xác nhận; POST /tasks/{id}/complete 200; UI hiện hoàn thành | PASS |
| Activity/History | Quick Note Contact qua UI, POST /activities 201; History hiện Activity | PASS |
| Refresh | History giữ Activity sau browser refresh | PASS |
| Relogin | Logout menu thật → login thật → Work vẫn hiện Task COMPLETED | PASS |

Task: `task_800b700b25b34aeea5f9382885cec4bf`. Customer: `customer_5ef537a105ad429db0acb2eeac0acd08`. Marker: `ui-crm-1792579864592`.

`browser-ui/ui-task-mutations.json` cho thấy create COMMITTED/version 0/OPEN; complete COMMITTED/version 1/COMPLETED. Request recordRef/sourceRef cùng Customer, priority HIGH, assignee member hợp lệ; không có Contact/Organization relationshipRef. Method/path/status/correlation trong ui-http.json, metadata response đã bỏ nội dung nhạy cảm.

`verify-ui-task.sql` chạy lại read-only trên DB UI và `sql-ui-task-result.txt` xác nhận: đúng Workspace; RecordModuleKey customers; đúng RecordId; RelationshipType NULL; CUSTOMER_360 source; Status=1; Priority=2; CompletedAt có giá trị; Version=1; unique title/reference count=1; assignee active membership count=1. Lead CustomerRef đúng, Source rỗng; canonical Contact Customer count=1; UI Activity count=1.

Sau restart ApiHost, harness đăng nhập lại bằng auth thật rồi GET/list Task thành công với COMPLETED. Đây là API verification sau restart; **browser relogin trước restart** được chạy riêng trong UI-only test, không gán thành browser restart test.

`complete-before` lưu ảnh/status GET trước sửa translation: GET COMPLETED/version 1 nhưng ảnh UI vẫn Đang mở. Ảnh sau sửa `browser-ui/customer-task-after-relogin.png` đã kiểm tra trực quan. UI vẫn cần một lần dùng nút “Bỏ thay đổi” trên guard báo **0 khu vực chưa lưu**, ghi ui-observations.json; giữ nhận định UX debt này, không tự inject state hay bỏ draft có dữ liệu. Task creation và Meeting creation là hai thao tác riêng; journey không dùng Meeting thay Task.

## Quality gate status và delta

| Gate | Result | Classification |
|---|---|---|
| Frontend build + bundle budget | PASS; build 31,48s, largest chunk 486,99 KiB < 500 | Đã chạy độc lập vì full verify dừng sớm |
| Lint, typecheck, strict regions, type safety budget | PASS; 109 strict files; 180 any / 15 as-any / 56 non-null | Không tăng budget |
| Presentation responsibility, guidance, mutation command authority, frontend/backend separation, API contract | PASS; API 328 operations / 300 ready / 28 blocked | Không đổi public HTTP contract |
| Full npm verify | FAIL group 10; 9/327 groups qua, các nhóm sau NOT_RUN trong full pipeline | Hai cycle ContactFormModal↔contactDraftRecovery và TaskCreateModal↔useTaskCreateController có trước |
| Backend architecture | 33 PASS / 4 FAIL | Cùng bốn failure messages với lần review trước; added=[]/removed=[] trong architecture-delta |
| Quality pipeline inventory | FAIL `347 !== 345` | Test inventory baseline, không đổi expected |
| Repository inventory | FAIL | Snapshot trước task chạy lại cũng FAIL; không thêm route/capability/Workspace flag. Không regenerate toàn inventory ngoài phạm vi |
| Task create form source gate | FAIL dòng 23 | Snapshot trước task FAIL cùng assertion; gate đọc component mà bỏ controller composition. Đây là verification debt liên quan Task, không được báo PASS |
| Task/commercial integration tổng hợp | FAIL Quote A dòng 214 | Snapshot trước task cũng FAIL cùng assertion; không sửa Quote, không skip test |

Không phát hiện regression mới trong các gate đã đối chiếu. Điều này không có nghĩa tất cả quality gates xanh. Các contract tests in-process mới/chạy trực tiếp được ghi riêng; không giả rằng full verify đã chạy chúng. Không có attestation độc lập/gatekeeper hay production acceptance.

## Checkpoint đề xuất và giới hạn

Đề xuất một **checkpoint candidate “Connected CRM P0 — Customer Task UI”** gồm task-only diffs và bộ evidence hiện tại, tách khỏi phát triển TEAM/Calendar/History. Chưa tạo commit hoặc nhãn ACCEPTED. Trước khi coi đây là checkpoint được duyệt cần review diff, xác nhận cách xử lý/chấp nhận baseline gate debt; trước production release phải có gate ownership/inventory đúng, kiểm thử tải và kiểm tra release đầy đủ.

Các giới hạn còn lại:

- TEAM vẫn fail-closed, chưa có positive TEAM acceptance. Không triển khai TeamManagement.
- Task Customer reference authority kiểm quyền/lifecycle tại validation time; không thêm commit-time lifecycle lock cho CreateTask. Không suy rộng serializable Activity guard thành bảo đảm tương tự cho Task. Policy revocation giữa request, concurrency/pagination và tải production giữ các giới hạn đã nêu trong báo cáo Activity performance.
- Local Task detail đã tránh translation bridge; bug bridge ghi đè dynamic literal ở những module khác chưa được sửa trong phạm vi này.
- Zero-area unsaved guard còn UX debt. Không phát triển unified timeline: Activity/History nghiệm thu ở Contact; Task completion không tự tạo một manual Activity giả.
- Một lần khởi chạy development harness bằng Windows PowerShell 5 không qua readiness vì harness cần PowerShell 7 (`SkipHttpErrorCheck`); lần đó không có test PASS và được loại khỏi các run trên. Lệnh tái lập bên dưới dùng `pwsh`.

## Reproduce

Từ backend, PowerShell 7, SQL Server Windows integrated auth, Node dependencies và Playwright Chromium; chạy browser jobs tuần tự vì dùng chung Vite port. Mỗi invocation tạo DB test GUID mới và giữ lại, không xoá dữ liệu cũ:

```powershell
pwsh -NoProfile -File scripts/verify-activity-security.ps1 -UiReview -Port 5392
pwsh -NoProfile -File scripts/verify-activity-security.ps1 -CustomerTaskReview -Browser -ProjectionReview -Port 5393
pwsh -NoProfile -File scripts/verify-activity-security.ps1 -CustomerTaskReview -ScaleReview -ProjectionReview -RaceReview -CommitRaceReview -Port 5393
```

Từ frontend:

```powershell
node --import tsx --loader ./scripts/quality/core/typescript-path-loader.mjs scripts/quality/run-node-entry.mjs tests/contract/customer-task-create-session.test.mts
node --import tsx --loader ./scripts/quality/core/typescript-path-loader.mjs scripts/quality/run-node-entry.mjs tests/contract/customer-relationship-panel.test.mts
npm run typecheck
npm run build
npm run verify
```

Evidence không chứa token/password/keys/connection string runtime hoặc browser trace thô. Raw bootstrap/host logs giữ trong ignored artifacts, không đưa vào evidence bàn giao. `changes.json` và `artifact-hashes.json` cho phép đối chiếu source/artifact; `prior-source-delta.json` chứng minh SQL authorization implementation của P0 trước không bị chỉnh trong đợt này.
