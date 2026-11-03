# Multi-currency — Phase 0 contract và CF01

Ngày 21/10/2026, Asia/Saigon. Phạm vi: working tree Backend và Frontend; không dùng remote HEAD thay source hiện tại. **Phase 0 hoàn tất; CF01 đã triển khai theo ngoại lệ một file pin do người dùng duyệt.** Kết quả kiểm thử và checkpoint proposal được ghi riêng trong `multicurrency-phase01-results.md`; không phải checkpoint đã được chấp thuận.

## Authority và invariants

| Khái niệm | Authority hiện tại / contract đề xuất |
|---|---|
| Workspace base currency | Backend Workspace. Studio configuration sở hữu thao tác chỉnh sửa; `EfStudioPersistence.CommitConfigurationAsync` cập nhật `StudioConfigurations.LocaleRegionJson` và `BootstrapProjections.BaseCurrency` trong cùng transaction SQL. CRM dùng Bootstrap projection qua API có kiểm tra membership và `workspace.context.resolve`, không cần quyền Studio. |
| Transaction currency | Amount/currency đã lưu của từng Money/transaction hoặc Product price. Đổi base không chuyển đổi, đổi nhãn, reprice hoặc rewrite các bản ghi này. Default cho draft mới không thay thế currency của record/source có sẵn. |
| Reporting currency | Chưa có authority quy đổi được nghiệm thu. Không đồng nhất base currency với currency hợp lệ của mọi tổng hợp. CF02 vẫn mở; sửa nguồn base trong CF01 không chứng minh metric đã đúng. |
| Exchange rate | Studio document hiện có id, pair, decimal-string rate, effectiveAt, source, providerConnectionId, status, version. Đây chưa phải bằng chứng provider integration, rate-selection policy hoặc immutable conversion ledger đã được nghiệm thu. Không tạo rate mới trong nhiệm vụ này. |

Backend automatic provisioning mặc định USD; demo repository mặc định VND. Giữ cả hai. Không migration, không sửa SQL records hoặc price lists hiện có.

`MoneyDto` dùng `amount: string`, `currency: string`; arithmetic dùng decimal/BigInt và chặn mixed-currency addition. Các helper `findEffectiveExchangeRate`, `convertMoneyPreview`, `snapshotExchangeRate`, `invertDecimal` đã tồn tại trong shared money. Không mở rộng hoặc coi preview helper là conversion engine có authority.

## Data flow hiện tại

```text
Auth -> memberships -> GET /workspaces/{id}/bootstrap -> AccessControl
     -> commit Workspace -> CRM
                              |
                              +-> useWorkspaceOperationalConfiguration
                                  -> connectedProjection ?? demoRepository
                                  -> VND nếu chưa vào Studio (CF01)

Studio route -> GET /workspace-configuration -> Studio runtime
            -> replaceConnectedWorkspaceOperationalConfigurationProjection
            -> CRM nhận USD/EUR sau khi Studio đã tải/save
```

`WorkspaceBootstrapHttpAdapter` đã map baseCurrency, locale, timeZone, configurationVersion. `enterWorkspace` là canonical entry và chỉ commit sau bootstrap/participant thành công. App composition participant hiện chuẩn bị AccessControl và module projection, chưa chuẩn bị currency context. `RequireWorkspaceContext` đã có loading/error/retry trước children.

Operational projection hiện không chứa owner Workspace/session. Scope disposer clear projection khi commit đổi Workspace, nhưng clear không thông báo subscriber. `ConnectedWorkspaceContextRuntime.clear` phát bootstrap null khi auth owner đổi; không trực tiếp retire operational projection. Studio async commit cũng chưa tự kiểm tra owner/generation sau await. Vì vậy chỉ thay VND thành USD hoặc preload Studio không đủ xử lý lifecycle.

## Data flow đề xuất cho CF01

```text
Auth -> memberships -> Bootstrap + AccessControl
     -> validate/prepare Workspace runtime settings
        {workspaceId, session owner, baseCurrency, timeZone, configurationVersion}
     -> commit Workspace + activate settings -> currency consumers

Studio locale-region PATCH -> authoritative response
     -> owner/generation guard -> update current currency settings -> subscribers
     -> full operational projection dành cho các consumer cần full configuration
```

Tách read contract nhỏ cho CRM khỏi full operational configuration. Không dựng một full configuration giả bằng cách trộn demo business information, enabledCurrencies hay exchangeRates với Bootstrap currency. Bootstrap chỉ cung cấp những trường nó thực sự có; đặc biệt không chứng minh enabledCurrencies hoặc reporting/FX policy.

Consumers chỉ cần base/timezone dùng proposed `useWorkspaceRuntimeSettings`; demo implementation đọc demo repository. Full configuration consumers cần business information/addresses/enabled currencies dùng read boundary riêng và endpoint hiện hữu khi có quyền; loading/error chỉ chặn surface cần nó. Không đưa GET `/workspace-configuration` hoặc Quick Setup vào dependency bắt buộc của toàn CRM, không mở rộng quyền `studio.read` để chữa CF01.

Product create/CSV lấy base từ runtime settings; edit giữ transaction currency có sẵn. Khi Product picker cần currency options, phải giữ rõ giới hạn Backend chỉ nhận effective base cho create/replace; không coi `[base]` là danh sách enabledCurrencies từ server. Chi tiết policy đổi base với catalog cũ thuộc CF05, không xử lý ở đây.

Lead/Contact/history chỉ cần timezone không phải đợi Studio. Lead form có hai chỗ tạo Money mới nên phải lấy cùng runtime settings cho cả timezone và base. Không đổi submit, draft retirement, idempotency, ownership hoặc Activity behavior.

## Cache lifecycle và failure contract

- Key authority bằng session/account + Workspace; configurationVersion là version của Bootstrap, không so sánh trực tiếp với Studio revision.
- Prepare trước mount consumer; không publish partial-ready khi currency không hợp lệ hoặc Bootstrap thất bại.
- Bắt đầu đổi Workspace thu hồi currency cũ và pending reads; activation chỉ được commit cho entry/session còn hiện hành. Bài test phải giữ response A rồi đổi sang B để kiểm tra late response.
- Logout, auth expiry, account/session replacement clear context và phát invalidation; không chỉ xóa field mà để React giữ snapshot cũ.
- Studio response chỉ cập nhật đúng owner đã mở request. Response cũ sau switch/logout không được hồi sinh currency. Save success thông báo các consumer đang mount; form có draft/transaction không tự reprice.
- Backend unavailable, missing/malformed Bootstrap currency: loading trong lúc chờ, error/retry khi thất bại, tuyệt đối không demo fallback. Trang không cần tiền không phát thêm yêu cầu Studio.
- Refresh/relogin/backend restart đọc lại SQL authority qua Bootstrap. Không coi local/sessionStorage selection là currency authority.
- Cross-tab/external update realtime invalidation chưa có transport authority; nếu cần freshness tức thời ngoài tab hiện tại phải chốt policy riêng. Không tuyên bố đã có push invalidation.

## Consumer inventory đã đọc

| Nhóm | Source / tác động CF01 |
|---|---|
| Product | ProductFormModal initial/new defaults và currency options; useProductListController CSV default |
| Lead | useLeadFormController timezone + Money defaults; filters/dialogs/history timezone |
| Contact | ContactHistoryPanel và ContactRelationshipPanel dùng timezone |
| Deal / lead qualification | DealFormModal và qualification/sell-now defaults; existing Lead/Deal currency có precedence |
| Quote | List/detail base; builder default; builder/view/preview còn cần enabled currencies, business info/addresses |
| Order | List/form base; customer document cần business info/addresses và transaction-first currency |
| Invoice | Form cần business info/addresses + source/existing/seller precedence; blank seller currency CF03 giữ mở |
| Payment | Base và enabled currencies; không mở rộng mutation availability |
| Dashboard/Reports | Base + timezone; engine cộng mixed currency CF02 giữ mở |
| Shipping | Full operational addresses; không dùng bootstrap để bịa địa chỉ |

Đây là **code inspection**, chưa phải browser/SQL acceptance của bản sửa.

## Phạm vi pin cần duyệt trước implementation

Đã đối chiếu manifest `p0-checkpoint-finalization-20261021/cumulative-candidate-manifest.json`: **103/103 file pin còn nguyên SHA-256**. Backend HEAD `13cd39eba01ebc9dc238f8b0ef98e174c1d4d3f6`, Frontend HEAD `7d41aba85f33c80855f598f3ab24371c0fb510e8`; cả hai index không được thay đổi.

Một file pin nằm trực tiếp trên đường CF01: Frontend `src/modules/leads/presentation/hooks/useLeadFormController.tsx`. Người dùng đã duyệt riêng việc đổi import/hook và truy cập timezone/base sang narrow runtime settings. Source sau sửa khớp byte-for-byte với proposal ở `evidence/multicurrency-phase01-20261021/lead-pinned-proposal.patch`; approval và hash trước/sau ở `pinned-exception-approval.json`. Không sửa lại manifest/checkpoint P0 gốc; 102 file pin khác giữ nguyên.

Các điểm sửa còn lại ở platform workspace configuration/context runtime, composition, Studio async publication, các consumer base/timezone và focused tests. Danh sách thực tế, patch và source hashes nằm trong `phase1-source-manifest.json`. Không cần thêm ngoại lệ pin.

Currency context có `source` BOOTSTRAP/STUDIO/DEMO. Khi nhận Studio configuration, nó giữ rõ `studioRevision` riêng với `configurationVersion` của Bootstrap lần đọc gần nhất; không giả rằng hai counters tương đương. Publication trùng toàn bộ configuration không invalidation lại consumer. Guard thu hồi currency trước cả membership read khi đổi Workspace; không đợi đến Bootstrap request mới thu hồi.

Product và Deal currency choices là các lựa chọn có authority hiện biết: base từ Bootstrap, enabled currencies nếu full projection đã có, currency của record/draft nguồn. Đây không phải API enabled-currency policy mới. Deal modal có thể được mount khi đang đóng trên Contact detail, nên không được buộc Contact phụ thuộc full Studio configuration chỉ để chuẩn bị lựa chọn currency.

## API/migration cho các phase sau

| Phase | Thực sự cần / chưa được kết luận |
|---|---|
| 1 CF01 | Base/timezone/version đã có trong Bootstrap; không cần đổi OpenAPI hoặc migration cho narrow context. Full enabled-currency policy không có trong Bootstrap; không tự mở rộng contract pin. |
| 2 Exchange Rate Authority | Chốt Workspace ownership, source provenance, effective time, immutable version/retirement, permission/concurrency và provider evidence. API/rate persistence chỉ bổ sung sau khi so sánh khả năng JSON document hiện có với yêu cầu lịch sử/query/audit; chưa có căn cứ nói bắt buộc tạo bảng mới. |
| 3 Conversion Engine | Chốt decimal rounding/minor-unit, as-of selection, missing/stale rate, inverse/cross-rate và snapshot reproducibility. Nếu conversion được lưu, contract cần source Money + target + rate id/version/effectiveAt/source + rounding policy. Không rewrite transaction Money. |
| 4 Dashboard/Reports | Group theo transaction currency hoặc conversion policy đã được duyệt; outputs cần amount/currency/provenance/as-of rõ. Không chỉ thay symbol formatter. API/read-model changes theo authority report sau này. |
| 5 Module consumers | Chốt Quote/Order/Invoice/Payment inheritance và edit/duplicate semantics; giải quyết CF03/CF04 riêng, không trộn fix formatter vào CF01. |
| 6 Catalog / rollout | Chốt CF05, repricing intent, eligibility và warning khi đổi base với catalog cũ. Migration chỉ khi cần lưu lịch sử/snapshot mới; giữ nguyên price records hiện có. |

Thứ tự đánh số 2–6 ở bảng là đề xuất phân công theo mục tiêu người dùng, chưa phải authority business đã chốt.

## Verification và checkpoint riêng

Đã chạy focused lifecycle/race/no-fallback tests; ApiHost + SQL Server thật trong database GUID mới được giữ lại; UI login trực tiếp USD, Studio EUR, refresh/relogin/restart, A EUR/B USD, unavailable, Product EUR, demo VND contract, Quick Setup; cumulative P0 security/connected/UI; typecheck/build và gates có liên quan. Kết quả và giới hạn cụ thể nằm trong [báo cáo Phase 1](multicurrency-phase01-results.md), phân biệt PASS/FAIL/BLOCKED/NOT_RUN; không dùng evidence cũ làm PASS cho patch mới.

Phase 1 checkpoint chỉ là đề xuất sau verification: paired HEAD + actual working-tree hashes + scoped patch/evidence manifest + preservation của 103 pins (ngoại lệ đã được duyệt được liệt kê riêng). Không stage/commit/push, nhận waiver, sửa compatibility baseline hoặc triển khai Phase 2.
