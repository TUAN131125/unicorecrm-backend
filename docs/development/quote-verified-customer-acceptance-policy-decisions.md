# Gate B6 — Verified Customer Acceptance: policy decisions

2026-10-25. **OPTION B APPROVED WITH USER AMENDMENTS — IMPLEMENTATION IN_PROGRESS.** User approved D1–D5 with mandatory amendments in the current conversation. Positive verification still requires approved actual identity/authority sources and trusted scanner/storage; none may be invented. B1–B5 vẫn giữ nguyên. Chưa có VERIFIED/ACCEPTED, schema migration, capability grant hay customer workflow mới trong B6.

## AnalysisRecord — source and SQL facts

Backend `master` HEAD `13cd39eba01ebc9dc238f8b0ef98e174c1d4d3f6`; Frontend `feat/deal-compact-kanban-card` HEAD `814cc4685b6d8f8ead2fc945f4607a2d0168320d`. Cả hai dirty; fresh inventory hash2845 Backend/2695 Frontend files, gồm toàn bộ local B5 implementation. Không dùng GitHub để suy ra local state. [entry-captured.json](D:/Project_All/UnicoreCRM/task-runtime/quote-verified-acceptance-b6/entry-captured.json), [backend-entry-status.txt](D:/Project_All/UnicoreCRM/task-runtime/quote-verified-acceptance-b6/backend-entry-status.txt), [frontend-entry-status.txt](D:/Project_All/UnicoreCRM/task-runtime/quote-verified-acceptance-b6/frontend-entry-status.txt).

SQL xác minh local `DESKTOP-ICH225R/UnicoreCRM_Development`: **44 Workspaces,251 Quotes,193 Deals,21 Documents/21 contents,55 customer decisions** (41reported acceptance/14rejection, tất cả UNVERIFIED), **0 ACCEPTED/0WON/0Orders/0PaymentPlans**. 21PDF tổng1,091,927bytes,0hash/size mismatch. 8IAM accounts đã verified email; số này không chứng minh customer identity/Buyer/signing authority. Đã chụp29bảng gốc, gồm B5 metadata và content row hashes, không copy credentials/PDF bytes. [sql-entry-review.json](D:/Project_All/UnicoreCRM/task-runtime/quote-verified-acceptance-b6/sql-entry-review.json), [protected-entry-row-hashes.json](D:/Project_All/UnicoreCRM/task-runtime/quote-verified-acceptance-b6/protected-entry-row-hashes.json).

Full Backend fresh baseline **276PASS/4FAIL/280**, khớp B5; strict Frontend **3/3PASS**. Đây là baseline review, không phải B6 positive verification acceptance. [backend-entry-tests.log](D:/Project_All/UnicoreCRM/task-runtime/quote-verified-acceptance-b6/backend-entry-tests.log), [frontend-entry-strict.log](D:/Project_All/UnicoreCRM/task-runtime/quote-verified-acceptance-b6/frontend-entry-strict.log).

### B6 Capability Gap Matrix

| Năng lực | Hiện có | Thiếu | Owner |
|---|---|---|---|
| Customer identity | IdentityAuth active account, EmailVerifiedAt, revocable sessions; CRM Buyer identity/contact records | No authenticated customer→Buyer mapping or documentary identity-verification case. Email control is insufficient. | IdentityAuth + CRM buyer participants + acceptance verification boundary |
| Signer authority | CRM organization affiliation/role/primary flag/effective dates | No verified delegation/provenance/amount-currency scope/revocation contract. Ordinary CRM role is not authority. | Quote-specific verification boundary; CRM owns affiliation facts |
| Customer consent | B3 authenticated employee reporting | No approved customer-origin consent mechanism; B3 must remain UNVERIFIED. | Quotes acceptance |
| Issued PDF binding | B5 DocumentId/root/revision/source-result RV/commercial fingerprint/immutable bytes/SHA and protected reads | Acceptance-specific owner binding read/lease and approved stale-policy/document rules. | Quotes Documents |
| Evidence storage | B5 SQL immutable bytes/metadata and B3 append-only event patterns | No customer-upload artifact/quarantine/type/malware/provenance/retention contract. B5 issued-PDF tables cannot be generic upload storage. | Narrow Quote acceptance evidence owner, scanner adapter |
| Evidence verification | Staff actor authorization/record scope/FLS/idempotency/SQL transaction infrastructure | No admitted method, policy, verification permission, independent verifier workflow or trust attestation. | Quotes acceptance verification + AccessControl |
| Quote ACCEPTED transition | Strict empty-object accept-and-close-deal endpoint, negative guard | Direct-sale typed verified-evidence acceptance command not admitted; linked atomic acceptance is Gate C. | Quotes; Workflows/Deals only in Gate C |
| Deal commitment verifier | MarkWon validates shape/auth/version then fails closed | No source-owner accepted commitment verifier; B6 evidence alone must not unblock Won. | Quotes/Deals, future Gate C |

### Evidence and trust conclusions

- [AuthenticatedIdentityReference.cs](D:/Project_All/UnicoreCRM/backend/src/UnicoreCRM.Platform/IdentityAuth/Contracts/AuthenticatedIdentityReference.cs): `IAuthenticatedIdentityReferenceLookup` trả active AccountId/MemberId, không customer realm, BuyerRef hoặc verified signing authority. `EmailVerifiedAt` là account email verification; nullable MFA DTO không chứng minh đã có customer MFA mechanism.
- [Contact.cs](D:/Project_All/UnicoreCRM/backend/src/UnicoreCRM.Crm/Contacts/Domain/Contact.cs): Contact–Organization role/primary/effective dates là CRM affiliation. Customer stakeholder/primary contact cũng không phải reviewed delegation.
- [QuoteDocumentPartyReader.cs](D:/Project_All/UnicoreCRM/backend/src/UnicoreCRM.Sales/Quotes/Contracts/QuoteDocumentPartyReader.cs): B5 capture chứng minh source record được phép đọc và eligible, không authentication/consent/representation.
- [QuoteCustomerDecisionRecord.cs](D:/Project_All/UnicoreCRM/backend/src/UnicoreCRM.Sales/Quotes/Domain/QuoteCustomerDecisionRecord.cs): B3 hard-coded UNVERIFIED, append-only. Không đổi constraint hoặc promote row cũ.
- [QuoteDocument.cs](D:/Project_All/UnicoreCRM/backend/src/UnicoreCRM.Sales/Quotes/Domain/QuoteDocument.cs): B5 identity/hash/immutable bytes có thể tái sử dụng. File hash chứng minh byte integrity trong storage, không customer signature/assent.
- [OriginalEvidenceContracts.cs](D:/Project_All/UnicoreCRM/backend/src/UnicoreCRM.CommercialEvidence/CommercialEvidence/Contracts/OriginalEvidenceContracts.cs): evidence hiện là completed-purchase, không acceptance. Generic signed lead webhook chỉ xác minh transport, không provider consent.
- [QuotesEndpoints.cs](D:/Project_All/UnicoreCRM/backend/src/UnicoreCRM.Sales/Quotes/Contracts/QuotesEndpoints.cs): `acceptQuoteAndCloseDeal` chỉ nhận `{}`, từ chối extra verification fields; [QuoteAcceptanceGuard.cs](D:/Project_All/UnicoreCRM/backend/src/UnicoreCRM.Sales/Quotes/Application/Common/QuoteAcceptanceGuard.cs) luôn fail-closed sau auth/record/FLS/version/state. Generated method và PRODUCTION_CONTRACT_READY admission không chứng minh runtime positive workflow đã tồn tại.
- [Handler.cs](D:/Project_All/UnicoreCRM/backend/src/UnicoreCRM.Crm/Deals/Application/MarkDealWon/Handler.cs): không mutate/replay success từ evidence ID do client khai. Giữ nguyên guard.
- Không tìm thấy implemented upload/multipart, quarantine, malware scanner, customer consent provider, signer-authority contract trong Backend src tại review này. Đây là kết luận bounded source inspection, không suy ra một hạ tầng bên ngoài đang hoạt động.

## So sánh ba phương án

| Phương án | Cơ chế / mức tin cậy | Phần tái sử dụng | Chi phí/phụ thuộc | Rủi ro / Development |
|---|---|---|---|---|
| A — Verified Customer Portal | Customer-authenticated explicit consent to exact Document; identity và delegation vẫn phải xác minh riêng | IAM session lifecycle + B5 | Customer principal→Buyer grants, separate customer authorization surface, challenge/expiry/replay, revocation, portal UI; không cấp CRM/Studio membership | Link possession/email/login không đủ authority; cần test customer actor thật theo cơ chế chính thức; phạm vi lớn hơn |
| **B — Verified Documentary Evidence (đề xuất)** | Independent human review of retained customer-origin identity, representation and explicit assent; assurance HUMAN_DOCUMENT_REVIEW_ONLY | B3 append-only pattern, B5 immutable SQL/hash, AccessControl, existing CRM buyer visibility | Separate private evidence entities, bounded upload/quarantine/inspection/clean-scanner contract, reviewer UI and procedures; thêm manual review cost, không cần provider subscription | Có thể giả giấy tờ/thông đồng; không cryptographic verification. Development kiểm thử procedure với synthetic source artifacts được đánh dấu; không fake flags hoặc production identity |
| C — Trusted External Provider | Trust theo authenticated provider identity/authority/consent claims và actual verified callbacks | Existing generic transport primitives only | Chưa có provider adapter/account/contract; lựa chọn commercial provider/phí phải do người dùng quyết định | Không nhận arbitrary callback/signature claim; BLOCKED_CONTRACT cho tới integration thật |

**Khuyến nghị B, có điều kiện.** Đây là xác minh nội bộ bằng chứng tài liệu, không phải chữ ký số, KYC certification hoặc bảo đảm tư cách pháp lý tuyệt đối. Nếu doanh nghiệp không chấp nhận mức assurance này để ràng buộc thương mại, chọn A hoặc C; không đổi tên lời khai thành VERIFIED để hoàn thành B.

## Proposed package B — cần người dùng duyệt toàn bộ hoặc điều chỉnh

Các policy D1–D5 được **APPROVED WITH AMENDMENTS** bởi người dùng. Approval này không kế thừa tự động từ B1/B3/B5 và không giả định các proof/source/scanner contracts đã tồn tại. Không có default enabled cho Workspace cũ.

### D1. Identity và authority proof

Mỗi case cho **một Buyer, một Quote revision và một DocumentId**, không tạo global signing registry hoặc IAM mới. Với CONTACT buyer: signer là chính cá nhân đó hoặc có delegation evidence rõ ràng. Với ORGANIZATION: signer Contact có liên hệ owner-verified với Organization và có giấy ủy quyền/authority basis xác minh độc lập. Nếu không có CRM affiliation hợp lệ, MVP chặn, không tự tạo Contact/quan hệ từ payload.

Recorder nộp customer-origin tài liệu chấp nhận, identity/representation artifacts và provenance: nguồn nhận, ngày nhận, ai nhận, reference tài liệu gốc, cách kiểm tra độc lập. Không xem source-description/email header upload là server-authenticated origin. Verifier phải kiểm tra trực tiếp:

1. Danh tính người chấp nhận gắn đúng Buyer; proof đủ theo checklist Workspace, không chỉ tên/email giống nhau.
2. Với Organization/delegation: chủ thể cấp quyền, scope mua/chấp nhận, effectiveFrom/effectiveUntil, amount/currency limit, revocation status và tài liệu nguồn. Authority không có thời hạn/scope rõ ràng không qua MVP.
3. Customer-origin văn bản có lời đồng ý rõ ràng, không còn điều kiện thương mại chưa được giải quyết; tham chiếu chính xác QuoteNumber/revision và DocumentId hoặc PDF SHA256 của B5. Generic “đồng ý”/ảnh chữ ký rời không đủ.
4. Từng kết luận có retained artifact IDs/hashes và rationale/procedure audit; không chỉ có second employee checkbox.

Narrow CRM participant trả visible Buyer/Contact–Organization affiliation facts/version/lease; **không trả AuthorityVerified từ role CRM**. Quote-specific verification boundary sở hữu reviewed identity/authority conclusions, assurance và nguồn proof theo policy đã duyệt. `IQuoteAcceptancePartyVerifier` dự kiến nằm ở Quotes.Contracts, được CRM implement như B5; không tạo Sales→CRM project-reference cycle hoặc foreign table access. Nếu về sau cần reusable buyer signing authority registry, đó là scope riêng.

Không thu thập số giấy tờ tùy thân/credentials vào structured fields, log hoặc audit payload. MVP ưu tiên business/delegation/consent proof tối thiểu; yêu cầu government-ID/KYC hoặc dữ liệu nhạy cảm bổ sung cần policy rõ ràng trước khi thu thập. Thiếu proof đáp ứng checklist thì giữ PENDING hoặc REJECTED_EVIDENCE, không VERIFIED.

### D1a. Checklist bắt buộc và tiêu chí từ chối

Đề xuất checklist version `B6-DOC-REVIEW-v1`, Backend-owned theo Workspace. Đây là nội dung cần duyệt; không để free-text rationale tự thay một checklist trống. Workspace chỉ bật positive review khi đã cấu hình approved proof/source types, nguồn cấp authority và người có trách nhiệm quản trị checklist qua contract chính thức. Thiếu cấu hình này → `VERIFICATION_CHECKLIST_UNAVAILABLE`, kể cả người dùng đã chọn Option B.

| Check | Proof tối thiểu được chấp nhận trong đề xuất | Kiểm chứng nguồn / điều kiện bắt buộc | Từ chối hoặc giữ blocked khi |
|---|---|---|---|
| ID-PERSON | Retained identity attestation do nguồn xác minh độc lập nằm trong policy approved-source list phát hành; tham chiếu đúng signer/Buyer, thời điểm và phương pháp kiểm tra | Verifier xác minh nguồn cấp attestation qua kênh/record độc lập với chính package nộp; lưu origin/issuer reference và artifact hash. Attestation không chỉ do Recorder tự viết | ContactId/email/name match hoặc CRM note đơn lẻ; nguồn cấp chưa được policy chấp thuận; không xác minh được nguồn; bản sao không có provenance |
| AUTH-INDIVIDUAL | ID-PERSON + explicit consent của chính cá nhân Buyer; nếu người khác đại diện phải có delegation document | Exact Buyer linkage; người cấp ủy quyền có identity proof, scope/amount/currency và thời hạn phù hợp | Recorder tự gán “self buyer”; đại diện không có delegation; thiếu scope/time |
| AUTH-ORGANIZATION | Document cấp quyền của Buyer legal representative/authorized grantor được xác định từ independent authoritative issuer/registry records do policy chỉ định; nêu signer và phạm vi chấp nhận | CRM affiliation chỉ hỗ trợ linkage. Verifier kiểm tra grantor/source, signed original provenance và direct independent confirmation; retained corroborating record, không chỉ letterhead | IsPrimaryRepresentative/Role=DecisionMaker; chữ ký ảnh rời; người cấp quyền không xác minh được; duy nhất một PDF upload không có source corroboration |
| SOURCE-PROVENANCE | Customer-origin original consent artifact + independent issuer/source corroboration artifacts | Source must match approved checklist, evidence file bytes retained, known acquisition time/reference; origin checks do independent Verifier thực hiện và audit | Chỉ lời kể Recorder, email address matching, forwarded screenshot hoặc copied URL; không có cách tái kiểm tra nguồn. Manual call note chỉ là supplementary attestation, không thay retained proof |
| CONSENT-DOCUMENT | Customer-origin signed statement đồng ý rõ ràng, đúng QuoteNumber/revision và DocumentId hoặc SHA256 của issued PDF | Reviewer đối chiếu trực tiếp artifact đã kiểm tra an toàn với B5 exact document; server verify stored binding/hash/fingerprint; consent không còn điều kiện chưa giải quyết | “Đồng ý” generic, file ký khác revision, thay hash bằng payload, delivery/approval/B3 report, không xác định được nội dung được đồng ý |
| SOURCE-VALIDITY | Proof và authority trong hạn, cùng Buyer/currency/amount scope; revocation freshness theo D1b | All mandatory checks have proof IDs/hashes and explicit outcome; source unavailable = blocked, negative finding = rejected | File chưa clean, expired/revoked, thiếu mandatory item hoặc unverifiable source |

**Approved-source list không có giá trị mặc định được tự bịa.** Tên nguồn phát hành identity/authority proof cụ thể hoặc phương thức độc lập được doanh nghiệp chấp nhận phải được cấu hình trong versioned checklist trước positive use. Phê duyệt framework B không tự chứng minh có nhà phát hành/proof verifier ngoài hệ thống. Current source/issuer registrations are unavailable; positive VERIFIED remains BLOCKED_CONTRACT, not a pending framework policy vote. Nếu chưa có source đáp ứng checklist thì chỉ có submission/history/negative paths; báo `BLOCKED_IDENTITY_AUTHORITY_SOURCE`, không báo Full B6 PASS. Không tự chọn identity/KYC/notary/provider subscription. Không nhận runtime payload bổ sung một issuer mới vào allowlist.

### D1b. Revocation và freshness

Documentary MVP **không có external revocation feed**. Chỉ có thể kiểm tra expiry, retained independent-source confirmation và revocation/invalidation đã được ghi nhận/thông báo qua owner workflow. Không có recorded event được mô tả là `NO_KNOWN_RECORDED_REVOCATION`, không phải “đã chứng minh bên ngoài chưa thu hồi”.

Đề xuất: verification/source-confirmation và authority review có freshness **24 giờ**, configurable/versioned theo Workspace. Positive commit phải trong cả freshness window, authority effective interval và Quote validity; quá cửa sổ phải re-review nguồn/authority và append verdict mới. Nguồn bắt buộc unavailable hoặc không thể reconfirm → fail-closed. Freshness 24h giới hạn mức stale được doanh nghiệp chấp nhận, không thay trusted external live revocation proof. Nếu doanh nghiệp cần assurance mạnh hơn/live revocation thì B positive acceptance tiếp tục BLOCKED đến khi có contract đó hoặc chọn A/C với nguồn thích hợp.

### D2. Trust và separation of duties

B3 records tiếp tục UNVERIFIED mãi trong history. B6 tạo evidence case mới; có thể reference B3 report để tra cứu nguồn nhưng không lấy nó làm proof hoặc promote tại chỗ.

State projections từ append-only events: PENDING_VERIFICATION → VERIFIED hoặc REJECTED_EVIDENCE; về sau INVALIDATED/REVOKED bằng event mới. `verificationMethod=INDEPENDENT_DOCUMENT_REVIEW`, `assuranceLevel=HUMAN_DOCUMENT_REVIEW_ONLY`; không gọi provider-/cryptographically-verified. Frontend không được chọn trust/method/verifiedBy/time.

Đề xuất quyền explicit mới (tên cần admission sau duyệt):

- `quotes.submitAcceptanceEvidence` + quotes.read: submit retained artifacts/case.
- `quotes.verifyAcceptanceEvidence` + quotes.read: independently review/verify/reject/invalidate evidence.
- `quotes.acceptVerified` + quotes.read + quotes.update: commit Direct Sale ACCEPTED từ verified effective case.
- `quotes.readAcceptanceEvidence` + quotes.read: xem proof files/identity/authority review. quotes.read đơn thuần không mặc nhiên đọc raw PII proof.

Submit/review/verify/accept actors còn phải có `quotes.readAcceptanceEvidence`, current proof/source record scope và FLS cho tất cả trường hiện diện trong retained artifacts. Mutation capability không bỏ qua quyền đọc proof. Không auto-grant existing members/Workspace Owner; quotes.update/quotes.approve/issueDocument không chứa quyền verify. Verifier phải khác **Evidence Recorder, Quote Creator và Approval Requester**; không owner override. B1 approver chỉ được làm verifier nếu được cấp capability riêng và không vi phạm các điều kiện độc lập này; B1 approval không thay consent.

### D3. Evidence file storage và provenance

Đề xuất private **SQL-backed immutable evidence storage riêng**, không chèn uploaded customer file vào `quotes.Documents/DocumentContents` hoặc sửa issued PDF. Reuse transaction/hash patterns, không xây DMS/microservice. Server cấp ArtifactId/hash/media/size, scope theo Workspace/case/Quote; opaque protected downloads, không public URL hoặc ApiHost temp-file authority.

MVP PDF-only, tối đa10MiB/file,20pages/file,5files/case, cumulative25MiB/case; kiểm signature/mime/parser/encryption/active-content/embedded-files/links risk theo inspected PDF contract, bounded isolated process. File mới QUARANTINED; bắt buộc trusted scanner/inspection verdict sạch, engine/signature version/time/hash recorded trước khi được review/download. Không tự chọn paid provider. **Nếu chưa có scanner/inspection contract đáp ứng policy thì storage/positive verification BLOCKED_CONTRACT**; không coi parse thành công, hash hoặc antivirus flag từ client là clean verdict. Không mở inline content chưa được kiểm tra.

Đề xuất retention kỹ thuật B6 ban đầu: giữ immutable proof/events vô thời hạn, không delete/overwrite/restore UI, tối thiểu hóa dữ liệu thu thập. Đây là **quyết định mới cần duyệt cho customer evidence**, không tự áp dụng B5 PDF retention thành legal policy cho identity proofs. Nếu người dùng cần thời hạn/hủy dữ liệu hoặc legal hold riêng, phải điều chỉnh trước implementation; không tự triển khai delete để “giải quyết” retention. Backup/DR và encryption deployment cần xác minh trước commercial rollout; B5 backup gap không tự được coi là đã giải quyết.

### D4. Commercial/lifecycle eligibility và limit ban đầu

Policy Backend versioned theo exact Workspace, missing/invalid/disabled fail-closed. **Đề xuất limit mới B6**: cho phép human-review Direct Sale acceptance khi GrandTotal authoritative **≤100,000,000 VND**. Giá trị lớn hơn, currency khác VND khi chưa có explicit limit, hoặc identity/authority assurance không đủ: positive acceptance blocked; không FX. Đây là đề xuất independent B6 policy, không mặc định B1 threshold là authority cho consent.

Chỉ verify effective acceptance khi Quote **SENT**, unarchived, chưa hết ValidUntil theo authoritative Workspace timezone, fresh B1 NOT_REQUIRED/independent APPROVED fingerprint+policy. B2 delivery cần tồn tại theo commercial content hiện hành, nhưng B2 không có DocumentId: **không tuyên bố B2 chứng minh đã gửi đúng PDF**. Customer-origin consent artifact phải bind exact issued B5 Document riêng.

Bind WorkspaceId/QuoteId/root/revision/Buyer, DocumentId/PdfSha256/commercial fingerprint và stored seller/buyer snapshot. Backend lookup/compare B5 metadata và stored bytes/hash, không tin client hash/Buyer/verified fields. Không tự chọn latest document hoặc suy ra new document supersedes old. Seller identity accepted là snapshot trong chính issued document; dữ liệu Studio thay đổi không sửa chứng từ cũ. Nếu Buyer/Quote commercial fingerprint/revision mismatch thì từ chối.

Đề xuất conservative policy cho approval version change: ở verification/commit, B5 document approval-policy binding phải còn phù hợp current B1; policy đổi cần reassess/reissue/new consent nếu binding cũ không còn hợp lệ. Không rebind consent cũ sang PDF mới. Current B6 policy version change làm case không eligible để commit cho đến re-review dưới version mới; append decision mới, không ghi đè lịch sử.

**Không so sánh B5 SourceResourceVersion bằng current Quote ResourceVersion**: Issue đã tăng RV, còn approval/delivery cũng có thể tăng RV mà không đổi commercial content. Dùng exact immutable document/revision/fingerprint và current If-Match cho mutation. Phân biệt acceptance reported time với BackendRecordedAt/VerifiedAt/CommittedAt.

### D5. Direct Sale, linked Quote và revocation

Verify evidence là mutation riêng, **không tự chuyển Quote ACCEPTED**. Direct Sale có typed command mới dự kiến `acceptQuoteWithVerifiedEvidenceCommand`, chỉ lấy EvidenceId reference và current If-Match, rồi owner re-verifies all bindings/proof/policy/authority. Khi thành công: atomic evidence-use record + Quote ACCEPTED + command/audit/outbox; không tạo Deal/Order/Payment. Command không cho client cung cấp trạng thái, signer truth hoặc trust.

Linked Quote: được lưu/review verified evidence khi đúng binding, nhưng **Quote vẫn SENT**, `LINKED_ACCEPTANCE_BLOCKED_GATE_C`/BLOCKED_CONTRACT cho positive linked transition. Không thay strict `acceptQuoteAndCloseDeal` request, không mở MarkWon hay staged Frontend Accept→Won. B6 không tạo atomic Quote/Deal participant hoặc Order branch.

Before acceptance only: authority expiry, recorded/notified revocation, source-confirmation older than24h, invalid proof or incompatible policy block the new acceptance decision. A historical committed acceptance does not expire merely because24h elapsed; later revocation/invalidation requires a separately approved lifecycle policy. Giữ immutable past verdict/event, append invalidation. Nếu Direct Sale đã ACCEPTED rồi mới bị invalidate: **không tự undo lifecycle**, không tự đổi SENT/REJECTED hoặc tạo Restore; read model thể hiện acceptance history + ineffective evidence, future commitment checks fail-closed và yêu cầu xử lý riêng. Không rewrite lịch sử thành “chưa từng accepted”.

Gate C future consumer phải query live narrow owner verification result, không dùng EvidenceId/old replay receipt như commitment. Contract phân biệt VerifiedEvidenceAvailable với **CommittedAcceptance**; linked SENT evidence không thể trả DealWonEligible. MarkWon không consume contract mới trong B6.

## ArchitectureRecord — chỉ triển khai sau duyệt

- Quotes owns scoped case/events/acceptance artifact bytes/provenance, verification and direct-sale acceptance; B5 issued document remains immutable owner source.
- CRM owns affiliation/eligible Buyer captures via narrow lease; IdentityAuth remains account/session authority, no new IAM. Workspace owns exact versioned B6 policy/config; AccessControl owns independent explicit grants.
- No new CommercialEvidence purchase meaning, no foreign persistence queries, no customer staff membership.
- Serialize B6 verification/accept/reject with **same canonical Quote aggregate SQL lock** as B3/B4/B5; case version and current Quote If-Match protect concurrent verification. One case/immutable document binding, unique command/evidence usage; compare request fingerprints and reauthorize replay.
- Hold source authority/affiliation/version fences through commit using owner leases. Quote + case/event + idempotency/audit/outbox share transaction. No partial VERIFIED receipt when artifact storage or validation failed; failed cases stay explicitly blocked/pending without lifecycle success.
- Reject wins first ⇒ acceptance state check blocks; Accept wins first ⇒ B3 reject terminal state blocks. Different keys cannot yield contradictory committed decisions. Same-key replay returns original identity/decision with current effective status separately; revoked/invalidated evidence cannot be reused by replay.
- Record reads require record scope, proof-specific permission/FLS and owner visibility; deny indivisible raw proof if any included field is now hidden. No sensitive content in outbox/logs. Browser aborts/reset case state on Workspace/account change.
- Add narrow typed routes/contracts only after policy approval/admission; don't repurpose generic accept. Draft operation names/routes are design proposals, not live endpoints or permissions.

## Implementation / verification checkpoints after approval

1. Admit policy/schema/capabilities/owner boundaries; no existing default grants.
2. Implement private immutable PDF evidence/quarantine/inspection/scanner narrow contract. No clean verifier ⇒ remain blocked; no synthetic bypass.
3. Implement documented identity/authority/provenance review case and independent capability enforcement.
4. B5 exact document binding, trust ledger and effective verifier; then typed Direct Sale ACCEPTED only under approved D4. Linked acceptance/Won stay blocked.
5. Lazy-load CRM evidence panel in existing Quote Detail; distinguish B3 UNVERIFIED, B6 trust and Quote status. No heavy main bundle dependency or budget increase. No customer portal in B.
6. Officially provision isolated Development roles/Workspace after approval; use labelled synthetic customer-origin artifacts and actual independent review workflow. Test synthetic actors/proof procedure, **not production authenticity**; no CRM employee UI acting as customer consent button. No real email, provider subscription, default grants or SQL authorization edits.
7. B6-01…30 API/SQL/Chromium no mocks, positive/negative/revocation/policy/concurrency/storage failures, persistence and original29-table hashes. Re-run relevant B1–B5 tests, compare baseline diagnostics. Full positive tests remain NOT_RUN until approved method and genuine mechanism operate; blocked cases never counted PASS.

## Decision set / AUTHORITY_GAP

| Claim | Decision cần chốt | Current status |
|---|---|---|
| AG-B6-METHOD | A portal / B independent documentary / C trusted provider; trust qualification | APPROVED_WITH_AMENDMENTS |
| AG-B6-IDENTITY-AUTHORITY | D1/D1a concrete mandatory checklist + approved source configuration; no default identity issuer | APPROVED_FRAMEWORK / SOURCE_CONTRACT_BLOCKED |
| AG-B6-REVOCATION | D1b24h freshness, known-recorded revocation only; no claim of live external revocation | APPROVED_WITH_AMENDMENTS |
| AG-B6-ASSURANCE-LIMIT | D4 initial VND100m limit and other currencies block; human assurance commercially acceptable? | APPROVED_WITH_AMENDMENTS |
| AG-B6-DUTIES | D2 separate capabilities + verifier independence; no auto-grants | APPROVED_WITH_AMENDMENTS |
| AG-B6-STORAGE | D3 separate SQL/PDF bounds/quarantine/trusted clean verdict/retention; absent scanner blocks | APPROVED_POLICY / CONTRACT_BLOCKED |
| AG-B6-LIFECYCLE | D4/D5 Direct Sale separate commit; linked evidence only/Gate C blocked; invalidation does not undo status | APPROVED_WITH_AMENDMENTS |

B6.0 §5.5 yêu cầu **“Bắt buộc yêu cầu người dùng chốt phương án có ảnh hưởng đến việc chuyển Quote thành ACCEPTED.”** §25 yêu cầu **“Không tự vượt qua B6.0 Policy Gate.”** Vì vậy proposal cụ thể này cần người dùng duyệt/điều chỉnh trước positive implementation; đây là yêu cầu trực tiếp của task mới, không hỏi lại policy đã duyệt ở các Gate trước.

## Historical A–R snapshot at B6.0 (superseded by final verification report)

| Report item | Evidence / status |
|---|---|
| A Git/Baseline | Fresh Git/hash/SQL29tables captured above; no resets |
| B Policy | Proposed A/B/C + D1–D5; approval pending |
| C Identity | Existing staff IAM/CRM inspected; customer/proof verification gap |
| D Signer | Ordinary affiliations reused only as facts; authority missing |
| E Document | B5 exact identity/revision/hash source verified SQL; consent binding design pending |
| F Storage | B5 remains intact; B6 upload/scanner/storage contract not implemented |
| G Consent | No method approved; no customer consent captured |
| H Backend | Existing acceptance and MarkWon fail-closed unchanged; no new endpoint/schema |
| I Frontend | Existing B3/B5 UX inspected; no positive button/action added |
| J API/SQL | SQL read-only baseline/integrity/migrations PASS; B6 live mutations NOT_RUN |
| K Connected browser | B6 journey NOT_RUN_POLICY_GATE; B5 prior4/4 evidence preserved |
| L Security | Trust/source boundary source review + existing guards; new security acceptance NOT_RUN |
| M Concurrency | Architecture proposed; existing behavior protected; B6 races NOT_RUN |
| N Regression | Backend276/4baseline, Frontend strict3/3; source/data preserved |
| O Performance | New verification latency/storage/concurrency NOT_RUN; no production claim |
| P Quality | No new code in Phase0; no release/full-functional claim |
| Q Gate C | Not open; no MarkWon changes; verified evidence ≠ linked acceptance/Won |
| R Git | Only B6 review documents/artifacts added, original local changes retained; no commit/push/merge |

**Verdict: policy B6 đã duyệt; independent assessment slice triển khai. Full B6 PARTIAL / BLOCKED_CONTRACT.** Không đánh dấu positive VERIFIED, Direct Sale ACCEPTED hoặc linked atomic acceptance PASS từ policy/model/negative guards.

## Controlling implementation amendment

Freshness applies before acceptance, never automatic historical acceptance expiry. Option B/framework approval does not approve a particular identity issuer, authority source, scanner or file store. Current source registrations contain none. Implement the independent versioned-policy/B5-binding/readiness and negative-assessment slice now. Do not expose upload, verification/acceptance mutation or proof download success until those contracts genuinely exist. Existing B3 UNVERIFIED, positive acceptance guard and MarkWon remain unchanged.

New `getQuoteCustomerAcceptanceReadiness` and `evaluateQuoteCustomerAcceptance` are **read-only assessments**, not verification or acceptance commands. HTTP200 means assessment returned, with `status=BLOCKED_CONTRACT`, `verificationStatus=NOT_VERIFIED`, `lifecycleCommitted=false`; never a verified receipt. The POST assessment requires current If-Match, bounded request/idempotency header and reauthorizes each repeated request; no success mutation receipt/outbox is written. True submission/verification/acceptance/storage remain BLOCKED_CONTRACT and are not exposed through alternate endpoints.

## Current delivery note — 2026-10-26

Implemented only versioned policy, exact B5 binding/hash read, proof-read/intent/record/source-FLS guarded readiness and read-only assessment, lazy UI, real negative API/SQL/browser checks. No upload, evidence case/byte store, positive verify/direct accept endpoint or new evidence migration exists while trusted sources/scanner/storage are unavailable. Historical positive acceptance is never invalidated by the24h pre-acceptance freshness predicate. Existing B3/MarkWon/atomic acceptance routes remain unchanged. Final evidence is in quote-verified-customer-acceptance-verification.md; historical Phase0 tables describe their capture time.


## B6.1 user-approved amendment — 2026-10-26

The subsequent Complete B6 Phase 1 request authorizes immutable proof storage while the scanner is unavailable: valid PDF candidates may be committed as QUARANTINED/SCAN_PENDING. This supersedes only the earlier blanket proof-storage blocker. Scanner/identity/signer authority/independent review/positive VERIFIED/ACCEPTED/Won remain blocked. Metadata read and file-content release are distinct; no proof-content HTTP route is introduced. See [storage design](quote-acceptance-proof-storage-design.md), [B6.1 verification](quote-acceptance-proof-storage-verification.md) and [B6.2 boundary plan](quote-acceptance-proof-scanner-boundary.md).
