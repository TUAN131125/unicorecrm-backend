# Gate B5 — Authoritative Quote Document & PDF Issuance

**Verdict: Development Functional PASS.** Phát hành, SQL storage, protected download và Connected browser chạy thật. Full quality/release readiness chưa PASS vì lỗi baseline; không tuyên bố production-ready, legal retention, chữ ký số hoặc provider delivery. Ngày kiểm chứng: 2026-10-25.

## A. Baseline & Git State

Backend `master`, HEAD `13cd39eba01ebc9dc238f8b0ef98e174c1d4d3f6`; Frontend `feat/deal-compact-kanban-card`, HEAD `814cc4685b6d8f8ead2fc945f4607a2d0168320d`. HEAD giữ nguyên. Cả hai repo đã dirty trước B5. Baseline: 218 Quotes, 18 Workspaces, 0 Orders/PaymentPlans; 27 bảng có hash từng dòng gốc, gồm 192 Deals và 17 Pipelines/89 Stages. Hash bao phủ dữ liệu demo 1.050 bản ghi và các fixtures/checkpoint có sẵn; số tổng trong baseline có thêm fixtures cũ.

Kết quả cuối: **0 dòng gốc thay đổi hoặc mất**; toàn bộ 218 Quote cũ, membership/quyền cũ, IAM Account/Credential và Pipeline cũ giữ nguyên. Không có file baseline mới bị mất; deletion Frontend đã tồn tại trước B5 được giữ nguyên. [SQL preservation](D:/Project_All/UnicoreCRM/task-runtime/quote-document-b5/sql-final-preservation.json), [source/HEAD preservation](D:/Project_All/UnicoreCRM/task-runtime/quote-document-b5/source-final-preservation.json).

## B. Document Issuance Policy

Áp dụng gói A đã được người dùng điều chỉnh/phê duyệt: `quotes.read` + `quotes.issueDocument` riêng; chỉ DRAFT/REVIEW/SENT chưa archive, còn hiệu lực hết ngày theo timezone Workspace, current B1 NOT_REQUIRED hoặc APPROVED độc lập khớp commercial fingerprint/policy. PENDING, approval stale, policy thiếu/sai, currency disabled, thiếu timezone/seller/buyer authority đều fail-closed. Không FX hoặc lấy giá Product mới.

Mỗi semantic binding commercial/seller/buyer/template/document-policy/approval-policy có tối đa một Document. Binding mới cấp identity/version mới. Replay trả receipt/Document cũ sau authorization hiện hành, không render lại. Issue chỉ tăng ResourceVersion một lần; không tăng QuoteRevision hay đổi status/Delivery/Customer evidence. Retention vô thời hạn trong B5 là giới hạn kỹ thuật; không phải chính sách pháp lý đã hoàn thiện. [Policy decisions](D:/Project_All/UnicoreCRM/backend/docs/development/quote-document-issuance-policy-decisions.md).

Để bật tại Workspace vận hành cần **cấu hình Backend versioned policy cho chính Workspace**, grant capability qua AccessControl và cấu hình Seller qua Studio. B5 không tự cấp quyền hoặc bật policy cho 18 Workspace cũ. Verification hosts dùng policy scope chính xác và biến môi trường chỉ trong process, phục hồi khi kết thúc.

## C. Seller Authority

Workspace sở hữu narrow `IQuoteSellerReader`/lease; Quote không đọc Workspace tables. Tái sử dụng `PATCH /workspace-configuration/business-information` và giao diện Studio hiện có, bổ sung readiness guidance và kiểm tra address ID trùng. Issuer không có Studio, quản trị viên synthetic không có issueDocument.

Bắt buộc legal/display name, đúng một active REGISTERED address, email hoặc điện thoại hợp lệ. Tax ID không tự tạo; không backfill thông tin pháp lý cho 18 Workspace gốc. Lấy revision đã lưu dù publication flag DRAFT, không nói đã publish. Seller lease serializable giữ capture qua Quote commit. Logo tùy chọn; nếu cấu hình mà thiếu trusted asset contract thì **BLOCKED_CONTRACT_LOGO_ASSET**. Không tải URL logo tùy ý.

Files: [QuoteSellerReader.cs](D:/Project_All/UnicoreCRM/backend/src/UnicoreCRM.Platform/Workspace/Contracts/QuoteSellerReader.cs), [EfQuoteSellerReader.cs](D:/Project_All/UnicoreCRM/backend/src/UnicoreCRM.Platform/Workspace/Infrastructure/Persistence/EfQuoteSellerReader.cs). Buyer name/contact cũng qua narrow Contact/Organization owner lease, có scope/FLS, không truy cập foreign persistence từ Quote.

## D. Document Domain

`QuoteDocument` + `QuoteDocumentContent` immutable, identity riêng, root/revision, document ordinal, source/result ResourceVersion, snapshot/field manifest, seller version, approval/document policy/template versions, actor/server time, SHA256/size/pages. Không đưa storage path/raw snapshot ra response. Immutable guard từ chối update/delete qua DbContext; FK restrict và unique binding/ordinal trong SQL. Bản revision mới không kế thừa documents/history; bản cũ không bị sửa.

Core: [QuoteDocumentCommands.cs](D:/Project_All/UnicoreCRM/backend/src/UnicoreCRM.Sales/Quotes/Application/Common/QuoteDocumentCommands.cs), [QuoteDocument.cs](D:/Project_All/UnicoreCRM/backend/src/UnicoreCRM.Sales/Quotes/Domain/QuoteDocument.cs).

## E. PDF Generation

Server worker .NET10, **PDFsharp-MigraDoc/PDFsharp 6.2.4 MIT** pin riêng, embedded NotoSans Regular/Bold OFL1.1 có SHA/version và notices. Helper ở `backend/scripts/QuotePdfWorker`, không thêm product module thứ15. Không HTML/DOM/browser renderer/network/font lookup từ người dùng.

PDF dùng stored commercial values, giữ unit-price precision (USD `0.004 × 1000 = 4.00`), totals theo VND/JPY0, USD/EUR2, KWD3. Có seller/buyer, Quote number/revision, hiệu lực/timezone, thuế/giảm giá/phí, billing cycle, payment agreement gồm fulfillment/preferred/channel/invoice policy, notes và document identity. Thiếu historical unit hiển thị “— (chưa ghi nhận)”. Không backfill.

Actual issued PDF **15 trang/24 dòng**, mọi end marker mô tả dài và stored monetary totals đều có trong extraction; đã xem ảnh trang đầu/cuối, header/footer/table không mất hàng hay tràn mép. [Extraction acceptance](D:/Project_All/UnicoreCRM/task-runtime/quote-document-b5/pdf-extraction-acceptance.json), [Issued multipage PDF](D:/Project_All/UnicoreCRM/task-runtime/quote-document-b5/api-3f489a4dca704f8f96cae46f3a59c144/multipage-issued.pdf), [Page1](D:/Project_All/UnicoreCRM/task-runtime/quote-document-b5/api-3f489a4dca704f8f96cae46f3a59c144/page-01.png), [Page15](D:/Project_All/UnicoreCRM/task-runtime/quote-document-b5/api-3f489a4dca704f8f96cae46f3a59c144/page-15.png). Published worker cũng chạy được sau publish; riêng bài packaging dùng renderer-only input, không coi là issued/Connected evidence.

## F. Storage

Migration additive `20261025083644_QuoteIssuedDocuments`, chỉ thêm hai bảng trong `quotes`; đã kiểm tra/applied đúng local `DESKTOP-ICH225R/UnicoreCRM_Development`, không reset hoặc sửa migration cũ. Bytes/metadata/Quote version/receipt/audit/outbox commit cùng transaction. Download đọc stored bytes, kiểm size/hash trước trả200, không render lại.

Hiện 21 documents, 1,091,927 PDF bytes, **0 integrity mismatch**; size 49,687–92,358, tối đa15 trang. [sql-final-preservation.json](D:/Project_All/UnicoreCRM/task-runtime/quote-document-b5/sql-final-preservation.json). Revision, Seller/policy change và archive giữ file cũ. Không delete/overwrite/restore endpoints.

Host restart/login/download chứng minh SQL persistence, **không phải backup restore**. Read-only msdb ghi nhận một full backup 2026-10-18, trước B5; chưa có bằng chứng backup chứa document mới hoặc restore thành công. Restore/DR test **NOT_RUN**, không tạo bản backup chứa IAM credentials trong evidence. Indefinite retention và SQL/log growth cần sizing/backup vận hành.

## G. Backend APIs

| Method | Route | Authority |
|---|---|---|
| POST | `/quotes/{quoteId}/documents/issue` | read + issue, If-Match, Idempotency-Key |
| GET | `/quotes/{quoteId}/documents` | read, cursor/limit bounded |
| GET | `/quotes/{quoteId}/documents/{documentId}` | current record/source/FLS |
| GET | `/quotes/{quoteId}/documents/{documentId}/content` | same checks, exact stored PDF |
| GET | `/quotes/document-seller` | narrow seller readiness, quotes.read |

Strict Issue body chỉ optional reason; từ chối fake Seller/DocumentId/hash/path/provider claims. Body gồm chunked input giới hạn8KiB. Download PDF có filename Backend, no-store/private, nosniff và hash header. Replay ETag dùng current Quote version; metadata vẫn giữ source/result versions nguyên gốc.

Đã đăng ký project extension +5 operation admissions/owner edges, OpenAPI/client regenerated **335 operations: 309 ready/26 blocked**. Exact operation hashes khớp. Không repin frozen canonical baseline hoặc tự ký attestation. Legacy bounded formal operation verifier chưa hỗ trợ extension mới: formal attestation **BLOCKED_TOOL_COVERAGE**, được phân biệt với runtime/API acceptance.

## H. Frontend

Trước B5, Connected builder trả BLOCKED_CONTRACT; demo PDF dùng BrowserInvoiceConfigurationRepository/DOM export. Hiện saved Quote Detail có content preview, Issue, history, View/Download riêng; chỉ Demo còn utility cũ. Builder hướng dẫn lưu rồi mở Detail, không tuyên bố PDF đã phát hành.

`QuoteDocumentPanel`, preview, query/command ports và HTTP adapter dùng Backend; Blob decode chỉ cho operation được generated contract thừa nhận, có bounded streaming/header/media validation + SHA từ downloaded bytes. JSON auth/errors/retry/workspace protections giữ nguyên. Chuyển Workspace abort/clear previous state. Không đưa generated DTO vào domain hoặc tự tính lại tiền trong issued file.

Root cause phát hiện trong Connected acceptance: Workspace bootstrap OpenAPI chỉ nhận lowercase capabilities, từ chối `quotes.issueDocument` dù HTTP200. Đã bổ sung đúng capability approved vào pattern và regenerate; không bỏ validation/mở rộng quyền. Hai lỗi renderer precision/omitted terms và một invalid-options500 được review độc lập phát hiện, sửa và kiểm chứng.

## I. API/SQL/Storage Evidence

Các suite thật: main30 requests, advanced72, layout10, policy11; browser runner còn có protected API/SQL readback. Không mock Backend/SQL trong các suite Connected.

- [Main30](D:/Project_All/UnicoreCRM/task-runtime/quote-document-b5/api-ac867af8b83b4771ac4e6f37f0f58de8/result.json): missing Seller/approval, permissions, forged/stale request, issue/replay/key reuse, Seller change/new binding, paging, archive, restart và SQL hash.
- [Advanced72](D:/Project_All/UnicoreCRM/task-runtime/quote-document-b5/api-51e08c5101064ecd8d4b3cacdf09012f/result.json): races,5currencies, pending/invalidated approval, expiry, revision, cross-Workspace/FLS, rollback/retry/render failure.
- [Layout10](D:/Project_All/UnicoreCRM/task-runtime/quote-document-b5/api-3f489a4dca704f8f96cae46f3a59c144/result.json): actual15-page PDF và linked Deal row hash.
- [Policy11](D:/Project_All/UnicoreCRM/task-runtime/quote-document-b5/api-0d0b12444c1c45e49c3e09f8ebccde9f/result.json): approval policy change, stable authorized replay, new binding, invalid bool B5 config → Quote GET200, issue409, historical download200.

Fixture ledger: **26 Workspace mới**, gồm companion isolation và các browser attempts bị lỗi trước khi fix; **33 Quotes,12 Contacts,1 Deal** mới, đều synthetic Development. **0 Account/Credential mới**: tái sử dụng ba tài khoản synthetic đã được phê duyệt, chỉ provision membership/role tại Workspace mới. Failed-attempt fixtures được giữ, không cleanup/reset. Toàn bộ keys/IDs/counts: [fixture-ledger.json](D:/Project_All/UnicoreCRM/task-runtime/quote-document-b5/fixture-ledger.json). Không thay đổi membership/quyền Workspace demo/B1–B4.

## J. Browser E2E

Dedicated Playwright Chromium, no API mocks, no Windows desktop takeover: **4/4 PASS** trong ba phase.

1. Admin Studio lưu synthetic Seller; issuer không có Studio mở Quote, Issue201, list/view/download, refresh/logout/login.
2. Stop/start Backend; login/download cùng identity/hash, Quote còn DRAFT.
3. Read-only không thể issue; issuer bị chặn Studio.
4. Sau **explicit synthetic B2 user-confirmed send** riêng, browser revise; new Draft không có documents, source revision/file vẫn còn; source full-row hash không đổi qua revise/download.

Document browser: `qdoc_1d9cfa628e854ba5b02992e0d145cc3f`, source/result RV `2/3`, `50323` bytes, SHA256 `140E8C0B8EA56A7C930E7BBE6B9CE1BA7955484F1D29D29A85EBA60A0DF99C3B`. [Issued proof](D:/Project_All/UnicoreCRM/task-runtime/quote-document-b5/api-c760ca5a0c734a128cc22329955c7446/browser/issued-proof.json), [Restart proof](D:/Project_All/UnicoreCRM/task-runtime/quote-document-b5/api-c760ca5a0c734a128cc22329955c7446/browser/restart-proof.json), [History proof](D:/Project_All/UnicoreCRM/task-runtime/quote-document-b5/api-c760ca5a0c734a128cc22329955c7446/browser/history-proof.json), [Studio screenshot](D:/Project_All/UnicoreCRM/task-runtime/quote-document-b5/api-c760ca5a0c734a128cc22329955c7446/browser/studio-seller.png), [Quote screenshot](D:/Project_All/UnicoreCRM/task-runtime/quote-document-b5/api-c760ca5a0c734a128cc22329955c7446/browser/quote-issued.png), [Browser log](D:/Project_All/UnicoreCRM/task-runtime/quote-document-b5/browser-final-history.log).

## K. Security

Issue requires explicit capability, read-only403; valid read-only download200 chỉ khi đủ record/source/FLS. Cross-Workspace Issue/metadata/download404; hidden grandTotal metadata/download403. Historical file không bị re-render/redact để vượt FLS. Buyer authority kiểm tra owner permission/scope/fields cả khi replay/download. No Studio privilege escalation/default grants.

`security:scan` PASS; B5 artifact scan không còn synthetic credential/JWT candidates. Playwright trace/video tắt; error-context có nguy cơ lưu password nên runner redact trước xuất evidence. Một bản copy local settings do publish mặc định đã được loại bỏ; project hiện loại `appsettings.Development.Local.json` khỏi publish, đã kiểm chứng absent. Không ghi secret/token vào source hoặc báo cáo. [evidence-secret-scan.json](D:/Project_All/UnicoreCRM/task-runtime/quote-document-b5/evidence-secret-scan.json), [frontend-security-final.log](D:/Project_All/UnicoreCRM/task-runtime/quote-document-b5/frontend-security-final.log).

## L. Concurrency

SQL aggregate lock dùng chung với Quote commands + unique binding/ordinal. Different-key concurrent issuance: một201/một412. Same-key concurrent: một COMMITTED/một REPLAYED, cùng DocumentId. Issue/edit: một mutation thành công/một412; không mixed snapshot. Current seller/buyer captures giữ owner leases đến commit.

Development-only exact-Workspace commit probe gây lỗi **sau SQL flush/trước commit**: HTTP500; SQL RV không đổi,0Documents/Receipts/Events. Tắt fault và retry cùng key →201, downloadable exact bytes. Đây là deliberate Development fault injection trên SQL thật, không mock storage/false failure. Oversized actual render trả409 RENDER_* và0partialDoc/version change. Production không đăng ký fault probe.

## M. Regression

Full Backend suite: **276 PASS/4 baseline FAIL/280** so với entry268/4/272; thêm8 focused tests. Bốn failure names và từng dòng diagnostic **khớp baseline, không thêm/bớt** sau khi khai báo exact new owner namespaces. [backend-baseline-comparison.json](D:/Project_All/UnicoreCRM/task-runtime/quote-document-b5/backend-baseline-comparison.json), [backend-final-tests.log](D:/Project_All/UnicoreCRM/task-runtime/quote-document-b5/backend-final-tests.log).

Quote Core/B1–B4 tests trong suite, frontend Quote typed paging/projection/empty/error/retry/workspace race, approval/delivery, lifecycle browser revision/history, direct-sale/order-integrity đều PASS ở phạm vi checks đã chạy. Không tuyên bố rerun toàn bộ Connected browser suite cũ; original evidence/data bảo toàn bằng row/source hashes. Linked Deal giữ full-row hash, Pipeline/Stage không đổi. Final SQL toàn hệ thống: **0 ACCEPTED/0 WON/0 Orders/0 PaymentPlans**. Test B2 chuyển SENT được gọi riêng có chủ đích; Issue/Download không tự làm việc đó.

## N. Performance

Local committed render durations: min265ms/avg558ms/max1458ms, maximum sampled worker working set55,959,552bytes cho21commits tại thời điểm capture. Đó là thời gian renderer, không phải end-to-end p95/production SLA. SQL Documents reserved491,520bytes, DocumentContents1,392,640bytes. [performance-sql.json](D:/Project_All/UnicoreCRM/task-runtime/quote-document-b5/performance-sql.json).

Limits:2slots,15sec timeout/kill,8MiB input,10MiB PDF,100pages,500lines,128MiB managed heap. **256MiB working-set check được lấy mẫu25ms, không phải hard OS process cap**. Production scale/cancellation storm/storage growth/backup load chưa benchmark. Frontend build PASS: entry293.89KiB, largest497.35KiB <500KiB; không tăng budget. [frontend-build-final.log](D:/Project_All/UnicoreCRM/task-runtime/quote-document-b5/frontend-build-final.log), [published-worker-verification.json](D:/Project_All/UnicoreCRM/task-runtime/quote-document-b5/published-worker-verification.json).

## O. Quality

PASS: Backend build0warnings/errors; additive migration; focused8 tests; API/SQL/real PDF/browser; strict-core/type-regions; HTTP foundation gồm binary transport negative controls; API335-operation contract; Quote boundary, approvals/delivery, guidance, presentation, mutation authority/conflict, capability manifest, frontend/backend separation, Quotes/unit/direct-sale/order-integrity, enterprise security, security scan, repository inventory/check, production bundle budget, worker publish/run.

**FAIL baseline**: global Frontend tsc còn3 lỗi (`deal-quote-transition-outcome` nullable model, `studio-first-visit`2 optional strings), nên full `npm verify` dừng ở lint. `quality.backend-readiness` còn document-status coverage thiếu các tài liệu cũ (Workspace Pipeline và Studio baseline); không nới gate để PASS. Full Backend4 architecture/admission/context/owner baseline failures giữ nguyên. **BLOCKED** optional provider-contract gate thiếu declared network/backend-provider repository dependencies trong generic runner; real local connected scripts đã chạy riêng. Formal legacy verifier coverage nêu ở G. [frontend-global-typecheck-final.log](D:/Project_All/UnicoreCRM/task-runtime/quote-document-b5/frontend-global-typecheck-final.log), [frontend-verify-final.log](D:/Project_All/UnicoreCRM/task-runtime/quote-document-b5/frontend-verify-final.log), [quality.backend-readiness.log](D:/Project_All/UnicoreCRM/task-runtime/quote-document-b5/quality.backend-readiness.log).

Independent read-only final review: không còn material blocker trong deltas đã review; các suite acceptance do primary writer chạy, reviewer không tự rerun. Baseline gates chưa xanh được giữ nguyên, không gộp vào Functional verdict.

### B5-01…34

| ID | Case | Status | Evidence |
|---|---|---|---|
| B5-01 | Valid Quote issue | PASS | [cases.json](D:/Project_All/UnicoreCRM/task-runtime/quote-document-b5/api-ac867af8b83b4771ac4e6f37f0f58de8/cases.json) |
| B5-02 | Missing approval | PASS | [cases.json](D:/Project_All/UnicoreCRM/task-runtime/quote-document-b5/api-51e08c5101064ecd8d4b3cacdf09012f/cases.json) |
| B5-03 | Invalidated approval | PASS | [cases.json](D:/Project_All/UnicoreCRM/task-runtime/quote-document-b5/api-51e08c5101064ecd8d4b3cacdf09012f/cases.json) |
| B5-04 | Missing Seller | PASS | [cases.json](D:/Project_All/UnicoreCRM/task-runtime/quote-document-b5/api-ac867af8b83b4771ac4e6f37f0f58de8/cases.json) |
| B5-05 | Foreign Seller / Workspace | PASS | [cases.json](D:/Project_All/UnicoreCRM/task-runtime/quote-document-b5/api-51e08c5101064ecd8d4b3cacdf09012f/cases.json) |
| B5-06 | Invalid/archived state | PASS | [cases.json](D:/Project_All/UnicoreCRM/task-runtime/quote-document-b5/api-ac867af8b83b4771ac4e6f37f0f58de8/cases.json) |
| B5-07 | Stale If-Match | PASS | [cases.json](D:/Project_All/UnicoreCRM/task-runtime/quote-document-b5/api-ac867af8b83b4771ac4e6f37f0f58de8/cases.json) |
| B5-08 | Same-key replay | PASS | [cases.json](D:/Project_All/UnicoreCRM/task-runtime/quote-document-b5/api-ac867af8b83b4771ac4e6f37f0f58de8/cases.json) |
| B5-09 | Reused key / changed payload | PASS | [cases.json](D:/Project_All/UnicoreCRM/task-runtime/quote-document-b5/api-ac867af8b83b4771ac4e6f37f0f58de8/cases.json) |
| B5-10 | Concurrent issue | PASS | [cases.json](D:/Project_All/UnicoreCRM/task-runtime/quote-document-b5/api-51e08c5101064ecd8d4b3cacdf09012f/cases.json) |
| B5-11 | Issue/edit race | PASS | [cases.json](D:/Project_All/UnicoreCRM/task-runtime/quote-document-b5/api-51e08c5101064ecd8d4b3cacdf09012f/cases.json) |
| B5-12 | Multi-line PDF | PASS | [multipage-proof.json](D:/Project_All/UnicoreCRM/task-runtime/quote-document-b5/api-3f489a4dca704f8f96cae46f3a59c144/multipage-proof.json) |
| B5-13 | Currency precision | PASS | [pdf-extraction-acceptance.json](D:/Project_All/UnicoreCRM/task-runtime/quote-document-b5/pdf-extraction-acceptance.json) |
| B5-14 | Tax/discount/fees | PASS | [pdf-extraction-acceptance.json](D:/Project_All/UnicoreCRM/task-runtime/quote-document-b5/pdf-extraction-acceptance.json) |
| B5-15 | Vietnamese Unicode | PASS | [pdf-extraction-acceptance.json](D:/Project_All/UnicoreCRM/task-runtime/quote-document-b5/pdf-extraction-acceptance.json) |
| B5-16 | Multi-page PDF | PASS | [page-15.png](D:/Project_All/UnicoreCRM/task-runtime/quote-document-b5/api-3f489a4dca704f8f96cae46f3a59c144/page-15.png) |
| B5-17 | Long descriptions | PASS | [pdf-extraction-acceptance.json](D:/Project_All/UnicoreCRM/task-runtime/quote-document-b5/pdf-extraction-acceptance.json) |
| B5-18 | Exact download bytes | PASS | [documents-sql.json](D:/Project_All/UnicoreCRM/task-runtime/quote-document-b5/api-ac867af8b83b4771ac4e6f37f0f58de8/documents-sql.json) |
| B5-19 | SHA256 matches SQL | PASS | [documents-sql.json](D:/Project_All/UnicoreCRM/task-runtime/quote-document-b5/api-ac867af8b83b4771ac4e6f37f0f58de8/documents-sql.json) |
| B5-20 | Backend restart | PASS | [restart-proof.json](D:/Project_All/UnicoreCRM/task-runtime/quote-document-b5/api-c760ca5a0c734a128cc22329955c7446/browser/restart-proof.json) |
| B5-21 | Revision preserves old PDF | PASS | [history-proof.json](D:/Project_All/UnicoreCRM/task-runtime/quote-document-b5/api-c760ca5a0c734a128cc22329955c7446/browser/history-proof.json) |
| B5-22 | Archive retains PDF | PASS | [cases.json](D:/Project_All/UnicoreCRM/task-runtime/quote-document-b5/api-ac867af8b83b4771ac4e6f37f0f58de8/cases.json) |
| B5-23 | Restricted issuer | PASS | [cases.json](D:/Project_All/UnicoreCRM/task-runtime/quote-document-b5/api-ac867af8b83b4771ac4e6f37f0f58de8/cases.json) |
| B5-24 | Cross-Workspace isolation | PASS | [cases.json](D:/Project_All/UnicoreCRM/task-runtime/quote-document-b5/api-51e08c5101064ecd8d4b3cacdf09012f/cases.json) |
| B5-25 | FLS denies indivisible PDF | PASS | [cases.json](D:/Project_All/UnicoreCRM/task-runtime/quote-document-b5/api-51e08c5101064ecd8d4b3cacdf09012f/cases.json) |
| B5-26 | Fake document identity | PASS | [cases.json](D:/Project_All/UnicoreCRM/task-runtime/quote-document-b5/api-51e08c5101064ecd8d4b3cacdf09012f/cases.json) |
| B5-27 | Injected storage/hash/path | PASS | [cases.json](D:/Project_All/UnicoreCRM/task-runtime/quote-document-b5/api-51e08c5101064ecd8d4b3cacdf09012f/cases.json) |
| B5-28 | Post-flush storage failure | PASS | [storage-failure-sql.json](D:/Project_All/UnicoreCRM/task-runtime/quote-document-b5/api-51e08c5101064ecd8d4b3cacdf09012f/storage-failure-sql.json) |
| B5-29 | Retry after rollback | PASS | [cases.json](D:/Project_All/UnicoreCRM/task-runtime/quote-document-b5/api-51e08c5101064ecd8d4b3cacdf09012f/cases.json) |
| B5-30 | Download does not Send | PASS | [browser-sql.json](D:/Project_All/UnicoreCRM/task-runtime/quote-document-b5/api-c760ca5a0c734a128cc22329955c7446/browser-sql.json) |
| B5-31 | No implicit ACCEPTED | PASS | [sql-final-preservation.json](D:/Project_All/UnicoreCRM/task-runtime/quote-document-b5/sql-final-preservation.json) |
| B5-32 | No implicit Won | PASS | [linked-deal-proof.json](D:/Project_All/UnicoreCRM/task-runtime/quote-document-b5/api-3f489a4dca704f8f96cae46f3a59c144/linked-deal-proof.json) |
| B5-33 | Audit/outbox | PASS | [documents-sql.json](D:/Project_All/UnicoreCRM/task-runtime/quote-document-b5/api-ac867af8b83b4771ac4e6f37f0f58de8/documents-sql.json) |
| B5-34 | Original rows preserved | PASS | [sql-final-preservation.json](D:/Project_All/UnicoreCRM/task-runtime/quote-document-b5/sql-final-preservation.json) |

Backup restore: **NOT_RUN**. Configured logo without asset owner: **BLOCKED_CONTRACT**. Production sizing/DR/encryption deployment: **NOT_RUN**. Provider Delivery/Verified Acceptance/GateC/Order/Payment: **NOT_APPLICABLE** to B5 implementation. These are not silently counted as runtime test PASS.

## P. Remaining Backlog

Trusted logo asset authority; production renderer/SQL sizing and backup containing B5 documents with restore/DR verification; durable retention/legal policy beyond B5; formal bounded verifier support for the approved project extension. Current read access cannot erase immutable bytes. Provider delivery, verified customer acceptance, document-linked delivery binding, Restore, B6/GateC/Order/Payment remain separate unimplemented decisions/workflows. No self-verification/Won bypass added.

## Q. Git Status / File Changes

Backend existing edits: document hook/record capability, Quote RV/domain/DbContext/module, Workspace seller/Studio validation, CRM owner registrations, explicit AccessControl assignable capability, pinned packages/solution/host packaging. Added domain/commands/reads/contracts/owner readers, migration, bounded renderer/fonts/notices, focused tests, verification scripts and docs.

Frontend existing edits: Quote Detail/read actions/runtime/adapter, generated API/binary transport, capability catalog, Studio business form/guidance, approved API inventory tests. Added document models/query hooks/command port/panel/preview, Studio readiness component and Connected spec/config. Generated client SHA changes across clients are expected regeneration, not unrelated business changes.

Exact delta vs B5 entry: [Backend files](D:/Project_All/UnicoreCRM/task-runtime/quote-document-b5/backend-b5-changed-files.json), [Frontend files](D:/Project_All/UnicoreCRM/task-runtime/quote-document-b5/frontend-b5-changed-files.json); final dirty state snapshots retained. Root manifests changed only for approved extension/admission/capability/owner mappings. No B5 deletion of existing files, no reset/reseed, commit/push/merge or opening B6/GateC. Existing HEAD/local changes/checkpoints preserved.
