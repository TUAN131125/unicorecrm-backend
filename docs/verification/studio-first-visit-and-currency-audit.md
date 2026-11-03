# Studio first visit và currency authority — 2026-10-21

Quick Setup: **FIX VERIFIED**. Currency: **AUDITED — FINDINGS OPEN**, không thay currency mặc định. Không phải một production release acceptance.

## 1. Root cause và thay đổi tối thiểu

`frontend/src/workspaces/studio/presentation/pages/StudioIndexRoute.tsx` trước đây luôn `Navigate` tới `SETTINGS_QUICK_SETUP`. Route không dùng `shouldAutoOpenQuickSetup`, không kiểm tra `studio.configure`, không đọc trạng thái Quick Setup trước quyết định điều hướng. Vì vậy cả trạng thái đã đóng và COMPLETED đều bị mở lại.

Regression test render **StudioIndexRoute thật** với state COMPLETED tái hiện `actual='quick', expected='business'` trước sửa; sau sửa PASS. [RED](evidence/studio-first-visit-20261021/route-before.log), [GREEN](evidence/studio-first-visit-20261021/route-after.log).

Backend hiện có đủ authority, không cần sửa:

- `StudioService.GetQuickSetupAsync`: studio.read + trusted Workspace.
- Mọi mutation open/dismiss/complete/skip yêu cầu studio.configure.
- `StudioQuickSetup.Open`: NOT_STARTED → IN_PROGRESS và lưu LastOpenedAt.
- `Dismiss`: lưu AutoOpenDismissedAt.
- Ba bước complete/skip: lưu các step IDs; khi hết bước chuyển COMPLETED và lưu CompletedAt.
- `EfStudioPersistence`: SQL theo WorkspaceId, serializable transaction, aggregate lock, version, idempotency, audit/outbox.

Sau sửa:

1. Studio index đi qua `StudioCoreRuntimeBoundary includeQuickSetup`, chờ configuration và Quick Setup GET hoàn tất.
2. Index dùng predicate sẵn có: chỉ tự mở khi có studio.configure, status NOT_STARTED, AutoOpenDismissedAt null. Các trường hợp khác tới Business Information.
3. Loader được key bằng WorkspaceId và includeQuickSetup để navigation/switch Workspace không tái sử dụng trạng thái ready của phạm vi tải trước.
4. Lỗi tải dùng error/retry UI hiện hữu. Không dùng dữ liệu demo hoặc local/sessionStorage để quyết định first visit trong connected mode.
5. Luồng manual open, save, dismiss, complete, skip, optimistic concurrency/idempotency không đổi. Read-only vẫn có thể xem những surface đã được phép trước đây; không tự mở popup cấu hình.

State hiện hữu là **workspace-level**, dùng chung giữa các thành viên được phép cấu hình, không phải tiến độ riêng từng người. Không thêm bảng hoặc migration.

## 2. Exact source/test/config changes

Repository-relative paths; hash và diff đầy đủ trong [changes.json](evidence/studio-first-visit-20261021/changes.json), [Backend patch](evidence/studio-first-visit-20261021/backend.patch), [Frontend patch](evidence/studio-first-visit-20261021/frontend.patch).

| Repository / file | Lý do |
|---|---|
| FE `src/workspaces/studio/presentation/pages/StudioIndexRoute.tsx` | Dùng persisted state và capability trước auto-open |
| FE `src/app/router/workspaces/studioWorkspaceRoutes.tsx` | Load authority trước index; remount loader theo scope |
| FE `src/guidance/content/studio/configuration.ts` | Giải thích first-visit/manual reopen VI/EN; Quick Setup guidance v4 |
| FE `playwright.studio-connected.config.ts` | Config test riêng; giữ nguyên output P0; không raw trace/video |
| FE `tests/e2e/studio-first-visit.spec.ts` | Real UI visits, quyền, cross-context, Workspace, currency, restart/unavailable |
| FE `tests/contract/studio-first-visit.test.mts` | RED/GREEN route regression |
| FE `tests/contract/currency-authority-audit.mts` | Demo defaults và diagnostic metric fixture; không phải connected E2E |
| BE `scripts/verify-studio-first-visit.ps1` | ApiHost/SQL test harness, fresh retained DB, real provisioning, SQL/API assertions |

Chỉ **3 production Frontend files** thay đổi, 18 insertions/3 deletions. Không sửa Backend production source, đăng ký, OTP, login, automatic provisioning, runtime currency defaults, schema, generated clients hay compatibility baseline.

## 3. Connected execution

Final run `256af25403c5420f9e3a5d1e2c84d8b4`, **2026-10-21 13:28:38–13:29:50 UTC**. ApiHost ASP.NET Core và SQL Server thật, database riêng `UnicoreCRM_StudioTest_256af25403c5420f9e3a5d1e2c84d8b4` được giữ lại. Hai Workspace được tạo qua **POST /workspaces/initial-provisioning → 201**, không SQL dựng Studio state.

Authentication fixtures clone account/password hash của bootstrap **trong DB mới**, không ghi hash/password vào evidence; mỗi user phải đăng nhập qua HTTP authentication thật. Không tuyên bố đã chạy lại đăng ký/OTP. Membership/role fixture ở Workspace thứ hai chỉ sửa trong DB mới để kiểm tra studio.read không configure; không phải thay đổi production policy.

| Kiểm tra | Actual | Verdict |
|---|---|---|
| First Studio visit | Loading trước GET authority; không sheet flash; sau GET tự mở | PASS |
| Đóng chưa hoàn tất | POST dismiss 200; persisted dismissal | PASS |
| Studio lần hai | Business Information, không sheet | PASS |
| SPA CRM → Studio | Reload authority, không sheet | PASS |
| Refresh | Không tự mở | PASS |
| Logout/login bằng UI | Không tự mở | PASS |
| Browser context mới, login độc lập | Không tự mở; không chia sẻ cookie/storage | PASS |
| Manual sidebar entry | Sheet vẫn mở | PASS |
| Skip cả ba bước qua UI | Mỗi HTTP 200; SQL COMPLETED và skipped IDs | PASS |
| Completed revisit | Không tự mở | PASS |
| Studio chỉ read, Workspace thứ hai NOT_STARTED | Không sheet; SQL vẫn NOT_STARTED | PASS |
| Cấp configure fixture, Workspace thứ hai | Tự mở độc lập; Workspace đầu không bị reset | PASS |
| ApiHost dừng thật | Error UI, không sheet/fabricated NOT_STARTED | PASS |
| Complete cả ba bước qua API riêng | HTTP 200 từng bước; API COMPLETED | PASS |
| Studio currency USD → EUR | UI PATCH 200; refresh/relogin/restart giữ EUR | PASS |
| Studio API / Bootstrap API / SQL | USD lúc provision; EUR sau thay đổi, cùng Workspace | PASS |
| Product form qua SPA sau Studio save | Currency EUR, không VND demo | PASS |
| Backend Product consumer | USD 201 trước đổi; USD 422 sau đổi; EUR 201 sau đổi | PASS |
| Product đã lưu trước đổi | SQL vẫn **10 USD**, không convert/relabel | PASS |
| Workspace thứ hai | SQL baseCurrency vẫn USD | PASS |

**23/23 HTTP/SQL assertions PASS; 16/16 browser assertions PASS; Playwright 1/1 PASS.** Browser contexts dùng Chromium với storage độc lập; chưa chạy Firefox/Safari hay máy vật lý khác. Complete-three-steps được kiểm tra qua API, skip-three-steps được thao tác qua UI; không tráo hai loại bằng chứng.

Evidence: [run](evidence/studio-first-visit-20261021/connected-final/run.json), [HTTP metadata/correlation IDs](evidence/studio-first-visit-20261021/connected-final/http.json), [SQL/API results](evidence/studio-first-visit-20261021/connected-final/results.json), [browser steps + redacted HTTP + currency observations](evidence/studio-first-visit-20261021/connected-final/browser/studio-results.json), [SQL state](evidence/studio-first-visit-20261021/connected-final/final-sql-state.json), [Product SQL](evidence/studio-first-visit-20261021/connected-final/product-currency-sql.json), [SELECT queries](evidence/studio-first-visit-20261021/verification-queries.sql). Screenshots ở thư mục browser.

Các run trước có lỗi calibration của harness (development seed thiếu Studio state; fixture column names; response key/status; selector song ngữ; JSON casing). Chúng được giữ tại `artifacts/studio-first-visit/<runId>` và không được tính PASS. Regression RED route ở trên là bằng chứng lỗi sản phẩm trước sửa. Không sửa expected của test/gate có sẵn để che lỗi.

## 4. Tiền tệ chính thực tế

**Backend Workspace authority mặc định là USD; Frontend demo operational configuration mặc định là VND. Không có một currency hiển thị thống nhất trên mọi surface connected hiện tại.** Studio connected lấy đúng SQL; một số CRM consumers vẫn có authority/fallback gaps bên dưới.

Phân biệt:

- Workspace base currency: cấu hình của Workspace, authority tại Backend Bootstrap/Studio SQL.
- Transaction currency: currency đã lưu của Quote/Order/Invoice/Payment hoặc Product price; không tự đổi theo base currency mới.
- Display currency: currency truyền vào formatter; có thể bị gắn nhãn sai nếu caller bỏ qua transaction currency.
- Demo/fixture currency: seed/local defaults, không phải giá trị SQL connected.

### Authority theo module

| Surface | Authority / quy tắc | USD/VND và tác động khi đổi base |
|---|---|---|
| Backend automatic Workspace provisioning | `ProvisioningDefaults.BaseCurrency = "USD"`; optional explicit intent có thể chọn khác. `StudioDefaults.Create` nhận cùng seed cho Studio | Fresh real endpoint + SQL xác nhận USD; không đổi provisioning |
| Backend development seed | `appsettings.Development.json`, `WorkspaceOptions`: USD | Fixture riêng; legacy development seed không tạo Studio rows nếu chạy sau migration; không dùng fixture đó thay canonical provisioning |
| Studio connected | GET/PATCH `/workspace-configuration`; SQL `StudioConfigurations.LocaleRegionJson.currencies.baseCurrency` | Save có hiệu lực ngay. Không có required publish transition trong runtime hiện tại |
| Workspace Bootstrap | SQL `BootstrapProjections.BaseCurrency` | `EfStudioPersistence.CommitConfigurationAsync` cập nhật Bootstrap cùng transaction với Studio locale-region; API/SQL USD→EUR khớp |
| Frontend demo | `BrowserWorkspaceOperationalConfigurationRepository.ts:48` | VND mặc định; enabledCurrencies ban đầu chỉ VND, giữ riêng theo Workspace demo |
| Frontend connected CRM configuration | `useWorkspaceOperationalConfiguration` → connected projection **hoặc demo repository fallback** | Trước vào Studio/fresh login: VND demo; Studio load/save: đúng USD/EUR từ server. CF01 |
| Quote | FE builder: existing Quote → referenced Deal → workspace config; Backend reads persisted Quote.Currency/Money currencies | Demo quote seed VND; không reprice giao dịch cũ. Quote mutation runtime chưa được nghiệm thu/triển khai trong checkpoint; không mở rộng task |
| Order | FE draft: workspace base; edit/duplicate/source Deal/Quote ưu tiên currency nguồn. Backend reads persisted Order currency/Money | Demo order seed VND; `OrderApiMapper` còn fallback VND nếu currency thiếu; không tự FX |
| Invoice | Existing Invoice → source Order → seller default → workspace base trong FE. Backend reads persisted Invoice currency/Money | Demo seller default VND; **connected seller default là chuỗi rỗng**, không phải BrowserInvoiceConfigurationRepository. CF03 |
| Payment | FE manual/intent initial currency từ workspace config; các record/plan/amount giữ Money.currency. Backend projection trả persisted currency và validate mã | Demo/payment seeds VND; không có global rewrite. Connected configuration save chưa hỗ trợ và không được giả bằng local write |
| Product | FE create/CSV lấy workspace config, edit giữ Product.currency. BE Create/Replace/price projection đọc `IEffectiveWorkspaceBaseCurrencyReader` từ Bootstrap SQL | Consumer thật nhận EUR sau update, rejects USD. Old Product giữ 10 USD. Price projection/Replace có thể bị chặn do mismatch sau đổi base: CF05 |
| Dashboard | `useDashboardReadModel` lấy operational configuration; metric engine tổng hợp snapshot rồi formatter gắn workspace base | Không thấy FX conversion/currency grouping cho revenue/open pipeline; CF01/CF02 |
| Reports | `ReportsPage` + cùng metric engine; currency formatter lấy workspace base, CSV summary chỉ numeric | Cùng rủi ro đổi nhãn/cộng nhiều transaction currencies; CF02. Đây là FE aggregation, không phải SQL Report authority đã nghiệm thu |

### Hardcodes/fallbacks

[Source inventory](evidence/studio-first-visit-20261021/currency-source-inventory.json) có 235 references, file/line/text, loại trừ generated files. Những ví dụ quan trọng:

- USD: canonical Backend provisioning defaults, development Workspace seed/options và migration backfill fallback. USD trong test fixture không thiết lập currency cho Workspace thực tế.
- VND: demo operational defaults; Product/Quote/Order/Invoice/Payment seeds; demo invoice seller default; một số in-memory adapters. Đây là demo/fixture trừ khi caller connected đi vào fallback chung.
- `shared/lib/format/currency.ts`: `formatCurrency` dùng currency được truyền, fallback VND khi rỗng; `formatCurrencyTotals` nhóm theo currency nhưng missing currency vẫn VND; `formatVnd` luôn VND. Đây là formatter, không đọc authority, không chuyển đổi số tiền.
- `ProductDetailPage` còn gọi `formatVnd` cho giá vốn và một số revenue/usage/deal values dù Product/Deal có currency riêng. CF04.
- `OrderApiMapper` và Product catalog legacy draft mapping còn VND fallback khi currency vắng; dữ liệu đầy đủ vẫn giữ transaction currency. Không đổi các fallback này hàng loạt.

## 5. Findings và khuyến nghị (currency không sửa)

| ID | Severity / evidence | Root cause và ảnh hưởng | Khuyến nghị |
|---|---|---|---|
| QS01 | P1, reproduced route RED→GREEN + real connected PASS | Unconditional index redirect bỏ qua persisted state/capability | FIXED như phần 1 |
| CF01 | P1, **runtime reproduced** | `workspaceOperationalConfigurationRuntime.ts` dùng `connectedProjection ?? demoRepository.getSnapshot()`; Studio là deferred loader duy nhất commit projection. Fresh connected CRM quan sát VND/false dù SQL USD, sau Studio EUR/true, fresh login sau restart lại VND/false dù SQL EUR. Draft defaults và display có thể sai; BE Product rejects mismatch | Load Backend config/bootstrap vào operational projection cho CRM trước currency-dependent consumers; connected loading/error riêng, không demo fallback. Cần scoped follow-up, chưa sửa |
| CF02 | P1, **in-process fixture reproduced + code confirmed** | `metricEngine.ts` cộng amount không currency; Dashboard/Reports gắn workspace base. Fixture 100 USD + 100 EUR → 200, không output currency/FX. Đổi base có thể đổi nhãn các tổng cũ dù số tiền không đổi | Group totals theo transaction currency hoặc FX policy có nguồn/timestamp rõ; không chỉ đổi ký hiệu. Không tuyên bố mixed-currency SQL E2E PASS |
| CF03 | P1, **code confirmed; Invoice creation E2E chưa chạy** | Connected `createInvoiceConfiguration()` trả defaultCurrency `""`; InvoiceFormPage dùng `??`, nên manual draft không có source/existing chọn `""` thay vì base. Blank currency có thể chặn form/validation/formatting. Demo seller VND là nguồn khác | Resolve non-empty currency và fail/loading khi owner configuration unavailable. Không triển khai Invoice/Quote mutation mới trong task |
| CF04 | P2, **code confirmed** | ProductDetailPage dùng `formatVnd` cho giá vốn và usage/revenue/deal amounts → gắn nhãn VND cho dữ liệu khác currency; formatCurrency blank fallback cũng có thể che mất currency | Dùng Money/transaction currency; tổng hỗn hợp cần grouping; bỏ currency giả ở connected missing-data paths sau review |
| CF05 | P1 operational constraint, **code confirmed, không gọi là lỗi của validation** | Product Create/Replace/price projection yêu cầu unitPrice currency bằng effective Workspace base. Đổi base không đổi giá đã lưu, nên price projection/replace old Product có thể bị từ chối | Cần UX cảnh báo và policy repricing/eligibility rõ trước đổi base ở Workspace có catalog. Không auto convert hoặc vô hiệu hóa guard |

Không sửa currency code trong task. Không đổi tất cả VND thành USD/EUR. Không dựng conversion rate hoặc viết lại số tiền cũ.

### Dữ liệu đã lưu và giao dịch mới

Backend Studio locale-region mutation chỉ cập nhật Workspace-owned Studio/Bootstrap/audit/outbox/receipt trong transaction; không UPDATE bảng Quote/Order/Invoice/Payment/Product. Product SQL trước đổi vẫn 10 USD sau đổi. Quote/Order/Invoice/Payment preservation ở đây là **code-confirmed**, chưa có mutation E2E riêng cho bốn module đó. UI đọc sai fallback hoặc đổi nhãn không được coi là FX conversion hợp lệ.

Giao dịch mới lấy default theo consumer: Product Backend bắt buộc effective base mới; Quote/Order UI có thể kế thừa source transaction currency; Payment UI có selectable currency; Invoice có seller/source precedence và CF03. Không kết luận mọi giao dịch mới đều chuyển sang base mới chỉ vì Studio save thành công.

## 6. Gates và P0 preservation

| Gate | Result |
|---|---|
| Typecheck (3 gates, budget 180/15/56) | PASS |
| Production build/bundle budget | PASS |
| Guidance / presentation responsibility | PASS |
| Studio Quick Setup / route runtime (13 sections) / module propagation | PASS |
| New Studio route regression | PASS; RED recorded trước sửa |
| Customer Task create session / Lead manual Activity / Deal Quote outcome focused regressions | PASS |
| New real connected Studio suite | PASS 23 HTTP/SQL + 16 browser assertions |
| Currency aggregation correctness | FINDING OPEN; diagnostic tái hiện sai lệch, không gắn business PASS |
| Full quality pipeline / full 580+128+12 P0 suite | NOT_RERUN trong task; giữ trạng thái checkpoint, không tăng phạm vi chứng nhận |
| Compatibility gate | Known FAIL từ checkpoint giữ nguyên; không rerun/accept baseline hay waiver mới |

[Preservation proof](evidence/studio-first-visit-20261021/preservation.json): Backend HEAD `13cd39eba01ebc9dc238f8b0ef98e174c1d4d3f6`, Frontend HEAD `7d41aba85f33c80855f598f3ab24371c0fb510e8` không đổi. **103 pinned P0 source files + 83 immutable/evidence references giữ nguyên SHA-256; 259 files ngoài candidate giữ nguyên hash.** Hai index rỗng. Chỉ thêm working-tree diff của task Studio và report/evidence mới. Không stage/commit/push, sửa accepted baseline, nhận waiver, drop DB hoặc xoá evidence.

## 7. Reproduce

Trong Backend, dùng PowerShell 7, SQL Server test local và .NET SDK đã có:

```powershell
pwsh -NoProfile -File scripts/verify-studio-first-visit.ps1
```

Script luôn cấp GUID database mới, chạy lệnh repo `dotnet run --no-launch-profile --project src/UnicoreCRM.ApiHost -- --migrate`, `--seed-demo`, rồi real initial provisioning cho identity fixtures; giữ DB cuối run. Chỉ chạy một browser job cùng lúc vì Vite test port 3039. Không dùng database production hoặc tái sử dụng DB hiện có. Credentials/connection được đặt trong environment; không đưa raw host/bootstrap logs, keys hoặc storageState vào evidence bàn giao.

Trong Frontend:

```powershell
node --import tsx --loader ./scripts/quality/core/typescript-path-loader.mjs tests/contract/studio-first-visit.test.mts
node --import tsx --loader ./scripts/quality/core/typescript-path-loader.mjs tests/contract/currency-authority-audit.mts
npm run typecheck
npm run build
npm run quality:gate -- --gate quality.studio-quick-setup
npm run quality:gate -- --gate quality.studio-route-runtime
npm run quality:gate -- --gate quality.studio-module-propagation
npm run quality:gate -- --gate quality.guidance-contracts
npm run quality:gate -- --gate quality.presentation-responsibility
```

Mỗi `--gate` chạy riêng. Timestamps, source hashes, SQL/API/browser evidence trong thư mục evidence riêng; các runtime logs chứa thông tin môi trường được giữ ở ignored artifacts, không commit hoặc bàn giao công khai.
