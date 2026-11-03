# UniCoreCRM — Opportunity & Communications quality acceptance

Ngày 2026-10-28. Verdict hiện tại: **NEEDS_FIX**. Phần lỗi missing estimate đã sửa và được kiểm tra qua HTTP/SQL và Chromium, nhưng race cùng idempotency key có response 500 và UI khóa intent sau validation; fault-injection rollback chưa được thực thi. Communications còn authority gaps. Không tuyên bố Full Quality PASS.

## A. Source & SQL baseline

Backend `master` HEAD `13cd39eba01ebc9dc238f8b0ef98e174c1d4d3f6`; frontend `feat/deal-compact-kanban-card` HEAD `814cc4685b6d8f8ead2fc945f4607a2d0168320d`; cả hai và `.unicore-ai` có dirty work trước task. Evidence mới tại `task-runtime/opportunity-communications-20261028/`: `*-status-before.txt`, `*-hashes-before.txt`, `*-before.patch`. Local SQL `DESKTOP-ICH225R / UnicoreCRM_Development`: snapshot mới 106985 PK rows / 160 tables, gồm 131 migration rows trên 21 schemas. Snapshot theo khóa chính và SHA-256 từng hàng, không bao gồm các bảng không có PK.

## B. Opportunity root cause

`qualifyLeadForOpportunity`, POST `/workflows/lead-qualification/{leadId}/opportunity`, frontend adapter → ApiHost typed request → Atomic coordinator → Leads/Contacts/Tasks/Deals owner participants. Fresh HTTP baseline với request không có ngày/tiền và Lead không có estimate trả 500 `INTERNAL_ERROR`, correlation `completion-2a61e663f3224b3ca89103b1f741f822`. Thực tế Lead participant vẫn có ngày fallback; thiếu effective amount mới kích hoạt guard 500. Guard chạy sau authorization, If-Match/preparation và trước tạo anchor hoặc participants.

## C. Contract decision

Admitted operation là `PRODUCTION_CONTRACT_READY`, `leads.qualify`, If-Match và idempotency bắt buộc; public request `expectedCloseDate`, `estimatedValue` optional. `backend/docs/architecture/CURRENT_IMPLEMENTATION_AUTHORITY.md`, mục OPPORTUNITY qualification quy định request estimate hoặc versioned Lead estimate, request date hoặc versioned Lead UpdatedAt UTC business date +30. Deal yêu cầu effective date và money; không tạo giá trị giả hoặc thay public schema. Currency canonical là 3 ký tự uppercase; negative policy operation-specific, không có căn cứ áp whitelist hay blanket nonnegative.

## D. Implementation

Atomic handler giữ thứ tự authorization, preflight, replay, owner và persistence port; khi effective value còn thiếu, trả existing `LEAD_OPPORTUNITY_INPUT_INVALID` 422 và field errors theo giá trị thiếu. Không có Deal/anchor/Contact/task/outbox/receipt do validation failure. Frontend dùng key có cấu trúc để trình bày copy VI/EN an toàn cho giá trị/ngày/tiền tệ; không render diagnostics cho các fields này, guidance được cập nhật. Frontend tiếp tục giữ frozen intent, hiện chưa cho sửa sau response validation, xem blocker.

## E. Opportunity HTTP/SQL tests

Fresh isolated synthetic Workspace, official Development provisioning/AccessControl owner HTTP; không SQL DML cấp quyền. `opportunity-negative-and-fallback-results.json`, `http-opportunity-first.json`, `browser-opportunity.json`, `concurrency-final.json`, `concurrency-recovery.json`, `negative-amount-result.json` là bằng chứng thực thi:

- Missing request amount + Lead estimate: 422 `LEAD_OPPORTUNITY_INPUT_INVALID`, field `deal.estimatedValue.amount`, correlation; SQL counts anchor, Contact, Task, Deal, owner receipts/outboxes không đổi; Lead version/state không đổi.
- Date sai, rỗng hoặc có timezone; decimal quá 6 chữ số, amount rỗng, currency không đúng syntax: 422 field errors. Unauthorized 403, cross-workspace 403, stale If-Match 412.
- Explicit values: 200 COMMITTED; cùng key/body/version 200 REPLAYED cùng Contact/Deal, counts không đổi; đổi body cùng key 409. Lead estimate và date fallback có xác nhận giá trị và ngày trên Deal read.
- Hai request cùng key đồng thời tạo đúng một anchor, Contact, Deal; một response 200, một response 500 trong SQL 1205 deadlock/rowversion contention. Sau host restart gửi lại cùng key trả 200 REPLAYED, không tạo thêm rows. Không coi concurrency là PASS toàn phần; root cause đầy đủ của response 500 và phương án điều phối cần tiếp tục điều tra.
- Hai request khác key đồng thời trên cùng Lead trả 200 và 422 relationship invalid; tạo một Contact, một Deal, hai anchors cho hai intent (`concurrency-distinct.json`). Không có duplicate Contact/Deal trong lần chạy; anchor của intent bị từ chối vẫn cần được xem xét trong recovery.
- Explicit negative `-1 VND` trên default stage được server chấp nhận 200 trong fixture mới. Contract Money khai `OPERATION_SPECIFIC`, chưa có authority rằng mọi giá trị âm bị cấm; owner cần quyết định trước thay validation. Currency không thuộc danh mục hỗ trợ không được suy đoán từ syntax-only contract.
- Chromium thật hiển thị thông báo missing estimate sau 422; field bị disabled vì intent vẫn frozen. Điều này cản sửa trực tiếp và không nên xóa intent khi partial effects chưa được phân biệt bằng contract.
- Một test-host-only EF fault hook đã được xây dựng, nhưng lệnh tạo và chạy kịch bản fault trước Deal commit bị automated review chặn với lý do duy nhất `blocked by policy`. Không có bằng chứng rollback sau flush hoặc fault recovery; không coi build hook là test PASS. Workflow hiện được phê duyệt là convergent qua owner-local transactions, không phải rollback toàn cục.

## F. Communications architecture

FE adapter/generated client → ApiHost Communications → owner Application services → Communications schema/context và Telegram infrastructure. Nội bộ hiện có 24 operations, 5 conversations + 4 CRM links + 8 channel metadata + 7 Telegram. Provider activation yêu cầu deployment/custody admission; không có provider thật được gọi. No public provider callback hay send/reply API được suy diễn. `communications-audit/audit-findings.md` mô tả claim body-forbidden, scope/FLS, receipts, row locks, Telegram và outbox boundary.

## G. Communications operation matrix

`task-runtime/opportunity-communications-20261028/communications-audit/operation-matrix.md` và `.json` liệt kê từng operation, method/path, handler, schema, capability/scope, idempotency và provider side effects. **0/24 có trong adopted backend canonical operation index; 24/24 implementation tồn tại và frontend extension tuyên bố READY.** Đánh giá backend là `IMPLEMENTED_NOT_ADMITTED` cho 24 operation. Evidence lịch sử `communications-telegram-pilot-readiness-20261017/02_CANONICAL_API_ADMISSION.md` chỉ công nhận frontend convention, không chứng minh sync/freeze backend. Không thay status/admission.

## H. Communications contracts

Custom typed schemas, claim body-forbidden và body-version concurrency đã được audit; các guard giả định mutation body/envelope chung không khớp mọi endpoint. 24 operation thiếu `x-error-codes`; source inventory phân biệt throw sites với provider classifications. Chưa thêm metadata/status tùy đoán. Năm hardening gates vẫn FAIL; error-catalog chung còn FAIL do Quote/Deals blocker/status phân loại chưa chốt. OpenAPI và generated client consistency focused PASS; không ngụ ý backend admission.

## I. Workspace/FLS security

Fresh thực HTTP + SQL: synthetic channel draft trong Workspace A, owner server cấp ID; same key replay 200, different body 409; Workspace B có cùng capability không đọc/ghi A (404), cùng key B tạo identity/receipt riêng, body gửi workspaceId bị từ chối 400; actor thiếu capability 403. Hidden required displayName từ synthetic role bị từ chối cả full read/write 403. `communications-security.json`, `communications-fls.json`, `http-communications.json`, `http-communications-fls.json` ghi correlation và status. Đây là metadata/channel phạm vi hẹp, không phải chứng nhận mọi operation, provider callback, conversation hoặc FLS path.

## J. Owner mapping & dependencies

Ba Communications record fact providers chuyển sang Infrastructure/Persistence; field descriptors giữ ở Application owner metadata; exact query predicates và DI không đổi. Focused 7 tests PASS (SQL query generation intercepted, không gọi SQL thật); independent real HTTP controls ở trên. Layer findings 109 → 103, không còn 6 fact-provider edges. Owner/edge còn 346, endpoint admission 55, contexts 2. CommunicationsDbContext ownership chưa được phê duyệt rõ ở adopted backend authority: implementation có schema `communications` và migrations nhưng ADR-COM-002/003 vẫn chưa approved. Không gán owner từ namespace; AiExecution là blocker riêng. Cross-owner CRM references dùng contracts; remaining Communications Application → own Infrastructure và provider dependency còn phải xử lý.

## K. Quality before/after

Backend fresh baseline 467 PASS / 4 FAIL / 471; full current 474 PASS / 4 FAIL / 478 (7 fact-provider tests mới). Các finding 55 admission, 346 mapping/edges, 2 contexts, 103 layer/workflow. Full build PASS 0 warnings/errors. TRX outputs được giữ tại `backend-trx/`. Frontend fresh focused API contract và strict core PASS; năm Communications hardening và error-catalog FAIL trước thay đổi. Focused frontend qualification boundary, guidance, safe error presentation, strict core và strict regions PASS; inventory regenerated và repo check PASS. Fresh isolated collection đã chạy đủ 328 gate: 319 PASS / 9 FAIL. Năm Communications hardening, error catalog và ba gate ngoài phạm vi là chín failure; log từng gate ở `frontend-gates/`, kết quả `frontend-collection.json`. Build PASS, largest bundle 482.39 KiB / 500 KiB. `npm run verify` chạy riêng FAIL tại gate 49 `quality.repository-cleanup` (14 duplicate groups), 48 PASS / 1 FAIL / 279 NOT_RUN; log `frontend-verify.log`.

## L. Connected regression

Mới: Lead → Opportunity HTTP success, fallback và negative; Lead → nurture → Customer → Customer360 và replay qua API (`lead-customer-regression.json`); browser Customer → Deal → Quote và protected PDF sau host restart (`connected-regression/browser-persistence.json`). Chín fresh negative HTTP controls PASS (`connected-regression/connected-security-results.json`), bao gồm approval/issue authorization, FLS và workspace isolation. Existing HIGH Quote vẫn SENT/APPROVED, requester khác approver, reported decision UNVERIFIED; hai PDF bytes/hash xác nhận. Đây là đọc lại quyết định/phát hành trước đây, không phải phê duyệt mới hoặc bằng chứng provider delivery.

## M. Data preservation

Đầu task 106985 rows; cuối snapshot 108090 rows ở `sql-preservation-final.json` so sánh từng PK và SHA-256. Synthetic A/B, identity/membership chính thức và role owner HTTP được dùng cho fixture; bootstrap CLI lịch sử `--seed-demo` chạy với demo composition và migrations disabled, bật chọn lọc Identity/Workspace owner; không CRM reseed. Original snapshots, migration keys/hashes và source baseline được giữ. Năm screenshot lịch sử có tên Unicode xuất hiện trong Git status đầu task nhưng bước hash đầu task dùng Git path quoting nên không hash được chúng; final hashes có ghi, song không có hash đầu task để chứng minh byte-identical cho năm file này. Không xóa hoặc chỉnh các file đó. `commercial-invariants.json` xác nhận 0 ACCEPTED/WON/Order/Payment trong hai workspace mới. Dữ liệu mới gồm synthetic fixture và audit/session.

## N. Remaining blockers

1. **Opportunity race / UI recovery (Workflow + Leads/Deals + frontend owners):** concurrent same key có 500, sau restart replay đúng; need diagnose deterministic contention and safe failure/recovery. UI error shown nhưng intent bị khóa; cần contract fact chứng minh no-effects trước cho bỏ intent hoặc thiết kế recovery an toàn.
2. **Rollback verification:** task-only host fault injection bị hệ thống chặn; cần cách kiểm thử được phép chứng minh Deal local transaction rollback sau flush và forward recovery, không khẳng định toàn workflow rollback.
3. **Communications authority/contracts:** owner xác nhận exact canonical operation admission, ownership/schema và typed error/status/conditional scopes, sau đó sửa guards với negative controls và tiếp tục layer ports; 5 hardening gates còn FAIL. AiExecution metadata riêng chưa rõ authority.
4. **Money policy:** owner xác nhận negative và unsupported currency theo operation/stage trước mọi thay đổi; hiện request negative được chấp nhận ở default stage.
5. **Outside scope:** historical-evidence `quality.repository-cleanup`, compatibility ceiling 53, Contact visual baseline và cross-owner findings cần quyết định/repair riêng; không xóa evidence hay nâng budget.

## O. Final verdict

`NEEDS_FIX` cho Opportunity end-to-end; `COMMUNICATIONS_PARTIAL_BLOCKED_GOVERNANCE`; Full Quality `NEEDS_FIX`. Các targeted missing-value và Communication channel security cases được nêu riêng không thay thế full acceptance.

## P. Git status / next steps

Không commit/push/merge/reset. Snapshot source và task delta ở task-runtime. Ba ưu tiên: (1) sửa concurrency và safe validation recovery, chứng minh rollback sau flush bằng phương án được phép; (2) chốt canonical Communications owner/admission/error contract rồi sửa phần boundaries còn lại; (3) xử lý quality blockers ngoài phạm vi và chạy lại full suites/canonical verify sau các quyết định.




