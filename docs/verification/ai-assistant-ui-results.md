# UniCore AI — Enterprise workspace UI verification

Kết quả: **UI và các hành vi Connected đã kiểm thử PASS**. **Tư vấn có provider và evidence thật: BLOCKED_ENVIRONMENT**. Không tuyên bố toàn bộ acceptance hoặc quality pipeline PASS.

## A. Baseline và nguyên nhân

Checkout local được khảo sát trước khi sửa, gồm presentation, Connected/Demo runtime, scope, interaction states, conversation store, route context và Backend advisory contract.

- Header gradient lớn và icon chuyển động lặp làm giao diện nặng; phần chào mừng và welcome message lặp lại.
- Header dùng thành công của việc tải/tạo conversation để thể hiện AI sẵn sàng. Đây không phải bằng chứng provider hoạt động.
- Composer và prompt chung vẫn hiện khi ở Dashboard/danh sách, dù Backend chỉ chấp nhận focused Lead, Contact, Organization, Customer, Deal hoặc Task.
- History chiếm nhiều chỗ trên màn hình hẹp; Connected history chưa được giải thích là bộ nhớ phiên. Xóa hội thoại thiếu xác nhận.
- Retry đi qua cùng đường thêm user message; khóa theo React state chưa chặn được hai sự kiện gửi trước render kế tiếp. Chưa có guard IME rõ ràng.
- Overlay AI chưa dùng focus lifecycle chung. Khi triển khai layout responsive, kiểm thử phát hiện link trong cột ẩn có thể làm mất vòng focus; layout mới chỉ mount cột đang dùng.
- Cầu dịch legacy nhớ aria-label của DOM node. Khi React tái sử dụng nút Gửi thành Dừng, nó ghi đè nhãn về Gửi. Vùng AI song ngữ mới dùng cơ chế opt-out sẵn có, đồng thời giữ nguyên nội dung người dùng/provider.
- Visual review phát hiện history drawer mặc định nằm sau full-screen AI do thứ tự layer. Drawer mới dùng portal host của AI và được kiểm tra click thực tế, không chỉ visibility.

## B. Thiết kế mới

- Giữ nguyên floating launcher và full-screen overlay; không thêm route/sidebar item.
- History desktop 248px; dưới 1024px chuyển thành drawer. Chat rộng tối đa 860px, cuộn riêng, composer ổn định phía dưới.
- Context bar trên laptop/mobile; từ 1536px có context panel 240px. Chỉ hiện khi focused entity hợp lệ và có quyền đọc module.
- Header trung tính, tên UniCore AI, trạng thái chưa kiểm tra/đang xử lý/nhận phản hồi/lỗi lấy từ hành vi thật. Mở UI không gọi Provider Test hoặc advisory.
- Thiếu context: hướng dẫn chọn hồ sơ, link module được lọc theo quyền, composer disabled. Connected có tối đa bốn prompt advisory theo loại hồ sơ.
- User bubble nhẹ, assistant plain text 14px với xuống dòng và wrapping; không render HTML hoặc regex tách summary/attention/next action.
- Source actions chỉ sử dụng actions/evidence có trong response. Điều hướng loại bỏ workspace khác, product space khác, module bị từ chối, URL ngoài và path traversal; màn hình đích vẫn kiểm tra quyền bản ghi.
- History có tìm kiếm, active state, tên tối đa hai dòng, xác nhận xóa và nhãn lịch sử phiên. Không hứa persistence qua Backend.
- Composer autosize trước paint, Enter/Shift+Enter, IME guard, request lock tức thời, Stop/abort và Retry giữ nguyên user message. Chat được remount theo conversation/focused record; workspace, actor và session cũng là ranh giới lifecycle.
- Launcher gọn, không animation lặp; giữ nguyên CSS variables tránh mobile action bar và safe area. Modal dùng focus trap, Escape, focus restoration và reduced motion chung.

## C. File thay đổi trong task này

Đường dẫn Frontend tương đối với `frontend/unicorecrm-web`:

| File | Phạm vi |
|---|---|
| `src/components/ai/AiAssistantDrawer.tsx` | Layout responsive, trạng thái thật, context, history/delete, modal lifecycle và nested portal |
| `src/components/ai/AiChatPanel.tsx` | Empty state, typography, composer, IME/lock/retry/abort và status callback; giữ Demo task flow |
| `src/components/ai/AiAssistantButton.tsx` | Launcher trung tính, bỏ animation liên tục, giữ safe area |
| `src/components/ai/AiSuggestedActions.tsx` | Source/action UI, navigation scope/read guard, clipboard feedback |
| `src/components/ai/AiHistoryPanel.tsx` | Component mới: history/search/active/delete affordance và nhãn persistence đúng runtime |
| `src/components/ai/aiPresentation.ts` | Helper UI: entity/prompt/route và responsive mounting |
| `src/i18n/translations/vi.ts`, `en.ts` | Thêm `ai.workspaceTitle`; giữ các thay đổi có sẵn |
| `src/guidance/content/studio/configuration.ts` | Bổ sung guidance Chat VI/EN, version 7; giữ guidance cấu hình provider có sẵn |
| `tests/e2e/ai-ui.acceptance.ts` | Acceptance read-only mới trên demo thật; ảnh baseline/after, keyboard, cancellation, scope và auth lifecycle |
| `tests/e2e/dashboard-connected.spec.ts` | Nhánh gọi AI acceptance trong harness read-only có sẵn; giữ Dashboard/Sidebar checks |
| `tests/quality/contracts/check-shell-ai-experience.mts` | Thay các assertion buộc gradient/sparkles/zoom cũ bằng yêu cầu UI mới; giữ business/scoping assertions |
| `tests/quality/unit/check-ai-application-contracts.mts` | Thêm checks route/evidence deny, sáu focused types và advisory prompts; giữ test runtime/governance |
| `docs/quality/repository-inventory.json`, `.md` | Regenerate inventory |

Backend chỉ thêm báo cáo này và bằng chứng trong `docs/verification/evidence/ai-assistant-ui/`. Không sửa code Backend trong task này.

## D. Before / after

Bộ ảnh do Chromium riêng tạo bằng account development hiện có, cùng workspace và Deal thật. Không điều khiển cửa sổ cá nhân.

| Trạng thái | Trước | Sau |
|---|---|---|
| Deal, 1440×900 | [Baseline](evidence/ai-assistant-ui/ai-before-focused-desktop.png) | [Redesign](evidence/ai-assistant-ui/ai-after-focused-desktop.png) |
| Dashboard không có focused record, 390×844 | [Baseline](evidence/ai-assistant-ui/ai-before-no-context-mobile.png) | [Redesign](evidence/ai-assistant-ui/ai-after-no-context-mobile.png) |
| Deal, 320px | [Baseline](evidence/ai-assistant-ui/ai-before-focused-narrow.png) | [Redesign](evidence/ai-assistant-ui/ai-after-focused-narrow.png) |

Ảnh bổ sung: [history drawer có thể click](evidence/ai-assistant-ui/ai-after-history-mobile.png), [context panel 1600px](evidence/ai-assistant-ui/ai-after-wide-context.png), [loading/Stop](evidence/ai-assistant-ui/ai-after-loading.png), [lỗi transport/Retry](evidence/ai-assistant-ui/ai-after-transport-error.png), [Backend Deal 503](evidence/ai-assistant-ui/ai-after-real-deal-result.png), [Backend Contact 503](evidence/ai-assistant-ui/ai-after-real-contact-result.png).

Có ảnh focused/no-context ở cả sáu viewport yêu cầu. Baseline dùng full-page capture nên một số ảnh bao gồm chiều cao tài liệu phía sau overlay; viewport thử nghiệm vẫn đúng kích thước đã ghi. Ảnh after chụp đúng viewport của full-screen overlay.

## E. Functional verification

Lượt acceptance cuối: **1 test PASS, 24,6 giây** (28,6 giây gồm khởi tạo browser). Dùng Playwright Chromium riêng qua harness read-only, không fixture seed hoặc thay credential/configuration. [Metadata kiểm thử](evidence/ai-assistant-ui/ai-after-checks.json).

| Kiểm tra | Kết quả | Bằng chứng/phạm vi |
|---|---|---|
| Fresh login và mở AI không gọi provider | PASS | Không `/ai/*` request trước khi người dùng gửi câu hỏi; không configuration/Test request |
| Focused/no-context tại 1440×900, 1366×768, 1024×768, 768×1024, 390×844, 320×844 | PASS | 12 phép đo overflow đều 0; composer nằm trong viewport |
| Context panel 1600px, thu gọn lại laptop | PASS | Panel chỉ mount ở màn hình rộng |
| Desktop/mobile WCAG A/AA scan | PASS | Axe trên focused AI workspace, reduced motion |
| Modal Tab, history drawer click/Escape, focus restoration | PASS | Pointer vào search của drawer; 20 Tab vẫn trong modal; Escape trả focus về launcher |
| Enter, Shift+Enter, composition guard | PASS | Event `isComposing` không gửi; Shift+Enter tạo newline; Enter gửi một request |
| Autosize | PASS | Nội dung nhiều dòng đạt 140px; xóa nội dung trở lại 44px |
| Loading, duplicate-send, Stop | PASS | Giữ transport để quan sát; hai lần gửi chỉ một request; Stop abort, không có assistant message đến muộn |
| Network error và Retry | PASS | Abort transport thật; Retry gọi Backend thật và vẫn chỉ một user message; không fabricates response |
| History search/xóa/hủy xóa | PASS | Chỉ thay đổi conversation của phiên, không thay đổi CRM |
| Delayed responses sau đổi conversation/record/workspace | PASS | Giữ phản hồi Backend thật rồi chuyển ngữ cảnh; phản hồi lỗi 503 cũ không xuất hiện trong ngữ cảnh mới |
| Deal và Contact context gửi đúng Backend | PASS | `contextReferences` đúng loại/ID và workspace header trong JSON evidence |
| Tư vấn thành công và evidence từ provider thật | BLOCKED_ENVIRONMENT | Deal và Contact đều HTTP 503 `AI_PROVIDER_UNAVAILABLE`; UI lý do máy chủ chưa có API key |
| Click evidence thật/hiển thị câu trả lời dài và nhiều assistant messages từ provider | NOT_RUN | Không có phản hồi thành công; không thay bằng mock Connected |
| Module denied/cross-workspace evidence route | PASS (unit) | Helper từ chối route khác tenant/space, denied module, URL ngoài, path traversal; không thay permission trên DB |
| Provider rate limit/permission denied/kill switch | PASS (unit contract); NOT_RUN (live UI) | Governance/state tests chạy; không sửa policy/quyền để tạo lỗi trực tiếp |
| Demo task confirmation và Connected mutation boundary | PASS (unit contract) | Existing Demo/governance tests; không gọi mutation qua Connected UI |
| Đổi workspace/đổi record khi AI mở, quay lại, refresh, logout/relogin | PASS | Control không thấy câu hỏi demo; focus/status mới; refresh và session mới không giữ conversation cũ |
| SQL preservation | PASS | Count + hash của 9 bảng CRM theo mọi Workspace giống trước/sau |
| Bàn phím ảo thiết bị thật / IME hệ điều hành thật | NOT_RUN | Desktop Chromium không mô phỏng được OS keyboard; chỉ kiểm tra composition event guard |

Provider success không được suy ra từ việc list/create conversation thành công. Không thay provider/model/key, không gọi Provider Test, không kích hoạt cấu hình và không gửi business mutation.

### Quality gates

[Kết quả từng gate và build](evidence/ai-assistant-ui/quality/summary.json), [log full verify](evidence/ai-assistant-ui/quality/ai-ui-full-verify.log), [log build](evidence/ai-assistant-ui/quality/ai-ui-build.log). Có 13 targeted gates PASS và 3 targeted gates FAIL ngoài phạm vi, được liệt kê dưới đây.

- PASS: AI governance, AI application/runtime (thêm presentation boundaries), shell AI, workspace isolation, guidance contracts/runtime, presentation responsibility, strict-core, strict type regions, API contract, API management boundaries, frontend/backend separation và repository inventory.
- PASS: production build và bundle budget. Vite còn cảnh báo chunk trên 500 kB theo đơn vị decimal; budget repository vẫn đạt.
- FAIL ngoài phạm vi: `npm run verify` dừng tại global lint/TypeScript với đúng ba lỗi đã có: `deal-quote-transition-outcome.test.mts:42` có thể null, `studio-first-visit.spec.ts:50,51` có thể undefined. Không có lỗi TypeScript mới thuộc AI.
- FAIL ngoài phạm vi: bilingual gate còn 8 literals ở TelegramConnectionPanel, DealBuyerPicker, LeadQualificationView; các literal mới của AI đã dùng VI/EN hoặc translation key.
- FAIL ngoài phạm vi: backend-readiness thiếu status của hai tài liệu Studio connections hiện có; quality-pipeline assertion `347 !== 345`.
- Full verify fail-fast ở gate đầu, nên không diễn giải các gate còn lại là đã chạy; kết quả targeted được ghi riêng.

## F. Contract và data safety

Frontend baseline HEAD `814cc4685b6d8f8ead2fc945f4607a2d0168320d`; Backend HEAD `13cd39eba01ebc9dc238f8b0ef98e174c1d4d3f6`. Đây là checkout có thay đổi chưa commit từ trước.

Database vẫn là `DESKTOP-ICH225R / UnicoreCRM_Development`. Workspace demo `unicore-demo-anh-duong`, ID `ws_f2332a12d56a421d8a722bd2f65c5efb`; control `unicore-demo-anh-duong-control`, ID `ws_811689cfc4b446eab4f29dc109e2abf2`.

[SQL preservation](evidence/ai-assistant-ui/sql-preservation.json) so sánh count và SHA của Activity, Contact, Customer, Deal, Lead, Organization, Product, Support, Task theo toàn bộ Workspace. Tập demo vẫn có 1.050 CRM records. Auth sessions và AI execution/audit có thể được Backend ghi đúng quy trình khi đăng nhập, đăng xuất và gửi advisory; không tuyên bố toàn database bất biến từng byte.

[Scope preservation](evidence/ai-assistant-ui/scope-preservation.json) xác nhận bảy file runtime/context/contract được bảo vệ có hash trước/sau giống nhau, gồm ConnectedAiRuntime, DemoAiRuntime, interaction state, runtime types, BrowserAiConversationStore, useGlobalAiContext và connectedAiRouteContext. So sánh tracked diff trước/sau không có thay đổi ngoài các file UI/i18n/guidance/test/inventory đã liệt kê.

Không seed lại, không migrate/reset DB, không ghi trực tiếp module tables, không đổi connection string, workspace settings, authorization/FLS hoặc governance. Không tạo lịch sử giả, evidence giả, fallback Demo hoặc response Connected giả. Fault injection chỉ hủy transport; kiểm tra response đến muộn chuyển tiếp nguyên phản hồi Backend.

## G. Giới hạn contract hiện tại

- Connected conversation là memory theo workspace/actor và bị clear theo lifecycle; không có durable history qua Backend.
- Route context chỉ cung cấp entity type và ID. Panel ban đầu dùng “Hồ sơ đang mở” + link bản gốc, không lấy tên/status từ cache chưa hydrate. Nếu Backend trả evidence có display label, giữ nguyên label đó.
- Runtime đã format advisory thành plain text. UI không tự suy diễn cấu trúc bằng regex hoặc dựng dữ kiện CRM từ văn bản.
- Connected không hỗ trợ AI business mutation hoặc workspace-wide analysis. Demo giữ khả năng riêng qua governance hiện có.
- Cần provider được người có thẩm quyền cấu hình và kích hoạt hợp lệ trước khi kiểm thử success/evidence/long answers thật. Task này không tự đặt key hoặc đổi cấu hình.

## H. Git status và tái kiểm thử

Mọi thay đổi vẫn chưa commit; không push hoặc merge. Các thay đổi có sẵn về AI provider, Dashboard, Studio và demo được giữ nguyên. Không thêm dependency, generated client, endpoint hay migration.

Mở ứng dụng ở Connected Mode, chọn workspace demo, mở một hồ sơ CRM rồi bấm launcher “Trợ lý AI”. Không có route AI mới.

Chạy lại kiểm thử với account development đã cấu hình trong user-secrets, không cần sửa SQL hoặc ghi credential vào source:

```powershell
Set-Location D:\Project_All\UnicoreCRM\backend
$env:UNICORECRM_AI_UI_PHASE = 'after'
try {
    pwsh -NoProfile -File scripts/verify-connected-dashboard.ps1 -Phase after
} finally {
    Remove-Item Env:UNICORECRM_AI_UI_PHASE
}
```

Harness dùng local Development DB đã xác minh, ApiHost riêng loopback 5098 ở host-only mode tắt background delivery, Vite 3039 và Chromium riêng. Không seed/migrate/provision; nó so sánh SQL trước/sau và dừng host riêng khi kết thúc. Bằng chứng chạy nằm trong `backend/artifacts/dashboard-hydration`; bản có chủ đích cho review được giữ cùng báo cáo này.
