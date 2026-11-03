# Exchange Rate Authority — contract change proposal

ID: **CC-EXCHANGE-RATE-AUTHORITY**, 22/10/2026. **PROPOSED, NOT_ADMITTED**. Đây là tài liệu riêng để review; không phải OpenAPI patch, không cấp quyền triển khai endpoint. `DEC-PUTWORKSPACEEXCHANGERATE` vẫn mở/BLOCKED. Không chạy generator, sửa checker hoặc accepted baseline trong nhiệm vụ này.

## 1. Quyết định làm đầu vào và phạm vi

Hướng hai bảng, MANUAL-only, Backend versions, append-only correction/withdrawal, reason/audit, ValidUntil bắt buộc end-exclusive và không phụ thuộc Studio Publish đã được người dùng chốt về thiết kế. CRM dùng narrow boundary với trusted membership; proposal cụ thể theo yêu cầu review là `workspace.context.resolve`. Đây không phải operation admission hoặc approval thay pin P0.

Không nhận amount/Money để convert, không inverse/cross-rate, không provider, repricing hoặc report calculation. Không thay current Money, DecimalAmount, Invoice/CreditNote snapshots trong Phase 2.

## 2. Current wire và thay đổi đề xuất

Current authority: `frontend/unicorecrm-web/docs/api/openapi.json`, cùng admission/owner registries hiện hữu. GET list hiện khai 200, `studio.read`, `{workspaceId,revision,items}` max500; chưa có handler Backend riêng tìm thấy trong analysis. PUT hiện không có body/success schema và generated client fail-closed. Có endpoint declaration không chứng minh callable success path.

| Method/path, operation ID đề xuất | Quyền / concurrency | Success hoặc failure semantics đề xuất |
|---|---|---|
| POST `/workspace-configuration/exchange-rates`, `createWorkspaceExchangeRate` | studio.configure, trusted Workspace, Idempotency-Key, global If-Match | 201: server RateId, v1, book/global revision, admin entry và command/audit envelope. Duplicate slot 409; stale 412. New operation chưa admit. |
| PUT `/workspace-configuration/exchange-rates/{rateId}`, existing `putWorkspaceExchangeRate` | studio.configure, Idempotency-Key, global If-Match; expectedRateVersion trong body | 200: append SET correction hoặc WITHDRAW, version server cấp; pair/effectiveAt immutable. Chỉ triển khai sau khi đóng quyết định/admission, không thay BLOCKED bằng code 2xx trước. |
| GET `/workspace-configuration/exchange-rates/history`, `listWorkspaceExchangeRateHistory` | studio.read + trusted Workspace, read access audit | DTO đầy đủ theo fixed book revision, filter pair/RateId, keyset page <=100; `latestPerSlot` phục vụ editor. Không giới hạn history thành 500 rows. |
| GET `/workspace-exchange-rates/resolve`, `resolveWorkspaceExchangeRate` | workspace.context.resolve + trusted active membership, read access log | 200 discriminated result SELECTED/IDENTITY/MISSING/NOT_YET_EFFECTIVE/EXPIRED/WITHDRAWN; authority unreviewed/corrupt/unavailable không bị trộn với missing. Không cần studio.read. |
| Existing GET `/workspace-configuration/exchange-rates` | studio.read như cũ | Giữ reconciliation gate; không tự đổi response thành V2/history hoặc silently truncate. Sau ledger activation, đề xuất fail-closed 409 authority-moved theo amendment được duyệt; Studio mới dùng history. Không phát stale JSON dưới nhãn authoritative. |
| Existing PATCH `/workspace-configuration/locale-region` | studio.configure, idempotency + global If-Match hiện hữu | Bridge plan mục 6. Không tiếp tục cho phép array replacement trở thành ledger writer. Semantic change phải được contract/compatibility review, không giả là compatible vì body shape còn giống. |

Mọi route mới dùng prefix `/v1` của host/client hiện hữu; bảng chỉ ghi operation path. Headers Workspace/Request/Correlation dùng contracts hiện tại. GET history page token bind Workspace/filter/book revision; không cho cursor của Workspace A đọc B. Không thêm public bulk import API trong Phase 2 tối giản; reviewed import là owner maintenance operation có scope riêng.

## 3. Request/result DTO

**Create**: `{fromCurrency,toCurrency,rate:ExchangeRateValue,effectiveAt:UtcDateTime,validUntil:UtcDateTime,sourceReference,reason,acknowledgeHistoricalImpact}`. Server xác định MANUAL; từ chối source/provider/actor/recordedAt/version/bookRevision ngoài schema. JSON `additionalProperties:false`. Global If-Match áp dụng cả create, không dùng `If-None-Match:*` cho cùng route.

**Correction**: `{action:"SET",expectedRateVersion,rate,validUntil,sourceReference,reason,acknowledgeHistoricalImpact}`. EffectiveAt/pair lấy từ root, không có field cho client đổi. Thay cặp/ngày bằng withdrawal + create có hai command/interval gap rõ; không tuyên bố atomic khi chưa có compound command được admit.

**Withdrawal**: `{action:"WITHDRAW",expectedRateVersion,reason,acknowledgeHistoricalImpact}`. Append version mới, giữ root pair/time và provenance liên kết; không nhận rate mới, không xóa old rows. Withdrawal là loại bỏ slot khỏi selection ở các book revision mới; nó có thể làm lịch sử latest không có rate. Không phải “expire từ giờ” theo wall-clock. Scheduled withdrawal riêng chưa được định nghĩa/triển khai.

**Admin entry/history**: WorkspaceId, RateId, Version, PreviousVersion, pair, EffectiveAt, ValidUntil, Action, nullable RateText, MANUAL, SourceReference, RecordedAt/RecordedByMemberId, Reason, BookRevision, CommandId, CorrelationId. Response envelope gồm current/pinned book revision, configurationRevision cho global If-Match, authorityState và pageInfo. History quyền Studio mới chứa metadata này.

History có thể nhận `coverageFrom/coverageUntil` và trả coverage segments/gaps cùng fixed R, có giới hạn cửa sổ/kích thước công khai. Backend lấy đủ slot heads + predecessor của cửa sổ, không suy coverage từ một page history; nếu không hoàn chỉnh phải báo rõ hoặc từ chối quá giới hạn. Historical acknowledgement do server yêu cầu theo TimeProvider/root interval, không lấy clock client làm authority.

**CRM strict allowlist**:

```text
{
  workspaceId, fromCurrency, toCurrency, at,
  bookRevision, selectionPolicyVersion, status,
  selection?: {
    rateId, rateVersion, value: ExchangeRateValue,
    effectiveAt, validUntil, source: "MANUAL"
  }
}
```

SELECTED bắt buộc có selection; các status khác không được có usable selection. IDENTITY không trả một record MANUAL có rate=1. **Không** có SourceReference free text, actor/member IDs, reason, RecordedAt, CommandId, audit/event IDs, raw JSON, SQL hoặc internal notes trong CRM DTO. Standard transport correlation/error metadata theo host vẫn giữ; không copy audit payload vào ProblemDetails. Các identifier/revision công khai ở allowlist cần cho tham chiếu/reproducibility, không cấp quyền đọc history.

Các business unavailable statuses khác hạ tầng: REVIEW_REQUIRED → proposed 409 `EXCHANGE_RATE_AUTHORITY_REVIEW_REQUIRED`; storage failure → 503 `INTEGRATION_UNAVAILABLE`; corrupt/ambiguous ledger → 503 `EXCHANGE_RATE_AUTHORITY_INVALID`, không trả partial selection. Invalid future/negative revision → 422; fixed revision phải được giữ đúng, không tự đổi latest. Error names mới chỉ là proposal, cần error catalog/admission đồng bộ trước endpoint code.

Writer errors: 400 invalid request metadata; 401/403 authentication/capability; 404 unknown rate trong Workspace đã authorized; 409 duplicate slot/idempotency reuse/lifecycle; 412 global/expected-rate stale; 422 payload/time/precision/no-change. Không lộ foreign-Workspace root qua error. Internal SQL duplicate/deadlock/trigger messages được map, không trả raw SQL. Validation/no-change không tăng version/revision.

## 4. ExchangeRateValue và precision impact

Đề xuất scalar độc lập với `Money.amount`: positive ASCII base-10 string, **tối đa 38 digits không tính dấu chấm**, scale tối đa 18, maxLength 39. Đếm cả digit `0` đứng trước dấu chấm để quy tắc SQL/schema/parser không mơ hồ; đây là làm rõ đề xuất “38 significant digits” ở bản trước, chưa phải decision precision đã được duyệt. Integer không leading zero ngoài `0`; không exponent/sign/space/zero. Request không được vượt precision/scale dù phần dư chỉ là zero; server không cắt/round để hợp lệ. Response/storage canonical có thể bỏ trailing fractional zeros vì không đổi giá trị, không lấy đó làm phép round.

OpenAPI phải biểu diễn được cả positivity/digit-count/scale, bằng pattern được parity-test (lookahead hoặc equivalent oneOf), maxLength và mô tả/x-metadata; không chỉ gắn `x-precision` rồi coi là validation chuẩn. Application parser và SQL CHECK kiểm tra cùng miền giá trị, không qua Number/float/C# decimal giới hạn thấp hơn. Cần test regex runtime/validator thật trước khi chọn pattern cuối, gồm zero, 38/39 digits, scale18/19, controls/newline, leading/trailing zeros và rất nhỏ >0.

| Schema / reference hiện tại | Tác động đề xuất |
|---|---|
| Money → DecimalAmount scale<=6 | **Giữ nguyên**; không đổi global precision hoặc financial amounts. |
| WorkspaceExchangeRateDocument.rate | Old string regex/max80, không expiry; không đổi thành một DTO ledger đầy đủ. Không dùng nó chứa admin fields mới. |
| ExchangeRateSnapshot.rate → DecimalAmount | Không tương thích rate scale>6. Phase 2 không đưa selected rate mới vào snapshot này. |
| InvoiceDocument / CreditNoteDocument.exchangeRateSnapshot | Hai consumers tìm thấy trong OpenAPI. Không sửa schema/records/module code của chúng trong Phase 2. Snapshot V2/conversion policy cần proposal Phase 3 riêng. |
| New entry/selection/write DTO | Dùng ExchangeRateValue độc lập. Giá trị có >6 decimals round-trip đầy đủ, không ép vào DTO cũ. |

Không có fallback âm thầm “tạm scale6”. Nếu ExchangeRateValue chưa được duyệt hoặc schema validation chưa đúng thì precision/API admission còn BLOCKED; giữ raw legacy evidence, không cắt để import. Storage varchar80 chỉ là capacity, miền giá trị được CHECK hẹp hơn; schema không hứa 80 digits dùng được.

## 5. Selection contract và restatement

Algorithm version đề xuất `DIRECT_EFFECTIVE_INTERVAL_1`, chưa là runtime code: pin R; chọn latest version mỗi RateId ở BookRevision<=R; chọn slot có EffectiveAt lớn nhất <= at; sau đó mới kiểm tra WITHDRAW/ValidUntil. Không lọc withdrawn/expired trước để làm old rate sống lại. Không latest-time fallback hoặc inverse/cross-rate. Overlap khác EffectiveAt dùng newest-start rule; duplicate root bị SQL chặn, corrupt store fail-closed.

Saved rate có hiệu lực theo interval sau commit, không đợi Studio Publish. At và book revision là hai trục riêng: correction hồi tố làm latest có thể khác, nhưng cùng pair/at/R phải giữ kết quả cũ. Global publishedRevision của Studio không được dùng thay R. Nếu đổi selection algorithm trong tương lai cần policy-version compatibility, không gọi lookup cũ “reproducible” bằng policy khác.

## 6. Old locale-region PATCH writer và reader bridge

Vấn đề cần xử lý trước activation: old clients gửi toàn array, DTO cũ không có expiry/withdrawal/recorded provenance; current full configuration getter mang cùng array. Không thể vừa coi JSON là history archive vừa phát nó dưới nhãn rate authority mà không công bố semantic change.

**Bridge proposal ưu tiên, cần contract/compatibility approval riêng:**

1. Trước cutover, deploy Backend bridge hiểu per-Workspace ledger state nhưng chưa activate; deploy Studio editor mới dùng history/dedicated rate commands. Không auto-activate chỉ vì binary mới đã chạy.
2. Tại cutover, giữ immutable legacy JSON và copy raw rate-array text vào `ExchangeRateBooks.LegacyRatesArchiveJson` cùng SHA-256 UTF-8 trong book/evidence; runtime không được sửa/xóa archive. Đây là **stable compatibility archive**, không current authority, không bảng thứ ba và không dual-write current rates. Full configuration field `localeRegion.exchangeRates` được **deprecate thành legacy archive** trong contract amendment, đọc archive qua owner boundary, có declared response header `Exchange-Rate-Authority: LEDGER`. Không gắn giá trị hiện hành theo clock vào array này. Current response body shape giữ nguyên nhưng đây là semantic compatibility change phải review; header không tự làm old client hiểu an toàn.
3. Sau activation, PATCH với array khác archive chuẩn hóa → 422 VALIDATION_FAILED trên exchangeRates, hướng client sang rate commands; request round-trip archive y nguyên được chỉnh locale/base với global If-Match. So sánh normalized structured fields với archive được giữ nguyên, không so sánh current-time selection. Không silently drop edits hoặc infer withdrawal từ item bị xóa. Global stale request → 412; idempotency replay hợp lệ trước stale check.
4. Existing dedicated old rate GET không phát archive dưới nhãn authoritative: sau activation trả fail-closed authority-moved 409 đã khai trong amendment. New admin dùng history; CRM dùng resolver. Không tự implement existing unimplemented GET success trong Phase 2 nếu readiness còn reconciliation.
5. Known old clients chỉ được tiếp tục locale-only round-trip nếu acceptance chứng minh chúng không dùng archive cho FX. Client còn dựa trên old rate reads/writes phải upgrade/retire trước activation. Header không là auth/security workaround. Nếu không inventory/kiểm chứng được consumers này, **BLOCK activation**, không áp dụng bridge như một waiver.

Lựa chọn thay thế nếu semantic deprecation không được chấp thuận: giữ old contract/Workspace chưa cutover và thiết kế versioned full configuration/locale API (rates tách khỏi locale request) qua change proposal khác. Không tự tăng scope endpoint v2 hoặc nới checker trong implementation để đi qua review. Proposal hiện tại chưa tuyên bố bridge backward-compatible hoặc được checker PASS.

## 7. Contract admission và compatibility gates

Thứ tự: approve decisions/DTO/bridge → reconcile ownership/transaction/error metadata và DEC-PUTWORKSPACEEXCHANGERATE → review actual OpenAPI diff → approve **đúng danh sách pin** chịu tác động → generate bằng pipeline hiện hữu, không accept baseline flag → run unchanged checker và so sánh old/new clients → chỉ sau khi gates/admission qua mới thêm public endpoint.

Generator nhúng spec checksum vào mọi generated client; ngay cả client nghiệp vụ không sửa schema cũng có thể đổi byte. Planned pin manifest phải bao phủ các thay đổi mechanical đó, nhưng không chấp nhận lẫn behavior change. Không sửa `scripts/api/openapi/breaking-changes.mjs`, write/check logic, quality pipeline hoặc `openapi-breaking-baseline.json`. Generator có option accept baseline; **không dùng** option đó.

Nếu unchanged checker còn FAIL (gồm debt Phase 1 hoặc diff mới), ghi đúng FAIL. Không nâng thành PASS bằng cách chỉ chạy generated-drift check hoặc xóa findings. Implementation/activation gate chưa qua thì giữ feature unexposed và báo BLOCKED, không coi contract proposal review là waiver. Current spec/baseline hash được giữ trong evidence của task thiết kế.
