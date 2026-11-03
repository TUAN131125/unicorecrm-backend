# Gate B6.2 — Trusted Scanner & PDF Structural Validation

Ngày kiểm chứng: 26/10/2026. **Development Functional PASS** trong phạm vi policy PDF đã duyệt. Full quality vẫn có các blocker baseline được phân biệt bên dưới. Không mở B6.3/B6.4/B6.5 hoặc Gate C.

## A. Git/SQL Baseline

- Backend: `master`, HEAD `13cd39eba01ebc9dc238f8b0ef98e174c1d4d3f6`; frontend: `feat/deal-compact-kanban-card`, HEAD `814cc4685b6d8f8ead2fc945f4607a2d0168320d`. HEAD không thay đổi; không commit/push/merge/reset.
- Đã chụp trạng thái dirty và hash file trước khi sửa, không dùng HEAD sạch để thay thế local changes. Bốn Backend architecture failures và ba TypeScript diagnostics đã tồn tại tại entry.
- SQL được khóa kiểm tra vào đúng máy local `DESKTOP-ICH225R`, database `UnicoreCRM_Development`.
- Baseline: 270 Quote, 195 Deal, 33 issued documents/contents, 4 acceptance cases, 13 proofs/contents, 69 Workspace. Tổng 5.075 dòng được bảo vệ trong 32 bảng.
- [Baseline và source delta](../../../task-runtime/quote-proof-scanner/task-file-changes.json), [SQL entry](../../../task-runtime/quote-proof-scanner/sql-entry-review.json), [so sánh Backend baseline](../../../task-runtime/quote-proof-scanner/backend-baseline-comparison-sealed.json).

## B. Scanner Environment

- Windows, .NET 10; runtime portable riêng tại `task-runtime/quote-proof-scanner/runtime`. Không cài service, thay PATH toàn hệ thống, khởi động Docker Desktop hoặc yêu cầu quyền administrator.
- ClamAV **1.5.4**, qpdf **12.4.2**; official signed daily DB **28165**, main **63**, bytecode **339**. Daily signature timestamp: `2026-10-26T06:27:07Z`.
- ClamAV ZIP SHA-256: `0D9E0228B2674137EA1A2853566C98A0278AD52AB2582C3D6DBD75373848C395`; qpdf ZIP: `DB87077E683630C1217E0E8F9A20A9749D952AB676E881C3689187763A5DE25D`.
- Đã đối chiếu official release digests, GPG signature của ClamAV và `sigtool --info`/`Verification OK.` cho CVD thực. ClamAV GPLv2 và qpdf Apache-2.0 chạy thành process riêng; không đưa binary vendor vào source repository.
- Daemon task-owned chỉ nghe `127.0.0.1:3317`. Đã kiểm tra PID, executable path, start time, PID sở hữu listener, config, native files và signature files bằng pinned manifest. Restart trong fault test tạo runtime pin mới; không tái dùng pin cũ để release file.
- Có probe `VERSION` thực, deadline hai giây; listener đang treo không được coi là scanner available. Engine/signature version phải khớp provenance đã pin.
- [Distribution integrity](../../../task-runtime/quote-proof-scanner/distribution-integrity.json), [GPG](../../../task-runtime/quote-proof-scanner/clamav-signature-posix.log), [manifest hiện hành](../../../task-runtime/quote-proof-scanner/runtime/trusted-runtime-manifest.json), [signature DB](../../../task-runtime/quote-proof-scanner/signature-db-info.log).
- Nguồn: [ClamAV releases](https://www.clamav.net/downloads), [Clamd protocol](https://docs.clamav.net/manual/Usage/ClamdProtocol.html), [signature management](https://docs.clamav.net/manual/Usage/SignatureManagement.html), [qpdf release](https://github.com/qpdf/qpdf/releases/tag/v12.4.2).

## C. Architecture

B6.1 giữ nguyên immutable encrypted proof bytes và metadata tiếp nhận. B6.2 bổ sung owner-local job/lease, append-only attempt/result, structural validator, scanner thật và effective-release assessment.

`QUARANTINED → claim SQL lease → decrypt/verify stored SHA → isolated qpdf validation → real ClamAV INSTREAM → atomic result/state/audit/outbox → effective CLEAN or blocked`

- Background worker off by default; chỉ xử lý Workspace có policy Backend enabled/versioned hợp lệ. Không scan tự động toàn bộ Workspace/dữ liệu lịch sử.
- Không giữ SQL transaction trong lúc parse hoặc scan. Mỗi claim/result dùng owner transaction, application lock theo Workspace/Proof, lease identity và EF optimistic concurrency.
- `QuoteProofScanCoordinator`, `QuoteProofEffectiveScan`, `QuoteProofScanReads` thuộc Quotes Application; parser/scanner/process launcher và encrypted-byte access thuộc Quotes Infrastructure. Không đọc trực tiếp bảng owner khác.
- Backend configuration: `QuoteProofScan:Enabled`, `RuntimeManifestPath`, `RuntimeManifestSha256`, `Workspaces:<WorkspaceId>:Enabled/Version`. Các policy limits/backoff được validate và đưa vào digest. Thiếu/invalid policy hoặc runtime authority đều fail-closed.
- Thứ tự triển khai: additive migration → provision/verify trusted runtime → pin manifest/configure đúng Workspace → restart host → scan/rescan. Khi thay runtime/signatures cần controlled daemon restart, pin mới và host restart; historical CLEAN không tự được gia hạn.

## D. PDF Validation

- qpdf strict syntax check; warning/repair cũng bị chặn. Worker đọc complete qpdf v2 graph, toàn bộ objects/references và decoded page content operators.
- Mặc định versioned: file ≤10 MiB, ≤100 trang, ≤10.000 objects, nesting ≤64, decoded streams ≤32 MiB, JSON/output envelope ≤64 MiB. Unsupported filters/content operators fail-closed.
- Từ chối encrypted PDF, actions/JavaScript, forms/XFA, attachments/external references, inline images, Form/Image/Pattern/Shading execution và XML metadata trong subset hiện tại. Không tuyên bố hỗ trợ toàn bộ PDF standard.
- Native validator: memory/process limits bằng Windows Job Object; qpdf deadline 15 giây. Managed validation chạy trong helper riêng, heap limit 256 MiB, outer deadline 20 giây, tối đa hai validation slots/process; không parse JSON/base64 không giới hạn trong API process.
- Matrix thật: valid static PDF PASS; non-PDF/corrupt/active/inline-image/XML/encrypted bị chặn; valid 101-page fixture trả `PDF_PAGE_LIMIT`; >10.000 objects bị chặn; compressed decoded-resource fixture bị `PDF_PROCESS_OUTPUT_LIMIT`. Engine cũng trả resource-limit verdict, không được nâng thành CLEAN.
- [Real parser matrix](../../../task-runtime/quote-proof-scanner/real-parser-matrix-sealed.json). Đây là component runtime evidence; không gán các CLI synthetic IDs thành SQL/Connected proof evidence.

## E. Real Malware Scanner

- Adapter dùng actual `INSTREAM`, SHA/length của stream thực, complete bounded terminal response, engine/signature checks trước/sau và actual signed signature freshness. Chỉ exact successful response mới cho malware CLEAN; error, partial/truncated response, exceeded limits hoặc stale authority không CLEAN.
- Signature freshness tối đa 24 giờ tại scan và tại release; policy Workspace có thể chặt hơn. Không chấp nhận future signature timestamp.
- Scan deadline 30 giây; outer work deadline 45 giây. Daemon: two scan threads, bounded queue, 10 MiB stream/file và 64 MiB scan expansion limits. Runtime reader còn kiểm tra working set; đây không phải tuyên bố daemon đã có hardened OS sandbox/hard memory quota.
- Official 68-byte EICAR được lấy vào memory, kiểm tra SHA-256, không chạy. PDF detection fixture chứa nguyên bytes này trong một decoded stream: parser PASS, ClamAV thật `UNSAFE`, SQL result `CLAMAV_THREAT_DETECTED`, content bị chặn.
- PDF detection SHA-256: `0527E10DFDB4BEDE05FC29604129EB7290C29A35C1B0C52942AC1C40D6F26A71`.
- Một fixture thử ban đầu chỉ đặt EICAR trong comment không trigger signature của engine; giữ nguyên kết quả quan sát đó và sửa fixture kiểm thử thành embedded stream đúng định dạng. Không thêm custom signature hoặc fake verdict để đạt PASS.
- [Real PDF detection](../../../task-runtime/quote-proof-scanner/real-pdf-av-detection.json), [bound API/SQL security evidence](../../../task-runtime/quote-proof-scanner/security-e2f89cb1c3ea4918ad27c73ccd12e800/security-sql.json), [EICAR](https://www.eicar.org/download-anti-malware-testfile/).

## F. Scan Domain

- Operational states: `SCAN_PENDING`, `SCANNING`, `CLEAN`, `UNSAFE`, `INVALID_PDF`, `SCAN_FAILED`, `NEEDS_INTERVENTION`.
- Attempt binds Workspace/Case/Proof, exact SHA/length, lease ID, epoch, attempt number, policy version/digest và runtime digest. Result lưu terminal outcome, structural/malware statuses, reason, time và sanitized provenance.
- B6.1 `StorageStatus=QUARANTINED`/initial metadata không bị rewrite. Effective status/reviewability được overlay từ ledger hiện hành. CLEAN là kết quả kỹ thuật; customer response vẫn **Chưa xác minh**.
- Effective CLEAN kiểm tra immutable proof binding, current policy/epoch, parser hash/version, live trusted engine/signature provenance và current freshness. Decrypt/download tính SHA lần nữa.
- Policy/runtime/signature pin thay đổi khiến old CLEAN không release được cho đến khi rescan thành công. History cũ vẫn đọc được và không bị sửa.

## G. SQL Persistence

- Applied additive migration `20261026082618_AddAcceptanceProofScanLedger`.
- Ba bảng: `quotes.AcceptanceProofScanJobs`, `AcceptanceProofScanAttempts`, `AcceptanceProofScanResults`; scoped keys/FKs, unique attempt number, result-per-attempt uniqueness, state/bounds/JSON/CLEAN check constraints và queue indexes.
- Attempts/results immutable qua mọi SaveChanges overload; job binding không được đổi/delete. Không có public ScanResult write/reset endpoint.
- Claim/result/state/system audit/outbox commit cùng transaction. Audit/outbox mới resolve owning Quote từ Case contract trong cùng owner; không gán ProofId thành QuoteId. Worker không cập nhật Quote/Deal lifecycle.
- SQL rollback thật sau result flush: job còn SCANNING, zero result, content denied. Sau lease recovery có failed-expired record và scan mới thực sự thành công.
- [Migration apply](../../../task-runtime/quote-proof-scanner/migration-apply.log), [rollback/restart SQL](../../../task-runtime/quote-proof-scanner/advanced-c91dabd8ca9e4ddd8e5c8067793d7bac/recovery-sql.json), [scan ledger metrics](../../../task-runtime/quote-proof-scanner/scan-sql-evidence.json).

## H. Security

- New capability `quotes.readAcceptanceProofContent` chỉ assignable qua custom role chính thức; không thêm vào default owner/member grants. Quyền của Workspace demo không đổi.
- History: `quotes.read` + `quotes.readAcceptanceEvidence`, record scope/FLS, Workspace/Case/Quote binding và B5 stored-party authority; không cần CLEAN.
- Content: thêm dedicated content capability và effective CLEAN. Metadata permission không thay thế quyền bytes. Missing capability, cross-Workspace và hidden `grandTotal` FLS đều bị từ chối qua API thật.
- Content trả attachment, `private, no-store`, `nosniff`, sandbox CSP/frame denial; không range/inline preview hoặc public storage URL. Download slots giữ đến khi HTTP result gửi xong, rồi xóa plaintext buffer/release lease.
- Public history không trả bytes, storage references, raw engine output hoặc private paths. Date wire dùng UTC `Z`.
- Malformed scanner options đã kiểm chứng: host/ordinary Quote reads/history vẫn hoạt động, content fail-closed; sửa config/restart thì protected download phục hồi.
- Uppercase path aliases dùng stored canonical IDs để decrypt; đã kiểm chứng download200 và đúng hash.
- Hai narrow GET được admit/generate: `getQuoteAcceptanceProofScanHistory`, `downloadQuoteAcceptanceProofContent`. Frontend OpenAPI: **346 operations / 320 ready / 26 blocked**; không mở positive acceptance operation.

## I. Recovery

- Default lease90 giây; work45 giây; tối đa3 automatic attempts/epoch và12/proof. Default backoff30 giây rồi120 giây; acceptance dùng backoff ngắn hơn trong policy synthetic riêng.
- Expired lease ghi append-only `SCAN_LEASE_EXPIRED`; stale lease/token/epoch không thể commit. Hai API hosts/worker cùng chạy cho một proof đã tạo đúng một attempt/result hợp lệ.
- Exhaustion sticky `NEEDS_INTERVENTION`, reason `EPOCH_RETRY_EXHAUSTED`/`LIFETIME_RETRY_EXHAUSTED`. Policy edit không reset counters. Không public override; cần proof mới, vẫn chịu B6.1 duplicate/quota/authorization controls.
- Backend restart giữa scan và SQL rollback recovery đều PASS bằng API/SQL thật.
- Fault test chỉ tạm suspend/stop PID ClamAV task-owned sau khi đối chiếu canonical path/start time. Hung/offline scanner fail-closed; restart runtime và submit proof mới phục hồi. Không tắt Windows security hoặc sửa scanner result.
- [Fault evidence](../../../task-runtime/quote-proof-scanner/fault-26a74becd18a4b45ab67bf7f67ff3c7a/result.json), [new-proof recovery](../../../task-runtime/quote-proof-scanner/fault-af37e76577954d5ba0dd356e2107694c/result.json).

## J. Frontend

- Own application port/models → generated client HTTP adapter → lazy proof section/card. Không đưa generated DTO vào presentation/domain, không fetch URL tự dựng.
- Hiển thị scan status, structural/malware statuses, history, policy/time, retry exhaustion và quyền tải file thực từ Backend. CLEAN không hiện badge Accepted/Verified.
- Polling bounded/cancellable; refresh, workspace retirement và unmount abort requests. Error dùng stable application error presentation.
- Attachment download đối chiếu size/SHA trước khi lưu, không auto-render iframe/object/embed.
- Bootstrap schema được mở đúng exception cho capability mới; không nới pattern chung. Guidance song ngữ/version và generated inventory được cập nhật.

## K. API/SQL Acceptance

Synthetic Workspace chính: `unicore-proof-scan-7d060adb` (`ws_2d82dd62061f4b4bb6403f6be6ef8c3f`) cùng companion isolation Workspace. Tái sử dụng ba synthetic identities đã tồn tại; official Development owner provisioning, CRM demo seeder disabled, không sửa permissions bằng SQL.

Các flow PASS: quarantine→real CLEAN→hash-matched content; metadata-only denial; active-content invalid/history-readable; idempotent upload replay; policy rescan; actual signature freshness denial/exhaustion; sticky recovery; two workers; real PDF/EICAR UNSAFE/history/content denial; cross-Workspace; FLS; restart/rollback; malformed config. B6.1 regression API/SQL cũng PASS, gồm B1 assessment, B5 issuance, B2 sending, unverified response và acceptance/Won guard.

| Case | Evidence scope | Kết quả |
|---|---|---|
| SC-01 Valid PDF + real scan | API/SQL + browser upload/poll/download | PASS CLEAN |
| SC-02 Non-PDF | actual validator + B6.1 ingress regression | PASS blocked |
| SC-03 Corrupt PDF | actual validator | PASS |
| SC-04 Unsupported content | actual inline-image/XML/operator validation | PASS |
| SC-05 Active content | API/SQL + actual parser | PASS INVALID_PDF |
| SC-06 Size limit | B6.1 API regression, unchanged quota | PASS |
| SC-07 Page/resource limits | actual101-page/object/decoded-output fixtures | PASS |
| SC-08 Offline scanner | actual task-owned daemon stopped, API/SQL | PASS fail-closed |
| SC-09 Timeout | actual daemon suspended; bounded live health timeout | PASS fail-closed; not a fabricated scanner response |
| SC-10 Stale signatures | current real DB age vs versioned1-hour test policy | PASS no CLEAN |
| SC-11 Engine error/limits | real scanner resource-limit + protocol failure unit cases | PASS component; no forced malware-engine crash |
| SC-12 Safe detection fixture | real embedded EICAR PDF, API/SQL | PASS UNSAFE |
| SC-13 Forged client CLEAN | B6.1 ingress API regression | PASS422 |
| SC-14 Forged ScanResult | POST scans405 + immutable persistence guards | PASS |
| SC-15 SHA/binding mismatch | release invariant tests + real downloaded-byte comparison | PASS; no production SQL corruption injected |
| SC-16 Concurrent scan | two real hosts/SQL lease | PASS one decision |
| SC-17 Expired lease | API/SQL recovery | PASS |
| SC-18 Restart mid-scan | actual host termination/restart | PASS |
| SC-19 Duplicate requests | upload replay, competing workers, CAS tests | PASS |
| SC-20 SQL rollback | real result-flush rollback | PASS no false CLEAN |
| SC-21 Wrong Workspace | actual API | PASS denied |
| SC-22 Restricted user | API + Connected browser | PASS |
| SC-23 FLS | official role with hidden commercial total, actual API | PASS |
| SC-24 Quarantined download | actual API | PASS denied |
| SC-25 Authorized CLEAN download | API + actual browser bytes/SHA | PASS |
| SC-26 Unsafe download | real UNSAFE proof API | PASS denied |
| SC-27 Historical results | append-only SQL/history, EF save/delete guards | PASS |
| SC-28 Policy/signature re-evaluation | actual policy change, daemon restart/new pin, SQL | PASS |
| SC-29 No implicit VERIFIED | no verification mutation/authority added | PASS |
| SC-30 No implicit ACCEPTED | global SQL count0 | PASS |
| SC-31 No implicit WON | global SQL count0 | PASS |
| SC-32 B6.1 preservation | original encrypted rows/hash + regression API | PASS |
| SC-33 B5 preservation | original33 document/byte row hashes + SQL integrity | PASS |
| SC-34 CRM preservation | original32-table/5.075-row hashes | PASS |

Runtime component/unit scope được ghi rõ; không gọi CLI evidence là Connected SQL acceptance. [API evidence](../../../task-runtime/quote-proof-scanner/api-4b5ed3b1700a4cfabc6f915fbd68189a/result.json), [security](../../../task-runtime/quote-proof-scanner/security-e2f89cb1c3ea4918ad27c73ccd12e800/result.json), [B6.1 regression](../../../task-runtime/quote-proof-storage/api-20be6e23aefa434ba68f0ad8ef75aba3/result.json).

## L. Connected Browser

Dedicated Playwright Chromium, actual API5098/SQL/worker/scanner, không mock hoặc dùng browser cá nhân.

Final run **3/3 PASS**, zero skipped:

1. CLEAN history và attachment SHA match qua refresh/logout/login.
2. Metadata-only actor đọc unsafe/failed history, không có quyền download.
3. UI upload → persisted SCAN_PENDING → bounded polling đến real CLEAN → attachment download/hash match; customer state vẫn Chưa xác minh.

[Final browser log](../../../task-runtime/quote-proof-scanner/browser-full-flow.log), [browser upload response](../../../task-runtime/quote-proof-scanner/read-d4e0400067004ba1889ab8a4c5db9278/browser/browser-scan-upload.json), [CLEAN screenshot](../../../task-runtime/quote-proof-scanner/read-d4e0400067004ba1889ab8a4c5db9278/browser/browser-upload-clean.png). Screenshot đã được xem để kiểm tra trạng thái/nhãn; không có inline PDF viewer.

## M. Data Preservation

- **5.075/5.075 original rows, 32/32 tables unchanged**, không mất dòng; gồm toàn bộ CRM demo gốc, Dynamic Pipeline/Deal rows, old Quote commands/history, original33 B5 PDFs và original13 B6.1 encrypted proofs.
- Dữ liệu thêm chỉ là fixtures Development rõ nhãn synthetic: hai Workspace cho B6.2 và hai Workspace cho B6.1 regression, reused accounts/memberships/custom roles, Contacts/Quotes/documents/proofs và scan ledger được tạo bằng owner commands/provisioning.
- Delivery SQL chốt cuối: **275 Quote, 195 Deal, 38 issued documents, 28 proofs, 73 Workspace**. Tất cả phần tăng là fixtures đã mô tả; không sửa số baseline để khớp fixture.
- ACCEPTED/WON/Orders/PaymentPlans đều0. Không xóa hoặc sửa historical scan/document/proof records. Không reset/reseed.
- [Preservation](../../../task-runtime/quote-proof-scanner/preservation-delivery.json), [delivery SQL](../../../task-runtime/quote-proof-scanner/sql-delivery-review.json), [protected entry](../../../task-runtime/quote-proof-scanner/protected-entry-row-hashes.json).

## N. Performance

- SQL timing của các successful attempts, parser/scanner durations và provenance bytes được đo trong [scan metrics](../../../task-runtime/quote-proof-scanner/scan-sql-evidence.json). Các mẫu610–745 byte không phải load test production.
- Successful wall time quan sát khoảng4,9–11,7 giây; có intentional6-second rescan pause trong mẫu. Parser khoảng0,14 giây, scanner adapter khoảng1,9 giây, phần còn lại gồm pinned-runtime verification/SQL.
- Chốt cuối:33 attempts/results, zero orphan result, 21.946 bytes provenance text; không dùng con số này làm tổng dung lượng DB. Signature/native runtime nằm ngoài SQL; PDF bytes tiếp tục chịu quota B6.1.
- ClamAV startup peak working set đo khoảng0,94 GiB. Native/managed parser jobs có enforced process/heap/output/time limits. Daemon memory chỉ được giới hạn gián tiếp bằng scan bounds và runtime working-set guard; dedicated low-privilege deployment/hard quota cần kiểm chứng riêng trước production.
- Không tuyên bố throughput, tải nhiều Workspace hoặc10.000 concurrent users đã được kiểm chứng.

## O. Quality

- Backend build: PASS, zero warnings/errors. Focused scan/release/persistence tests **45 PASS**. Full Backend **339 PASS / 4 FAIL**, cùng failure names và exact diagnostics baseline, zero new failures.
- Frontend typecheck: đúng **3 baseline diagnostics**, không thêm lỗi. `npm run verify` đã chạy, dừng tại chính ba lỗi này; không tuyên bố328 gates đều PASS.
- PASS: API contract346/320/26, HTTP/binary foundation, strict core/regions, API management boundaries, presentation responsibility, guidance, frontend/backend separation, focused Quote boundary/direct-sale/order-integrity/presentation/approval-delivery gates, inventory/repo check, security scan và production build/bundle budget.
- Bundle budget: entry295,57 KiB; largest chunk498,04 KiB <500 KiB. Security scan: zero unmanaged high-confidence findings.
- `quality.backend-readiness` còn fail document-status inventory đã có ở entry; source/status file và Markdown file set không bị sửa để né gate. `quality.quote-core-provider-contract` BLOCKED do formal external provider/network dependency chưa được cấu hình, dù dedicated local Connected tests phía trên đã chạy thật.
- API gate count assertions chỉ tăng đúng hai approved read operations; không giảm blocked count hoặc nới gate/rule để đạt PASS. B6.1 content-denial regression cập nhật404→403 vì public protected route mới, quyền bytes vẫn denied.
- Independent read-only review cuối: không tìm thấy Critical/High mới trong binding, lease/CAS, transaction, release, canonical IDs, live health hoặc authorization. [Baseline debt source evidence](../../../task-runtime/quote-proof-scanner/baseline-debt-source-evidence.json), [full Backend](../../../task-runtime/quote-proof-scanner/backend-sealed.log), [full frontend attempt](../../../task-runtime/quote-proof-scanner/frontend-verify.log).

## P. Remaining Blockers / Risks

- Identity/signer authority source, independent documentary review và positive verified acceptance chưa triển khai/không được mở. CLEAN không giải quyết các dependency này; MarkWon guard vẫn fail-closed.
- Scanner implementation hiện được kiểm chứng trên Windows portable runtime. Dedicated low-privilege/OS isolation, hard daemon resource quota, production operations/monitoring và backup/DR paired restore chưa được chứng minh.
- Technical indefinite retention không phải legal retention policy. Không delete/restore hay manual CLEAN override.
- Conservative policy chủ động chặn nhiều PDF hợp lệ có image/form/metadata/unsupported content. Một synthetic B5 PDF trước đó tạo qpdf warnings khi thử làm customer proof; B6.2 giữ strict rejection và không sửa lại B5 bytes/renderer trong scope này.
- Cần controlled signature update/restart/pin/rescan vận hành trước khi signature quá24 giờ. Scanner mặc định off; chưa tự bật cho Workspace nghiệp vụ.
- Baseline architecture/TypeScript/documentation gates và formal external-provider acceptance còn như phần O. Đây không phải production/full-quality PASS.

## Q. Final Verdict

**B6.2 Development Functional PASS** cho approved PDF subset, actual scanner/API/SQL/browser và protected attachment access. Data preservation PASS; no false release khi pending/unsafe/stale policy/signature, permission/FLS/Workspace denial hoặc SQL rollback.

**Verified Customer Acceptance / ACCEPTED / Deal WON: BLOCKED_CONTRACT và không phát sinh.** Không mở B6.3/B6.4/B6.5, Gate C, Order hoặc Payment workflows. Không tuyên bố PDF cryptographic signature verification, provider delivery hay hoàn thiện retention pháp lý.

## R. Git Status / File Changes

HEAD hai repository giữ nguyên. Không commit/push/merge. Local changes trước task được giữ; không khôi phục file đã bị xóa từ trước.

- Backend: scan domain/policy/ports/coordinator/effective reads; ClamAV/qpdf/managed worker/process/runtime authority; three-table model/store/additive migration; separate capability/DI/endpoints/metadata; focused tests và guarded Development verification scripts dùng shared harness.
- Frontend: own proof models/port, generated-client adapter, scan card/hook/bounded polling, capability/bootstrap contract, bilingual guidance, generated API/inventory và Connected browser tests.
- Authority: đúng hai operation admission rows/indexes, canonical extension/registry và approved policy decisions. Historical checkpoint reports không bị viết lại để nhận công của B6.2.
- [Full task-relative changed-file list](../../../task-runtime/quote-proof-scanner/task-file-changes.json), [Backend status](../../../task-runtime/quote-proof-scanner/backend-final-status.txt), [frontend status](../../../task-runtime/quote-proof-scanner/frontend-final-status.txt).
