# Exchange Rate Authority — delivery, cutover và acceptance plan

22/10/2026. **DESIGN ONLY.** Không PR thực tế được tạo; không stage/commit/push, production edit, endpoint hoặc migration execution. Dependency ở đây là đề xuất cho nhiệm vụ implementation sau khi được duyệt, không phải quyền tự tiếp tục.

## 1. UX hiệu lực, hết hạn và sửa hồi tố

Editor MANUAL có From/To, rate dạng text, source reference, reason và hai field bắt buộc:

- **Có hiệu lực từ**: ngày/giờ tại Workspace timezone.
- **Không còn hiệu lực từ**: ngày/giờ đầu tiên bị loại trừ. Helper ghi “Khoảng hiệu lực gồm thời điểm bắt đầu, không gồm thời điểm kết thúc”. Không đặt nhãn “ngày cuối cùng” cho end-exclusive value.

Date-only mode: từ đầu ngày D đến trước đầu ngày E, theo timezone đang hiển thị; E phải sau D. Nếu người dùng muốn hết ngày D phải chọn E là ngày kế tiếp — UI hiển thị rõ cả câu tóm tắt, không âm thầm cộng ngày sau save. Advanced time mode chọn giờ/phút/offset rõ. Hiển thị Workspace timezone và UTC tương ứng trước xác nhận; không lấy timezone browser/host làm authority. DST ambiguous/nonexistent không tự đoán; yêu cầu chọn offset hợp lệ hoặc sửa input. Đổi timezone sau đó không sửa UTC đã lưu. Không default thời hạn 24 giờ/7 ngày, không tự gia hạn.

Trước save hiển thị khoảng hiệu lực và **coverage preview** cho pair trong một cửa sổ ngày do người dùng chọn. Backend owner reader dùng cùng selection policy/fixed R để trả segments/gaps, không dựa vào một page history UI rồi kết luận đã phủ hết. UI cảnh báo “Chưa có tỷ giá từ … đến …; nghiệp vụ cần tỷ giá sẽ báo thiếu/hết hạn”. Chồng khoảng khác EffectiveAt hiển thị “Tỷ giá có ngày bắt đầu mới hơn sẽ được ưu tiên từ …”. Không lấp gap bằng rate hết hạn.

Coverage proposal gộp vào history read khi có `coverageFrom/coverageUntil`, không endpoint/service mới: lấy predecessor slot đầu cửa sổ và các heads trong cửa sổ ở R, clip theo next start/ValidUntil/WITHDRAW, trả covered segments và complement gaps. Cửa sổ quá lớn/summary vượt giới hạn đã công bố trả lỗi giới hạn hoặc `complete=false` rõ; không ghi “không có gap” khi chưa đủ dữ liệu. Preview không khóa tương lai; command If-Match kiểm tra lại sau khi có concurrent write. Đây là read metadata về interval, không conversion hoặc report FX.

Create hồi tố, correction hoặc withdrawal với EffectiveAt đã qua đều phải hiển thị:

> Sửa đổi này có thể làm thay đổi báo cáo tính lại theo latest revision. Giao dịch gốc và kết quả dùng fixed book revision không bị sửa.

Reason bắt buộc; form yêu cầu xác nhận historical impact. Proposal body có `acknowledgeHistoricalImpact` và server tự xác định cần xác nhận theo TimeProvider/root interval, không tin clock/flag để suy actor hoặc timestamp. Thiếu acknowledgement khi bắt buộc trả 422 có field error, không commit; người dùng xem lại rồi gửi intent đã xác nhận. Không sinh rate sample hoặc tự invert khi tạo dòng.

Withdrawal được diễn giải là loại slot khỏi lookup ở các book revision mới, không đổi amount/currency hoặc xóa version. Lịch sử hiển thị SET/WITHDRAW, valid interval, version, actor/time, reason/source reference cho người có studio.read. Correction không sửa pair/start; chọn sai pair/ngày cần withdraw + create, UI không hứa atomic hai command.

## 2. Migration và cutover theo Workspace

**Phân biệt schema readiness với activation.** Migration chỉ tạo hai bảng, keys/FKs/indexes/triggers và mapping; không tự nhập/activate existing Workspace, kể cả JSON rỗng. Không sửa migration cũ, không có rate seed và không đổi bảng giao dịch/bảng giá.

| Bước | Điều kiện và thao tác được đề xuất cho implementation tương lai |
|---|---|
| M0 inventory | Per database + Workspace: hash exact legacy JSON, parse/validate each entry, duplicate id/slot, old references/snapshots, timezone/time, precision, source/provider evidence, thiếu expiry. Bản Phase 1 DB có 0 rates không thay inventory thực tế. Read-only trước. |
| M1 schema/bridge | Additive migration, guard writer và paired client được test; feature chưa activate. Missing book hoặc REVIEW_REQUIRED không có rate authority; CRM nhận lý do rõ, CF01 base vẫn từ Bootstrap. Old arrays chưa cutover không tự được dùng cho FX. |
| M2 reviewed plan | Human/operator duyệt danh sách row được nhập, ValidUntil, nguồn/reason, legacy-to-new mapping và expected JSON hash. Dữ liệu thiếu/không tin cậy ở lại review; không tự suy ngày hết hạn, actor cũ hoặc provider truth. Không xóa JSON/history. |
| M3 locked activation | Trong một owner transaction/global config lock, reread hash khớp approved plan; nếu drift thì abort để inventory/review lại. Verify contract/compatibility/client retirement gates. Lưu raw rate archive/hash bất biến trong book, không xóa original JSON. Tạo/recheck book REVIEW_REQUIRED rồi chuyển ACTIVE trong transaction; import verified v1 roots bằng một INSERT → trigger cấp R=1; empty verified book ACTIVE R=0 không seed rate. Audit/outbox/envelope/cutover record commit atomically. Bất kỳ lỗi rollback giữ chưa-active. |
| M4 observe | New Studio/history/CRM resolve đúng owner và SQL; old rate GET fail-closed và locale writer guard hoạt động; kiểm tra no reprice/rewrite và restart/relogin. Không rollback authority về JSON khi lỗi. |

Có thể cần book REVIEW_REQUIRED trước import để theo dõi readiness; GET không tự tạo row hoặc activate. Sau rollout đã được duyệt, **Workspace mới** có thể tạo empty ACTIVE book trong provisioning theo một explicit server rollout setting; trước đó mặc định chưa enable. Không scan/activate các Workspace cũ khi Backend restart. Setting chỉ bật khả năng initializer cho Workspace mới, không cấp tỷ giá và không đổi USD/VND defaults. Chi tiết tên setting là implementation choice, cần test on/off; existing Workspace luôn đi quy trình per-Workspace.

Legacy mapping đề xuất giữ trong immutable import evidence/cutover command response hiện hữu và version provenance liên kết tới hash, không thêm bảng mapping thứ ba ngay. Nếu số lượng vượt khả năng command response hiện hữu cần đánh giá archival contract riêng trước import, không truncate mapping. Snapshot cũ luôn giữ exact value/id/version của nó; mapping chỉ tra cứu provenance, không redirect calculation sang giá mới. Không thể phục hồi versions đã bị overwrite trước ledger: ghi là **UNKNOWN**, không bịa lịch sử.

Import maintenance operation phải được admit cùng owner intent/transaction trong contract change-control và có operator authorization per Workspace. Không dùng CLI/admin route để làm blocked PUT callable hoặc tránh review. Không public bulk-import endpoint trong scope tối giản.

## 3. Rollback và stop conditions

- Trước activation: dừng exposure, giữ tables/legacy/evidence. Schema mới không tự bật feature. CF01 base/timezone độc lập.
- Sau activation: giữ Backend bridge chặn JSON writer; dùng read-only/unavailable FX mode nếu cần. Không deploy bản cũ có thể ghi stale array. Ưu tiên roll forward; không đọc legacy fallback.
- Rate sai đã commit: append correction/withdrawal có reason/audit, không delete/update. Transaction sai cần workflow module riêng ngoài Phase 2.
- Không tự chạy Down/drop tables/triggers sau khi có history dùng thực tế. Migration principal chỉ dùng deployment/repair được duyệt, không runtime.
- New migration Down đề xuất có guard THROW khi đã có book ACTIVE hoặc history; không để generated Down mặc định xóa ledger. Operational rollback ưu tiên giữ additive schema ngay cả khi chưa có data. Destructive repair/downgrade cần proposal/backup/approval riêng; không nằm trong PR triển khai thông thường.
- Dừng activation nếu thiếu approved inventory, JSON drift, unsupported old clients, precision/schema mismatch, integrity/security/compatibility FAIL, blocked operation chưa admitted hoặc chưa có exception cho pin cần sửa. Không biến lỗi thành waiver.

## 4. Production file impact dự kiến và P0 pins

[Machine-readable candidate manifest](evidence/multicurrency-phase2-review-20261022/planned-file-impact.json) ghi **70 candidate paths** tại lúc lập kế hoạch (source + contracts/generated artifacts + tests/scripts), current SHA và P0 SHA nếu có; thêm các file migration mới tên timestamp chưa được tạo. Đây là phạm vi dự kiến, không phải diff đã thực hiện hoặc quyền sửa.

| Nhóm | File hiện hữu dự kiến thay đổi / file mới |
|---|---|
| SQL/domain | WorkspaceDbContext + model snapshot; mới WorkspaceExchangeRateBook/Version/ExchangeRateValue; một additive migration + Designer chứa constraints/triggers. Không sửa migration cũ. |
| Owner transaction/bridge | StudioConfiguration, EfStudioPersistence, StudioApplication, StudioService, WorkspaceModule; mới ExchangeRateApplication/AuthorityService/EfExchangeRatePersistence/owner contracts. Cùng assembly/SQL; không framework/assembly mới. |
| Transport | ApiHost Program maps endpoint file mới **chỉ sau admission**; ExchangeRateEndpoints/Contracts mới. Existing StudioContracts/Endpoints chỉ sửa nếu actual approved wire mapping cần, không mặc định mở scope. |
| Provisioning | EfInitialWorkspaceProvisioningPersistence + rollout option nếu được duyệt, không đổi currency defaults. |
| Frontend authority/editor | applicationComposition, workspace-config public boundary, adapter/runtime/type files mới; Studio gateway/types/adapter/runtime/public; LocaleRegionView tách editor/history components; i18n vi/en và Studio guidance. Không chuyển toàn CRM sang Studio dependency. |
| Verification | New Backend verifier + new SQL tests; new contract/browser tests. Reuse P0 harness để chạy regression, **không sửa P0 verifier/test** để pass. |
| Contract/generation | Current OpenAPI, command/query ownership registries, generated artifacts theo pipeline hiện hữu. Không checker/accepted baseline changes. |

### 28 pin P0 có thể chịu tác động — đều CHƯA ĐƯỢC PHÉP SỬA

Các path dưới đây tính từ `frontend/unicorecrm-web/`; manifest P0 ghi repo `frontend`. Hash gốc được giữ nguyên. Không có Backend P0 source pin trong candidate plan này.

| Pinned group | Paths |
|---|---|
| Current contract/catalog | `docs/api/openapi.json`, `openapi.sha256`, `api-operation-catalog.json`, `generated-client-manifest.json`, `operation-coverage-ledger.json` (các tên ngắn cùng thư mục docs/api) |
| Readiness projections | `docs/backend-readiness/concurrency-policy.json`, `idempotency-policy.json`, `operation-authorization-matrix.json`, `operation-contract-status.json` |
| Generated runtime/catalog | `src/platform/api/catalog/generatedApiOperationCatalog.ts`; `src/platform/api/contracts/generatedOpenApiRuntimeContract.ts`, `generatedProductionCommandRegistry.ts`, `generatedProductionQueryRegistry.ts` |
| Generated clients/index | Trong `src/platform/api/generated/`: `accessGovernanceApi.ts`, `aiApi.ts`, `commercialApi.ts`, `communicationsApi.ts`, `crmConfigurationApi.ts`, `financialApi.ts`, `financialConfigurationApi.ts`, `identityApi.ts`, `index.ts`, `integrationConfigurationApi.ts`, `productConfigurationApi.ts`, `receivablesApi.ts`, `studioQuickSetupApi.ts`, `workspaceBootstrapApi.ts`, `workspaceConfigurationApi.ts` |

Spec hash nhúng trong header mọi generated client nên nhiều diff ngoài Workspace chỉ là mechanical checksum/version. Vẫn cần exact diff review/exception trước khi generate; không lấy “generated” làm lý do vượt pin. Không chạy generator vào working tree để lấy preview trước approval; tạo proposal trong evidence/scratch được duyệt nếu cần, không sửa các pin.

75 pin khác không nằm trong plan Phase 2, gồm Lead form đã có ngoại lệ **riêng CF01**. Giữ file Lead ở hash sau CF01, không áp dụng lại ngoại lệ cho Phase 2. Nếu implementation phát hiện cần thêm pin, phải phân tích impact và xin ngoại lệ mới trước khi sửa. Mọi initial Studio dirty patch của người dùng tiếp tục được phân biệt với diff mới.

Danh sách 70 không phải cap cứng: mapping/triggers test files mới và rollout option được ghi cụ thể trong PR scope trước implementation; conditional paths phải cập nhật manifest/approval trước khi động tới. Không lén mở rộng scope để chữa quality debt.

## 5. Acceptance matrix

Mỗi dòng dưới đây có trạng thái Phase 2 **NOT_RUN**. Không lấy proof thiết kế hoặc Phase 1 PASS làm SQL/API acceptance. `BLOCKED` chỉ dùng đúng admission/gate blocker đang tồn tại, không đổi quality history.

| ID | Acceptance / expected outcome | Evidence cần có | Planned owner PR |
|---|---|---|---|
| DB01 | Root self-FK v1 hợp lệ; hai RateId độc lập cùng slot bị unique reject, kể cả withdrawn root | Direct SQL positive/negative trong DB GUID mới, không chỉ HTTP | B |
| DB02 | Insert v2/v99 không root, NULL/gap predecessor, đổi pair/start bị FK/CHECK reject | SQL error numbers + before/after counts | B |
| DB03 | Two-session create/correct race; không cả hai commit conflicting root/version | Barrier concurrency logs + SQL final state | B/C |
| DB04 | Book sequence đúng +1/statement; fake/old R và late append fixed R bị reject | Trigger tests, import multirow cùng R, empty INSERT không tăng R | B |
| DB05 | UPDATE/DELETE/TRUNCATE/DDL bypass bị chặn ở runtime principal; constraints/triggers enabled/trusted | Permission inventory + negative SQL; admin dùng migration không thay runtime proof | B/F |
| DB06 | EF10.0.11 trigger/OUTPUT mapping đúng; rollback toàn ledger/book/global/audit/outbox khi inject fault | Real persistence tests, SQL transaction evidence | B/C |
| V01 | ExchangeRateValue boundaries 38/39 digits, scale18/19, tiny >0; no cut/round; schema/parser/SQL parity | Fixtures + exact string round-trip HTTP/SQL | A/B/D |
| S01 | Time-bound start inclusive/end exclusive, UTC/DST, missing/future/expired/withdrawn, overlap newest-start, no fallback | Owner tests + ApiHost SQL selection cases | C/D |
| S02 | Backdated create/correct/withdraw changes latest only; pair/at/R/policy fixed reproducible across restart | Saved fixed-R outputs/hash before/after, original rows unchanged | C/D |
| S03 | Consistent multi-pair R; capacity/pagination/coverage window not silently incomplete | Concurrent read/write + >500 history rows + query plans | C/D |
| P01 | CRM context.resolve without Studio gets only allowlist; Studio read/write distinct; revoked/foreign scope denied | Real HTTP/capability fixtures, recursive DTO leak assertions | D |
| P02 | No SourceReference/actor/reason/audit/event/command IDs trong CRM success/error payload | JSON schema/runtime payload tests, not visual inspection only | D |
| C01 | Idempotent replay before stale If-Match, no extra versions/events; different fingerprint conflict | HTTP requests + SQL row/revision counts | C/D |
| C02 | OpenAPI/admission/owner registries reconciled; blocked PUT remains blocked until approved | Current-contract diff, generator/unchanged compatibility checker output | A/D |
| C03 | Old GET authority-moved; old PATCH edited array rejected; stable archive round-trip locale-only; no semantic false PASS | Old/new client real HTTP matrix + cutover boundary tests | C/E/F |
| U01 | Required expiry labels/timezone summary/gap warning; historical acknowledgement/reason/audit; no fake rate on add | Browser records/screenshots + HTTP/SQL | E |
| L01 | Inventory/import/hash drift, REVIEW_REQUIRED/no fallback, only verified rows, mappings/legacy snapshots unchanged | Per-Workspace approved plan + run records, real isolated SQL | F |
| L02 | No auto activation on migration/restart; new Workspace initializer off/on per rollout policy; rollback bridge preserves ledger | Deployment rehearsal, SQL state, retained evidence | F |
| R01 | CF01 direct CRM/USD/EUR/A-B/session/outage; demo VND; Quick Setup; P0 journeys unchanged | Focused contracts + ApiHost/SQL/browser P0 reruns | E/F |
| R02 | No Product/transaction amount/currency rewrite; no CF02/CF05 behavior changes | SQL hash/rows of real fixture records before/after, scoped source diff | C/F |
| Q01 | Focused tests/typecheck/build/architecture/API/guidance/full gates report true statuses | Logs + source hashes; old FAIL not auto-accepted | Each/F |
| Q02 | Independent review/admission/compatibility acceptance trước activation/release | Reviewer attestation/decision, no self-waiver | F |

Current blockers giữ nguyên: existing PUT BLOCKED; contract bridge/precision chưa admitted; Phase 1 quality FAIL/NOT_RUN không thay. SQL/runtime/UX mới đều NOT_RUN, không gọi BLOCKED thay NOT_RUN chỉ vì chưa bắt đầu implementation.

## 6. Pull request nhỏ, thứ tự và dependency

Tên dưới đây là nhãn kế hoạch, không phải branch/PR đã tạo; không commit/push trong task này.

| PR | Nội dung hẹp / dependency | Điều kiện hoàn tất và triển khai |
|---|---|---|
| A — Authority & contract | Chốt precision/DTO/reader bridge, owner intent và blocked PUT admission; exact contract/generated pin exceptions. Không runtime endpoint. | Unchanged checker kết quả thật; contract/admission được duyệt. Nếu không thể giải quyết compatibility trong constraint baseline bất biến thì dừng dependency, không waive. |
| B — Database integrity | Sau A/design details được duyệt: 2 entities, additive migration, root/predecessor constraints, triggers, SQL/EF tests. Không rates seed/auto-activation/endpoints. | DB01–06/V01 trong DB thử; migration **deployment** vẫn là hành động cần admission riêng, PR code không tự execute production. |
| C — Owner commands & selection | Sau B: exact parser, append/replay/audit/outbox transaction, fixed-R direct reader, old-writer guard. Private owner contracts, không admin workaround/public routes. | S01–03/C01/R02, atomicity fault tests; no Money conversion. Bridge guard có trước bất kỳ ledger public write/activation. |
| D — Admitted transport | Sau A+C: resolver/history/create/PUT theo admitted contract và permissions/allowlist; new API SQL verifier. | Real HTTP/SQL security/concurrency, C02; chưa auto-activate existing Workspace. |
| E — Studio UX & scoped runtime | Sau D: focused editor/history/coverage, expiry/reason/historical warning, owner session lifecycle, bilingual guidance; CRM boundary độc lập. | U01/P02/R01, focused tests/typecheck/build, giữ original Studio patches và không đụng Lead nghiệp vụ. |
| F — Import/cutover rehearsal & review | Sau B–E: reviewed maintenance path, per-Workspace inventory/hash/activation safeguards, initializer setting, rollback bridge và full evidence. | L01–02/R01–02/Q01–02; independent review và operator approval trước **mỗi activation thật**. Không gọi một PR merge là release acceptance. |

Dependency tuyến tính là lựa chọn dễ quản lý cho một người phát triển; không cần parallel branches/framework. Contract review/pin exception/admission là gate thật, không tách API mới ra PR khác để lách PUT blocker. Nếu PR C hoặc E quá lớn, tách read trước/write sau trong cùng dependency; không cắt transaction integrity khỏi audit/outbox hoặc cho old writer ghi song song.
