# UniCoreCRM — Opportunity intent lifecycle acceptance

Ngày 2026-10-28–29. Functional **NEEDS_FIX**. Lần kiểm thử mới tái hiện HTTP 500 dưới same-key contention; different-key loser vẫn `Started`; UI vẫn không thể sửa sau 422 vì wire contract chưa có chứng cứ no-effects. Không tuyên bố Full Quality PASS. Fault after flush: `NOT_RUN_POLICY_BLOCKED`.

## A. Source & SQL baseline

Backend `master` / `13cd39eba01ebc9dc238f8b0ef98e174c1d4d3f6`; frontend `feat/deal-compact-kanban-card` / `814cc4685b6d8f8ead2fc945f4607a2d0168320d`; local dirty work được bảo toàn. `task-runtime/opportunity-intent-lifecycle-20261028/` chứa status Git NUL-delimited, patch và SHA-256 source JSON; năm screenshot tên Unicode được hash. SQL local `DESKTOP-ICH225R / UnicoreCRM_Development`; entry **110872 rows / 160 PK tables**, snapshot SHA-256 từng row và migration keys. Bảng không có PK không được bao phủ.

## B. Actual intent state model

| State thực tế (Workflows owner) | Durable facts và xử lý |
|---|---|
| No anchor | Trường hợp validation hiện tại trả trước `StartAsync`; không có endpoint/status receipt chứng minh absence chống concurrent admission. Security/read audits có thể tồn tại. |
| Started | Anchor được lưu; Contact có thể đã commit trước khi coordinator ghi progress, nên không suy ra no-effects. Retry cùng intent có thể redrive participant. |
| ContactResolved | Contact receipt đã được ghi nhận; optional Task hoặc Deal có thể đã commit nhưng chưa được ghi trong anchor. |
| TaskCreated | Optional Task được ghi nhận; Deal có thể đã commit trước update anchor. |
| DealCreated | Deal được ghi nhận; Lead close có thể đã commit trước completion. |
| Completed | Lưu response; cùng fingerprint và current actor được authorize có thể REPLAYED. |

State tồn tại sau restart; code chỉ forward, không có trạng thái terminal rejected, failure reason, lease hay public status query cho qualification. Các owner commit riêng: Contact → Task tùy chọn → Deal → Lead; không có distributed rollback. Started khác key loser sau khi Lead đóng không có recovery path được chứng minh.

## C. Same-key pending race

Fresh 10 vòng × 2 requests và 5 vòng × 4 requests cùng key: tất cả 200; một COMMITTED và còn lại REPLAYED từng vòng (`race-final.json`, `race-multi.json`, `race-owner-rows.json`). Tải thực hợp lệ có Task, 8 vòng × 8 requests: 63 HTTP 200, **1 HTTP 500** `INTERNAL_ERROR`; 8 vòng đều chỉ thêm một anchor, Contact, Task, Deal và owner outbox/receipt tương ứng (`race-task.json`, `race-task-check.json`).

Thêm 12 vòng × 8 requests với thời điểm và SQL read ngay sau HTTP: 95 HTTP 200, **1 HTTP 500**, correlation `pending-1-b1f604a5a4954b8a8fd3e2d5f09127f3`; lúc response 500, 7 requests chưa trả lời, 24 ms sau SQL quan sát anchor `Completed`, Contact/Task/Deal và response có đủ (`pending-measure.json`). Điều này tái hiện cửa sổ contention; không chứng minh được chính xác stage ở lần đọc cuối của loser. Không dùng fault injection hoặc mock backend, không có lock graph mới; không khẳng định winner chắc chắn pending tại đúng thời điểm đó.

## D. Concurrency contract & fix

Giữ nguyên sửa đổi đã có: sau ba rowversion conflicts, handler reauthorize, kiểm tra Workspace/fingerprint và đọc lại Completed receipt trước REPLAYED. Không có in-progress result/status query được admitted cho operation; generic 409/503 hiện có không chứng minh semantics pending/retryable. Không tự gán status hay thành công giả. Backend vẫn có thể trả 500 khi không đọc được Completed. `decision-packet.md` trình bày phương án contract hẹp cho owner ratify; chưa triển khai pending contract.

## E. Different-key rejected anchor

Fresh 2 requests khác key cùng Lead trả **422 + 200**, một Contact/Deal, hai anchors: loser `Started` không có Contact/Task/Deal hoặc response; winner `Completed`. Retry loser sau Lead đóng trả **412 VERSION_CONFLICT**, không tạo thêm business rows (`different-key-final.json`, `http-different-key.json`). Anchor lịch sử được bảo toàn. Không có terminal-rejection state hay policy phân biệt no-effect/partial; không tự thêm enum/migration hoặc xóa anchor. Terminal/recovery quyết định còn thiếu.

## F. Authoritative no-effects

Fresh missing-effective-amount request trả **422 LEAD_OPPORTUNITY_INPUT_INVALID** với `deal.estimatedValue.amount`; counts anchor/Contact/Task/Deal/receipts/outbox và Lead state/version không đổi (`missing-final.json`, `http-missing.json`). Đây là một execution path/fixture, không chứng minh mọi 422 hoặc các race đều no-effects. Canonical `ProblemDetails` là closed schema (`additionalProperties:false`), không có field biểu diễn business-effects và không có qualification status query cho negative proof. Đề xuất signal chỉ là proposal trong decision packet; chưa có wire authority.

## G. Frontend recovery

Sửa `leadOpportunityIntent.ts` và controller: refresh cùng authority, generation và Lead giữ frozen key/body/version và draft; đổi authority/generation/Lead không reuse intent. Focused test kiểm tra predicate same scope và ba negative scopes; guidance bilingual version tăng. Predicate được kiểm thử riêng, chưa chạy browser scenario kích hoạt chính refresh effect trong controller. Không mở khóa dựa trên HTTP 422 hay diagnostic text; không tạo key mới cho unknown/partial outcome. Đây là bảo vệ refresh cùng component, chưa chứng minh bảo toàn intent qua unmount/logout/Workspace switch hoặc browser restart; Workspace switching cần authoritative recovery và policy phù hợp.

## H. HTTP/SQL tests

- Same-key 15 vòng cơ bản: SQL kiểm tra từng anchor Completed, Contact conversion receipt, Contact, Deal source Lead và outbox tương ứng; 40 response 200.
- Optional Task 64 requests: một 500; 63 response 200, counts một Contact/Task/Deal mỗi vòng, HTTP và correlation trong `race-task.json`. Sau host restart, retry đúng key/body/version của intent nhận 500 trả REPLAYED cùng Task và không thêm owner rows (`task-500-recovery.json`).
- Sequential replay sau host restart 200 REPLAYED; changed payload 409; same key khác If-Match replay theo contract hiện tại; unauthorized 403, cross-workspace denied (`replay-security.json`).
- Different-key và no-effects như E/F. Không chứng minh exact controlled interleaving winner đang pending hoặc winner failed before completion; không suy diễn từ timing.

## I. Chromium acceptance

Chromium riêng, ApiHost/SQL thật không mock: missing estimate nhận 422, hiện field error; field vẫn disabled (`browser-opportunity.json`). UI-01 edit/resubmit **FAIL**. Pending/unknown, stale-version và Workspace-switch browser scenarios chưa được thực hiện; không tuyên bố PASS. Fresh browser Customer 360 → Deal → Quote, protected PDF read/download PASS; independent approval và SENT/UNVERIFIED được read lại, không phát hành mới (`connected-regression/`).

## J. Owner-local recovery

Restart + same-key replay trả cùng identity; Lead nurture → Customer → Customer360 và replay fresh PASS (`lead-customer-regression.json`). Contact, Task, Deal và Lead commits riêng; anchor progress không đồng nghĩa toàn workflow Completed. Không chứng minh rollback toàn workflow hoặc fault sau flush.

## K. Fault-test limitation

`NOT_RUN_POLICY_BLOCKED`: automatic approval review trước đây đã từ chối test after-flush với lý do `blocked by policy`. Task này không đổi tên, chuyển công cụ hoặc chạy đường khác để vượt chặn. Existing adapter tests dùng intercepted writes chỉ chứng minh logic boundary, không thay thế real SQL rollback.

## L. Security & architecture

Auth/Workspace checked trước replay; unauthorized replay/cross-workspace denied. Chín fresh HTTP checks về FLS/approval/issue/workspace và commercial guards PASS (`connected-security-results.json`). Scope isolation helper từ chối giữ intent qua authority, generation hoặc Lead khác; chưa chứng minh full Workspace switch browser. Không sửa owner allowlist/admission/permissions.

## M. Quality before/after

Backend fresh full baseline **474 PASS / 4 FAIL / 478**; final full **474 PASS / 4 FAIL / 478**, finding counts 55 admission, 346 owner/edges, 2 contexts, 103 layer (không tăng). `npm run verify` cuối FAIL tại gate 49 `quality.repository-cleanup`: 48 PASS / 1 FAIL / 279 NOT_RUN (`frontend-verify-final.json`); lần chạy trước tái hiện inventory drift, sau đó inventory được regenerate và repo check PASS. Frontend tám gate tập trung và repo inventory/check PASS, production build 482.50 KiB / 500 KiB PASS. Frontend historical isolated collection 319/328 không phải fresh full run. Không nới gate hay xóa evidence.

## N. SQL preservation

Entry 110872 PK rows / 160 tables; final 117202 rows, **66 original rows changed, 0 missing, 0 migration changes** (`sql-preservation-final.json`). Có 64 outbox rows hiện PUBLISHED, một outbound consumer cursor và một StudioQuickSetups; `changed-outbox-current.json` ghi trạng thái, 64 outbox có PublishedAt khoảng 22:47 ngày 28/10, giữa entry và lần snapshot sau nửa đêm 29/10. Lần đối chiếu trước khi gián đoạn đã báo 0 thay đổi, nhưng bản snapshot trung gian đã bị ghi đè, nên không thể so sánh byte từng thời điểm. Không có task-owned host log chạy ở thời điểm publish; log host replay cuối lúc 01:06 không có UPDATE các bảng này. Chưa xác định được tiến trình/actor gây thay đổi, không sửa ngược dữ liệu. Dòng thêm gồm fixture Development, session/audit và có thể hoạt động ngoài task; không quy toàn bộ cho fixture. Owner provisioning chính thức dùng CLI tên lịch sử `--seed-demo` với demo composition/migration disabled và selective Identity/Workspace bootstrap; không CRM reseed, không SQL DML quyền. Không tạo Quote ACCEPTED, Deal WON, Order/Payment trong synthetic flows.

## O. Verdict

Functional `NEEDS_FIX`: pending contention, terminal loser và UI confirmed-no-effects chưa đóng. Full Quality chưa PASS. Không có attested independent review/freeze assertion. User-visible decision packet là proposal, không phải approval.

## P. Git / next priorities

Không commit/push/merge/reset/rebase. Ưu tiên tối đa ba việc: (1) xác minh 66 original rows thay đổi và nguồn relay/Studio Quick Setup; (2) owner ratify pending, terminal/recovery và proof wire no-effects; (3) implement UI recovery, kiểm chứng fault-window bằng phương án được phép và chạy lại full regression.




