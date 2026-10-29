# Activity performance và xác minh Lead UI — 2026-10-21

Trạng thái: **ACT-PERF đã sửa và kiểm chứng; nghiệm thu Connected CRM còn BLOCKED**. Không tạo checkpoint ACCEPTED, commit hoặc push. Không tuyên bố toàn bộ sản phẩm hoàn thiện hay đạt hiệu năng production.

Backend HEAD `7b734f98a7173af2e24e094392826a20b6e11359`; frontend HEAD `99db1773c03ce4a4b820db7e793da6eca872f862`. Các kết quả chạy trên working tree có thay đổi chưa commit; patch và SHA-256 nằm trong [evidence](evidence/activity-performance-20261021/changes.json). Patch là **cumulative so với HEAD**, gồm các bản sửa P0 trước đợt này. Thuộc tính `changedSincePriorReview` phân biệt file thay đổi so với báo cáo trước. Công việc và artifact có sẵn được giữ nguyên.

## Findings, root cause, changes

| Finding | Root cause | Files changed | Tests | Actual | Status |
|---|---|---|---|---|---|
| ACT-PERF, P0 | ListActivities materialize mọi reference trong Workspace rồi gọi kiểm quyền và owner authority từng target, trước COUNT/PAGING. SQL tăng theo distinct targets. | ActivityRecordAccess, ActivityReferenceAuthority, ListActivities/Handler, TasksPersistence, EfTasksPersistence, ActivityReadSql; 4 owner fact providers và persistence; TasksDbContext + migration | SQL Server, 107 request, 100/1.000/5.000 cùng target và distinct targets, OWN/WORKSPACE | Mỗi request có đúng 2 SQL dữ liệu Activity, COUNT và page ở SQL; không có ID-list theo kích thước dataset | PASS |
| ACT-COMMIT-RACE, P0 | Kiểm chứng owner/lifecycle bằng DbContext khác không giữ hiệu lực tới commit Activity | LogActivity/Handler, GuardActivityReferencesAsync, ActivityReadSql | Chặn INSERT trong test; đồng thời đổi owner/archive | Trước sửa: UPDATE hoàn tất trước khi INSERT được thả. Sau sửa: UPDATE chờ Activity transaction commit | PASS cho interleaving đã đo |
| ACT-REPLAY-WAIT, P0 | Request có thể chờ idempotency receipt trong khi quyền thay đổi | LogActivity tái kiểm tasks.update và reference sau receipt wait, trước replay | Hai barrier: receipt mới và receipt đã có; owner bị thu hồi trong lúc chờ | Cả hai trả 404, không trả payload cache | PASS |
| UI-SOURCE, phát hiện cũ không đúng | Trước đây coi danh sách Source trống là prerequisite thất bại mà chưa submit; Source mặc định không bắt buộc | UI-only scenario và guidance LeadList | Workspace mới có 0 Lead, Source control rỗng, submit thật | POST /leads 201; SQL Source rỗng; dữ liệu tồn tại | **Rút lại kết luận BLOCKED cũ** |
| UI-LEAD-SAVE | `globalSave || Boolean(props.onSave)` ép create bình thường vào nhánh deferClose, nên API đã lưu nhưng modal vẫn mở | useLeadFormController: chỉ giữ mặc định này cho edit; explicit global-save vẫn giữ | Browser Red trước sửa; browser SQL thật sau sửa; full-page lifecycle regression | Modal đóng sau create 201, không phải lỗi Source validation | PASS |
| UI-CONVERSION-NAV | Guard draft vẫn active sau conversion đã commit trong lúc chờ refresh cache | LeadCustomerConversionPage, guidance LeadQualify | UI convert Contact, click Customer, refresh | Mở Customer authoritative thành công; không thay conversion protocol | PASS |
| UI-TASK | Connected Customer workspace chưa có control Create Task; chỉ có Task-based Meeting | Không mở rộng Meeting/Task UI trong đợt này | UI-only journey | Task BLOCKED; runner exit 1; không thay bước này bằng API hay Meeting | BLOCKED |

Các vị trí cụ thể và toàn bộ file: `evidence/activity-performance-20261021/{backend.patch,frontend.patch,changes.json}`. Hai verifier và `scripts/verify-activity-security.ps1` được cập nhật để chạy SQL/race/performance; Playwright UI-only không ghi dữ liệu qua API client. Guidance được đồng bộ hành vi Source tùy chọn và điều hướng Customer sau commit. Public HTTP DTO và generated client không thay đổi.

## Đường đọc và caller

Caller global xác nhận được: `useTasksAuthoritative` gọi `getActivityCollectionResource()` không filter; caller hook duy nhất là TaskListPage. Trang này đọc `snapshot.tasks`, chưa có global Activity feed hiển thị. Task detail và History của Lead/Contact/Organization/Customer dùng record filter. Giữ caller và API global tương thích trong đợt này; có thể bỏ eager fetch thừa trong một thay đổi frontend riêng. Không âm thầm bắt API global trả lỗi.

AccessControl được gọi một lần cho mỗi owner resource, tối đa bốn owner, không gọi theo distinct target trong đường list. Chỉ scope OWN/WORKSPACE được dịch sang predicate từ quyết định AccessControl. SQL facts tĩnh do persistence của từng owner cung cấp; Tasks không mở CRM DbContext. Workspace, owner và cờ policy dùng SQL parameters, tối đa 21 tham số cho bốn owners; không có IN-list theo số record, không cache quyền xuyên request/user/Workspace.

SQL áp dụng proof primary record, quan hệ do owner chứng minh, quyền độc lập của related record, provenance và field security **trước COUNT, search và paging**. Relationship-only Contact/Organization vẫn được hỗ trợ. TEAM/Custom/Denied không tạo scope được đọc. Với Activity free text có thể trích bất kỳ trường owner nào, nếu một field owner không đọc được thì Activity bị giữ lại toàn bộ, gồm count/search; đây là fail-closed của P0, không phải redaction tự suy đoán. SYSTEM và source evidence không đáng tin từ HTTP tiếp tục bị từ chối.

Record History có index mới `IX_Activities_RecordHistory(WorkspaceId, RecordModuleKey, RecordId, OccurredAt, ActivityId)`. Migration chính thức `20261021075605_IndexActivityRecordHistory` chỉ thêm index, không đổi subject unique hay schema nghiệp vụ. Các DB test mới áp dụng migration qua verifier `--migrate`; không chạm DB sản xuất.

## Before/after SQL và memory

Môi trường: SQL Server localhost 17.0.1000.7, ASP.NET Core thật, .NET SDK 10.0.401, mỗi run có DB GUID riêng. Timestamps UTC và DB chính xác trong từng `run.json`.

Trước sửa đo lại: `fbc04bbfa0374814a35d22b73f0796a6`. Sau sửa: `b66784440f094da8ad3e51ed9ead21bb`, 10:18:48–10:19:49 UTC. Với `/activities?limit=1`, WORKSPACE:

| Distinct targets | Trước: tổng SQL commands | Trước ms | Sau: tổng SQL commands | Sau ms | Sau: SQL Activity |
|---|---:|---:|---:|---:|---:|
| 100 | 907 | 1.178,81 | 31 | 262,73 | 2 |
| 1.000 | 8.156 | 7.157,89 | 31 | 280,01 | 2 |
| 5.000 | Chưa đo trước | Chưa đo trước | 33 | 526,06 | 2 |

Không thay số lịch sử 8.180/10,11s trong báo cáo cũ bằng số mới; đây là lần đo lại độc lập. Dataset trước/sau có chênh một Activity fixture do test race mới, không phải benchmark SLA được kiểm soát tuyệt đối.

Sau sửa, unfiltered limit=1:

| Shape | Size | WORKSPACE ms | SALES OWN ms |
|---|---:|---:|---:|
| Cùng target | 100 | 53,45 | 47,74 |
| Cùng target | 1.000 | 293,47 | 214,55 |
| Cùng target | 5.000 | 356,46 | 240,75 |
| Distinct targets | 100 | 262,73 | 265,13 |
| Distinct targets | 1.000 | 280,01 | 267,22 |
| Distinct targets | 5.000 | 526,06 | 274,06 |

107 request gồm global, record-scoped, search, hidden search, limit 1/50 và next page, hai tài khoản thực với role OWN và WORKSPACE. Distinct dataset xen kẽ owner được phép/không được phép; exact item/count assertions trong results.json. Khi chạy distinct, 5.000 same-target rows vẫn tồn tại; ví dụ distinct 5.000 có total WORKSPACE 10.009 và OWN 7.501. Tổng SQL toàn pipeline 30–37/request gồm auth/access/audit/background; **2 câu SQL Activity** là đo riêng bằng query tag, không giả định tổng pipeline bằng 2.

ApiHost private bytes quan sát 121.221.120 đến tối đa 133.181.440; mức tăng lớn nhất giữa trước/sau một request 2.957.312 bytes. Có WorkingSet từng request trong performance-matrix.json. Đây là process memory observation, **không phải managed allocation profiling**, không có p95, stress/throughput hay chứng nhận production. COUNT vẫn cần xử lý dataset được phép và độ trễ còn tăng theo dữ liệu; không còn chuỗi truy vấn theo target.

Bốn Query Store plans và SQL nằm trong performance-security-after; plans 2/3/4 có index RecordHistory, plan 1 global không dùng index này. Không tuyên bố tất cả truy vấn đều index seek. Có activity-indexes.json để đối chiếu schema thực.

## Verification

| Test | Environment | Expected | Actual | Result |
|---|---|---|---|---|
| Negative security, OWN/WORKSPACE, TEAM denied, FLS, malformed/missing/cross-workspace refs, search/count/page, replay, races và scale | ApiHost HTTP + SQL Server GUID DB b667… | Không lộ/ghi trái phép; không duplicate; bounded SQL count | 531 assertions PASS, 0 FAIL | PASS |
| Conversion Contact/Organization canonical subject, retry unique, Task/Meeting API, browser history/refresh/OWN, reauth/restart, ended affiliation | ApiHost + SQL + Playwright, DB 58c… | Projection đúng, persisted và quan hệ lịch sử giữ nguyên | 83 assertions PASS, 0 FAIL | PASS; API workflow + browser checks, không phải full UI E2E |
| SQL follow-up verifier | SQL Server DB ContactFollowUpVerifier_46e9… | Query/projection/cursor đúng | 30 checks PASS | PASS |
| Tasks handover verifier | In-process memory participant | Regression protocol | 48 checks PASS | PASS trong phạm vi này; không chứng minh SQL concurrency |
| Lead/Contact History, Contact overview | JSDOM/contract | Regression projection/state | 21/25 checks và overview PASS | PASS; không thay SQL evidence |
| UI-only từ Workspace không có Lead | Browser + ApiHost + SQL, DB 793… | Tất cả bước UI | 9 bước PASS, Task BLOCKED, process exit 1 | BLOCKED toàn journey |
| Source rỗng | Cùng UI run | Lead được tạo nếu Source không required | HTTP 201; SQL Source rỗng, Lead/Customer/Affiliation/Activity có thật | PASS |

UI run 10:17:19–10:18:09 UTC: Login → Workspace → Create Lead → Create Contact → Create/Link Organization → Conversion → Customer đều qua control UI. Manual Activity và History được kiểm tra tiếp độc lập sau khi ghi Task BLOCKED. Test có một lần bấm UI “Bỏ thay đổi” trên guard báo **0 khu vực chưa lưu**, ghi trong ui-observations.json; đây là UX debt của guard, không inject state hoặc bỏ draft có dữ liệu. Không fake Source WEB, không fixture thay Backend. Source request correlation `d7776c54-8ed6-41c8-bf5f-267f28e5aed0`. Xem lead-create-evidence.json, ui-http.json, ui-steps.json và ảnh. SQL read-only chạy lại bằng verify-ui.sql cho kết quả subject Customer duy nhất=1, affiliation=1, Activity=1, CustomerRef đúng và Source rỗng.

## Quality gates và delta

| Gate | Current | So với baseline/trước task |
|---|---|---|
| Backend architecture suite | 33 PASS, 4 FAIL | Bốn failure message có added=[]/removed=[] so với baseline; xem activity-final-architecture-delta.json. Gồm owner mapping, durable metadata, layer boundaries, endpoint admission |
| Frontend full verify | FAIL ở group 10, 9/327 nhóm đã qua | Hai cycle ContactFormModal↔contactDraftRecovery và TaskCreateModal↔useTaskCreateController đã tồn tại baseline. Các nhóm sau chưa chạy trong full pipeline |
| Typecheck, strict budget, API contract, guidance, presentation gates | PASS | Không đổi public operation contract; API inventory 328/300 ready/28 blocked; budget 180 any/15 as-any/56 non-null giữ nguyên |
| Lead full-page workflow integration | PASS | Giữ lifecycle targeted regression |
| Lead target identity isolated test | FAIL dòng 147, actual undefined | Snapshot chính xác trước task cũng FAIL dòng 147. Clean baseline FAIL sớm hơn dòng 137; không gán nhầm hai failure là một. Giữ test, không sửa expected |

Không phát hiện regression mới trong các gate đã đối chiếu. Điều này không biến gate đỏ thành PASS. Inventory count 347/345 và repo manifest drift đã được ghi ở review trước; không refactor các module không liên quan. Không có attestation independent reviewer/gatekeeper, do đó không tuyên bố governed FROZEN/ACCEPTED.

## Consistency contract và rủi ro còn lại

1. Session/Workspace lấy từ Backend. Quyền tasks.update được kiểm lại sau receipt lock wait; module capability/FLS/scope được resolve sau wait và trước SQL commit guard. Không dùng owner/team do client làm authority.
2. Record facts, lifecycle và current affiliation được proof trên **cùng connection Tasks transaction SERIALIZABLE**, trong cùng SQL database hiện tại. Các row/range đã proof được giữ tới commit Activity+receipt. Test-only table lock chỉ là barrier của harness, không có production global lock hay distributed transaction. Interleaving owner+archive trước sửa FAIL, sau sửa mutation phải chờ; Activity commit trước mutation là write hợp lệ.
3. Thay đổi membership/capability/FLS **sau khi policy đã resolve** có ngữ nghĩa request tiếp theo; không bảo đảm huỷ tức thời mọi request đang chạy. Không giữ khoá toàn AccessControl. Nếu nghiệp vụ đòi instantaneous policy revocation, phần đó vẫn cần quyết định/thiết kế riêng.
4. Read policy là snapshot theo request; facts được filter trong từng statement. COUNT và page là hai statement READ COMMITTED, không phải timeline snapshot. Concurrent insert/owner change có thể làm count drift hoặc offset skip/revisit. Không hứa snapshot pagination giữa các request; không để count/page chưa filter.
5. Chưa stress serializable guard dưới nhiều workflow cạnh tranh; còn nguy cơ deadlock/retry và overhead range locks. Chỉ bốn owner DB facts hiện tại được hỗ trợ; chuyển các module sang DB khác cần đánh giá lại contract.
6. TEAM vẫn fail-closed; chưa có TEAM positive acceptance. UI Task từ Customer thiếu control; full UI E2E chưa PASS. Global eager frontend fetch và zero-area draft guard còn UX/performance debt. Không mở rộng TeamManagement, Calendar hoặc History mới.

## Evidence và chạy lại

Thư mục `evidence/activity-performance-20261021` chứa sanitized method/path/status/correlation/response metadata, results/run JSON, plans, index metadata, SQL read-only và output, browser screenshots, gate logs, cumulative git diffs, source SHA-256 và artifact hashes. Không chứa raw host/bootstrap logs, keys, password, tokens, connection strings hoặc browser trace. Test DB được giữ lại, không tự dọn/xoá.

Từ backend, dùng port test còn trống; mỗi invocation tạo DB test GUID riêng:

```powershell
powershell -NoProfile -File scripts/verify-activity-security.ps1 -ScaleReview -RaceReview -CommitRaceReview -Port 5097
powershell -NoProfile -File scripts/verify-activity-security.ps1 -Browser -ProjectionReview -Port 5097
powershell -NoProfile -File scripts/verify-activity-security.ps1 -UiReview -Port 5097
```

Chạy các browser invocation tuần tự vì Vite port dùng chung. UiReview được kỳ vọng exit 1 cho đến khi có UI Task đầy đủ; không coi đó là PASS. Cần SQL Server tích hợp Windows localhost, .NET SDK, node dependencies và Playwright Chromium. Các lệnh tạo dữ liệu chỉ trong DB riêng của harness; verify-ui.sql chỉ đọc DB test cụ thể. Kiểm tra thay đổi hiện tại bằng changes.json trước khi diễn giải kết quả từ một working tree khác.
