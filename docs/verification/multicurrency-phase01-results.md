# Multi-currency Phase 0 & 1 — kết quả và checkpoint đề xuất

Ngày thực hiện: 21/10/2026, Asia/Saigon. Phạm vi hoàn thành: thiết kế Phase 0 và implementation CF01. **Checkpoint: PROPOSED_NOT_APPROVED.** Không triển khai Phase 2, không chấp nhận quality waiver.

## 1. Currency data flow trước / sau

Contract chi tiết và các authority gap: [thiết kế Phase 0](multicurrency-phase01-design.md). Đã đọc source của cả hai working tree, gồm các patch Studio chưa commit; không dùng remote HEAD thay cho source này.

| Trước CF01 | Sau CF01 |
|---|---|
| CRM vào từ Bootstrap nhưng consumers đọc full operational configuration; projection chưa tải thì có thể rơi xuống demo VND. Mở Studio mới nạp đúng currency. | Canonical Workspace entry validate/prepare base currency, timezone và Bootstrap configurationVersion trước khi activate context và mount consumers. CRM trực tiếp dùng Bootstrap, không cần mở Studio hoặc quyền `studio.read`. |
| Operational projection chưa có owner Workspace/session; clear không bảo đảm cập nhật subscribers; async Studio response có thể đến muộn. | Scope bằng session/account/Workspace/generation. Retire currency trước membership/Bootstrap khi đổi Workspace; logout/session replacement cũng retire. Late Bootstrap/full configuration/Studio responses không được publish vào owner mới. |
| Consumer chỉ cần timezone/base cũng phụ thuộc full Studio configuration. | Narrow `useWorkspaceRuntimeSettings` phục vụ các trường Bootstrap có authority. Chỉ surface cần business information/addresses/enabled currencies mới tải full configuration qua adapter hiện hữu, với loading/error/retry. |
| Studio save cập nhật projection nhưng freshness giữa requests chưa được bảo vệ đầy đủ. | Studio response có owner/revision guard, publish context và subscribers. GET cũ không ghi đè save mới; response trùng không phát invalidation thừa. Bootstrap version và Studio revision được giữ riêng. |

Workspace base authority vẫn là Backend Workspace; Studio chỉnh sửa và cập nhật Studio configuration + Bootstrap projection trong transaction SQL hiện hữu. Transaction amount/currency vẫn thuộc từng record. Reporting currency và conversion authority chưa được nghiệm thu. Không tạo rate, conversion engine, service, framework, migration hoặc OpenAPI mới; Backend USD và demo VND giữ nguyên.

Thiếu authority trong connected mode trả pending/error; không dựng full configuration bằng dữ liệu demo. Currency choices chỉ gồm base đã biết, enabled currencies nếu đã tải authoritative full projection, và currency của record/draft liên quan; không tuyên bố Bootstrap có full enabled-currency policy. External/cross-tab push invalidation chưa triển khai; refresh/entry đọc lại authority.

## 2. Thay đổi source và bảo toàn checkpoint P0

[Source manifest](evidence/multicurrency-phase01-20261021/phase1-source-manifest.json) liệt kê **36 file source/test/config**: Frontend 35, Backend 1 verification script; không sửa production Backend. Mỗi file có SHA-256 trước/sau; file mới có before=null. Những before hash chưa chụp raw trước task được ghi rõ là tái dựng từ local pre-task checkout, kèm normalized hash để phân biệt EOL. Ba patch Studio có sẵn được bảo toàn và loại khỏi patch CF01.

| Nhóm thay đổi | File / trách nhiệm chính |
|---|---|
| Entry và session | `applicationComposition.ts`, `RequireWorkspaceContext.tsx`, `workspaceContextRuntime.ts`, `connectedWorkspaceContextRuntime.ts`: prepare/activate/retire, owner guard, route readiness và injectable full-config loader. |
| Configuration runtime | `workspaceRuntimeSettings.ts` mới; `useWorkspaceCurrencyChoices.ts` mới; operational runtime/hook/barrel: narrow authority, subscription, deferred full read và fail closed. `LazyRouteErrorBoundary.tsx`: retry lỗi configuration. |
| Studio | `studioCoreRuntime.ts`: async owner/revision guard và publication sau authoritative response. Không buộc CRM tải Studio UI. |
| Consumers | Lead/Contact timezone và Lead/Product/Deal/qualification/Quote/Order/Invoice/Dashboard/Reports base reads; chỉ đổi nguồn configuration theo phạm vi CF01. Chi tiết đầy đủ trong manifest và patch. |
| Verification | Contract lifecycle test; connected browser spec/config; `backend/scripts/verify-multicurrency-connected.ps1` chạy ApiHost và SQL Server thật trong DB GUID mới. |

Patch riêng: [Frontend](evidence/multicurrency-phase01-20261021/frontend-phase1.patch), [Backend verifier](evidence/multicurrency-phase01-20261021/backend-phase1.patch). HEAD để nhận diện checkout, không đại diện toàn bộ source đã kiểm thử:

```text
Frontend HEAD 7d41aba85f33c80855f598f3ab24371c0fb510e8
Backend  HEAD 13cd39eba01ebc9dc238f8b0ef98e174c1d4d3f6
```

Ngoại lệ pin duy nhất đã được người dùng duyệt: `useLeadFormController.tsx`. [Diff thực tế](evidence/multicurrency-phase01-20261021/lead-pinned-actual.patch) gồm 5 dòng thay thế: import, hook, timezone read, hai baseCurrency reads. Không đổi validation, submit, lifecycle, cấu trúc API payload hoặc logic nghiệp vụ; currency mặc định mới đi từ Bootstrap theo yêu cầu CF01. File khớp byte-for-byte proposal đã duyệt.

```text
Lead trước ed9aba57de79f3ce041e57dd0d426ded61b1ef501c89fc3d33cb574fd4631bbc
Lead sau   b8e212c65e473a46d3a0a9fe1eef03249aa3e7292c26ee0cb5f1c5af6c3998de
P0 manifest gốc
16efc5bf70fad10c8e8d1a03849bd1520017991e347a2e154c916d63c51215f2
```

[Preservation evidence](evidence/multicurrency-phase01-20261021/preservation-after.json): 102 pin khác giữ nguyên, 1 ngoại lệ được duyệt, không có thay đổi pin ngoài phép; 83 immutable/evidence references giữ nguyên. Không sửa manifest P0 gốc, compatibility baseline, không stage/commit/push. [Approval record](evidence/multicurrency-phase01-20261021/pinned-exception-approval.json) không phải phê duyệt checkpoint.

## 3. HTTP / SQL / browser verification

[Tổng hợp machine-readable](evidence/multicurrency-phase01-20261021/connected-verification-summary.json), [artifact hashes](evidence/multicurrency-phase01-20261021/artifact-hashes.json). Các tập con dưới đây có `run.json`, `results.json`, `http.json`, SQL evidence và browser records/screenshots tương ứng. Không đóng gói credentials, auth tokens, host logs, trace/video; raw artifacts và database thử nghiệm được giữ lại.

| Kiểm thử | Trạng thái | Bằng chứng / giới hạn |
|---|---|---|
| CF01 ApiHost + localhost SQL Server | **PASS** | [50 assertions, 0 fail](evidence/multicurrency-phase01-20261021/phase1-connected/results.json); [27 browser checks](evidence/multicurrency-phase01-20261021/phase1-connected/browser/currency-results.json), Playwright 1/1. Run `e78e1ec3aacb4c02941ce2cf2926cd7e`, 21:41:41–21:43:16 giờ Saigon. |
| Workspace mới, login → CRM trước Studio | **PASS** | Provision bằng API thật; SQL Bootstrap và Studio đều USD. Lead và Product trước Studio dùng USD, không yêu cầu full Studio configuration. |
| Studio USD → EUR → CRM | **PASS** | UI save PATCH 200; Product form và CRM nhận EUR. Refresh, explicit logout/login, login sau restart ApiHost vẫn EUR. |
| Workspace A EUR / B USD | **PASS** | Chuyển B rồi về A đúng currency; 8 Lead tạo từ UI có amount 123 và Workspace/currency đúng theo SQL: initial USD, EUR, refresh, relogin, restart, B USD, về A EUR, B không `studio.read`. |
| Lead → Customer sau đổi currency | **PASS** | UI tạo Contact và conversion 200; Customer hiển thị. [SQL join](evidence/multicurrency-phase01-20261021/phase1-connected/lead-customer-sql.json) xác nhận Lead.CustomerRef → Customer CONTACT → Contact cùng Workspace, Lead giữ EUR. |
| Backend/configuration unavailable | **PASS** trong phạm vi đã chạy | Dừng ApiHost thật rồi chuyển Workspace: entry error và currency cũ bị retire. Thu hồi `studio.read` trong fixture B: Lead/Product vẫn dùng Bootstrap USD; Payment full config GET 403 hiện error, không demo fallback; Lead tiếp tục dùng được. Cold authentication outage chưa được chứng nhận. |
| Product và giá gốc | **PASS** | UI consumer EUR; create EUR API 201, stale USD create 422 theo guard Backend hiện hữu. [SQL](evidence/multicurrency-phase01-20261021/phase1-connected/product-currency-sql.json) Product gốc vẫn 10 USD và Product mới 10 EUR; không tự reprice. Không sửa CF05. |
| Studio Quick Setup first visit | **PASS** | Chờ authoritative load; đóng/revisit/SPA/refresh/relogin/independent browser; manual entry; skip 3 bước COMPLETED persisted. Workspace B read-only không tự mở; sau cấp configure có first visit riêng. |
| Demo VND | **PASS** | Contract test in-process dùng demo repository thực; không phải browser/SQL demo acceptance mới. |
| Cumulative P0 security | **PASS** | [580 assertions](evidence/multicurrency-phase01-20261021/p0-security/results.json), SQL thật; scale/projection/race/commit-race/customer-task/activity/quote-route probes. |
| Cumulative P0 connected | **PASS** | [132 assertions](evidence/multicurrency-phase01-20261021/p0-connected/results.json), Playwright 1/1; projection/history và restart persistence. |
| Cumulative P0 UI journey | **PASS** | [11 harness checks](evidence/multicurrency-phase01-20261021/p0-ui/results.json), [12/12 UI steps](evidence/multicurrency-phase01-20261021/p0-ui/browser/ui-steps.json), Playwright 1/1; Lead → Contact/Organization → Customer → Task → Activity/history, refresh/relogin và restart SQL. |

Harness dùng DB mới `UnicoreCRM_CurrencyTest_e78e1ec3aacb4c02941ce2cf2926cd7e`; auth fixtures clone account/password hash trong DB thử riêng, login qua HTTP thật. Không tuyên bố đã chạy lại registration/OTP; không inject auth/browser localStorage. Quick Setup có trì hoãn một request thật để kiểm tra loading, không dùng response currency giả. Script chỉ cấp/thu quyền trong fixture database; không thay bảng giá/giao dịch SQL đang có của môi trường người dùng. Các migration hiện hữu chỉ được apply để tạo DB thử; không thêm migration.

Các run FAIL trước vẫn được giữ. P0 connected/UI từng phát hiện Contact detail bị phụ thuộc full configuration do Deal modal đóng vẫn mount. Đã bỏ dependency đó khỏi currency choices của Deal, rồi chạy lại cả hai P0 suite trên source cuối đạt PASS. Kiểm thử outage cũng phát hiện retire quá muộn khi membership request thất bại; đã retire trước membership và kiểm tra lại. Không dùng kết quả FAIL cũ hoặc run cũ để chứng nhận source cuối.

### Lệnh và quality results

Từ Backend, chạy riêng từng lệnh; mỗi lệnh tạo DB mới và giữ evidence:

```powershell
pwsh -NoProfile -File scripts/verify-multicurrency-connected.ps1 -Port 5397
pwsh -NoProfile -File scripts/verify-activity-security.ps1 -ScaleReview -ProjectionReview -RaceReview -CommitRaceReview -CustomerTaskReview -ActivityContractReview -QuoteRouteReview -Port 5402
pwsh -NoProfile -File scripts/verify-activity-security.ps1 -ProjectionReview -RaceReview -CommitRaceReview -CustomerTaskReview -ActivityContractReview -QuoteRouteReview -Browser -Port 5400
pwsh -NoProfile -File scripts/verify-activity-security.ps1 -Browser -UiReview -Port 5401
```

Từ Frontend, contract tests chạy qua TypeScript loader hiện hữu, ví dụ:

```powershell
node --import tsx --loader ./scripts/quality/core/typescript-path-loader.mjs tests/contract/workspace-currency-context.test.mts
npm run typecheck
npm run build
npm run api:check
npm run quality:gate -- --gate quality.application-composition
npm run verify
```

| Kiểm tra | Trạng thái | Kết luận |
|---|---|---|
| Currency authority focused contract | **PASS** | Direct entry, subscribers, pending switch, malformed/unavailable authority, late Bootstrap/full/Studio response, Studio save/read race, identical read, logout và demo. [Log](evidence/multicurrency-phase01-20261021/accepted-workspace-currency-context.log). |
| Existing focused contracts | **PASS** | studio-first-visit, lead-manual-activity, customer-task-create-session, deal-quote-transition-outcome, lead-history, contact-history. Các test cũ giữ nguyên. Logs `verified-*`; Deal test có run cuối `accepted-deal-quote-transition-outcome.log`. |
| Typecheck | **PASS** | [3/3 gates](evidence/multicurrency-phase01-20261021/accepted-typecheck.log), strict regions 109 files; any 180 / as-any 15 / non-null 56, không tăng budget. |
| Production build | **PASS** | [Build và bundle budget](evidence/multicurrency-phase01-20261021/accepted-build.log): entry 290.04 KiB, largest 489.59 KiB / 500 KiB. Có Vite generic chunk-size warning; budget gate PASS. ApiHost build/start/migrate hiện hữu thành công trong các harness. |
| API check và focused boundaries | **PASS** | Logs `review-api-check.log`, `review-quality.*.log`: application-composition, workspace-bootstrap-api-boundary, studio-quick-setup, studio-route-runtime, studio-module-propagation, guidance-contracts, presentation-responsibility, connected-backend-integration, api-management-boundaries, frontend-backend-separation. |
| Full `npm run verify` | **FAIL** | [Run cuối](evidence/multicurrency-phase01-20261021/review-verify.log): dừng lint, 0/327 gates; 3 TS errors có sẵn: nullable model ở deal-quote test L42 và optional control ở studio-first-visit E2E L50–51. Không sửa các test này ngoài scope/pin. [Pre-task baseline](evidence/multicurrency-phase01-20261021/baseline-verify.log) có cùng 3 errors. |
| `quality.backend-readiness` | **FAIL** | Hai cycle có sẵn ContactFormModal ↔ contactDraftRecovery, TaskCreateModal ↔ useTaskCreateController; baseline và current cùng fail. |
| `quality.quality-pipeline` | **FAIL** | Baseline và current cùng assertion 347 != 345. |
| `repo:check` | **FAIL** | Inventory drift có sẵn tái hiện trên baseline và current. Không regenerate baseline/inventory để hấp thụ drift ngoài task. |
| `quality.crm-ui-interaction-contracts` | **FAIL** | Baseline và current cùng lỗi fixture Meeting thiếu real Workspace directory member. Không đồng nhất gate FAIL này với các browser P0 PASS. |
| Full compatibility gate / older-client acceptance | **NOT_RUN** | Baseline bất biến giữ nguyên hash; không có quality waiver hoặc tuyên bố compatibility mới. |
| Quote mutation SQL acceptance | **BLOCKED** | Backend handlers chưa có; route probes 404/405 không phải mutation acceptance. TEAM positive-scope acceptance cũng còn BLOCKED theo authority hiện hữu. |
| Independent signed review / gatekeeper | **NOT_RUN** | Báo cáo implementation và tự kiểm tra không thay thế attestation độc lập; chưa đủ điều kiện freeze/approve checkpoint. |

Baseline so sánh chất lượng được tái dựng trong `review-artifacts/multicurrency-phase01-20261021/baseline-frontend` từ **local** checkout + đúng ba patch Studio và các untracked Studio tests/config/audit có sẵn. Baseline chỉ dùng xác định lỗi tồn tại trước task; mọi PASS implementation ở trên dùng working tree thực tế cuối. Không có waiver cho các FAIL; toàn bộ full pipeline chưa xanh.

## 4. Consumers đã xác minh và giới hạn

| Consumer | Mức chứng minh |
|---|---|
| Lead | Real UI/HTTP/SQL qua đổi currency, refresh, explicit login, backend restart, switch A/B, thiếu Studio read; Lead → Customer PASS. Diff pin đúng ngoại lệ. |
| Product form | Real browser EUR/USD, không cần Studio read; create API + SQL bảo toàn giá gốc. CSV default chỉ source inspection/typecheck, CSV browser import **NOT_RUN**. |
| Contact / Customer / Task / Activity | P0 connected/UI và SQL PASS; focused history/lifecycle contracts PASS. Không tuyên bố mọi biến thể nghiệp vụ đã chạy. |
| Deal / lead qualification | Source/compile + focused transition contract PASS; đóng Deal modal không còn ép Contact tải full config, đã chứng minh qua P0. Broad connected money-write E2E **NOT_RUN**. |
| Quote / Order / Invoice | Base reads đã chuyển nguồn, source/compile PASS; mixed full-config surfaces vẫn cần authority thực. Mutation E2E **NOT_RUN** hoặc BLOCKED với Quote. CF03 giữ mở. |
| Payment | Full config 403 fail-closed browser PASS; mutation E2E **NOT_RUN**. |
| Dashboard / Reports | Base/timezone reads source/compile PASS; mixed-currency correctness **NOT_RUN**, CF02 còn mở. |
| Shipping / document business information | Full-config boundary giữ authoritative loading/error; không dựng địa chỉ demo. Broader document/browser cases **NOT_RUN**. |

## 5. Gaps Phase 2–6

- Phase 2: chốt Exchange Rate Authority, provenance, effective time, version/retirement, permission và provider evidence; chưa kết luận cần schema/tables mới.
- Phase 3: conversion policy, precision/rounding, as-of/missing/stale/inverse/cross-rate, immutable snapshot và reproducibility; không rewrite Money gốc.
- Phase 4: Dashboard/Reports grouping hoặc conversion có authority; CF02 chưa sửa, thay base context không làm metric mixed currency tự đúng.
- Phase 5: từng module inheritance/edit/duplicate semantics và CF03/CF04; cần money mutation E2E riêng.
- Phase 6: catalog/base-change eligibility, explicit repricing policy và CF05; Product SQL guard hiện hữu không đổi.

Chi tiết API/migration nào thực sự cần chỉ chốt sau authority của từng phase trong [thiết kế](multicurrency-phase01-design.md). External update freshness, full enabled-currency policy cho CRM, cold authentication outage, các browser engines khác và production-load acceptance còn ngoài chứng minh hiện tại.

## 6. Đề xuất checkpoint riêng cho Phase 1

Đề xuất review candidate dựa trên paired HEAD + actual working-tree hashes trong `phase1-source-manifest.json`, scoped patches, `artifact-hashes.json`, approval ngoại lệ Lead và `preservation-after.json`. P0 gốc là immutable reference; không cập nhật lại hash P0 để che thay đổi. Mọi DB/evidence thử nghiệm giữ lại.

Trạng thái đề xuất giữ **PROPOSED_NOT_APPROVED**: CF01 focused + real integration + cumulative P0 + typecheck/build PASS trong phạm vi nêu trên; full quality còn FAIL và independent signed review chưa có. Không tự freeze, cấp waiver hoặc tuyên bố production-ready. Chờ người dùng review kết quả Phase 1 trước bất kỳ implementation Phase 2 nào.
