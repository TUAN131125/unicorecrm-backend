# Dashboard sidebar navigation

Kết quả: **PASS navigation và Connected Dashboard acceptance** trên workspace demo hiện có.
Không đổi Backend, role assignment, capability, route guard hoặc dữ liệu CRM; không seed.

## A. Root cause

Dashboard đã có JSX và canonical route. Đây không phải lỗi thiếu item hay lỗi CSS/mobile.
Trong CRM, DOM không có Dashboard vì cả item và nhóm `crm-overview` dùng
`visible("dashboard")`.

Chuỗi điều kiện đã đối chiếu với code và authorization context thật:

| Điều kiện | Trước | Sau |
|---|---|---|
| Backend `productSpaces` có `crm` | true | true |
| Backend cấp `dashboard.read` | false | false |
| `canAccessModule("dashboard")`, ánh xạ tới capability trên | false | false, không sửa |
| Role template IDs / profile | `[]` / `custom` | Giữ nguyên |
| `isPrimaryNavigationModule(..., "dashboard")` | true: custom cho phép mọi module có quyền | Không đổi registry |
| Dashboard link trong sidebar CRM | 0 | 1 |

`dashboard.read` có trong catalog/demo role của Frontend nhưng không thuộc tập capability
Workspace đang được Backend chấp nhận. Vì vậy lọc theo capability này không nhất quán với
Dashboard thực tế. Workspace capability manifest không có Dashboard flag để chặn nó.

Đánh giá route: `RequireWorkspaceContext` xác minh membership/bootstrap/authority;
`CanonicalProductSpaceGuard` yêu cầu membership active và CRM admission từ authority.
Dashboard là CRM home, không khai báo một operation/capability riêng tại child route.
Các widget và owner API áp dụng quyền đọc riêng. Không suy ra quyền chỉ từ việc URL mở được;
đã kiểm tra các guard và contract hiện tại trước khi sửa điều kiện navigation.

[Authorization trước](evidence/dashboard-navigation/sidebar-before-authority.json) và
[sau](evidence/dashboard-navigation/sidebar-after-authority.json) lấy từ `/access/context` thật.

## B. Implementation

Các file Frontend sửa trong task này:

- `src/app/shell/layout/Sidebar.tsx`: Dashboard visible khi workspace active, authority thuộc đúng
  Workspace ID, membership đã hydrate và `productSpaces` cho phép CRM. Đưa nhóm Tổng quan lên
  trước các nhóm CRM khác. Giữ nguyên `SidebarItem`, icon `LayoutDashboard`, label, canonical
  workspace path và active/mobile handlers.
- `src/guidance/content/crm/screens.ts`: Dashboard guidance version 7; mô tả đúng điểm vào và quyền.
- `tests/quality/integration/check-role-based-navigation.mts`: kiểm tra render các profile,
  Connected không có legacy capability, duplicate, active state và các trường hợp denied/stale.
- `tests/e2e/dashboard-connected.spec.ts`: thêm chế độ acceptance navigation, before/after,
  actual authority evidence; giữ regression hydration hiện có.
- `docs/quality/repository-inventory.json`, `docs/quality/repository-inventory.md`: regenerate.

Không sửa `SidebarItem`, role navigation registry, capability catalog/role templates,
`crmWorkspaceRoutes`, canonical routing, `PermissionRouteGuard` hoặc Dashboard UI/read model.
Những hunk Studio có sẵn trong Sidebar được giữ nguyên, không thuộc thay đổi mới.

## C. Authorization assessment

| Trường hợp | Hành vi |
|---|---|
| Administrator, Sales, Finance, Operations, Customer Success/Support, Viewer | Có entry khi authority cho phép CRM và workspace hợp lệ; không phụ thuộc admin |
| Custom/Connected profile không có `dashboard.read` nhưng có CRM admission | Hiển thị CRM home; từng widget vẫn kiểm tra owner read permissions |
| Không được vào CRM | Không hiển thị Dashboard |
| Membership chưa hydrate / suspended | Không hiển thị Dashboard |
| Authority còn thuộc workspace cũ | Không hiển thị Dashboard |
| Product space Studio/People | Không hiển thị Dashboard |

Role/capability hash không thay đổi qua các lần đọc của từng workspace; không có request ghi
`/access/*` hoặc `/workspaces/*`. Không cấp thêm `dashboard.read`, không thêm admin role.
Calendar/Notifications và các menu khác giữ nguyên điều kiện hiện có; task này chỉ sửa Dashboard.

## D. Verification

| Kiểm tra | Kết quả |
|---|---|
| Desktop 1440×900, laptop 1366×768 | PASS: một item đầu nhóm Tổng quan, canonical path, active state |
| Mobile 390×844 | PASS: mở drawer, click Dashboard và tự đóng |
| Collapse/expand | PASS: icon/link vẫn xuất hiện, mở được Dashboard |
| Deals → Dashboard; Back/Forward; refresh | PASS |
| Workspace selector → control → demo | PASS: key mới trong link, control zero và demo đúng số liệu |
| Studio/People | PASS: không có Dashboard item; navigation cũ giữ nguyên |
| Unit profile/denied/stale/membership cases | PASS |
| Role hạn chế với account Backend thật | NOT_RUN: không thay đổi membership hoặc quyền để tạo kịch bản |
| Connected hydration regression đầy đủ | PASS: fresh login, refresh, scoped data, delayed response, error/retry, navigation và responsive |
| SQL/API/UI | PASS: pipeline 124.445.000.000 VND / 165 Deal, Task quá hạn 48, đến hạn hôm nay 6, Support mở 15 / ưu tiên cao 5 |
| CRM data preservation | PASS: count và SHA-256 mọi workspace trước/sau không đổi; demo giữ nguyên 1.050 rows |
| Typecheck group | PASS: strict core, strict regions và type-safety budget |
| Production build / bundle budget | PASS |

Navigation browser run: **1 PASS, 21,4s**. Full hydration run sau sửa: **1 PASS, 28,1s**.
Browser là Chromium context riêng của Playwright, không điều khiển desktop/browser cá nhân.

PASS: `quality.role-based-navigation`, `quality.workspace-navigation-language`,
`quality.access-control-contracts`, `quality.sidebar-navigation-migration`,
`quality.guidance-contracts`, `quality.dashboard-runtime`, API contract/boundaries,
frontend/backend separation và repository inventory.

FAIL ngoài phạm vi, không nới test:

- `quality.route-module-loads`: runner Node không nạp được `DealKanbanCard.css` của module Deal.
  File/import này không bị sửa trong task navigation.
- Full `npm run verify`: ba diagnostic TypeScript có sẵn tại
  `deal-quote-transition-outcome.test.mts:42`, `studio-first-visit.spec.ts:50,51`.
  Không có diagnostic của các file sửa trong task; các gate phía sau bước đầu là NOT_RUN.
- Gate pipeline vẫn có assertion 347/345 mâu thuẫn; backend-readiness vẫn thiếu hai Studio
  document status entries. Không tuyên bố full pipeline PASS.

Bằng chứng: [navigation browser checks](evidence/dashboard-navigation/after-browser.json),
[hydration regression](evidence/dashboard-navigation/hydration-regression.json),
[SQL/API](evidence/dashboard-navigation/after-api-summary.json),
[SQL preservation](evidence/dashboard-navigation/after-data-preservation.json),
[hydration preservation](evidence/dashboard-navigation/hydration-data-preservation.json),
[quality](evidence/dashboard-navigation/quality-results.json),
[route-smoke output](evidence/dashboard-navigation/route-smoke-output.txt),
[full verify output](evidence/dashboard-navigation/full-verify-output.txt).

## E. Screenshots

Cùng workspace, viewport và locale:

| Viewport | Before | After |
|---|---|---|
| 1440×900 | [Before](evidence/dashboard-navigation/sidebar-before-desktop.png) | [After](evidence/dashboard-navigation/sidebar-after-desktop.png) |
| 1366×768 | [Before](evidence/dashboard-navigation/sidebar-before-laptop.png) | [After](evidence/dashboard-navigation/sidebar-after-laptop.png) |
| 390×844, drawer mở | [Before](evidence/dashboard-navigation/sidebar-before-mobile.png) | [After](evidence/dashboard-navigation/sidebar-after-mobile.png) |

[Collapsed sidebar sau sửa](evidence/dashboard-navigation/sidebar-after-collapsed.png).

## F. Git và reproduction

Không commit, push, merge hoặc reset. Đã đối chiếu và giữ nguyên
[61 diff tracked ngoài phạm vi](evidence/dashboard-navigation/local-diff-preservation.json).
[Hash source](evidence/dashboard-navigation/source-hashes.json) xác nhận role navigation registry,
Connected Dashboard view, query và hydration hook không đổi. Chỉ bổ sung báo cáo/evidence trong
Backend docs; không sửa Backend source/scripts/configuration.

Từ `D:\Project_All\UnicoreCRM\backend`, dùng harness read-only hiện có:

```powershell
$env:UNICORECRM_DASHBOARD_NAV_PHASE = 'after'
try {
  pwsh -NoProfile -File scripts/verify-connected-dashboard.ps1 -Phase after
} finally {
  Remove-Item Env:UNICORECRM_DASHBOARD_NAV_PHASE
}
```

Bỏ biến trên để chạy toàn bộ regression Dashboard. Không chạy seed. Credentials lấy từ secrets
Development đã cấu hình, không ghi vào source/evidence. Artifacts tạm nằm trong vùng ignored của
harness; screenshots và metadata đã loại credentials được giữ có chủ đích trong docs.
