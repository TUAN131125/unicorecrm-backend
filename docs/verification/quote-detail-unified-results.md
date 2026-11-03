# Quote Detail V4 — kết quả triển khai

Yêu cầu: `a4f93520-c606-47c3-8c99-e3abd57acc39`, quyết định sản phẩm D1/D2/D3 ngày 2026-11-02. Thực hiện trên working tree local; giữ các thay đổi O1/O2/P0 có sẵn. Không commit/push/reset/merge/rebase, không sửa pins/threshold 60/53, không ghi graduation ZIP.

## Kết quả và nguyên nhân

- B1 vẫn xuất hiện trong V3 vì bỏ duyệt chỉ áp dụng khi Workspace bật ordinary-sales và không bật AlwaysRequireApproval. Đã bỏ điều kiện này trên B5 issuance/readiness, B2 delivery, ordinary acceptance, automatic Order binding và các commercial prerequisites của B6. Quote hợp lệ được phát hành trực tiếp ở mọi Workspace/mức giá; không sinh APPROVED/NOT_REQUIRED hay người duyệt giả.
- Lịch sử approval vẫn giữ nguyên, kể cả khi sửa nội dung draft. FLS tiếp tục che metadata không được đọc; commercial edit không ghi các trường approval đã nghỉ sử dụng. Các guard issuer, Workspace, record/FLS, Buyer, version, fingerprint, expiry/archive/revision vẫn có hiệu lực. B6 identity/source/proof, credit, payment instructions, fulfillment và operational review giữ độc lập.
- Quote Detail có một workflow tiến triển. CTA duy nhất nằm ở header, cùng một owner điều khiển form; các bước hoàn tất thu gọn và bước hiện tại mở. Không còn ba card hoặc progress/primary-action owner trùng lặp. Có cột Thuế, số tiền chính thức, thỏa thuận thanh toán và lịch sử theo múi giờ. Modal không tự mở lại sau chuyển bước; lỗi validation hiện trong modal và sửa được.
- Đọc Order thật qua public query của Orders; hiển thị số, state và exact total. Intent tự xác nhận không được dùng để gắn CONFIRMED cho DRAFT lịch sử. O1/O2 tiếp tục tạo CONFIRMED + ACTIVE Plan trong transaction/recovery hiện có; frontend không gọi thêm lệnh xác nhận, không tự Deal WON/thu tiền.
- Form mới trước đây ưu tiên Deal.currency và có thể giữ nguồn rỗng khi chuyển từ direct-create sang URL có dealId. Đã ưu tiên cấu hình Studio hiện hành, chờ cấu hình authoritative, reset nguồn theo lifecycle và dùng Deal detail từ query hiện hành. Giá khác tiền tệ không được sao chép; có giải thích để chọn sản phẩm cùng tiền tệ hoặc nhập giá mới. Không có FX engine.
- Quote cũ giữ currency/snapshot gốc. Detail/PDF/Order không đổi nhãn tiền theo Studio. Decimal strings được giữ đến formatter chính xác. Shared formatter bỏ default VND khi thiếu currency và dùng ISO minor units, sửa lỗi KWD 3 chữ số lẻ.

Quyết định/boundary chi tiết: [Direct issuance and Studio currency](../development/quote-direct-issuance-and-currency.md). Các hợp đồng B1 legacy vẫn giữ để tương thích và đọc lịch sử; active UI/new commercial workflow không gọi chúng. Không thêm public conversion endpoint; disposition cũ của convertAcceptedQuoteToOrderDraft không thay đổi.

## HTTP / SQL / Chromium

Server SQL: `DESKTOP-ICH225R`. Environment của task ApiHost: Development, connection được override vào database QA riêng và guard tên `UnicoreCRM_OrderConfirmationTest_*`.

- Full run `UnicoreCRM_OrderConfirmationTest_f76f54d1b7fa44f79b3937e24fe9666d`: **117 checks PASS**, không có check thất bại. Bao gồm direct issuance với policy cũ AlwaysRequireApproval, mức giá vượt hạn mức duyệt cũ, acceptance/auto-confirm guards độc lập, staff/customer consent, replay, concurrency, stale version, 401/403, foreign Workspace/document, FLS, tampering, expiry, payment/ack fault rollback và restart.
- UI cuối `UnicoreCRM_OrderConfirmationTest_10b76d41f35748ba8440c9a2cd40979a`: **PASS** qua Chromium đầy đủ ở chế độ headless (native PDF renderer báo loadState=success), HTTP/SQL thật, không điều khiển Windows desktop. Kiểm tra 1440/1280/768/390, 24px semibold title, một primary CTA, PDF preview/đóng iframe + download/hash, từ chối khách hàng với lý do và state đóng đúng bước, staff và customer-link, tạo link rồi ghi nhận staff, lỗi 422 sửa trong modal, SENT lịch sử thiếu PDF, VND Deal → Studio USD, Studio USD/VND reload, Workspace A/B/A, refresh/relogin, đọc Order thực tế.
- **12 SQL invariants đều 0 vi phạm**: không trùng Order/current Plan, không lệch CONFIRMED–ACTIVE Plan, fingerprint/receipt đúng, không gán human cho policy-confirm, không ghi payment satisfied, không durable auto-draft hoặc evidence/event/receipt mồ côi.
- PDF VND/USD: download chính thức bằng Chromium, bytes/SHA-256 khớp issuance; download lại sau ApiHost restart vẫn cùng hash. Đã trích text PDF để xác nhận ISO và USD total `244.43`; số tiền không chỉ được kiểm tra trên HTML.

Bằng chứng: `task-runtime/quote-detail-unified/` tại workspace root; `summary.json`, `changes-from-entry.json`, từng run có `http.json`, `results.json`, `sql-target.json`, `sql-migrations.json`, `sql-invariants.json`, `sql-orders.json`, `sql-plans.json`, `browser/result.json`. HTTP evidence không ghi bearer/password.

## Bảo toàn SQL Development

Target đọc: `DESKTOP-ICH225R / UnicoreCRM_Development`. Không chạy migration, reset, reseed hoặc test fixture trên target này. Bốn chain Quotes/Orders/Payments/Workflows hiện ALIGNED; không có pending migration mới của task.

- 299 Quote và 0 Order; ba Quote chụp lúc bắt đầu giữ nguyên version **2/2/1** và DRAFT.
- Owner journals trong cửa sổ task: **0 Quote commands, 0 Quote outbox, 0 Quote updates, 0 Orders**.
- Baseline checksum P0 cũ **FAIL khi dùng xuyên task**: có lần `updateQuoteDraftCommand` lúc **06:50:43 UTC**, trước snapshot source V4 **07:10:40 UTC**, cùng các read-audit append. Trạng thái version 2 đã có trong ảnh/API trước V4. Không sửa dữ liệu hoặc đổi baseline để ép kết quả.
- Giữ nguyên strict mismatch trong `development-preservation/preservation.json`. Bằng chứng hiện tại riêng: `development-preservation/task-journal-preservation.json` và `development-preservation-journal.log` (PASS theo captured versions + owner journals). Không tuyên bố checksum toàn bộ 86 bảng giống baseline P0 cũ.

## Kiểm tra chất lượng

| Kiểm tra | Kết quả |
|---|---|
| Backend build | PASS, 0 warning/error |
| Quote/Order/PaymentPlan focused tests | PASS 347/347 |
| Frontend typecheck / project lint / production build | PASS |
| Bundle budget | PASS, không đổi budget |
| API contract | PASS: 365 operations, 339 ready, 26 blocked; generated contract/pins không đổi |
| Quote unit, direct-sale, presentation, delivery, API boundary, Order integrity | PASS |
| Exact money / historical DRAFT state presentation | PASS |
| Presentation responsibility / mutation-command authority | PASS |
| Guidance contracts/runtime, bilingual localization | PASS |
| Full repository verify | NOT_RUN lại; kết quả ở đây là các check đã liệt kê, không tuyên bố toàn repository xanh |

Test kỳ vọng B1 cũ được cập nhật theo D1: giữ history thay vì xóa, không chặn bằng retired approval metadata. Commercial-field FLS, projected redaction, issuer denial, immutable states và stale version vẫn được kiểm tra. Không thay threshold/pins hoặc xóa evidence lỗi ban đầu.

## Ảnh trước/sau

Ảnh trước lấy từ Development ở đầu task; ảnh sau dùng fixture QA riêng. Hai bộ dùng dữ liệu khác nhau để kiểm tra bố cục; kiểm tra tiền dùng HTTP/SQL/PDF assertions riêng.

- Trước: `before/browser/quote-detail-1440.png`, `quote-detail-1280.png`, `quote-detail-768.png`, `quote-detail-390.png`.
- Sau tại `UnicoreCRM_OrderConfirmationTest_10b76d41f35748ba8440c9a2cd40979a/browser/`: `quote-draft-1440.png`, `quote-draft-1280.png`, `quote-draft-768.png`, `quote-draft-390.png`; `workflow-draft-*.png`; `workflow-confirmed-*.png`; `quote-confirmed-order-desktop.png`, `quote-confirmed-order-mobile.png`.
- Tương tác: `issued-pdf-preview.png`, `customer-rejected-workflow.png`, `staff-acceptance-dialog.png`, `staff-validation-error.png`, `legacy-sent-official-pdf.png`, `deal-vnd-new-quote-studio-usd.png`, `quote-create-studio-usd.png`, `quote-create-studio-vnd.png`, `workspace-b-usd-create.png`, `historical-usd-under-vnd-studio.png`.
- Các lượt thất bại ban đầu vẫn có `browser/result.json` và `failure-*.png` trong database-run folder tương ứng.

## Gates

PASS dưới đây có phạm vi implementation và connected QA biệt lập; không có ý nghĩa bật production acceptance/provider authority.

| Gate | Kết quả |
|---|---|
| QUOTE_APPROVAL_REMOVAL | PASS |
| QUOTE_DIRECT_PDF_ISSUANCE | PASS |
| QUOTE_DETAIL_V4_REDESIGN | PASS |
| UNIFIED_QUOTE_WORKFLOW | PASS |
| QUOTE_ACTION_HIERARCHY | PASS |
| QUOTE_TYPOGRAPHY | PASS |
| QUOTE_DOCUMENT_ACTIONS | PASS |
| CUSTOMER_ACCEPTANCE_UI | PASS |
| QUOTE_ORDER_LINKAGE | PASS |
| STUDIO_CURRENCY_AUTHORITY | PASS |
| QUOTE_CREATE_CURRENCY | PASS |
| QUOTE_DETAIL_CURRENCY | PASS |
| HISTORICAL_CURRENCY_INTEGRITY | PASS |
| QUOTE_PDF_CURRENCY | PASS |
| QUOTE_FE_BE_CONNECTED | PASS |
| WORKSPACE_AUTHORIZATION | PASS |
| SQL_PRESERVATION | PASS |
| RESPONSIVE_VISUAL_CHECK | PASS |
| DEAD_CODE_CLEANUP | PASS |

## Giới hạn thực tế

- Live Gmail: **NOT_RUN** vì không có credentials/recipient được cung cấp; không giả provider success hoặc gửi thư ngoài fixture.
- Không có FX được phê duyệt: giữ nguyên số tiền/ISO gốc và giải thích giới hạn. Không tự quy đổi.
- Không bật production VERIFIED acceptance/identity authority. Ordinary sales consent chỉ đi tiếp khi policy hiện hành cho phép; signer/credit/payment/fulfillment/operational blockers còn hiệu lực.
- Các task-owned ApiHost/Vite được dừng sau test. Không triển khai/commit/push trong task.

## File thay đổi so với entry snapshot

Tổng **66 file**: 48 sửa, 14 thêm, 4 bỏ. Danh sách loại trừ các evidence lịch sử vốn không nằm trong snapshot source.

- MODIFIED: `backend/docs/development/quote-automatic-confirmation-policy.md`
- ADDED: `backend/docs/development/quote-direct-issuance-and-currency.md`
- ADDED: `backend/docs/verification/quote-detail-unified-results.md`
- ADDED: `backend/scripts/capture-quote-workflow-before.mjs`
- ADDED: `backend/scripts/capture-quote-workflow-before.ps1`
- ADDED: `backend/scripts/verify-quote-unified-development-preservation.ps1`
- ADDED: `backend/scripts/verify-quote-unified-workflow-browser.mjs`
- ADDED: `backend/scripts/verify-quote-unified-workflow.ps1`
- MODIFIED: `backend/src/UnicoreCRM.Sales/Quotes/Application/Common/QuoteDeliveryCommands.cs`
- ADDED: `backend/src/UnicoreCRM.Sales/Quotes/Application/Common/QuoteDirectIssuancePolicy.cs`
- MODIFIED: `backend/src/UnicoreCRM.Sales/Quotes/Application/Common/QuoteDocumentCommands.cs`
- MODIFIED: `backend/src/UnicoreCRM.Sales/Quotes/Application/Common/QuoteDraftCommands.cs`
- MODIFIED: `backend/src/UnicoreCRM.Sales/Quotes/Application/Common/QuoteManualSourceWorkflow.cs`
- MODIFIED: `backend/src/UnicoreCRM.Sales/Quotes/Application/Common/QuoteSalesAcceptanceService.cs`
- MODIFIED: `backend/src/UnicoreCRM.Sales/Quotes/Application/Common/QuoteVerifiedAcceptanceAssessment.cs`
- MODIFIED: `backend/src/UnicoreCRM.Sales/Quotes/Domain/Quote.cs`
- MODIFIED: `backend/src/UnicoreCRM.Sales/Quotes/Infrastructure/Persistence/QuoteOrderConfirmationBindingReader.cs`
- MODIFIED: `backend/tests/UnicoreCRM.ArchitectureTests/QuoteApprovalTests.cs`
- MODIFIED: `backend/tests/UnicoreCRM.ArchitectureTests/QuoteDeliveryTests.cs`
- MODIFIED: `backend/tests/UnicoreCRM.ArchitectureTests/QuoteDocumentTests.cs`
- MODIFIED: `backend/tests/UnicoreCRM.ArchitectureTests/QuoteDraftCommandSecurityTests.cs`
- MODIFIED: `frontend/unicorecrm-web/src/guidance/content/crm/extendedScreens.ts`
- MODIFIED: `frontend/unicorecrm-web/src/guidance/content/fields/fields.ts`
- MODIFIED: `frontend/unicorecrm-web/src/guidance/content/workflows/workflows.ts`
- MODIFIED: `frontend/unicorecrm-web/src/i18n/translations/en.ts`
- MODIFIED: `frontend/unicorecrm-web/src/i18n/translations/vi.ts`
- MODIFIED: `frontend/unicorecrm-web/src/modules/deals/presentation/hooks/useDealDetailController.tsx`
- MODIFIED: `frontend/unicorecrm-web/src/modules/deals/presentation/views/DealDetailDialogs.tsx`
- MODIFIED: `frontend/unicorecrm-web/src/modules/deals/presentation/views/DealDetailQuoteWorkspace.tsx`
- MODIFIED: `frontend/unicorecrm-web/src/modules/orders/application/read-models/orderReadModel.ts`
- MODIFIED: `frontend/unicorecrm-web/src/modules/orders/domain/model/order.types.ts`
- MODIFIED: `frontend/unicorecrm-web/src/modules/orders/public/orders.ts`
- MODIFIED: `frontend/unicorecrm-web/src/modules/quotes/application/commands/quoteCommands.ts`
- MODIFIED: `frontend/unicorecrm-web/src/modules/quotes/application/read-models/quoteReadModel.ts`
- MODIFIED: `frontend/unicorecrm-web/src/modules/quotes/domain/model/quote.types.ts`
- MODIFIED: `frontend/unicorecrm-web/src/modules/quotes/presentation/components/QuoteAcceptanceReadinessPanel.tsx`
- MODIFIED: `frontend/unicorecrm-web/src/modules/quotes/presentation/components/QuoteActionMenu.tsx`
- REMOVED: `frontend/unicorecrm-web/src/modules/quotes/presentation/components/QuoteApprovalBadge.tsx`
- MODIFIED: `frontend/unicorecrm-web/src/modules/quotes/presentation/components/QuoteBuilderPreview.tsx`
- MODIFIED: `frontend/unicorecrm-web/src/modules/quotes/presentation/components/QuoteBuilderProductPicker.tsx`
- ADDED: `frontend/unicorecrm-web/src/modules/quotes/presentation/components/QuoteCommercialTerms.tsx`
- MODIFIED: `frontend/unicorecrm-web/src/modules/quotes/presentation/components/QuoteCustomerResponsePanel.tsx`
- MODIFIED: `frontend/unicorecrm-web/src/modules/quotes/presentation/components/QuoteDocumentPanel.tsx`
- MODIFIED: `frontend/unicorecrm-web/src/modules/quotes/presentation/components/QuoteLifecyclePanel.tsx`
- MODIFIED: `frontend/unicorecrm-web/src/modules/quotes/presentation/components/QuoteListPanels.tsx`
- REMOVED: `frontend/unicorecrm-web/src/modules/quotes/presentation/components/QuotePrimaryAction.tsx`
- REMOVED: `frontend/unicorecrm-web/src/modules/quotes/presentation/components/QuoteProgressPanel.tsx`
- MODIFIED: `frontend/unicorecrm-web/src/modules/quotes/presentation/components/QuoteSalesAcceptancePanel.tsx`
- ADDED: `frontend/unicorecrm-web/src/modules/quotes/presentation/components/QuoteWorkflow.tsx`
- MODIFIED: `frontend/unicorecrm-web/src/modules/quotes/presentation/hooks/useQuoteBuilderController.tsx`
- ADDED: `frontend/unicorecrm-web/src/modules/quotes/presentation/hooks/useQuoteWorkflow.ts`
- MODIFIED: `frontend/unicorecrm-web/src/modules/quotes/presentation/model/quoteActionPolicy.ts`
- REMOVED: `frontend/unicorecrm-web/src/modules/quotes/presentation/model/quoteApprovalFeedback.ts`
- MODIFIED: `frontend/unicorecrm-web/src/modules/quotes/presentation/model/quoteDeliveryFeedback.ts`
- ADDED: `frontend/unicorecrm-web/src/modules/quotes/presentation/model/quoteOrderPresentation.ts`
- MODIFIED: `frontend/unicorecrm-web/src/modules/quotes/presentation/pages/QuoteDetailPage.tsx`
- MODIFIED: `frontend/unicorecrm-web/src/modules/quotes/presentation/pages/QuoteListPage.tsx`
- MODIFIED: `frontend/unicorecrm-web/src/modules/quotes/presentation/views/QuoteBuilderView.tsx`
- MODIFIED: `frontend/unicorecrm-web/src/modules/quotes/runtime/createQuoteDemoApiRuntime.ts`
- ADDED: `frontend/unicorecrm-web/src/shared/lib/format/commercialMoney.ts`
- MODIFIED: `frontend/unicorecrm-web/src/shared/lib/format/currency.ts`
- MODIFIED: `frontend/unicorecrm-web/tests/quality/architecture/check-quote-api-boundary.mts`
- MODIFIED: `frontend/unicorecrm-web/tests/quality/contracts/check-quote-approval-delivery-contracts.mts`
- MODIFIED: `frontend/unicorecrm-web/tests/quality/contracts/check-quote-presentation-contracts.mts`
- ADDED: `frontend/unicorecrm-web/tests/quality/unit/check-commercial-money.mts`
- MODIFIED: `frontend/unicorecrm-web/tests/quality/unit/check-quotes.mts`
