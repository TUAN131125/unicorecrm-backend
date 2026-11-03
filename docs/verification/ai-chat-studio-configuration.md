# AI Chat và Studio AI Configuration

Ngày 22/10/2026, Asia/Saigon. Đã sửa luồng connected và chạy nghiệm thu API/SQL/browser với **deterministic provider transport**. **Real-provider integration và chất lượng phân tích Customer của model thật: BLOCKED_NO_CREDENTIAL.** Chưa đủ điều kiện tuyên bố production-ready hoặc nghiệm thu đầy đủ với provider thật.

## 1. Thành phần đã hoạt động và trạng thái

| Phạm vi | Kết quả | Giới hạn bằng chứng |
|---|---|---|
| Studio catalog Gemini/OpenAI, model allowlist, draft, Workspace key, test, activate, rotation, provider change | PASS trong deterministic/connected tests | Code adapter production chạy trong Backend; provider HTTP transport được thay rõ ràng trong Development test |
| Credential lưu được bảo vệ, không echo key; read/manage; optimistic concurrency/idempotency | PASS | SQL/API test; không chứng nhận hạ tầng production |
| Disable khi có configuration | PASS | Không có active snapshot thì chặn; deployment key có sẵn cũng không được dùng; zero provider attempts được kiểm tra |
| Key và provider qua restart; wrong ring fail-closed; restore ring phục hồi | PASS | ApiHost + SQL Server 17.0.1000.7, Windows, cùng service identity và application name |
| Lead/Customer detail → `/ai/advisories`, kể cả cold route/refresh | PASS | Browser gửi reference; không gửi browser CRM collection, credentials hay workspace authority trong body |
| Lead/Contact/Organization/Customer/Deal/Task owner context; capabilities, scope, FLS, evidence | PASS | Six-owner API/SQL suite: 100 checks; một phần dùng development deterministic provider trực tiếp, phần orchestration dùng WorkspaceProduction + deterministic HTTP |
| Tạo Lead, Contact, Customer, Task liên quan bằng UI | PASS | Browser connected; Customer được onboard từ Contact, không dùng SQL/API để tạo các bản ghi này trong hành trình UI |
| Lỗi credential, rate-limit, outage, permission; Workspace switch, logout/login, refresh | PASS | Negative scenarios chủ động trên dữ liệu test riêng; provider faults được mô phỏng tại transport |
| Gọi OpenAI/Gemini thật | BLOCKED | Cả `OPENAI_API_KEY` và `GEMINI_API_KEY` đều không được cấp; local configuration không có AI section |
| Model thật đưa kết luận Customer có căn cứ, locale và chất lượng đề xuất chăm sóc | BLOCKED | Không suy chất lượng model từ output deterministic hoặc chỉ từ evidence references |
| Global analytics / AI mutations / Multi-Currency Phase 2 | NOT_RUN / không triển khai | Giữ các giới hạn đã yêu cầu |

Backend vẫn có JSON contract `summary`, `attentionPoints`, `suggestedNextAction` nullable và evidence references do server tạo. Output validator kiểm tra hình dạng, không chứng minh mọi câu model nói đều đúng. Prompt được siết để phân biệt dữ kiện/suy luận/đề xuất, trích entity type/id, coi dữ liệu thiếu/bị hạn chế là chưa biết và tôn trọng health confidence/reason code.

## 2. Root cause và sửa chữa

| Lỗi | Bằng chứng / nguyên nhân | Sửa |
|---|---|---|
| Disable vẫn có thể chạy AI | Resolver dùng deployment default mỗi khi `FindActivePolicyAsync` trả null. Disable xóa active snapshot; với pending draft, status vẫn là DRAFT | Nếu Workspace đã có configuration mà không có active snapshot, trả `AI_PROVIDER_UNAVAILABLE`, reason `WORKSPACE_AI_INACTIVE`; không gọi adapter |
| Studio không điều khiển Chat ở Development mặc định | `appsettings.Development.json` chọn `DevelopmentDeterministic`, bypass Workspace resolver | Đổi default sang `WorkspaceProduction`; deterministic chỉ được bật rõ ràng trong harness Development |
| Hỏi Lead không gửi HTTP; Customer cold route thiếu context | Hook global không map Lead; các builder khác phụ thuộc browser snapshot có record | Connected route tạo type/id cho sáu owner, với các collection trống. Backend tải lại theo quyền. Dashboard không có reference nên không mở global analytics |
| Customer thủ công không gửi HTTP | Form tạo thuộc tính optional với giá trị undefined; runtime OpenAPI validator kiểm tra trước serialization và từ chối | Bỏ thuộc tính trống khi dựng request. `customer-create-request-proof.log` chứng minh request trước invalid/sau valid |
| Sau khi tạo Customer có guard “0 khu vực chưa lưu” | Callback điều hướng trước khi form đã lưu được đóng và unregister guard | Modal Customer hoàn tất/đóng bằng `flushSync` trước callback điều hướng; không sửa shared guard |
| Lỗi khóa bị gộp thành provider unavailable chung | AuthenticationFailure, thiếu key, lỗi giải mã đều mất lý do | Giữ error code/schema hiện hữu; thêm safe reason vào `fieldErrors.provider`, UI dịch reason allowlist, không render raw diagnostics |
| Lựa chọn provider chưa lưu vẫn cho Set key/Test | Backend thao tác cấu hình persisted, trong khi màn hình có lựa chọn chưa lưu | Chặn Set key/Test/Activate khi dirty; reset form theo Workspace/account và xóa input key khi đổi provider |
| Hội thoại dài bị Backend từ chối | Browser có thể gửi 12 × 2000 ký tự, vượt giới hạn tổng 8000 | Giữ các lượt mới nhất trong budget 8000; regression test đã chạy |

Chính sách giữ lại: Workspace **chưa từng có configuration** vẫn có thể dùng deployment default theo hạ tầng hiện hữu. Draft đầu tiên chưa active và cấu hình đã Disable đều bị chặn. Draft sửa khi có active snapshot vẫn giữ snapshot cũ chạy cho tới khi activate/disable. Disable không hủy request đã được admit trước thời điểm Disable.

Hai lỗi harness cũng được sửa: six-owner fixture tạo Deal với Contact không tồn tại; deterministic fault marker cũ trong conversation làm các câu tiếp theo tiếp tục lỗi. Những lần chạy thất bại được giữ trong evidence, không đổi thành PASS. Readiness probe của harness mới dùng `/auth/session` 401; response `application/problem+json` dạng byte[] được decode UTF-8 trước assertion.

## 3. File changes và ảnh hưởng

Danh sách đầy đủ **26 source/test/config files (14 Backend, 12 Frontend)** cùng SHA-256 nằm trong [source-manifest.json](evidence/ai-connected-20261022/source-manifest.json).

- Backend: Workspace resolver, provider failure mapping, configuration/advisory/proactive error translation, prompt; Data Protection registration; Development default; provider verifier và hai SQL/browser harness.
- Frontend: connected route context, bounded conversation, safe failure presenter, Chat error display; Studio form state/dirty controls; Customer onboarding modal; bilingual AI guidance và tests.
- Không thêm endpoint, capability, schema migration, framework hay microservice. Không sửa generated clients/OpenAPI hoặc compatibility baseline. Safe reason dùng fieldErrors đã được contract cho phép; API contract gate vẫn PASS với 328 operations/300 ready/28 blocked.
- So sánh task path list với `p0-checkpoint-finalization-20261021/cumulative-candidate-manifest.json`: **0 pinned overlap**. Không sửa các file P0. `studio/configuration.ts` đã dirty trước task; chỉ phần AI seed/version/reviewedAt được chỉnh ở lượt này, giữ các thay đổi Studio khác.
- Không stage, commit, push hoặc tạo waiver. Các database test riêng được giữ; process ApiHost/Vite của harness đã dừng. Không cấu hình key giả vào workspace CRM thường dùng của người dùng.

## 4. Bằng chứng thực thi

Evidence root: [ai-connected-20261022](evidence/ai-connected-20261022/).

| Nhóm tách biệt | Lệnh / artifact | Kết quả |
|---|---|---|
| Deterministic provider | `dotnet run --project scripts/AiProviderVerifier/UnicoreCRM.AI.ProviderVerifier.csproj` — [log](evidence/ai-connected-20261022/provider-deterministic.log) | PASS; adapters/schema, retry/failover, circuit breaker, durable DP processes, inactive Workspace với deployment key, fixture history |
| Real provider | Cùng project, thêm `-- live` — [log](evidence/ai-connected-20261022/provider-real.log) | Exit 2; Gemini và OpenAI BLOCKED_NO_CREDENTIAL; không có vendor request thật |
| ApiHost + SQL six-owner | `pwsh -NoProfile -File scripts/verify-ai-assistant.ps1 -DatabaseName <fresh-name>` — [100 checks](evidence/ai-connected-20261022/six-owner-connected-rerun.log) | PASS; SQL LocalDB 15.0.4382.1; synthetic owner fixtures; không gọi đây là UI-only hoặc real-provider test |
| Browser/Playwright + SQL + restart | `pwsh -NoProfile -File scripts/verify-ai-workspace.ps1` — [connected-results](evidence/ai-connected-20261022/UnicoreCRM_AI_2a409e784ee54416bc701961797fbd8a/connected-results.json), [browser log](evidence/ai-connected-20261022/UnicoreCRM_AI_2a409e784ee54416bc701961797fbd8a/browser.log) | PASS; Chromium 1 scenario với 9 nhóm checkpoints; SQL Server localhost 17.0.1000.7; 14 execution rows; fault transport deterministic |
| Backend build qua adopted gate | `python ../.unicore-ai/tools/run_gate.py --repo-root . --task backend-work/tasks/CRM-AI-002.json --gate-id build ...` — [record](../../backend-work/evidence/CRM-AI-002/build.json) | PASS, LOCAL_EXECUTION; không phải trusted CI/freeze |
| Frontend focused gates | `node scripts/quality/run-quality-pipeline.mjs --gate <gate-id>` | PASS: AI application/governance, shell AI, Studio configuration, strict core/regions, type-safety budget, guidance, presentation, mutation authority, API contract/management, frontend-backend separation |
| Frontend production build | `npm run build` — [log](evidence/ai-connected-20261022/build-frontend.log) | PASS; bundle budget dưới 500 KiB |
| Backend architecture | `dotnet test tests/UnicoreCRM.ArchitectureTests/UnicoreCRM.ArchitectureTests.csproj ...` — [TRX](evidence/ai-connected-20261022/architecture/ai-connected.trx) | FAIL: 33 pass/4 fail (owner mapping, durable metadata, layer/workflow edges, endpoint admission); tên lỗi trùng nhóm được ghi trong checkpoint cũ, không cấp waiver |
| Full frontend verification | `npm run verify` — [log](evidence/ai-connected-20261022/verify.log) | FAIL ở lint: nullable errors trong `deal-quote-transition-outcome.test.mts` và `studio-first-visit.spec.ts`; 326 gate sau NOT_RUN qua full pipeline. Focused gates kể trên chạy riêng |
| Other repository gates | Các log `quality.backend-readiness`, `quality.quality-pipeline`, `quality.repository-inventory` | FAIL: 2 dependency cycles; 347 != 345; inventory drift. Không sửa baseline để làm xanh |

Không chạy lại một checkout baseline sạch trong task này, nên không tuyên bố mọi failure byte-identical với baseline. Chúng vẫn là FAIL, dù đã được báo cáo trước hoặc nằm ngoài các file task sửa.

Tooling control gap: lookup/derive capabilities không tìm thấy `requestAiAdvisory` trong operation index của `.unicore-ai`, trong khi Task Packet CRM-AI-002, actual endpoint và OpenAPI hiện hữu đã có operation này. Không tự sửa authority manifest. Independent reviewer principal attestation/trusted evidence audit: **BLOCKED** trong phiên này; không tự nhận independent PASS hoặc FROZEN.

## 5. Hành trình demo

1. Đăng nhập, chọn Workspace. Vào Studio → Trợ lý AI, chọn Gemini/OpenAI và model trong catalog; chọn Workspace source, lưu draft rồi nhập key. Test thành công mới Activate. Input key được xóa sau khi lưu; GET chỉ trả credentialConfigured.
2. Trong CRM tạo Lead thủ công. Mở detail, hỏi “Tóm tắt Lead này theo bằng chứng hiện có”. Request chỉ mang reference Lead; Backend đọc owner summary trước khi gọi provider.
3. Tạo Contact thủ công, vào Khách hàng → Tạo Customer, chọn Contact đó. Tạo Task trực tiếp từ Customer.
4. Mở Customer, hỏi “Phân tích tình trạng, rủi ro và đề xuất chăm sóc. Nêu rõ dữ liệu chưa đủ”. Backend summary có các field được cấp quyền và health assessment có confidence/reason code; evidence link mang Customer ID thật.
5. Refresh hoặc logout/login rồi mở lại Customer; hỏi lại. Tắt AI trong Studio rồi thử lại phải hiện lỗi inactive, không được chạy deployment fallback. Đổi provider/key phải lưu, test và activate lại.

Run UI giữ các ID trong [journey.json](evidence/ai-connected-20261022/UnicoreCRM_AI_2a409e784ee54416bc701961797fbd8a/journey.json). Customer `customer_94336223d0c64215b9bcafa453747497`; Task `task_69c58ae432734098a56c1db7a31c2d6a`. Customer/Task versions và số lượng không đổi do Chat. Screenshot trong thư mục Playwright là **output deterministic “Provider configuration verified.”**, chỉ chứng minh transport/render/evidence, không chứng minh phân tích rủi ro của model thật.

## 6. Còn thiếu và vận hành

- **BLOCKED:** nghiệm thu với key thật, account/model availability, grounding/locale/safety của câu trả lời thật và tình huống outage/rotation thật ở vendor. Live smoke hiện chỉ hỗ trợ synthetic health check; kể cả live smoke PASS sau này cũng chưa thay nghiệm thu Customer thực.
- **NOT_RUN:** production deployment, multi-instance key sharing, Linux key-ring protection, load/SLA, expired-session/refresh-token attack campaign, external conformance và independent principal attestation.
- **Giới hạn hiện hữu:** focused Customer tool không tự mở rộng sang mọi Contact/Task/Deal liên quan. API đã hỗ trợ explicit Customer + Task references và kiểm tra quyền riêng cho từng reference; browser hiện gửi focused Customer. Không tự xây global read model hoặc analytics để lấp chỗ trống.
- **Key ring:** production phải cấu hình `AI__DataProtection__KeyRingPath` tới thư mục bền vững ngoài source/deploy output, ACL chỉ cho service identity và quản trị, backup cùng chính sách recovery. App name giữ `UnicoreCRM.AI`. Windows test chứng minh key mới ở path cấu hình được DPAPI bảo vệ theo user; thay máy/service identity cần cơ chế phục hồi phù hợp. Không tự xóa key cũ khi rotate; thay đổi này không mã hóa lại các XML key cũ đã tồn tại. Non-Windows cần custody/encryption phù hợp của môi trường, chưa được chứng nhận ở đây.
- **FAIL còn nguyên:** architecture/full verify/repository gates nói trên; không thay compatibility baseline, không waiver. AI mutations tiếp tục bị chặn trong ConnectedAiRuntime. Không mở thêm proactive mutations; proactive application chỉ nhận safe provider failure reason tương thích với shared provider.

Catalog hiện hữu được giữ nguyên: Gemini 2.5 Flash/Pro, GPT-5 mini/GPT-5. Kiểm tra tài liệu primary về [Gemini structured output](https://ai.google.dev/gemini-api/docs/structured-output) và [GPT-5 mini structured outputs](https://developers.openai.com/api/docs/models/gpt-5-mini) chỉ hỗ trợ lựa chọn wire contract; không thay thế live account conformance.
