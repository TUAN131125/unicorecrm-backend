# UniCoreCRM — Full quality stabilization acceptance

Ngày: 2026-10-28. Kết luận: **NEEDS_FIX**. Các quality gate còn lỗi và một trường hợp Opportunity trả HTTP 500 khi dữ liệu ngày/giá trị không có cả trong request lẫn Lead cần được owner xử lý. Không có căn cứ công bố full quality hoặc production readiness.

## A. Source baseline

Backend `master`, HEAD `13cd39eba01ebc9dc238f8b0ef98e174c1d4d3f6`, ahead 1/behind 0 local tracking. Frontend `feat/deal-compact-kanban-card`, HEAD `814cc4685b6d8f8ead2fc945f4607a2d0168320d`, ahead 0/behind 0. Cả hai và repository điều khiển `.unicore-ai` đã dirty trước task; không coi Git HEAD là source ban đầu. Full status, hash và patch đầu task cùng delta hiện tại lưu trong `review-artifacts/full-quality-stabilization-20261028`. SDK .NET 10.0.401; Node v22.23.1; npm 10.9.8.

## B. Quality baseline

Backend fresh baseline 408 PASS / 4 FAIL / 412: endpoint admission 55, owner/edge 549, persistence ownership 15, layer/workflow 136 findings. Frontend chạy độc lập toàn bộ 328 canonical gate: 286 PASS / 42 FAIL; pipeline `verify` có fail-fast. Danh sách và log chính xác nằm tại `frontend-baseline-results.json`, `frontend-all-gates/`, backend baseline log trong evidence directory.

## C. Root-cause analysis

42 dòng gate được gom thành **19 nhóm remediation** trong `root-cause-matrix.md` và `.json`. Chín dòng có sửa product/UI; 33 dòng phân loại chính là guard, contract, metadata hoặc governance. Đây là số dòng gate, không phải chín lỗi sản phẩm độc lập; nguyên nhân có thể chồng lấn. Các nhóm đã xử lý gồm guard sau khi tách controller, Dynamic Pipeline, fixtures sai authority, route smoke, copy/FLS và bảo toàn Demo. Ma trận ghi đủ owner, affected gates, kết quả và remediation từng nhóm.

## D. Frontend contracts & integration

Sửa xóa nhầm dữ liệu Demo khi retire read projection trong `organizationAuthoritativeQueries.ts` và `taskAuthoritativeQueries.ts`: Connected tiếp tục clear authority, Demo giữ repository; positive gates và negative controls tái đưa lỗi cũ đều kiểm chứng. `EffectiveRecordAccessBoundary.tsx` hiển thị notice bilingual theo `showNotice`, không lộ field ẩn. Sửa fixtures identity/workspace, DOM event realm, cập nhật async, picker và source guards theo runtime thực tế; giữ các negative controls. Cập nhật guidance và giao diện để wrap dữ liệu và dùng shared components. Catalog kiểm đối chiếu chính xác 360 operations, không ngụ ý backend admission. `quality.api-contract`, `quality.strict-core`, `quality.guidance-contracts`, build và focused integration tương ứng PASS.

## E. Frontend architecture & route

Route smoke dùng esbuild/JSDOM tải module và CSS thực, kiểm tra exports, 69 workspace routes và 17 Studio routes; kiểm tra thiếu import/export có negative control. Chỉnh guard checkpoint để nhận cả command closure được tách ra; tái hiện negative control lỗi cũ và xác nhận detector mới bắt được. Metadata pipeline khai báo dependency thực tế (`typescript`, `esbuild`, `jsdom`), không giảm gates hoặc ngân sách.

## F. Backend endpoint admission

Không tự cấp admission. Kiểm thử host thực còn 55 endpoint chưa được admission; Communications có 24 operations thiếu metadata lỗi đầy đủ. Năm gate hardening liên quan Communications còn FAIL vì body/result, isolation và aggregate ID chưa được owner chuẩn hóa; cần quyết định owner và contract. API catalog frontend đồng bộ với spec/generated manifest hiện có; điều này không chứng minh mọi endpoint server đã được admission hoặc mọi response metadata hoàn chỉnh.

## G. Backend owner mapping

Bổ sung các mapping module-first và owner edges được xác nhận từ skeleton/registrations; sửa normalize tên Atomic/Durable và phân tích type wrapper Cecil. Kiểm thử regression/deny vẫn giữ. Findings 549 → 346, chưa giải quyết hết; các ownership hỗn hợp Workspace, Communications, AI Execution và cross-owner edge cần owner phê duyệt chính xác, không thêm wildcard.

## H. Persistence ownership

Đăng ký 13 context/schema/migration roots dựa trên authority và DI đã có; 15 → 2 context chưa có owner metadata (CommunicationsDbContext và AiExecutionDbContext). Không tạo migration, schema hoặc quyết định owner cho hai context chưa rõ.

## I. Layer & workflow boundaries

Chuyển hai EF readers Customers sang Infrastructure và options thuần Durable sang Application; so sánh source và review độc lập lưu ở evidence. Bốn handlers Atomic Lead dùng port nội bộ cố định và adapter WorkflowsDbContext scoped; cùng predicate, tracked anchor, lưu progress/outbox, exception identity và retry. 17 kiểm thử adapter/negative controls PASS; full architecture còn 109 findings, không còn Atomic finding; các finding còn lại ở Communications/Integrations. Evidence: `atomic-store-implementation-evidence.md`, `atomic-store-source-delta.patch`, `atomic-store-focused-tests.log`.

## J. Quality before/after

| Quality | Before fresh | After | Verdict |
|---|---:|---:|---|
| Backend full suite | 408 PASS / 4 FAIL | 467 PASS / 4 FAIL / 471 | FAIL, thêm 59 tests; không xóa test |
| Frontend 328 gates | 286 PASS / 42 FAIL | 319 PASS / 9 FAIL | PARTIAL; full isolated run và rerun chính xác dependency metadata |
| Canonical `npm run verify` | FAIL | 48 PASS, gate 49 FAIL, 279 NOT_RUN | FAIL, fail-fast |
| Global TypeScript | PASS | PASS | PASS |
| Production build | PASS | PASS | PASS |
| Bundle | 297.26 KiB fresh | 481.75 KiB / ceiling 500 KiB | PASS, budget không đổi |
| Connected regression | Prior PASS | Browser read/PDF PASS; 9 security HTTP controls PASS; bốn Atomic workflows + replay PASS | giới hạn bên dưới |

Frontend full isolated checkpoint 318/10; sau đó sửa dependency declarations và rerun gate bị ảnh hưởng thành PASS, kết quả reconciliation 319/9. Đây không phải một lần full `verify` hoàn tất. Full backend `backend-final-atomic.log` và TRX là sau Atomic extraction; four failure finding counts lần lượt 55, 346, 2, 109. Build không cảnh báo. Browser read/PDF được chạy lại sau extraction; chín HTTP security controls được chạy trước extraction. 11 lần ghi business form trong task trước là historical evidence, không phải lần chạy mới này.

## K. Security & commercial invariants

Chín HTTP controls thật bao gồm từ chối approve/issue thiếu quyền, FLS ẩn, từ chối PDF/quote và workspace isolation; ID/path/status/correlation lưu `http-security-regression.json`. Fresh browser đọc Customer360 → Deal → hai Quote, PDF protected bytes khớp SQL/hash lịch sử sau restart (`browser-persistence.json`). Quote HIGH vẫn SENT/APPROVED, independent approver khác seller, response UNVERIFIED; xác minh thủ công chỉ về nội dung thương mại, không khẳng định xác minh nguồn hay giao provider. Không tạo ACCEPTED, WON, Order, Payment, hoặc mở B6.5/Gate C.

Trong workspace synthetic C mới, các lệnh nurture, conversion, opportunity, handover đều 200 COMMITTED rồi cùng key/body/version 200 REPLAYED; HTTP/correlation ở `atomic-workflow-regression/http-atomic-regression.json`. Adapter tests dùng interceptor, không chứng minh fault-window trên SQL thật. Lần Opportunity thử đầu thiếu ngày và số tiền trong request/Lead trả 500 trước tạo anchor; sau cung cấp `expectedCloseDate` và `estimatedValue` thì thành công. Schema cho phép omission nhưng handler trả INTERNAL_ERROR nếu không có default Lead: đây là lỗi xử lý input cần owner xác định validation/default, không suy diễn do extraction gây ra. `atomic-workflow-regression/run.log` giữ lỗi; host log giữ chi tiết; run thành công ở `run-retry.log`. Chưa kiểm chứng exhaustively concurrency, changed-payload conflict hoặc fault recovery live.

## L. SQL preservation

Local `DESKTOP-ICH225R / UnicoreCRM_Development`: snapshot theo primary key + SHA-256 toàn hàng, **106087 dòng gốc / 160 bảng, 0 dòng gốc thay đổi hoặc mất**; snapshot cuối 106985 dòng. Dòng mới là synthetic fixture, audit/session và hoạt động HTTP được ủy quyền. Workspace C, identity/membership dùng owner provisioning và AccessControl HTTP chính thức; CLI lịch sử `--seed-demo` chỉ chạy với demo composition disabled, migrations disabled và bật có chọn lọc identity/workspace bootstrap để thêm fixture mới. Không reset/reseed CRM, không SQL DML cấp quyền trực tiếp. `sql-before.json`, `sql-after.json`, `sql-preservation.json` lưu bằng chứng; không suy diễn bảng không có primary key được bao phủ bởi snapshot.

## M. Remaining blockers

| Gate / issue | Root cause, impact, owner and narrow decision |
|---|---|
| `quality.repository-cleanup` | 14 nhóm duplicate của historical evidence; repository tooling owner cần policy giữ bằng chứng tương thích guard. Chưa xóa 31 files lịch sử hoặc nới zero-duplicate gate. |
| `quality.compatibility-retirement` | 60 candidates > ceiling 53; compatibility owners cần inventory/admission và kế hoạch loại đúng consumer, nếu không migration an toàn chưa chứng minh. Không nâng trần. |
| `quality.contact-visual-baseline` | Hash CSS hiện tại khác baseline đã pin, CSS không bị task sửa; shared UI owner cần quyết định visual được duyệt trước thay baseline. |
| `quality.backend-contract-hardening.openapi-lint`, `.no-generic-mutation-payload`, `.no-generic-mutation-result`, `.cross-workspace-denial`, `.aggregate-id-authority` | Communications owner cần xác nhận 24 operations, error codes, claim body forbidden, typed result, isolation và ID authority; không tự cấp READY hoặc exceptions. |
| `quality.backend-contract-hardening.error-catalog` | 34 enum entries còn lẫn HTTP error, blocker details, nhiều status; Quote/Deals owners cần phân loại và schema biểu diễn đúng; đã bổ sung năm Pipeline errors được audit, không đoán status. |
| Four backend architecture tests | 55 admission, 346 owner/edge, 2 contexts, 109 layer findings còn lại; module owners và architecture governance cần chốt ownership/contracts, rồi tiếp tục sửa boundary thực. |
| Opportunity missing effective close date/amount | Request fields optional nhưng thiếu cả request và Lead fallback trả INTERNAL_ERROR 500; Lead/Deal contract owners cần quyết định validation/default truthful và sửa/test. Không tự bịa số tiền hoặc ngày. |

## N. Final verdict

**NEEDS_FIX**. Các cải thiện đã được kiểm chứng nhưng full canonical pipeline chưa PASS, bốn kiểm thử backend chưa được giải quyết và Opportunity có trường hợp input trả 500. Không có production-readiness assertion.

## O. Git status

Không commit/push/merge/reset. Dirty work trước task được giữ; hash/status/delta ở evidence (`source-delta.json`, `*-hashes-before.txt`, `*-hashes-final.txt`). Hai control manifests có byte snapshot đầu task và task delta riêng. Final report là file mới trong backend, được thêm vào final source inventory sau khi viết.

## P. Next priorities

1. Owner Lead/Deal quyết định và sửa trường hợp Opportunity thiếu ngày/số tiền, với test HTTP và recovery SQL tương ứng.
2. Chốt Communications/AI/Workspace ownership, admission, error/isolation contracts; sửa layer boundaries và chạy lại toàn bộ backend + frontend hardening.
3. Phê duyệt phương án bảo toàn historical evidence, retirement compatibility và visual baseline; chạy `npm run verify` đầy đủ và Connected security regression sau mọi thay đổi.
