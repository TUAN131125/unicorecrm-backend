# UI notices, branding and logout/login verification

Kết quả: **UI cleanup PASS; Login → Logout → Login lại PASS. Bug auth được báo: NOT_REPRODUCED.** Không sửa auth hoặc Backend khi chưa có bằng chứng lỗi. Chưa commit, push hoặc merge.

## A. Current State

- `ReadOnlyAccessBanner` là notice thông tin chung do `PermissionRouteGuard` hiển thị khi được phép đọc module nhưng không có quyền ghi. Trước sửa không có timer hoặc cơ chế dismiss.
- Banner “Chế độ chỉ xem / Theo quyền hiện tại” tái hiện được bằng tài khoản QA người dùng cung cấp trên Customer, vẫn hiển thị sau 4,5 giây. Tài khoản demo có quyền ghi ở các module đã kiểm tra nên không hiển thị notice này.
- Dòng dưới logo nằm trong `Sidebar.tsx`, render `navigationProfile.labelVi/labelEn`. Profile custom hiển thị “Không gian theo quyền”.
- Tài khoản QA có membership trong workspace hiện có trước khi chạy UI. Preflight chỉ đọc memberships và đăng nhập/đăng xuất qua API; không provision workspace hoặc tạo user.
- Baseline đã chạy 3 vòng logout/relogin cùng account QA, refresh sau mỗi lần và chuyển Demo → QA → Demo thành công. Không tái hiện được lỗi đăng nhập lại.

## B. Root Cause

**Banner:** notice luôn được render trong suốt thời gian route ở chế độ đọc, không có lifetime. Cần quản lý lifetime tại component notice chung; quyền và error state vẫn thuộc guard.

**Branding:** dòng phụ là presentation của navigation profile, không phải dữ liệu thương hiệu bắt buộc. Bỏ dòng render này; giữ profile dùng để chọn menu và metadata guidance.

**Logout/login:** `NOT_REPRODUCED`; chưa xác định được root cause của tình huống người dùng gặp. Mã local đã có generation guards, cleanup session/workspace và adapter giữ access token trong memory. Browser hiện tại xác nhận flow này hoạt động; không coi đây là bằng chứng giải thích lỗi trên một browser/profile khác.

## C. Implementation

Frontend paths tương đối với `frontend/unicorecrm-web`:

| File | Thay đổi |
|---|---|
| `src/components/access/ReadOnlyAccessBanner.tsx` | Lifetime 4.000ms, cleanup `clearTimeout` khi unmount; collapse/fade 180ms, reduced motion 0ms |
| `src/components/PermissionRouteGuard.tsx` | Key notice theo pathname + module, không dùng search/filter; giữ nguyên permission branches và read-only metadata |
| `src/app/shell/layout/Sidebar.tsx` | Xóa dòng phụ dưới UnicoreCRM; chuyển navigation-profile/guidance metadata lên sidebar owner, không đổi menu/quyền |
| `src/guidance/content/crm/screens.ts` | Guidance VI/EN và version cho navigation/read-only notice |
| `src/guidance/content/crm/extendedScreens.ts` | Guidance VI/EN và version cho Customer/Segments/Health |
| `tests/quality/integration/check-role-based-navigation.mts` | Assert HTML branding không chứa nhãn profile; giữ các kiểm tra menu và permission hiện có |
| `tests/e2e/ui-cleanup.acceptance.ts` | Baseline/after UI, timer thực, filter/refresh, ba vòng relogin, account switch; credentials chỉ đọc từ environment |
| `tests/e2e/dashboard-connected.spec.ts` | Nhánh opt-in chạy acceptance trong harness read-only có sẵn |
| `docs/quality/repository-inventory.json`, `.md` | Regenerate inventory |

Backend chỉ thêm báo cáo và evidence, không sửa production code. Auth fix: **không có**, vì flow được báo chưa tái hiện.

Thông báo tự ẩn chỉ áp dụng cho `ReadOnlyAccessBanner`, là hướng dẫn mode không chặn thao tác. Permission-denied route, access verification failure, notice quyền chi tiết/FLS, error, warning, loading và empty-state owners không đổi. Không có CSS ẩn vĩnh viễn hoặc thay đổi permission/business rule.

Notice được mount lại khi vào trang khác/refresh. Filter hoặc query trên cùng pathname giữ cùng notice identity và không tạo timer mới. Khi notice ẩn, route vẫn mang `data-access-mode="read-only"`; giới hạn thao tác không biến mất.

## D. Verification

Dùng Chromium riêng qua ApiHost Development host-only loopback 5098 và Vite 3039. Không điều khiển desktop/browser cá nhân. [Baseline metadata](evidence/ui-cleanup/cleanup-before-checks.json), [after metadata](evidence/ui-cleanup/cleanup-after-checks.json).

| Hạng mục | Kết quả | Bằng chứng |
|---|---|---|
| Customer read-only banner | PASS | Baseline còn sau 4,5s; after hiện khi vào trang rồi biến mất sau thời gian chờ |
| Refresh / quay lại Customer | PASS | Notice hiện lại rồi tự ẩn |
| Filter/tìm kiếm cùng trang | PASS | Sau khi ẩn, nhập/xóa tìm kiếm không làm notice hiện lại |
| Similar banners | PASS | Customer Segments và Customer Health dùng cùng notice, đều tự ẩn; Orders/Payments được mở nhưng không có notice với account này |
| Branding desktop/collapsed/mobile | PASS | Không còn dòng phụ; UnicoreCRM và layout sidebar hiển thị đúng tại 1366×768 và 390×844 |
| Login | PASS | Account QA do người dùng cung cấp và account demo đều vào CRM qua Backend thật |
| Logout | PASS | API 200, session/workspace selection keys được xóa theo flow sẵn có |
| Login again | PASS | 3 vòng cùng account QA trước sửa và 3 vòng sau sửa, không xóa cache/cookie thủ công |
| Refresh after relogin | PASS | Mỗi vòng refresh thành công, refresh-session API trả 200 |
| Different account / workspace | PASS | Demo → QA → Demo, workspace có sẵn và dữ liệu demo đúng khi quay lại |
| Bug logout/login được báo | NOT_REPRODUCED | Không có redirect loop, blank page hoặc JavaScript page error trong các lượt thành công |
| Permission/error preservation | PASS qua scope review và existing contract gates | Chỉ notice thông tin được sửa; permission/FLS owners và rules giữ nguyên hash |
| Fault injection cho error/permission UI | NOT_RUN | Không thay quyền để tạo lỗi; không khẳng định đã test mọi error case bằng browser |
| SQL preservation | PASS | Count/hash 9 bảng CRM theo toàn bộ workspace giống trước/sau; demo vẫn có 1.050 records |
| Typecheck | PASS | `npm run typecheck`: strict-core, strict-type-regions và type-safety-budget |
| Production build | PASS | Vite build + bundle budget, largest chunk 491,55 KiB / limit 500 KiB |
| Targeted gates | PASS trong phạm vi liên quan | Auth/session, auth UX, identity API, read-only policy, role navigation, shell, guidance, presentation, API và separation |
| Full verify/lint | FAIL — pre-existing | Ba lỗi TypeScript ở test Deal/Studio; fail-fast tại gate đầu |

After acceptance cuối: **1 test PASS, 41,9s** (45,6s gồm khởi tạo). Metadata sau sửa có 5 lần `POST /auth/sessions` 200, 5 lần logout 200 và 4 lần refresh 200; `pageErrors` rỗng. Không có credential/header/token trong metadata.

Một lượt browser trước đó chạy đồng thời với thử nghiệm JSDOM gặp thiếu bộ nhớ hệ thống; kết quả cuối ở trên là lượt chạy riêng thành công. Không dùng kết quả thất bại đó làm bằng chứng PASS.

[Kết quả quality](evidence/ui-cleanup/quality/summary.json), [typecheck](evidence/ui-cleanup/quality/ui-cleanup-typecheck.log), [build](evidence/ui-cleanup/quality/ui-cleanup-build.log), [full verify](evidence/ui-cleanup/quality/ui-cleanup-verify.log).

Lỗi cũ ngoài phạm vi được giữ nguyên:

- Global TypeScript: `tests/contract/deal-quote-transition-outcome.test.mts:42` có thể null; `tests/e2e/studio-first-visit.spec.ts:50,51` có thể undefined.
- `quality.backend-readiness`: hai tài liệu Studio connections thiếu status entry.
- `quality.quality-pipeline`: assertion `347 !== 345`.

## E. Screenshots / Evidence

| Trạng thái | Bằng chứng |
|---|---|
| Branding trước/sau | [Trước](evidence/ui-cleanup/cleanup-before-branding.png) · [Sau](evidence/ui-cleanup/cleanup-after-branding.png) |
| Customer banner trước sửa | [Banner baseline](evidence/ui-cleanup/cleanup-before-customers-notice.png) |
| Customer sau sửa, lúc mở / đã ẩn | [Lúc mở](evidence/ui-cleanup/cleanup-after-customers-notice.png) · [Sau timeout](evidence/ui-cleanup/cleanup-after-customers-hidden.png) |
| Similar notices | [Segments](evidence/ui-cleanup/cleanup-after-customers-segments-hidden.png) · [Health](evidence/ui-cleanup/cleanup-after-customers-health-hidden.png) |
| Sidebar responsive | [Collapsed](evidence/ui-cleanup/cleanup-after-collapsed.png) · [Mobile](evidence/ui-cleanup/cleanup-after-mobile.png) |
| Logout / relogin | [Logout](evidence/ui-cleanup/cleanup-after-logout.png) · [Relogin](evidence/ui-cleanup/cleanup-after-relogin.png) |
| Business data integrity | [SQL count/hash](evidence/ui-cleanup/sql-preservation.json) |
| Local changes / protected auth owners | [Scope preservation](evidence/ui-cleanup/scope-preservation.json) |

Ảnh có account QA được người dùng cho phép kiểm thử; không có mật khẩu. Không ghi credential vào source, test hoặc cấu hình. Các environment variables chứa credential được xóa sau harness.

## F. Git Status và tái kiểm thử

Frontend HEAD `814cc4685b6d8f8ead2fc945f4607a2d0168320d`; Backend HEAD `13cd39eba01ebc9dc238f8b0ef98e174c1d4d3f6`. Checkout có nhiều thay đổi chưa commit từ trước; so sánh hash toàn bộ file baseline không phát hiện thay đổi ngoài phạm vi liệt kê. Auth/permission owners được kiểm tra giữ nguyên từng byte.

Tất cả thay đổi task vẫn chưa commit; không push, merge, reset hoặc ghi đè thay đổi có sẵn. Không seed/reset/migrate, không đổi SQL connection, workspace, quyền hoặc business rules. Các thao tác auth ghi session/audit hợp lệ qua Backend; bảo toàn SQL nói đến dữ liệu CRM, không phải toàn DB bất biến từng byte.

Có thể chạy lại bằng account development hiện có; nhập credential vào environment tạm, không đặt vào file:

```powershell
Set-Location D:\Project_All\UnicoreCRM\backend
$env:UNICORECRM_UI_CLEANUP_PHASE = 'after'
$env:UNICORECRM_UI_READONLY_EMAIL = Read-Host 'QA development email'
$qaSecret = Read-Host 'QA development password' -AsSecureString
$env:UNICORECRM_UI_READONLY_PASSWORD = [Net.NetworkCredential]::new('', $qaSecret).Password
try {
    pwsh -NoProfile -File scripts/verify-connected-dashboard.ps1 -Phase after
} finally {
    Remove-Item Env:UNICORECRM_UI_CLEANUP_PHASE, Env:UNICORECRM_UI_READONLY_EMAIL, Env:UNICORECRM_UI_READONLY_PASSWORD
    $qaSecret.Dispose()
}
```

Harness kiểm tra local Development DB đã xác minh, tắt background delivery, so sánh SQL trước/sau và dừng các process riêng khi kết thúc. Preflight chặn kiểm thử account không có workspace active, không tự tạo workspace hoặc user.
