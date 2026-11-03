# P0 — Khôi phục tải Quote List và Order List

Ngày kiểm chứng: 2026-11-02. Bằng chứng nằm tại `task-runtime/quote-order-loading-p0/`.

## Kết quả

Đã sửa database thực đang được cấu hình cho ứng dụng: **`DESKTOP-ICH225R / UnicoreCRM_Development`**. Cả Quote List và Order List đã chuyển từ HTTP **500 → 200**, không sửa thông báo lỗi, không mock dữ liệu, không reset/reseed và không sửa trực tiếp bản ghi nghiệp vụ.

Workspace demo `unicore-demo-anh-duong` đọc được **3 Quote cũ**; Order List hiển thị **empty state hợp lệ**. SQL xác nhận toàn database hiện có **299 Quote và 0 Order**.

## Root cause của từng danh sách

| API | Trước sửa | SQL exception | Nguyên nhân |
|---|---|---|---|
| `GET /quotes?limit=250` | 500, `INTERNAL_ERROR` | Error 207: `Invalid column name 'AutoConfirmRequested'` | EF tải navigation SalesAcceptance bằng model mới nhưng schema còn thiếu cột |
| `GET /orders?limit=250` | 500, `INTERNAL_ERROR` | Error 207: `Invalid column name 'ConfirmationOrigin'`, `'ConfirmationPolicyVersion'` | EF projection Orders tham chiếu hai cột chưa được migrate |

Hai lỗi cùng root cause: **schema Development chưa theo kịp hai migration mới của Quote Detail/automatic confirmation**. Các migration O1/O2 trước đó đã được áp dụng; không phải toàn bộ chuỗi O1/O2 bị thiếu. SQL kiểm tra tên cột trước khi trả kết quả nên Order List vẫn lỗi dù bảng Orders đang trống.

Correlation ID của hai request lỗi được lưu trong `before/initial-quotes.json` và `before/initial-orders.json`, lần lượt:

- `demo-1f4ed6f263794a789ecf220dced07226`
- `demo-3befab791259465eb137cbba8ec7e3e8`

`before/host.log` giữ SQL query, stack trace và error 207. Response gửi tới Frontend vẫn là lỗi tổng quát an toàn, không chứa SQL exception hoặc secrets.

## Môi trường đã xác minh

- Khi bắt đầu kiểm tra, không có ApiHost/Vite đang lắng nghe trên các cổng ứng dụng thông thường. Không khẳng định đã bám vào một process cũ còn sống.
- Cấu hình local thực, bao gồm các override hiện hành, được giải quyết bằng helper sẵn có. `DB_NAME()` và `@@SERVERNAME` xác nhận đúng target nói trên; đây không phải suy đoán từ tên file cấu hình.
- Authentication SQL dùng cấu hình local hiện hành. Evidence chỉ ghi server/catalog/integrated-security metadata; không ghi password, token, SQL login secret hay nội dung UserSecrets.
- Binary ApiHost thực được ghi path, timestamp và SHA-256 trong `api-binary.json`/`api-binary-after.json`; Backend hiện tại build sạch.
- Frontend mặc định connected, API `http://localhost:5080`. Task dùng Vite riêng `http://127.0.0.1:3048` với API base URL tường minh `http://127.0.0.1:5098`.
- ApiHost riêng chạy Development với chế độ `--demo-seed-host` sẵn có. Chế độ này giữ nguyên HTTP/auth/RBAC/FLS và tắt product background workers để không tự phát sinh nghiệp vụ trong lúc kiểm chứng. **Không chạy `--seed-demo`.**
- Đăng nhập bằng identity demo đã tồn tại và provenance manifest sẵn có. Không tạo account, đổi password, thêm membership hoặc cấp quyền mới. Identity mặc định trong bootstrap config không tồn tại nên không được reseed để ép đăng nhập.
- Các task-owned host đều được dừng sau kiểm chứng. Database đã sửa được giữ nguyên; launch thông thường vẫn dùng cấu hình gốc.

## Ma trận migration

| DbContext / schema | Latest source migration | Latest applied trước sửa | Pending trước | Sau sửa |
|---|---|---|---:|---|
| QuotesDbContext / quotes | `20261102014225_QuoteAutomaticConfirmationIntent` | `20261030150242_QuoteSalesAcceptance` | 1 | ALIGNED, 0 pending |
| OrdersDbContext / orders | `20261102014228_OrderPolicyConfirmationOrigin` | `20261101031438_OrderConfirmationConcurrency` | 1 | ALIGNED, 0 pending |
| PaymentsDbContext / payments | `20261101031438_CurrentOrderPaymentPlan` | Cùng source | 0 | ALIGNED, 0 pending |
| WorkflowsDbContext / workflow | `20261101025942_OrderConfirmationReceipt` | Cùng source | 0 | ALIGNED, 0 pending |

Đã đối chiếu source migration, model snapshot, history riêng từng schema và các cột/index hiện có. `QuoteSalesAcceptance`, `AcceptedQuoteOrderDraft`, `AcceptedQuoteBuyerBinding`, `OrderOperationalConfirmation`, `OrderConfirmationConcurrency`, `OrderPaymentPlanActivation`, `CurrentOrderPaymentPlan` và `OrderConfirmationReceipt` đã có từ trước, không bị chạy lại.

Chỉ áp dụng hai migration đang thiếu bằng **EF Core `database update` với đúng DbContext và target migration**:

1. `20261102014225_QuoteAutomaticConfirmationIntent`: thêm `quotes.SalesAcceptances.AutoConfirmRequested`, `bit NOT NULL DEFAULT 0`.
2. `20261102014228_OrderPolicyConfirmationOrigin`: thêm `orders.Orders.ConfirmationOrigin`, `nvarchar(48) NULL`, và `ConfirmationPolicyVersion`, `nvarchar(80) NULL`.

Forward SQL đã xuất ra `quotes-pending.sql` và `orders-pending.sql`. Chỉ có `ALTER TABLE ADD`, insert EF history và transaction wrapper; không có DROP/TRUNCATE/DELETE/UPDATE/MERGE. Không tạo migration mới, không dùng EnsureCreated và không xóa migration history. Hai file `*-migration-apply.log` ghi kết quả EF thực.

## Backup và bảo toàn dữ liệu

Trước khi migrate đã tạo native SQL backup **COPY_ONLY + CHECKSUM** và chạy **RESTORE VERIFYONLY WITH CHECKSUM: PASS**. Đây là kiểm tra backup, không phải restore/reset database.

Backup được SQL Server lưu tại:

`C:\Program Files\Microsoft SQL Server\MSSQL17.MSSQLSERVER\MSSQL\Backup\UnicoreCRM_Development_quote_order_loading_20261102061310_217452a6.bak`

`backup.json` ghi target, đường dẫn, thời điểm và kết quả verify. Trước apply, script xác nhận lại server/database, đúng hai pending migration, SQL additive và baseline chưa thay đổi.

Baseline ghi row counts và SHA-256 JSON của **toàn bộ cột gốc**, sắp xếp bằng primary key, trên **86 bảng nghiệp vụ/lịch sử**. Cột mới không bị đưa ngược vào phép so sánh cột gốc. Các bảng auth/session, migration history và read-access logging chuyên dụng không được coi là dữ liệu nghiệp vụ bất biến.

Kết quả cuối:

- **84 bảng:** số dòng và hash toàn bảng theo cột gốc giống hệt trước migration.
- `deals.AuditRecords`: 3565 dòng gốc còn nguyên count/hash; nối thêm 20 dòng `listDeals / READ` do kiểm chứng.
- `workspace.AccessRecords`: 2562 dòng gốc còn nguyên count/hash; nối thêm 68 dòng đọc `getWorkspaceBootstrap`/`listMyWorkspaces`. Các lượt kiểm chứng tiếp theo chỉ được phép nối thêm các operation READ đã khai báo.
- Không sửa/xóa audit gốc để ép checksum khớp. Phép so sánh strict ban đầu phát hiện append audit được giữ ở `preservation-strict-after-browser.json`; phép chứng minh append-only đối chiếu lại **đúng count và hash phần gốc**, đồng thời kiểm tra operation/outcome của từng nhóm dòng mới. Không thay baseline và không loại bỏ failed evidence.
- Quote, document/proof, command evidence, outbox, Payment Plan và workflow business tables không mất hoặc đổi dữ liệu gốc. Không tạo Order hoặc acceptance để làm đẹp empty state.

Chi tiết: `original-column-inventory.json`, `business-before.json`, `business-after.json`, `preservation.json`, `columns-after.json`, `indexes-after.json` và `final-preservation.log`.

## Historical compatibility

- SQL hiện có 299 Quote, cả 299 không có SalesAcceptance; **297 Quote có thời điểm tạo trước O1**. Optional LEFT JOIN sau migrate đọc được bình thường, không biến navigation không tồn tại thành required relationship.
- HTTP/Chromium đọc 3 Quote thực trong Workspace demo, mở Quote Detail, quay lại List và đọc lại sau refresh/relogin/restart.
- Toàn database không có Order. Order List trả `items: []`, `totalCount: 0`, HTTP 200 và UI “Chưa có đơn hàng”. Không bịa một Order để kiểm thử Detail; Order Detail và legacy Order row không có Plan không có mẫu thực trên target này. Regression tập trung kiểm tra các owner read/optional-field/security contracts hiện có.
- Không chạy fixture hoặc sửa dữ liệu lịch sử trong Development.

## Connected acceptance và quyền

Chromium headless thật, không điều khiển desktop Windows:

1. Login và chọn đúng Workspace demo.
2. Quote List tải 3 bản ghi thực; mở Quote Detail rồi quay lại.
3. Order List tải empty state đúng SQL.
4. Search và status filter của cả hai module gọi API 200.
5. Đổi page size Quote; next-page UI bị disable đúng vì chỉ có 3 bản ghi. HTTP cursor với `limit=1` đọc trang tiếp theo chứa Quote khác, không giả pagination bằng cache.
6. Refresh và logout/login vẫn đọc được.
7. Dừng/khởi động lại đúng ApiHost riêng; HTTP List vẫn 200. Chromium context mới đăng nhập và thực hiện lại List/Detail/search/filter/refresh/relogin sau restart.
8. Không có page error hoặc HTTP lỗi ở các request Quote/Order sau sửa. Không gọi business mutation để kiểm chứng danh sách.

Security:

- Anonymous Quote/Order List: **401**.
- Workspace thật nhưng không thuộc membership hiện hành: **403** cho cả hai List.
- Field access được đánh giá qua endpoint Backend thật, authority `backend`, đúng trusted Workspace và đầy đủ decision cho status/grandTotal/lineItems/salesAcceptance. Đây là positive field-policy case với identity hiện có.
- Chèn Workspace vào request đánh giá quyền: **422**, không thể tự cung cấp authoritative Workspace fact.
- Negative hidden-field/masking contracts được bao phủ bởi focused Quote/Order/Payment tests. Không giả vờ đã tạo thêm một live masked-field policy trên Development; không sửa quyền để dựng test case.

Evidence: `before/http.json`, `after/http.json`, `after/effective-field-access.json`, `after/browser/result.json`, `after/browser-restart/result.json`.

## Screenshot mới trước/sau

| Trang | Trước migration | Sau migration |
|---|---|---|
| Quote List | `before/browser/initial-quotes.png` | `after/browser/initial-quotes.png` |
| Order List | `before/browser/initial-orders.png` | `after/browser/initial-orders.png` |
| Quote Detail | Không mở từ List lỗi | `after/browser/initial-quotes-detail.png` |
| Sau relogin/restart | — | Thư mục `after/browser/` và `after/browser-restart/` |

Đây là ảnh chụp fresh của cùng source Frontend trước/sau sửa schema thực, không dùng screenshot cũ thay kết quả mới.

## Quality và file thay đổi

| Check | Kết quả |
|---|---|
| Backend build | PASS, 0 warnings/errors |
| Focused Quote/Order/Payment | PASS, 347/347 |
| SQL owner migration status | PASS, 4/4 aligned |
| SQL original data preservation | PASS, 86 bảng với append-only read audit được chứng minh riêng |
| Frontend typecheck | PASS |
| Frontend project lint | PASS; dùng pipeline lint hiện hành của repo, không tuyên bố standalone ESLint vì repo không cấu hình ESLint riêng |
| Production build/bundle budget | PASS |
| API contract | PASS, 365 operations, hash `3b7546319d9284acb3eeeea13fca5b2ff0de3d2ce080578c6859b814b9e5b504` |
| Connected Chromium trước/sau/restart | PASS |
| PowerShell parser / Node script syntax | PASS |

**Không cần sửa source ứng dụng FE/BE, DTO, EF query hay contract. Không có migration mới.** Thay đổi thực tế là áp dụng đúng hai migration tồn tại vào database đích, kèm các file công cụ/báo cáo mới:

- `backend/scripts/repair-quote-order-loading-schema.ps1`: audit, baseline/backup, apply đúng target migration và preservation verification.
- `backend/scripts/verify-quote-order-loading.ps1`: host riêng, login hiện có, HTTP/security/cursor/filter và Chromium/restart.
- `backend/scripts/verify-quote-order-loading-browser.mjs`: browser acceptance và screenshot.
- `backend/docs/verification/quote-order-loading-p0-results.md`: báo cáo này.

86 file thuộc thay đổi Quote/Order trước đó được đối chiếu hash: **0 mismatch**, lưu tại `preceding-worktree-preservation.json`. Working tree chưa commit được giữ nguyên. Full verify toàn repo không được báo PASS; không đổi threshold/pins 60/53, không commit/push/reset/merge và không ghi đè graduation ZIP.

## Acceptance gates

| Gate | Kết quả / phạm vi |
|---|---|
| QUOTE_LIST_HTTP | PASS, 500 → 200 |
| ORDER_LIST_HTTP | PASS, 500 → 200 empty đúng SQL |
| DATABASE_SCHEMA_ALIGNMENT | PASS, đúng 3 cột mới và owner schemas |
| EF_MIGRATION_STATUS | PASS, 0 pending trên 4 DbContext liên quan |
| QUOTE_LIST_CONNECTED | PASS, 3 Quote thực |
| ORDER_LIST_CONNECTED | PASS, empty state thực |
| HISTORICAL_DATA_COMPATIBILITY | PASS với Quote/optional acceptance hiện có; Order legacy Detail N/A vì target không có Order |
| WORKSPACE_ISOLATION | PASS, real 401/403 và field authority; phạm vi negative FLS như đã nêu |
| SQL_DATA_PRESERVATION | PASS, original rows/columns giữ nguyên; chỉ append audit READ hợp lệ |
| RESTART_PERSISTENCE | PASS, HTTP và fresh Chromium sau ApiHost restart |
