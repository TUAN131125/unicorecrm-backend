# Final Acceptance Review — Authoritative Dynamic Deal Pipeline

Ngày kiểm tra: 2026-10-24. Review bổ sung cho [báo cáo triển khai](deal-pipeline-connected.md). Không triển khai lại registry, sửa production logic, nới invariant, seed lại demo, đổi quyền, commit hoặc merge.

## Quyết định đề xuất

**PASS cho functional checkpoint riêng: Dynamic Pipeline Registry & Connected OPEN-stage Journey.** Phạm vi gồm provisioning/default, cấu hình có phân quyền/version/idempotency, routing theo PipelineId, stage hợp lệ, Studio → Kanban → Detail, refresh/session/workspace isolation và bảo toàn dữ liệu. Các bằng chứng Connected trước đó vẫn áp dụng vì production source không đổi trong đợt review.

Checkpoint có cấu trúc: [functional-checkpoint.json](../../../task-runtime/deal-pipeline-final-acceptance/functional-checkpoint.json). [Review subject](../../../task-runtime/deal-pipeline-final-acceptance/review-subject.json) ghi commit/branch và hash của production pipeline source; delta so với lúc kết thúc implementation xác nhận **0 production source changes** trong cả hai repo. Chỉ thêm harness, private-browser test/config, tài liệu và cập nhật inventory.

Không có Critical/High regression mới được phát hiện trong phạm vi Dynamic Pipeline đã review và kiểm thử. Đây không phải chứng nhận toàn hệ thống không có regression: các gate baseline đang đỏ dừng tại assertion đầu tiên. Không tuyên bố full-suite green, commercial Won acceptance, production release hoặc FROZEN. Review engineering này không có reviewer-principal attestation trong môi trường tách biệt.

| Hạng mục | Kết quả | Ý nghĩa nghiệm thu |
|---|---|---|
| PL-21: current persisted Deal ở inactive stage | NOT_APPLICABLE | Không thể đạt qua migration/lifecycle được hỗ trợ hiện hành |
| Historical replay trỏ stage đã inactive | PASS | Fixture thật, API/SQL; khác với current Deal GET |
| Guard cấm nghỉ stage đang được tham chiếu | PASS | 409 PIPELINE_STAGE_IN_USE |
| Create/change vào inactive stage | PASS | 422 DEAL_STAGE_INACTIVE |
| Won Connected visual/journey hợp lệ | BLOCKED_CONTRACT | Thiếu write contracts/evidence owner-authoritative |
| Won presentation terminal logic | PASS, unit-only | Bằng chứng từ gate pipeline authority; không phải visual Connected |
| Won presentation screenshot mới | NOT_RUN | Không dùng mock để thay cho Connected acceptance |
| Attribution của 4 BE + 5 FE failures | PASS | Đã tái hiện/đối chiếu baseline độc lập |
| Kết quả quality toàn repository | FAIL | Các lỗi baseline vẫn tồn tại; không được biến thành PASS |
| Restricted-role API và UI | PASS | CRM đọc được; Studio/write/lifecycle bị chặn đúng quyền |
| Pipeline tenant isolation và query count | PASS | Header workspace không phải member bị từ chối; một registry SELECT/request |
| Registry lock dưới tải lớn | NOT_RUN | Chỉ review cơ chế và đo các request nhỏ; chưa load test |

## PL-21: invariant và trạng thái lịch sử

`CountStageUsageAsync` trong `Deals/Infrastructure/Persistence/EfDealsPersistence.cs` đếm các Deal **đang có** WorkspaceId/PipelineId/StageCode đó. Không lọc bỏ Deal archived. Khi count lớn hơn 0, `DealPipelineRegistry.SaveAsync` từ chối xóa/nghỉ stage bằng 409. Create và ChangeStage chỉ chọn stage active. Migration `20261023150422_AuthoritativeDealPipelines` chỉ chấp nhận bảy mã legacy đã biết và tạo cả bảy stage active trước khi backfill PipelineId.

Vì vậy, một **current Deal row** ở inactive stage không có đường tạo hợp lệ từ tập trạng thái/migration và command được hỗ trợ hiện tại. Không có API import hoặc fixture setup chính thức khác được xác minh cho trạng thái đó. Phân loại PL-21 là NOT_APPLICABLE trong lifecycle hiện hành; không phải PASS cho một current-GET scenario chưa chạy. SQL FK chỉ bảo đảm reference tồn tại và cùng workspace/pipeline, **không** kiểm tra IsActive; lập luận này dựa cả migration và application guards, không dựa FK một mình.

Điểm cần sửa trong cách diễn đạt trước đây: usage guard không kiểm tra mọi JSON snapshot/audit lịch sử. Một Deal đã đi A → B thì có thể nghỉ A khi không còn current Deal nào ở A. Replay lệnh tạo vẫn trả snapshot bất biến ở A. Đây là lịch sử hợp lệ, không phải current state sai.

Đã tạo fixture nhỏ bằng command thật, chỉ trong workspace test được provision ở task trước:

| Fixture | Identity |
|---|---|
| Workspace | `ws_772b487b87e84456b5020631fbaedd27` / `ki-m-th-pipeline-provisioning-c019e52d` |
| Pipeline | `pipeline_56d0aa7237c9465a811ae3f03707ef5b` |
| Contact | `contact_b4c3fcf6eaf044d9a10f3a6b0b06ceb9` |
| Deal | `deal_815f4f74ff304c65968ee95f01fbc86b` |

Luồng chạy thật: tạo ở REVIEW_INTAKE → chuyển sang REVIEW_FOLLOWUP → nghỉ REVIEW_INTAKE → replay create. Replay trả `REPLAYED`, stage REVIEW_INTAKE; GET hiện tại và SQL vẫn REVIEW_FOLLOWUP, version 1, active=true. Thử nghỉ REVIEW_FOLLOWUP (vẫn giữ một OPEN khác active để đi tới usage guard) bị 409. Create mới và change-stage về REVIEW_INTAKE đều bị 422. Toàn database có 0 current Deal ở inactive stage.

Rerun cùng các idempotency key đã PASS và không tạo duplicate fixture. UI restricted-reader sau refresh hiển thị REVIEW_FOLLOWUP, không biến snapshot lịch sử thành trạng thái hiện tại. Presentation unit sẵn có kiểm tra giữ label/current inactive stage khi nhận historical representation; không gọi đó là current persisted inactive Connected E2E.

Bằng chứng: [API/SQL review và denied writes](../../../task-runtime/deal-pipeline-final-acceptance/api-review.json), [read-only SQL inventory](../../../task-runtime/deal-pipeline-final-acceptance/source-sql-review.json), [restricted UI](../../../task-runtime/deal-pipeline-final-acceptance/browser/restricted-role-review.json).

Nếu sau này business cho phép một migration/import chính thức giữ current Deal tại retired stage, cần admission và acceptance fixture mới. Không nới invariant hiện tại chỉ để đạt PL-21.

## Won: contract gap và finding High có sẵn

SQL read-only xác nhận: Quotes=0, Orders=0, WonDeals=0, LinkedAcceptedQuotes=0, LinkedConfirmedOrders=0. Backend Quotes/Orders chỉ map GET list/detail; không có create/accept/confirm command được hỗ trợ trong slice hiện hành. Một API được mô tả READY ở Frontend không thay thế được command Backend thật. Không tạo chứng từ bằng SQL hoặc gọi MarkWon bằng evidence tự bịa.

**Finding H-01 — High, pre-existing: MarkWon chưa xác minh evidence từ owner.**

Trong [MarkDealWon/Handler.cs](../../src/UnicoreCRM.Crm/Deals/Application/MarkDealWon/Handler.cs), sau authorization, code gọi `deal.MarkWon(...)` và không có business precondition/resolver kiểm tra chứng từ. [MarkDealWonValidation.cs](../../src/UnicoreCRM.Crm/Deals/Application/MarkDealWon/MarkDealWonValidation.cs) chỉ kiểm tra enum `QUOTE_ACCEPTED`/`ORDER_CONFIRMED`, cú pháp sourceId và timestamp. Không có bằng chứng kiểm tra source tồn tại, workspace, source Deal, trạng thái Accepted/Confirmed hoặc occurrence time authoritative.

Đây là finding từ inspection code, không phải đã chạy exploit tạo Won giả. Hai file có SHA-256 khớp hoàn toàn task-entry trước Dynamic Pipeline; các endpoint Quotes/Orders cũng khớp. Vì vậy không phân loại là regression mới của Pipeline. Tuy nhiên không được gọi endpoint chấp nhận payload là bằng chứng hệ thống đã xác minh evidence. Backend gap này là blocker trước nghiệm thu/release luồng thương mại Won, độc lập với checkpoint registry/OPEN-stage.

Giữ **BLOCKED_CONTRACT** cho Won Connected acceptance. Dependency tối thiểu cho một task riêng: public owner contracts đọc/xác minh Quote/Order commitment cùng workspace và source Deal, rồi enforce trong command MarkWon; bổ sung đường create/accept/confirm hoặc owner-controlled fixture được thừa nhận. Kiểm thử negative missing/wrong-tenant/wrong-Deal/wrong-state/replayed intent trước nghiệm thu. Không đọc trực tiếp bảng module khác từ Deals hoặc thêm client bypass.

## Quality baseline và phạm vi khẳng định

Reviewer độc lập đã so sánh lỗi và tái chạy các gate baseline, không chỉ dựa vào hash của file test không đổi.

| Failure | Task-entry baseline | Dynamic Pipeline | Đối chiếu |
|---|---|---|---|
| Backend owner mapping | 549 messages | 549 | Exact |
| Backend durable metadata | 16 messages | 16 | Exact |
| Backend layer/workflow | 138 messages | 138 | Chỉ khác compiler state-machine d__5/d__6; normalize riêng suffix này thì bằng nhau |
| Backend endpoint metadata | 55 messages | 55 | Exact |
| FE connected config authority | Webhook label mismatch | Cùng assertion | Tái chạy baseline cùng dependencies |
| FE opportunity presentation | Pre-V5 navigation string | Cùng assertion | Tái chạy baseline pipeline source |
| FE backend-readiness | Hai docs Studio Connections thiếu status | Cùng hai docs | Tái chạy baseline doc registry |
| FE bilingual | Tám JSX literals trong ba file | Cùng tám literals | Tái chạy baseline translation/audit inputs |
| FE quality pipeline | 347 != stale345 | 348 != stale345 | Thêm đúng một authority gate; assertion stale đã có từ trước |

Backend full run đã ghi nhận 52 PASS/4 FAIL, so với baseline 46 PASS/4 FAIL; sáu test pipeline mới PASS. 1.202 Backend source/test entry files được audit khớp reconstruction. Frontend reconstruction khôi phục 2.641/2.643 entry files; không khôi phục được hai file untracked cũ `dealDetailPresentation.ts` và `deal-workspace.test.mts`. Reviewer xác định chúng không gây các first-failure signature đang so sánh. Các assertion nằm sau điểm fail chưa được thực thi đầy đủ, nên không suy rộng thành universal regression-free.

Đợt review này không đổi quality gate/assertion nào. `npm run verify` được chạy lại và vẫn chỉ ra đúng ba diagnostic TypeScript có sẵn trong hai file test; không có diagnostic mới ở review test/config. Full verify dừng ở lint. Các gate khác cần đọc kết quả riêng, không được ghi toàn bộ suite PASS. Build/source compilation và 19 focused PASS từ checkpoint trước được giữ làm bằng chứng tương ứng với production subject không đổi.

Kiểm tra mới sau review: pipeline-authority gate PASS, repository inventory PASS, security scan PASS. Không chạy lại build/full Backend suite vì không sửa production code; các kết quả trước được gắn với subject hash không đổi, không được trình bày như một lần chạy mới.

Xem [independent baseline audit](../../../task-runtime/deal-pipeline/frontend/independent-baseline-review.json), các `independent-baseline-quality.*` log/JSON cùng thư mục, [full verify mới](../../../task-runtime/deal-pipeline-final-acceptance/frontend-verify.log), [Backend comparison](../../../task-runtime/deal-pipeline/backend/architecture-baseline-comparison.json) và [focused results](../../../task-runtime/deal-pipeline/frontend/focused-results.json).

## Performance và security

`ReadAsync` giữ Serializable transaction và exclusive workspace application lock. `EnsureDefaultAsync` cũng dùng exclusive lock để initialization không đua. Resource lock chứa WorkspaceId, được nhả theo transaction; timeout được map sang retryable RESOURCE_BUSY. Cách này đúng về serialization trong các test đã chạy nhưng khiến reads/writes cùng workspace phải xếp hàng. **Advisory P2:** review tách initialization và steady-state shared/read path trong task riêng; giữ double-check/default atomicity và kiểm thử concurrency. Không đổi cơ chế lock trong acceptance review.

`LoadPipelinesAsync` dùng một query `Where WorkspaceId` + eager `Include(Stages)` + no tracking. UI dùng shared registry resource, không fetch registry theo từng card. Ba request mới với hai pipeline trong fixture workspace đều có đúng một registry SELECT; checkpoint trước đã đo một SELECT với 12 pipeline/56 stages. Đây là bằng chứng chống N+1 trong đường đọc đã kiểm tra, không phải load/capacity claim. Load test: **NOT_RUN**.

Tài khoản restricted có đúng `deals.read` và `workspace.context.resolve`, không `studio.read`, `studio.configure`, `deals.update` hoặc quyền close. Không đổi quyền hoặc tạo user mới trong review này.

| Thử nghiệm restricted-role | Kết quả |
|---|---|
| GET registry và Deal trong workspace hợp lệ | 200 |
| POST/PATCH pipeline | 403 |
| Create Deal / change stage / MarkWon | 403 |
| GET registry/Deal với header workspace không có membership | 403 |
| Deal UI sau login/refresh | Đọc được, không có control đổi stage/close |
| Direct Studio URL | Access denied, không render form/save controls |

Browser riêng của agent chạy PASS 1 test trong 13,2 giây; desktop/mobile được kiểm tra, không document overflow. Không có CRM mutation từ UI. POST `/access/records/evaluate` được phân loại đúng là authorization read; lần chạy đầu thất bại do harness đếm nhầm nó là business write, sau đó đã sửa phân loại và chạy lại PASS. Không bỏ assertion permission hay giả response.

Ảnh: [Deal mobile chỉ xem](../../../task-runtime/deal-pipeline-final-acceptance/browser/restricted-deal-mobile.png), [Studio bị chặn](../../../task-runtime/deal-pipeline-final-acceptance/browser/restricted-studio-denied.png). Pipeline reads không lộ dữ liệu workspace khác trong các probe đã chạy. H-01 là gap integrity của commercial evidence; không dùng kết quả read-isolation để phủ nhận gap đó.

## Bảo toàn dữ liệu và tái kiểm thử

Demo vẫn 1.050 CRM records/180 Deals. Toàn bộ CRM trong các workspace ngoài fixture mới được so sánh count/hash trước và sau và không đổi. Fixture review gồm một Contact, một Pipeline, một Deal trong workspace test riêng; stages/Deal được thay đổi qua command hợp lệ, không SQL writes. Không tạo Quotes/Orders/Won. Không đổi connection string, gọi provider bên ngoài hoặc chạy CRM dataset bootstrap. Private hosts/browser servers dừng khi kết thúc.

```powershell
cd D:\Project_All\UnicoreCRM\backend
# Existing synthetic provisioning/readonly identities and test workspace are prerequisites.
pwsh -NoProfile -File scripts/review-deal-pipeline-acceptance.ps1 -Mode Api
pwsh -NoProfile -File scripts/review-deal-pipeline-acceptance.ps1 -Mode Browser
pwsh -NoProfile -File scripts/review-deal-pipeline-acceptance.ps1 -Mode Inspect

cd D:\Project_All\UnicoreCRM\frontend\unicorecrm-web
npm run quality:gate -- --gate quality.deal-pipeline-authority
npm run repo:check
npm run verify
```

Harness chỉ chấp nhận SQL local `DESKTOP-ICH225R / UnicoreCRM_Development` và fixture workspace ID đã xác minh. Api mode tạo fixture bằng intent/key ổn định rồi thử các thao tác phải bị từ chối. Browser không ghi CRM. Inspect chỉ đọc SQL/source hashes. Credential chỉ lấy từ config/secrets local vào process; không có credential trong source/evidence.

## Backlog và điều kiện nâng checkpoint

| Priority | Vấn đề | Điều kiện đóng |
|---|---|---|
| P1 / High | H-01: authoritative Won evidence verification | Owner contracts + tenant/Deal/state/time verification + negative/replay tests; cần trước commercial release |
| P1 dependency | Quote create/accept và Order create/confirm contracts/fixture | Owner-authorized Connected commitment journey; sau đó chạy Won visual/E2E |
| P2 | Exclusive registry read serialization | Thiết kế steady-state read an toàn, concurrency/load evidence |
| P2 | Bốn Backend architecture baseline failures | Fix đúng authority/layer/metadata; full suite green |
| P2 | Năm FE focused baseline failures, ba TypeScript diagnostics, 44 unrelated OpenAPI baseline findings | Task riêng, không nới gate hoặc blanket baseline acceptance |
| P2 verification | Load test và các assertion bị red gates che phía sau | Bounded capacity plan và full rerun sau khi baseline được xử lý |

PL-21 không phải backlog implement để nới business rule. Chỉ mở lại nếu một requirement/migration mới hợp lệ tạo current inactive Deal. Functional Pipeline checkpoint có thể được ghi nhận PASS với các ngoại lệ/dependency rõ ở trên; nghiệm thu Won và full product/release vẫn chưa đủ điều kiện.

## Cleanup và Git

Không commit/merge/reset local changes. Baseline reconstruction tạm của reviewer nằm ở `C:/Users/welcome/AppData/Local/Temp/unicore-deal-entry-review-o9pfszm0`. Automatic approval review chặn thao tác xóa tree, kể cả sau khi tháo junction, với lý do chính xác **"blocked by policy"**. Junction `node_modules` đã được tháo an toàn; node_modules thật và mọi evidence được giữ nguyên. Thư mục tạm còn lại được giữ làm review evidence, không nằm trong repo. [Cleanup record](C:/Users/welcome/AppData/Local/Temp/unicore-deal-entry-review-cleanup.json). Không thử bypass policy. Các private ports 5098/3042 được dừng sau kiểm thử.
