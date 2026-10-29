# F01 — Activity manual write contract alignment

Ngày 2026-10-21. **F01 source/runtime alignment đã sửa; checkpoint: BLOCKED_APPROVAL.** Không đề xuất ACCEPTED_FOR_FUNCTIONAL_CHECKPOINT khi compatibility baseline và các quality-gate waivers chưa được phê duyệt. Không stage/commit/push, không chạy `--accept-breaking-baseline`.

Backend HEAD `7b734f98a7173af2e24e094392826a20b6e11359`; Frontend HEAD `99db1773c03ce4a4b820db7e793da6eca872f862`. Kết quả thuộc working tree, gồm các patch P0 có trước. Status/diff trước sửa được giữ trong evidence mới, không ghi đè báo cáo trước.

## Root cause và thay đổi

Backend LogActivityValidation đã chỉ nhận CALL/EMAIL/MEETING/NOTE/MESSAGE, trả 422 cho SYSTEM/unknown. Handler vẫn kiểm Tasks capability/FLS, mọi target, quan hệ owner-owned, lifecycle, Workspace và replay/commit guard. HTTP DTO giữ string để validation trả đúng 422; không cần đổi enum C# hay endpoint.

OpenAPI trước đây cho LogActivityRequest.type tham chiếu ActivityType dùng chung với read model, nên generated client và runtime request validator vẫn chấp nhận SYSTEM. Lead command còn normalize unknown → SYSTEM. Đây là lệch contract thực sự; test schema và compile-time đều Red trước sửa.

| Thành phần | Before | After |
|---|---|---|
| OpenAPI LogActivityRequest.type | ActivityType, 6 giá trị gồm SYSTEM | ManualActivityType, 5 giá trị manual |
| ActivityReadModel.type | ActivityType gồm SYSTEM | Giữ nguyên |
| Generated LogActivityRequest.type | ActivityType | ManualActivityType |
| TaskApiRuntime.LogActivityInput.type | ActivityType | ManualActivityType |
| Domain ActivityType | 6 giá trị | Giữ 6 giá trị; thêm ManualActivityType = Exclude<ActivityType, SYSTEM> |
| Mapper / public manual command | Không có type guard phù hợp | Từ chối SYSTEM/unknown trước command port/HTTP; không fallback |
| Lead command | Unknown → SYSTEM | Normalize casing của 5 loại hợp lệ; unknown/SYSTEM → VALIDATION_FAILED trước port |

OpenAPI thêm mô tả CRM reference, relationship-only, optional sourceRef và whitelist origin theo module; 422 validation; valid examples và `x-invalid-request-examples`. Không làm sourceRef bắt buộc. Không dùng oneOf/anyOf để mô phỏng ràng buộc mà generator không biểu diễn TypeScript đúng. Schema/type chỉ kiểm enum và structural vocabulary; tồn tại record, quan hệ thật, lifecycle, Workspace và quyền vẫn do Backend quyết định. Điều kiện phải có CRM anchor được mô tả trong contract và kiểm chứng HTTP 422, không tuyên bố generated types tự chứng minh quyền/anchor.

Không sửa Activity Security, ACT-PERF SQL, AccessControl, conversion/Task business protocol, SYSTEM read model, hoặc tạo cơ chế SYSTEM write. Không thêm SQL migration.

## Source và generated allowlist

Chi tiết 33 file và SHA-256 trong [changes.json](evidence/activity-write-contract-20261021/changes.json); task-only diffs trong `frontend-f01-only.patch` và `backend-f01-only.patch`.

**14 source/test files**:

- Frontend `docs/api/openapi.json`.
- Tasks: domain `task.types.ts`, `manualActivityType.ts`; application `TaskApiRuntime.ts`; HTTP `TaskApiMapper.ts`; `public/api.ts`; `RelationshipActivityCreateModal.tsx`.
- Leads `leadActivityCommands.ts`; Deals `useDealDetailController.tsx`.
- Tests: `activity-manual-write.test.mts`, `lead-manual-activity.test.mts`, compile-time fixture `activity-manual-write.ts` và tsconfig riêng.
- Backend `scripts/verify-activity-security.ps1`: thêm ActivityContractReview, không sửa Backend production source.

**19 generated files thay đổi** qua `npm run api:generate`: 4 docs metadata/checksum/catalog, 14 generated API client files, và `generatedOpenApiRuntimeContract.ts`. Xem `generated-changed.json` để có path chính xác. Generator render 27 outputs, 8 outputs không đổi; kiểm tra bytes 27/27 khớp deterministic render. Không sửa tay generated TypeScript hoặc generator/checker.

Before/after OpenAPI và generated excerpts nằm ở `before-contract.json`, `after-contract.json`, `before-generated.txt`, `after-generated.txt`; bản đầy đủ trước sửa trong `before/frontend`.

## Caller inventory / compatibility impact

- Lead: fallback unknown/SYSTEM đã được loại bỏ; CALL/EMAIL/MEETING/NOTE/MESSAGE với casing legacy được normalize và kiểm thử.
- Contact: command caller dùng các loại manual; recordRef Contact, CONTACT relationship và CONTACT_ACTIVITY source hiện hữu không đổi. UI quick Note thật vẫn PASS.
- Organization: relationship wrapper dùng 5 loại manual; ORGANIZATION_DETAIL origin, Organization record/relationship đúng owner. Không thay caller protocol.
- Customer: relationship wrapper dùng manual types; canonical Customer/Contact multi-reference và CUSTOMER_360 được HTTP/SQL chứng minh. Customer Task và Meeting protocol không đổi.
- Task public command/HTTP mapper: manual input được thu hẹp, guard chặn cả caller JavaScript/Reflect sai type. Read Activity vẫn có SYSTEM.
- **Caller SYSTEM phát hiện thêm:** Deal Quote-status UI gọi logDealTimelineActivity với SYSTEM. Helper hiện dừng trước manual HTTP port cho loại không manual; không đổi thành NOTE, không giả system audit. Quote command/state transition không đổi. Việc tạo timeline hệ thống tin cậy cho Quote nằm ngoài task này; không bổ sung endpoint/system writer.
- Customer conversion/care demo có SYSTEM trong snapshot/demo-owned model. Những đường này không được biến thành HTTP write; giữ nguyên read/domain và demo snapshot mechanism có sẵn. Chưa xác định được toàn bộ consumer bên ngoài repository; cần inventory khi release.

`compatibility-impact.json` dùng **checker nguyên bản** trên accepted baseline, before working tree và after working tree:

| Comparison | Findings |
|---|---:|
| Accepted baseline → before F01 | 42 |
| Accepted baseline → after F01 | 43 |
| Before F01 → after F01 | **1** |

Finding mới duy nhất: `Schema LogActivityRequest [REQUEST].type: enum domain is incompatible.` Toàn bộ 42 findings cũ được giữ trong ledger, không chấp nhận ngầm.

`npm run api:check` **thực tế PASS**. Tuy nhiên gate này không assert compatibility của spec hiện tại với accepted baseline; source của gate chỉ có baseline self/control checks ở đoạn liên quan. Lệnh chính thức `node scripts/api/generate-openapi-client.mjs --check` **FAIL**, báo 43 findings. Vì vậy **không báo API compatibility PASS** và không sửa checker trong task này. Đây là giới hạn coverage của gate hiện tại, được ghi nhận thay vì che kết quả.

Breaking baseline giữ nguyên byte-for-byte, SHA-256 `c8e77d670453860796e3c32bcf17bc9ffbefe06bc0a8915d9509338bcabf66ef`. Spec mới SHA-256 `d894e313d9dad7ae78c0c9bb8fd865f06d2d04546bfbaca3683465780e7e1659`.

Đề xuất release-owner review một security contract candidate **0.24.1-contract.0**; chưa đổi version package/release/spec hiện tại. Quy trình repository tại `docs/quality/api-contract-verification.md` yêu cầu explicit `--accept-breaking-baseline`. Lệnh đó nhận toàn bộ snapshot: **không thể chạy chỉ dựa trên phê duyệt F01 rồi vô tình nhận thêm 42 findings cũ**. Cần phê duyệt security narrowing F01 và giải quyết/review riêng backlog compatibility trước khi chấp nhận baseline bằng quy trình chính thức. Không chuẩn bị một thao tác reset baseline tự động.

## Verification

| Test | Actual | Status |
|---|---|---|
| New schema/mapper test trước sửa | FAIL: write enum còn SYSTEM | Red chứng minh bug |
| Compile-time test trước sửa | TS2344: SYSTEM vẫn assignable vào generated write type | Red chứng minh bug |
| Schema/runtime mapper sau sửa | 5 loại manual hợp lệ; SYSTEM/unknown/lowercase sai wire type/null/number bị từ chối; read SYSTEM giữ nguyên; valid examples qua runtime schema validator | PASS |
| Compile-time sau sửa | Generated và application-port write không có SYSTEM; read generated/domain vẫn có SYSTEM | PASS |
| Lead/public command test | 5 casing manual normalize đúng; unknown/SYSTEM bị chặn trước port, không fallback | PASS, in-process |
| Generation | Official api:generate; 27 deterministic outputs khớp | PASS |
| API gate | npm run api:check | PASS theo kết quả thực, **không phải compatibility acceptance** |
| Generator compatibility | --check, 42 cũ + 1 mới | FAIL / BLOCKED_APPROVAL |
| Typecheck | 3/3 gates; strict/type budget không tăng | PASS |
| Build/bundle budget | 21,58s; largest chunk 486,99 KiB < 500 | PASS |
| Task/Activity API boundary, API management, mutation authority | Focused gates | PASS |
| Activity form contracts | 5 canonical forms, standardized Lead/Contact/Customer/Organization/Deal, không duplicate state | PASS, source-contract test |
| Customer panel và Task create session | Scope/FLS/paging/late responses; double submit, same-key unknown retry, Customer/Workspace retirement | PASS, in-process |
| ApiHost + SQL security/performance + F01 HTTP | **575 assertions PASS, 0 FAIL**; 107 benchmark request vẫn đúng 2 SQL Activity/request | PASS |
| Connected regression + F01 HTTP | **123 assertions PASS, 0 FAIL**; Customer Task, canonical Contact/Organization Customer, UI history/OWN, affiliation, refresh/relogin/restart | PASS |
| UI-only CRM journey | **12/12 steps PASS**, Playwright 1 test PASS; thêm 11 harness SQL/restart assertions PASS | PASS |

HTTP F01 đã thực thi: CALL/EMAIL/MEETING/NOTE/MESSAGE → 201; SYSTEM/UNKNOWN/lowercase wire type → 422; không reference → 422; missing/foreign target → 404; contradictory references → 422; forged source → 422; canonical Customer+Contact+CUSTOMER_360 → 201; relationship-only Contact NOTE không source → 201. SQL read-only `verify-f01.sql` xác nhận 7 receipt/activity hợp lệ và **0 receipt cho các request bị từ chối**.

Environment: SQL Server localhost 17.0.1000.7, .NET SDK 10.0.401, ASP.NET Core HTTP pipeline thật, Frontend connected và Playwright Chromium; không EF InMemory để chứng minh SQL. Các DB riêng:

- Security/performance `UnicoreCRM_ActivityTest_3dd6e8153b3d40e98e7851aba2de7237`, 11:29:42–11:31:26 UTC.
- Connected `UnicoreCRM_ActivityTest_7990b079e1ca475c9ab51f09f4cff372`, 11:31:38–11:32:39 UTC.
- UI-only `UnicoreCRM_ActivityTest_4e250039bf384f40bb03d9dddab6a392`, 11:29:53–11:31:21 UTC.

Sanitized method/path/status/correlation metadata, results, query plans, SQL output và browser screenshots ở các subfolders tương ứng. Không mock response để báo connected PASS. Không xoá các DB test. Browser trace, keys, bootstrap/raw host logs và credentials không nằm trong evidence bàn giao.

## Remaining FAIL / BLOCKED / NOT_RUN

- **BLOCKED_APPROVAL:** intentional public write enum narrowing, release version decision và compatibility acceptance. Không có approval cho baseline quality-gate waivers, nên chưa ACCEPTED_FOR_FUNCTIONAL_CHECKPOINT.
- 42 compatibility findings trước F01 vẫn FAIL trong generator check. Không xử lý Order/Quote và các schema không liên quan để làm xanh toàn baseline.
- Full baseline architecture/inventory/cycle failures đã được chứng minh trong [Customer Task report](customer-task-ui.md); **không chạy lại full suite trong F01** và không đổi chúng thành PASS. Chỉ các focused gates trên có kết quả mới.
- Five-form source contract PASS không tương đương browser E2E cho từng CALL/EMAIL/MEETING/SMS form. UI-only regression thực thi Contact NOTE; 5 manual types đều đã được HTTP test riêng. Full per-form browser matrix: NOT_RUN.
- JSON Schema không thay thế quyền/lifecycle/SQL relationship validation. SYSTEM còn ở read type không đồng nghĩa endpoint list hiện công bố các legacy SYSTEM rows; P0 fail-closed read policy giữ nguyên.
- TEAM vẫn fail-closed; giới hạn policy snapshot, pagination concurrency và production-load qualification giữ nguyên theo báo cáo P0 trước. Không phát triển P1.

## Reproduce / checkpoint

Từ frontend:

```powershell
npm run api:generate
npm run api:check
node scripts/api/generate-openapi-client.mjs --check # expected FAIL until reviewed compatibility acceptance
node node_modules/typescript/bin/tsc -p tests/fixtures/typechecks/activity-manual-write.tsconfig.json
node --import tsx --loader ./scripts/quality/core/typescript-path-loader.mjs scripts/quality/run-node-entry.mjs tests/contract/activity-manual-write.test.mts
node --import tsx --loader ./scripts/quality/core/typescript-path-loader.mjs scripts/quality/run-node-entry.mjs tests/contract/lead-manual-activity.test.mts
npm run typecheck
npm run build
```

Từ backend, PowerShell 7, từng browser invocation chạy tuần tự:

```powershell
pwsh -NoProfile -File scripts/verify-activity-security.ps1 -ActivityContractReview -CustomerTaskReview -ProjectionReview -ScaleReview -RaceReview -CommitRaceReview -Port 5393
pwsh -NoProfile -File scripts/verify-activity-security.ps1 -UiReview -Port 5392
pwsh -NoProfile -File scripts/verify-activity-security.ps1 -ActivityContractReview -CustomerTaskReview -Browser -ProjectionReview -Port 5393
```

Updated checkpoint proposal/allowlist/SHA-256 ở `checkpoint-proposal.json`, trạng thái **BLOCKED_APPROVAL**. Không chứa baseline acceptance hoặc waiver giả. Artifact hashes và task-only diffs cho phép review riêng F01, bảo toàn công việc P0 chưa commit có trước.
