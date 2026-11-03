# Connected Dashboard — UI redesign

Kết quả: **PASS UI/responsive và functional regression trong phạm vi đã chạy**.
Không mở rộng Backend contract. Ba nhóm chỉ số chưa được hỗ trợ vẫn là `BLOCKED_CONTRACT`.
Không tuyên bố toàn bộ quality pipeline PASS.

## A. Vấn đề baseline đã xác minh

- Hai KPI chưa hỗ trợ đứng trước pipeline; trên mobile, pipeline bị đẩy xuống dưới hơn nửa màn hình.
- Bộ chọn Hôm nay/Tuần này/Tháng này chỉ đổi state giao diện, không thay đổi truy vấn snapshot.
- Lead ranking trống chiếm một phần ba hàng chính; công việc chỉ nhận một phần ba chiều rộng.
- Pipeline dùng chữ nhỏ hơn các số đếm; không hiển thị số Deal trong context.
- Border đậm giữa các dòng, nhiều khối cùng trọng lượng; thông tin ngày và tiền chen cùng một dòng.
- Loading chỉ có dòng chữ, không giữ hình dạng của card/list; các số liệu theo kỳ chưa hỗ trợ chiếm diện tích lớn.

Baseline được mở bằng Chromium riêng, dùng dữ liệu Backend thật, chụp trước khi sửa presentation.

## B. Quyết định thiết kế

- Header gọn “Tổng quan”, thời điểm truy vấn và nút refresh thực sự. Bỏ global period selector.
- Bốn KPI: pipeline, việc quá hạn, đến hạn hôm nay, Support đang mở. Desktop bốn cột,
  tablet hai cột, mobile một cột. Label 13px, số 28px, context 12px.
- Pipeline rút gọn theo decimal string, giữ từng currency riêng. Giá trị đầy đủ có trong native
  tooltip và nội dung dành cho trình đọc màn hình; không dùng floating point để rút gọn số tiền.
- Priority strip ngắn với liên kết; công việc/Deal chia 60/40, Activity/Support là vùng phụ.
- Dòng công việc ngắn, hạn xử lý và nhãn quá hạn dựa trên thời điểm read-model. Tên dài được
  giới hạn dòng nhưng vẫn có nội dung đầy đủ cho điều hướng/trợ năng.
- Skeleton tĩnh, thông báo lỗi kèm Retry, trạng thái rỗng/không có quyền rõ ràng.
- Unsupported metrics nằm trong ghi chú cuối trang; không có card số 0 hoặc biểu đồ giả.
- Dùng Button, shell và token màu hiện có; không thêm dependency hoặc animation liên tục.
  Chừa khoảng trống cuối trang để nội dung cuối có thể cuộn lên khỏi launcher AI của shell.

## C. Implementation và phạm vi Git

Các file Frontend sửa trong task này, tương đối với `frontend/unicorecrm-web`:

| File | Mục đích |
|---|---|
| `src/workspaces/crm/presentation/dashboard/ConnectedDashboardView.tsx` | Bố cục, bốn KPI, priority strip, compact rows, footer unsupported |
| `src/workspaces/crm/presentation/dashboard/DashboardPrimitives.tsx` | Metric/panel/skeleton/empty/forbidden/error presentation |
| `src/workspaces/crm/presentation/dashboard/dashboardPresentation.ts` | Định dạng tiền bằng decimal/BigInt và ngày theo workspace |
| `src/guidance/content/crm/screens.ts` | Dashboard guidance song ngữ, version 6, hướng dẫn pipeline hiện tại |
| `tests/contract/dashboard-connected.test.mts` | Precision, ngày, loading/empty/forbidden/error và retired scope |
| `tests/e2e/dashboard-connected.spec.ts` | Responsive, accessibility, navigation và giữ toàn bộ hydration regression |
| `docs/quality/repository-inventory.json`, `docs/quality/repository-inventory.md` | Regenerate inventory theo quy định repository |

Không sửa `DashboardPage.tsx`, `connectedDashboardQueries.ts`,
`useConnectedDashboardReadModel.ts`, request lifecycle, authorization, API contracts hay module nghiệp vụ.
[Hash trước/sau](evidence/connected-dashboard-ui/runtime-after-hashes.json) xác nhận ba file trên không đổi.
Không đổi Backend production code/script, connection string hoặc dữ liệu SQL.
Chỉ thêm báo cáo này và evidence vào Backend docs để đặt cạnh bằng chứng API/SQL đã có.

## D. Before / After

Cùng workspace `unicore-demo-anh-duong`, locale `vi`, cùng dataset và trạng thái READY;
thời điểm “Cập nhật” khác nhau do từng lần đọc thật.

| Viewport | Before | After | Visual / overflow |
|---|---|---|---|
| 1440×900 | [Ảnh](evidence/connected-dashboard-ui/design-before-desktop.png) | [Ảnh](evidence/connected-dashboard-ui/design-after-desktop.png) | PASS / 0px |
| 1366×768 | [Ảnh](evidence/connected-dashboard-ui/design-before-laptop.png) | [Ảnh](evidence/connected-dashboard-ui/design-after-laptop.png) | PASS / 0px |
| 768×1024 | [Ảnh](evidence/connected-dashboard-ui/design-before-tablet.png) | [Ảnh](evidence/connected-dashboard-ui/design-after-tablet.png) | PASS / 0px |
| 390×844 | [Ảnh](evidence/connected-dashboard-ui/design-before-mobile.png) | [Ảnh](evidence/connected-dashboard-ui/design-after-mobile.png) | PASS / 0px |
| 1024×768 | [Ảnh](evidence/connected-dashboard-ui/design-before-compact.png) | [Ảnh](evidence/connected-dashboard-ui/design-after-compact.png) | PASS / 0px |
| 320×844 | [Ảnh](evidence/connected-dashboard-ui/design-before-narrow.png) | [Ảnh](evidence/connected-dashboard-ui/design-after-narrow.png) | PASS / 0px |

Đã xem trực tiếp ảnh desktop/laptop/tablet/mobile/320px, loading và lỗi mạng.
Ảnh được chụp trong viewport của shell có vùng cuộn riêng; thêm ảnh
[mobile work](evidence/connected-dashboard-ui/design-after-mobile-work.png),
[mobile Support](evidence/connected-dashboard-ui/design-after-mobile-support.png),
[320px Support](evidence/connected-dashboard-ui/design-after-narrow-support.png)
để kiểm tra các vùng phía dưới. Trên laptop, bốn KPI cùng hàng và vùng công việc bắt đầu
trong viewport đầu tiên. Các giá trị tiền không tràn ngang.

## E. Verification

| Kiểm tra | Kết quả |
|---|---|
| Fresh login trực tiếp Dashboard; refresh | PASS |
| Pipeline 124.445.000.000 VND / 165 Deal mở | PASS — API/SQL/UI |
| Task quá hạn 48; đến hạn hôm nay 6 | PASS — API/SQL/UI |
| Support mở 15; ưu tiên cao 5 | PASS — API/SQL/UI |
| Task qua bàn phím, Deal detail và cả bốn KPI navigation | PASS |
| Workspace control rỗng; quay lại demo đúng số liệu | PASS |
| Response thật bị trì hoãn không ghi đè workspace mới | PASS |
| Loading skeleton, không lộ số cũ | PASS — browser + unit |
| Empty states | PASS — control workspace + unit |
| Network failure và Retry trong card | PASS — transport abort; không tạo response hoặc số liệu giả |
| Forbidden, không lộ dữ liệu scope đã vô hiệu | PASS — presentation unit |
| Restricted-role/FLS scenario với account thật | NOT_RUN — không thay đổi membership/permissions |
| WCAG A/AA trong `#connected-dashboard`, desktop và mobile | PASS — axe; không thay cho kiểm thử toàn bộ assistive technology |
| Manual screen reader và mọi trình duyệt | NOT_RUN |
| Sáu viewport, giảm chuyển động, không overflow | PASS — browser assertions + xem ảnh |
| Resize không tạo fetch; không quét toàn bộ danh sách | PASS — 22 request lần tải đầu, collection limit 1/5/6, không cursor sweep |
| SQL business data | PASS — count/hash mọi workspace không đổi; 1.050 demo rows giữ nguyên |
| Production build / bundle budget | PASS — entry 290,33 KiB; chunk lớn nhất 490,67 KiB, ngưỡng 500 KiB |

Lần browser acceptance cuối: **1 test PASS, 20,3 giây** (test body 18,2 giây).
Baseline build từ hydration: entry 290,32 KiB, chunk lớn nhất 490,52 KiB;
không có tăng đáng kể ở hai ngân sách được đo. Không tuyên bố đo load/performance Backend.

Bằng chứng: [browser checks và HTTP metadata đã bỏ credentials](evidence/connected-dashboard-ui/after-browser.json),
[SQL expectations](evidence/connected-dashboard-ui/expected.json),
[API summaries](evidence/connected-dashboard-ui/after-api-summary.json),
[data preservation](evidence/connected-dashboard-ui/after-data-preservation.json),
[loading](evidence/connected-dashboard-ui/design-loading.png),
[network failure](evidence/connected-dashboard-ui/design-network-error.png).

## F. Quality và regression

PASS: presentation responsibility, Dashboard runtime + contract/presentation tests, read models,
guidance, strict core, strict type regions, API contract, API management boundaries,
frontend/backend separation, repository inventory, production build.

FAIL tồn đọng, giữ nguyên:

- Full `npm run verify` dừng tại TypeScript: `deal-quote-transition-outcome.test.mts:42`
  có possibly-null model; `studio-first-visit.spec.ts:50,51` truyền string-or-undefined.
  Không còn diagnostic của các file Dashboard. Các gate sau bước đầu của full verify là NOT_RUN.
- `quality.backend-readiness`: hai tài liệu Studio chưa có status entry.
- `quality.quality-pipeline`: assertion 347 gate và 345 indexed gate mâu thuẫn sẵn có.

[Kết quả gate](evidence/connected-dashboard-ui/quality-results.json) và
[output full verify](evidence/connected-dashboard-ui/full-verify-output.txt).
Không sửa các test/gate ngoài phạm vi để làm xanh pipeline.

## G. Bảo toàn local changes và tái kiểm tra

[So sánh diff](evidence/connected-dashboard-ui/unrelated-diff-preservation.json) xác nhận
62 diff tracked có sẵn ngoài phạm vi không thay đổi. Không reset, xóa source, commit hoặc merge.
Những file runtime đang untracked từ task hydration vẫn được giữ tại chỗ và kiểm tra hash.
Artifacts tạm được giữ trong vùng ignored của harness; chỉ evidence có chủ đích nằm trong docs.

Từ Backend, dùng harness read-only hiện có, không chạy seed:

```powershell
pwsh -NoProfile -File scripts/verify-connected-dashboard.ps1 -Phase after
```

Từ Frontend:

```powershell
npm run quality:gate -- --gate quality.dashboard-runtime
npm run quality:gate -- --gate quality.guidance-contracts
npm run build
npm run verify
```

Harness dùng host Development riêng, background delivery tắt, Chromium/Vite riêng;
chỉ authentication/read audit là tác động thông thường ngoài các CRM tables được đối chiếu.

## H. Remaining issues

Period Revenue, Period New Leads và Ranked Priority Leads vẫn thiếu authoritative contract.
Không triển khai contract phase tiếp theo, không đổi KPI formula hoặc dùng cache làm fallback.
Các lỗi full verification nêu trên và shell/AI launcher hiện có thuộc phạm vi riêng.
