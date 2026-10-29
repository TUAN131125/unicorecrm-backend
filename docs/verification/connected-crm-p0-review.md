# Connected CRM: P0 review và sửa projection

## Kết luận

**P0: NEEDS_FIX. Không tạo checkpoint ACCEPTED.** Global Activity list còn N+1 và materialize toàn bộ tham chiếu của Workspace. Chưa có chứng minh tính nguyên tử giữa quyết định quyền của module sở hữu và SQL đọc/ghi Activities. Không tuyên bố production-ready hoặc UI E2E toàn trình PASS.

Đã đọc hai patch, changes/results/run của `evidence/connected-crm-20261021`, đối chiếu source đang chạy và chạy lại trên SQL Server thật. HEAD local và remote HEAD kiểm tra ngày 21/10/2026 vẫn là Backend `7b734f98a7173af2e24e094392826a20b6e11359`, Frontend `99db1773c03ce4a4b820db7e793da6eca872f862`. Các sửa đổi vẫn chưa commit; bằng chứng gắn với diff và SHA-256, không chỉ HEAD. Không stage, reset, xóa hay ghi đè artifact có sẵn của người dùng.

Đây là review source và kiểm chứng lại bằng thực thi trong chat hiện tại. Không có reviewer principal attestation từ môi trường reviewer cách ly; do đó không phải governed independent sign-off theo unicore-review/independent-reviewer.

## A. Findings

| ID | Severity | File/vị trí | Root cause và tái hiện | Impact | Status |
|---|---|---|---|---|---|
| ACT-PERF | High | Tasks/Application/ListActivities/Handler.cs:74; Tasks/Infrastructure/Persistence/EfTasksPersistence.cs:129 | Scan toàn bộ Activity references trước khi search/count/page; mỗi đích khác nhau lặp resource authorization, đọc facts, đọc validation và ghi decision audit. GET `/activities?limit=1` với 1.000 đích đo 8.180 SQL commands, 10.114,9 ms. | Request nhỏ có chi phí tăng theo dữ liệu toàn Workspace; rủi ro timeout/tải DB và bộ nhớ. | **FAIL, chưa sửa**; cần thay đường list bằng truy vấn có scope/batch do module sở hữu cung cấp qua AccessControl, không nhân bản OWN/TEAM. Chưa thực hiện một refactor lớn trong đợt review này. |
| ACT-WAIT | High | Tasks/Application/LogActivity/Handler.cs:34 | Kiểm tra quyền trước khi chờ khóa receipt. Giữ TABLOCKX trên tasks.IdempotencyRecords trong DB test, chờ request bị block, đổi owner rồi nhả khóa: trước sửa HTTP 201. | Ghi sau khi quyền record đã bị thu hồi trong lúc chờ; replay cũng có cửa sổ tương tự. | **Đã sửa cửa sổ chờ đã tái hiện**: reference authorization nằm sau locking read; test tạo mới và cached replay đều 404. Không khẳng định loại bỏ mọi race cross-module. |
| ACT-HISTORY | Medium | Contacts/Application/ProvideContactRecordAccessFacts/ContactRecordAccessFactProvider.cs:12 | Read validation dùng điều kiện affiliation đang active như write. Tạo Activity hợp lệ, kết thúc affiliation trong SQL test, GET History trả totalCount=0. | Lịch sử hợp lệ biến mất sau thay đổi quan hệ. | **Đã sửa/test**: read chấp nhận quan hệ lịch sử được Contacts lưu; write vẫn cần active; cả hai record vẫn phải qua quyền hiện tại. Sau sửa History=1, write mới=404. |
| CONTACT-SUBJECT | High nghiệp vụ | Contacts/Application/GetRelationshipSummary/Handler.cs:35 | Summary chỉ đọc contacts.CustomerRelationships (stakeholder), bỏ canonical customers.Customers subject CONTACT. SQL có Customer nhưng HTTP customerIds rỗng. | UI báo 0 Customer sau conversion. | **Đã sửa/test**: thêm lookup subject do Customers sở hữu, qua Customer authorization/scope/FLS rồi hợp nhất CustomerIds/LinkedRecords; không tạo membership giả. |
| CONTACT-CACHE | Medium | Frontend contactCustomerLookup.ts:5; useContactOverview.ts:28 | Lookup ưu tiên Customer của Organization trong snapshot; Overview chỉ đếm stakeholder documents. | Nhầm Customer trực tiếp, mất kết quả sau refresh. | **Đã sửa/test**: direct lookup chỉ lấy CONTACT subject; Overview dùng linked record từ summary API cho canonical account link, giữ riêng role stakeholder. |
| UI-SOURCE | High nghiệp vụ | Frontend useLeadReferenceData.ts:56; LeadListPage.tsx:81; useLeadFormController.tsx:385 | Route không cung cấp sources, active sources=[], form vẫn yêu cầu Source theo profile validation. | Không tạo Lead qua UI ở Workspace test mới. | **BLOCKED** UI journey; không chèn option/fixture và không thay bằng API để tuyên bố UI PASS. Cần nguồn catalog/wiring hợp lệ hoặc quyết định policy form trước khi sửa. |

Các đường record permission, invalid ID/Workspace/module, contradictory references, source spoofing, SYSTEM, restrictive FLS, owner/capability/archive replay, OWN pagination/count/search và TEAM denial được thực thi lại; không thấy bypass trong các case đã chạy. Đây không phải chứng minh mọi tổ hợp quyền.

### Compatibility và race còn lại

- Legacy SYSTEM vẫn giữ trong DB nhưng không công bố như audit đáng tin cậy. Module không hỗ trợ, unanchored/source tùy ý vẫn fail-closed; chưa có migration/compatibility proof cho mọi dữ liệu legacy ngoài các UI shapes được test.
- Read sau archive được phép nếu còn quyền; ghi/replay sau archive bị từ chối. History của affiliation đã kết thúc được giữ nếu cả Contact và Organization còn readable.
- Module facts, quan hệ và Tasks transaction vẫn được đọc bằng các DbContext riêng. Sửa ACT-WAIT không cung cấp transaction snapshot chung. Revocation trong khoảng sau lần kiểm tra cuối hoặc trong một global scan dài **chưa được kiểm chứng/giải quyết đầy đủ**; không đánh dấu PASS atomicity.
- TEAM chỉ có negative fail-closed proof; positive TEAM **BLOCKED** do chưa có Team/TeamMembership đáng tin cậy. Không fallback sang OWN/WORKSPACE, không phát triển TeamManagement.

## B. Changes trong đợt review

Backend:

- `Tasks/Application/LogActivity/Handler.cs`: kiểm tra reference sau lấy khóa idempotency, trước replay/write.
- `Contacts/.../ContactRecordAccessFactProvider.cs`: phân biệt quan hệ active cho write với quan hệ lịch sử cho read.
- `Customers/Contracts/CustomerRelationshipTargetParticipant.cs`, `Customers/Application/ResolveRelationshipTargets/Participant.cs`: lookup canonical subject nội bộ và reuse Customer visibility enforcement.
- `Contacts/Application/GetRelationshipSummary/Handler.cs`: thêm Customer subject vào IDs/linked records; không thay conversion, uniqueness, schema hay stakeholder documents.
- `scripts/verify-activity-security.ps1`: opt-in ProjectionReview/ScaleReview/RaceReview/UiReview; DB mới mỗi lần, official migrate/seed, HTTP pipeline thật, SQL lock barrier, metadata đã loại secret. UiReview không đọc records.json cũ của API smoke.

Frontend:

- `contactCustomerLookup.ts`: bỏ fallback Organization Customer cho direct Contact.
- `useContactOverview.ts`: hiện canonical Customer từ summary có thẩm quyền; không tạo stakeholder role giả.
- `tests/contract/contact-overview.test.mts`: canonical account link không có stakeholder, Organization-vs-Contact lookup, quyền/late response regressions.
- `tests/e2e/crm-real.spec.ts`: đặt tên đúng API workflow + UI; kiểm tra canonical Contact/Organization subject, không lẫn Organization Customer, refresh/relogin/history.
- `playwright.crm-ui.config.ts`, `tests/e2e/crm-ui-real.spec.ts`: kịch bản UI riêng, Login/Workspace thực thi; prerequisite thiếu được ghi BLOCKED và process exit 1, không báo xanh.
- `src/guidance/content/crm/extendedScreens.ts`: Contact guidance version 6, VI/EN giải thích account subject khác stakeholder/Organization affiliation.
- `scripts/quality/quality-pipeline.json`: khai báo dependency `typescript` thực sự được commercial-flow gate import; không sửa assertion inventory.

Public HTTP DTO shape, OpenAPI/generated client, DB schema và conversion protocol không đổi. Organization conversion trả Customer có subject ORGANIZATION_ACCOUNT đúng; Contact summary vẫn chỉ gồm direct Contact Customer và stakeholder được khai báo, không tự kế thừa Organization Customer. Chưa bổ sung một panel canonical Customer mới cho Organization Overview.

## C. Verification

Môi trường: ASP.NET Core ApiHost .NET 10, SQL Server 17.0.1000.7 localhost, Windows integrated auth; Vite connected + Chromium. Mỗi harness run dùng DB `UnicoreCRM_ActivityTest_<runId>` mới; giữ lại DB; không xóa/reuse DB người dùng.

| Test | Environment | Expected | Actual | Kết quả |
|---|---|---|---|---|
| Security + projection + locking race + history regressions | HTTP/SQL Server, run 87a69f3116574b61ab762886beffcab3, 07:23:48–07:24:42 UTC | Quyền và references đúng, không duplicate | 81 assertions PASS, gồm 1 Playwright scenario | PASS trong phạm vi case |
| tasks.read thiếu contacts.read; OWN ngoài owner; TEAM; foreign/missing IDs; unsupported module; mismatch/source/SYSTEM | Cùng run, HTTP thật | Không lộ/không ghi | Đúng 0/403/404/422 theo contract | PASS |
| Owner/capability/archive replay; switch Workspace | HTTP thật | Receipt không bypass quyền hiện tại | Đúng 403/404; retry hợp lệ cùng ID | PASS |
| Search/count/cursor/pagination OWN, restrictive FLS | HTTP + SQL | Metadata chỉ gồm record được phép | 3 trang hợp lệ/distinct, total=3; hidden total=0 | PASS |
| Owner thay đổi khi chờ receipt, create và replay | SQL lock barrier + HTTP | 404 | Trước sửa create=201; sau sửa cả hai=404 | PASS regression cụ thể |
| Canonical subject không stakeholder | SQL + HTTP + FE contract | Customer hiển thị không cần membership giả | Trước HTTP rỗng/UI 0; sau IDs đúng/UI 1 | PASS |
| Organization subject và không kế thừa sang Contact | API conversion thật + SQL | 2 subject độc lập, mỗi subject unique | Đúng relationshipRef, Contact giữ đúng 1 Customer; SQL mỗi subject=1 | PASS |
| Customer scope/capability denial; foreign Contact | HTTP thật | Không lộ liên kết | IDs=[], foreign404 | PASS |
| Refresh/relogin/restart ApiHost | Chromium + HTTP + SQL | Dữ liệu và reference tồn tại | Đọc lại Lead/Contact/Org/Customer/Task, Activity; subject count=1 | PASS |
| End affiliation giữ History, chặn write mới | HTTP/SQL | total=1 và write404 | Trước total=0; sau total=1, write404 | PASS |
| Global list performance | SQL Server, scale-race-before | Không N+1 theo số đích | 914→8.180 commands khi 100→1.000 đích | **FAIL** |
| Memory amplification/production capacity | Chưa có allocation/working-set/load profile | Đo có kiểm soát | Chỉ xác nhận source materialize không giới hạn | **BLOCKED**, chưa đo |
| Cross-module atomic revocation ở mọi interleaving | Chưa có shared snapshot proof | Không TOCTOU ngoài barrier đã test | Chưa đủ proof | **BLOCKED** |
| FE Contact Overview contract | Node/tsx/JSDOM | Canonical links, direct lookup, denial, ABA | FAIL-before; PASS-after/final | PASS, không phải backend security proof |
| Lead/Contact History contracts | Node/tsx/JSDOM | Hồi quy | 21 + 25 checks PASS | PASS |
| Contact follow-up verifier | SQL Server riêng | SQL scope/filter/count compose đúng | 30 PASS; DB giữ lại | PASS |
| Backend architecture regression | .NET HTTP metadata + architecture | Không regression mới | 33 PASS, 4 FAIL; message sets so với baseline không thêm/bớt | FAIL baseline |
| FE typecheck, API check, strict-core, guidance, presentation responsibility | Node quality gates | Pass | Pass | PASS |
| Quality pipeline | Node gate | Manifest đúng | Đã qua dependency check; dừng 347 !== 345 | FAIL inventory baseline |
| UI-only journey | Chromium, run 6d25a0e51fe74e03a00090f2fb2375be | Thao tác bằng UI | Login/Workspace PASS; Create Lead BLOCKED; 7 bước sau BLOCKED; runner exit1 | **BLOCKED**, không UI E2E PASS |

### Số đo SQL performance

Run `7181c51bad644ec9bb95d938e8cdd091`, mỗi request `GET /activities?limit=1`. Dữ liệu tạo tăng dần trong một DB test mới; số Activity tổng gồm fixture bảo mật ngoài các hàng scale.

| Synthetic Activity cùng 1 target | HTTP | elapsed ms | totalCount |
|---:|---:|---:|---:|
| 100 | 200 | 220,8115 | 109 |
| 1.000 | 200 | 2.018,4372 | 1.009 |
| 2.200 | 200 | 251,7097 | 2.209 |
| 5.000 | 200 | 243,6378 | 5.009 |

| Distinct Contact targets thêm vào | HTTP | elapsed ms | SQL command log entries trong request | totalCount |
|---:|---:|---:|---:|---:|
| 100 | 200 | 1.486,5718 | 914 | 5.109 |
| 1.000 | 200 | 10.114,9019 | 8.180 | 6.009 |

SQL command count lấy delta `Executed DbCommand` trong log host riêng đang idle trước/sau request; không phải server-wide load benchmark. Chỉ một sample/mức, không có p95/concurrency/production hardware. EF có OPENJSON ở các collection lớn; không tái hiện lỗi giới hạn 2.100 tham số với 5.000 rows. Không suy kết quả này thành an toàn với cardinality bất kỳ. Log raw không đưa vào evidence công khai.

## D. Quality gate triage

| Nhóm | Bằng chứng | Xử lý |
|---|---|---|
| Regression mới P0 | ACT-WAIT, ACT-HISTORY FAIL-before | Sửa và chạy PASS-after. ACT-PERF còn mở, chặn acceptance. |
| Baseline ảnh hưởng Connected CRM | Contact canonical projection; Source UI rỗng | Projection đã sửa; Source UI còn BLOCKED, không tự thêm catalog hoặc bỏ business validation. |
| Baseline architecture debt | 4 failed tests, so sánh TRX message sets chính xác: không delta | Ghi debt owner mapping, migration metadata, cross-layer workflow edges, unadmitted endpoint metadata; không mở rộng sửa toàn hệ thống. |
| Baseline verification/config | Thiếu requires.typescript; baseline manifest thực tế 347 nhưng assertion345 | Dependency đã sửa đúng thực tế; giữ assertion, ghi inventory debt. |
| Baseline readiness | ContactFormModal↔contactDraftRecovery; TaskCreateModal↔useTaskCreateController type dependency cycles | Không đổi runtime CRM để che gate; cần tách ownership types trong task riêng. |
| Baseline repository inventory | repo:check baseline cũng FAIL; source/file inventory đã drift trước patch | Không tự regenerate toàn inventory cũ để báo PASS. File mới trong diff cũng chưa được inventory chấp nhận, gate vẫn FAIL. |
| Harness/config trong review | Hai browser runner chạy đồng thời tranh port3039; UI run từng đọc records cũ của API smoke | Giữ failed artifacts; sửa nhánh UiReview không đọc API records, chạy tuần tự lại. Chỉ run cuối được dùng cho kết luận. |

## E. Evidence và tái lập

Evidence mới: `evidence/connected-crm-review-20261021/`. Evidence P0 ban đầu được giữ nguyên.

- `review.json`, `changes.json`, `backend.patch`, `frontend.patch`: verdict, source hashes, full source diff; chưa có Git checkpoint vì chưa đạt P0.
- `projection-before/`, `scale-race-before/`, `history-before/`, `connected-after/`, `ui-only/`: run times, DB identity, results, sanitized HTTP method/path/status/correlation ID và metadata.
- `verify-projection.sql`, `sql-projection.txt`: read-only SQL cho hai subject, uniqueness, stakeholder0, ended affiliation1 và persisted Activity1.
- `browser-api/`: screenshots Contact và Sales OWN, API HTTP metadata, record IDs. `browser-ui/`: UI step statuses và screenshot source prerequisite. Không lưu trace/video/header/token/password trong evidence này.
- `review-gate-delta.json`: baseline-vs-current architecture failure message sets. Các `review-*.log`, `projection-ui-*.log`: focused regression output.

Từ thư mục backend, dùng PowerShell, chạy tuần tự các browser runs vì cùng port3039:

```powershell
./scripts/verify-activity-security.ps1 -ProjectionReview -RaceReview -Browser -Port 5400
./scripts/verify-activity-security.ps1 -ScaleReview -Port 5402
./scripts/verify-activity-security.ps1 -Browser -UiReview -Port 5401
sqlcmd -S localhost -E -C -i docs/verification/evidence/connected-crm-review-20261021/verify-projection.sql -W
```

Mỗi invocation harness tạo DB GUID mới; SQL file trên chỉ đọc DB retained của run đã ghi trong báo cáo. UiReview được kỳ vọng exit1/BLOCKED trong trạng thái source catalog hiện tại. Không đổi expected để làm xanh. Mật khẩu bootstrap ngẫu nhiên chỉ nằm trong process environment; cần SQL Server local và quyền tạo DB test, .NET SDK, Node/node_modules và Playwright Chromium. Các gate/contract commands có trong log đầu ra tương ứng.

## Remaining risks và bàn giao

ACT-PERF vẫn là finding High chưa xử lý; cần query/batch authorization phù hợp boundary trước checkpoint P0. Việc đóng toàn bộ TOCTOU cần quy tắc snapshot/transaction rõ ràng qua module owner; chưa có chứng minh bằng test cho mọi interleaving. Chưa benchmark allocation/concurrency. UI-only scenario mới thực thi tới prerequisite tạo Lead; các thao tác còn lại chưa có automation được chạy thành công. Organization Overview chưa có canonical Customer panel mới. Full verify/repo inventory và governed independent attestation chưa đạt. Không tuyên bố hoàn tất toàn bộ Connected CRM.
