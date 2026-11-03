# Connected CRM — kết quả P0 OWN/WORKSPACE

Ngày: 21/10/2026, Asia/Saigon. Đây là báo cáo thay đổi đã thực thi, không phải xác nhận toàn sản phẩm đã hoàn thiện.

Backend HEAD và origin/master sau fetch: `7b734f98a7173af2e24e094392826a20b6e11359`. Frontend HEAD và origin/main: `99db1773c03ce4a4b820db7e793da6eca872f862`. Hai HEAD trùng baseline được giao. Trước sửa không có tracked diff; các thư mục evidence, runtime, backend-work và artifact chưa commit đã được giữ nguyên. Thay đổi hiện tại chưa commit; diff và SHA-256 từng file nằm trong thư mục evidence kèm báo cáo.

Theo quyết định bổ sung của người dùng: P0 kiểm chứng OWN/WORKSPACE; TEAM giữ fail-closed. Các ca nghiệp vụ TEAM thành công là **BLOCKED cho P1**, không dùng kết quả từ chối để tuyên bố đã hỗ trợ TEAM.

## PHASE 0 — A. Findings

| ID | Severity | File / vị trí | Root cause | Impact | Status |
|---|---|---|---|---|---|
| A | P0 | `Tasks/Application/ListActivities/Handler.cs`; `EfTasksPersistence.ListActivitiesAsync` | Baseline chỉ kiểm tra Tasks scope/field gate rồi lọc Workspace, không xác quyền bản ghi CRM | Người giữ tasks.read đọc Activity, count và cursor của Contact không được đọc | CODE_CONFIRMED, HTTP/SQL_REPRODUCED, FIXED trong phạm vi đã kiểm thử |
| B | P0 | `Tasks/Application/LogActivity/Handler.cs` | Chỉ nhánh customers gọi reference authority; module khác và module tùy ý đi thẳng tới ghi SQL | Lưu ID không tồn tại, foreign Workspace hoặc quan hệ giả | CODE_CONFIRMED, HTTP/SQL_REPRODUCED, FIXED |
| C | P0 | `LogActivity/Handler.cs`, trước nhánh receipt | Cached response không được chặn bởi quyền hiện tại của Lead/Contact/Organization | Replay trả nội dung sau đổi owner hoặc thu hồi quyền | CODE_CONFIRMED, HTTP/SQL_REPRODUCED, FIXED |
| D | P0 | `LogActivityValidation.Activity` | HTTP chấp nhận SYSTEM; không có trusted SYSTEM writer riêng | Nhân viên giả nguồn audit; dữ liệu cũ có thể mang nhãn hệ thống | CODE_CONFIRMED, HTTP/SQL_REPRODUCED, FIXED: từ chối ghi và không công bố legacy SYSTEM |
| E | P0 | Backend `TaskRecordAccess`; FE `leadHistory.ts:6`, `contactHistory.ts:6` | Gắn khả năng dùng Activity với Tasks WORKSPACE, dù Activity không có Task owner | SALES OWN không mở được History hợp lệ | CODE_CONFIRMED, contract test RED→GREEN và browser OWN PASS |
| F | P1 | `RecordAccessEvaluator.cs:300`; Workspace directory; member-access normalizer | Chưa có Team/TeamMembership chính thức; TeamIds rỗng, nonempty TeamIds bị từ chối | TEAM không thực hiện được nghiệp vụ hợp lệ | CONFIRMED; giữ fail-closed theo quyết định người dùng; P1 |
| G | P1 | `LeadCustomerConversionPage.tsx:68` | Nhập subjectId trực tiếp | Chưa có picker Contact/Organization và bước xác nhận hoàn chỉnh | CODE_CONFIRMED; chưa sửa trong P0 |
| H | P1 | Lead/Contact History models/hooks; UI History | Nguồn timeline hiện là Activity, chưa tổng hợp audit/outbox/task events | Không thể gọi đây là History nghiệp vụ đầy đủ | CODE_CONFIRMED; chưa sửa trong P0 |
| I | P1 | `contactCustomerLookup.ts`; Contact relationship-summary handler | Lookup snapshot và quan hệ stakeholder khác với canonical Customer subject | UI Contact sau conversion vẫn có thể hiển thị 0 liên kết Customer dù SQL Customer/LeadRef đúng | OBSERVED trên browser và CODE_CONFIRMED; cần hoàn thiện projection/UI ở P1 |
| J | P2 | `useCustomerMeeting.ts`, `useOrganizationMeeting.ts` | Appointment dùng Task dueAt; end/location/attendees ghép description | Chưa có các trường lịch hẹn có cấu trúc | CODE_CONFIRMED; giữ Task-based theo yêu cầu |

Tái hiện: chạy `pwsh ./scripts/verify-activity-security.ps1` trong backend. Harness luôn tạo database mới có GUID; không tái sử dụng hay xóa database có sẵn. Bản chạy trước sửa có 17 assertion FAIL: missing/foreign IDs, module tùy ý, quan hệ mâu thuẫn, SYSTEM, OWN/TEAM, capability revocation, replay và metadata đều tái hiện qua HTTP thật. File `baseline-activity-results.json` giữ nguyên bằng chứng đó. Kỳ vọng validation ban đầu dùng 400 đã được đối chiếu và chỉnh sang **422** theo `TaskErrors.Validation`; baseline thực tế trả **201**, nên đây không phải đổi test để che lỗi.

### B. Changes

Phase 0 chỉ đọc source, fetch remote, kiểm tra working tree/baseline và chạy tái hiện. Không sửa các artifact chưa commit của người dùng.

### C. Verification

| Test | Environment | Expected | Actual | Status |
|---|---|---|---|---|
| HEAD / baseline / remote | Hai Git repository, fetch origin | Xác định đúng source và dirty state | HEAD trùng baseline; không có tracked diff trước sửa | PASS |
| Tái hiện A–D trước patch | ASP.NET Core + SQL Server riêng | Chặn request không hợp lệ, không lộ metadata | 17 assertion FAIL trên baseline | FAIL — bằng chứng bug |

### D. Evidence

Xem `evidence/connected-crm-20261021/baseline-activity-results.json`, `backend.patch`, `frontend.patch`, `changes.json`.

### E. Remaining risks

Các findings G–J chưa được giải quyết bởi bản sửa Activity Security. TEAM chuyển sang P1 theo quyết định đã chốt, không chặn P0 OWN/WORKSPACE.

## PHASE 1 — A. Findings và kết quả sửa

A–E được sửa bằng module-owned facts và chính `IRecordAccessEvaluator`. Tasks không truy vấn DbContext của CRM, không tự tính OWN/TEAM và không tin owner/team/policy từ client.

Quy tắc đã triển khai:

- Mỗi Activity phải chứng minh được bản ghi Lead, Contact, Organization hoặc Customer trong Workspace tin cậy. Relationship-only Contact/Organization được hỗ trợ; không có cả record lẫn relationship thì trả 422.
- Mọi tham chiếu phải được phép đọc độc lập. Module sở hữu xác minh quan hệ thực tế; một Contact được phép không mở quyền cho Organization bị cấm. Customer subject phải khớp canonical relationship nếu request gửi relationshipRef.
- Lifecycle chặn ghi mới và replay vào bản ghi đã archive. Quyền và field security hiện tại được kiểm tra trước khi đọc receipt.
- Free text không thể được redaction đáng tin cậy theo từng field CRM. Khi có field CRM bị withheld, toàn bộ Activity liên quan bị loại, kể cả search/count/cursor; Tasks field restriction tiếp tục từ chối bảo thủ. Không tuyên bố đã có một vocabulary field security riêng cho Activity.
- SQL chỉ lấy reference facts cho bước kiểm quyền. Danh sách ActivityId đã được cho phép trở thành predicate SQL **trước COUNT, search và paging**. Không lấy một trang rồi lọc ở Frontend.
- HTTP chỉ ghi CALL/EMAIL/MEETING/NOTE/MESSAGE. SourceRef chỉ là origin marker đã giới hạn, khớp ID; không nhận audit evidence tùy ý. Legacy SYSTEM giữ trong SQL nhưng bị loại khỏi danh sách/search/count vì source hiện chưa có trusted SYSTEM writer.
- Các wire shape hiện hữu `CONTACT_ACTIVITY`, `ORGANIZATION_DETAIL`, `CUSTOMER_360`, direct record và canonical multi-reference đã có positive tests. Không đổi hình dạng DTO hay generated client.
- Frontend History cho phép OWN/WORKSPACE của **module CRM sở hữu**, yêu cầu module read + tasks.read; TEAM vẫn unavailable. State loading/empty/denied/error và authority retirement được giữ, kiểm tra lại bằng contract tests.

### B. Changes

Danh sách đầy đủ 32 file source/config/test/decision cùng SHA-256: `evidence/connected-crm-20261021/changes.json`.

| Files | Lý do |
|---|---|
| `Tasks/Application/Common/ActivityRecordAccess.cs` (mới); `Tasks/Contracts/ActivityReferenceAuthority.cs` (mới) | Compose quyết định AccessControl và kiểm chứng lifecycle/relationship do module sở hữu |
| Bốn `Provide*RecordAccessFacts/*RecordAccessFactProvider.cs` và bốn `*Module.cs` trong Leads/Contacts/Organizations/Customers | Cung cấp và đăng ký kiểm chứng tham chiếu bằng persistence của chính owner |
| `TaskRecordAccess.cs`, `TasksPersistence.cs`, `ListActivities/Handler.cs`, `LogActivity/Handler.cs`, `LogActivityValidation.cs`, `EfTasksPersistence.cs`, `TasksModule.cs` | Scope theo bản ghi CRM, replay guard, input validation, SQL allowlist trước metadata, SYSTEM fail-closed |
| `scripts/verify-activity-security.ps1` (mới) | HTTP/SQL/browser acceptance, bốn account, database riêng, migration chính thức, restart, evidence không chứa credential |
| Hai verifier project files, `ContactFollowUpVerifier/Program.cs`, `TasksHandoverVerifier/Program.cs` | Giữ linked-source verifier build được; cập nhật đúng semantics của Activity; tùy chọn giữ database test riêng |
| Backend `.gitignore` | Không đưa runtime logs/data-protection keys của test vào Git |
| FE hai History models, `useLeadHistory.ts`, hai History contract tests | Sửa OWN availability; chứng minh RED→GREEN và chống stale/denied disclosure |
| FE `guidance/content/crm/extendedScreens.ts` | Guidance VI/EN và version Lead/Contact đồng bộ với phạm vi hỗ trợ |
| FE `playwright.crm-real.config.ts`, `tests/e2e/crm-real.spec.ts` (mới) | Bắt buộc ApiHost thật, không fixture fallback, đăng nhập thật và kiểm tra UI của admin/SALES OWN |
| `docs/architecture/team-scope.md` | Lưu quyết định TEAM và ma trận module/backfill bắt buộc trước P1 |

### C. Verification

Environment: .NET SDK 10.0.401; SQL Server `17.0.1000.7`, localhost, Integrated Security; database mới theo run ID; không EF InMemory cho HTTP/SQL acceptance.

| Test bắt buộc | Expected | Actual | Status |
|---|---|---|---|
| tasks.read nhưng thiếu contacts.read | Không trả Activity/metadata | count 0; replay 404 | PASS |
| Contact ngoài OWN | Không đọc/ghi | list loại bản ghi; log 404 | PASS |
| TEAM ngoài phạm vi | Fail-closed | count 0; log 404 | PASS — chỉ chứng minh từ chối |
| Contact/Lead foreign Workspace | Không tham chiếu | 404; Workspace switch 403 | PASS |
| ID không tồn tại | Không lưu | 404 cho Contact/Lead/Organization | PASS |
| moduleKey tùy ý | Không bypass | 422 | PASS |
| Quan hệ mâu thuẫn | Không lưu/không lộ | 422 hoặc 404 không tiết lộ target | PASS |
| Thu hồi capability sau create | Không đọc lại | count 0; replay 404 | PASS |
| Replay sau đổi owner/archive/quyền | Không trả cache nhạy cảm | 404; valid replay cùng ID | PASS |
| Phân trang/count/search | Chỉ tính rows được phép | 3 authorized rows qua 3 trang, không hidden continuation | PASS |
| Positive read/write | Lưu và đọc đúng | Bốn module; canonical multi-reference; UI shape hiện hữu | PASS |
| SALES OWN | Chỉ thấy bản ghi của mình | Tài khoản riêng: API và UI History sau refresh | PASS |
| SYSTEM/source giả | Không tạo audit giả | HTTP 422; legacy SYSTEM count/search 0 | PASS |
| Field security | Không lộ free text/metadata/cache | count 0; replay 404 | PASS |

Run cuối: **72/72 assertion HTTP/SQL/harness PASS**, bao gồm **1 Playwright test thật** có admin và SALES OWN. Hai contract suites FE: **21 Lead + 25 Contact = 46 checks PASS**; đây là runtime-port fixtures, không được tính thay cho E2E thật.

Regression:

| Test | Actual | Status |
|---|---|---|
| ApiHost build | 0 warning, 0 error | PASS |
| Task handover verifier | 48 checks; memory participant, không tuyên bố SQL concurrency | PASS |
| Contact follow-up verifier | 30 checks, gồm SQL composition và migration trên database test riêng | PASS |
| Backend architecture suite | 33 PASS / 4 FAIL trên final; baseline cũng cùng bốn test FAIL | FAIL — tồn tại trên baseline |
| FE typecheck / strict-core / guidance / presentation | Focused gates đã đạt | PASS |
| FE api:check | 328 operations, generated contract khớp | PASS |
| Full npm verify | Dừng gate 10/327; 9 gate đạt; hai import cycles có trên baseline | FAIL; phần sau NOT_RUN |
| FE quality-pipeline | Thiếu khai báo TypeScript dependency của commercial-flow-regressions; baseline cùng lỗi | FAIL |
| FE repo:check | Inventory đã drift trên baseline; test mới cũng chưa có trong inventory cũ | FAIL; không regenerate toàn bộ để che drift |
| TEAM positive | Cần Team/TeamMembership/TeamId chính thức | Chưa triển khai | BLOCKED cho P1 |

### D. Evidence

Thư mục `evidence/connected-crm-20261021/` chứa:

- `results.json`, `http.json`, `run.json`: kết quả, HTTP method/path/status/correlation ID và thời gian UTC của run cuối.
- `activity-count.sql`: SQL do EF thực thi, có Workspace, loại SYSTEM và ActivityId allowlist trước COUNT; không có parameter values.
- `browser-http.json`, `browser-records.json`: HTTP và ID dữ liệu synthetic của hành trình.
- `browser-contact-after-refresh.png`, `browser-sales-own-history-after-refresh.png`: History UI sau refresh.
- `regression-comparison.json`, `quality-summary.json`, focused regression logs.
- `backend.patch`, `frontend.patch`, `changes.json`: diff và hashes để review/rollback.

Raw Playwright trace tắt vì có thể lưu bearer headers/password bodies; evidence dùng screenshot và HTTP metadata đã loại credential. Runtime logs/key ring chỉ giữ cục bộ trong thư mục đã ignore. Không commit secret, token, password hay connection string thực tế.

### E. Remaining risks

1. Global Activity list đang đọc reference candidates và kiểm từng target, có cache trong một request theo reference; predicate kết quả nằm ở SQL nhưng chưa được benchmark ở Workspace lớn. Không tuyên bố đã đạt hiệu năng production.
2. Cross-module lifecycle/reference checks là validation-point checks, không có distributed transaction/lock với các owner. Chưa kiểm chứng race archive/ownership đồng thời trong cửa sổ giữa validation và commit.
3. Historical Contact→Organization Activity yêu cầu relationship còn được owner chứng minh là active; cần quyết định timeline lịch sử cho quan hệ đã kết thúc. Không tự phục dựng quan hệ từ label/body.
4. Field security đang dùng chính sách loại cả Activity khi có trường owner bị withheld; chưa có redaction từng field Activity.
5. Chưa có independent reviewer principal attestation hoặc release freeze. Các full gates còn FAIL không được ghi thành PASS.

## PHASE 2 — A. Findings

Smoke đã chạy với Frontend connected, ASP.NET Core ApiHost và SQL Server thật. Existing connected config mặc định dùng fixture nên không được dùng làm bằng chứng này; config mới bắt buộc API URL và test credential qua environment.

### B. Changes

Harness tạo database mới có GUID, Workspace test, admin, Sales OWN, Sales TEAM và account không quyền. Account/role fixtures được seed trong database test; mọi account đều đăng nhập qua Identity HTTP thật. OWN/TEAM có platform capabilities tối thiểu `workspace.context.resolve`, `access.read` để mở Workspace, không cấp thêm quyền CRM rộng. Migration chạy bằng lệnh chính thức `ApiHost --migrate`; bootstrap test bằng `--seed-demo`. Database được giữ lại, không xóa database/file SQL của người dùng.

### C. Verification

| Bước | Environment / actual | Status |
|---|---|---|
| Login → Workspace | UI đăng nhập thật, auto chọn Workspace hợp lệ | PASS |
| Lead + owner | Tạo qua API thật; GET xác nhận owner là member đã đăng nhập | PASS — API |
| Contact + Organization + affiliation | Tạo và liên kết qua HTTP thật, concurrency version | PASS — API |
| Lead → Customer | Workflow thật với EXISTING Contact; retry cùng Customer ID | PASS — API |
| Không trùng Customer | SQL đúng một Customer cho subject, Lead.CustomerRef đúng | PASS |
| Task/Meeting | Task Customer; appointment Task có dueAt/assignee/description; complete qua HTTP | PASS — API |
| Activity/History | Ghi/replay thật, đọc API; mở History UI sau refresh và logout/login | PASS |
| User không quyền | Account riêng: list/log 403 | PASS |
| SALES OWN UI | Browser context riêng, đọc History Contact mình sau refresh, không thấy Activity admin | PASS |
| Restart ApiHost | GET Lead/Contact/Organization/Customer/Task vẫn 200; SQL giữ quan hệ/Activity | PASS |
| Toàn bộ CRUD qua form/picker UI | Smoke tạo dữ liệu bằng API, không giả lập form | NOT_RUN đầy đủ |
| TEAM nghiệp vụ thành công | Giữ fail-closed theo quyết định người dùng | BLOCKED cho P1 |

### D. Evidence

Run cuối và thời điểm nằm trong `run.json`. ID và quan hệ được lưu trong `browser-records.json`; các assertion SQL sau restart nằm trong `results.json`. Trước/sau snapshot được kiểm bằng API và SQL, không có response mock hoặc fixture server.

### E. Remaining risks

Đây là **real connected smoke và nghiệm thu Activity Security OWN/WORKSPACE**, chưa phải nghiệm thu toàn bộ sản phẩm. Chưa chứng minh toàn hành trình qua form UI, picker conversion, unified business History hoặc TEAM thành công. Không coi Phase 2 tổng thể đã PASS.

## PHASE 3–5 và TEAM P1

Activity/History hoàn chỉnh, Contact/Organization picker cho conversion và phần appointment có cấu trúc vẫn chưa triển khai. Ma trận TEAM đã được lập trong `../architecture/team-scope.md`: Lead, Contact, Organization, Customer và Task cần TeamId module-owned nếu bật TEAM; Activity/History kế thừa quyền tham chiếu; bản ghi cũ giữ null và fail-closed, không backfill từ assignedTeam/owner. Mọi mở rộng Team API/migration/client thuộc thay đổi P1 riêng có thể review và rollback.
