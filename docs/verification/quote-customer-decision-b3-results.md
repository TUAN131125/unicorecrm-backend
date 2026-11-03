# Gate B3 — Quote Customer Response & Rejection

Ngày kiểm chứng: 25/10/2026, Asia/Saigon. **B3 PARTIAL_FUNCTIONAL_PASS** theo phương án A người dùng đã duyệt. Reject và USER_REPORTED_ACCEPTANCE/UNVERIFIED chạy bằng Backend/SQL/browser thật. **Verified acceptance, positive ACCEPTED và atomic Quote Accepted → Deal Won vẫn BLOCKED_CONTRACT.** Không mở Gate C.

## A. Baseline

- Backend: `master`, HEAD `13cd39eba01ebc9dc238f8b0ef98e174c1d4d3f6`; Frontend: `feat/deal-compact-kanban-card`, HEAD `814cc4685b6d8f8ead2fc945f4607a2d0168320d`. Cả hai đã có nhiều local changes từ trước; không checkout/reset/commit/merge.
- SQL chỉ dùng local `DESKTOP-ICH225R / UnicoreCRM_Development`. Đầu B3 có 141 Quotes. CRM demo gốc 1.050 bản ghi/180 Deals và Dynamic Pipeline được bảo vệ bằng row hashes.
- Baseline Backend: 209/213 PASS, 4 lỗi architecture tồn tại trước. Baseline strict Frontend: 3/3 PASS; global tsc có 3 diagnostics cũ.
- Entry HEAD/status/file hashes và SHA-256 từng hàng của 26 bảng nằm trong `task-runtime/quote-customer-decision/`; SQL entry nằm trong `task-runtime/quote-lifecycle/b3-entry/`.

## B. Evidence Policy

Authority: quyết định người dùng chọn A trong `docs/development/quote-customer-acceptance-policy-decisions.md` và record kiến trúc `docs/development/quote-customer-decision-architecture.md`.

| Nội dung | Policy đã triển khai |
|---|---|
| Nhân viên thông báo khách đồng ý | Command riêng; USER_REPORTED_ACCEPTANCE; server chỉ cấp UNVERIFIED; Quote vẫn SENT |
| Nhiều phản hồi | Append-only; không ghi đè. Reject sau báo đồng ý giữ cả hai bản ghi |
| Reject | Chỉ SENT, reason bắt buộc 1–500 ký tự, note tùy chọn tối đa 2.000; provenance lời khai nhân viên UNVERIFIED |
| Quyền | quotes.read + quotes.update, current record scope/FLS; không suy ra quyền verifier |
| Policy | Backend cấu hình theo đúng Workspace; version label + semantic hash; thiếu/sai/disabled thì fail-closed, kể cả replay |
| Terminal | Chặn quyết định mới; replay receipt hợp lệ không tạo thêm decision |
| Verified acceptance/Won | BLOCKED_CONTRACT; không có verifier/storage/signer authority được tự chọn |

Configuration key: `QuoteCustomerDecisionPolicies:Workspaces:<workspaceId>`, gồm `Version`, `AllowReportedAcceptance`, `AllowRejection`. Các test dùng process environment và phục hồi khi kết thúc. **Workspace chưa được cấu hình chính sách này vẫn bị chặn; không có fallback tự áp dụng cho mọi Workspace.** Không thêm Studio policy UI trong B3.

## C. Backend Implementation

- Thêm `QuoteCustomerResponseCommands`, `QuoteRejectionCommands`, `QuoteAcceptanceGuard`, policy reader, decision factory và DTO hẹp.
- Route mới: `POST /quotes/{quoteId}/customer-decisions`, operation `recordQuoteCustomerDecisionCommand`. Request chỉ nhận decisionId, evidenceType cố định, channel, sourceDescription, thời điểm khai báo tùy chọn và note. Client không được cung cấp actor/Workspace/Buyer/trust/revision/fingerprint.
- Route Reject hiện hành được triển khai; workflow accept-and-close chỉ có negative guard. Generic accept vẫn BLOCKED.
- Actor/Workspace lấy từ trusted authorization; BuyerRef/revision/content hash lấy từ Quote authoritative; recordedAt từ TimeProvider. Thời gian khai báo phải UTC-Z và không nằm trong tương lai.
- Quote + ledger + immutable command audit + outbox commit cùng owner transaction; SQL application locks bảo vệ aggregate/idempotency/decision identity, kèm If-Match.
- Ledger `quotes.CustomerDecisionRecords` có composite PK Workspace/DecisionId, FK cùng Workspace/Quote, unique resulting version, JSON/type/trust/version checks. Chỉ UNVERIFIED được lưu; EF chặn sửa/xóa evidence.
- Migration additive `QuoteCustomerDecisions` chỉ tạo bảng/checks/FK/index mới; đã áp dụng qua QuotesDbContext factory vào database Development đã xác minh. Không sửa migration đã áp dụng, không cập nhật hàng lịch sử.
- Get detail tải history; list query không join toàn bộ history, giữ pagination/search/workspace scope. Commercial fingerprint B1 giữ nguyên thuật toán; history/lifecycle không tự thay đổi commercial content.

## D. Customer Acceptance

**PASS:** ghi nhận lời khai UNVERIFIED, liên kết actor/Buyer/Workspace/Quote revision/pre-mutation ResourceVersion/commercial fingerprint/policy version, đọc lại lịch sử.

**BLOCKED_CONTRACT:** xác minh khách hàng/signer authority, retained signed evidence/document storage, trusted verification provider, positive ACCEPTED. Empty-body accept-and-close không thể tạo acceptance; client-supplied verification flags bị từ chối. `MarkDealWon` guard không bị thay đổi/bypass. Không tạo Order.

## E. Customer Rejection

Trước: Connected Reject chưa có Backend mutation hoàn chỉnh và bị chặn trên UI. Sau: command typed nhận reason, tăng ResourceVersion, append USER_REPORTED_REJECTION/UNVERIFIED, ghi audit + QUOTE_REJECTED, chuyển REJECTED sau commit. Customer response và delivery cũ còn nguyên. Không ghi Deal, không tự chuyển LOST. Reject SENT không tự thêm điều kiện approval/expiry ngoài authority hiện hành; EXPIRED/terminal không được Reject.

## F. Frontend

- Typed application port → Quote HTTP Adapter → generated client → Backend; read projection giữ customerDecisionHistory và action hints optional.
- Quote Detail có panel riêng **“Ghi nhận khách hàng thông báo đồng ý”**, badge **“Chưa xác minh”**, lịch sử nguồn/người/thời điểm/revision; Quote Status tách biệt.
- Reject có biểu mẫu reason bắt buộc, dùng opening version. Quote chỉ thay đổi theo kết quả Backend, không local fake ACCEPTED/Won.
- Retry giữ identity/idempotency/opening version cho cùng payload; thay payload là attempt mới. Pending ref chặn double-submit. Biểu mẫu đăng ký unsaved work/beforeunload, Cancel yêu cầu xác nhận bỏ thay đổi; không đổi loại quyết định khi đang nhập.
- Kết hợp capability/record access với Backend action hints. Workspace/target được bind; không carry form sang Workspace khác. Demo không giả lập authority của command mới.
- Cập nhật guidance tiếng Việt/Anh. Connected acceptance/Order không được mở thêm.

## G. API / SQL

Evidence root: `task-runtime/quote-customer-decision/`.

| Lượt kiểm chứng cuối | HTTP requests | Kết quả | Thư mục |
|---|---:|---|---|
| Core/B1 assessment/B2 send/B3/replay/version/policy/restart/concurrency | 42 | PASS | `guards-1b67c7623ddb46519f73ace58d345b98` |
| Official provisioning + permission/FLS + read/replay | 15 | PASS | `guards-1058c031dac04c19a09d5efb27b23192` |
| Strict acceptance guard, spoofing, stale, restricted, cross-workspace | 19 | PASS, zero Quote writes | `guards-437d652dfcef4cedba6c387321b06867` |

Mỗi thư mục có cases.json, http-evidence.json/correlation IDs, result.json và preservation.json; có host logs riêng. Full-spec JSON Schema kiểm tra **76/76 HTTP responses PASS**, gồm shared error responses (`response-schema-validation.json`). Browser cũng kiểm tra schema trực tiếp của hai mutation thành công.

Ví dụ final API Quote `quote_483250678903467f95b7b071521692ae`: report làm SENT + 1 UNVERIFIED; Reject làm REJECTED + 2 UNVERIFIED, giữ 1 delivery. SQL ledger chứng minh pre/result ResourceVersion, content fingerprint, policy version và actor. Không có verified promotion.

**Bảo toàn từ đầu B3:** `preservation-final.json` xác nhận 0 changed/missing entry rows trên 26 bảng, gồm 141 Quote + command/outbox cũ, CRM, Pipeline, Workspace/ACL, accounts/credentials. Tổng Quotes 141 → **154** (+13 synthetic). Không thêm Deal, Pipeline, Product, account hoặc credential. B1/B2 fixtures cũ không dùng làm mutation phá hủy.

Fixtures của tất cả lượt chạy, kể cả lần harness chưa đạt: +13 Quotes, +2 Contacts, +4 Workspace/config/quicksetup/bootstrap projections, +4 memberships, +8 roles, +42 role capabilities, +4 role scopes, +4 role field security, +4 role assignments; +60 Quote command records, +56 Quote outbox events. Hai Workspace có membership dùng cho FLS; hai companion isolation không cấp membership. Không xóa fixture khi kiểm tra lỗi.

Provisioning dùng owner Development bootstrap chính thức, chỉ tái sử dụng synthetic identities; IdentityAuth bootstrap tắt và `UNICORE_DEV_SEED_ENABLED=false`, không chạy CRM seeder. Thử nghiệm đầu FLS dùng sai version từ projection nên nhận 412; harness sửa để dùng version từ command receipt, không sửa SQL hoặc nới authorization.

## H. Connected E2E

Playwright Chromium riêng, API thật, không mock. Final **3/3 PASS**:

1. Ghi nhận UNVERIFIED/SENT → refresh → logout/login → Reject có reason → giữ response/delivery history → refresh.
2. Actor thiếu quotes.update mở một Quote SENT riêng: cả hai action bị disabled.
3. Dirty form: không đổi loại quyết định; Cancel/Keep Editing giữ nội dung, Discard cần xác nhận; chuyển Workspace isolation không mang theo form.

Evidence final: `guards-1b67c7623ddb46519f73ace58d345b98/browser/browser-evidence.json`, screenshot `browser/customer-response-history.png`. SQL readback sau browser trong final SQL snapshot; không có API mock/provider delivery/PDF byte verification.

Lượt browser đầu FAIL do evidence listener đọc body sau navigation; đã sửa bộ thu để không phát sinh unhandled rejection khi GET bị hủy, đồng thời vẫn assert status/schema của mutations. Lượt sau 2/2 và lượt cuối 3/3 PASS. Báo cáo không che lần kiểm thử chưa đạt.

## I. Security

PASS: thiếu quotes.update → 403; history READ_ONLY → đọc được nhưng ghi/replay/Reject → 403; HIDDEN → history không có trong response và replay → 403. Thay đổi quyền chỉ trong Workspace synthetic mới qua AccessControl role/member commands; không sửa quyền demo.

PASS: cross-workspace Quote → 404; client-supplied actor/Buyer/Workspace/revision/hash/verification → 422; fake VERIFIED evidence → 422; khai thời gian tương lai → 422. Không biến quotes.update thành verifier authority. Frontend secret policy scan PASS: 2.546 text files. Không lưu credentials/token trong source/evidence; credentials kiểm thử chỉ ở process memory/environment.

## J. Concurrency

- Same idempotency key + same payload: REPLAYED, không thêm ledger/outbox/version.
- Same key + changed payload: 409 IDEMPOTENCY_KEY_REUSED.
- DecisionId cũ + changed payload/Quote: 409 QUOTE_CUSTOMER_DECISION_ID_REUSED; alias key cùng identity/payload bind receipt riêng, không tạo decision thứ hai.
- Stale If-Match: 412 VERSION_CONFLICT.
- Hai decision đồng thời cùng opening version: **1 HTTP 200 + 1 HTTP 412**, SQL **1 decision + 1 event** (`concurrency.json`).
- Missing policy → 503; disabled policy → 409, kể cả replay; restart với policy version mới vẫn đọc được lịch sử/policy version cũ, không nâng trust.

## K. Regression

PASS: 165 Quote-focused tests gồm Core, B1, B2 và B3; fresh API tạo draft/assessment NOT_REQUIRED/send trước B3. Commercial/approval fingerprint và delivery history được bảo toàn. B1/B2 dữ liệu functional đã kiểm chứng từ trước không đổi theo row hashes. Không rerun destructive mutation lên fixtures cũ. Dynamic Pipeline/Deal rows và code MarkWon giữ nguyên; không sửa Registry hoặc mở Quote Lifecycle khác.

## L. Quality

| Check | Kết quả |
|---|---|
| Backend build qua dotnet test | PASS |
| Quote focused | 165/165 PASS |
| Full Backend | 231/235 PASS; **4 baseline failures**, cùng test và cùng diagnostic set như entry; 0 diagnostic mới |
| Strict Frontend | 3/3 PASS |
| Global tsc | 3 diagnostics baseline: nullable model trong deal-quote-transition-outcome.test.mts:42; undefined string trong studio-first-visit.spec.ts:50/51; không có diagnostic B3 |
| OpenAPI / generated artifacts | PASS, 330 operations = 304 ready + 26 blocked; spec SHA bc9c46008722ec62d391496a0d21820714c4fab82d272e952e977901833ede60 |
| Quote API boundary | PASS, 18 operations; thêm kiểm tra request không cho client chỉ định authority/trust |
| Quote approval/delivery contracts | PASS |
| Presentation responsibility, guidance contract/runtime | PASS |
| Routed mutation authority | PASS |
| Secret policy | PASS |
| Production build/bundle budget | PASS; entry 291.38 KiB, largest 495.43 KiB < 500 KiB budget |
| git diff --check, cả hai repo | PASS |
| Independent read-only review | Không phát hiện bypass Backend; finding dirty/pending UI đã sửa và review closure xác nhận |

4 baseline Backend failures: endpoint admission của các module khác, owner mapping/cross-owner edges, durable owner metadata, layer/workflow boundaries. `baseline-failure-comparison.json` xác nhận diagnostic sets giống hoàn toàn. Không sửa gate/allowlist để xanh. Cập nhật expected operation count 329→330 và ready 303→304 là delta một contract đã được người dùng duyệt; 26 blocked giữ nguyên.

Giới hạn: full global suites chưa green do baseline debt. External verified evidence/provider acceptance không chạy vì BLOCKED_CONTRACT. Cần triển khai cấu hình policy cho Workspace đích; hiện chỉ test process có policy B3, thiếu cấu hình vẫn fail-closed. Bundle largest gần budget, chưa vượt ngưỡng.

## M. Gate C Readiness

B3 có immutable unverified ledger, policy binding, rejection và server evidence provenance. **Chưa đủ cho atomic Quote Accepted → Deal Won**: thiếu authoritative signer/consent verifier, retained secure evidence authority và atomic cross-owner participant/Deal concurrency contract. Gate C không được mở; Order/Payment/Invoice/Shipping không mở rộng. Không diễn giải user report hoặc rejection thành purchase commitment.

## N. Git Status / Files

Không commit/merge/reset. Không có entry file bị xóa; file hashes ghi nhận các file ngoài phạm vi B3 không thay đổi. Delta đối chiếu entry tại `backend-file-delta.json` và `frontend-file-delta.json`. Inventory còn thấy file mới `src/components/access/ReadOnlyAccessBanner.tsx` ngoài tác vụ; B3 không tạo/sửa file này, không quy nó vào thay đổi B3 và giữ nguyên.

Các nhóm thay đổi chính:

- Backend `src/UnicoreCRM.Sales/Quotes/`: Domain Quote/ledger; application customer response/rejection/negative acceptance/policy/projection/FLS; strict endpoint/DTO; persistence/DI; một migration mới và model snapshot.
- Backend `tests/UnicoreCRM.ArchitectureTests/`: QuoteRejectionTests và QuoteCustomerDecisionPolicyTests; `scripts/verify-quote-customer-decision*.ps1` tái lập API/SQL/security.
- Frontend `src/modules/quotes/`: application port/read model; domain history; HTTP adapter; public commands; Demo fail-closed; Quote Detail và customer response panel/service. Thêm Playwright config/spec; cập nhật bilingual guidance.
- Frontend `docs/api/`, generated clients/catalog/runtime contracts/backend-readiness policies: sinh deterministic từ một OpenAPI delta. Các generated client khác đổi contract checksum theo spec chung.
- Root authority: operation registry/project extension và exact lookup row/index cho command người dùng đã duyệt; không repin/ghi đè historical baseline hoặc mở generic accept.
- Backend docs: policy decision, architecture record, báo cáo này.

**Verdict cuối: B3 PARTIAL_FUNCTIONAL_PASS; verified customer acceptance và Deal Won integration vẫn BLOCKED_CONTRACT.**
