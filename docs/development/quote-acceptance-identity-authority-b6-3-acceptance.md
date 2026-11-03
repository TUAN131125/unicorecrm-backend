# B6.3 — Identity & Signer Authority Evidence: Development acceptance

Ngày 27/10/2026 (Asia/Saigon). **PARTIAL_FUNCTIONAL_PASS trong phạm vi gói A đã duyệt. Positive SOURCE_CHECKED / READY_FOR_INDEPENDENT_REVIEW vẫn BLOCKED_CONTRACT.** Không có actual approved identity/authority source; không tạo VERIFIED, ACCEPTED, WON hoặc commercial commitment receipt.

## A. Git/SQL Baseline

| Repository | Branch | HEAD giữ nguyên |
|---|---|---|
| Backend | master | 13cd39eba01ebc9dc238f8b0ef98e174c1d4d3f6 |
| Frontend | feat/deal-compact-kanban-card | 814cc4685b6d8f8ead2fc945f4607a2d0168320d |

Cả hai repository đã dirty trước task. Entry hashes: 2.914 Backend files và 2.710 Frontend files; một frontend file đã bị xóa trước task được giữ nguyên trạng thái. Không checkout/reset/reseed/commit/push/merge. Chỉ migration bổ sung trên đúng local `DESKTOP-ICH225R / UnicoreCRM_Development`.

Entry SQL: 275 Quotes, 195 Deals, 38 issued PDFs, 28 acceptance proofs, 73 Workspaces. Bộ bảo vệ 35 bảng bao gồm các hàng CRM/demo, Quote/Core/B1–B6.2, Pipeline, quyền và IAM. Đối chiếu cuối: **5.514/5.514 hàng entry giữ nguyên SHA-256**, bao gồm dữ liệu demo gốc; xem [preservation](../../../task-runtime/quote-identity-authority-b6-3/preservation-sealed-comparison.json). Không ghi credentials/token vào evidence.

## B. CRM Business Review

BuyerRef chỉ là CONTACT hoặc ORGANIZATION_ACCOUNT. Customer là commercial account subject khác với các stakeholder links; CustomerId không thay thế identity Buyer. Contact có thể có nhiều affiliation đang hoạt động, với tối đa một primary. Role/title/email/ContactId/primary affiliation là dữ liệu CRM, không chứng minh danh tính pháp lý hoặc quyền ký.

Contract mới yêu cầu Contact và relationship ID cụ thể đối với Buyer tổ chức. API thật đã chứng minh lựa chọn affiliation thứ hai, kể cả Buyer ID khác casing; thiếu/sai affiliation bị từ chối. Kết thúc affiliation qua Contact owner làm Contact.Version tăng và assessment nhận ra snapshot stale/inactive. Không truy vấn bảng Contact/Organization từ Quotes.

## C. Identity Evidence Policy

Tái sử dụng Acceptance Case và Proof của B6.1/B6.2. Identity references nhận IDENTITY/SUPPORTING_DOCUMENT; authority references nhận DELEGATION/SUPPORTING_DOCUMENT. Backend lấy SHA-256/type từ proof metadata đã lưu, kiểm tra đúng Workspace/Case và effective CLEAN. Client không gửi trust level, actor, checkedAt hoặc verified flag.

Binding có thể ghi nhận hồ sơ còn thiếu chứng cứ để hiển thị lý do; trạng thái chỉ SELF_REPORTED/DOCUMENT_SUBMITTED/MISSING. Contact-only không tạo identity verification. Metadata/history tiếp tục đọc theo quyền khi scan không còn đủ điều kiện; file bytes vẫn theo capability và CLEAN riêng của B6.2.

## D. Authority Policy

Giữ policy Workspace versioned của documentary workflow: VND Grand Total tối đa 100 triệu; currency khác cần policy riêng, không FX. Các khai báo effectiveFrom/effectiveUntil, amount/currency và scope được kiểm tra như **claims chưa xác minh**. Hết hạn, chưa hiệu lực hoặc vượt hạn mức tạo blocker; không biến lời khai thành nguồn authority.

Freshness tối đa 24 giờ có pure-rule tests; chưa có actual successful source check để áp dụng một positive freshness decision. CheckedAt không được tạo cho SOURCE_UNAVAILABLE. Source revocation/version changes làm binding cũ không còn đủ điều kiện. Việc thu hồi legal signer authority thực tế vẫn cần nguồn authoritative; không giả lập nó bằng revocation của descriptor.

## E. Approved Source Registry

Thêm registry descriptor hẹp, theo Workspace/Buyer, có source ID/version, issuer, method, requirements, provenance, actor/time và change reason. CREATE/REVISE chỉ tạo DRAFT; REVOKE append version REVOKED. SQL không cho lưu trạng thái APPROVED trong B6.3.

Capability manage không tự tạo trust. Self-admission bị 403; actor độc lập gọi approve nhận 409 `BLOCKED_CONTRACT_SOURCE_METHOD_NOT_ADMITTED`. CanApprove luôn false. Tất cả descriptor được tạo trong QA mang nhãn SYNTHETIC, chưa duyệt; nhóm trên 20 descriptor chỉ kiểm tra pagination, không phải nguồn operational và không chứng minh positive verification.

Source check ghi `SOURCE_UNAVAILABLE`, AttemptedAt/actor/method/policy version/evidence references thực; CheckedAt vắng mặt. Thiếu proof, CLEAN, affiliation hiện hành hoặc source-version binding thì fail-closed. Không có VerificationReceipt hoặc adapter gọi tổ chức/khách hàng bên ngoài.

## F. Identity Domain

Identity Subject là case-bound, server-issued. Binding giữ Buyer, CRM owner snapshot/versions, proof IDs/hashes, source ID/version, người ghi nhận và thời điểm. Thay Contact tạo subject mới; đổi binding cần expectedSubjectId, own resource version và reason. Quote/Root/Revision/Issued Document/PDF SHA/commercial fingerprint được bảo vệ qua Case bất biến và B5 read authority, không lấy từ FE cache.

## G. Authority Domain

Assessment phân biệt trạng thái identity, authority, source, blocker reasons và policy version. Read đánh giá lại facts hiện hành; ledger lưu snapshot lịch sử bất biến. Không chuyển historical assessment thành quyết định hiện hành. ReadyForIndependentReview luôn false khi actual source chưa được admission. Không có self-verification hoặc owner override.

## H. Owner Contracts

`IQuoteAcceptancePartyReader` nằm trong Sales.Contracts, được CRM implement bằng owner authorization và serializable leases. Contact read/fullName/status FLS bắt buộc; với tổ chức thêm organizationRelationships FLS và Organization owner read/record scope. Khóa Contact trước Organization, cùng thứ tự official relationship writes. ID matching theo CI owner identity; BuyerType vẫn chính xác.

Quotes giữ transaction riêng: idempotency lock → canonical Quote aggregate → identity case/source fence → CRM lease. Quyền/eligibility được đọc lại dưới fence; không giữ external provider call trong SQL transaction. Replay reauthorize và không trả historical party dưới quyền của subject khác. Không thêm Sales→CRM project reference hoặc foreign-table SQL.

## I. B6.1/B6.2 Integration

Không tạo PDF storage thứ hai. Proofs/contents/scan results cũ được tái sử dụng; SHA/Case/hash binding/effective CLEAN kiểm tra từ owner data. Thử QUARANTINED, UNSAFE, policy re-scan và scanner restart thật; không override CLEAN, không reset retry counters.

Trong kiểm chứng cuối, scanner cục bộ đã dừng: UI mutation nhận 409 PROOF_EFFECTIVE_CLEAN_REQUIRED với correlation ID. Khởi động lại portable scanner có ownership xác định, tạo public runtime pin mới và re-scan thật cho fixture B6.3. Không xóa scan history hoặc sửa verdict cũ. Runtime digest mới buộc re-scan trước khi release; chỉ Workspace synthetic B6.3 được bật trong verification. Các Workspace khác giữ nguyên dữ liệu và cần policy/runtime hợp lệ cùng re-scan của chính owner để tiếp tục tải nếu CLEAN cũ không còn effective. Đây là recovery kỹ thuật, không phải identity verification. Một lượt chờ 60 giây không đủ cho live lease 90 giây; kiểm thử recovery sau đó dùng bounded wait và đã PASS.

## J. SQL

Migration `20261026165846_QuoteIdentityAuthorityLedger`: thêm `quotes.IdentityAuthorityVersions`, `quotes.AcceptanceEvidenceSourceVersions`, composite Case/Quote FK, keys/indexes/check constraints. Không đổi commercial rows hoặc PDF bytes.

Ledger chỉ append; SaveChanges guards chặn update/delete qua cả sync/async và boolean overloads (8 focused cases). Profile/source resource versions riêng, không bump Quote version. Receipt, audit reference và outbox commit cùng transaction. Probe Development, đúng synthetic Workspace, ném lỗi sau flush: API 500 và SQL Versions/Commands/Outbox trước/sau bằng nhau; retry sau restart commit thành công. Không direct-SQL provisioning/quyền/membership writes.

## K. Frontend

Panel lazy trong Acceptance Case hiện có, dùng application port, generated HTTP client và adapter riêng; không redesign Quote Detail. Hiển thị Buyer, candidate Contact/affiliation, proof links, claims, assessment/blockers, actor/time và lịch sử. Metadata source draft/revoke chỉ cho capability phù hợp; không có nút VERIFIED/acceptance.

Ba capability mới chỉ assignable, không default grants: manageAcceptanceEvidenceSources, recordAcceptanceSourceCheck, assessAcceptanceIdentityAuthority. Source checking còn yêu cầu readAcceptanceProofContent. Có AbortController/Workspace fencing, own If-Match và stable retry keys.

Review tìm và sửa hai nguy cơ mất binding: chỉ giữ proof đầu tiên, và mất source ngoài trang đầu. Form hiện giữ toàn bộ proof arrays và source ID+version đã liên kết; không tự nhận source version mới khi sửa reason. Browser thật kiểm chứng hai proof và source ngoài 20 dòng đầu. Guidance Việt/Anh và version cập nhật. Form hẹp có controls đầy chiều rộng, lịch sử có thể mở xem.

## L. API/SQL Acceptance

Evidence root: [task-runtime](../../../task-runtime/quote-identity-authority-b6-3). Mỗi thư mục run có result.json/cases.json; các HTTP failure giữ code/detail/correlation ID, không giữ authorization headers.

| Run / evidence | Kết quả và phạm vi |
|---|---|
| api-22c2da06a347400b98579a6a9917b50d | PARTIAL_FUNCTIONAL_PASS; 33 requests Core→B1→B5→B2→Case→CLEAN→B6.3, replay, self-check/admission, revoke, restart/readiness |
| read-750c2bba52c84baa948f5cae54fb2011 | PASS; claims, stale/subject mismatch, cross-Workspace, concurrent source updates 200+412, FLS, rollback |
| read-38b595a99f10430992227a34c81f6f07 | PASS; owner CRM secondary affiliation, wrong/missing relation, end/version invalidation |
| read-6de215d42a224e19b35e6e46617d8a86 | PASS; actual UNSAFE reference denied, policy-stale CLEAN denied, restart recovery, attachment permission and read latency |
| read-f67c358299aa4577a2fdc7f18e474aab | PASS; two CLEAN proof links and out-of-page unapproved source fixture |
| read-8e935d199d764b1b90e1fb29071001f5 | PASS; FLS/content restriction and cached source-check replay reauthorization |
| read-c761d4e8072041ac9e409306efcc561f | PASS; OWN record scope and replay denied with 404, existence not disclosed; official role restoration |
| read-7ca6739e2323438e843fec68b9c49f00 | PASS; forged source status/checker/checkedAt rejected 422, Contact-only insufficient, missing-proof source check denied, full binding restored |

Chỉ fixture Workspace `unicore-identity-authority-a72eefc8` và synthetic companion được provision qua official Development/AccessControl mechanisms. Dùng lại ba synthetic accounts đã có, không gửi email/thay credentials. Setup retries để lại các tổ chức/role synthetic được giữ trong evidence, không xóa/reset để che thất bại; tổng delta cuối nằm trong preservation JSON. Các lỗi setup đã sửa theo contract thực: Organization create trả 200, organization mutation result trực tiếp, relationship ID lấy từ owner mutation result, Organization phải active để Quote owner xác nhận.

## M. Connected Browser

Dedicated Playwright Chromium, API 5098 + Vite 3044; không mock/intercept API, không dùng trình duyệt cá nhân. Hai journeys: ghi binding/lý do → kiểm tra đầy đủ proof/source links → refresh → logout/login; independent assessment → source draft administration → vẫn blocked/unverified. Browser validates actual OpenAPI responses.

[Binding regression run](../../../task-runtime/quote-identity-authority-b6-3/read-d61ee442a0eb4affbb1826d117997c61/browser) đã PASS 2/2, kèm screenshot và wire evidence. [Run UI cuối sau recovery](../../../task-runtime/quote-identity-authority-b6-3/read-1b050f7951a94fd8932ff3977c03893f/browser) PASS 2/2, bao gồm form hẹp đã chỉnh bố cục và giữ binding. Lỗi nullability được browser phát hiện đã sửa ở OpenAPI rồi generate lại: các affiliation fields null được Backend bỏ khỏi JSON, không bắt client nhận trường bắt buộc không tồn tại. Không che lỗi tải dữ liệu hoặc bỏ response validation.

## N. Security

PASS cho cross-Workspace, Quote FLS, OWN record scope, content capability, replay reauthorization, source/checker/trust spoofing và creator/requester/recorder/source-creator separation. Không cấp quyền Studio cho người chỉ cần evidence. Không trả raw PDF/storage reference qua identity/source history. Lịch sử identity chỉ chứa summary an toàn; owner lease kiểm tra quyền với candidate hiện hành.

Privacy scope chỉ documentary evidence cho Quote Case; không DOB/biometrics/government-ID dossier. Technical indefinite history chưa phải legal retention policy. Không gọi actual registry/KYC/provider, không tuyên bố chữ ký số hoặc cryptographic identity verification. Read-only independent source review đã sửa lock/replay/provenance và binding-loss findings; không giả attestation hoặc trusted freeze.

## O. Performance

10 actual local identity reads: min 738.10 ms, mean 773.60 ms, max 870.31 ms, dataset trước pagination stress. [Latency samples](../../../task-runtime/quote-identity-authority-b6-3/read-6de215d42a224e19b35e6e46617d8a86/read-latency.json). Đây không phải load/SLA benchmark.

Reads bounded 50 items, cursor offset ≤10.000; mỗi category ≤5 proof references, proof metadata batch; JSON body ≤16 KiB, snapshot sizes có SQL constraints. History không N+1 Contact lookups. Existing B5 authorization/integrity/runtime checks tạo overhead; chưa claim capacity production.

## P. Regression/Quality

- Backend build PASS, 0 warnings/errors. Focused mới 31/31 PASS (23 applicability cases + 8 immutable-save cases).
- Full Backend: **370 PASS / 4 FAIL / 374**; cả tên và error messages của 4 failure giống entry baseline 339 PASS / 4 FAIL / 343. [Exact comparison](../../../task-runtime/quote-identity-authority-b6-3/backend-final-baseline-comparison.json). Không gọi full suite PASS.
- Frontend strict/type-region/type-budget gates PASS; global tsc còn đúng 3 diagnostics entry trong hai test files (deal-quote-transition-outcome nullable model; studio-first-visit optional strings), không thêm source diagnostic.
- API check PASS: 357 operations, 331 wire-ready, 26 explicitly blocked. Wire-ready không đồng nghĩa source verification/acceptance ready.
- Build/bundle PASS: entry 296.59 KiB, largest 498.72 KiB dưới 500 KiB, không tăng budget. Repo inventory/check và API management, presentation, mutation authority, guidance, frontend/backend separation, Quote presentation/direct sale/approval-delivery gates PASS.
- quality.backend-readiness vẫn FAIL cùng documentation-status inventory debt đã có ở B6.2. quality.quote-core-provider-contract harness BLOCKED vì external dependency bindings network/backend-provider chưa được khai báo cho gate đó; các API/SQL/browser thật của task được chạy riêng, không đổi BLOCKED thành PASS.
- SQL: các rows entry, B5 bytes, proof contents, B3 UNVERIFIED history và Dynamic Pipeline giữ nguyên. Final SQL có 0 Quote ACCEPTED, 0 Deal WON, 0 Orders, 0 PaymentPlans; 65 customer decisions vẫn UNVERIFIED. Toàn bộ issued PDF hashes khớp bytes, integrity failures 0. Không rerun toàn bộ historical browser matrix của mọi gate cũ.

### IA-01–IA-41

PASS dưới đây là đúng phạm vi ghi rõ; guard/unit PASS không thay cho positive actual-source verification.

| Case | Kết quả | Evidence / giới hạn |
|---|---|---|
| IA-01 | PASS | Actual Case/B5 binding API |
| IA-02 | PASS | Actual CLEAN proof capture + hashes |
| IA-03 | PASS guard | Quarantined proof binding 409 |
| IA-04 | PASS guard | Actual scanner UNSAFE proof binding 409 |
| IA-05 | PASS guard | Policy-stale CLEAN 409, real re-scan recovery |
| IA-06 | PASS | Foreign Workspace denied 403 |
| IA-07 | PASS | Wrong Buyer/candidate/affiliation denied |
| IA-08 | PASS | Wrong Case/proof reference denied; Case/B5 authority bound |
| IA-09 | PASS | Wrong subject rejected 409 |
| IA-10 | PASS guard | Contact-only persisted as insufficient, never ready |
| IA-11 | PASS guard | Email claim unsupported/rejected; no email-as-authority path |
| IA-12 | PASS guard | Client verified flag rejected 422 |
| IA-13 | PASS guard | DRAFT source cannot become approved/ready |
| IA-14 | PARTIAL / BLOCKED_CONTRACT | Pure synthetic applicability facts only; actual approved source absent |
| IA-15 | PASS guard | Actual unavailable-source attempt, CheckedAt absent |
| IA-16 | PARTIAL | Expired claim blocked by API assessment; actual authority source still absent |
| IA-17 | PARTIAL | Descriptor revocation/version invalidation and revoked predicate; actual legal-authority revocation BLOCKED |
| IA-18 | PARTIAL | Future-effective claim invalid; no positive actual source |
| IA-19 | BLOCKED_CONTRACT | Real product/business authority scope requires admitted source; no invented scope facts |
| IA-20 | PASS claim rule | Amount limit exceeded → blocker; not actual authority validation |
| IA-21 | PASS claim rule | Currency without matching policy → blocker, no FX |
| IA-22 | PARTIAL | Stale >24h pure predicate denied; no actual successful source check exists |
| IA-23 | PARTIAL | Fresh predicate only; no positive SOURCE_CHECKED claim |
| IA-24 | PASS | Missing file capability denied 403 |
| IA-25 | PASS | FLS denied 403; OWN scope denied 404 |
| IA-26 | PASS | Forged APPROVED source request rejected 422 |
| IA-27 | PASS | Forged checker/time/outcome rejected 422 |
| IA-28 | PASS | Stale identity/source If-Match 412 |
| IA-29 | PASS | Same-key replay no duplicate; permission rechecked |
| IA-30 | PASS | Two source writers → one 200, one 412, two immutable versions |
| IA-31 | PASS | Real flush/rollback: version/receipt/outbox counts unchanged |
| IA-32 | PASS | Immutable save guards all overloads + SQL history retained |
| IA-33 | PASS | Backend restart/read persistence; browser refresh/logout/login |
| IA-34 | PASS bounded regression | Real parser/scanner CLEAN/UNSAFE/re-scan/restart + existing tests; not every old B6.2 browser scenario rerun |
| IA-35 | PASS | All original proof/contents row hashes unchanged |
| IA-36 | PASS | All original B5 document/content rows unchanged, final SHA integrity 0 failures |
| IA-37 | PASS | Original 65 UNVERIFIED decisions unchanged |
| IA-38 | PASS | No VERIFIED/positive source/ready emitted |
| IA-39 | PASS | SQL ACCEPTED count 0 |
| IA-40 | PASS | SQL WON count 0 |
| IA-41 | PASS | 5.514 original rows across 35 tables unchanged |

## Q. B6.4 Readiness

Foundation đã có: protected Case/Proof references, owner affiliation lease, versioned candidate/source descriptors, immutable attempts/assessments, authorization và UI. **Chưa đủ điều kiện mở B6.4**: cần actual approved identity/authority issuer + verification method, origin/provenance/coverage, effective dates/revocation, actual successful source result/freshness và source-specific policy. Không có synthetic positive verification. Không mở B6.4/B6.5/Gate C trong task này.

## R. Final Verdict

| Phạm vi | Verdict |
|---|---|
| Binding/affiliation/ledger/negative assessments/source descriptor UI | PARTIAL_FUNCTIONAL_PASS trong gói A |
| API/SQL/security/concurrency/rollback/Connected binding persistence | PASS theo evidence đã ghi |
| Positive SOURCE_CHECKED và READY_FOR_INDEPENDENT_REVIEW | BLOCKED_CONTRACT |
| VERIFIED / ACCEPTED / WON / commercial receipt | Không được triển khai hoặc phát sinh |
| Full repository release/freeze | Chưa PASS: baseline debt + actual-source dependency; không claim trusted freeze |

## S. Git Status

Giữ nguyên HEAD/branch và prior local changes. Backend thay đổi hẹp tại Quotes, CRM Contact owner contract/reader, AccessControl assignable capabilities, migration, verification scripts và policy/report. Frontend thêm application model/port/public boundary/adapter/hook/panel, nối connected runtime, cập nhật capability vocabulary/guidance/OpenAPI/generated artifacts/inventory và browser tests/config. Root design authority/manifests nhận đúng 11 operations đã duyệt.

Danh sách file so với entry và hashes: [change manifest](../../../task-runtime/quote-identity-authority-b6-3/source-change-manifest.json). Backend/frontend git status snapshots nằm cùng thư mục. Không xóa/rollback local changes, không reset/reseed, không commit/push/merge. Test fixtures chỉ ở Development, được giữ để review; credentials vẫn process-only.
