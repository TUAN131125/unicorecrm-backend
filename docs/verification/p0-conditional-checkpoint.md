# Connected CRM P0 — conditional functional checkpoint candidate

**Đề xuất: READY_FOR_CONDITIONAL_FUNCTIONAL_CHECKPOINT**, chỉ cho paired frontend/backend và hành trình Login → Workspace → Lead → Contact/Organization → Customer → Task → Activity/History đã kiểm thử. Đây là trạng thái **sẵn sàng để người dùng xem xét điều kiện**, không phải approval, ACCEPTED, compatibility PASS hoặc production-ready.

Không phát hiện regression chức năng mới trong hành trình P0 được xác minh. F01 approval, 42 findings cũ, baseline quality-gate waivers và việc loại Quote mutations chưa có Backend khỏi phạm vi checkpoint **đều chưa được người dùng phê duyệt**. Chưa stage/commit/push/tag và chưa chạy accept-breaking-baseline.

Backend HEAD `7b734f98a7173af2e24e094392826a20b6e11359`; Frontend HEAD `99db1773c03ce4a4b820db7e793da6eca872f862`. Working tree chứa cumulative P0 chưa commit. Manifest phân biệt modified/new source, generated outputs, immutable references và evidence; không lấy toàn bộ untracked user work vào candidate.

## A — Deal/Quote verdict

**Không xác nhận regression F01 “Quote đã commit rồi báo lỗi do SYSTEM”.** F01 đã guard SYSTEM trước command port, helper trả false và caller không coi false là transition failure. Test controller chạy thật xác nhận owner port trả commit acknowledgement, Quote snapshot chuyển REJECTED, Activity port không được gọi; trước sửa task này toast thành công còn thiếu. Đây là lỗi UX tái hiện được, đã sửa tối thiểu.

Thay đổi tại `useDealDetailController.tsx`:

- Quote transition outcome là nguồn quyết định thông báo, không phụ thuộc một manual SYSTEM Activity phía sau. Xóa nhánh Quote-status → SYSTEM Activity vốn không được phép; không chuyển thành NOTE.
- Trạng thái thường có thông báo thành công sau command acknowledgement; lỗi thật vẫn qua error presenter. ACCEPTED giữ workflow command hiện hữu.
- Chặn duplicate trong lúc command pending; trạng thái đã được authoritative Quote snapshot xác nhận thì retry là no-op có thông báo, không phát command lần hai gây version conflict.
- Guidance Deal detail song ngữ/version được cập nhật. Không đổi Quote state machine, command adapter, endpoint hoặc audit model.

`deal-quote-transition-outcome.test.mts` render controller bằng React/JSDOM và gọi **public Quote command/real application projection**, với controlled owner command port. Red trước sửa: committed status REJECTED nhưng toast undefined. Green sau sửa: commit→success, port rejection→error và không commit, retry sau xác nhận không gửi lại, hai submit pending chỉ một command, Activity port calls=0. Test này chứng minh UI/application orchestration, **không phải SQL commit evidence**; emitted audit/event IDs trong controlled port không được coi là audit thật.

### Connected Quote limitation đã thực thi

ApiHost hiện map `GET /quotes` và `GET /quotes/{quoteId}` trong QuotesEndpoints; chưa map Quote mutation routes. Trên DB test riêng và HTTP pipeline thật:

| Probe | Actual | Verdict |
|---|---:|---|
| GET /quotes | 200 | Read route PASS |
| POST /quotes | 405 | Mutation chưa được hỗ trợ |
| POST /quotes/quote_route_probe/reject | 404 | Reject route chưa được hỗ trợ |
| POST /quotes/quote_route_probe/accept | 404 | Accept route chưa được hỗ trợ |
| SQL Quote count trước/sau probes | 0 → 0 | Không có Quote commit |

Do đó “Quote transition commit trên Backend rồi Activity thất bại” **không xảy ra được trong ApiHost hiện tại**, vì Quote command không vào một mutation handler. End-to-end Quote mutation/acceptance: **BLOCKED**, không giả PASS bằng port test hoặc SQL fixture.

SQL schema `quotes` chỉ có Quotes, ReadAuditRecords và migration history. Audit đã ghi nhận listQuotes/READ; đó là bằng chứng đọc, không phải Quote status transition. Không có owner-local mutation producer/outbox được tìm thấy trong Quotes slice. Các audit/event envelope mà FE contract kỳ vọng **chưa đủ để chứng minh Quote mutation trên runtime này**. Đây là baseline capability gap ngoài hành trình P0 trên, phải giải quyết trước khi quảng bá/release Quote mutation. Không triển khai endpoint mới trong task.

## B — Ledger 42 findings

[Ledger Markdown](evidence/p0-checkpoint-finalization-20261021/compatibility-42-ledger.md) và [JSON đầy đủ](evidence/p0-checkpoint-finalization-20261021/compatibility-42-ledger.json) có đúng 42 dòng C01–C42. Mỗi dòng có schema/path, RESPONSE direction, operation/method/route/status mapping, rủi ro client, P0 impact, checkpoint/production disposition, evidence và xử lý đề xuất.

Phân tích leaf schemas xác nhận **42 findings cùng một root change**: ErrorCode response enum thêm 13 giá trị, không xóa giá trị. 12 mã AI_* và LEAD_CONVERSION_IN_PROGRESS lan qua commercial action/credit blocker arrays và các response wrappers. Không phải 42 thay đổi nghiệp vụ riêng.

Rủi ro với client cũ là response validation/exhaustive enum handling từ chối mã mới. Current regenerated client biết các mã này. ErrorCode root map tới 304 operation responses (kể cả ProblemDetails); riêng LEAD_CONVERSION_IN_PROGRESS có thể ảnh hưởng conversion concurrency trong P0 và có predicate recovery trong client hiện tại. Chưa chứng minh AI_* thật sự được phát trong Quote/Order blockerCodes; ledger ghi rõ đây là nguy cơ schema propagation, không bịa runtime occurrence.

Không sửa các wrapper/schema để giảm finding count. Không có row nào được chấp nhận. Code fix trước conditional paired-current-client checkpoint không tự động bắt buộc cho từng wrapper; **review/conditional acknowledgement vẫn bắt buộc**. Trước production phải quyết định consumer inventory, unknown-enum behavior và version/baseline policy.

## C — F01 riêng

[Approval proposal](f01-security-approval-proposal.md): **intentional security contract narrowing — APPROVAL_REQUESTED**. Write từ 6 xuống 5 loại manual; SYSTEM giữ ở read. Before/after, runtime, caller inventory, tests và exact hashes được gắn ở proposal.

Checker thật vẫn trả **43 findings = 42 cũ + 1 F01**. Accepted baseline SHA-256 vẫn `c8e77d670453860796e3c32bcf17bc9ffbefe06bc0a8915d9509338bcabf66ef`; spec SHA-256 vẫn `d894e313d9dad7ae78c0c9bb8fd865f06d2d04546bfbaca3683465780e7e1659`.

Không chạy official `--accept-breaking-baseline`, vì nó nhận cả snapshot. Không triển khai exception/filter/bypass và không sửa checker. Một conditional approval artifact có thể ghi quyết định người dùng nhưng không làm compatibility gate PASS. Version 0.24.1-contract.0 vẫn chỉ là proposal, chưa thay release identity.

## D — Regression và evidence

| Test | Environment / result | Status |
|---|---|---|
| Deal/Quote outcome | Controller + public command/controlled port, Red→Green như trên | PASS trong phạm vi in-process |
| Connected Quote mutation | ApiHost + SQL; 405/404 và không có commit | BLOCKED feature acceptance; route probes PASS |
| Activity Security/ACT-PERF/F01/Customer Task + Quote route probes | Fresh DB `UnicoreCRM_ActivityTest_6de52df30c624eb2a5fda1c1faa2efe2`, 12:01:53–12:03:17 UTC; **580 PASS, 0 FAIL** | PASS |
| Connected regression + route probes | Fresh DB `…b06d50ebf40e46779ba8402eccbc0d14`, 12:05:17–12:06:13 UTC; **128 PASS, 0 FAIL** | PASS |
| UI-only P0 | Fresh DB `…b025ecf180ba470188a776aab066790e`, 12:02:03–12:03:19 UTC; **12/12 steps**, Playwright 1/1; 11 SQL/restart assertions | PASS |
| Typecheck | 3/3 gates; any/as-any/non-null budget 180/15/56 | PASS |
| Build/bundle | 18,23s, largest chunk 487,47 KiB < 500 | PASS |
| Guidance / mutation authority | Focused gates | PASS |
| Generator compatibility | 43 findings, baseline unchanged | FAIL / approval pending |
| Ledger coverage | 42 unique originals, all leaf deltas equal ErrorCode +13/-0, full response operation mappings | PASS |

ApiHost + SQL Server 17.0.1000.7, .NET SDK 10.0.401 và Playwright Chromium thật; GUID DB riêng, không production DB hay InMemory SQL. ACT-PERF regression giữ đúng 2 câu SQL Activity cho COUNT/page trong 107 measured requests. Connected regression kiểm Contact/Organization canonical Customer, Task, Activity quyền OWN/FLS/replay, affiliation history, refresh/login và restart. UI-only không thay thao tác UI bằng API.

Full baseline gates không được rerun để mở rộng scope task; trạng thái gần nhất giữ nguyên, **không chuyển thành PASS**:

- Backend architecture 33 PASS/4 FAIL: layer/owner metadata/durable metadata/endpoint admission; message delta so baseline rỗng.
- Frontend full verify FAIL ở group 10: ContactFormModal↔contactDraftRecovery và TaskCreateModal↔useTaskCreateController cycles.
- Pipeline inventory FAIL 347≠345; repository inventory drift.
- Task create form source gate FAIL vì đọc component mà bỏ controller composition.
- Broad Task/commercial integration FAIL Quote A assertion dòng 214, snapshot trước task cũng FAIL cùng assertion. Test outcome mới chạy riêng, không skip/sửa expected của test cũ.

Các evidence này được pin từ báo cáo trước trong cumulative manifest. Waivers chưa được chấp thuận. New scope chỉ sửa controller/guidance, thêm focused test và thêm route probes trong test harness. Backend production/SQL security implementation không đổi.

## E — Cumulative candidate và quyết định cần duyệt

`cumulative-candidate-manifest.json` là allowlist nguồn duy nhất của proposal: source P0 từ các phase, generated outputs, Tasks index migration/designer/snapshot, test scripts/configs, current source hashes và evidence references. Có Backend/Frontend cumulative patches so HEAD, delta riêng task này, và danh sách source so với phase trước. Không gộp untracked backend-work hay artifact người dùng không thuộc allowlist.

Điều kiện người dùng cần quyết định riêng:

1. F01 security narrowing với exact baseline/spec/source hashes.
2. Có chấp thuận **conditional functional scope chỉ cho paired current repos**, giữ compatibility FAIL, không cam kết historical clients, hay chưa.
3. Review/acknowledge 42 ledger findings riêng; không coi F01 approval là blanket acceptance.
4. Có cấp checkpoint-only waiver cho từng baseline quality failure được liệt kê hay không. Không đồng nghĩa production waiver.
5. Xác nhận Quote mutations chưa có Backend nằm ngoài functional P0 checkpoint; native Quote transition/acceptance vẫn BLOCKED và chặn release tính năng đó.

Không có approval nào ở trên được tự ghi nhận. TEAM vẫn fail-closed theo quyết định đã có; không thêm TEAM, Meeting, History hoặc P1.

## Git proposal và rollback

Sau khi review/approval, đề xuất **hai commit phối hợp**: Backend cumulative P0 + evidence, Frontend cumulative P0 + generated/test files. Manifest giữ cặp base HEAD và candidate hashes, proposed names `connected-crm-p0-conditional-be` / `connected-crm-p0-conditional-fe`; chưa tạo commit/tag. Chỉ stage exact changed-file/evidence allowlist sau khi người dùng cho phép, không git add -A. Immutable reference files không được sửa để làm gate xanh.

Rollback an toàn:

- Nếu chỉ rollback Deal/Quote UI task hiện tại, reverse delta task-only sau khi kiểm tra SHA của file hiện hành; khôi phục snapshot `before` đúng các file đó, giữ P0/F01 security trước task.
- Không reset/checkout toàn working tree hoặc lùi về baseline HEAD dễ tái mở lỗ hổng Activity. Không ghi đè thay đổi người dùng phát sinh sau manifest; phải merge/review nếu hash khác.
- Frontend/backend rollout phải theo cặp manifest. Nếu cần operational rollback toàn P0, phải chọn một **secure checkpoint đã được duyệt**; hiện chưa có checkpoint đó, không tự coi baseline HEAD là target an toàn.
- Tasks migration chỉ thêm index. Rollback UI/code không cần xoá index; giữ SQL data/index và test DB. Không chạy Down/drop database/xóa artifact tự động. Contract baseline không thay nên không có baseline acceptance phải rollback.

Production vẫn bị chặn bởi compatibility/consumer policy, waivers/gate debt, Quote mutation gap, release authority và tải/concurrency qualification chưa đầy đủ. Conditional functional readiness không thay các điều kiện này.

Reproduce từ frontend: chạy `tests/contract/deal-quote-transition-outcome.test.mts` bằng Node/tsx loader theo log, `npm run typecheck`, `npm run build`; từ backend dùng verifier với `-QuoteRouteReview` cùng các flags ghi trong current run logs. Các browser jobs phải tuần tự; mỗi run tạo và giữ DB test riêng.
