# UniCoreCRM — Source cleanup, Git audit & delivery

Thời điểm báo cáo: 2026-10-29T08:45:12+07:00 (Asia/Saigon).

**Kết quả: PARTIAL.** Đã commit/push tập thay đổi độc lập đã kiểm tra; chưa dọn xóa file vật lý và chưa commit phần CRM/Quote/governance còn thiếu review/admission. Không sửa dữ liệu SQL Server, không merge default branch, không rewrite history.

## A. Repository Baseline

| Repository | Git root | Branch ban đầu | HEAD ban đầu | Upstream | Dirty ban đầu | Ignored ban đầu |
|---|---|---|---|---|---:|---:|
| backend | `D:\Project_All\UnicoreCRM\backend` | `master` | `13cd39eba01ebc9dc238f8b0ef98e174c1d4d3f6` | `origin/master` | 1277 | 7588 |
| frontend | `D:\Project_All\UnicoreCRM\frontend\unicorecrm-web` | `feat/deal-compact-kanban-card` | `814cc4685b6d8f8ead2fc945f4607a2d0168320d` | `origin/feat/deal-compact-kanban-card` | 729 | 20476 |
| control | `D:\Project_All\UnicoreCRM\.unicore-ai` | `main` | `096551c9e1a558bb598c4524e1c7cf9ef50349ca` | `không có` | 376 | 104 |

Backend origin: `https://github.com/TUAN131125/unicorecrm-backend.git`; frontend origin: `https://github.com/TUAN131125/unicorecrm-web.git`. `.unicore-ai` là Git repository độc lập, không có remote. Thư mục cha UniCoreCRM không phải Git repository. Không có submodule hoặc nested repository trong ba cây mục tiêu; các worktree review khác được kiểm kê và giữ nguyên.

Sau fetch ban đầu: backend ahead 1 / behind 0 (`13cd39e`); frontend ahead 0 / behind 0. Không có staged files trong cả ba repository. Status được đọc bằng `git status --porcelain=v1 -z --untracked-files=all`, parse UTF-8/NUL, không tách tên file theo khoảng trắng.

| Repository | Modified | Deleted có sẵn | Untracked | Added/staged/renamed |
|---|---:|---:|---:|---:|
| backend | 97 | 6 | 1174 | 0 |
| frontend | 281 | 1 | 447 | 0 |
| control | 290 | 2 | 84 | 0 |

Snapshot trước mutation nằm ngoài working tree tại `D:/Project_All/UnicoreCRM-cleanup-recovery-20261029/`: mỗi repository có `inventory.json`, toàn bộ dirty file bytes + SHA-256 trong `dirty-files/`, `unstaged.patch`, `staged.patch`, `diff.txt`, lịch sử và danh sách worktree. Deleted files có sẵn vẫn khôi phục được từ HEAD; không phải file đã xóa trong nhiệm vụ này.

## B. File Inventory

| Category | Backend | Frontend | Control |
|---|---:|---:|---:|
| Production source | 1056 | 1583 | 0 |
| Tests | 39 | 573 | 0 |
| Migrations | 288 | 8 | 0 |
| Generated required | 10 | 21 | 0 |
| Governance | 0 | 1 | 384 |
| Documentation | 68 | 207 | 103 |
| Evidence | 979 | 280 | 4 |
| Temporary | 309 | 0 | 0 |
| Build output | 0 | 0 | 0 |
| Duplicate | 0 | 0 | 0 |
| Unknown | 251 | 63 | 87 |

Các số trên là phân loại bảo thủ của tracked + dirty paths, không gồm toàn bộ ignored dependency/build tree. Script/tool/config chưa được chứng minh mục đích vẫn nằm Unknown/NEEDS_REVIEW. Duplicate là thuộc tính bổ sung: 14 nhóm/31 file frontend thuộc Evidence, không cộng lại thành category riêng. Generated build output (`bin/`, `obj/`, `node_modules/`, `dist/`, cache) được kiểm kê qua ignored paths và giữ local; không coi mọi file có tên test/temp/backup là rác.

Bản inventory đầy đủ, gồm từng tracked/untracked/ignored path, hash dirty files, root/upstream/worktrees, nằm trong snapshot ngoài Git. Không có thao tác untrack application source, migrations, canonical contracts hay required clients.

## C. Cleanup Results

| Decision | Kết quả | Lý do |
|---|---|---|
| REMOVE_SAFE | **0 file đã xóa thực tế** | Automatic approval review chặn cả lệnh xóa directory và lệnh xóa từng file có hash verification; chỉ trả về `blocked by policy`. Không thử vượt chặn. |
| ARCHIVE | **445 file được copy và đối chiếu SHA-256**, bản gốc vẫn còn | Runtime acceptance logs/requests/JSON/screenshots/keys, có giá trị tham khảo và khôi phục. |
| KEEP | Toàn bộ source, migrations, snapshots, governance, B1–B6 evidence, fixtures, local config và 31 duplicate evidence files | Không chứng minh được là disposable hoặc thuộc nhóm cần bảo vệ. |
| NEEDS_REVIEW | Các thay đổi source/admission/authority chưa thể xác minh độc lập và file mục đích chưa rõ | Giữ nguyên, không stage để làm sạch status. |

Archive bổ sung tại `D:/Project_All/UnicoreCRM-cleanup-recovery-20261029/archived-runtime/`, có `manifest.json` ghi source/archive/hash/bytes từng file. Ba root: `backend/artifacts/multicurrency/`, `backend/artifacts/studio-first-visit/`, `backend/artifacts/ui-cleanup/`. Đã kiểm tra không có tracked files hoặc database/SQL backup trong tập archive, không có process runtime đang dùng các root này khi kiểm tra. Nội dung là output của các verifier scripts; tham chiếu chính thức trong docs được bảo toàn vì bản gốc không bị xóa.

`.gitignore` backend bổ sung sáu runtime roots, TestResults, Python/editor/OS cache và SQL data/backup extensions. 309 runtime files, hai Python bytecode và một TRX có sẵn không còn hiện untracked; **không bị xóa**. Frontend bổ sung tool caches, root-local credential files và SQL data/backup extensions; fixture subtrees không bị blanket-ignore bởi các rule credential root. Control đã có Python/cache ignore phù hợp nên giữ nguyên.

Không xóa environment configs; không sửa governance content, integrity pins, allowlists, thresholds, budgets hoặc accepted snapshots. Hash check sau thao tác: mọi dirty file gốc còn tồn tại, chỉ `.gitignore` backend đổi bytes trong tập dirty gốc. `.gitignore` frontend ban đầu sạch và được sửa trong nhiệm vụ này.

### 14 duplicate groups

Không có implementation/module duplication để refactor. Đây là screenshot và JSON evidence của các checkpoint khác nhau; giữ danh tính before/after/timepoint thay vì chọn một runtime implementation chung. Gate FAIL được giữ nguyên trên cây source đầy đủ. Các reference dưới đây được tìm trong tracked + dirty textual source/docs/tests/scripts; không bao gồm chính generated inventory.

1. SHA-256 `d3ecbac1dafe417cc496fbb1e0403307e649eeb9511f0f3b80cd1f9a163b18e3` — **KEEP**.
   - `docs/verification/evidence/access-notices/access-before-desktop-elapsed.png` — references: `docs/verification/access-notices-results.md`; `docs/verification/evidence/access-notices/source-preservation.json`
   - `docs/verification/evidence/access-notices/access-before-desktop-initial.png` — references: `docs/verification/evidence/access-notices/source-preservation.json`
2. SHA-256 `0a94fd88be702b57f653dd36dfb3971cbf1268cdb09a08c05d24dca8cf7908b2` — **KEEP**.
   - `docs/verification/evidence/customer-ui/after-data-preservation.json` — references: `docs/verification/internal-notices-removal-results.md`; `docs/verification/evidence/customer-ui/frontend-scope.json`; `docs/verification/evidence/remaining-access/frontend-scope.json`; `docs/verification/customer-ui-results.md`; `docs/verification/evidence/internal-notices/frontend-scope.json`; `docs/verification/evidence/deal-workspace/scope.json`; `docs/verification/remaining-access-notices-results.md`; `docs/verification/deal-workspace-results.md`
   - `docs/verification/evidence/internal-notices/after-data-preservation.json` — references: `docs/verification/internal-notices-removal-results.md`; `docs/verification/evidence/customer-ui/frontend-scope.json`; `docs/verification/evidence/remaining-access/frontend-scope.json`; `docs/verification/customer-ui-results.md`; `docs/verification/evidence/internal-notices/frontend-scope.json`; `docs/verification/evidence/deal-workspace/scope.json`; `docs/verification/remaining-access-notices-results.md`; `docs/verification/deal-workspace-results.md`
3. SHA-256 `caa2930fbae72d6ed9219da25a2206e9131840518009377ec5fce2584542a13b` — **KEEP**.
   - `docs/verification/evidence/deal-workspace/deal-workspace-before-desktop.png` — references: `docs/verification/evidence/deal-workspace/scope.json`; `docs/verification/deal-workspace-results.md`
   - `docs/verification/evidence/internal-notices/internal-notices-after-deal-desktop.png` — references: `docs/verification/internal-notices-removal-results.md`; `docs/verification/evidence/internal-notices/frontend-scope.json`
4. SHA-256 `185f6316ace61f9870c5c4c92feedc073e9ae7c943298f93ab09d58dd4112527` — **KEEP**.
   - `docs/verification/evidence/deal-workspace/deal-workspace-before-laptop.png` — references: `docs/verification/evidence/deal-workspace/scope.json`; `docs/verification/deal-workspace-results.md`
   - `docs/verification/evidence/internal-notices/internal-notices-after-deal-laptop.png` — references: `docs/verification/internal-notices-removal-results.md`; `docs/verification/evidence/internal-notices/frontend-scope.json`
5. SHA-256 `c0dd2a5b580ad9c8b6fdef0a4df06656c2c8d13f1343699369e759b13a0d82a8` — **KEEP**.
   - `docs/verification/evidence/deal-workspace/deal-workspace-before-mobile.png` — references: `docs/verification/evidence/deal-workspace/scope.json`; `docs/verification/deal-workspace-results.md`
   - `docs/verification/evidence/internal-notices/internal-notices-after-deal-mobile.png` — references: `docs/verification/internal-notices-removal-results.md`; `docs/verification/evidence/internal-notices/frontend-scope.json`
6. SHA-256 `2d73220acf9a8881f9d1f54ce1104eade0ca56fe5f9163eb94f27cb32f77094e` — **KEEP**.
   - `docs/verification/evidence/internal-notices/internal-notices-after-error.png` — references: `docs/verification/evidence/internal-notices/frontend-scope.json`; `tests/e2e/internal-notices.acceptance.ts`
   - `docs/verification/evidence/remaining-access/remaining-access-after-critical-error.png` — references: `tests/e2e/remaining-access.acceptance.ts`; `docs/verification/evidence/remaining-access/frontend-scope.json`
7. SHA-256 `9836a6f792da4471c127e08041ee85f1971c290c0956d7037da200ba665b8f3c` — **KEEP**.
   - `docs/verification/evidence/internal-notices/internal-notices-before-customer-desktop-initial.png` — references: `docs/verification/internal-notices-removal-results.md`; `docs/verification/evidence/internal-notices/frontend-scope.json`
   - `docs/verification/evidence/remaining-access/remaining-access-after-customer-desktop-initial.png` — references: `docs/verification/evidence/remaining-access/frontend-scope.json`; `docs/verification/remaining-access-notices-results.md`
   - `docs/verification/evidence/remaining-access/remaining-access-before-customer-desktop-initial.png` — references: `docs/verification/evidence/remaining-access/frontend-scope.json`
8. SHA-256 `e2f02b605daec98eb584a0d11dff707b7191003d8fb632069dc58cfe127f9f64` — **KEEP**.
   - `docs/verification/evidence/internal-notices/internal-notices-before-customer-mobile-elapsed.png` — references: `docs/verification/evidence/internal-notices/frontend-scope.json`
   - `docs/verification/evidence/remaining-access/remaining-access-after-customer-mobile-elapsed.png` — references: `docs/verification/evidence/remaining-access/frontend-scope.json`; `docs/verification/remaining-access-notices-results.md`
9. SHA-256 `6feb003d750bbc38d06c6e0a22c9f10727d14a6e59acf8ea0c2e0a92e1580694` — **KEEP**.
   - `docs/verification/evidence/internal-notices/internal-notices-before-customer-mobile-initial.png` — references: `docs/verification/internal-notices-removal-results.md`; `docs/verification/evidence/internal-notices/frontend-scope.json`
   - `docs/verification/evidence/remaining-access/remaining-access-after-customer-mobile-initial.png` — references: `docs/verification/evidence/remaining-access/frontend-scope.json`; `docs/verification/remaining-access-notices-results.md`
   - `docs/verification/evidence/remaining-access/remaining-access-before-customer-mobile-initial.png` — references: `docs/verification/evidence/remaining-access/frontend-scope.json`
10. SHA-256 `3a704df05e92efd71d36f9febfba67a2a619a40a42c79f8163b3725f07c06fc1` — **KEEP**.
   - `docs/verification/evidence/internal-notices/internal-notices-before-deal-desktop-elapsed.png` — references: `docs/verification/evidence/internal-notices/frontend-scope.json`
   - `docs/verification/evidence/remaining-access/remaining-access-after-deal-desktop-elapsed.png` — references: `docs/verification/evidence/remaining-access/frontend-scope.json`; `docs/verification/remaining-access-notices-results.md`
11. SHA-256 `64534a0787ecaede2acbff037cf9884ba0e398578a6c794d100ca37705bde6fa` — **KEEP**.
   - `docs/verification/evidence/internal-notices/internal-notices-before-deal-desktop-initial.png` — references: `docs/verification/internal-notices-removal-results.md`; `docs/verification/evidence/internal-notices/frontend-scope.json`
   - `docs/verification/evidence/remaining-access/remaining-access-after-deal-desktop-initial.png` — references: `docs/verification/evidence/remaining-access/frontend-scope.json`; `docs/verification/remaining-access-notices-results.md`
   - `docs/verification/evidence/remaining-access/remaining-access-before-deal-desktop-initial.png` — references: `docs/verification/evidence/remaining-access/frontend-scope.json`
12. SHA-256 `f3f73c717a80e28eb7339a9a960171e09e7fbc2c8e4e747dbcbc99af5522075e` — **KEEP**.
   - `docs/verification/evidence/internal-notices/internal-notices-before-deal-laptop-elapsed.png` — references: `docs/verification/evidence/internal-notices/frontend-scope.json`
   - `docs/verification/evidence/remaining-access/remaining-access-after-deal-laptop-elapsed.png` — references: `docs/verification/evidence/remaining-access/frontend-scope.json`; `docs/verification/remaining-access-notices-results.md`
13. SHA-256 `0a2392d81ca5b8115a11bb1d8edb92e39d78b17fdd449670e7619052dcee6b5f` — **KEEP**.
   - `docs/verification/evidence/internal-notices/internal-notices-before-deal-mobile-elapsed.png` — references: `docs/verification/evidence/internal-notices/frontend-scope.json`
   - `docs/verification/evidence/remaining-access/remaining-access-after-deal-mobile-elapsed.png` — references: `docs/verification/evidence/remaining-access/frontend-scope.json`; `docs/verification/remaining-access-notices-results.md`
14. SHA-256 `4cd89642d48c6f938f1c748bf00c75ce3eff67a3a55f5366dfaea21b93f53dda` — **KEEP**.
   - `docs/verification/evidence/remaining-access/remaining-access-after-deal-mobile-initial.png` — references: `docs/verification/evidence/remaining-access/frontend-scope.json`; `docs/verification/remaining-access-notices-results.md`
   - `docs/verification/evidence/remaining-access/remaining-access-before-deal-mobile-initial.png` — references: `docs/verification/evidence/remaining-access/frontend-scope.json`

## D. Source Review

Actual diff của cả ba cây được lưu đầy đủ ngoài Git. Review sâu chỉ hoàn tất cho các file được allowlist và commit; **không tuyên bố đã audit/approve toàn bộ 2.382 dirty paths ban đầu**. Phần còn lại được phân loại và bảo toàn để tránh push nhầm thay đổi chưa admit.

| Nhóm | Quyết định |
|---|---|
| Backend CRM: Contact/Organization/Customer, Deal/Dynamic Pipeline, Tasks/Workflows, Quote B1–B6, AI, Communications, migrations, architecture tests, docs | KEEP local / NEEDS_REVIEW: nhiều dependency xuyên nhóm, owner metadata và endpoint admission FAIL. Không commit mới nhóm này. |
| Backend checkpoint `13cd39e` có sẵn | Giữ nguyên lịch sử; quét toàn bộ 467 changed files trước push; build exact committed source PASS. Giữ tên CONDITIONAL_FUNCTIONAL_CHECKPOINT, không nâng thành production acceptance. |
| Frontend Connected CRM, Quote/Deal, Dashboard, AI Assistant, UI/UX, new clients/contracts/routes, quality test changes | Phần lớn KEEP local / NEEDS_REVIEW; không stage hàng loạt. |
| Frontend error feedback | Commit bốn file: safe messages theo category, không render remote diagnostics, lọc fieldErrors/businessBlockers theo category, regression coverage. Test độc lập, typecheck/build/lint PASS. |
| Frontend Deal regression harness | Commit optional access `model?.toast`; không đổi assertion hoặc architecture test. Lint PASS; behavioural assertions hoàn tất nhưng runtime OPEN_HANDLE vẫn giống baseline. |
| Control preflight | Commit hai tools review-only và hai tests; output luôn PROPOSAL_ONLY_NOT_ADMITTED, không confer approval/adoption/admission. |
| Control generator | Commit generator + regression tests về baseline hash, duplicate/unsafe id và bảo toàn extension/orphan shards; chỉ chạy fixture tests trong temp directories. Không chạy generator vào production manifests. |
| Control runtime/governance/admission/integrity/schema/package changes | Giữ local, không commit. Không tự sửa pins hoặc nhận owner approval. |

Manual Quote Acceptance, Deal WON và Control Plane admission không được mở lại. Local diff đã có Quote identity/manual-source/acceptance endpoints và migration candidates; chúng **không nằm trong commit mới**. Không tạo production endpoint giả, không tuyên bố feature hoàn tất. Backend test đã báo endpoint admission FAIL; đây là lý do không đẩy các nhóm implementation đó.

## E. Quality Results

| Check | Phạm vi | Kết quả |
|---|---|---|
| `dotnet test UnicoreCRM.slnx` (restore/build included) | Cây backend đầy đủ | **474 PASS / 4 FAIL / 478 total**, khớp baseline được cung cấp. |
| `dotnet build UnicoreCRM.slnx` | Exact committed backend export | **PASS, 0 warnings / 0 errors**. |
| Backend security/authorization coverage | Các test trong full suite, metadata discovery dùng fake unreachable DB, không migrate/seed | Chạy trong full suite; endpoint admission test thuộc bốn baseline FAIL. Không chạy verifier scripts cần SQL. |
| Frontend `npm run verify` | Cây đầy đủ | **FAIL: 47/328 PASS**, dừng tại gate 48 `quality.repository-inventory` vì drift. Không chạy tiếp 280 gate còn lại qua canonical command. |
| Frontend lint + typecheck | Cây đầy đủ và selected-source export | **PASS**; isolated export ban đầu lộ nullable harness baseline, đã bảo toàn bản sửa có sẵn và chạy lại lint PASS. |
| Frontend `npm run build` | Cây đầy đủ và selected-source export | **PASS**, bundle budget PASS. |
| Frontend `npm run api:check` | Cây đầy đủ | **PASS**: 360 operations, 334 ready / 26 blocked. Không hand-edit hoặc regenerate API client để nhận PASS. |
| `quality.repository-cleanup` | Cây đầy đủ | **FAIL: 14 duplicate groups**, vẫn giữ evidence. |
| `quality.user-facing-error-presentation` | Selected-source export | **PASS**. |
| `quality.guidance-contracts` | Selected-source export | **PASS**. |
| Deal/Quote regression harness | Selected-source export và nguyên gốc HEAD | Behavioural assertions hoàn tất ở cả hai; **OPEN_HANDLE exit 86** ở cả hai. Không ghi full PASS. |
| Control proposal tests | Temp fixtures | **5/5 PASS**. |
| Control staging tests | Temp fixtures | **5/5 PASS**. |
| Control generator tests | Temp fixtures | **6/6 PASS**. |
| Frontend `npm test` (unit, contract, integration, route-smoke) | Cây đầy đủ | **[quality] FAIL 145/162 passed across 4 groups (891.3s).**. |
| Frontend route-smoke chạy riêng sau khi full command dừng | Cây đầy đủ | **PASS 8/8**; route module loads và missing-import/export negative controls PASS. |

Frontend extended suite dừng tại gate 146 `quality.contact-visual-baseline`: raw CSS hash khác snapshot. `src/index.css` không có dirty diff; LF-normalized bytes khớp HEAD ban đầu, nhưng HEAD hash cũng khác baseline expected. Đây là mismatch trước nhiệm vụ, có thêm ảnh hưởng CRLF ở working checkout. Không sửa CSS hoặc snapshot để nhận PASS. Các gate còn lại của npm test chưa chạy trong command đó.

Bốn backend FAIL: `Owner_mapping_is_complete_and_cross_owner_edges_are_explicit`, `Durable_contexts_and_migrations_have_explicit_owner_metadata`, `Layer_and_workflow_dependencies_obey_the_canonical_boundaries`, `Actual_ApiHost_endpoints_obey_route_operation_and_authorization_admission`.

So với baseline mô tả: backend không tăng số FAIL. Frontend lần này dừng sớm hơn baseline 48 gates PASS vì inventory drift; không quy kết drift là regression mới của cleanup, vì tập uncommitted source rộng đã tồn tại trước thao tác và không đổi bytes trong nhiệm vụ. Build và focused tests không phát hiện regression do thay đổi đã commit. Không có E2E browser/connected SQL claim.

Trong export ngoài Git đã sinh thử inventory để hiểu chênh lệch giữa committed source và dirty source. Inventory này **không được copy vào source, không stage, không commit**, các PASS sau regeneration chỉ là diagnostic và không thay thế kết quả gate của cây thật. Không hạ counts/routes/capabilities hoặc đổi threshold để giả PASS.

Logs/TRX đầy đủ nằm trong recovery directory: `backend-test.log`, `test-results/cleanup.trx`, `backend-committed-build.log`, `frontend-verify-before.log`, `frontend-tests.log`, `frontend-build.log`, `frontend-api-check.log`, `frontend-cleanup-gate.log`, `frontend-isolated-*.log`, `frontend-route-smoke.log`, `contact-visual-baseline-diagnosis.json`, `control-*-tests.log`.

## F. Security

Không có gitleaks/trufflehog cài sẵn; không cài scanner mới. Dùng scanner có sẵn frontend (`npm run security:scan`: PASS, 2.600 text files) cùng read-only pattern scan cho tracked + dirty source, staged blobs và toàn bộ changed blob của commit sắp push. Phát hiện high-confidence trên pushed range: **0** (provider tokens, private keys, JWT, Data Protection master keys, database/credential file extensions). `git diff --cached --check` PASS trước từng commit; staged paths đối chiếu allowlist; author lấy từ cấu hình Git có sẵn, không tạo danh tính reviewer.

23 Data Protection key files có trong untracked backend runtime, đã copy/hash ra archive và được ignore, **không stage/push**. Generic password/connection matches trong checkpoint cũ được đối chiếu là environment/runtime expressions hoặc frontend demo identity fixtures; không đưa giá trị vào báo cáo. Không có bằng chứng credential thực đã bị push trong task; không có yêu cầu rotate/revoke phát sinh từ finding đã xác minh. Đây không phải security approval cho phần source/evidence local chưa review.

Các env/local connection config, keys, runtime logs, SQL data/backups, customer/session evidence chưa clearance được giữ local hoặc trong archive; không đưa vào commit mới để làm sạch status. Recovery archive chứa dữ liệu local nhạy cảm, nằm ngoài Git và không được publish. Không sửa/xóa local configuration. Không chạy migration/seed/update SQL Server.

## G. Git Commits

| Repository | Branch | SHA | Message | Changed files |
|---|---|---|---|---:|
| backend | `chore/graduation-source-cleanup-20261029` | `13cd39eba01ebc9dc238f8b0ef98e174c1d4d3f6` | Connected CRM P0: CONDITIONAL_FUNCTIONAL_CHECKPOINT (backend) | 467 |
| backend | `chore/graduation-source-cleanup-20261029` | `e255a4da916f36ccaa62c83c6543fa45a4e7a981` | chore(repo): exclude local runtime artifacts and disposable files | 1 |
| frontend | `feat/deal-compact-kanban-card` | `53927252c5c774d6dbe946728b7696973acff9ec` | chore(repo): ignore local credentials and disposable tool caches | 1 |
| frontend | `feat/deal-compact-kanban-card` | `04cc4524ee98759a9fb5fb6583a97e37a4ce1b4e` | fix(frontend): sanitize remote errors and preserve recovery guidance | 4 |
| frontend | `feat/deal-compact-kanban-card` | `70436019c34ad95626222e873312ccbfa1f9c17a` | test(frontend): handle nullable deal controller in regression harness | 1 |
| control | `chore/graduation-source-cleanup-20261029` | `6ea8544cd4d2f357d622b8019bc44dd349d1d393` | chore(control): preserve review-only contract proposal tooling | 4 |
| control | `chore/graduation-source-cleanup-20261029` | `c196423234b90c4eab8dd7f34dbcdf7b2b721513` | fix(control): preserve extension operations during admission generation | 2 |

Có **6 commit mới**: backend 1, frontend 3, control 2. Backend còn push một commit checkpoint có sẵn, không squash/amend/rebase. New commits không xóa tracked file. Full backend pushed range có các thay đổi/deletions lịch sử thuộc checkpoint cũ; danh sách chính xác lấy từ `prepush-security-and-commits.json` (lưu riêng từng commit), không coi là cleanup deletion.

### Allowlist của các commit mới

- `e255a4da916f36ccaa62c83c6543fa45a4e7a981`: `.gitignore`
- `53927252c5c774d6dbe946728b7696973acff9ec`: `.gitignore`
- `04cc4524ee98759a9fb5fb6583a97e37a4ce1b4e`: `src/shared/operations/backendAvailability.ts`, `src/shared/operations/errorPresentation.ts`, `src/shared/operations/mutationState.ts`, `tests/quality/unit/check-user-facing-error-presentation.mts`
- `70436019c34ad95626222e873312ccbfa1f9c17a`: `tests/contract/deal-quote-transition-outcome.test.mts`
- `6ea8544cd4d2f357d622b8019bc44dd349d1d393`: `tools/inspect_contract_proposal.py`, `tools/stage_contract_candidate.py`, `tools/test_inspect_contract_proposal.py`, `tools/test_stage_contract_candidate.py`
- `c196423234b90c4eab8dd7f34dbcdf7b2b721513`: `tools/generate_operation_admission.py`, `tools/test_generate_operation_admission.py`

## H. Push Results

| Repository | Remote | Branch | Local HEAD = tracking SHA = observed remote HEAD | Result |
|---|---|---|---|---|
| backend | `https://github.com/TUAN131125/unicorecrm-backend.git` | `chore/graduation-source-cleanup-20261029` | `e255a4da916f36ccaa62c83c6543fa45a4e7a981` | **PUSH PASS**, `git ls-remote --heads` verified, ahead/behind 0/0 |
| frontend | `https://github.com/TUAN131125/unicorecrm-web.git` | `feat/deal-compact-kanban-card` | `70436019c34ad95626222e873312ccbfa1f9c17a` | **PUSH PASS**, `git ls-remote --heads` verified, ahead/behind 0/0 |
| control | Không có remote | `chore/graduation-source-cleanup-20261029` | `c196423234b90c4eab8dd7f34dbcdf7b2b721513` (local only) | **NOT PUSHED** — không tự đoán/tạo remote |

Mỗi repository có remote được push đúng một lần. Backend trên branch tích hợp mới, chưa vào master; frontend giữ feature branch hợp lệ, chưa merge default branch. Không force push, không rebase/reset, không bypass CI/security gates.

GitHub Actions: `gh run list` trả `[]` cho cả hai branch ở lần kiểm tra sau push. Không có run URL/status để ghi; **không tuyên bố CI PASS hoặc disabled**.

Control được lưu thêm thành `D:/Project_All/UnicoreCRM-cleanup-recovery-20261029/control-history.bundle`; `git bundle verify` PASS, complete history + 15 refs. SHA-256: `0715d15548c5d79b4882c13a305a95b770e933978047845bd2af0f6b2cb909e4`.

## I. Remaining Work

| Repository | Modified | Deleted có sẵn còn local | Untracked (trước chính report này) | Ignored hiện tại |
|---|---:|---:|---:|---:|
| backend | 96 | 6 | 862 | 7900 |
| frontend | 276 | 1 | 447 | 20476 |
| control | 289 | 2 | 79 | 104 |

Báo cáo này được tạo sau push để ghi đúng kết quả remote; **bản report hiện là thêm một untracked file backend, chưa commit/push**. Không stage toàn bộ phần dirty để đạt status sạch. Trạng thái file chi tiết ở phụ lục bên dưới và `postpush-state.json` ngoài Git.

Cần hoàn tất: review/admission các nhóm CRM/Quote/AI/Communications trước commit; giải quyết inventory drift dựa trên tập source thực được chọn; giữ và đánh giá 14 nhóm protected evidence; giải quyết 4 architecture FAIL; sửa lifecycle teardown của Deal regression harness; giải quyết CSS visual baseline mismatch dựa trên quyết định UI chính thức; review whitespace có sẵn trong bốn frontend files (LeadStatisticsModal, OrderStatisticsDrawer, ReturnListPage, ShippingBookingListPage); chọn remote control nếu chủ dự án muốn lưu lên GitHub. Phần xóa vật lý runtime/build outputs chưa hoàn tất do automatic policy rejection. Những file này vẫn được bảo toàn.

## J. Final Verdict

| Verdict | Kết luận và phạm vi |
|---|---|
| `SOURCE_CLEANUP_PARTIAL` | Đã inventory, snapshot/hash/archive, cải thiện ignore. Xóa vật lý bị chặn; protected duplicates giữ nguyên. |
| `GIT_COMMIT_PARTIAL` | 6 commit mới đã tạo; phần lớn dirty work được bảo toàn ngoài commit vì chưa đủ review/admission. |
| `GITHUB_PUSH_PARTIAL` | Hai GitHub branches có remote đã verified PASS; control không có remote; phần source chưa review và report còn local. |
| `BUILD_REGRESSION_PASS` | Current tree và selected committed source build PASS; source bytes gốc bảo toàn; không phát hiện lỗi mới do cleanup. Không đồng nghĩa full quality PASS. |
| `SECURITY_SCAN_PASS` | Chỉ tập staged/delivered commits đã clearance; local keys/data không push. Phần chưa review không được security-approved. |

## Phụ lục 1 — Archive manifest (copy đã verify, bản gốc chưa xóa)

| Source relative to backend | Bytes | SHA-256 |
|---|---:|---|
| `artifacts/multicurrency/53efb32b891945bb96c60df7e0f5f085/bootstrap.log` | 16465 | `d81276dc6946ebea25e27fd55a95f027ee38b6289c11d95905976e16f8f436b4` |
| `artifacts/multicurrency/53efb32b891945bb96c60df7e0f5f085/browser-error.log` | 352 | `f04220f02f2d684545b0021e360c886b7bb519c88fd1ab84e0d386d610d44766` |
| `artifacts/multicurrency/53efb32b891945bb96c60df7e0f5f085/browser.log` | 191 | `60cda9942019b68eb33178a3b929e6ce909078ba7a238f1bb26d28d379a9a1fd` |
| `artifacts/multicurrency/53efb32b891945bb96c60df7e0f5f085/configure.done` | 6 | `12e5cb1d6521d6a9f0369978cca6ec93c52582896608bf12882764ae21a0c6e4` |
| `artifacts/multicurrency/53efb32b891945bb96c60df7e0f5f085/configure.request` | 17 | `23d38606339fac5ed5d00d47f4cfa63ffe2635176edbd4b8164d0aac7192a5cb` |
| `artifacts/multicurrency/53efb32b891945bb96c60df7e0f5f085/final-sql-state.json` | 669 | `44db77ec91d3d76f7a3d06036fe89e6b8b1488aba04d1e5ed0a410b43e54e2d5` |
| `artifacts/multicurrency/53efb32b891945bb96c60df7e0f5f085/host-error.log` | 0 | `e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855` |
| `artifacts/multicurrency/53efb32b891945bb96c60df7e0f5f085/host.log` | 52425 | `2e3e54f6bbde5ff178554e0242d48857b5eadcd7c88d5d5ddba4bc1810fa3006` |
| `artifacts/multicurrency/53efb32b891945bb96c60df7e0f5f085/http.json` | 5457 | `2a888c26da27fb2ece821d98cb119a8ff35e18d2b5d9c1d52240bce122fd4db1` |
| `artifacts/multicurrency/53efb32b891945bb96c60df7e0f5f085/initial-sql-currency.json` | 269 | `745ad87cdccee2a8691b1d59c3fbec5a3abc11063c547d0c19849db41e801bbc` |
| `artifacts/multicurrency/53efb32b891945bb96c60df7e0f5f085/migration.log` | 396704 | `a7309b0248f60020456a0612eb828d2e4256a9f737fca548c512f9e23e156299` |
| `artifacts/multicurrency/53efb32b891945bb96c60df7e0f5f085/no-studio.done` | 6 | `12e5cb1d6521d6a9f0369978cca6ec93c52582896608bf12882764ae21a0c6e4` |
| `artifacts/multicurrency/53efb32b891945bb96c60df7e0f5f085/no-studio.request` | 17 | `23d38606339fac5ed5d00d47f4cfa63ffe2635176edbd4b8164d0aac7192a5cb` |
| `artifacts/multicurrency/53efb32b891945bb96c60df7e0f5f085/product-currency-sql.json` | 233 | `b37422d13b17f40140b7a977eaad7830c9c7e24b00ab16e054071f141295e109` |
| `artifacts/multicurrency/53efb32b891945bb96c60df7e0f5f085/read-only.done` | 6 | `12e5cb1d6521d6a9f0369978cca6ec93c52582896608bf12882764ae21a0c6e4` |
| `artifacts/multicurrency/53efb32b891945bb96c60df7e0f5f085/read-only.request` | 17 | `23d38606339fac5ed5d00d47f4cfa63ffe2635176edbd4b8164d0aac7192a5cb` |
| `artifacts/multicurrency/53efb32b891945bb96c60df7e0f5f085/restart.done` | 6 | `12e5cb1d6521d6a9f0369978cca6ec93c52582896608bf12882764ae21a0c6e4` |
| `artifacts/multicurrency/53efb32b891945bb96c60df7e0f5f085/restart.request` | 17 | `23d38606339fac5ed5d00d47f4cfa63ffe2635176edbd4b8164d0aac7192a5cb` |
| `artifacts/multicurrency/53efb32b891945bb96c60df7e0f5f085/results.json` | 6271 | `c5825fc26304ee59b9645bb16d241d11bee2a4fb91db393ee2dd73cba74dd993` |
| `artifacts/multicurrency/53efb32b891945bb96c60df7e0f5f085/run.json` | 399 | `f327b86d98eb814fba0c068c2289a8175805fc492ad5a0c0e88175c7706d0d87` |
| `artifacts/multicurrency/53efb32b891945bb96c60df7e0f5f085/stop.done` | 6 | `12e5cb1d6521d6a9f0369978cca6ec93c52582896608bf12882764ae21a0c6e4` |
| `artifacts/multicurrency/53efb32b891945bb96c60df7e0f5f085/stop.request` | 17 | `23d38606339fac5ed5d00d47f4cfa63ffe2635176edbd4b8164d0aac7192a5cb` |
| `artifacts/multicurrency/53efb32b891945bb96c60df7e0f5f085/browser/.last-run.json` | 45 | `91d1c43004802cd49950d78eb11c8fa7d05da8ffffe219a8b13b2f561bc00903` |
| `artifacts/multicurrency/53efb32b891945bb96c60df7e0f5f085/browser/connected-product-currency.png` | 135977 | `958547d02cb0ed8742be083010f3322f83abd44fb6f2ea6c7883b1eb8819ecea` |
| `artifacts/multicurrency/53efb32b891945bb96c60df7e0f5f085/browser/currency-results.json` | 18409 | `5706813a4e0b19aad727b3664464d6c5eae75bea0f3c9c23d23b1a07dcbede7e` |
| `artifacts/multicurrency/53efb32b891945bb96c60df7e0f5f085/browser/first-visit.png` | 321204 | `4f576d569e69ec1ef6a851b4b0ccd44e362080fa68927cccf681a0693b6a477a` |
| `artifacts/multicurrency/53efb32b891945bb96c60df7e0f5f085/keys/key-b0e2a1ae-85bd-4171-9bc9-d3d863819f3b.xml` | 1016 | `07e039b28bd89effb83db21f087e8620b001b2aa1a352cb84256e5adb873926a` |
| `artifacts/multicurrency/6ae2e8bfe6cb4e3291cd45cd3572eb96/bootstrap.log` | 16465 | `badbf8570a24d0302c41365895ba4a60a9fecfe93054705a3d6ad16d75d9f5a9` |
| `artifacts/multicurrency/6ae2e8bfe6cb4e3291cd45cd3572eb96/browser-error.log` | 352 | `3a00f9a9813e7c58062e48b492974fa0cbd335e750d0dddc9d3618d26e200532` |
| `artifacts/multicurrency/6ae2e8bfe6cb4e3291cd45cd3572eb96/browser.log` | 2867 | `22f8232bcb602d7e2f512a9b475aa21d3999c0a378e1f4e4364f65591f96ab57` |
| `artifacts/multicurrency/6ae2e8bfe6cb4e3291cd45cd3572eb96/final-sql-state.json` | 617 | `eabcaada3813ff8c62b00d3b697212587e949f4a64abbe5c87ecc8f79f63758c` |
| `artifacts/multicurrency/6ae2e8bfe6cb4e3291cd45cd3572eb96/host-error.log` | 0 | `e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855` |
| `artifacts/multicurrency/6ae2e8bfe6cb4e3291cd45cd3572eb96/host.log` | 639073 | `f3c6aa176bb88c5cb87f7e209db5cb47c5a6a988b972e042894fc1c3ceb9edc4` |
| `artifacts/multicurrency/6ae2e8bfe6cb4e3291cd45cd3572eb96/http.json` | 2955 | `6bdd06c9e1e89faef5b9dcfc3420824ada2e2c046be21d9dc29687e442484f77` |
| `artifacts/multicurrency/6ae2e8bfe6cb4e3291cd45cd3572eb96/initial-sql-currency.json` | 269 | `0ad76e4f6d0e35d24fe8e76879b7e7a9d4678db070112080d8d3c28fb4c88218` |
| `artifacts/multicurrency/6ae2e8bfe6cb4e3291cd45cd3572eb96/migration.log` | 396707 | `2a4d156576ee79aa0b9d0a0f03846aa1204dab4115e9f760f2d2ea1e8eef4097` |
| `artifacts/multicurrency/6ae2e8bfe6cb4e3291cd45cd3572eb96/results.json` | 1972 | `0f85d5e09ed783f5e060a9fa5ba182c33a0426f87c2dac09b3745d82bbe05310` |
| `artifacts/multicurrency/6ae2e8bfe6cb4e3291cd45cd3572eb96/run.json` | 399 | `129d1adf529b1a8fe9a76103e243eb6ad0a2802a8b5cd8f47ef214170721dc16` |
| `artifacts/multicurrency/6ae2e8bfe6cb4e3291cd45cd3572eb96/browser/.last-run.json` | 96 | `e115f14270d9a82f15ac5d61ab6d160641be5d975f92e13fe79e0e8ad5d39619` |
| `artifacts/multicurrency/6ae2e8bfe6cb4e3291cd45cd3572eb96/browser/currency-results.json` | 9690 | `4b8172120ea337313869cedd6530ce4f0f38404fce5b2346f86f73013cc8369f` |
| `artifacts/multicurrency/6ae2e8bfe6cb4e3291cd45cd3572eb96/browser/first-visit.png` | 365510 | `36a90a54ddc9a145ee7012a85d217756842303265b486d7ca4b4d95c264ff24d` |
| `artifacts/multicurrency/6ae2e8bfe6cb4e3291cd45cd3572eb96/browser/test-failed-1.png` | 4257 | `74f05932c7848edf4192607a3d825ea9e993e46352d8a542f87b5ba785b6fa9d` |
| `artifacts/multicurrency/6ae2e8bfe6cb4e3291cd45cd3572eb96/browser/test-failed-2.png` | 106280 | `bb46b4deaf66b9db9bbe3419a0501a1d3884bf02fd21f49b1fac40fde82f09e9` |
| `artifacts/multicurrency/6ae2e8bfe6cb4e3291cd45cd3572eb96/keys/key-2882328d-4de9-4237-a9b7-3d7538f3cc15.xml` | 1016 | `7812093e8088a584ff19f6a1fd981d5298b5d7aee1a744df36a71aff5b6a82c2` |
| `artifacts/multicurrency/90f5c5cdc4c640e88fee14cf819d7fe2/bootstrap.log` | 16464 | `f4bc6d4c2f0080cc8bf270f67b922241546bece1dceb89df5f82cb41154d666e` |
| `artifacts/multicurrency/90f5c5cdc4c640e88fee14cf819d7fe2/browser-error.log` | 352 | `76a8b37953f54bb8d3c0cd76c455a92f08888f8d74611f35e5395a837b9db226` |
| `artifacts/multicurrency/90f5c5cdc4c640e88fee14cf819d7fe2/browser.log` | 2041 | `e5ef8264f30fde2256a30010b7ce2c7805bd1ed17fc01e0679665e13599404d6` |
| `artifacts/multicurrency/90f5c5cdc4c640e88fee14cf819d7fe2/final-sql-state.json` | 503 | `549534d94d6e2d0823d122d97c5f8159d1abfa303869388da216dd0b48005948` |
| `artifacts/multicurrency/90f5c5cdc4c640e88fee14cf819d7fe2/host-error.log` | 0 | `e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855` |
| `artifacts/multicurrency/90f5c5cdc4c640e88fee14cf819d7fe2/host.log` | 333175 | `3d58605264cae860787bc9e6f8cf608de63354ff77ce18df1de49b6775e63de3` |
| `artifacts/multicurrency/90f5c5cdc4c640e88fee14cf819d7fe2/http.json` | 2955 | `468b2f74ce27656afd48cc8e19579c46d891f3fca7c898d09a18b7f3211060d3` |
| `artifacts/multicurrency/90f5c5cdc4c640e88fee14cf819d7fe2/initial-sql-currency.json` | 269 | `c69a945c2c480d84c6ec2d93c94c7cd6a02001e48c19113ab86df249f6a52762` |
| `artifacts/multicurrency/90f5c5cdc4c640e88fee14cf819d7fe2/migration.log` | 396706 | `64eb28aafdaa6a0e9250d849190071dc4ee3baa3ee20b1d595fff30162367851` |
| `artifacts/multicurrency/90f5c5cdc4c640e88fee14cf819d7fe2/results.json` | 1210 | `2b0a8d1f90a9cb846632d05bd15d2f4abd4b8dce409b69af03b1c9e5f19a928f` |
| `artifacts/multicurrency/90f5c5cdc4c640e88fee14cf819d7fe2/run.json` | 399 | `4025b27016a49145ee90005cd3f73e38e561190ce37d907b32b99890a06fad8e` |
| `artifacts/multicurrency/90f5c5cdc4c640e88fee14cf819d7fe2/browser/.last-run.json` | 96 | `e115f14270d9a82f15ac5d61ab6d160641be5d975f92e13fe79e0e8ad5d39619` |
| `artifacts/multicurrency/90f5c5cdc4c640e88fee14cf819d7fe2/browser/currency-results.json` | 636 | `6ab7407551a6cf82c8e89bc209a2488ca6199dc777baaa8aff13b36f81d35a7a` |
| `artifacts/multicurrency/90f5c5cdc4c640e88fee14cf819d7fe2/browser/test-failed-1.png` | 86842 | `e4acad958c2904d342cb69c897b45d76dfebbe35ce9293539d623ed13e630417` |
| `artifacts/multicurrency/90f5c5cdc4c640e88fee14cf819d7fe2/keys/key-81124bb1-0555-4048-b07b-353afcefe88f.xml` | 1016 | `754bc41bf349b76289aff4384f147371e26190c8ee7323bb5b7c38caa6f267cd` |
| `artifacts/multicurrency/91e7457b89574c4bbfe73b91903e92a9/bootstrap.log` | 16465 | `ccf2823627d3f466fea8f7ef49ac79f69217c1c724ccc1ec0d8d88655db4684f` |
| `artifacts/multicurrency/91e7457b89574c4bbfe73b91903e92a9/browser-error.log` | 351 | `6636d1b316056156d96e05d57e55c2f2386b198616b078a8c20039ad4b101271` |
| `artifacts/multicurrency/91e7457b89574c4bbfe73b91903e92a9/browser.log` | 4908 | `bcda00389b2a55d14cde602615c57d7690e02ce1410a436af9e2ab6e8c4420e4` |
| `artifacts/multicurrency/91e7457b89574c4bbfe73b91903e92a9/configure.done` | 6 | `12e5cb1d6521d6a9f0369978cca6ec93c52582896608bf12882764ae21a0c6e4` |
| `artifacts/multicurrency/91e7457b89574c4bbfe73b91903e92a9/configure.request` | 17 | `23d38606339fac5ed5d00d47f4cfa63ffe2635176edbd4b8164d0aac7192a5cb` |
| `artifacts/multicurrency/91e7457b89574c4bbfe73b91903e92a9/final-sql-state.json` | 669 | `d8c3d858a4cfd679b9477df40fee38c4d1c545b96546612577f52db2bed0ddf1` |
| `artifacts/multicurrency/91e7457b89574c4bbfe73b91903e92a9/host-error.log` | 0 | `e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855` |
| `artifacts/multicurrency/91e7457b89574c4bbfe73b91903e92a9/host.log` | 21425 | `ba793c5204e8e4f573fd3366a38d753cefa7cf54edd952d0a7361db34c97399c` |
| `artifacts/multicurrency/91e7457b89574c4bbfe73b91903e92a9/http.json` | 3385 | `24cd0e33da1a48364f28b354b9941a67fdd62fa5aed7ee7c1eaa7b4b36acd188` |
| `artifacts/multicurrency/91e7457b89574c4bbfe73b91903e92a9/initial-sql-currency.json` | 269 | `31b61d3e49330ad4ee510b7cf23b6c39a0faa10b5562527421c9e4ccf0d3b242` |
| `artifacts/multicurrency/91e7457b89574c4bbfe73b91903e92a9/migration.log` | 396705 | `01339158d7d452d0f2de5a5fa196da579ec8c817f2bb33bb6f9858b01bab3629` |
| `artifacts/multicurrency/91e7457b89574c4bbfe73b91903e92a9/no-studio.done` | 6 | `12e5cb1d6521d6a9f0369978cca6ec93c52582896608bf12882764ae21a0c6e4` |
| `artifacts/multicurrency/91e7457b89574c4bbfe73b91903e92a9/no-studio.request` | 17 | `23d38606339fac5ed5d00d47f4cfa63ffe2635176edbd4b8164d0aac7192a5cb` |
| `artifacts/multicurrency/91e7457b89574c4bbfe73b91903e92a9/read-only.done` | 6 | `12e5cb1d6521d6a9f0369978cca6ec93c52582896608bf12882764ae21a0c6e4` |
| `artifacts/multicurrency/91e7457b89574c4bbfe73b91903e92a9/read-only.request` | 17 | `23d38606339fac5ed5d00d47f4cfa63ffe2635176edbd4b8164d0aac7192a5cb` |
| `artifacts/multicurrency/91e7457b89574c4bbfe73b91903e92a9/restart.done` | 6 | `12e5cb1d6521d6a9f0369978cca6ec93c52582896608bf12882764ae21a0c6e4` |
| `artifacts/multicurrency/91e7457b89574c4bbfe73b91903e92a9/restart.request` | 17 | `23d38606339fac5ed5d00d47f4cfa63ffe2635176edbd4b8164d0aac7192a5cb` |
| `artifacts/multicurrency/91e7457b89574c4bbfe73b91903e92a9/results.json` | 4248 | `4bb8f7cf7d93b925b83b1adb80cc3ab1297485ea922a64a22622272d7e8de3fb` |
| `artifacts/multicurrency/91e7457b89574c4bbfe73b91903e92a9/run.json` | 399 | `7576f79a0d1e9e3664aae49f1dbc05e0a319a596073aef3ad3d6439588fb1816` |
| `artifacts/multicurrency/91e7457b89574c4bbfe73b91903e92a9/stop.done` | 6 | `12e5cb1d6521d6a9f0369978cca6ec93c52582896608bf12882764ae21a0c6e4` |
| `artifacts/multicurrency/91e7457b89574c4bbfe73b91903e92a9/stop.request` | 17 | `23d38606339fac5ed5d00d47f4cfa63ffe2635176edbd4b8164d0aac7192a5cb` |
| `artifacts/multicurrency/91e7457b89574c4bbfe73b91903e92a9/browser/.last-run.json` | 96 | `e115f14270d9a82f15ac5d61ab6d160641be5d975f92e13fe79e0e8ad5d39619` |
| `artifacts/multicurrency/91e7457b89574c4bbfe73b91903e92a9/browser/connected-product-currency.png` | 139066 | `d2351733af7f49f74d5adda884d09353e665f42ebce7201adc7ed90216ed0c36` |
| `artifacts/multicurrency/91e7457b89574c4bbfe73b91903e92a9/browser/currency-results.json` | 17683 | `681d288dbf77db663029582935e2eb1e3b5c7b3e4b2c271d21123734c48a684e` |
| `artifacts/multicurrency/91e7457b89574c4bbfe73b91903e92a9/browser/first-visit.png` | 404981 | `583898e0e36ec50cbe334c087082cd6c213d707926cba8547a99d71d5284ff31` |
| `artifacts/multicurrency/91e7457b89574c4bbfe73b91903e92a9/browser/test-failed-1.png` | 4257 | `74f05932c7848edf4192607a3d825ea9e993e46352d8a542f87b5ba785b6fa9d` |
| `artifacts/multicurrency/91e7457b89574c4bbfe73b91903e92a9/browser/test-failed-2.png` | 106280 | `bb46b4deaf66b9db9bbe3419a0501a1d3884bf02fd21f49b1fac40fde82f09e9` |
| `artifacts/multicurrency/91e7457b89574c4bbfe73b91903e92a9/browser/test-failed-3.png` | 106280 | `bb46b4deaf66b9db9bbe3419a0501a1d3884bf02fd21f49b1fac40fde82f09e9` |
| `artifacts/multicurrency/91e7457b89574c4bbfe73b91903e92a9/browser/test-failed-4.png` | 101538 | `9313110121c6e137d539b8350f67d7e4f4ffc24ec5eb943fec16ee39e28271b3` |
| `artifacts/multicurrency/91e7457b89574c4bbfe73b91903e92a9/browser/test-failed-5.png` | 4257 | `74f05932c7848edf4192607a3d825ea9e993e46352d8a542f87b5ba785b6fa9d` |
| `artifacts/multicurrency/91e7457b89574c4bbfe73b91903e92a9/keys/key-7f6e9857-9647-4178-8d2e-a95ac2591dd2.xml` | 1016 | `293ca45aaa0bd4e1f578efce848e086a291b42180a006654ce28744a31d3545b` |
| `artifacts/multicurrency/96d77eabaa0240bbb3ea5a58cb9de513/bootstrap.log` | 16464 | `70edce01b9735647a9ac9195b592073ada55507779f6d3f519dc7e5438183efb` |
| `artifacts/multicurrency/96d77eabaa0240bbb3ea5a58cb9de513/browser-error.log` | 352 | `fcc558e1aaed52fd0a27facb46e68a71edae11baa41002dd5a69d2bb6b182333` |
| `artifacts/multicurrency/96d77eabaa0240bbb3ea5a58cb9de513/browser.log` | 2848 | `c14670813c98d41f7b399820b0ed97ed39e2365e538ddbe6452bdf8cec0b208c` |
| `artifacts/multicurrency/96d77eabaa0240bbb3ea5a58cb9de513/final-sql-state.json` | 555 | `4e84755cb640e9de84b18c0a0f2d60719bf17330287cd816c94ddad2620645d9` |
| `artifacts/multicurrency/96d77eabaa0240bbb3ea5a58cb9de513/host-error.log` | 0 | `e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855` |
| `artifacts/multicurrency/96d77eabaa0240bbb3ea5a58cb9de513/host.log` | 677514 | `2e7a6f2997ae18edc1f27ff14508c061689a9619c3dc1646c935e1d1391347d9` |
| `artifacts/multicurrency/96d77eabaa0240bbb3ea5a58cb9de513/http.json` | 2955 | `ef6e197c737a36812aa54cb5f3e8465ff0c4f77adc6260dd816c7fb892cd2a82` |
| `artifacts/multicurrency/96d77eabaa0240bbb3ea5a58cb9de513/initial-sql-currency.json` | 269 | `eae1757c911453480e550d64fb51def03b0f373094d1a61403508f9ea7555efc` |
| `artifacts/multicurrency/96d77eabaa0240bbb3ea5a58cb9de513/migration.log` | 396705 | `efe6eb47734a6f2dad8534b155f64f6d6e06b43fc89e91d54243850c815d6017` |
| `artifacts/multicurrency/96d77eabaa0240bbb3ea5a58cb9de513/results.json` | 1597 | `246c027bf756c344475cb6edf1db8cb6c613a813570d5a9864da2222a6985429` |
| `artifacts/multicurrency/96d77eabaa0240bbb3ea5a58cb9de513/run.json` | 399 | `ce979fd8e18386badfe42c6bbb0dc579bcad95c7dfe17bc4748a66c93b3cd572` |
| `artifacts/multicurrency/96d77eabaa0240bbb3ea5a58cb9de513/browser/.last-run.json` | 96 | `e115f14270d9a82f15ac5d61ab6d160641be5d975f92e13fe79e0e8ad5d39619` |
| `artifacts/multicurrency/96d77eabaa0240bbb3ea5a58cb9de513/browser/currency-results.json` | 5426 | `3a108827f6589c5f3b0186ecb7ab0eb9a8a4bb18f9a82fc8b689e9926200a399` |
| `artifacts/multicurrency/96d77eabaa0240bbb3ea5a58cb9de513/browser/first-visit.png` | 481248 | `8d0438950e6fa5de1d8c7af681cfbff8ac58591dc64960c81ecf8390fd17db1c` |
| `artifacts/multicurrency/96d77eabaa0240bbb3ea5a58cb9de513/browser/test-failed-1.png` | 106280 | `bb46b4deaf66b9db9bbe3419a0501a1d3884bf02fd21f49b1fac40fde82f09e9` |
| `artifacts/multicurrency/96d77eabaa0240bbb3ea5a58cb9de513/browser/test-failed-2.png` | 367210 | `aa32fa41c36ed5bcaf69ba1d4c628cea36351a3a41ec1e4a777d923c271b22ca` |
| `artifacts/multicurrency/96d77eabaa0240bbb3ea5a58cb9de513/keys/key-dd9b9401-27c2-4fe5-b588-4ed8c2077fee.xml` | 1016 | `8e9a509406267f5e1e802e7e0d6a4132964dfc9b53dac51348edeff3a819fe56` |
| `artifacts/multicurrency/9eb7d4783b6547a5b750f683205f3361/bootstrap.log` | 16464 | `8b924b75931fc2885e1568ab31f00bb2437469d2b87984e6dd88d71c65ebba09` |
| `artifacts/multicurrency/9eb7d4783b6547a5b750f683205f3361/browser-error.log` | 352 | `303f5f56a75b00c8a2ff32d2ad75440ef8828017f04265e216052e34a5a36e11` |
| `artifacts/multicurrency/9eb7d4783b6547a5b750f683205f3361/browser.log` | 2837 | `20984571d74b274b2d87923dad6d197f6d993be992887574d567caba7f4b0766` |
| `artifacts/multicurrency/9eb7d4783b6547a5b750f683205f3361/final-sql-state.json` | 617 | `9d3a2d73e4c6e8e071f9a1223441f41d0507bf9431d9b3ee603ed7b1bb08dcc1` |
| `artifacts/multicurrency/9eb7d4783b6547a5b750f683205f3361/host-error.log` | 0 | `e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855` |
| `artifacts/multicurrency/9eb7d4783b6547a5b750f683205f3361/host.log` | 777870 | `51934ae4ad706355ecbacdd85ccde5c92489d2dd6470a529ac4f137132d1e39c` |
| `artifacts/multicurrency/9eb7d4783b6547a5b750f683205f3361/http.json` | 2955 | `a9d3b3a9bde0120088ea3951a5c6940af34451f6dbf0ef5f3fb0c315fd17d176` |
| `artifacts/multicurrency/9eb7d4783b6547a5b750f683205f3361/initial-sql-currency.json` | 269 | `dbdca42c3aa69b4dbfb34e0fbcf0f7e8dc25f58790d22afbf8e3339ff0e930e8` |
| `artifacts/multicurrency/9eb7d4783b6547a5b750f683205f3361/migration.log` | 396707 | `a1cc64479ab5469bfdc95399057974173b2d821e3a0e1b220d1da0ded18aa608` |
| `artifacts/multicurrency/9eb7d4783b6547a5b750f683205f3361/results.json` | 2359 | `6a5a13478df25cd02f577ce9861ffa7f938d63ae2afca1625d0556b7cb5896c6` |
| `artifacts/multicurrency/9eb7d4783b6547a5b750f683205f3361/run.json` | 399 | `a2c3a945629db8764dc651913a197fce49c80839bf72dcc207c5f06f1a2d5951` |
| `artifacts/multicurrency/9eb7d4783b6547a5b750f683205f3361/browser/.last-run.json` | 96 | `e115f14270d9a82f15ac5d61ab6d160641be5d975f92e13fe79e0e8ad5d39619` |
| `artifacts/multicurrency/9eb7d4783b6547a5b750f683205f3361/browser/currency-results.json` | 10107 | `e4d3f651fa3362c314e9fce67272ffb274a5eb9d9f9563f19c209f826ca040c6` |
| `artifacts/multicurrency/9eb7d4783b6547a5b750f683205f3361/browser/first-visit.png` | 368252 | `bc73964c7d7a79e0a6a6baa901940c79d026f945116c82d2ef23a0a92fbefa8e` |
| `artifacts/multicurrency/9eb7d4783b6547a5b750f683205f3361/browser/test-failed-1.png` | 86068 | `62fa0e873a47f16c1d2a6523635ea5a48316a2a91ee513a9b57361abdc97eb92` |
| `artifacts/multicurrency/9eb7d4783b6547a5b750f683205f3361/browser/test-failed-2.png` | 106280 | `bb46b4deaf66b9db9bbe3419a0501a1d3884bf02fd21f49b1fac40fde82f09e9` |
| `artifacts/multicurrency/9eb7d4783b6547a5b750f683205f3361/keys/key-84acd9ac-367e-40e8-a34e-98d4c4bff848.xml` | 1016 | `694a29273ee2ec3f76d296dd3a09ab95daa4334878e48f2bf3fd40f0f598f6b6` |
| `artifacts/multicurrency/bf782ddac5024ece9f670f9be38efcdd/bootstrap.log` | 16464 | `0cf012684fd5de7ef8cf8fac06ebb86565f341edda78ce2af1ed44e1940d7bc6` |
| `artifacts/multicurrency/bf782ddac5024ece9f670f9be38efcdd/browser-error.log` | 352 | `075c194a118af0d68b24520e37f070c3420718cfe944c4229b41eb4ac96b60ec` |
| `artifacts/multicurrency/bf782ddac5024ece9f670f9be38efcdd/browser.log` | 4333 | `c9839179124eae6de4ed1ceb02ec396ca44e1face68177d260f2eef007dcd010` |
| `artifacts/multicurrency/bf782ddac5024ece9f670f9be38efcdd/configure.done` | 6 | `12e5cb1d6521d6a9f0369978cca6ec93c52582896608bf12882764ae21a0c6e4` |
| `artifacts/multicurrency/bf782ddac5024ece9f670f9be38efcdd/configure.request` | 17 | `23d38606339fac5ed5d00d47f4cfa63ffe2635176edbd4b8164d0aac7192a5cb` |
| `artifacts/multicurrency/bf782ddac5024ece9f670f9be38efcdd/final-sql-state.json` | 669 | `d52094d45ae532ed824989080abc7bd1b8e014c23c4574d21d15f2e7f2b98e2f` |
| `artifacts/multicurrency/bf782ddac5024ece9f670f9be38efcdd/host-error.log` | 0 | `e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855` |
| `artifacts/multicurrency/bf782ddac5024ece9f670f9be38efcdd/host.log` | 21425 | `e4836e164c862cdbadd548db1f9c9238eb4a2bb8dcecb80785d43e197d1ea97a` |
| `artifacts/multicurrency/bf782ddac5024ece9f670f9be38efcdd/http.json` | 3385 | `9542ffc867033896496520a25143323f287072b54c3702d5e7d1d43710af5f65` |
| `artifacts/multicurrency/bf782ddac5024ece9f670f9be38efcdd/initial-sql-currency.json` | 269 | `8645879cfd48e4e3c215df7402519be9b24537e37969b55878885f72d6896811` |
| `artifacts/multicurrency/bf782ddac5024ece9f670f9be38efcdd/migration.log` | 396706 | `91212bc6553152bfc7c7389c375bc7b9ea1df2044e43e53e2b76f0ed34778240` |
| `artifacts/multicurrency/bf782ddac5024ece9f670f9be38efcdd/no-studio.done` | 6 | `12e5cb1d6521d6a9f0369978cca6ec93c52582896608bf12882764ae21a0c6e4` |
| `artifacts/multicurrency/bf782ddac5024ece9f670f9be38efcdd/no-studio.request` | 17 | `23d38606339fac5ed5d00d47f4cfa63ffe2635176edbd4b8164d0aac7192a5cb` |
| `artifacts/multicurrency/bf782ddac5024ece9f670f9be38efcdd/read-only.done` | 6 | `12e5cb1d6521d6a9f0369978cca6ec93c52582896608bf12882764ae21a0c6e4` |
| `artifacts/multicurrency/bf782ddac5024ece9f670f9be38efcdd/read-only.request` | 17 | `23d38606339fac5ed5d00d47f4cfa63ffe2635176edbd4b8164d0aac7192a5cb` |
| `artifacts/multicurrency/bf782ddac5024ece9f670f9be38efcdd/restart.done` | 6 | `12e5cb1d6521d6a9f0369978cca6ec93c52582896608bf12882764ae21a0c6e4` |
| `artifacts/multicurrency/bf782ddac5024ece9f670f9be38efcdd/restart.request` | 17 | `23d38606339fac5ed5d00d47f4cfa63ffe2635176edbd4b8164d0aac7192a5cb` |
| `artifacts/multicurrency/bf782ddac5024ece9f670f9be38efcdd/results.json` | 4635 | `2d6a622ac19e068cbf63ca56b2fb183b3c0b24794910bab7cccf3be1bef7df02` |
| `artifacts/multicurrency/bf782ddac5024ece9f670f9be38efcdd/run.json` | 399 | `62a97c493bb6571e3e72cb1aa3eb7a3b7ae03ebedfdf0b062bfad50ad42e7eae` |
| `artifacts/multicurrency/bf782ddac5024ece9f670f9be38efcdd/stop.done` | 6 | `12e5cb1d6521d6a9f0369978cca6ec93c52582896608bf12882764ae21a0c6e4` |
| `artifacts/multicurrency/bf782ddac5024ece9f670f9be38efcdd/stop.request` | 17 | `23d38606339fac5ed5d00d47f4cfa63ffe2635176edbd4b8164d0aac7192a5cb` |
| `artifacts/multicurrency/bf782ddac5024ece9f670f9be38efcdd/browser/.last-run.json` | 96 | `e115f14270d9a82f15ac5d61ab6d160641be5d975f92e13fe79e0e8ad5d39619` |
| `artifacts/multicurrency/bf782ddac5024ece9f670f9be38efcdd/browser/connected-product-currency.png` | 127699 | `03f1b5dde991bd8818b726a75ef4a5971dd421111e1310164fba575a4a9e56f8` |
| `artifacts/multicurrency/bf782ddac5024ece9f670f9be38efcdd/browser/currency-results.json` | 18442 | `0269dea964914f9af8ca45decd8869e6798641c4d83e1b5a1ce829497bb74114` |
| `artifacts/multicurrency/bf782ddac5024ece9f670f9be38efcdd/browser/first-visit.png` | 363868 | `1db7ea68553da8d9f6e2af294b6d368436f9ac4450ef97390345169f282bcd41` |
| `artifacts/multicurrency/bf782ddac5024ece9f670f9be38efcdd/browser/test-failed-1.png` | 113619 | `b2fdfa4086bf83b3ba51e879aa5903df763f6499b1f0f441fe421ed12e5ded76` |
| `artifacts/multicurrency/bf782ddac5024ece9f670f9be38efcdd/browser/test-failed-2.png` | 106280 | `bb46b4deaf66b9db9bbe3419a0501a1d3884bf02fd21f49b1fac40fde82f09e9` |
| `artifacts/multicurrency/bf782ddac5024ece9f670f9be38efcdd/browser/test-failed-3.png` | 106280 | `bb46b4deaf66b9db9bbe3419a0501a1d3884bf02fd21f49b1fac40fde82f09e9` |
| `artifacts/multicurrency/bf782ddac5024ece9f670f9be38efcdd/browser/test-failed-4.png` | 101538 | `9313110121c6e137d539b8350f67d7e4f4ffc24ec5eb943fec16ee39e28271b3` |
| `artifacts/multicurrency/bf782ddac5024ece9f670f9be38efcdd/browser/test-failed-5.png` | 17100 | `6d21523ecd10dde85645d350e9c6c78ff9f7abaee388f65e0f3d24cc48905a8c` |
| `artifacts/multicurrency/bf782ddac5024ece9f670f9be38efcdd/keys/key-90bd4ae4-ab71-464d-8b3b-a4a9b41d197a.xml` | 1016 | `71dd4ac66331de81333e30b0cd08a131ced61ffe4371e1a416274937f79afd3c` |
| `artifacts/multicurrency/dc5d2f4e8a324610af37df8c5ff5515f/bootstrap.log` | 16465 | `7c9ac56514d638856f6abb2efcab2bf62d1dcee8cb3201dfb675be80c067cece` |
| `artifacts/multicurrency/dc5d2f4e8a324610af37df8c5ff5515f/browser-error.log` | 352 | `107e9f9c08eb3470213c90319393a2d5a5c1a7138217c3ad92ee0f300cbcb53e` |
| `artifacts/multicurrency/dc5d2f4e8a324610af37df8c5ff5515f/browser.log` | 191 | `d8ca2feb296ef75ee307f35cd6f814ea717f890df1de67b9a0be44dc1793a80b` |
| `artifacts/multicurrency/dc5d2f4e8a324610af37df8c5ff5515f/configure.done` | 6 | `12e5cb1d6521d6a9f0369978cca6ec93c52582896608bf12882764ae21a0c6e4` |
| `artifacts/multicurrency/dc5d2f4e8a324610af37df8c5ff5515f/configure.request` | 17 | `23d38606339fac5ed5d00d47f4cfa63ffe2635176edbd4b8164d0aac7192a5cb` |
| `artifacts/multicurrency/dc5d2f4e8a324610af37df8c5ff5515f/final-sql-state.json` | 669 | `8e7481b5bc7f720c6de1045d0a6f18c8905a75181b7984b96247dce79fb44297` |
| `artifacts/multicurrency/dc5d2f4e8a324610af37df8c5ff5515f/host-error.log` | 0 | `e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855` |
| `artifacts/multicurrency/dc5d2f4e8a324610af37df8c5ff5515f/host.log` | 71758 | `9b4625473ebb38a9aae812f18f22c7f8d10b4681f2a6d9ca5e52ae3d823b87dd` |
| `artifacts/multicurrency/dc5d2f4e8a324610af37df8c5ff5515f/http.json` | 5457 | `988e8a33802cc40a87552414946238bb049db20278d2d8dd31a86c935567ab5b` |
| `artifacts/multicurrency/dc5d2f4e8a324610af37df8c5ff5515f/initial-sql-currency.json` | 269 | `5feec3ff156c4c81d12423412376bcd2b1f15c302a40bfe89c7661b3f655d9e9` |
| `artifacts/multicurrency/dc5d2f4e8a324610af37df8c5ff5515f/lead-customer-sql.json` | 294 | `8d501899715d31d2df6bb1cda0e9874b35dcfb64a6eaf177145583e7ab7cb5e0` |
| `artifacts/multicurrency/dc5d2f4e8a324610af37df8c5ff5515f/migration.log` | 396713 | `96f9fc0d70cfb4aedc8c180320962ac92a46e893663bd00878fa85a2e12dac5b` |
| `artifacts/multicurrency/dc5d2f4e8a324610af37df8c5ff5515f/no-studio.done` | 6 | `12e5cb1d6521d6a9f0369978cca6ec93c52582896608bf12882764ae21a0c6e4` |
| `artifacts/multicurrency/dc5d2f4e8a324610af37df8c5ff5515f/no-studio.request` | 17 | `23d38606339fac5ed5d00d47f4cfa63ffe2635176edbd4b8164d0aac7192a5cb` |
| `artifacts/multicurrency/dc5d2f4e8a324610af37df8c5ff5515f/product-currency-sql.json` | 233 | `3183496b2371fe52a53fdbfc03507ffce540b47ddae9e67ba2d6416f606368ad` |
| `artifacts/multicurrency/dc5d2f4e8a324610af37df8c5ff5515f/read-only.done` | 6 | `12e5cb1d6521d6a9f0369978cca6ec93c52582896608bf12882764ae21a0c6e4` |
| `artifacts/multicurrency/dc5d2f4e8a324610af37df8c5ff5515f/read-only.request` | 17 | `23d38606339fac5ed5d00d47f4cfa63ffe2635176edbd4b8164d0aac7192a5cb` |
| `artifacts/multicurrency/dc5d2f4e8a324610af37df8c5ff5515f/restart.done` | 6 | `12e5cb1d6521d6a9f0369978cca6ec93c52582896608bf12882764ae21a0c6e4` |
| `artifacts/multicurrency/dc5d2f4e8a324610af37df8c5ff5515f/restart.request` | 17 | `23d38606339fac5ed5d00d47f4cfa63ffe2635176edbd4b8164d0aac7192a5cb` |
| `artifacts/multicurrency/dc5d2f4e8a324610af37df8c5ff5515f/results.json` | 6717 | `e445fcaf1bb1ef0cdfa69ddd868adebf481a575f47086bdf1aaeac789edbe753` |
| `artifacts/multicurrency/dc5d2f4e8a324610af37df8c5ff5515f/run.json` | 399 | `12c00aa5ee928ee5492b58b78d359d907dba7cf8d47e939bd5670395c51dc75c` |
| `artifacts/multicurrency/dc5d2f4e8a324610af37df8c5ff5515f/stop.done` | 6 | `12e5cb1d6521d6a9f0369978cca6ec93c52582896608bf12882764ae21a0c6e4` |
| `artifacts/multicurrency/dc5d2f4e8a324610af37df8c5ff5515f/stop.request` | 17 | `23d38606339fac5ed5d00d47f4cfa63ffe2635176edbd4b8164d0aac7192a5cb` |
| `artifacts/multicurrency/dc5d2f4e8a324610af37df8c5ff5515f/browser/.last-run.json` | 45 | `91d1c43004802cd49950d78eb11c8fa7d05da8ffffe219a8b13b2f561bc00903` |
| `artifacts/multicurrency/dc5d2f4e8a324610af37df8c5ff5515f/browser/connected-product-currency.png` | 126245 | `a8ed4fa9b91c1a1e47cae93ecfffb554533baccf6ba48f03695fbb39fce9b27f` |
| `artifacts/multicurrency/dc5d2f4e8a324610af37df8c5ff5515f/browser/currency-results.json` | 18409 | `36cb67d91e32703d93c39a7273ec5eac5c4b4f3afd05b83168e0040d598628d4` |
| `artifacts/multicurrency/dc5d2f4e8a324610af37df8c5ff5515f/browser/first-visit.png` | 313854 | `cae1ce102c0ca4ed755725c221d0f326a456159f8273881b604d46940218703e` |
| `artifacts/multicurrency/dc5d2f4e8a324610af37df8c5ff5515f/keys/key-45636057-abaa-441d-8007-5f01f2d80844.xml` | 1016 | `8fbbfb014702d578d9bccd1c1eb77569afd7a0f2509e24d82358b03ef37f48bd` |
| `artifacts/multicurrency/e78e1ec3aacb4c02941ce2cf2926cd7e/bootstrap.log` | 16464 | `e66a06b57d4b3518749a789b412376cdb39411d741475ca2c895be75a6abc883` |
| `artifacts/multicurrency/e78e1ec3aacb4c02941ce2cf2926cd7e/browser-error.log` | 351 | `aec217865ff3e837fe7be96e111aa9aab10608d80de38b94fde4a5733d4dd414` |
| `artifacts/multicurrency/e78e1ec3aacb4c02941ce2cf2926cd7e/browser.log` | 191 | `60cda9942019b68eb33178a3b929e6ce909078ba7a238f1bb26d28d379a9a1fd` |
| `artifacts/multicurrency/e78e1ec3aacb4c02941ce2cf2926cd7e/configure.done` | 6 | `12e5cb1d6521d6a9f0369978cca6ec93c52582896608bf12882764ae21a0c6e4` |
| `artifacts/multicurrency/e78e1ec3aacb4c02941ce2cf2926cd7e/configure.request` | 17 | `23d38606339fac5ed5d00d47f4cfa63ffe2635176edbd4b8164d0aac7192a5cb` |
| `artifacts/multicurrency/e78e1ec3aacb4c02941ce2cf2926cd7e/final-sql-state.json` | 669 | `9abfdbaaeb4d009ace4b3874754ead1013744114a58813e31faf3eba282c4d4f` |
| `artifacts/multicurrency/e78e1ec3aacb4c02941ce2cf2926cd7e/host-error.log` | 0 | `e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855` |
| `artifacts/multicurrency/e78e1ec3aacb4c02941ce2cf2926cd7e/host.log` | 75972 | `35dd1b149753660cd26f28d9eade5345674a1ed3bb4322b30ce83515dfe11c23` |
| `artifacts/multicurrency/e78e1ec3aacb4c02941ce2cf2926cd7e/http.json` | 5457 | `8c385df8705c8d0630ea43a8f8bf76acbac790488ec9ab8caa983b3aef0029e6` |
| `artifacts/multicurrency/e78e1ec3aacb4c02941ce2cf2926cd7e/initial-sql-currency.json` | 269 | `3bf5c97f557836bca7a804952ec2522b55b5ea7473e336c6934d1396ff06d01d` |
| `artifacts/multicurrency/e78e1ec3aacb4c02941ce2cf2926cd7e/lead-customer-sql.json` | 294 | `47f06317be7f199c5f75dc599219985fd6858466d3f8d68d3044b77571bb5a2c` |
| `artifacts/multicurrency/e78e1ec3aacb4c02941ce2cf2926cd7e/migration.log` | 396706 | `f51dbb999f7871c6bd5429627e0841fbc7ea7fb2e150bbe5f215613966e57a84` |
| `artifacts/multicurrency/e78e1ec3aacb4c02941ce2cf2926cd7e/no-studio.done` | 6 | `12e5cb1d6521d6a9f0369978cca6ec93c52582896608bf12882764ae21a0c6e4` |
| `artifacts/multicurrency/e78e1ec3aacb4c02941ce2cf2926cd7e/no-studio.request` | 17 | `23d38606339fac5ed5d00d47f4cfa63ffe2635176edbd4b8164d0aac7192a5cb` |
| `artifacts/multicurrency/e78e1ec3aacb4c02941ce2cf2926cd7e/product-currency-sql.json` | 233 | `d497fe0595c69700c5852a37df6f5d2ec1941a4600cbe7fe3264f1b6a112e6a5` |
| `artifacts/multicurrency/e78e1ec3aacb4c02941ce2cf2926cd7e/read-only.done` | 6 | `12e5cb1d6521d6a9f0369978cca6ec93c52582896608bf12882764ae21a0c6e4` |
| `artifacts/multicurrency/e78e1ec3aacb4c02941ce2cf2926cd7e/read-only.request` | 17 | `23d38606339fac5ed5d00d47f4cfa63ffe2635176edbd4b8164d0aac7192a5cb` |
| `artifacts/multicurrency/e78e1ec3aacb4c02941ce2cf2926cd7e/restart.done` | 6 | `12e5cb1d6521d6a9f0369978cca6ec93c52582896608bf12882764ae21a0c6e4` |
| `artifacts/multicurrency/e78e1ec3aacb4c02941ce2cf2926cd7e/restart.request` | 17 | `23d38606339fac5ed5d00d47f4cfa63ffe2635176edbd4b8164d0aac7192a5cb` |
| `artifacts/multicurrency/e78e1ec3aacb4c02941ce2cf2926cd7e/results.json` | 6717 | `e24c4715c952da09455716c21f4af65a8c3e567fcc413d8c8d85054ee34f9e6a` |
| `artifacts/multicurrency/e78e1ec3aacb4c02941ce2cf2926cd7e/run.json` | 399 | `a0e5694aee7e5b0837babe2db260274af75b07fc461e88f7e69cbf5eb5d3b166` |
| `artifacts/multicurrency/e78e1ec3aacb4c02941ce2cf2926cd7e/stop.done` | 6 | `12e5cb1d6521d6a9f0369978cca6ec93c52582896608bf12882764ae21a0c6e4` |
| `artifacts/multicurrency/e78e1ec3aacb4c02941ce2cf2926cd7e/stop.request` | 17 | `23d38606339fac5ed5d00d47f4cfa63ffe2635176edbd4b8164d0aac7192a5cb` |
| `artifacts/multicurrency/e78e1ec3aacb4c02941ce2cf2926cd7e/browser/.last-run.json` | 45 | `91d1c43004802cd49950d78eb11c8fa7d05da8ffffe219a8b13b2f561bc00903` |
| `artifacts/multicurrency/e78e1ec3aacb4c02941ce2cf2926cd7e/browser/connected-product-currency.png` | 140295 | `77d61fbb073f17e97bb08ed27483f0282b7f65b3b487419642da649caf56f0b7` |
| `artifacts/multicurrency/e78e1ec3aacb4c02941ce2cf2926cd7e/browser/currency-results.json` | 18248 | `bcb961a530798c3e06281d1130e788dee19368c376adf4780d36b31eb1667651` |
| `artifacts/multicurrency/e78e1ec3aacb4c02941ce2cf2926cd7e/browser/first-visit.png` | 331907 | `a139df089ef4d4ec79ea4c441c008eaa20e698e9d148650fa1f20ed329ef6984` |
| `artifacts/multicurrency/e78e1ec3aacb4c02941ce2cf2926cd7e/keys/key-b6edb56b-f0bd-467c-86c6-d824962208d6.xml` | 1016 | `3dfa7513c76c590928f458a66cc8e3a8a9597c28d44f43c6cea04c9c8c147f85` |
| `artifacts/multicurrency/e8188d53a43d48678c4860772ad8be17/bootstrap.log` | 16464 | `7a9357ae772262475cea64062259d198af75b8e9558b96f5ca9600877ff94218` |
| `artifacts/multicurrency/e8188d53a43d48678c4860772ad8be17/browser-error.log` | 352 | `1c27a994701282db97e5b8bb29dfe8fac8bb98dd969a68f40574debe1bb626e9` |
| `artifacts/multicurrency/e8188d53a43d48678c4860772ad8be17/browser.log` | 4370 | `56f2b1b44356850d7ed02184b30396feb05d941d7c8f3ba8a4539763bc58ddf3` |
| `artifacts/multicurrency/e8188d53a43d48678c4860772ad8be17/final-sql-state.json` | 617 | `d46e28adb8e8d030b0e4b10166ebfe9dbd55065f2f48383c361be5471b2b0e34` |
| `artifacts/multicurrency/e8188d53a43d48678c4860772ad8be17/host-error.log` | 0 | `e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855` |
| `artifacts/multicurrency/e8188d53a43d48678c4860772ad8be17/host.log` | 630068 | `581037784d04eb56b93f621e607afc6ed8a83e9c1921c50e9304dd6e66932498` |
| `artifacts/multicurrency/e8188d53a43d48678c4860772ad8be17/http.json` | 2955 | `53f31c17815dd1bd0a51d2055c928c384a2d10050787aa1ab9706e092fab17b4` |
| `artifacts/multicurrency/e8188d53a43d48678c4860772ad8be17/initial-sql-currency.json` | 269 | `8fe3f544a19932bdda9fb697712bc93cd3f57cab6e109bb601cae4de46dca4cb` |
| `artifacts/multicurrency/e8188d53a43d48678c4860772ad8be17/migration.log` | 396706 | `3857d444331a38a1f19857dc8e8c8d6efa9abb14555841fe273b0f5f61ed0868` |
| `artifacts/multicurrency/e8188d53a43d48678c4860772ad8be17/results.json` | 1597 | `246c027bf756c344475cb6edf1db8cb6c613a813570d5a9864da2222a6985429` |
| `artifacts/multicurrency/e8188d53a43d48678c4860772ad8be17/run.json` | 399 | `8833938d82aa36f2fbd5a4abd14e8dc0c019d2f468ba964623991c0601504800` |
| `artifacts/multicurrency/e8188d53a43d48678c4860772ad8be17/browser/.last-run.json` | 96 | `e115f14270d9a82f15ac5d61ab6d160641be5d975f92e13fe79e0e8ad5d39619` |
| `artifacts/multicurrency/e8188d53a43d48678c4860772ad8be17/browser/currency-results.json` | 8613 | `c4f1ab558dfee905d2639bc1f5b381741eb7fb07d23977e6e0ba0d695dc3ae1d` |
| `artifacts/multicurrency/e8188d53a43d48678c4860772ad8be17/browser/first-visit.png` | 411046 | `c3bf09b38f0bc9ac6d9c84ddd9567338d918a25b60fe159b7c5ce7fe95a33192` |
| `artifacts/multicurrency/e8188d53a43d48678c4860772ad8be17/browser/test-failed-1.png` | 85590 | `cda124ff9d61db740e9f934a181f92329da1af97a56af04bd91e204fed0e36c0` |
| `artifacts/multicurrency/e8188d53a43d48678c4860772ad8be17/browser/test-failed-2.png` | 106280 | `bb46b4deaf66b9db9bbe3419a0501a1d3884bf02fd21f49b1fac40fde82f09e9` |
| `artifacts/multicurrency/e8188d53a43d48678c4860772ad8be17/keys/key-bf65c850-7dc1-4388-bfbc-320855f557bd.xml` | 1016 | `79d04852b4c5420b897743e6e825f19a840e672f216e07c2cff8104fd0d49fc6` |
| `artifacts/multicurrency/feaf19ad4d9e416e8665f28c4c33f383/bootstrap.log` | 16464 | `7e0f899827c629ccc069d6a5bbe3c695ff3da8b008b5728e03f5b8b59012a741` |
| `artifacts/multicurrency/feaf19ad4d9e416e8665f28c4c33f383/browser-error.log` | 351 | `c6e218bb33941353c415bfd992a88df579f98d0ddfe7875ba63295918a3445cc` |
| `artifacts/multicurrency/feaf19ad4d9e416e8665f28c4c33f383/browser.log` | 3718 | `2262d609842d22c857d91c0442fdb770c6ef60181aba1de5036960bb18b9d6b4` |
| `artifacts/multicurrency/feaf19ad4d9e416e8665f28c4c33f383/final-sql-state.json` | 617 | `10d6f035784abafc8326feabf1d86ca1f9b125d3ead6a2ede9f61e7d9d6bea3a` |
| `artifacts/multicurrency/feaf19ad4d9e416e8665f28c4c33f383/host-error.log` | 0 | `e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855` |
| `artifacts/multicurrency/feaf19ad4d9e416e8665f28c4c33f383/host.log` | 632895 | `788f778e95474376012f511fbfc524e71cef330ca351d2aa4068012653da09ee` |
| `artifacts/multicurrency/feaf19ad4d9e416e8665f28c4c33f383/http.json` | 2955 | `3079685b115b9295ba10c71d6ec38ed5b02ebb1ab002d52914ccf375469b73ec` |
| `artifacts/multicurrency/feaf19ad4d9e416e8665f28c4c33f383/initial-sql-currency.json` | 269 | `f401754e1d7379c0c9c0b7761847316c385a3cba6636d88aa14c482af6b8aa41` |
| `artifacts/multicurrency/feaf19ad4d9e416e8665f28c4c33f383/migration.log` | 396707 | `491c71277330a41e73d0a767dc5d187c4402dd211746f7f0bc583ddce6c8a339` |
| `artifacts/multicurrency/feaf19ad4d9e416e8665f28c4c33f383/results.json` | 1972 | `33723d7fe9fcdcc39e057be21240f2e5a412e3303a6160c71dd1c106094b9ee0` |
| `artifacts/multicurrency/feaf19ad4d9e416e8665f28c4c33f383/run.json` | 399 | `67d93f8f819802f1b17e4130ea92b0b7ab992f8b94dfa95488b4e4a1aeb4c4a4` |
| `artifacts/multicurrency/feaf19ad4d9e416e8665f28c4c33f383/browser/.last-run.json` | 96 | `e115f14270d9a82f15ac5d61ab6d160641be5d975f92e13fe79e0e8ad5d39619` |
| `artifacts/multicurrency/feaf19ad4d9e416e8665f28c4c33f383/browser/currency-results.json` | 9690 | `ae86b1ee6221c2a4c5f0cb8ff93241f0c56d00bd7130b648648f5e05203ee148` |
| `artifacts/multicurrency/feaf19ad4d9e416e8665f28c4c33f383/browser/first-visit.png` | 473278 | `c541ac7576c58e79925c7bd7933c5830d418f0a295b593ce27fa06bb86e282a8` |
| `artifacts/multicurrency/feaf19ad4d9e416e8665f28c4c33f383/browser/test-failed-1.png` | 4257 | `74f05932c7848edf4192607a3d825ea9e993e46352d8a542f87b5ba785b6fa9d` |
| `artifacts/multicurrency/feaf19ad4d9e416e8665f28c4c33f383/browser/test-failed-2.png` | 106280 | `bb46b4deaf66b9db9bbe3419a0501a1d3884bf02fd21f49b1fac40fde82f09e9` |
| `artifacts/multicurrency/feaf19ad4d9e416e8665f28c4c33f383/keys/key-971d3810-e148-41d6-ad36-5458a4e0d05f.xml` | 1016 | `530d2b769785d166c105a09f56ecd3668aab211144f1a143e87dbe06f96a4d1a` |
| `artifacts/multicurrency/ff6022444bad43fda8ecbb7310b5f77f/bootstrap.log` | 16463 | `2ba28d7b0bba92a21ca13f3ddb49448b9cae9c5a56bf224372f94a48302fa72d` |
| `artifacts/multicurrency/ff6022444bad43fda8ecbb7310b5f77f/browser-error.log` | 352 | `fea384b7c9b4e8cf079abbe6a433ad0b650f4671ddcb8a7b5996066e2721e7b2` |
| `artifacts/multicurrency/ff6022444bad43fda8ecbb7310b5f77f/browser.log` | 4884 | `8e344f3fe31183b72933c35410357fe9d2a88dc762245fcc459a15049fb84160` |
| `artifacts/multicurrency/ff6022444bad43fda8ecbb7310b5f77f/configure.done` | 6 | `12e5cb1d6521d6a9f0369978cca6ec93c52582896608bf12882764ae21a0c6e4` |
| `artifacts/multicurrency/ff6022444bad43fda8ecbb7310b5f77f/configure.request` | 17 | `23d38606339fac5ed5d00d47f4cfa63ffe2635176edbd4b8164d0aac7192a5cb` |
| `artifacts/multicurrency/ff6022444bad43fda8ecbb7310b5f77f/final-sql-state.json` | 669 | `72bce0877253382ab1c39e7885393eca831796fa0eaef7dc85482715d572ca25` |
| `artifacts/multicurrency/ff6022444bad43fda8ecbb7310b5f77f/host-error.log` | 0 | `e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855` |
| `artifacts/multicurrency/ff6022444bad43fda8ecbb7310b5f77f/host.log` | 21427 | `2d40088767a3f90a6c1fade253481fa359a97e75ea0f6eb498841b838aa019da` |
| `artifacts/multicurrency/ff6022444bad43fda8ecbb7310b5f77f/http.json` | 3385 | `13e9301c7668defb4564dcdfabfbbab0521a704403d392d1df7ee79ab41a9b77` |
| `artifacts/multicurrency/ff6022444bad43fda8ecbb7310b5f77f/initial-sql-currency.json` | 269 | `08db1a4760cb93609d28124165a286584e6c04418a60d04a3d5c9bd518949e82` |
| `artifacts/multicurrency/ff6022444bad43fda8ecbb7310b5f77f/migration.log` | 396705 | `4ae257051f0c4dbea556bd80dd0023dd830d37a34cf97dac97f5645b3bf94a4e` |
| `artifacts/multicurrency/ff6022444bad43fda8ecbb7310b5f77f/no-studio.done` | 6 | `12e5cb1d6521d6a9f0369978cca6ec93c52582896608bf12882764ae21a0c6e4` |
| `artifacts/multicurrency/ff6022444bad43fda8ecbb7310b5f77f/no-studio.request` | 17 | `23d38606339fac5ed5d00d47f4cfa63ffe2635176edbd4b8164d0aac7192a5cb` |
| `artifacts/multicurrency/ff6022444bad43fda8ecbb7310b5f77f/read-only.done` | 6 | `12e5cb1d6521d6a9f0369978cca6ec93c52582896608bf12882764ae21a0c6e4` |
| `artifacts/multicurrency/ff6022444bad43fda8ecbb7310b5f77f/read-only.request` | 17 | `23d38606339fac5ed5d00d47f4cfa63ffe2635176edbd4b8164d0aac7192a5cb` |
| `artifacts/multicurrency/ff6022444bad43fda8ecbb7310b5f77f/restart.done` | 6 | `12e5cb1d6521d6a9f0369978cca6ec93c52582896608bf12882764ae21a0c6e4` |
| `artifacts/multicurrency/ff6022444bad43fda8ecbb7310b5f77f/restart.request` | 17 | `23d38606339fac5ed5d00d47f4cfa63ffe2635176edbd4b8164d0aac7192a5cb` |
| `artifacts/multicurrency/ff6022444bad43fda8ecbb7310b5f77f/results.json` | 4248 | `5a1b03cfe111e666ae5008fec0d0fb09dd83fc62e4c09c8a5c5ac7513254556c` |
| `artifacts/multicurrency/ff6022444bad43fda8ecbb7310b5f77f/run.json` | 399 | `0f028dbfcd3f0c4943220ec58e39df9c5dbd8a5bec8a04a6bfaee162cd854821` |
| `artifacts/multicurrency/ff6022444bad43fda8ecbb7310b5f77f/stop.done` | 6 | `12e5cb1d6521d6a9f0369978cca6ec93c52582896608bf12882764ae21a0c6e4` |
| `artifacts/multicurrency/ff6022444bad43fda8ecbb7310b5f77f/stop.request` | 17 | `23d38606339fac5ed5d00d47f4cfa63ffe2635176edbd4b8164d0aac7192a5cb` |
| `artifacts/multicurrency/ff6022444bad43fda8ecbb7310b5f77f/browser/.last-run.json` | 96 | `e115f14270d9a82f15ac5d61ab6d160641be5d975f92e13fe79e0e8ad5d39619` |
| `artifacts/multicurrency/ff6022444bad43fda8ecbb7310b5f77f/browser/connected-product-currency.png` | 126447 | `71a3605080309dab9d6458dd0bad2e40cc30819a642d62779d4a15b130ad840f` |
| `artifacts/multicurrency/ff6022444bad43fda8ecbb7310b5f77f/browser/currency-results.json` | 17844 | `bf2f3ece04a9685be2a56f372fbf5979258131d1793ceb7ba641bfb8a0d0fb02` |
| `artifacts/multicurrency/ff6022444bad43fda8ecbb7310b5f77f/browser/first-visit.png` | 390136 | `d1dcf2225695091cd5bc031d0c4d7a503ff9e4cc47cde92aeabb450a4845942f` |
| `artifacts/multicurrency/ff6022444bad43fda8ecbb7310b5f77f/browser/test-failed-1.png` | 113162 | `6fe6208d650854dd599a2e5998c6974ecb8ae70381438b3a365d85318afcc90c` |
| `artifacts/multicurrency/ff6022444bad43fda8ecbb7310b5f77f/browser/test-failed-2.png` | 106280 | `bb46b4deaf66b9db9bbe3419a0501a1d3884bf02fd21f49b1fac40fde82f09e9` |
| `artifacts/multicurrency/ff6022444bad43fda8ecbb7310b5f77f/browser/test-failed-3.png` | 106280 | `bb46b4deaf66b9db9bbe3419a0501a1d3884bf02fd21f49b1fac40fde82f09e9` |
| `artifacts/multicurrency/ff6022444bad43fda8ecbb7310b5f77f/browser/test-failed-4.png` | 101538 | `9313110121c6e137d539b8350f67d7e4f4ffc24ec5eb943fec16ee39e28271b3` |
| `artifacts/multicurrency/ff6022444bad43fda8ecbb7310b5f77f/browser/test-failed-5.png` | 99930 | `35c3c249a773a2f49e5db1f6849d636b73d1ab7d8a7fde2f08525a749d493247` |
| `artifacts/multicurrency/ff6022444bad43fda8ecbb7310b5f77f/keys/key-dd1c4c96-42ba-4d80-b019-0e9184042b58.xml` | 1016 | `79a59df4b942e7b643d7df4eaa2e40cf77014929e5199714a4090e98fe18ebe7` |
| `artifacts/studio-first-visit/256af25403c5420f9e3a5d1e2c84d8b4/bootstrap.log` | 16464 | `844b72b41bae40c28a41d549fcf212b0dac89693a40223ffea32f4ed92272768` |
| `artifacts/studio-first-visit/256af25403c5420f9e3a5d1e2c84d8b4/browser-error.log` | 351 | `c1842de2f625971f564fe8101ffa34e2f38e2da51a753d1c99fc7d807f40c895` |
| `artifacts/studio-first-visit/256af25403c5420f9e3a5d1e2c84d8b4/browser.log` | 181 | `6a057c1d742dae90cae35e2bd844bc79a5ae81f0f5531a9846d8489ed94e30d1` |
| `artifacts/studio-first-visit/256af25403c5420f9e3a5d1e2c84d8b4/configure.done` | 6 | `12e5cb1d6521d6a9f0369978cca6ec93c52582896608bf12882764ae21a0c6e4` |
| `artifacts/studio-first-visit/256af25403c5420f9e3a5d1e2c84d8b4/configure.request` | 17 | `23d38606339fac5ed5d00d47f4cfa63ffe2635176edbd4b8164d0aac7192a5cb` |
| `artifacts/studio-first-visit/256af25403c5420f9e3a5d1e2c84d8b4/final-sql-state.json` | 669 | `801591179959f2b080c7f1352e3228a81e8761307c7903d14761c07dee1703a5` |
| `artifacts/studio-first-visit/256af25403c5420f9e3a5d1e2c84d8b4/host-error.log` | 0 | `e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855` |
| `artifacts/studio-first-visit/256af25403c5420f9e3a5d1e2c84d8b4/host.log` | 75976 | `5b461df7015b475183cb2661ccf4d623191f67e1e3a0cdecef21e491350c410f` |
| `artifacts/studio-first-visit/256af25403c5420f9e3a5d1e2c84d8b4/http.json` | 5457 | `be16cac8d03ad0e8cb89b6c776a6c0fe509fd8e0634bbef862e5956f0b91b2a6` |
| `artifacts/studio-first-visit/256af25403c5420f9e3a5d1e2c84d8b4/initial-sql-currency.json` | 269 | `b9cd10d0e989239dc35ef760a3c60aa6016b26b1fe6a8a018984c999ba8000bc` |
| `artifacts/studio-first-visit/256af25403c5420f9e3a5d1e2c84d8b4/migration.log` | 396703 | `0e3876bfa9d8435536294914f7b99c0f391f7e06a7a845db57b88218c26f56c7` |
| `artifacts/studio-first-visit/256af25403c5420f9e3a5d1e2c84d8b4/product-currency-sql.json` | 233 | `7183ae0fd85f609048b1771d0dbd1c01b0e718038d479c0af91ca365b009196d` |
| `artifacts/studio-first-visit/256af25403c5420f9e3a5d1e2c84d8b4/read-only.done` | 6 | `12e5cb1d6521d6a9f0369978cca6ec93c52582896608bf12882764ae21a0c6e4` |
| `artifacts/studio-first-visit/256af25403c5420f9e3a5d1e2c84d8b4/read-only.request` | 17 | `23d38606339fac5ed5d00d47f4cfa63ffe2635176edbd4b8164d0aac7192a5cb` |
| `artifacts/studio-first-visit/256af25403c5420f9e3a5d1e2c84d8b4/restart.done` | 6 | `12e5cb1d6521d6a9f0369978cca6ec93c52582896608bf12882764ae21a0c6e4` |
| `artifacts/studio-first-visit/256af25403c5420f9e3a5d1e2c84d8b4/restart.request` | 17 | `23d38606339fac5ed5d00d47f4cfa63ffe2635176edbd4b8164d0aac7192a5cb` |
| `artifacts/studio-first-visit/256af25403c5420f9e3a5d1e2c84d8b4/results.json` | 3167 | `2fb3b54f59a958853704a09c975f0a7f142212a104dba5140e5a76ac853e726a` |
| `artifacts/studio-first-visit/256af25403c5420f9e3a5d1e2c84d8b4/run.json` | 397 | `92edf36c5c29bb064f8e814250801ffaae742dc3a281580bf06631915ae15cb2` |
| `artifacts/studio-first-visit/256af25403c5420f9e3a5d1e2c84d8b4/stop.done` | 6 | `12e5cb1d6521d6a9f0369978cca6ec93c52582896608bf12882764ae21a0c6e4` |
| `artifacts/studio-first-visit/256af25403c5420f9e3a5d1e2c84d8b4/stop.request` | 17 | `23d38606339fac5ed5d00d47f4cfa63ffe2635176edbd4b8164d0aac7192a5cb` |
| `artifacts/studio-first-visit/256af25403c5420f9e3a5d1e2c84d8b4/browser/.last-run.json` | 45 | `91d1c43004802cd49950d78eb11c8fa7d05da8ffffe219a8b13b2f561bc00903` |
| `artifacts/studio-first-visit/256af25403c5420f9e3a5d1e2c84d8b4/browser/connected-product-currency.png` | 126690 | `4706ec38a2d2adfa795e42a6e1abd9bb7dd6792c22e51d5b46474db949b20077` |
| `artifacts/studio-first-visit/256af25403c5420f9e3a5d1e2c84d8b4/browser/first-visit.png` | 382157 | `c02b7ab0fb0886e960665a73eede855e695c20f451b8a548b19055c32b534a79` |
| `artifacts/studio-first-visit/256af25403c5420f9e3a5d1e2c84d8b4/browser/studio-results.json` | 14801 | `26184f0a4452d92019eb8f158ca1cecaf4211c49f11ba8a0e0e87a59547a1645` |
| `artifacts/studio-first-visit/256af25403c5420f9e3a5d1e2c84d8b4/keys/key-d844e774-6212-406e-8db5-d375ec69e673.xml` | 1016 | `ec60b8921c1fa762e7623c8b5a75882315ed9bfc8af196acbded6e70ef042c83` |
| `artifacts/studio-first-visit/34448f2781854ff28485da963da7f819/bootstrap.log` | 16465 | `b4887e9ba8d9fc7b142ac692874f00394d62a47528132f4f6d312a0fc6940a4b` |
| `artifacts/studio-first-visit/34448f2781854ff28485da963da7f819/browser-error.log` | 352 | `22f449205cde2a9873b421a95f2743de03e86b4a8fcb0ce4bff73d121fd67a0f` |
| `artifacts/studio-first-visit/34448f2781854ff28485da963da7f819/browser.log` | 2674 | `3db61372b12ff0b7da665eba5f98f81c084f4ee60e4e8b45ca804e1afe020c9c` |
| `artifacts/studio-first-visit/34448f2781854ff28485da963da7f819/final-sql-state.json` | 617 | `9d641e76c78b77163623ad2918665bc9a1275a6d837f69581d0026aa903ff516` |
| `artifacts/studio-first-visit/34448f2781854ff28485da963da7f819/host-error.log` | 0 | `e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855` |
| `artifacts/studio-first-visit/34448f2781854ff28485da963da7f819/host.log` | 662213 | `d7c809cf8953946f63cac9c396151cf12107a4a2b4a163b4baf2129c1cd2ef17` |
| `artifacts/studio-first-visit/34448f2781854ff28485da963da7f819/http.json` | 2955 | `5c0d6473e242cb69c789f0e3eb25202b52bf458432ffcf0e8496cd0ea755d612` |
| `artifacts/studio-first-visit/34448f2781854ff28485da963da7f819/initial-sql-currency.json` | 269 | `8137e61d34e30f307c346283ac4807f259291f9a3bbb404f9533c7d19b7c7290` |
| `artifacts/studio-first-visit/34448f2781854ff28485da963da7f819/migration.log` | 396704 | `a564e639981fe9f2e0e0200c00a53cb4539025cd6f4499997735a76fe00cac17` |
| `artifacts/studio-first-visit/34448f2781854ff28485da963da7f819/results.json` | 1210 | `c4c23efcb022a75e8925101f187e9472e9d078e278e7f52a00b296a64c235e83` |
| `artifacts/studio-first-visit/34448f2781854ff28485da963da7f819/run.json` | 397 | `c7ef8fb827a0c9311b25fc1e5bbd1509f80b944dba37014860d37b3815fd0531` |
| `artifacts/studio-first-visit/34448f2781854ff28485da963da7f819/browser/.last-run.json` | 96 | `3ed50683a52fcd84168c50d8432b155118f0aa488cddba3ddd796fe61ce273e3` |
| `artifacts/studio-first-visit/34448f2781854ff28485da963da7f819/browser/first-visit.png` | 385397 | `f952e07347b897435971bf67e6e8e14828cbfff407328cb0bef574260157f4cc` |
| `artifacts/studio-first-visit/34448f2781854ff28485da963da7f819/browser/studio-results.json` | 9249 | `2e3f4e6ed5a782e79cf2e81f352058e4bf594370573f5a2d2e0b55d9d9174e78` |
| `artifacts/studio-first-visit/34448f2781854ff28485da963da7f819/browser/test-failed-1.png` | 145960 | `ac36b917f847fbb831bfd578d76751d7b33a022819b96190da2696e4728542d4` |
| `artifacts/studio-first-visit/34448f2781854ff28485da963da7f819/browser/test-failed-2.png` | 106280 | `bb46b4deaf66b9db9bbe3419a0501a1d3884bf02fd21f49b1fac40fde82f09e9` |
| `artifacts/studio-first-visit/34448f2781854ff28485da963da7f819/keys/key-68b501a3-4f7d-4ba7-b52a-252b2f0140bb.xml` | 1016 | `5caa62df503a97143602670772f4ebcdc8883e43e4c22a0ef467a9ec40a83f15` |
| `artifacts/studio-first-visit/6aa5df5b759f4503b9c8ecba06ad6304/bootstrap.log` | 16465 | `9eeac0e6eb9a946b55e653862c7e0ebb0406553fdddaa928dc290924845f3ce4` |
| `artifacts/studio-first-visit/6aa5df5b759f4503b9c8ecba06ad6304/browser-error.log` | 352 | `d3a349c0b563b19848a9e895c9da2ee5c94f00ef2920cb7e79b319358f6fda0c` |
| `artifacts/studio-first-visit/6aa5df5b759f4503b9c8ecba06ad6304/browser.log` | 4280 | `dfd79532e0d4ad680b9819d4897b0956ffff5e07c57e96d3c811fac478b913e1` |
| `artifacts/studio-first-visit/6aa5df5b759f4503b9c8ecba06ad6304/final-sql-state.json` | 617 | `259a1e3e00fa500c944fe9daed2256599d3c7e66e892d1db8114c568180d9ed1` |
| `artifacts/studio-first-visit/6aa5df5b759f4503b9c8ecba06ad6304/host-error.log` | 0 | `e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855` |
| `artifacts/studio-first-visit/6aa5df5b759f4503b9c8ecba06ad6304/host.log` | 615387 | `80919a38624b4aab2d95c6c3fa7667657f3e0a4bacb1df353f117c59df0cd66b` |
| `artifacts/studio-first-visit/6aa5df5b759f4503b9c8ecba06ad6304/http.json` | 2955 | `3c54f9614f7e6435cb18294441da56d4a826ea2d78f0b4b9cc16d531a62a28b3` |
| `artifacts/studio-first-visit/6aa5df5b759f4503b9c8ecba06ad6304/initial-sql-currency.json` | 269 | `10a53c2830c8f13fb6b98c95463736faf0893f1a5239f91a9cb6085a2fbe52ac` |
| `artifacts/studio-first-visit/6aa5df5b759f4503b9c8ecba06ad6304/migration.log` | 396707 | `c8da50049faafa501f51ae8d11673bbdf94160330d64f59bbbb4f10b61ca3d6b` |
| `artifacts/studio-first-visit/6aa5df5b759f4503b9c8ecba06ad6304/results.json` | 1210 | `40505af456e1cccade974e17feb4b550e2e721c3d72a43d0087dabbaf23fd044` |
| `artifacts/studio-first-visit/6aa5df5b759f4503b9c8ecba06ad6304/run.json` | 397 | `2b8e654c0a1611f4a0f473818cd34dd1a1dc5d4dca68502b8d5d5d72e38cc2b3` |
| `artifacts/studio-first-visit/6aa5df5b759f4503b9c8ecba06ad6304/browser/.last-run.json` | 96 | `3ed50683a52fcd84168c50d8432b155118f0aa488cddba3ddd796fe61ce273e3` |
| `artifacts/studio-first-visit/6aa5df5b759f4503b9c8ecba06ad6304/browser/first-visit.png` | 358089 | `95ec2f11b4b4a2e448f7b9dd8662c733f7ab8d8d0470a2a5996ec9b3ff78ee93` |
| `artifacts/studio-first-visit/6aa5df5b759f4503b9c8ecba06ad6304/browser/studio-results.json` | 8168 | `4eaac45029056e056e2f5032ac580ea242e7d925c8dce02dde055af668edc2cd` |
| `artifacts/studio-first-visit/6aa5df5b759f4503b9c8ecba06ad6304/browser/test-failed-1.png` | 85725 | `0be2501ee9217de82e4daa4a31494571abd7c517952203593af9defc29e5b79b` |
| `artifacts/studio-first-visit/6aa5df5b759f4503b9c8ecba06ad6304/browser/test-failed-2.png` | 106280 | `bb46b4deaf66b9db9bbe3419a0501a1d3884bf02fd21f49b1fac40fde82f09e9` |
| `artifacts/studio-first-visit/6aa5df5b759f4503b9c8ecba06ad6304/keys/key-4bd2276c-3f66-4fce-8f41-e0c647c4b736.xml` | 1013 | `19005f25075c4ea6d6291f26a8b6700a22aba0c595446e972c5a64d421d005c7` |
| `artifacts/studio-first-visit/8aa26c7057c24b44adf79497e3cdac45/bootstrap.log` | 16464 | `f5c8c9328969e395e5feaad7cc1b663811d712ce837f68f8957a91f2478a6d84` |
| `artifacts/studio-first-visit/8aa26c7057c24b44adf79497e3cdac45/browser-error.log` | 352 | `2c2be367e5700051128c34510da4baf45d5f71a284cc8a6cb4476a6979776c71` |
| `artifacts/studio-first-visit/8aa26c7057c24b44adf79497e3cdac45/browser.log` | 2327 | `70541d3b8c27d46a89421e0e868efdf2d06318ff2ac66eb0f4cbbac39f90faba` |
| `artifacts/studio-first-visit/8aa26c7057c24b44adf79497e3cdac45/final-sql-state.json` | 503 | `19500925b3759b36d56a74fe108ef6b0979f95ff1ff18375cd600332c6a2ea80` |
| `artifacts/studio-first-visit/8aa26c7057c24b44adf79497e3cdac45/host-error.log` | 0 | `e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855` |
| `artifacts/studio-first-visit/8aa26c7057c24b44adf79497e3cdac45/host.log` | 251794 | `844470144ca1c1a6a0b1e2545b88e6d5885dc14bcb5361db95af6fdb9f3eef91` |
| `artifacts/studio-first-visit/8aa26c7057c24b44adf79497e3cdac45/http.json` | 2955 | `1a139f6e278582cee3d05486723f359133050adce9913fd22208761c9c6e0f55` |
| `artifacts/studio-first-visit/8aa26c7057c24b44adf79497e3cdac45/initial-sql-currency.json` | 269 | `b9a7439d01f6a021a79a7b7f5823efd30b03daa332ea20aa2028b000ce1e1ff2` |
| `artifacts/studio-first-visit/8aa26c7057c24b44adf79497e3cdac45/migration.log` | 396705 | `359c5be4c146da527cda0d4fd3782d3b74140da85d80317b5166cd84443a7473` |
| `artifacts/studio-first-visit/8aa26c7057c24b44adf79497e3cdac45/results.json` | 1210 | `15e64e740813d325acbf7370b80a0e13a6eb5240afa9841807b2003817fbdc0d` |
| `artifacts/studio-first-visit/8aa26c7057c24b44adf79497e3cdac45/run.json` | 397 | `a3f97922e95911e1b16a477228401564de47274e3f03d04586b4ff28b21c119c` |
| `artifacts/studio-first-visit/8aa26c7057c24b44adf79497e3cdac45/keys/key-12035b05-5322-4b0c-8611-aec58afafb82.xml` | 1016 | `b986b54aabace02703f1accbb509c176f0bd422b45791e032b38318a52b9046b` |
| `artifacts/studio-first-visit/917b55134b90478e9978013197d35053/bootstrap.log` | 16464 | `013f74ec82c2dab73affb20f8b9440d2039618a9488c40333588a10245d50dda` |
| `artifacts/studio-first-visit/917b55134b90478e9978013197d35053/browser-error.log` | 352 | `2312951320ba344943e0f67711744efcc4016481b367536ef18929f8dee03aa4` |
| `artifacts/studio-first-visit/917b55134b90478e9978013197d35053/browser.log` | 2483 | `6ca5a446a4431fcebff001a92ce53a137f883a61e262e1b4b5db4c807ee125f9` |
| `artifacts/studio-first-visit/917b55134b90478e9978013197d35053/final-sql-state.json` | 503 | `9402c7d9b8e1c23eb27fc4d60ad901bf2538cffc2fb7a7e8143a709ecae3aecc` |
| `artifacts/studio-first-visit/917b55134b90478e9978013197d35053/host-error.log` | 0 | `e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855` |
| `artifacts/studio-first-visit/917b55134b90478e9978013197d35053/host.log` | 168599 | `82f1c5241e7c45371d29abe45950ecc98c2a421475027448d8032cb7dea21c38` |
| `artifacts/studio-first-visit/917b55134b90478e9978013197d35053/http.json` | 2955 | `9a59100df5840c488ffd99c2d50c5acea3249ffeb3371fbcc1338214b7ec349b` |
| `artifacts/studio-first-visit/917b55134b90478e9978013197d35053/initial-sql-currency.json` | 269 | `aa5e6f0c213d9e00c87da6c7fbab9cf7c241fc3af49d7fb40162b4003be363bc` |
| `artifacts/studio-first-visit/917b55134b90478e9978013197d35053/migration.log` | 396705 | `011bdb085ec50eeeb42885185921c0d7a606162f075967563319a8a14330584a` |
| `artifacts/studio-first-visit/917b55134b90478e9978013197d35053/results.json` | 1210 | `2e6d0b625cfd44f8738646b0d19241f682a02ad92acf99763b6d1898c0c69f39` |
| `artifacts/studio-first-visit/917b55134b90478e9978013197d35053/run.json` | 397 | `5bd232cc966a80b6b57f01f3a8ae6edc9db9781d40fe3339780fbba5491bb417` |
| `artifacts/studio-first-visit/917b55134b90478e9978013197d35053/keys/key-2e05dd6a-fed1-495f-885b-08693cd5c458.xml` | 1016 | `3c2cef91e05569df03f63a34adc798733513a29d295ec547df93ea40329700ed` |
| `artifacts/studio-first-visit/969da988638c47fc844c351b3a2eea9b/bootstrap.log` | 16463 | `2c1fb5def63ee76ba2dc331c3db072a16fa1576782bf55f5d12a993b051a8823` |
| `artifacts/studio-first-visit/969da988638c47fc844c351b3a2eea9b/browser-error.log` | 351 | `6a5594230cdf003a7a98efd6bb2f6c1dc96537637b53b072fa80735e0e1712f7` |
| `artifacts/studio-first-visit/969da988638c47fc844c351b3a2eea9b/browser.log` | 4192 | `a76387d910dc3e42d2ffbdcac1a8429b6c4d362572176b2d897928fa35334078` |
| `artifacts/studio-first-visit/969da988638c47fc844c351b3a2eea9b/final-sql-state.json` | 617 | `18652674eb2e2c731029e0b76a6f7ce59476fde8d98ea0ebb9b86b6d21046534` |
| `artifacts/studio-first-visit/969da988638c47fc844c351b3a2eea9b/host-error.log` | 0 | `e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855` |
| `artifacts/studio-first-visit/969da988638c47fc844c351b3a2eea9b/host.log` | 467086 | `7c771774e648c0b4056365f2e7040e688d057d65c3d04a2dca9a1acca2c4e6f8` |
| `artifacts/studio-first-visit/969da988638c47fc844c351b3a2eea9b/http.json` | 2955 | `35d924bf4f4a64947751e0d1540c3323cf02440ab85520cc86e272e88df4be52` |
| `artifacts/studio-first-visit/969da988638c47fc844c351b3a2eea9b/initial-sql-currency.json` | 269 | `2e2dc936580764c37aa2fe38135dae3509ac16c3ab87a899faa3829a02e7c5ce` |
| `artifacts/studio-first-visit/969da988638c47fc844c351b3a2eea9b/migration.log` | 396704 | `cb4752657813203db8adf83b28baa4bd0a33b9104bdbc8be4046cfa58609d4b8` |
| `artifacts/studio-first-visit/969da988638c47fc844c351b3a2eea9b/results.json` | 1210 | `b93bd7d2c526e7bf5060418cadfc93c3af5f3149f062018acb0cd1ac3d144aaa` |
| `artifacts/studio-first-visit/969da988638c47fc844c351b3a2eea9b/run.json` | 397 | `6caa3a0f409760cc87694bb06d90de4e051cabe6785ca63122c24684a7df31c2` |
| `artifacts/studio-first-visit/969da988638c47fc844c351b3a2eea9b/browser/.last-run.json` | 96 | `3ed50683a52fcd84168c50d8432b155118f0aa488cddba3ddd796fe61ce273e3` |
| `artifacts/studio-first-visit/969da988638c47fc844c351b3a2eea9b/browser/first-visit.png` | 364403 | `e7326bc98be0051b80d1188cb7780b145b62adf829ae566495a918e1ec7a76d0` |
| `artifacts/studio-first-visit/969da988638c47fc844c351b3a2eea9b/browser/studio-results.json` | 6969 | `e88c8e5b2b3abbe911a0b00702c29ea36a6a427e409adab6773b3276fe464183` |
| `artifacts/studio-first-visit/969da988638c47fc844c351b3a2eea9b/browser/test-failed-1.png` | 87365 | `a854075e9386cac9ca3b55e344295e2e2231dff53dcfdf92aa5574a7614e905a` |
| `artifacts/studio-first-visit/969da988638c47fc844c351b3a2eea9b/browser/test-failed-2.png` | 106280 | `bb46b4deaf66b9db9bbe3419a0501a1d3884bf02fd21f49b1fac40fde82f09e9` |
| `artifacts/studio-first-visit/969da988638c47fc844c351b3a2eea9b/keys/key-97a8d43e-2d7e-4b23-bafb-05e9910e1602.xml` | 1016 | `1d3d453c84ced4f66c491fbf96a4fb7cf2fa18edaad68cea8f54e7d37aef8822` |
| `artifacts/studio-first-visit/9d489483a4124e0c900f35a7eebf8b3f/bootstrap.log` | 16464 | `a037d17ae9b04beec72c6c7de01295e88b0fda86bb6ba2fa9286ebc59cc972c9` |
| `artifacts/studio-first-visit/9d489483a4124e0c900f35a7eebf8b3f/browser-error.log` | 352 | `dcb05d95602ba847dd3965e6d541e36f2ce891abbe8eddae0c0939d3016e0627` |
| `artifacts/studio-first-visit/9d489483a4124e0c900f35a7eebf8b3f/browser.log` | 181 | `bf45a6bbeeb9cff6fd98d49f59d8489416c41cbcbb07b9e3c22d1280c2fb5ab2` |
| `artifacts/studio-first-visit/9d489483a4124e0c900f35a7eebf8b3f/configure.done` | 6 | `12e5cb1d6521d6a9f0369978cca6ec93c52582896608bf12882764ae21a0c6e4` |
| `artifacts/studio-first-visit/9d489483a4124e0c900f35a7eebf8b3f/configure.request` | 17 | `23d38606339fac5ed5d00d47f4cfa63ffe2635176edbd4b8164d0aac7192a5cb` |
| `artifacts/studio-first-visit/9d489483a4124e0c900f35a7eebf8b3f/final-sql-state.json` | 669 | `cb88b96ce3870c36a314ec6a935987e087e710aac3c8a7ddfa6b0f27e914bf78` |
| `artifacts/studio-first-visit/9d489483a4124e0c900f35a7eebf8b3f/host-error.log` | 0 | `e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855` |
| `artifacts/studio-first-visit/9d489483a4124e0c900f35a7eebf8b3f/host.log` | 75977 | `5ef944954c1c43e3fb9b0fa17a030aa287b4dc41bd15f1bc732e88b7ce7adc3d` |
| `artifacts/studio-first-visit/9d489483a4124e0c900f35a7eebf8b3f/http.json` | 5457 | `f2b19daae951ed4436c35694c6779e28fa082664758d9d311414c87cde70baf6` |
| `artifacts/studio-first-visit/9d489483a4124e0c900f35a7eebf8b3f/initial-sql-currency.json` | 269 | `a6abc03babceeb03a74c758d008bb1424e687f508c7f492af993f796cac36d7c` |
| `artifacts/studio-first-visit/9d489483a4124e0c900f35a7eebf8b3f/migration.log` | 396704 | `f21e8f92ac1de479351a52f31d3af2e3cdc3469e60d4e4839d0ab34506b5f8ed` |
| `artifacts/studio-first-visit/9d489483a4124e0c900f35a7eebf8b3f/product-currency-sql.json` | 231 | `12dc981f4b83e266940655d7617510fd430fa2f8706be8d3e1911f0b58ccca6b` |
| `artifacts/studio-first-visit/9d489483a4124e0c900f35a7eebf8b3f/read-only.done` | 6 | `12e5cb1d6521d6a9f0369978cca6ec93c52582896608bf12882764ae21a0c6e4` |
| `artifacts/studio-first-visit/9d489483a4124e0c900f35a7eebf8b3f/read-only.request` | 17 | `23d38606339fac5ed5d00d47f4cfa63ffe2635176edbd4b8164d0aac7192a5cb` |
| `artifacts/studio-first-visit/9d489483a4124e0c900f35a7eebf8b3f/restart.done` | 6 | `12e5cb1d6521d6a9f0369978cca6ec93c52582896608bf12882764ae21a0c6e4` |
| `artifacts/studio-first-visit/9d489483a4124e0c900f35a7eebf8b3f/restart.request` | 17 | `23d38606339fac5ed5d00d47f4cfa63ffe2635176edbd4b8164d0aac7192a5cb` |
| `artifacts/studio-first-visit/9d489483a4124e0c900f35a7eebf8b3f/results.json` | 3166 | `b432718e39861d8b86ac338d24d0c31e3c3ce79ef80d25081eb64043ba850784` |
| `artifacts/studio-first-visit/9d489483a4124e0c900f35a7eebf8b3f/run.json` | 397 | `7bc879985cb8bc046d41b121028dcceed5c89502b1d3bb47f77ffce2b9bcd171` |
| `artifacts/studio-first-visit/9d489483a4124e0c900f35a7eebf8b3f/stop.done` | 6 | `12e5cb1d6521d6a9f0369978cca6ec93c52582896608bf12882764ae21a0c6e4` |
| `artifacts/studio-first-visit/9d489483a4124e0c900f35a7eebf8b3f/stop.request` | 17 | `23d38606339fac5ed5d00d47f4cfa63ffe2635176edbd4b8164d0aac7192a5cb` |
| `artifacts/studio-first-visit/9d489483a4124e0c900f35a7eebf8b3f/browser/.last-run.json` | 45 | `91d1c43004802cd49950d78eb11c8fa7d05da8ffffe219a8b13b2f561bc00903` |
| `artifacts/studio-first-visit/9d489483a4124e0c900f35a7eebf8b3f/browser/first-visit.png` | 394776 | `2c5b58a212149edda8fc1441756f9f5f4161621d87cf08e1f7f7029d7d43393d` |
| `artifacts/studio-first-visit/9d489483a4124e0c900f35a7eebf8b3f/browser/studio-results.json` | 13919 | `66aa7b5716dd3c3009a6a5d98e5ffb450f0b8339aa83922650a4d523e9dfd735` |
| `artifacts/studio-first-visit/9d489483a4124e0c900f35a7eebf8b3f/keys/key-8909bb7e-fdcc-4ce4-88d7-3b077a4a3c21.xml` | 1016 | `f7086f87cd4eb1e8459d101b95086cd364a968633d8e266325b39b7279a63f8a` |
| `artifacts/studio-first-visit/a77215ce3b434014a565354c47aa383e/bootstrap.log` | 16465 | `c75c2d28a371ffdd1e7a0ab2f2c872990ccf336af7a5d30450ffe01efe2f7314` |
| `artifacts/studio-first-visit/a77215ce3b434014a565354c47aa383e/host-error.log` | 0 | `e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855` |
| `artifacts/studio-first-visit/a77215ce3b434014a565354c47aa383e/host.log` | 16074 | `280f3a8599a83ea96ac1072c6e8f53c7604436e7605a7e9fa2243bd5c6d4f925` |
| `artifacts/studio-first-visit/a77215ce3b434014a565354c47aa383e/http.json` | 660 | `5ab67561aba8608e2cfa27014ad3bdceec3da11406d6480f982933b524be0239` |
| `artifacts/studio-first-visit/a77215ce3b434014a565354c47aa383e/migration.log` | 396706 | `fb5f4653b8ddc6dbf1765c3777c41021ebbee0d983e0f422b4ded61cce4da767` |
| `artifacts/studio-first-visit/a77215ce3b434014a565354c47aa383e/run.json` | 397 | `362a144915378633e70ae6ed45b78c5d9abb22c7eb89fd90d54fa5537961f218` |
| `artifacts/studio-first-visit/a77215ce3b434014a565354c47aa383e/keys/key-9ba14e00-a670-4286-a9ae-209679f249ff.xml` | 1016 | `76980a3e9417d62fc75386fce82bfbf2c8b2d15f5e7852219b4aaf306e6f08c3` |
| `artifacts/studio-first-visit/a8efdd4d03cf4cfc961db0877397200f/bootstrap.log` | 16464 | `53f8ca269ed3d17b10f0c2ee377e144e71d5cf471cc9ae4991f17093f3044bd2` |
| `artifacts/studio-first-visit/a8efdd4d03cf4cfc961db0877397200f/browser-error.log` | 352 | `d989e9caf40300c2a85573b8c2b5f7c81adfd8d3c11d47388d7c377f8cb8e893` |
| `artifacts/studio-first-visit/a8efdd4d03cf4cfc961db0877397200f/browser.log` | 2284 | `828409ecc6fdfc44f84b8b5b0cbca133caae3244a4fffc752d4cae81c3caadc5` |
| `artifacts/studio-first-visit/a8efdd4d03cf4cfc961db0877397200f/final-sql-state.json` | 503 | `8f9650b8735bee3cbc90f1ddf4e2b7c52acbd35379ac88c6f4598c724be920f1` |
| `artifacts/studio-first-visit/a8efdd4d03cf4cfc961db0877397200f/host-error.log` | 0 | `e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855` |
| `artifacts/studio-first-visit/a8efdd4d03cf4cfc961db0877397200f/host.log` | 185155 | `3ce3d90a69b75319929d09823b4f90f86f1b539de1943c8de44d6d65451aba49` |
| `artifacts/studio-first-visit/a8efdd4d03cf4cfc961db0877397200f/http.json` | 2955 | `2c90841689dda7cfa95c33c42f6e56c69256b86a3f986d7c1149b9f3d08c4f93` |
| `artifacts/studio-first-visit/a8efdd4d03cf4cfc961db0877397200f/initial-sql-currency.json` | 269 | `8f5f668c69ad0ae2684a0460a83f3c5be7b1a69e31442c799e7bb527b7fde2ad` |
| `artifacts/studio-first-visit/a8efdd4d03cf4cfc961db0877397200f/migration.log` | 396706 | `feeaf9c7f7e31640684206d7b5a2865702ed577e216cef1ea35bda9e1a38012b` |
| `artifacts/studio-first-visit/a8efdd4d03cf4cfc961db0877397200f/results.json` | 1210 | `a7e35ee4cd3c6b608192c0dc947b59dc323c1e595b751e8115d2d8f5387d980e` |
| `artifacts/studio-first-visit/a8efdd4d03cf4cfc961db0877397200f/run.json` | 397 | `823dd10abf0ab5cdd59c48a0afca0696f56d108491bddbe6ff34bd2843284e64` |
| `artifacts/studio-first-visit/a8efdd4d03cf4cfc961db0877397200f/browser/.last-run.json` | 96 | `3ed50683a52fcd84168c50d8432b155118f0aa488cddba3ddd796fe61ce273e3` |
| `artifacts/studio-first-visit/a8efdd4d03cf4cfc961db0877397200f/browser/studio-results.json` | 305 | `7053d4eaa036d0082f9b476bedd80daaa24d32a43272db9b0336b298707a9fb3` |
| `artifacts/studio-first-visit/a8efdd4d03cf4cfc961db0877397200f/browser/test-failed-1.png` | 62327 | `e824807bcc6151b769d30aae943c5eed56bad9c73fd5f62edf2470581d3184de` |
| `artifacts/studio-first-visit/a8efdd4d03cf4cfc961db0877397200f/keys/key-ffe7b014-6c92-44ed-a26f-01cf3b3a7b71.xml` | 1016 | `15e059997226edba5e4f6bfcfc3b3805cc71365a7f3b9101d38337d2be8febf6` |
| `artifacts/studio-first-visit/b34ac3d7e3cb4fb4b09ea27b3906b8e9/bootstrap.log` | 16465 | `c3e886c1c98933d5d97ce3913c3a8d5647c08d7cd7b0f471026d2fe0114691a7` |
| `artifacts/studio-first-visit/b34ac3d7e3cb4fb4b09ea27b3906b8e9/browser-error.log` | 350 | `5d250952d9ef5b357682df21ae746f2ff1fcb14de5408ba177a294ecb7d1786f` |
| `artifacts/studio-first-visit/b34ac3d7e3cb4fb4b09ea27b3906b8e9/browser.log` | 2203 | `9d6a8a1adfa7e7d23f115df34fcabfd37a015a5faca4da4413cd9bcdf3e75ef4` |
| `artifacts/studio-first-visit/b34ac3d7e3cb4fb4b09ea27b3906b8e9/final-sql-state.json` | 503 | `b9e797d70aa4b59f3515d12511000446495b94ddd64f5eaf0f50371410428147` |
| `artifacts/studio-first-visit/b34ac3d7e3cb4fb4b09ea27b3906b8e9/host-error.log` | 0 | `e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855` |
| `artifacts/studio-first-visit/b34ac3d7e3cb4fb4b09ea27b3906b8e9/host.log` | 211282 | `faeccaa370170b34ffb995f7042642640f51f20d658978152faf172e30e11563` |
| `artifacts/studio-first-visit/b34ac3d7e3cb4fb4b09ea27b3906b8e9/http.json` | 2743 | `18bde71631f40dabc8a736f706a0907822e32a672364bb8432491ae866407943` |
| `artifacts/studio-first-visit/b34ac3d7e3cb4fb4b09ea27b3906b8e9/initial-sql-currency.json` | 269 | `8404fa6e1785ddf6b79a13befc59d8503a41627ebbf594a026b5b9d47b672897` |
| `artifacts/studio-first-visit/b34ac3d7e3cb4fb4b09ea27b3906b8e9/migration.log` | 396708 | `944c28ed4bb472d1bc45cf485b3ca23449896ed29169041b454bd8b2d5052f37` |
| `artifacts/studio-first-visit/b34ac3d7e3cb4fb4b09ea27b3906b8e9/results.json` | 1067 | `775b2aea428cec4b5144480c66561c4d11c669fbea0f3fabaced13c8d0e7d6c9` |
| `artifacts/studio-first-visit/b34ac3d7e3cb4fb4b09ea27b3906b8e9/run.json` | 397 | `7104ba4f193602c0001cc21d41bc54aacef7947dec7f02c204f3a59110e28ee1` |
| `artifacts/studio-first-visit/b34ac3d7e3cb4fb4b09ea27b3906b8e9/keys/key-3da5a41e-bc62-4c80-8748-fafd122ddd1d.xml` | 1016 | `924215085e44efce3ff4d6a0cd2a1bf07f3d3e0408ec638ca43a91c4f9ca33c1` |
| `artifacts/studio-first-visit/cf5dc926bf1049d99af59b6c66dce417/bootstrap.log` | 16465 | `a0aa0258bf58fe7cefa12af27e02871079a484ad6fa009528eb6e1a04e3a326d` |
| `artifacts/studio-first-visit/cf5dc926bf1049d99af59b6c66dce417/browser-error.log` | 352 | `e5178e95b15f450d5cab64837f2b5f2f08270d794d4c7658e79a41b9ccc1cbf0` |
| `artifacts/studio-first-visit/cf5dc926bf1049d99af59b6c66dce417/browser.log` | 2070 | `fbcb0394221cdd3648d5d28bba64a6f544ed57a88dc03547e886bb2cba656984` |
| `artifacts/studio-first-visit/cf5dc926bf1049d99af59b6c66dce417/host-error.log` | 0 | `e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855` |
| `artifacts/studio-first-visit/cf5dc926bf1049d99af59b6c66dce417/host.log` | 206883 | `1083a1be1e73392a61dfc4d01c3cc0872f95b549668a8f4b2eac233f700cc156` |
| `artifacts/studio-first-visit/cf5dc926bf1049d99af59b6c66dce417/http.json` | 1626 | `bedc17cf5fa262b9eb70461ca9f1589e8f1e71988f58f909e26d8aacd970ca28` |
| `artifacts/studio-first-visit/cf5dc926bf1049d99af59b6c66dce417/migration.log` | 396709 | `cdb72526a81139e83c5e04764ba692e68ac55b0796ea84e654061ddd3fe8cdb6` |
| `artifacts/studio-first-visit/cf5dc926bf1049d99af59b6c66dce417/results.json` | 546 | `cfaf8fece48fb5a4bbb524d2240595aa36325be0a1e0ac0e7dba380db9d1b9e5` |
| `artifacts/studio-first-visit/cf5dc926bf1049d99af59b6c66dce417/run.json` | 397 | `5a0835d0f274f4aadc4b5e3be79dcf88cec41d3d61a25b7eb6186ca3003495b0` |
| `artifacts/studio-first-visit/cf5dc926bf1049d99af59b6c66dce417/keys/key-5c28902a-00bd-4702-bb26-eed28c6c9033.xml` | 1016 | `964df7d236dad7c753199b31599673919c1c8893cc9d8ca39fa89ca42dc9e05c` |
| `artifacts/ui-cleanup/scope-preservation.json` | 2641 | `846e932d7a907ce60a66147220525652b53b8931e44d68b91b3635b985ff7da0` |

## Phụ lục 2 — Remaining status paths

Đây là status sau push; modified/deleted/untracked đều giữ local, không nằm trong các commit mới trừ khi đã nêu trong allowlist. D = deletion có sẵn trước nhiệm vụ, không phải thao tác cleanup đã thực hiện.

### backend

| Git status | Path |
|---|---|
| ` M` | `Directory.Packages.props` |
| ` M` | `UnicoreCRM.slnx` |
| ` M` | `scripts/AiProviderVerifier/Program.cs` |
| ` M` | `scripts/AiProviderVerifier/UnicoreCRM.AI.ProviderVerifier.csproj` |
| ` M` | `scripts/verify-ai-assistant.ps1` |
| ` M` | `src/UnicoreCRM.AI/Gateway/AiAdvisoryApplication.cs` |
| ` M` | `src/UnicoreCRM.AI/Gateway/AiConfigurationApplication.cs` |
| ` M` | `src/UnicoreCRM.AI/Gateway/AiOperationResults.cs` |
| ` M` | `src/UnicoreCRM.AI/Gateway/ProactiveSuggestionApplication.cs` |
| ` M` | `src/UnicoreCRM.AI/Prompts/AiPromptComposer.cs` |
| ` M` | `src/UnicoreCRM.AI/Providers/AiProvider.cs` |
| ` M` | `src/UnicoreCRM.AI/Providers/AiProviderCatalog.cs` |
| ` M` | `src/UnicoreCRM.AI/Providers/DevelopmentDeterministicProviderHttpHandler.cs` |
| ` M` | `src/UnicoreCRM.AI/Providers/GeminiAiProvider.cs` |
| ` M` | `src/UnicoreCRM.AI/Providers/OpenAiProvider.cs` |
| ` M` | `src/UnicoreCRM.AI/Providers/ProductionAiProviderAdapter.cs` |
| ` M` | `src/UnicoreCRM.AI/Providers/ProvidersModule.cs` |
| ` M` | `src/UnicoreCRM.AI/Providers/WorkspaceAiProviderResolver.cs` |
| ` M` | `src/UnicoreCRM.AI/Providers/WorkspaceProductionAiProvider.cs` |
| ` M` | `src/UnicoreCRM.ApiHost/Program.cs` |
| ` M` | `src/UnicoreCRM.ApiHost/UnicoreCRM.ApiHost.csproj` |
| ` M` | `src/UnicoreCRM.ApiHost/appsettings.Development.json` |
| ` M` | `src/UnicoreCRM.Billing/Payments/Infrastructure/Persistence/Migrations/PaymentsDbContextModelSnapshot.cs` |
| ` M` | `src/UnicoreCRM.Billing/Payments/Infrastructure/Persistence/PaymentsDbContext.cs` |
| ` M` | `src/UnicoreCRM.Billing/Payments/PaymentsModule.cs` |
| ` M` | `src/UnicoreCRM.Billing/UnicoreCRM.Billing.csproj` |
| ` D` | `src/UnicoreCRM.Communications/Application/ChannelAccessFacts.cs` |
| ` M` | `src/UnicoreCRM.Communications/Application/ChannelManagement.cs` |
| ` D` | `src/UnicoreCRM.Communications/Application/ConversationAccessFacts.cs` |
| ` M` | `src/UnicoreCRM.Communications/Application/ConversationClaimer.cs` |
| ` M` | `src/UnicoreCRM.Crm/Contacts/Application/Common/ContactsApplication.cs` |
| ` M` | `src/UnicoreCRM.Crm/Contacts/Application/CustomerParticipants/Participants.cs` |
| ` M` | `src/UnicoreCRM.Crm/Contacts/ContactsModule.cs` |
| ` M` | `src/UnicoreCRM.Crm/Contacts/Contracts/CustomerParticipants.cs` |
| ` M` | `src/UnicoreCRM.Crm/Contacts/Infrastructure/Persistence/EfContactsPersistence.cs` |
| ` D` | `src/UnicoreCRM.Crm/Customers/Application/Health/CustomerAttentionReader.cs` |
| ` D` | `src/UnicoreCRM.Crm/Customers/Application/Health/ProactiveCustomerHealthReader.cs` |
| ` M` | `src/UnicoreCRM.Crm/Customers/Application/ListCustomers/Handler.cs` |
| ` M` | `src/UnicoreCRM.Crm/Customers/Contracts/CustomerContracts.cs` |
| ` M` | `src/UnicoreCRM.Crm/Customers/CustomersModule.cs` |
| ` M` | `src/UnicoreCRM.Crm/Deals/Application/ArchiveDealsBatch/Handler.cs` |
| ` M` | `src/UnicoreCRM.Crm/Deals/Application/ChangeDealStage/Handler.cs` |
| ` M` | `src/UnicoreCRM.Crm/Deals/Application/Common/DealCommandSupport.cs` |
| ` M` | `src/UnicoreCRM.Crm/Deals/Application/Common/DealMutationExecution.cs` |
| ` M` | `src/UnicoreCRM.Crm/Deals/Application/Common/DealProjection.cs` |
| ` M` | `src/UnicoreCRM.Crm/Deals/Application/Common/DealRecordAccess.cs` |
| ` M` | `src/UnicoreCRM.Crm/Deals/Application/Common/DealStages.cs` |
| ` M` | `src/UnicoreCRM.Crm/Deals/Application/Common/DealValidation.cs` |
| ` M` | `src/UnicoreCRM.Crm/Deals/Application/Common/DealsApplication.cs` |
| ` M` | `src/UnicoreCRM.Crm/Deals/Application/CreateDeal/CreateDealValidation.cs` |
| ` M` | `src/UnicoreCRM.Crm/Deals/Application/CreateDeal/Handler.cs` |
| ` M` | `src/UnicoreCRM.Crm/Deals/Application/CreateLeadOpportunity/Handler.cs` |
| ` M` | `src/UnicoreCRM.Crm/Deals/Application/ListDeals/Handler.cs` |
| ` M` | `src/UnicoreCRM.Crm/Deals/Application/MarkDealWon/Handler.cs` |
| ` M` | `src/UnicoreCRM.Crm/Deals/Application/ReplaceDealProfile/Handler.cs` |
| ` M` | `src/UnicoreCRM.Crm/Deals/Application/UpdateDealForecast/Handler.cs` |
| ` M` | `src/UnicoreCRM.Crm/Deals/Contracts/DealContracts.cs` |
| ` M` | `src/UnicoreCRM.Crm/Deals/Contracts/DealsEndpoints.cs` |
| ` M` | `src/UnicoreCRM.Crm/Deals/DealsModule.cs` |
| ` M` | `src/UnicoreCRM.Crm/Deals/Domain/Deal.cs` |
| ` M` | `src/UnicoreCRM.Crm/Deals/Infrastructure/Persistence/DealsDbContext.cs` |
| ` M` | `src/UnicoreCRM.Crm/Deals/Infrastructure/Persistence/EfDealsPersistence.cs` |
| ` M` | `src/UnicoreCRM.Crm/Deals/Infrastructure/Persistence/Migrations/DealsDbContextModelSnapshot.cs` |
| ` M` | `src/UnicoreCRM.Crm/Organizations/Application/ResolveCustomerSubject/Participant.cs` |
| ` M` | `src/UnicoreCRM.Crm/Organizations/Contracts/CustomerSubjectParticipant.cs` |
| ` M` | `src/UnicoreCRM.Crm/Organizations/OrganizationsModule.cs` |
| ` M` | `src/UnicoreCRM.Crm/UnicoreCRM.Crm.csproj` |
| ` M` | `src/UnicoreCRM.Platform/AccessControl/AccessControlModule.cs` |
| ` D` | `src/UnicoreCRM.Platform/AccessControl/Application/Common/ServiceAccessAuthorizer.cs` |
| ` M` | `src/UnicoreCRM.Platform/AccessControl/Application/Common/WorkspaceCapabilityPolicy.cs` |
| ` M` | `src/UnicoreCRM.Platform/UnicoreCRM.Platform.csproj` |
| ` M` | `src/UnicoreCRM.Platform/Workspace/Application/Common/StudioApplication.cs` |
| ` M` | `src/UnicoreCRM.Platform/Workspace/Infrastructure/Persistence/DevelopmentWorkspaceBootstrap.cs` |
| ` M` | `src/UnicoreCRM.Platform/Workspace/Infrastructure/WorkspaceOptions.cs` |
| ` M` | `src/UnicoreCRM.Platform/Workspace/WorkspaceModule.cs` |
| ` M` | `src/UnicoreCRM.Sales/Orders/OrdersModule.cs` |
| ` M` | `src/UnicoreCRM.Sales/Products/ProductsModule.cs` |
| ` M` | `src/UnicoreCRM.Sales/Quotes/Application/Common/QuoteProjection.cs` |
| ` M` | `src/UnicoreCRM.Sales/Quotes/Application/Common/QuoteRecordAccess.cs` |
| ` M` | `src/UnicoreCRM.Sales/Quotes/Application/Common/QuotesApplication.cs` |
| ` M` | `src/UnicoreCRM.Sales/Quotes/Application/GetQuote/Handler.cs` |
| ` M` | `src/UnicoreCRM.Sales/Quotes/Application/ListQuotes/Handler.cs` |
| ` M` | `src/UnicoreCRM.Sales/Quotes/Application/ProvideQuoteRecordAccessFacts/QuoteRecordAccessFactProvider.cs` |
| ` M` | `src/UnicoreCRM.Sales/Quotes/Contracts/QuoteContracts.cs` |
| ` M` | `src/UnicoreCRM.Sales/Quotes/Contracts/QuotesEndpoints.cs` |
| ` M` | `src/UnicoreCRM.Sales/Quotes/Domain/Quote.cs` |
| ` M` | `src/UnicoreCRM.Sales/Quotes/Infrastructure/Persistence/EfQuotesPersistence.cs` |
| ` M` | `src/UnicoreCRM.Sales/Quotes/Infrastructure/Persistence/Migrations/QuotesDbContextModelSnapshot.cs` |
| ` M` | `src/UnicoreCRM.Sales/Quotes/Infrastructure/Persistence/QuotesDbContext.cs` |
| ` M` | `src/UnicoreCRM.Sales/Quotes/QuotesModule.cs` |
| ` M` | `src/UnicoreCRM.Workflows/Atomic/Application/ConvertLeadToCustomer/Handler.cs` |
| ` M` | `src/UnicoreCRM.Workflows/Atomic/Application/HandoverLead/Handler.cs` |
| ` M` | `src/UnicoreCRM.Workflows/Atomic/Application/QualifyLeadForNurture/Handler.cs` |
| ` M` | `src/UnicoreCRM.Workflows/Atomic/Application/QualifyLeadForOpportunity/Handler.cs` |
| ` M` | `src/UnicoreCRM.Workflows/Atomic/AtomicModule.cs` |
| ` M` | `src/UnicoreCRM.Workflows/Durable/Application/ProvisionInitialWorkspace/InitialWorkspaceAccessCompletion.cs` |
| ` M` | `src/UnicoreCRM.Workflows/Durable/Application/ProvisionInitialWorkspace/InitialWorkspaceProvisioningResumeService.cs` |
| ` M` | `src/UnicoreCRM.Workflows/Durable/DurableModule.cs` |
| ` D` | `src/UnicoreCRM.Workflows/Durable/Infrastructure/InitialWorkspaceProvisioningOptions.cs` |
| ` M` | `tests/UnicoreCRM.ArchitectureTests/ArchitectureInventory.cs` |
| ` M` | `tests/UnicoreCRM.ArchitectureTests/ArchitectureTests.cs` |
| ` M` | `tests/UnicoreCRM.ArchitectureTests/CheckerRegressionTests.cs` |
| `??` | `backend-work/evidence/CRM-AI-002/build.json` |
| `??` | `backend-work/evidence/LC-CA-001-cursor-20261012-final/cursor-build.json` |
| `??` | `backend-work/evidence/LC-CA-001-cursor-20261012-final/cursor-focused.json` |
| `??` | `backend-work/evidence/LC-CA-001-cursor-20261012/cursor-build.json` |
| `??` | `backend-work/evidence/LC-CA-001-cursor-20261012/cursor-focused.json` |
| `??` | `backend-work/evidence/UCRM-ARCHITECTURE-CHECKER-CORRECTIONS-V2-20261020-BACKEND/build.json` |
| `??` | `backend-work/evidence/UCRM-ARCHITECTURE-GATE-FINDINGS-AND-ROOT-ADMISSION-20261019/architecture.json` |
| `??` | `backend-work/evidence/UCRM-ARCHITECTURE-GATE-IMPLEMENTATION-20261019/architecture.json` |
| `??` | `backend-work/evidence/UCRM-CONTACT-CONNECTED-ACCEPTANCE-V2/build.json` |
| `??` | `backend-work/evidence/UCRM-ROOT-CONTROL-PLANE-ADMISSION-AND-ARCHITECTURE-CHECKER-CORRECTIONS-20261019/architecture.json` |
| `??` | `backend-work/evidence/UCRM-TASKS-CONTACT-REFERENCE-INTEGRITY-20261019/architecture.json` |
| `??` | `backend-work/evidence/UCRM-TASKS-CONTACT-REFERENCE-INTEGRITY-20261019/build.json` |
| `??` | `backend-work/evidence/UCRM-TASKS-CREATE-CONCURRENCY-DEADLOCK-REMEDIATION-20261019/architecture.json` |
| `??` | `backend-work/evidence/UCRM-TASKS-CREATE-CONCURRENCY-DEADLOCK-REMEDIATION-20261019/build.json` |
| `??` | `backend-work/handoffs/BE-PROACTIVE-VERIFIER-BUILD-REMEDIATION-20261015/analysis.json` |
| `??` | `backend-work/handoffs/BE-PROACTIVE-VERIFIER-BUILD-REMEDIATION-20261015/architecture.json` |
| `??` | `backend-work/handoffs/BE-PROACTIVE-VERIFIER-CORPUS-COMPATIBILITY-20261015/analysis.json` |
| `??` | `backend-work/handoffs/BE-PROACTIVE-VERIFIER-CORPUS-COMPATIBILITY-20261015/architecture.json` |
| `??` | `backend-work/handoffs/PRIMARY-SOURCE-INTEGRATION-20261015-BE/analysis.json` |
| `??` | `backend-work/handoffs/PRIMARY-SOURCE-INTEGRATION-20261015-BE/architecture.json` |
| `??` | `backend-work/handoffs/PRIMARY-SOURCE-INTEGRATION-20261015-BE/change.json` |
| `??` | `backend-work/handoffs/UCRM-ARCHITECTURE-GATE-IMPLEMENTATION-20261019/analysis.json` |
| `??` | `backend-work/handoffs/UCRM-ARCHITECTURE-GATE-IMPLEMENTATION-20261019/architecture.json` |
| `??` | `backend-work/handoffs/UCRM-ARCHITECTURE-GATE-IMPLEMENTATION-20261019/change.json` |
| `??` | `backend-work/handoffs/UCRM-TASKS-CONTACT-REFERENCE-INTEGRITY-20261019/analysis.json` |
| `??` | `backend-work/handoffs/UCRM-TASKS-CONTACT-REFERENCE-INTEGRITY-20261019/architecture.json` |
| `??` | `backend-work/handoffs/UCRM-TASKS-CONTACT-REFERENCE-INTEGRITY-20261019/change.json` |
| `??` | `backend-work/handoffs/UCRM-TASKS-CREATE-CONCURRENCY-DEADLOCK-REMEDIATION-20261019/analysis.json` |
| `??` | `backend-work/handoffs/UCRM-TASKS-CREATE-CONCURRENCY-DEADLOCK-REMEDIATION-20261019/architecture.json` |
| `??` | `backend-work/handoffs/UCRM-TASKS-CREATE-CONCURRENCY-DEADLOCK-REMEDIATION-20261019/change.json` |
| `??` | `backend-work/runtime-context/UCRM-ORGANIZATION-TASK-REFERENCE-INTEGRITY-AND-MEETING/architecture-controls/Controls.csproj` |
| `??` | `backend-work/runtime-context/UCRM-ORGANIZATION-TASK-REFERENCE-INTEGRITY-AND-MEETING/architecture-controls/Program.cs` |
| `??` | `backend-work/state/BE-PROACTIVE-VERIFIER-BUILD-REMEDIATION-20261015.json` |
| `??` | `backend-work/state/BE-PROACTIVE-VERIFIER-CORPUS-COMPATIBILITY-20261015.json` |
| `??` | `backend-work/state/PRIMARY-SOURCE-INTEGRATION-20261015-BE.json` |
| `??` | `backend-work/state/UCRM-ARCHITECTURE-CHECKER-CORRECTIONS-V2-20261020-BACKEND.json` |
| `??` | `backend-work/state/UCRM-ARCHITECTURE-GATE-IMPLEMENTATION-20261019.json` |
| `??` | `backend-work/state/UCRM-CONTACT-CONNECTED-ACCEPTANCE-V2.json` |
| `??` | `backend-work/state/UCRM-CUSTOMER-ACTIVITY-REFERENCE-HISTORY-BACKEND.json` |
| `??` | `backend-work/state/UCRM-CUSTOMER-TASK-REFERENCE-CONNECTED-BACKEND.json` |
| `??` | `backend-work/state/UCRM-ORGANIZATION-EDIT-DESCRIPTOR-REMEDIATION.json` |
| `??` | `backend-work/state/UCRM-ORGANIZATION-OVERVIEW-FLS-PROJECTION-REMEDIATION.json` |
| `??` | `backend-work/state/UCRM-ORGANIZATION-TASK-REFERENCE-INTEGRITY-AND-MEETING-BACKEND.json` |
| `??` | `backend-work/state/UCRM-TASKS-CONTACT-REFERENCE-INTEGRITY-20261019.json` |
| `??` | `backend-work/state/UCRM-TASKS-CREATE-CONCURRENCY-DEADLOCK-REMEDIATION-20261019.json` |
| `??` | `backend-work/tasks/PRIMARY-SOURCE-INTEGRATION-20261015-BE.json` |
| `??` | `backend-work/tasks/UCRM-ARCHITECTURE-CHECKER-CORRECTIONS-V2-20261020-BACKEND.json` |
| `??` | `backend-work/tasks/UCRM-CONTACT-CONNECTED-ACCEPTANCE-V2.json` |
| `??` | `backend-work/tasks/UCRM-CUSTOMER-ACTIVITY-REFERENCE-HISTORY-BACKEND.json` |
| `??` | `backend-work/tasks/UCRM-CUSTOMER-TASK-REFERENCE-CONNECTED-BACKEND.json` |
| `??` | `backend-work/tasks/UCRM-ORGANIZATION-EDIT-DESCRIPTOR-REMEDIATION.json` |
| `??` | `backend-work/tasks/UCRM-ORGANIZATION-OVERVIEW-FLS-PROJECTION-REMEDIATION.json` |
| `??` | `backend-work/tasks/UCRM-ORGANIZATION-TASK-REFERENCE-INTEGRITY-AND-MEETING-BACKEND.json` |
| `??` | `docs/development/deal-pipelines.md` |
| `??` | `docs/development/quote-acceptance-identity-authority-b6-3-acceptance.md` |
| `??` | `docs/development/quote-acceptance-identity-authority-b6-3-policy-decisions.md` |
| `??` | `docs/development/quote-acceptance-proof-scanner-boundary.md` |
| `??` | `docs/development/quote-acceptance-proof-storage-design.md` |
| `??` | `docs/development/quote-acceptance-proof-storage-verification.md` |
| `??` | `docs/development/quote-acceptance-trusted-scanner-b6-2-acceptance.md` |
| `??` | `docs/development/quote-acceptance-trusted-scanner-policy-decisions.md` |
| `??` | `docs/development/quote-approval-configuration.md` |
| `??` | `docs/development/quote-core-contract-proposal.md` |
| `??` | `docs/development/quote-customer-acceptance-policy-decisions.md` |
| `??` | `docs/development/quote-customer-decision-architecture.md` |
| `??` | `docs/development/quote-delivery-policy-decisions.md` |
| `??` | `docs/development/quote-document-issuance-acceptance.md` |
| `??` | `docs/development/quote-document-issuance-policy-decisions.md` |
| `??` | `docs/development/quote-independent-review-b6-4-acceptance.md` |
| `??` | `docs/development/quote-independent-review-b6-4-policy.md` |
| `??` | `docs/development/quote-lifecycle-b4-acceptance.md` |
| `??` | `docs/development/quote-lifecycle-b4-policy-decisions.md` |
| `??` | `docs/development/quote-lifecycle-policy-decisions.md` |
| `??` | `docs/development/quote-manual-acceptance-product-decision.md` |
| `??` | `docs/development/quote-manual-source-workflow-b6-3b-acceptance.md` |
| `??` | `docs/development/quote-manual-source-workflow-b6-3b-policy.md` |
| `??` | `docs/development/quote-verified-customer-acceptance-policy-decisions.md` |
| `??` | `docs/development/quote-verified-customer-acceptance-verification.md` |
| `??` | `docs/development/realistic-demo-workspace.md` |
| `??` | `docs/development/unicorecrm-canonical-api-extension-acceptance.md` |
| `??` | `docs/development/unicorecrm-connected-crm-completion-acceptance.md` |
| `??` | `docs/development/unicorecrm-connected-crm-stabilization-acceptance.md` |
| `??` | `docs/development/unicorecrm-control-extension-manual-quote-status.md` |
| `??` | `docs/development/unicorecrm-full-quality-stabilization-acceptance.md` |
| `??` | `docs/development/unicorecrm-manual-attestation-continuation-status.md` |
| `??` | `docs/development/unicorecrm-manual-quote-acceptance-deal-won-acceptance.md` |
| `??` | `docs/development/unicorecrm-manual-quote-acceptance-delivery.md` |
| `??` | `docs/development/unicorecrm-manual-quote-acceptance-functional-acceptance.md` |
| `??` | `docs/development/unicorecrm-opportunity-communications-quality-acceptance.md` |
| `??` | `docs/development/unicorecrm-opportunity-concurrency-recovery-acceptance.md` |
| `??` | `docs/development/unicorecrm-opportunity-intent-lifecycle-acceptance.md` |
| `??` | `docs/development/unicorecrm-reconciled-canonical-adoption-acceptance.md` |
| `??` | `docs/development/unicorecrm-sql-provenance-opportunity-authority-acceptance.md` |
| `??` | `docs/verification/ai-assistant-ui-results.md` |
| `??` | `docs/verification/ai-chat-studio-configuration.md` |
| `??` | `docs/verification/connected-crm-activity-security.md` |
| `??` | `docs/verification/connected-dashboard-design.md` |
| `??` | `docs/verification/connected-dashboard-results.md` |
| `??` | `docs/verification/connected-dashboard-ui-results.md` |
| `??` | `docs/verification/dashboard-navigation-results.md` |
| `??` | `docs/verification/deal-pipeline-connected.md` |
| `??` | `docs/verification/deal-pipeline-final-acceptance.md` |
| `??` | `docs/verification/evidence/ai-assistant-ui/ai-after-checks.json` |
| `??` | `docs/verification/evidence/ai-assistant-ui/ai-after-focused-compact.png` |
| `??` | `docs/verification/evidence/ai-assistant-ui/ai-after-focused-desktop.png` |
| `??` | `docs/verification/evidence/ai-assistant-ui/ai-after-focused-laptop.png` |
| `??` | `docs/verification/evidence/ai-assistant-ui/ai-after-focused-mobile.png` |
| `??` | `docs/verification/evidence/ai-assistant-ui/ai-after-focused-narrow.png` |
| `??` | `docs/verification/evidence/ai-assistant-ui/ai-after-focused-tablet.png` |
| `??` | `docs/verification/evidence/ai-assistant-ui/ai-after-history-mobile.png` |
| `??` | `docs/verification/evidence/ai-assistant-ui/ai-after-loading.png` |
| `??` | `docs/verification/evidence/ai-assistant-ui/ai-after-no-context-compact.png` |
| `??` | `docs/verification/evidence/ai-assistant-ui/ai-after-no-context-desktop.png` |
| `??` | `docs/verification/evidence/ai-assistant-ui/ai-after-no-context-laptop.png` |
| `??` | `docs/verification/evidence/ai-assistant-ui/ai-after-no-context-mobile.png` |
| `??` | `docs/verification/evidence/ai-assistant-ui/ai-after-no-context-narrow.png` |
| `??` | `docs/verification/evidence/ai-assistant-ui/ai-after-no-context-tablet.png` |
| `??` | `docs/verification/evidence/ai-assistant-ui/ai-after-real-contact-result.png` |
| `??` | `docs/verification/evidence/ai-assistant-ui/ai-after-real-deal-result.png` |
| `??` | `docs/verification/evidence/ai-assistant-ui/ai-after-transport-error.png` |
| `??` | `docs/verification/evidence/ai-assistant-ui/ai-after-wide-context.png` |
| `??` | `docs/verification/evidence/ai-assistant-ui/ai-before-checks.json` |
| `??` | `docs/verification/evidence/ai-assistant-ui/ai-before-focused-compact.png` |
| `??` | `docs/verification/evidence/ai-assistant-ui/ai-before-focused-desktop.png` |
| `??` | `docs/verification/evidence/ai-assistant-ui/ai-before-focused-laptop.png` |
| `??` | `docs/verification/evidence/ai-assistant-ui/ai-before-focused-mobile.png` |
| `??` | `docs/verification/evidence/ai-assistant-ui/ai-before-focused-narrow.png` |
| `??` | `docs/verification/evidence/ai-assistant-ui/ai-before-focused-tablet.png` |
| `??` | `docs/verification/evidence/ai-assistant-ui/ai-before-no-context-compact.png` |
| `??` | `docs/verification/evidence/ai-assistant-ui/ai-before-no-context-desktop.png` |
| `??` | `docs/verification/evidence/ai-assistant-ui/ai-before-no-context-laptop.png` |
| `??` | `docs/verification/evidence/ai-assistant-ui/ai-before-no-context-mobile.png` |
| `??` | `docs/verification/evidence/ai-assistant-ui/ai-before-no-context-narrow.png` |
| `??` | `docs/verification/evidence/ai-assistant-ui/ai-before-no-context-tablet.png` |
| `??` | `docs/verification/evidence/ai-assistant-ui/quality/ai-ui-quality.ai-application-contracts.json` |
| `??` | `docs/verification/evidence/ai-assistant-ui/quality/ai-ui-quality.ai-governance-contracts.json` |
| `??` | `docs/verification/evidence/ai-assistant-ui/quality/ai-ui-quality.api-contract.json` |
| `??` | `docs/verification/evidence/ai-assistant-ui/quality/ai-ui-quality.api-management-boundaries.json` |
| `??` | `docs/verification/evidence/ai-assistant-ui/quality/ai-ui-quality.backend-readiness.json` |
| `??` | `docs/verification/evidence/ai-assistant-ui/quality/ai-ui-quality.bilingual-ui.json` |
| `??` | `docs/verification/evidence/ai-assistant-ui/quality/ai-ui-quality.frontend-backend-separation.json` |
| `??` | `docs/verification/evidence/ai-assistant-ui/quality/ai-ui-quality.guidance-contracts.json` |
| `??` | `docs/verification/evidence/ai-assistant-ui/quality/ai-ui-quality.guidance-runtime.json` |
| `??` | `docs/verification/evidence/ai-assistant-ui/quality/ai-ui-quality.presentation-responsibility.json` |
| `??` | `docs/verification/evidence/ai-assistant-ui/quality/ai-ui-quality.quality-pipeline.json` |
| `??` | `docs/verification/evidence/ai-assistant-ui/quality/ai-ui-quality.repository-inventory.json` |
| `??` | `docs/verification/evidence/ai-assistant-ui/quality/ai-ui-quality.shell-ai-experience.json` |
| `??` | `docs/verification/evidence/ai-assistant-ui/quality/ai-ui-quality.strict-core.json` |
| `??` | `docs/verification/evidence/ai-assistant-ui/quality/ai-ui-quality.strict-type-regions.json` |
| `??` | `docs/verification/evidence/ai-assistant-ui/quality/ai-ui-quality.workspace-isolation-contracts.json` |
| `??` | `docs/verification/evidence/ai-assistant-ui/quality/summary.json` |
| `??` | `docs/verification/evidence/ai-assistant-ui/scope-preservation.json` |
| `??` | `docs/verification/evidence/ai-assistant-ui/sql-preservation.json` |
| `??` | `docs/verification/evidence/ai-connected-20261022/UnicoreCRM_AI_2326a155117b4ce6a014899421a73390/playwright/.last-run.json` |
| `??` | `docs/verification/evidence/ai-connected-20261022/UnicoreCRM_AI_2326a155117b4ce6a014899421a73390/playwright/ai-workspace-connected-man-c17df-inistic-provider-transport--ai-workspace-chromium/error-context.md` |
| `??` | `docs/verification/evidence/ai-connected-20261022/UnicoreCRM_AI_2326a155117b4ce6a014899421a73390/playwright/ai-workspace-connected-man-c17df-inistic-provider-transport--ai-workspace-chromium/test-failed-1.png` |
| `??` | `docs/verification/evidence/ai-connected-20261022/UnicoreCRM_AI_2a409e784ee54416bc701961797fbd8a/connected-results.json` |
| `??` | `docs/verification/evidence/ai-connected-20261022/UnicoreCRM_AI_2a409e784ee54416bc701961797fbd8a/journey.json` |
| `??` | `docs/verification/evidence/ai-connected-20261022/UnicoreCRM_AI_2a409e784ee54416bc701961797fbd8a/playwright/.last-run.json` |
| `??` | `docs/verification/evidence/ai-connected-20261022/UnicoreCRM_AI_2a409e784ee54416bc701961797fbd8a/playwright/ai-workspace-connected-man-c17df-inistic-provider-transport--ai-workspace-chromium/customer-ai.png` |
| `??` | `docs/verification/evidence/ai-connected-20261022/UnicoreCRM_AI_6f6adffb15f14edd99d1b42870ff8595/journey.json` |
| `??` | `docs/verification/evidence/ai-connected-20261022/UnicoreCRM_AI_6f6adffb15f14edd99d1b42870ff8595/playwright/.last-run.json` |
| `??` | `docs/verification/evidence/ai-connected-20261022/UnicoreCRM_AI_6f6adffb15f14edd99d1b42870ff8595/playwright/ai-workspace-connected-man-c17df-inistic-provider-transport--ai-workspace-chromium/customer-ai.png` |
| `??` | `docs/verification/evidence/ai-connected-20261022/UnicoreCRM_AI_787e8884d54b4aa19f774bf24912bc47/playwright/.last-run.json` |
| `??` | `docs/verification/evidence/ai-connected-20261022/UnicoreCRM_AI_787e8884d54b4aa19f774bf24912bc47/playwright/ai-workspace-connected-man-c17df-inistic-provider-transport--ai-workspace-chromium/customer-ai.png` |
| `??` | `docs/verification/evidence/ai-connected-20261022/UnicoreCRM_AI_787e8884d54b4aa19f774bf24912bc47/playwright/ai-workspace-connected-man-c17df-inistic-provider-transport--ai-workspace-chromium/error-context.md` |
| `??` | `docs/verification/evidence/ai-connected-20261022/UnicoreCRM_AI_787e8884d54b4aa19f774bf24912bc47/playwright/ai-workspace-connected-man-c17df-inistic-provider-transport--ai-workspace-chromium/test-failed-1.png` |
| `??` | `docs/verification/evidence/ai-connected-20261022/UnicoreCRM_AI_79fd6742af6c49aca141be792fcd7d78/playwright/.last-run.json` |
| `??` | `docs/verification/evidence/ai-connected-20261022/UnicoreCRM_AI_79fd6742af6c49aca141be792fcd7d78/playwright/ai-workspace-connected-man-c17df-inistic-provider-transport--ai-workspace-chromium/error-context.md` |
| `??` | `docs/verification/evidence/ai-connected-20261022/UnicoreCRM_AI_79fd6742af6c49aca141be792fcd7d78/playwright/ai-workspace-connected-man-c17df-inistic-provider-transport--ai-workspace-chromium/test-failed-1.png` |
| `??` | `docs/verification/evidence/ai-connected-20261022/UnicoreCRM_AI_859df9c6a318458ebdaf0c9907f05782/playwright/.last-run.json` |
| `??` | `docs/verification/evidence/ai-connected-20261022/UnicoreCRM_AI_859df9c6a318458ebdaf0c9907f05782/playwright/ai-workspace-connected-man-c17df-inistic-provider-transport--ai-workspace-chromium/error-context.md` |
| `??` | `docs/verification/evidence/ai-connected-20261022/UnicoreCRM_AI_859df9c6a318458ebdaf0c9907f05782/playwright/ai-workspace-connected-man-c17df-inistic-provider-transport--ai-workspace-chromium/test-failed-1.png` |
| `??` | `docs/verification/evidence/ai-connected-20261022/UnicoreCRM_AI_BrowserGrounded_9c30df05c3f2471e9c065a9a394fafc3/journey-no-credential.json` |
| `??` | `docs/verification/evidence/ai-connected-20261022/UnicoreCRM_AI_BrowserGrounded_9c30df05c3f2471e9c065a9a394fafc3/playwright/.last-run.json` |
| `??` | `docs/verification/evidence/ai-connected-20261022/UnicoreCRM_AI_BrowserGrounded_9c30df05c3f2471e9c065a9a394fafc3/playwright/current-workspace-no-crede-fc8b7-elopmentDeterministic-only--ai-workspace-chromium/customer-ai.png` |
| `??` | `docs/verification/evidence/ai-connected-20261022/UnicoreCRM_AI_BrowserGrounded_9c30df05c3f2471e9c065a9a394fafc3/playwright/current-workspace-no-crede-fc8b7-elopmentDeterministic-only--ai-workspace-chromium/studio-no-credential.png` |
| `??` | `docs/verification/evidence/ai-connected-20261022/UnicoreCRM_AI_BrowserGrounded_9c30df05c3f2471e9c065a9a394fafc3/playwright/current-workspace-no-crede-fc8b7-elopmentDeterministic-only--ai-workspace-chromium/workspace-b-after-delayed-response.png` |
| `??` | `docs/verification/evidence/ai-connected-20261022/UnicoreCRM_AI_BrowserLifecycle_7f0c66954f5e400c9ddd7b7a7fd2d31d/journey-no-credential.json` |
| `??` | `docs/verification/evidence/ai-connected-20261022/UnicoreCRM_AI_BrowserLifecycle_7f0c66954f5e400c9ddd7b7a7fd2d31d/playwright/.last-run.json` |
| `??` | `docs/verification/evidence/ai-connected-20261022/UnicoreCRM_AI_BrowserLifecycle_7f0c66954f5e400c9ddd7b7a7fd2d31d/playwright/current-workspace-no-crede-fc8b7-elopmentDeterministic-only--ai-workspace-chromium/customer-ai.png` |
| `??` | `docs/verification/evidence/ai-connected-20261022/UnicoreCRM_AI_BrowserLifecycle_7f0c66954f5e400c9ddd7b7a7fd2d31d/playwright/current-workspace-no-crede-fc8b7-elopmentDeterministic-only--ai-workspace-chromium/studio-no-credential.png` |
| `??` | `docs/verification/evidence/ai-connected-20261022/UnicoreCRM_AI_BrowserLifecycle_7f0c66954f5e400c9ddd7b7a7fd2d31d/playwright/current-workspace-no-crede-fc8b7-elopmentDeterministic-only--ai-workspace-chromium/workspace-b-after-delayed-response.png` |
| `??` | `docs/verification/evidence/ai-connected-20261022/UnicoreCRM_AI_BrowserReview_7769d9908e70483e81da8cd011f00fd1/journey-no-credential.json` |
| `??` | `docs/verification/evidence/ai-connected-20261022/UnicoreCRM_AI_BrowserReview_7769d9908e70483e81da8cd011f00fd1/playwright/.last-run.json` |
| `??` | `docs/verification/evidence/ai-connected-20261022/UnicoreCRM_AI_BrowserReview_7769d9908e70483e81da8cd011f00fd1/playwright/current-workspace-no-crede-fc8b7-elopmentDeterministic-only--ai-workspace-chromium/customer-ai.png` |
| `??` | `docs/verification/evidence/ai-connected-20261022/UnicoreCRM_AI_BrowserReview_7769d9908e70483e81da8cd011f00fd1/playwright/current-workspace-no-crede-fc8b7-elopmentDeterministic-only--ai-workspace-chromium/studio-no-credential.png` |
| `??` | `docs/verification/evidence/ai-connected-20261022/UnicoreCRM_AI_BrowserReview_b31b702633834be098b5b3887288373e/playwright/.last-run.json` |
| `??` | `docs/verification/evidence/ai-connected-20261022/UnicoreCRM_AI_BrowserReview_b31b702633834be098b5b3887288373e/playwright/current-workspace-no-crede-fc8b7-elopmentDeterministic-only--ai-workspace-chromium/error-context.md` |
| `??` | `docs/verification/evidence/ai-connected-20261022/UnicoreCRM_AI_BrowserReview_b31b702633834be098b5b3887288373e/playwright/current-workspace-no-crede-fc8b7-elopmentDeterministic-only--ai-workspace-chromium/test-failed-1.png` |
| `??` | `docs/verification/evidence/ai-connected-20261022/UnicoreCRM_AI_d5081c1abd3f40709a2bc8d410f77c99/journey.json` |
| `??` | `docs/verification/evidence/ai-connected-20261022/UnicoreCRM_AI_d5081c1abd3f40709a2bc8d410f77c99/playwright/.last-run.json` |
| `??` | `docs/verification/evidence/ai-connected-20261022/UnicoreCRM_AI_d5081c1abd3f40709a2bc8d410f77c99/playwright/ai-workspace-connected-man-c17df-inistic-provider-transport--ai-workspace-chromium/customer-ai.png` |
| `??` | `docs/verification/evidence/ai-connected-20261022/UnicoreCRM_AI_f8d73ce066c148b5aaf1d6205fbb796c/playwright/.last-run.json` |
| `??` | `docs/verification/evidence/ai-connected-20261022/UnicoreCRM_AI_f8d73ce066c148b5aaf1d6205fbb796c/playwright/ai-workspace-connected-man-c17df-inistic-provider-transport--ai-workspace-chromium/error-context.md` |
| `??` | `docs/verification/evidence/ai-connected-20261022/UnicoreCRM_AI_f8d73ce066c148b5aaf1d6205fbb796c/playwright/ai-workspace-connected-man-c17df-inistic-provider-transport--ai-workspace-chromium/test-failed-1.png` |
| `??` | `docs/verification/evidence/ai-connected-20261022/UnicoreCRM_AI_fad9be35fecb4351b25c777730afdd0e/playwright/.last-run.json` |
| `??` | `docs/verification/evidence/ai-connected-20261022/UnicoreCRM_AI_fad9be35fecb4351b25c777730afdd0e/playwright/ai-workspace-connected-man-c17df-inistic-provider-transport--ai-workspace-chromium/error-context.md` |
| `??` | `docs/verification/evidence/ai-connected-20261022/UnicoreCRM_AI_fad9be35fecb4351b25c777730afdd0e/playwright/ai-workspace-connected-man-c17df-inistic-provider-transport--ai-workspace-chromium/test-failed-1.png` |
| `??` | `docs/verification/evidence/ai-connected-20261022/architecture/ai-connected.trx` |
| `??` | `docs/verification/evidence/ai-connected-20261022/backend-status.txt` |
| `??` | `docs/verification/evidence/ai-connected-20261022/engineering-record.json` |
| `??` | `docs/verification/evidence/ai-connected-20261022/evidence-manifest.json` |
| `??` | `docs/verification/evidence/ai-connected-20261022/frontend-status.txt` |
| `??` | `docs/verification/evidence/ai-connected-20261022/source-manifest.json` |
| `??` | `docs/verification/evidence/connected-crm-20261021/activity-count.sql` |
| `??` | `docs/verification/evidence/connected-crm-20261021/backend.patch` |
| `??` | `docs/verification/evidence/connected-crm-20261021/baseline-activity-results.json` |
| `??` | `docs/verification/evidence/connected-crm-20261021/browser-contact-after-refresh.png` |
| `??` | `docs/verification/evidence/connected-crm-20261021/browser-http.json` |
| `??` | `docs/verification/evidence/connected-crm-20261021/browser-records.json` |
| `??` | `docs/verification/evidence/connected-crm-20261021/browser-sales-own-history-after-refresh.png` |
| `??` | `docs/verification/evidence/connected-crm-20261021/changes.json` |
| `??` | `docs/verification/evidence/connected-crm-20261021/frontend.patch` |
| `??` | `docs/verification/evidence/connected-crm-20261021/http.json` |
| `??` | `docs/verification/evidence/connected-crm-20261021/quality-summary.json` |
| `??` | `docs/verification/evidence/connected-crm-20261021/regression-comparison.json` |
| `??` | `docs/verification/evidence/connected-crm-20261021/results.json` |
| `??` | `docs/verification/evidence/connected-crm-20261021/retained-test-databases.json` |
| `??` | `docs/verification/evidence/connected-crm-20261021/run.json` |
| `??` | `docs/verification/evidence/connected-crm-20261021/sql-counts.json` |
| `??` | `docs/verification/evidence/connected-dashboard-ui/after-api-summary.json` |
| `??` | `docs/verification/evidence/connected-dashboard-ui/after-browser.json` |
| `??` | `docs/verification/evidence/connected-dashboard-ui/after-data-preservation.json` |
| `??` | `docs/verification/evidence/connected-dashboard-ui/design-after-compact.png` |
| `??` | `docs/verification/evidence/connected-dashboard-ui/design-after-desktop.png` |
| `??` | `docs/verification/evidence/connected-dashboard-ui/design-after-laptop.png` |
| `??` | `docs/verification/evidence/connected-dashboard-ui/design-after-mobile-support.png` |
| `??` | `docs/verification/evidence/connected-dashboard-ui/design-after-mobile-work.png` |
| `??` | `docs/verification/evidence/connected-dashboard-ui/design-after-mobile.png` |
| `??` | `docs/verification/evidence/connected-dashboard-ui/design-after-narrow-support.png` |
| `??` | `docs/verification/evidence/connected-dashboard-ui/design-after-narrow-work.png` |
| `??` | `docs/verification/evidence/connected-dashboard-ui/design-after-narrow.png` |
| `??` | `docs/verification/evidence/connected-dashboard-ui/design-after-tablet.png` |
| `??` | `docs/verification/evidence/connected-dashboard-ui/design-before-compact.png` |
| `??` | `docs/verification/evidence/connected-dashboard-ui/design-before-desktop.png` |
| `??` | `docs/verification/evidence/connected-dashboard-ui/design-before-laptop.png` |
| `??` | `docs/verification/evidence/connected-dashboard-ui/design-before-mobile.png` |
| `??` | `docs/verification/evidence/connected-dashboard-ui/design-before-narrow.png` |
| `??` | `docs/verification/evidence/connected-dashboard-ui/design-before-tablet.png` |
| `??` | `docs/verification/evidence/connected-dashboard-ui/design-loading.png` |
| `??` | `docs/verification/evidence/connected-dashboard-ui/design-network-error.png` |
| `??` | `docs/verification/evidence/connected-dashboard-ui/design-viewports.json` |
| `??` | `docs/verification/evidence/connected-dashboard-ui/expected.json` |
| `??` | `docs/verification/evidence/connected-dashboard-ui/full-verify-output.txt` |
| `??` | `docs/verification/evidence/connected-dashboard-ui/quality-results.json` |
| `??` | `docs/verification/evidence/connected-dashboard-ui/runtime-after-hashes.json` |
| `??` | `docs/verification/evidence/connected-dashboard-ui/unrelated-diff-preservation.json` |
| `??` | `docs/verification/evidence/connected-dashboard/after-api-summary.json` |
| `??` | `docs/verification/evidence/connected-dashboard/after-browser.json` |
| `??` | `docs/verification/evidence/connected-dashboard/after-data-preservation.json` |
| `??` | `docs/verification/evidence/connected-dashboard/before-browser.json` |
| `??` | `docs/verification/evidence/connected-dashboard/before-data-preservation.json` |
| `??` | `docs/verification/evidence/connected-dashboard/dashboard-after.png` |
| `??` | `docs/verification/evidence/connected-dashboard/dashboard-before.png` |
| `??` | `docs/verification/evidence/connected-dashboard/expected.json` |
| `??` | `docs/verification/evidence/connected-dashboard/quality-results.json` |
| `??` | `docs/verification/evidence/connected-dashboard/verify-output.txt` |
| `??` | `docs/verification/evidence/dashboard-navigation/after-api-summary.json` |
| `??` | `docs/verification/evidence/dashboard-navigation/after-browser.json` |
| `??` | `docs/verification/evidence/dashboard-navigation/after-data-preservation.json` |
| `??` | `docs/verification/evidence/dashboard-navigation/expected.json` |
| `??` | `docs/verification/evidence/dashboard-navigation/full-verify-output.txt` |
| `??` | `docs/verification/evidence/dashboard-navigation/hydration-data-preservation.json` |
| `??` | `docs/verification/evidence/dashboard-navigation/hydration-regression.json` |
| `??` | `docs/verification/evidence/dashboard-navigation/local-diff-preservation.json` |
| `??` | `docs/verification/evidence/dashboard-navigation/quality-results.json` |
| `??` | `docs/verification/evidence/dashboard-navigation/route-smoke-output.txt` |
| `??` | `docs/verification/evidence/dashboard-navigation/sidebar-after-authority.json` |
| `??` | `docs/verification/evidence/dashboard-navigation/sidebar-after-collapsed.png` |
| `??` | `docs/verification/evidence/dashboard-navigation/sidebar-after-desktop.png` |
| `??` | `docs/verification/evidence/dashboard-navigation/sidebar-after-laptop.png` |
| `??` | `docs/verification/evidence/dashboard-navigation/sidebar-after-mobile.png` |
| `??` | `docs/verification/evidence/dashboard-navigation/sidebar-before-authority.json` |
| `??` | `docs/verification/evidence/dashboard-navigation/sidebar-before-desktop.png` |
| `??` | `docs/verification/evidence/dashboard-navigation/sidebar-before-laptop.png` |
| `??` | `docs/verification/evidence/dashboard-navigation/sidebar-before-mobile.png` |
| `??` | `docs/verification/evidence/dashboard-navigation/source-hashes.json` |
| `??` | `docs/verification/evidence/gemini-pre-header-20261022/transport-probe.json` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/artifact-hashes.json` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/backend-phase1.patch` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/connected-verification-summary.json` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/consumer-inventory.json` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/frontend-phase1.patch` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/handoff-integrity.json` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/lead-pinned-actual.patch` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/lead-pinned-proposal.patch` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/p0-connected/browser-records.json` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/p0-connected/browser/contact-after-refresh.png` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/p0-connected/browser/http.json` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/p0-connected/browser/records.json` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/p0-connected/browser/sales-own-history-after-refresh.png` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/p0-connected/commit-race.json` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/p0-connected/http.json` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/p0-connected/quote-route-review.json` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/p0-connected/race-review-activity-own.json` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/p0-connected/race-review-race-3cb580aec9974115ae95d2d649f1f507.json` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/p0-connected/results.json` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/p0-connected/run.json` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/p0-connected/sql-counts.json` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/p0-security/commit-race.json` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/p0-security/http.json` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/p0-security/performance-matrix.json` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/p0-security/quote-route-review.json` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/p0-security/race-review-activity-own.json` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/p0-security/race-review-race-b18f4e93e62d48b588701f77c495db49.json` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/p0-security/results.json` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/p0-security/run.json` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/p0-security/sql-counts.json` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/p0-ui/browser/contact-ui-history-refresh.png` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/p0-ui/browser/customer-task-after-relogin.png` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/p0-ui/browser/lead-create-evidence.json` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/p0-ui/browser/lead-empty-source-created.png` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/p0-ui/browser/ui-http.json` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/p0-ui/browser/ui-journey-records.json` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/p0-ui/browser/ui-observations.json` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/p0-ui/browser/ui-records.json` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/p0-ui/browser/ui-steps.json` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/p0-ui/browser/ui-task-mutations.json` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/p0-ui/browser/ui-task-reads.json` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/p0-ui/http.json` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/p0-ui/results.json` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/p0-ui/run.json` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/p0-ui/ui-journey-records.json` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/p0-ui/ui-records.json` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/package_evidence.py` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/phase1-connected/browser/connected-product-currency.png` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/phase1-connected/browser/currency-results.json` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/phase1-connected/browser/first-visit.png` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/phase1-connected/final-sql-state.json` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/phase1-connected/http.json` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/phase1-connected/initial-sql-currency.json` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/phase1-connected/lead-customer-sql.json` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/phase1-connected/product-currency-sql.json` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/phase1-connected/results.json` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/phase1-connected/run.json` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/phase1-source-manifest.json` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/pinned-before.json` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/pinned-exception-approval.json` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/preservation-after.json` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/readonly-sql-recheck.json` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/record_phase1_manifest.py` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/source-read-hashes.json` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/useLeadFormController.proposed.tsx` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/verification-queries.sql` |
| `??` | `docs/verification/evidence/multicurrency-phase01-20261021/verify_handoff_integrity.py` |
| `??` | `docs/verification/evidence/multicurrency-phase2-design-20261021/analysis-before.json` |
| `??` | `docs/verification/evidence/multicurrency-phase2-design-20261021/analysis-integrity.json` |
| `??` | `docs/verification/evidence/multicurrency-phase2-design-20261021/artifact-hashes.json` |
| `??` | `docs/verification/evidence/multicurrency-phase2-design-20261021/cf01-functional-review-record.json` |
| `??` | `docs/verification/evidence/multicurrency-phase2-design-20261021/observed-sql.json` |
| `??` | `docs/verification/evidence/multicurrency-phase2-design-20261021/source-evidence.json` |
| `??` | `docs/verification/evidence/multicurrency-phase2-review-20261022/artifact-hashes.json` |
| `??` | `docs/verification/evidence/multicurrency-phase2-review-20261022/design-before-review.md` |
| `??` | `docs/verification/evidence/multicurrency-phase2-review-20261022/design-review.patch` |
| `??` | `docs/verification/evidence/multicurrency-phase2-review-20261022/external-reference-record.json` |
| `??` | `docs/verification/evidence/multicurrency-phase2-review-20261022/planned-file-impact.json` |
| `??` | `docs/verification/evidence/multicurrency-phase2-review-20261022/review-before.json` |
| `??` | `docs/verification/evidence/multicurrency-phase2-review-20261022/review-decision.json` |
| `??` | `docs/verification/evidence/multicurrency-phase2-review-20261022/review-integrity.json` |
| `??` | `docs/verification/evidence/multicurrency-phase2-review-20261022/test-acceptance-matrix.json` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/architecture-delta.json` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/artifact-hashes.json` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/backend-commit-proposal.txt` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/backend-cumulative.patch` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/backend-file-review.tsv` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/backend-status.txt` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/baseline-backend.txt` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/baseline-backend/baseline.trx` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/baseline-frontend-results.json` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/baseline-mutation.txt` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/baseline-quality.backend-readiness.txt` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/baseline-quality.quality-pipeline.txt` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/baseline-quality.repository-inventory.txt` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/baseline-quality.task-create-form-contracts.txt` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/baseline-task-commercial.txt` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/browser-connected/.last-run.json` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/browser-connected/crm-real-API-CRM-workflow--66eaf-ection-and-durable-Activity/contact-after-refresh.png` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/browser-connected/crm-real-API-CRM-workflow--66eaf-ection-and-durable-Activity/http.json` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/browser-connected/crm-real-API-CRM-workflow--66eaf-ection-and-durable-Activity/records.json` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/browser-connected/crm-real-API-CRM-workflow--66eaf-ection-and-durable-Activity/sales-own-history-after-refresh.png` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/checkpoint-pair.pending.json` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/compiled-assemblies.json` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/connected/browser-records.json` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/connected/http.json` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/connected/results.json` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/connected/run.json` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/connected/sql-counts.json` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/contact-follow-up.txt` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/current-backend.txt` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/current-backend/current.trx` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/current-build.txt` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/current-contact-history.txt` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/current-contact-overview.txt` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/current-customer-relationship-panel.txt` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/current-customer-task-create-session.txt` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/current-frontend-results.json` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/current-lead-history.txt` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/current-mutation-retry.txt` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/current-quality.api-contract.txt` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/current-quality.api-management-boundaries.txt` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/current-quality.backend-readiness.txt` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/current-quality.frontend-backend-separation.txt` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/current-quality.guidance-contracts.txt` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/current-quality.mutation-command-authority.txt` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/current-quality.presentation-responsibility.txt` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/current-quality.quality-pipeline.txt` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/current-quality.repository-inventory.txt` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/current-quality.strict-core.txt` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/current-quality.strict-type-regions.txt` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/current-quality.task-create-form-contracts.txt` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/current-task-commercial.txt` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/current-typecheck.txt` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/current-verify.txt` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/frontend-commit-proposal.txt` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/frontend-cumulative.patch` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/frontend-file-review.tsv` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/frontend-status.txt` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/new-source/backend/scripts/verify-activity-security.ps1` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/new-source/backend/src/UnicoreCRM.Operations/Tasks/Application/Common/ActivityRecordAccess.cs` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/new-source/backend/src/UnicoreCRM.Operations/Tasks/Contracts/ActivityReferenceAuthority.cs` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/new-source/backend/src/UnicoreCRM.Operations/Tasks/Infrastructure/Persistence/ActivityReadSql.cs` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/new-source/backend/src/UnicoreCRM.Operations/Tasks/Infrastructure/Persistence/Migrations/20261021075605_IndexActivityRecordHistory.Designer.cs` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/new-source/backend/src/UnicoreCRM.Operations/Tasks/Infrastructure/Persistence/Migrations/20261021075605_IndexActivityRecordHistory.cs` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/new-source/frontend/playwright.crm-real.config.ts` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/new-source/frontend/playwright.crm-ui.config.ts` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/new-source/frontend/tests/contract/customer-task-create-session.test.mts` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/new-source/frontend/tests/e2e/crm-real.spec.ts` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/new-source/frontend/tests/e2e/crm-ui-real.spec.ts` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/review-summary.json` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/secret-scan-locations.json` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/security-performance/activity-indexes.json` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/security-performance/activity-plan-1.sqlplan` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/security-performance/activity-plan-2.sqlplan` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/security-performance/activity-plan-3.sqlplan` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/security-performance/activity-plan-4.sqlplan` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/security-performance/activity-query-1.sql` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/security-performance/activity-query-2.sql` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/security-performance/activity-query-3.sql` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/security-performance/activity-query-4.sql` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/security-performance/commit-race.json` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/security-performance/http.json` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/security-performance/performance-matrix.json` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/security-performance/race-review-activity-own.json` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/security-performance/race-review-race-3d6f57f724044bd2a6ab2f18b376d77f.json` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/security-performance/results.json` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/security-performance/run.json` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/security-performance/sql-counts.json` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/source-and-baseline-integrity.json` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/source-manifest.json` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/tasks-handover.txt` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/ui-failed/http.json` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/ui-failed/results.json` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/ui-failed/run.json` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/ui-final/http.json` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/ui-final/results.json` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/ui-final/run.json` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/ui-final/ui-journey-records.json` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/ui-final/ui-records.json` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/ui-run1/.last-run.json` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/ui-run1/crm-ui-real-CRM-journey-through-UI-controls-only/error-context.md` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/ui-run1/crm-ui-real-CRM-journey-through-UI-controls-only/test-failed-1.png` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/ui-run1/crm-ui-real-CRM-journey-through-UI-controls-only/ui-http.json` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/ui-run1/crm-ui-real-CRM-journey-through-UI-controls-only/ui-journey-records.json` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/ui-run1/crm-ui-real-CRM-journey-through-UI-controls-only/ui-observations.json` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/ui-run1/crm-ui-real-CRM-journey-through-UI-controls-only/ui-steps.json` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/ui-run1/crm-ui-real-CRM-journey-through-UI-controls-only/ui-task-mutations.json` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/ui-run1/crm-ui-real-CRM-journey-through-UI-controls-only/ui-task-reads.json` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/ui-run2/.last-run.json` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/ui-run2/crm-ui-real-CRM-journey-through-UI-controls-only/contact-ui-history-refresh.png` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/ui-run2/crm-ui-real-CRM-journey-through-UI-controls-only/customer-task-after-relogin.png` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/ui-run2/crm-ui-real-CRM-journey-through-UI-controls-only/lead-create-evidence.json` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/ui-run2/crm-ui-real-CRM-journey-through-UI-controls-only/lead-empty-source-created.png` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/ui-run2/crm-ui-real-CRM-journey-through-UI-controls-only/ui-http.json` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/ui-run2/crm-ui-real-CRM-journey-through-UI-controls-only/ui-journey-records.json` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/ui-run2/crm-ui-real-CRM-journey-through-UI-controls-only/ui-observations.json` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/ui-run2/crm-ui-real-CRM-journey-through-UI-controls-only/ui-records.json` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/ui-run2/crm-ui-real-CRM-journey-through-UI-controls-only/ui-steps.json` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/ui-run2/crm-ui-real-CRM-journey-through-UI-controls-only/ui-task-mutations.json` |
| `??` | `docs/verification/evidence/p0-final-checkpoint-20261021/ui-run2/crm-ui-real-CRM-journey-through-UI-controls-only/ui-task-reads.json` |
| `??` | `docs/verification/evidence/realistic-demo/provenance-summary.json` |
| `??` | `docs/verification/evidence/realistic-demo/ui-verification.json` |
| `??` | `docs/verification/evidence/realistic-demo/verification.json` |
| `??` | `docs/verification/evidence/studio-first-visit-20261021/artifact-hashes.json` |
| `??` | `docs/verification/evidence/studio-first-visit-20261021/backend.patch` |
| `??` | `docs/verification/evidence/studio-first-visit-20261021/changes.json` |
| `??` | `docs/verification/evidence/studio-first-visit-20261021/connected-final/browser/.last-run.json` |
| `??` | `docs/verification/evidence/studio-first-visit-20261021/connected-final/browser/connected-product-currency.png` |
| `??` | `docs/verification/evidence/studio-first-visit-20261021/connected-final/browser/first-visit.png` |
| `??` | `docs/verification/evidence/studio-first-visit-20261021/connected-final/browser/studio-results.json` |
| `??` | `docs/verification/evidence/studio-first-visit-20261021/connected-final/final-sql-state.json` |
| `??` | `docs/verification/evidence/studio-first-visit-20261021/connected-final/http.json` |
| `??` | `docs/verification/evidence/studio-first-visit-20261021/connected-final/initial-sql-currency.json` |
| `??` | `docs/verification/evidence/studio-first-visit-20261021/connected-final/product-currency-sql.json` |
| `??` | `docs/verification/evidence/studio-first-visit-20261021/connected-final/results.json` |
| `??` | `docs/verification/evidence/studio-first-visit-20261021/connected-final/run.json` |
| `??` | `docs/verification/evidence/studio-first-visit-20261021/currency-demo-audit.json` |
| `??` | `docs/verification/evidence/studio-first-visit-20261021/currency-source-inventory.json` |
| `??` | `docs/verification/evidence/studio-first-visit-20261021/frontend.patch` |
| `??` | `docs/verification/evidence/studio-first-visit-20261021/preservation.json` |
| `??` | `docs/verification/evidence/studio-first-visit-20261021/verification-manifest.json` |
| `??` | `docs/verification/evidence/studio-first-visit-20261021/verification-queries.sql` |
| `??` | `docs/verification/evidence/ui-cleanup/cleanup-after-branding.png` |
| `??` | `docs/verification/evidence/ui-cleanup/cleanup-after-checks.json` |
| `??` | `docs/verification/evidence/ui-cleanup/cleanup-after-collapsed.png` |
| `??` | `docs/verification/evidence/ui-cleanup/cleanup-after-customers-health-hidden.png` |
| `??` | `docs/verification/evidence/ui-cleanup/cleanup-after-customers-health-notice.png` |
| `??` | `docs/verification/evidence/ui-cleanup/cleanup-after-customers-health-page.png` |
| `??` | `docs/verification/evidence/ui-cleanup/cleanup-after-customers-hidden.png` |
| `??` | `docs/verification/evidence/ui-cleanup/cleanup-after-customers-notice.png` |
| `??` | `docs/verification/evidence/ui-cleanup/cleanup-after-customers-page.png` |
| `??` | `docs/verification/evidence/ui-cleanup/cleanup-after-customers-segments-hidden.png` |
| `??` | `docs/verification/evidence/ui-cleanup/cleanup-after-customers-segments-notice.png` |
| `??` | `docs/verification/evidence/ui-cleanup/cleanup-after-customers-segments-page.png` |
| `??` | `docs/verification/evidence/ui-cleanup/cleanup-after-logout.png` |
| `??` | `docs/verification/evidence/ui-cleanup/cleanup-after-mobile.png` |
| `??` | `docs/verification/evidence/ui-cleanup/cleanup-after-orders-page.png` |
| `??` | `docs/verification/evidence/ui-cleanup/cleanup-after-payments-page.png` |
| `??` | `docs/verification/evidence/ui-cleanup/cleanup-after-relogin.png` |
| `??` | `docs/verification/evidence/ui-cleanup/cleanup-before-branding.png` |
| `??` | `docs/verification/evidence/ui-cleanup/cleanup-before-checks.json` |
| `??` | `docs/verification/evidence/ui-cleanup/cleanup-before-customers-notice.png` |
| `??` | `docs/verification/evidence/ui-cleanup/cleanup-before-customers-page.png` |
| `??` | `docs/verification/evidence/ui-cleanup/cleanup-before-logout.png` |
| `??` | `docs/verification/evidence/ui-cleanup/cleanup-before-orders-page.png` |
| `??` | `docs/verification/evidence/ui-cleanup/cleanup-before-payments-page.png` |
| `??` | `docs/verification/evidence/ui-cleanup/cleanup-before-relogin.png` |
| `??` | `docs/verification/evidence/ui-cleanup/quality/summary.json` |
| `??` | `docs/verification/evidence/ui-cleanup/quality/ui-cleanup-command-results.json` |
| `??` | `docs/verification/evidence/ui-cleanup/quality/ui-cleanup-quality.api-contract.json` |
| `??` | `docs/verification/evidence/ui-cleanup/quality/ui-cleanup-quality.api-management-boundaries.json` |
| `??` | `docs/verification/evidence/ui-cleanup/quality/ui-cleanup-quality.auth-session-contracts.json` |
| `??` | `docs/verification/evidence/ui-cleanup/quality/ui-cleanup-quality.auth-ux-contracts.json` |
| `??` | `docs/verification/evidence/ui-cleanup/quality/ui-cleanup-quality.backend-readiness.json` |
| `??` | `docs/verification/evidence/ui-cleanup/quality/ui-cleanup-quality.frontend-backend-separation.json` |
| `??` | `docs/verification/evidence/ui-cleanup/quality/ui-cleanup-quality.guidance-contracts.json` |
| `??` | `docs/verification/evidence/ui-cleanup/quality/ui-cleanup-quality.guidance-runtime.json` |
| `??` | `docs/verification/evidence/ui-cleanup/quality/ui-cleanup-quality.identity-auth-api-boundary.json` |
| `??` | `docs/verification/evidence/ui-cleanup/quality/ui-cleanup-quality.presentation-responsibility.json` |
| `??` | `docs/verification/evidence/ui-cleanup/quality/ui-cleanup-quality.quality-pipeline.json` |
| `??` | `docs/verification/evidence/ui-cleanup/quality/ui-cleanup-quality.read-only-cross-domain.json` |
| `??` | `docs/verification/evidence/ui-cleanup/quality/ui-cleanup-quality.repository-inventory.json` |
| `??` | `docs/verification/evidence/ui-cleanup/quality/ui-cleanup-quality.role-based-navigation.json` |
| `??` | `docs/verification/evidence/ui-cleanup/quality/ui-cleanup-quality.shell-ai-experience.json` |
| `??` | `docs/verification/evidence/ui-cleanup/quality/ui-cleanup-quality.strict-core.json` |
| `??` | `docs/verification/evidence/ui-cleanup/quality/ui-cleanup-quality.strict-type-regions.json` |
| `??` | `docs/verification/evidence/ui-cleanup/quality/ui-cleanup-typecheck.json` |
| `??` | `docs/verification/evidence/ui-cleanup/scope-preservation.json` |
| `??` | `docs/verification/evidence/ui-cleanup/sql-preservation.json` |
| `??` | `docs/verification/evidence/workspace-pipeline-selection/task-entry/DealFormModal.tsx` |
| `??` | `docs/verification/evidence/workspace-pipeline-selection/task-entry/DealPipelineModals.tsx` |
| `??` | `docs/verification/evidence/workspace-pipeline-selection/task-entry/DealPipelinePage.tsx` |
| `??` | `docs/verification/evidence/workspace-pipeline-selection/task-entry/source-hashes.json` |
| `??` | `docs/verification/evidence/workspace-pipeline-selection/task-entry/useDealPipelineController.ts` |
| `??` | `docs/verification/evidence/workspace-pipeline-selection/task-entry/useDealStageWindows.ts` |
| `??` | `docs/verification/gemini-configuration-diagnostics.md` |
| `??` | `docs/verification/gemini-pre-header-timeout.md` |
| `??` | `docs/verification/gemini-rest-integration.md` |
| `??` | `docs/verification/gemini-timeout-code-only.md` |
| `??` | `docs/verification/multicurrency-phase01-design.md` |
| `??` | `docs/verification/multicurrency-phase01-results.md` |
| `??` | `docs/verification/multicurrency-phase2-contract-change-proposal.md` |
| `??` | `docs/verification/multicurrency-phase2-database-integrity-proposal.md` |
| `??` | `docs/verification/multicurrency-phase2-delivery-and-acceptance-plan.md` |
| `??` | `docs/verification/multicurrency-phase2-exchange-rate-authority-design.md` |
| `??` | `docs/verification/openai-current-model-catalog.md` |
| `??` | `docs/verification/p0-final-checkpoint.md` |
| `??` | `docs/verification/quote-approval-b1-results.md` |
| `??` | `docs/verification/quote-core-results.md` |
| `??` | `docs/verification/quote-customer-decision-b3-results.md` |
| `??` | `docs/verification/quote-delivery-b2-results.md` |
| `??` | `docs/verification/quote-list-workspace-pipeline-results.md` |
| `??` | `docs/verification/realistic-demo-workspace-results.md` |
| `??` | `docs/verification/studio-first-visit-and-currency-audit.md` |
| `??` | `docs/verification/ui-cleanup-and-auth-results.md` |
| `??` | `scripts/AiProviderVerifier/Fixtures/gemini-3.8-configuration-request.json` |
| `??` | `scripts/AiProviderVerifier/Fixtures/gemini-multipart-success.json` |
| `??` | `scripts/AiProviderVerifier/GeminiConnectivityProbe.cs` |
| `??` | `scripts/AiProviderVerifier/OpenAiModelVerifier.cs` |
| `??` | `scripts/ContactFollowUpVerifier/evidence/contacts-migration-indexes.txt` |
| `??` | `scripts/ContactFollowUpVerifier/evidence/contacts-read-current/contact-verifier-host.log.err` |
| `??` | `scripts/ContactFollowUpVerifier/evidence/contacts-read-current/contact-verifier-results.txt` |
| `??` | `scripts/ContactFollowUpVerifier/evidence/contacts-read-review-fixed/contact-verifier-host.log.err` |
| `??` | `scripts/ContactFollowUpVerifier/evidence/contacts-read-review-fixed/contact-verifier-results.txt` |
| `??` | `scripts/ContactFollowUpVerifier/evidence/contacts-read-review/contact-verifier-failure.txt` |
| `??` | `scripts/ContactFollowUpVerifier/evidence/contacts-read-review/contact-verifier-host.log.err` |
| `??` | `scripts/ContactFollowUpVerifier/evidence/current-query-review.md` |
| `??` | `scripts/ContactFollowUpVerifier/evidence/current-solution-build.txt` |
| `??` | `scripts/ContactFollowUpVerifier/evidence/current-tasks-runtime-sql.txt` |
| `??` | `scripts/ContactFollowUpVerifier/evidence/operations-build.txt` |
| `??` | `scripts/ContactFollowUpVerifier/evidence/review.md` |
| `??` | `scripts/ContactFollowUpVerifier/evidence/runtime-sql-migrations.txt` |
| `??` | `scripts/QuotePdfWorker/Fonts/LICENSE` |
| `??` | `scripts/QuotePdfWorker/Fonts/NotoSans-Bold.ttf` |
| `??` | `scripts/QuotePdfWorker/Fonts/NotoSans-Regular.ttf` |
| `??` | `scripts/QuotePdfWorker/Program.cs` |
| `??` | `scripts/QuotePdfWorker/THIRD_PARTY_NOTICES.txt` |
| `??` | `scripts/QuotePdfWorker/UnicoreCRM.QuotePdfWorker.csproj` |
| `??` | `scripts/QuoteProofScannerVerifier/Program.cs` |
| `??` | `scripts/QuoteProofScannerVerifier/UnicoreCRM.QuoteProofScannerVerifier.csproj` |
| `??` | `scripts/QuoteProofValidatorWorker/Program.cs` |
| `??` | `scripts/QuoteProofValidatorWorker/ProofParserResult.cs` |
| `??` | `scripts/QuoteProofValidatorWorker/UnicoreCRM.QuoteProofValidatorWorker.csproj` |
| `??` | `scripts/QuoteProofVerifier/Program.cs` |
| `??` | `scripts/QuoteProofVerifier/UnicoreCRM.QuoteProofVerifier.csproj` |
| `??` | `scripts/apply-quote-document-development-schema.ps1` |
| `??` | `scripts/apply-quote-identity-authority-development-schema.ps1` |
| `??` | `scripts/apply-quote-independent-review-development-schema.ps1` |
| `??` | `scripts/apply-quote-manual-source-development-schema.ps1` |
| `??` | `scripts/demo/DealPipelineAcceptance.ps1` |
| `??` | `scripts/demo/DealPipelineConcurrency.ps1` |
| `??` | `scripts/demo/DealPipelineLegacyReplay.ps1` |
| `??` | `scripts/demo/DealPipelineMetrics.ps1` |
| `??` | `scripts/demo/DealPipelinePermissions.ps1` |
| `??` | `scripts/demo/DealPipelineProvisioning.ps1` |
| `??` | `scripts/demo/DemoRuntime.ps1` |
| `??` | `scripts/demo/RealisticDataset.ps1` |
| `??` | `scripts/demo/VerifyDemo.ps1` |
| `??` | `scripts/inspect-quote-lifecycle.ps1` |
| `??` | `scripts/prepare-quote-independent-review-fixtures.ps1` |
| `??` | `scripts/prepare-quote-manual-source-browser.ps1` |
| `??` | `scripts/repair-quote-lifecycle-test-access.ps1` |
| `??` | `scripts/review-deal-pipeline-acceptance.ps1` |
| `??` | `scripts/scanner/QuoteIdentityVerificationRuntime.ps1` |
| `??` | `scripts/scanner/QuoteIndependentReviewVerificationRuntime.ps1` |
| `??` | `scripts/scanner/QuoteManualSourceVerificationRuntime.ps1` |
| `??` | `scripts/scanner/QuoteProofScannerVerificationRuntime.ps1` |
| `??` | `scripts/seed-realistic-demo.ps1` |
| `??` | `scripts/test-realistic-demo.ps1` |
| `??` | `scripts/verify-ai-workspace.ps1` |
| `??` | `scripts/verify-connected-dashboard.ps1` |
| `??` | `scripts/verify-customer-ui.ps1` |
| `??` | `scripts/verify-deal-pipelines.ps1` |
| `??` | `scripts/verify-multicurrency-connected.ps1` |
| `??` | `scripts/verify-quote-approval-synthetic.ps1` |
| `??` | `scripts/verify-quote-approval.ps1` |
| `??` | `scripts/verify-quote-archive-contract-gap.ps1` |
| `??` | `scripts/verify-quote-archive.ps1` |
| `??` | `scripts/verify-quote-core-guard.ps1` |
| `??` | `scripts/verify-quote-core.ps1` |
| `??` | `scripts/verify-quote-customer-decision-guards.ps1` |
| `??` | `scripts/verify-quote-customer-decision-security.ps1` |
| `??` | `scripts/verify-quote-customer-decisions.ps1` |
| `??` | `scripts/verify-quote-delivery.ps1` |
| `??` | `scripts/verify-quote-documents-advanced.ps1` |
| `??` | `scripts/verify-quote-documents-browser.ps1` |
| `??` | `scripts/verify-quote-documents-layout.ps1` |
| `??` | `scripts/verify-quote-documents-policy.ps1` |
| `??` | `scripts/verify-quote-documents.ps1` |
| `??` | `scripts/verify-quote-expiration.ps1` |
| `??` | `scripts/verify-quote-identity-affiliation.ps1` |
| `??` | `scripts/verify-quote-identity-authority-browser.ps1` |
| `??` | `scripts/verify-quote-identity-authority-security.ps1` |
| `??` | `scripts/verify-quote-identity-authority.ps1` |
| `??` | `scripts/verify-quote-identity-binding-preservation.ps1` |
| `??` | `scripts/verify-quote-identity-record-scope.ps1` |
| `??` | `scripts/verify-quote-identity-replay-access.ps1` |
| `??` | `scripts/verify-quote-identity-scan-guards.ps1` |
| `??` | `scripts/verify-quote-identity-trust-guards.ps1` |
| `??` | `scripts/verify-quote-independent-review-browser.ps1` |
| `??` | `scripts/verify-quote-independent-review-guards.ps1` |
| `??` | `scripts/verify-quote-independent-review.ps1` |
| `??` | `scripts/verify-quote-lifecycle-browser.ps1` |
| `??` | `scripts/verify-quote-lifecycle-concurrency.ps1` |
| `??` | `scripts/verify-quote-lifecycle-security.ps1` |
| `??` | `scripts/verify-quote-list-pipeline.ps1` |
| `??` | `scripts/verify-quote-manual-source-browser.ps1` |
| `??` | `scripts/verify-quote-manual-source-guards.ps1` |
| `??` | `scripts/verify-quote-manual-source-security.ps1` |
| `??` | `scripts/verify-quote-manual-source-workflow.ps1` |
| `??` | `scripts/verify-quote-proof-guards.ps1` |
| `??` | `scripts/verify-quote-proof-minimal-access.ps1` |
| `??` | `scripts/verify-quote-proof-scanner-advanced.ps1` |
| `??` | `scripts/verify-quote-proof-scanner-faults.ps1` |
| `??` | `scripts/verify-quote-proof-scanner-reads.ps1` |
| `??` | `scripts/verify-quote-proof-scanner-security.ps1` |
| `??` | `scripts/verify-quote-proof-scanner.ps1` |
| `??` | `scripts/verify-quote-proof-storage-advanced.ps1` |
| `??` | `scripts/verify-quote-proof-storage-browser.ps1` |
| `??` | `scripts/verify-quote-proof-storage.ps1` |
| `??` | `scripts/verify-quote-revisions.ps1` |
| `??` | `scripts/verify-quote-verified-acceptance.ps1` |
| `??` | `scripts/verify-studio-first-visit.ps1` |
| `??` | `src/UnicoreCRM.Billing/Payments/Infrastructure/Persistence/Migrations/20261025053037_QuoteRetentionReference.Designer.cs` |
| `??` | `src/UnicoreCRM.Billing/Payments/Infrastructure/Persistence/Migrations/20261025053037_QuoteRetentionReference.cs` |
| `??` | `src/UnicoreCRM.Billing/Payments/Infrastructure/Persistence/PaymentsQuoteRetentionVerifier.cs` |
| `??` | `src/UnicoreCRM.Communications/Application/CommunicationsAccessFields.cs` |
| `??` | `src/UnicoreCRM.Communications/Infrastructure/Persistence/ChannelAccessFacts.cs` |
| `??` | `src/UnicoreCRM.Communications/Infrastructure/Persistence/ConversationAccessFacts.cs` |
| `??` | `src/UnicoreCRM.Crm/Contacts/Application/QuoteDocuments/AcceptancePartyReader.cs` |
| `??` | `src/UnicoreCRM.Crm/Contacts/Application/QuoteDocuments/PartyReader.cs` |
| `??` | `src/UnicoreCRM.Crm/Customers/Infrastructure/Persistence/EfCustomerAttentionReader.cs` |
| `??` | `src/UnicoreCRM.Crm/Customers/Infrastructure/Persistence/EfProactiveCustomerHealthReader.cs` |
| `??` | `src/UnicoreCRM.Crm/Deals/Application/Common/DealPipelinePersistence.cs` |
| `??` | `src/UnicoreCRM.Crm/Deals/Application/Common/DealPipelineRegistry.cs` |
| `??` | `src/UnicoreCRM.Crm/Deals/Application/VerifyQuoteSource/QuoteRelationshipVerifier.cs` |
| `??` | `src/UnicoreCRM.Crm/Deals/Contracts/DealBusyFilter.cs` |
| `??` | `src/UnicoreCRM.Crm/Deals/Contracts/DealPipelineContracts.cs` |
| `??` | `src/UnicoreCRM.Crm/Deals/Contracts/DealPipelineEndpoints.cs` |
| `??` | `src/UnicoreCRM.Crm/Deals/Domain/DealPipeline.cs` |
| `??` | `src/UnicoreCRM.Crm/Deals/Infrastructure/Persistence/DealsQuoteRetentionVerifier.cs` |
| `??` | `src/UnicoreCRM.Crm/Deals/Infrastructure/Persistence/Migrations/20261023150422_AuthoritativeDealPipelines.Designer.cs` |
| `??` | `src/UnicoreCRM.Crm/Deals/Infrastructure/Persistence/Migrations/20261023150422_AuthoritativeDealPipelines.cs` |
| `??` | `src/UnicoreCRM.Crm/Deals/Infrastructure/Persistence/Migrations/20261025053042_QuoteRetentionReference.Designer.cs` |
| `??` | `src/UnicoreCRM.Crm/Deals/Infrastructure/Persistence/Migrations/20261025053042_QuoteRetentionReference.cs` |
| `??` | `src/UnicoreCRM.Crm/Organizations/Application/QuoteDocuments/PartyReader.cs` |
| `??` | `src/UnicoreCRM.Platform/AccessControl/Application/CheckQuoteApprovalAvailability/QuoteApprovalAvailability.cs` |
| `??` | `src/UnicoreCRM.Platform/AccessControl/Contracts/QuoteApprovalAvailability.cs` |
| `??` | `src/UnicoreCRM.Platform/AccessControl/Infrastructure/Persistence/EfServiceAccessAuthorizer.cs` |
| `??` | `src/UnicoreCRM.Platform/Workspace/Contracts/QuoteApprovalPolicy.cs` |
| `??` | `src/UnicoreCRM.Platform/Workspace/Contracts/QuoteSellerReader.cs` |
| `??` | `src/UnicoreCRM.Platform/Workspace/Contracts/QuoteWorkspacePolicy.cs` |
| `??` | `src/UnicoreCRM.Platform/Workspace/Infrastructure/ConfiguredQuoteApprovalPolicyReader.cs` |
| `??` | `src/UnicoreCRM.Platform/Workspace/Infrastructure/Persistence/EfQuoteSellerReader.cs` |
| `??` | `src/UnicoreCRM.Platform/Workspace/Infrastructure/Persistence/EfQuoteWorkspacePolicyReader.cs` |
| `??` | `src/UnicoreCRM.Sales/Orders/Infrastructure/Persistence/OrdersQuoteRetentionVerifier.cs` |
| `??` | `src/UnicoreCRM.Sales/Products/Application/ReadQuoteProductSnapshots/Handler.cs` |
| `??` | `src/UnicoreCRM.Sales/Products/AssemblyInfo.cs` |
| `??` | `src/UnicoreCRM.Sales/Products/Contracts/QuoteProductSnapshotReader.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Application/Common/QuoteAcceptanceGuard.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Application/Common/QuoteApprovalAssessment.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Application/Common/QuoteApprovalCommands.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Application/Common/QuoteArchiveCommands.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Application/Common/QuoteCustomerDecisionPolicy.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Application/Common/QuoteCustomerDecisionRecords.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Application/Common/QuoteCustomerResponseCommands.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Application/Common/QuoteDeliveryCommands.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Application/Common/QuoteDocumentCommands.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Application/Common/QuoteDocumentCommitProbe.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Application/Common/QuoteDocumentPersistence.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Application/Common/QuoteDocumentPolicy.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Application/Common/QuoteDocumentReads.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Application/Common/QuoteDraftCommands.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Application/Common/QuoteEvidenceSourceService.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Application/Common/QuoteExpirationCommands.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Application/Common/QuoteIdentityAccess.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Application/Common/QuoteIdentityCaseService.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Application/Common/QuoteIdentityEvaluation.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Application/Common/QuoteIdentityPorts.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Application/Common/QuoteIndependentReviewRules.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Application/Common/QuoteIndependentReviewService.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Application/Common/QuoteLifecycleClock.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Application/Common/QuoteLifecyclePolicy.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Application/Common/QuoteManualSourceWorkflow.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Application/Common/QuotePdfRenderer.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Application/Common/QuotePricing.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Application/Common/QuoteProofAccess.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Application/Common/QuoteProofCommands.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Application/Common/QuoteProofEffectiveScan.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Application/Common/QuoteProofPdfInspector.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Application/Common/QuoteProofReads.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Application/Common/QuoteProofScanCoordinator.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Application/Common/QuoteProofScanPolicy.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Application/Common/QuoteProofScanPorts.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Application/Common/QuoteProofScanReads.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Application/Common/QuoteProofStorage.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Application/Common/QuoteRejectionCommands.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Application/Common/QuoteRetentionVerification.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Application/Common/QuoteRevisionCommands.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Application/Common/QuoteRevisionContent.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Application/Common/QuoteValidity.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Application/Common/QuoteVerifiedAcceptanceAssessment.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Application/Common/QuoteVerifiedAcceptancePolicy.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/AssemblyInfo.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Contracts/QuoteAcceptancePartyReader.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Contracts/QuoteCustomerDecisionContracts.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Contracts/QuoteDeliveryContracts.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Contracts/QuoteDocumentContracts.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Contracts/QuoteDocumentEndpoints.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Contracts/QuoteDocumentPartyReader.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Contracts/QuoteDraftContracts.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Contracts/QuoteIdentityContracts.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Contracts/QuoteIdentityEndpoints.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Contracts/QuoteIndependentReviewContracts.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Contracts/QuoteLifecycleContracts.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Contracts/QuoteProofContracts.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Contracts/QuoteProofEndpoints.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Contracts/QuoteRelationships.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Contracts/QuoteRetentionVerification.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Contracts/QuoteVerifiedAcceptanceContracts.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Contracts/QuoteVerifiedAcceptanceEndpoints.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Contracts/TrustedProofScanner.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Domain/QuoteAcceptanceProof.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Domain/QuoteCommandRecord.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Domain/QuoteCustomerDecisionRecord.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Domain/QuoteDocument.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Domain/QuoteIdentityAuthorityRules.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Domain/QuoteIdentityRecords.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Domain/QuoteProofScanJob.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Domain/QuoteProofScanRecords.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Infrastructure/Documents/ClamAvProofScanner.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Infrastructure/Documents/DevelopmentDocumentCommitProbe.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Infrastructure/Documents/ProofToolProcess.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Infrastructure/Documents/QpdfProofValidator.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Infrastructure/Documents/QpdfProofValidatorCore.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Infrastructure/Documents/QuoteProofScanWorker.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Infrastructure/Documents/RuntimeProofStructuralValidator.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Infrastructure/Documents/TrustedClamAvRuntimeReader.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Infrastructure/Documents/WorkerQuotePdfRenderer.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Infrastructure/Persistence/ConfiguredQuoteCustomerDecisionPolicyReader.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Infrastructure/Persistence/ConfiguredQuoteLifecyclePolicyReader.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Infrastructure/Persistence/DevelopmentProofCommitProbe.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Infrastructure/Persistence/EfQuoteDocumentPersistence.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Infrastructure/Persistence/EfQuoteIdentityPersistence.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Infrastructure/Persistence/EfQuoteProofPersistence.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Infrastructure/Persistence/EfQuoteProofScanStore.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Infrastructure/Persistence/Migrations/20261023200111_QuoteDraftCore.Designer.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Infrastructure/Persistence/Migrations/20261023200111_QuoteDraftCore.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Infrastructure/Persistence/Migrations/20261024201729_QuoteCustomerDecisions.Designer.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Infrastructure/Persistence/Migrations/20261024201729_QuoteCustomerDecisions.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Infrastructure/Persistence/Migrations/20261025051129_QuoteRevisionLineage.Designer.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Infrastructure/Persistence/Migrations/20261025051129_QuoteRevisionLineage.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Infrastructure/Persistence/Migrations/20261025083644_QuoteIssuedDocuments.Designer.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Infrastructure/Persistence/Migrations/20261025083644_QuoteIssuedDocuments.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Infrastructure/Persistence/Migrations/20261026040920_QuoteAcceptanceProofQuarantine.Designer.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Infrastructure/Persistence/Migrations/20261026040920_QuoteAcceptanceProofQuarantine.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Infrastructure/Persistence/Migrations/20261026082618_AddAcceptanceProofScanLedger.Designer.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Infrastructure/Persistence/Migrations/20261026082618_AddAcceptanceProofScanLedger.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Infrastructure/Persistence/Migrations/20261026165846_QuoteIdentityAuthorityLedger.Designer.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Infrastructure/Persistence/Migrations/20261026165846_QuoteIdentityAuthorityLedger.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Infrastructure/Persistence/Migrations/20261027015446_QuoteManualSourceAdmission.Designer.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Infrastructure/Persistence/Migrations/20261027015446_QuoteManualSourceAdmission.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Infrastructure/Persistence/Migrations/20261027032029_QuoteIndependentReviewLedger.Designer.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Infrastructure/Persistence/Migrations/20261027032029_QuoteIndependentReviewLedger.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Infrastructure/Persistence/QuoteIdentityModel.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Infrastructure/Persistence/QuoteProofCipher.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Infrastructure/Persistence/QuoteProofModel.cs` |
| `??` | `src/UnicoreCRM.Sales/Quotes/Infrastructure/Persistence/QuoteProofScanModel.cs` |
| `??` | `src/UnicoreCRM.Workflows/Atomic/Application/Persistence/IAtomicWorkflowAnchorStore.cs` |
| `??` | `src/UnicoreCRM.Workflows/Atomic/Infrastructure/Persistence/EfAtomicWorkflowAnchorStore.cs` |
| `??` | `src/UnicoreCRM.Workflows/Durable/Application/ProvisionInitialWorkspace/InitialWorkspaceProvisioningOptions.cs` |
| `??` | `tests/UnicoreCRM.ArchitectureTests/AtomicWorkflowAnchorStoreTests.cs` |
| `??` | `tests/UnicoreCRM.ArchitectureTests/CommunicationsFactProviderBoundaryTests.cs` |
| `??` | `tests/UnicoreCRM.ArchitectureTests/CustomerListIdentityTests.cs` |
| `??` | `tests/UnicoreCRM.ArchitectureTests/DealListSecurityTests.cs` |
| `??` | `tests/UnicoreCRM.ArchitectureTests/DealPipelinePolicyTests.cs` |
| `??` | `tests/UnicoreCRM.ArchitectureTests/ImplementationOwnerMappingTests.cs` |
| `??` | `tests/UnicoreCRM.ArchitectureTests/MarkDealWonSecurityTests.cs` |
| `??` | `tests/UnicoreCRM.ArchitectureTests/ProductQuoteSnapshotTests.cs` |
| `??` | `tests/UnicoreCRM.ArchitectureTests/QuoteApprovalAvailabilityTests.cs` |
| `??` | `tests/UnicoreCRM.ArchitectureTests/QuoteApprovalTests.cs` |
| `??` | `tests/UnicoreCRM.ArchitectureTests/QuoteArchiveTests.cs` |
| `??` | `tests/UnicoreCRM.ArchitectureTests/QuoteCoreArchitectureTests.cs` |
| `??` | `tests/UnicoreCRM.ArchitectureTests/QuoteCoreOwnerMappingTests.cs` |
| `??` | `tests/UnicoreCRM.ArchitectureTests/QuoteCustomerDecisionPolicyTests.cs` |
| `??` | `tests/UnicoreCRM.ArchitectureTests/QuoteDeliveryTests.cs` |
| `??` | `tests/UnicoreCRM.ArchitectureTests/QuoteDocumentTests.cs` |
| `??` | `tests/UnicoreCRM.ArchitectureTests/QuoteDraftCommandSecurityTests.cs` |
| `??` | `tests/UnicoreCRM.ArchitectureTests/QuoteExpirationTests.cs` |
| `??` | `tests/UnicoreCRM.ArchitectureTests/QuoteIdentityAuthorityRulesTests.cs` |
| `??` | `tests/UnicoreCRM.ArchitectureTests/QuoteIdentityPersistenceTests.cs` |
| `??` | `tests/UnicoreCRM.ArchitectureTests/QuoteIndependentReviewTests.cs` |
| `??` | `tests/UnicoreCRM.ArchitectureTests/QuoteManualSourceWorkflowTests.cs` |
| `??` | `tests/UnicoreCRM.ArchitectureTests/QuotePricingTests.cs` |
| `??` | `tests/UnicoreCRM.ArchitectureTests/QuoteProofScanPersistenceTests.cs` |
| `??` | `tests/UnicoreCRM.ArchitectureTests/QuoteProofScanReleaseTests.cs` |
| `??` | `tests/UnicoreCRM.ArchitectureTests/QuoteProofScannerTests.cs` |
| `??` | `tests/UnicoreCRM.ArchitectureTests/QuoteProofStorageTests.cs` |
| `??` | `tests/UnicoreCRM.ArchitectureTests/QuoteRejectionTests.cs` |
| `??` | `tests/UnicoreCRM.ArchitectureTests/QuoteRelationshipVerifierTests.cs` |
| `??` | `tests/UnicoreCRM.ArchitectureTests/QuoteRevisionTests.cs` |
| `??` | `tests/UnicoreCRM.ArchitectureTests/QuoteVerifiedAcceptanceTests.cs` |

### frontend

| Git status | Path |
|---|---|
| ` M` | `AGENTS.md` |
| ` M` | `docs/api/api-operation-catalog.json` |
| ` M` | `docs/api/generated-client-manifest.json` |
| ` M` | `docs/api/openapi-breaking-baseline.json` |
| ` M` | `docs/api/openapi.json` |
| ` M` | `docs/api/openapi.sha256` |
| ` M` | `docs/api/operation-coverage-ledger.json` |
| ` M` | `docs/architecture/compatibility-and-migration.md` |
| ` M` | `docs/architecture/compatibility-ledger.json` |
| ` M` | `docs/architecture/quote-api-boundary.md` |
| ` M` | `docs/backend-readiness/concurrency-policy.json` |
| ` M` | `docs/backend-readiness/error-catalog.json` |
| ` M` | `docs/backend-readiness/idempotency-policy.json` |
| ` M` | `docs/backend-readiness/operation-authorization-matrix.json` |
| ` M` | `docs/backend-readiness/operation-contract-status.json` |
| ` M` | `docs/document-status.json` |
| ` M` | `docs/product/guidance-screen-inventory.md` |
| ` M` | `docs/product/guidance-system.md` |
| ` M` | `docs/quality/repository-inventory.json` |
| ` M` | `docs/quality/repository-inventory.md` |
| ` M` | `scripts/api/openapi/client-ownership.json` |
| ` M` | `scripts/api/openapi/normalize.mjs` |
| ` M` | `scripts/api/openapi/render.mjs` |
| ` M` | `scripts/api/openapi/validate.mjs` |
| ` M` | `scripts/lib/presentationCompositionSource.mts` |
| ` M` | `scripts/quality/quality-pipeline.json` |
| ` M` | `scripts/repository-inventory/repositoryInventory.mjs` |
| ` M` | `src/ai/index.ts` |
| ` M` | `src/ai/runtime/ConnectedAiRuntime.ts` |
| ` M` | `src/app/composition/applicationComposition.ts` |
| ` M` | `src/app/composition/connected/connectedCrmModuleServices.ts` |
| ` M` | `src/app/router/guards/RequireWorkspaceContext.tsx` |
| ` M` | `src/app/router/workspaces/studioWorkspaceRoutes.tsx` |
| ` M` | `src/app/routes/routeMeta.ts` |
| ` M` | `src/app/shell/layout/Sidebar.tsx` |
| ` M` | `src/components/AppErrorBoundary.tsx` |
| ` M` | `src/components/LazyRouteErrorBoundary.tsx` |
| ` M` | `src/components/PermissionRouteGuard.tsx` |
| ` D` | `src/components/access/ReadOnlyAccessBanner.tsx` |
| ` M` | `src/components/ai/AiAssistantButton.tsx` |
| ` M` | `src/components/ai/AiAssistantDrawer.tsx` |
| ` M` | `src/components/ai/AiChatPanel.tsx` |
| ` M` | `src/components/ai/AiSuggestedActions.tsx` |
| ` M` | `src/components/crm/list-archetype/ListPaginationBar.tsx` |
| ` M` | `src/components/crm/relationship-detail/RelationshipDetailTabs.tsx` |
| ` M` | `src/guidance/content/crm/communications.ts` |
| ` M` | `src/guidance/content/crm/extendedScreens.ts` |
| ` M` | `src/guidance/content/crm/screens.ts` |
| ` M` | `src/guidance/content/people/screens.ts` |
| ` M` | `src/guidance/content/studio/configuration.ts` |
| ` M` | `src/i18n/translations/en.ts` |
| ` M` | `src/i18n/translations/vi.ts` |
| ` M` | `src/main.tsx` |
| ` M` | `src/modules/communications/presentation/components/ChannelSettingsView.tsx` |
| ` M` | `src/modules/communications/presentation/components/ConversationCrmLinkView.tsx` |
| ` M` | `src/modules/communications/presentation/components/TelegramConnectionPanel.tsx` |
| ` M` | `src/modules/contacts/presentation/components/ContactFormModal.tsx` |
| ` M` | `src/modules/contacts/presentation/detail/ContactHistoryPanel.tsx` |
| ` M` | `src/modules/contacts/presentation/detail/ContactRelationshipPanel.tsx` |
| ` M` | `src/modules/contacts/presentation/model/contactDraftRecovery.ts` |
| ` M` | `src/modules/customers/application/ports/CustomerApiRuntime.ts` |
| ` M` | `src/modules/customers/detail-route.tsx` |
| ` M` | `src/modules/customers/infrastructure/http/CustomerHttpApiAdapter.ts` |
| ` M` | `src/modules/customers/presentation/components/CustomerStatusBadge.tsx` |
| ` M` | `src/modules/customers/presentation/detail/CustomerConnectedOverview.tsx` |
| ` M` | `src/modules/customers/presentation/detail/CustomerConnectedWorkspace.tsx` |
| ` M` | `src/modules/customers/presentation/detail/CustomerDetailTabs.tsx` |
| ` M` | `src/modules/customers/presentation/detail/CustomerIntegrityIndicator.tsx` |
| ` M` | `src/modules/customers/presentation/detail/CustomerOverviewTab.tsx` |
| ` M` | `src/modules/customers/presentation/detail/CustomerRelationshipPanel.tsx` |
| ` M` | `src/modules/customers/presentation/hooks/useCustomers.ts` |
| ` M` | `src/modules/customers/presentation/list/CustomerCardList.tsx` |
| ` M` | `src/modules/customers/presentation/list/CustomerSavedViewSelector.tsx` |
| ` M` | `src/modules/customers/presentation/list/CustomerStatisticsPanel.tsx` |
| ` M` | `src/modules/customers/presentation/list/CustomerTable.tsx` |
| ` M` | `src/modules/customers/presentation/list/ExistingCustomerOnboardingModal.tsx` |
| ` M` | `src/modules/customers/presentation/pages/CustomerListPage.tsx` |
| ` M` | `src/modules/deals/application/composition/dealApplicationServices.ts` |
| ` M` | `src/modules/deals/application/ports/DealApiRuntime.ts` |
| ` M` | `src/modules/deals/application/ports/DealStageRepository.ts` |
| ` M` | `src/modules/deals/application/read-models/dealReadModel.ts` |
| ` M` | `src/modules/deals/application/vertical-slice/dealAuthoritativeQueries.ts` |
| ` M` | `src/modules/deals/domain/model/deal.types.ts` |
| ` M` | `src/modules/deals/domain/rules/dealPipelineHealth.ts` |
| ` M` | `src/modules/deals/domain/rules/dealProgressiveProfile.ts` |
| ` M` | `src/modules/deals/domain/rules/dealStages.ts` |
| ` M` | `src/modules/deals/infrastructure/http/DealApiMapper.ts` |
| ` M` | `src/modules/deals/infrastructure/http/DealHttpApiAdapter.ts` |
| ` M` | `src/modules/deals/presentation/components/DealBuyerPicker.tsx` |
| ` M` | `src/modules/deals/presentation/components/DealFollowUpTasks.tsx` |
| ` M` | `src/modules/deals/presentation/components/DealFormModal.tsx` |
| ` M` | `src/modules/deals/presentation/components/DealInterestedProductsPicker.tsx` |
| ` M` | `src/modules/deals/presentation/components/DealKanbanCard.tsx` |
| ` M` | `src/modules/deals/presentation/components/DealPipelineHealth.tsx` |
| ` M` | `src/modules/deals/presentation/components/DealPipelineModals.tsx` |
| ` M` | `src/modules/deals/presentation/components/dealFormModel.ts` |
| ` M` | `src/modules/deals/presentation/hooks/useDealDetailController.tsx` |
| ` M` | `src/modules/deals/presentation/hooks/useDealFollowUpTasks.ts` |
| ` M` | `src/modules/deals/presentation/hooks/useDealPipelineController.ts` |
| ` M` | `src/modules/deals/presentation/hooks/useDealStageWindows.ts` |
| ` M` | `src/modules/deals/presentation/hooks/useDealStages.ts` |
| ` M` | `src/modules/deals/presentation/hooks/useDeals.ts` |
| ` M` | `src/modules/deals/presentation/pages/DealDetailPage.tsx` |
| ` M` | `src/modules/deals/presentation/pages/DealPipelinePage.tsx` |
| ` M` | `src/modules/deals/presentation/views/DealDetailActivityTimeline.tsx` |
| ` M` | `src/modules/deals/presentation/views/DealDetailCommercialWorkspace.tsx` |
| ` M` | `src/modules/deals/presentation/views/DealDetailDialogs.tsx` |
| ` M` | `src/modules/deals/presentation/views/DealDetailHeaderSection.tsx` |
| ` M` | `src/modules/deals/presentation/views/DealDetailPipelineWorkspace.tsx` |
| ` M` | `src/modules/deals/presentation/views/DealDetailQuoteWorkspace.tsx` |
| ` M` | `src/modules/deals/presentation/views/DealDetailRelatedRecords.tsx` |
| ` M` | `src/modules/deals/presentation/views/DealDetailSidebar.tsx` |
| ` M` | `src/modules/deals/presentation/views/DealDetailSidebarActions.tsx` |
| ` M` | `src/modules/deals/presentation/views/DealDetailView.tsx` |
| ` M` | `src/modules/deals/public/deals.ts` |
| ` M` | `src/modules/deals/public/index.ts` |
| ` M` | `src/modules/invoices/presentation/pages/InvoiceFormPage.tsx` |
| ` M` | `src/modules/leads/presentation/components/LeadHistoryPanel.tsx` |
| ` M` | `src/modules/leads/presentation/components/LeadStatisticsModal.tsx` |
| ` M` | `src/modules/leads/presentation/components/lead-transition/LeadTransitionRequirementsModal.tsx` |
| ` M` | `src/modules/leads/presentation/hooks/useLeadDetailDialogs.ts` |
| ` M` | `src/modules/leads/presentation/hooks/useLeadFilters.ts` |
| ` M` | `src/modules/leads/presentation/hooks/useLeadFormController.tsx` |
| ` M` | `src/modules/orders/presentation/components/OrderCustomerDocument.tsx` |
| ` M` | `src/modules/orders/presentation/hooks/useOrderFormController.tsx` |
| ` M` | `src/modules/orders/presentation/hooks/useOrderListController.tsx` |
| ` M` | `src/modules/orders/presentation/list/OrderStatisticsDrawer.tsx` |
| ` M` | `src/modules/orders/presentation/pages/OrderDetailPage.tsx` |
| ` M` | `src/modules/orders/presentation/views/OrderListView.tsx` |
| ` M` | `src/modules/organizations/application/vertical-slice/organizationAuthoritativeQueries.ts` |
| ` M` | `src/modules/organizations/detail-route.tsx` |
| ` M` | `src/modules/organizations/presentation/pages/OrganizationAccountDetailPage.tsx` |
| ` M` | `src/modules/organizations/presentation/pages/OrganizationAccountListPage.tsx` |
| ` M` | `src/modules/products/detail-route.tsx` |
| ` M` | `src/modules/products/presentation/components/ProductFormModal.tsx` |
| ` M` | `src/modules/products/presentation/components/ProductPickerModal.tsx` |
| ` M` | `src/modules/products/presentation/hooks/useProductListController.ts` |
| ` M` | `src/modules/quotes/application/ports/QuoteApiRuntime.ts` |
| ` M` | `src/modules/quotes/application/read-models/quoteReadModel.ts` |
| ` M` | `src/modules/quotes/domain/model/quote.types.ts` |
| ` M` | `src/modules/quotes/domain/rules/quoteCalculations.ts` |
| ` M` | `src/modules/quotes/domain/rules/quoteDraftPricing.ts` |
| ` M` | `src/modules/quotes/infrastructure/http/QuoteApiMapper.ts` |
| ` M` | `src/modules/quotes/infrastructure/http/QuoteHttpApiAdapter.ts` |
| ` M` | `src/modules/quotes/infrastructure/http/createQuoteConnectedApiRuntime.ts` |
| ` M` | `src/modules/quotes/presentation/components/QuoteApprovalBadge.tsx` |
| ` M` | `src/modules/quotes/presentation/components/QuoteBuilderPreview.tsx` |
| ` M` | `src/modules/quotes/presentation/components/QuoteBuilderProductPicker.tsx` |
| ` M` | `src/modules/quotes/presentation/components/QuoteDeliveryConfirmationModal.tsx` |
| ` M` | `src/modules/quotes/presentation/components/QuoteFilterPopover.tsx` |
| ` M` | `src/modules/quotes/presentation/hooks/useQuoteBuilderController.tsx` |
| ` M` | `src/modules/quotes/presentation/hooks/useQuotes.ts` |
| ` M` | `src/modules/quotes/presentation/model/quoteActionPolicy.ts` |
| ` M` | `src/modules/quotes/presentation/pages/QuoteDetailPage.tsx` |
| ` M` | `src/modules/quotes/presentation/pages/QuoteListPage.tsx` |
| ` M` | `src/modules/quotes/presentation/views/QuoteBuilderView.tsx` |
| ` M` | `src/modules/quotes/public/index.ts` |
| ` M` | `src/modules/quotes/public/quotes.ts` |
| ` M` | `src/modules/quotes/runtime/createQuoteDemoApiRuntime.ts` |
| ` M` | `src/modules/returns/detail-route.tsx` |
| ` M` | `src/modules/returns/presentation/pages/ReturnListPage.tsx` |
| ` M` | `src/modules/shipping/detail-route.tsx` |
| ` M` | `src/modules/shipping/presentation/pages/ShippingBookingListPage.tsx` |
| ` M` | `src/modules/support/detail-route.tsx` |
| ` M` | `src/modules/tasks/application/vertical-slice/taskAuthoritativeQueries.ts` |
| ` M` | `src/modules/tasks/detail-route.tsx` |
| ` M` | `src/modules/tasks/presentation/components/TaskCreateModal.tsx` |
| ` M` | `src/modules/tasks/presentation/hooks/useTaskCreateController.ts` |
| ` M` | `src/platform/access-control/domain/capabilityCatalog.ts` |
| ` M` | `src/platform/access-control/domain/effectiveRecordAccessCatalog.ts` |
| ` M` | `src/platform/access-control/react/EffectiveRecordAccessBoundary.tsx` |
| ` M` | `src/platform/api/catalog/generatedApiOperationCatalog.ts` |
| ` M` | `src/platform/api/client/FetchHttpClient.ts` |
| ` M` | `src/platform/api/client/httpSerialization.ts` |
| ` M` | `src/platform/api/contracts/generatedOpenApiRuntimeContract.ts` |
| ` M` | `src/platform/api/contracts/generatedProductionQueryRegistry.ts` |
| ` M` | `src/platform/api/contracts/openApiRuntimeValidation.ts` |
| ` M` | `src/platform/api/generated/accessGovernanceApi.ts` |
| ` M` | `src/platform/api/generated/aiApi.ts` |
| ` M` | `src/platform/api/generated/commercialApi.ts` |
| ` M` | `src/platform/api/generated/communicationsApi.ts` |
| ` M` | `src/platform/api/generated/crmConfigurationApi.ts` |
| ` M` | `src/platform/api/generated/financialApi.ts` |
| ` M` | `src/platform/api/generated/financialConfigurationApi.ts` |
| ` M` | `src/platform/api/generated/identityApi.ts` |
| ` M` | `src/platform/api/generated/integrationConfigurationApi.ts` |
| ` M` | `src/platform/api/generated/productConfigurationApi.ts` |
| ` M` | `src/platform/api/generated/receivablesApi.ts` |
| ` M` | `src/platform/api/generated/studioQuickSetupApi.ts` |
| ` M` | `src/platform/api/generated/workspaceBootstrapApi.ts` |
| ` M` | `src/platform/api/generated/workspaceConfigurationApi.ts` |
| ` M` | `src/platform/connected-configuration/connectedConfigurationAvailability.ts` |
| ` M` | `src/platform/developer-configuration/developerConfigurationRuntime.ts` |
| ` M` | `src/platform/navigation/routeKeys.ts` |
| ` M` | `src/platform/workspace-config/index.ts` |
| ` M` | `src/platform/workspace-config/useWorkspaceOperationalConfiguration.ts` |
| ` M` | `src/platform/workspace-config/workspaceOperationalConfigurationRuntime.ts` |
| ` M` | `src/platform/workspace-context/runtime/connectedWorkspaceContextRuntime.ts` |
| ` M` | `src/platform/workspace-context/workspaceContextRuntime.ts` |
| ` M` | `src/shared/operations/useServerPagedCollection.ts` |
| ` M` | `src/workflows/lead-qualification/domain/leadQualification.rules.ts` |
| ` M` | `src/workflows/lead-qualification/presentation/hooks/useLeadQualificationController.ts` |
| ` M` | `src/workflows/lead-qualification/presentation/model/leadOpportunityIntent.ts` |
| ` M` | `src/workflows/lead-qualification/presentation/pages/LeadSellNowPage.tsx` |
| ` M` | `src/workflows/lead-qualification/presentation/views/LeadQualificationView.tsx` |
| ` M` | `src/workspaces/crm/ai-context/presentation/useGlobalAiContext.ts` |
| ` M` | `src/workspaces/crm/presentation/pages/DashboardPage.tsx` |
| ` M` | `src/workspaces/crm/presentation/pages/ReportsPage.tsx` |
| ` M` | `src/workspaces/crm/read-models/dashboard/useDashboardReadModel.ts` |
| ` M` | `src/workspaces/people-access/presentation/pages/UsersPermissionsPage.tsx` |
| ` M` | `src/workspaces/studio/infrastructure/AiConfigurationHttpAdapter.ts` |
| ` M` | `src/workspaces/studio/navigation/studioSectionRegistry.ts` |
| ` M` | `src/workspaces/studio/presentation/pages/StudioIndexRoute.tsx` |
| ` M` | `src/workspaces/studio/presentation/views/AiConfigurationView.tsx` |
| ` M` | `src/workspaces/studio/presentation/views/BusinessInformationView.tsx` |
| ` M` | `src/workspaces/studio/presentation/views/FeatureUsageView.tsx` |
| ` M` | `src/workspaces/studio/presentation/views/InformationFieldsView.tsx` |
| ` M` | `src/workspaces/studio/presentation/views/IntegrationsView.tsx` |
| ` M` | `src/workspaces/studio/presentation/views/InvoiceInformationView.tsx` |
| ` M` | `src/workspaces/studio/presentation/views/LocaleRegionView.tsx` |
| ` M` | `src/workspaces/studio/presentation/views/PaymentInformationView.tsx` |
| ` M` | `src/workspaces/studio/presentation/views/PipelinesStatusesView.tsx` |
| ` M` | `src/workspaces/studio/presentation/views/ProductTypesView.tsx` |
| ` M` | `src/workspaces/studio/presentation/views/WebhooksApiView.tsx` |
| ` M` | `src/workspaces/studio/runtime/studioCoreRuntime.ts` |
| ` M` | `tests/fixtures/backend-contract/commercial-read-models/deal-detail.json` |
| ` M` | `tests/fixtures/backend-contract/commercial-read-models/deal-list.json` |
| ` M` | `tests/fixtures/runtime/check-lead-tag-mutation.mts` |
| ` M` | `tests/fixtures/runtime/check-safe-save-consumers.mts` |
| ` M` | `tests/quality/architecture/check-api-contract.mts` |
| ` M` | `tests/quality/architecture/check-architecture.mjs` |
| ` M` | `tests/quality/architecture/check-backend-audit-viewer.mts` |
| ` M` | `tests/quality/architecture/check-backend-readiness.mts` |
| ` M` | `tests/quality/architecture/check-commercial-authoritative-queries.mts` |
| ` M` | `tests/quality/architecture/check-connected-platform-configuration-authority.mts` |
| ` M` | `tests/quality/architecture/check-deal-stage-window-query.mts` |
| ` M` | `tests/quality/architecture/check-effective-record-access-authority.mts` |
| ` M` | `tests/quality/architecture/check-http-api-foundation.mts` |
| ` M` | `tests/quality/architecture/check-lead-qualification-api-boundary.mts` |
| ` M` | `tests/quality/architecture/check-mutation-authority-negative-controls.mts` |
| ` M` | `tests/quality/architecture/check-partial-commit-outcomes.mts` |
| ` M` | `tests/quality/architecture/check-post-commit-projection-writes.mts` |
| ` M` | `tests/quality/architecture/check-quality-pipeline.mts` |
| ` M` | `tests/quality/architecture/check-quote-api-boundary.mts` |
| ` M` | `tests/quality/architecture/check-server-side-list-query.mts` |
| ` M` | `tests/quality/architecture/check-studio-single-authority.mts` |
| ` M` | `tests/quality/architecture/check-workflow-coordinator-ownership.mts` |
| ` M` | `tests/quality/backend-contract/check-backend-contract-hardening.mjs` |
| ` M` | `tests/quality/contracts/check-bilingual-ui.mts` |
| ` M` | `tests/quality/contracts/check-canonical-crm-form-contracts.mts` |
| ` M` | `tests/quality/contracts/check-deal-stage-localization.mts` |
| ` M` | `tests/quality/contracts/check-display-format-contracts.mts` |
| ` M` | `tests/quality/contracts/check-frontend-quality-contracts.mts` |
| ` M` | `tests/quality/contracts/check-opportunity-presentation-contracts.mts` |
| ` M` | `tests/quality/contracts/check-order-to-cash-cleanup.mts` |
| ` M` | `tests/quality/contracts/check-product-space-contracts.mts` |
| ` M` | `tests/quality/contracts/check-quote-approval-delivery-contracts.mts` |
| ` M` | `tests/quality/contracts/check-shell-ai-experience.mts` |
| ` M` | `tests/quality/contracts/check-studio-configuration-experience.mts` |
| ` M` | `tests/quality/contracts/check-task-create-form-contracts.mts` |
| ` M` | `tests/quality/contracts/check-user-facing-copy-contracts.mts` |
| ` M` | `tests/quality/contracts/check-work-operations-ux.mts` |
| ` M` | `tests/quality/integration/check-connected-unavailable-port-containment.mts` |
| ` M` | `tests/quality/integration/check-crm-ui-interaction-contracts.mts` |
| ` M` | `tests/quality/integration/check-deal-product-form-parity.mts` |
| ` M` | `tests/quality/integration/check-lead-handover.mts` |
| ` M` | `tests/quality/integration/check-order-creation-contracts.mts` |
| ` M` | `tests/quality/integration/check-read-only-cross-domain.mts` |
| ` M` | `tests/quality/integration/check-role-based-navigation.mts` |
| ` M` | `tests/quality/integration/check-studio-module-propagation.mts` |
| ` M` | `tests/quality/integration/check-workspace-governance-contracts.mts` |
| ` M` | `tests/quality/route-smoke/check-route-module-loads.mts` |
| ` M` | `tests/quality/route-smoke/check-studio-route-runtime.mts` |
| ` M` | `tests/quality/unit/check-ai-application-contracts.mts` |
| ` M` | `tests/quality/unit/check-customers.mts` |
| ` M` | `tests/quality/unit/check-quotes.mts` |
| ` M` | `vite.config.ts` |
| `??` | `backend-work/handoffs/COMMERCIAL-FLOW-REGRESSION-REMEDIATION-20261015-FE/analysis.json` |
| `??` | `backend-work/handoffs/COMMERCIAL-FLOW-REGRESSION-REMEDIATION-20261015-FE/architecture.json` |
| `??` | `backend-work/handoffs/CONTACT-FLS-LEGACY-FIXTURE-COMPATIBILITY-20261015-FE/analysis.json` |
| `??` | `backend-work/handoffs/CONTACT-FLS-LEGACY-FIXTURE-COMPATIBILITY-20261015-FE/architecture.json` |
| `??` | `backend-work/handoffs/CONTACT-FLS-LEGACY-FIXTURE-COMPATIBILITY-20261015-FE/change.json` |
| `??` | `backend-work/handoffs/CONTACT-FLS-UX-REMEDIATION-20261015-FE/analysis.json` |
| `??` | `backend-work/handoffs/CONTACT-FLS-UX-REMEDIATION-20261015-FE/architecture.json` |
| `??` | `backend-work/handoffs/CONTACT-FLS-UX-REMEDIATION-20261015-FE/change.json` |
| `??` | `backend-work/handoffs/DEAL-LOST-REGRESSION-REMEDIATION-20261015-FE/analysis.json` |
| `??` | `backend-work/handoffs/DEAL-LOST-REGRESSION-REMEDIATION-20261015-FE/architecture.json` |
| `??` | `backend-work/handoffs/PRIMARY-SOURCE-INTEGRATION-20261015-FE/analysis.json` |
| `??` | `backend-work/handoffs/PRIMARY-SOURCE-INTEGRATION-20261015-FE/architecture.json` |
| `??` | `backend-work/handoffs/PRIMARY-SOURCE-INTEGRATION-20261015-FE/change.json` |
| `??` | `backend-work/state/COMMERCIAL-FLOW-REGRESSION-REMEDIATION-20261015-FE.json` |
| `??` | `backend-work/state/CONTACT-FLS-LEGACY-FIXTURE-COMPATIBILITY-20261015-FE.json` |
| `??` | `backend-work/state/CONTACT-FLS-UX-REMEDIATION-20261015-FE.json` |
| `??` | `backend-work/state/DEAL-LOST-REGRESSION-REMEDIATION-20261015-FE.json` |
| `??` | `backend-work/state/PRIMARY-SOURCE-INTEGRATION-20261015-FE.json` |
| `??` | `backend-work/state/UCRM-CONTACT-CONNECTED-MEETING-ASSIGNEE-RESOLUTION-FINAL-VERIFICATION.json` |
| `??` | `backend-work/state/UCRM-CUSTOMER-RELATIONSHIP-PANEL-OVERVIEW.json` |
| `??` | `backend-work/state/UCRM-CUSTOMER-TASK-REFERENCE-CONNECTED-FRONTEND-FINAL.json` |
| `??` | `backend-work/state/UCRM-ORGANIZATION-RELATIONSHIP-PANEL-OVERVIEW.json` |
| `??` | `backend-work/state/UCRM-ORGANIZATION-TASK-REFERENCE-INTEGRITY-AND-MEETING-FRONTEND.json` |
| `??` | `backend-work/tasks/CONTACT-FLS-LEGACY-FIXTURE-COMPATIBILITY-20261015-FE.json` |
| `??` | `backend-work/tasks/CONTACT-FLS-UX-REMEDIATION-20261015-FE.json` |
| `??` | `backend-work/tasks/PRIMARY-SOURCE-INTEGRATION-20261015-FE.json` |
| `??` | `backend-work/tasks/UCRM-CONTACT-CONNECTED-MEETING-ASSIGNEE-RESOLUTION-FINAL-VERIFICATION.json` |
| `??` | `backend-work/tasks/UCRM-CONTACT-CONNECTED-MEETING-ASSIGNEE-RESOLUTION-TYPE-BOUNDARY.json` |
| `??` | `backend-work/tasks/UCRM-CONTACT-CONNECTED-MEETING-ASSIGNEE-RESOLUTION.json` |
| `??` | `backend-work/tasks/UCRM-CUSTOMER-RELATIONSHIP-PANEL-OVERVIEW.json` |
| `??` | `backend-work/tasks/UCRM-CUSTOMER-TASK-REFERENCE-CONNECTED-FRONTEND-FINAL.json` |
| `??` | `backend-work/tasks/UCRM-CUSTOMER-TASK-REFERENCE-CONNECTED-FRONTEND.json` |
| `??` | `backend-work/tasks/UCRM-ORGANIZATION-RELATIONSHIP-PANEL-OVERVIEW.json` |
| `??` | `backend-work/tasks/UCRM-ORGANIZATION-TASK-REFERENCE-INTEGRITY-AND-MEETING-FRONTEND.json` |
| `??` | `docs/architecture/deal-pipelines.md` |
| `??` | `docs/architecture/workspace-pipeline-selection.md` |
| `??` | `docs/design/studio-connections-api/artifact-verification.json` |
| `??` | `docs/design/studio-connections-api/capability-matrix.csv` |
| `??` | `docs/design/studio-connections-api/design.md` |
| `??` | `docs/design/studio-connections-api/prototype-checks.json` |
| `??` | `docs/design/studio-connections-api/review-evidence.json` |
| `??` | `docs/design/studio-connections-api/source-and-file-plan.md` |
| `??` | `docs/design/studio-connections-api/wireframe-api.png` |
| `??` | `docs/design/studio-connections-api/wireframe-apps.png` |
| `??` | `docs/design/studio-connections-api/wireframe-inbound.png` |
| `??` | `docs/design/studio-connections-api/wireframe-mobile.png` |
| `??` | `docs/design/studio-connections-api/wireframe-outbound-detail.png` |
| `??` | `docs/design/studio-connections-api/wireframe-outbound.png` |
| `??` | `docs/design/studio-connections-api/wireframe-overview.png` |
| `??` | `docs/design/studio-connections-api/wireframe-secret-layout.png` |
| `??` | `docs/design/studio-connections-api/wireframe-state-empty.png` |
| `??` | `docs/design/studio-connections-api/wireframe-state-error.png` |
| `??` | `docs/design/studio-connections-api/wireframe-state-expired.png` |
| `??` | `docs/design/studio-connections-api/wireframe-state-loading.png` |
| `??` | `docs/design/studio-connections-api/wireframe-state-switch.png` |
| `??` | `docs/design/studio-connections-api/wireframes.html` |
| `??` | `docs/verification/access-notices-results.md` |
| `??` | `docs/verification/customer-ui-results.md` |
| `??` | `docs/verification/deal-workspace-results.md` |
| `??` | `docs/verification/evidence/access-notices/acceptance.json` |
| `??` | `docs/verification/evidence/access-notices/access-after-buyer-unavailable.png` |
| `??` | `docs/verification/evidence/access-notices/access-after-desktop-elapsed.png` |
| `??` | `docs/verification/evidence/access-notices/access-after-desktop-explanation.png` |
| `??` | `docs/verification/evidence/access-notices/access-after-desktop-initial.png` |
| `??` | `docs/verification/evidence/access-notices/access-after-mobile-explanation.png` |
| `??` | `docs/verification/evidence/access-notices/access-after-mobile-initial.png` |
| `??` | `docs/verification/evidence/access-notices/access-after-mobile.png` |
| `??` | `docs/verification/evidence/access-notices/access-after-product-existing-error.png` |
| `??` | `docs/verification/evidence/access-notices/access-after-verification-failed.png` |
| `??` | `docs/verification/evidence/access-notices/access-after-workspace-isolation.png` |
| `??` | `docs/verification/evidence/access-notices/access-before-desktop-elapsed.png` |
| `??` | `docs/verification/evidence/access-notices/access-before-desktop-initial.png` |
| `??` | `docs/verification/evidence/access-notices/access-before-mobile.png` |
| `??` | `docs/verification/evidence/access-notices/baseline.json` |
| `??` | `docs/verification/evidence/access-notices/data-preservation.json` |
| `??` | `docs/verification/evidence/access-notices/quality.json` |
| `??` | `docs/verification/evidence/access-notices/source-preservation.json` |
| `??` | `docs/verification/evidence/customer-ui/after-data-preservation.json` |
| `??` | `docs/verification/evidence/customer-ui/backend-scope.json` |
| `??` | `docs/verification/evidence/customer-ui/customer-list-identity.trx` |
| `??` | `docs/verification/evidence/customer-ui/customer-ui-after-contact-detail-desktop.png` |
| `??` | `docs/verification/evidence/customer-ui/customer-ui-after-contact-detail-laptop.png` |
| `??` | `docs/verification/evidence/customer-ui/customer-ui-after-contact-detail-mobile.png` |
| `??` | `docs/verification/evidence/customer-ui/customer-ui-after-contact-detail-tablet.png` |
| `??` | `docs/verification/evidence/customer-ui/customer-ui-after-contact-list-desktop.png` |
| `??` | `docs/verification/evidence/customer-ui/customer-ui-after-contact-list-laptop.png` |
| `??` | `docs/verification/evidence/customer-ui/customer-ui-after-contact-list-mobile.png` |
| `??` | `docs/verification/evidence/customer-ui/customer-ui-after-contact-list-tablet.png` |
| `??` | `docs/verification/evidence/customer-ui/customer-ui-after-customer-detail-desktop.png` |
| `??` | `docs/verification/evidence/customer-ui/customer-ui-after-customer-detail-laptop.png` |
| `??` | `docs/verification/evidence/customer-ui/customer-ui-after-customer-detail-mobile.png` |
| `??` | `docs/verification/evidence/customer-ui/customer-ui-after-customer-detail-tablet.png` |
| `??` | `docs/verification/evidence/customer-ui/customer-ui-after-customer-list-desktop.png` |
| `??` | `docs/verification/evidence/customer-ui/customer-ui-after-customer-list-laptop.png` |
| `??` | `docs/verification/evidence/customer-ui/customer-ui-after-customer-list-mobile.png` |
| `??` | `docs/verification/evidence/customer-ui/customer-ui-after-customer-list-tablet.png` |
| `??` | `docs/verification/evidence/customer-ui/customer-ui-after.json` |
| `??` | `docs/verification/evidence/customer-ui/customer-ui-before-contact-detail-desktop.png` |
| `??` | `docs/verification/evidence/customer-ui/customer-ui-before-contact-detail-laptop.png` |
| `??` | `docs/verification/evidence/customer-ui/customer-ui-before-contact-detail-mobile.png` |
| `??` | `docs/verification/evidence/customer-ui/customer-ui-before-contact-detail-tablet.png` |
| `??` | `docs/verification/evidence/customer-ui/customer-ui-before-contact-list-desktop.png` |
| `??` | `docs/verification/evidence/customer-ui/customer-ui-before-contact-list-laptop.png` |
| `??` | `docs/verification/evidence/customer-ui/customer-ui-before-contact-list-mobile.png` |
| `??` | `docs/verification/evidence/customer-ui/customer-ui-before-contact-list-tablet.png` |
| `??` | `docs/verification/evidence/customer-ui/customer-ui-before-customer-detail-desktop.png` |
| `??` | `docs/verification/evidence/customer-ui/customer-ui-before-customer-detail-laptop.png` |
| `??` | `docs/verification/evidence/customer-ui/customer-ui-before-customer-detail-mobile.png` |
| `??` | `docs/verification/evidence/customer-ui/customer-ui-before-customer-detail-tablet.png` |
| `??` | `docs/verification/evidence/customer-ui/customer-ui-before-customer-list-desktop.png` |
| `??` | `docs/verification/evidence/customer-ui/customer-ui-before-customer-list-laptop.png` |
| `??` | `docs/verification/evidence/customer-ui/customer-ui-before-customer-list-mobile.png` |
| `??` | `docs/verification/evidence/customer-ui/customer-ui-before-customer-list-tablet.png` |
| `??` | `docs/verification/evidence/customer-ui/customer-ui-before.json` |
| `??` | `docs/verification/evidence/customer-ui/frontend-scope.json` |
| `??` | `docs/verification/evidence/customer-ui/full-verify.json` |
| `??` | `docs/verification/evidence/customer-ui/quality.access-control-contracts.json` |
| `??` | `docs/verification/evidence/customer-ui/quality.api-contract.json` |
| `??` | `docs/verification/evidence/customer-ui/quality.api-management-boundaries.json` |
| `??` | `docs/verification/evidence/customer-ui/quality.backend-readiness.json` |
| `??` | `docs/verification/evidence/customer-ui/quality.bilingual-ui.json` |
| `??` | `docs/verification/evidence/customer-ui/quality.customer-contact-ui-parity.json` |
| `??` | `docs/verification/evidence/customer-ui/quality.customer-integration-contracts.json` |
| `??` | `docs/verification/evidence/customer-ui/quality.customer-relationship-completeness.json` |
| `??` | `docs/verification/evidence/customer-ui/quality.customer-relationship-integrity.json` |
| `??` | `docs/verification/evidence/customer-ui/quality.customer-view-boundary.json` |
| `??` | `docs/verification/evidence/customer-ui/quality.customer360-reconciliation.json` |
| `??` | `docs/verification/evidence/customer-ui/quality.effective-record-access.json` |
| `??` | `docs/verification/evidence/customer-ui/quality.frontend-backend-separation.json` |
| `??` | `docs/verification/evidence/customer-ui/quality.guidance-contracts.json` |
| `??` | `docs/verification/evidence/customer-ui/quality.guidance-runtime.json` |
| `??` | `docs/verification/evidence/customer-ui/quality.presentation-responsibility.json` |
| `??` | `docs/verification/evidence/customer-ui/quality.server-side-list-query.json` |
| `??` | `docs/verification/evidence/customer-ui/sql-identity.json` |
| `??` | `docs/verification/evidence/customer-ui/typecheck.json` |
| `??` | `docs/verification/evidence/deal-workspace/after-data-preservation.json` |
| `??` | `docs/verification/evidence/deal-workspace/deal-workspace-after-desktop.png` |
| `??` | `docs/verification/evidence/deal-workspace/deal-workspace-after-laptop.png` |
| `??` | `docs/verification/evidence/deal-workspace/deal-workspace-after-mobile.png` |
| `??` | `docs/verification/evidence/deal-workspace/deal-workspace-after-small-desktop.png` |
| `??` | `docs/verification/evidence/deal-workspace/deal-workspace-after-tablet.png` |
| `??` | `docs/verification/evidence/deal-workspace/deal-workspace-after.json` |
| `??` | `docs/verification/evidence/deal-workspace/deal-workspace-before-desktop.png` |
| `??` | `docs/verification/evidence/deal-workspace/deal-workspace-before-laptop.png` |
| `??` | `docs/verification/evidence/deal-workspace/deal-workspace-before-mobile.png` |
| `??` | `docs/verification/evidence/deal-workspace/deal-workspace-before-small-desktop.png` |
| `??` | `docs/verification/evidence/deal-workspace/deal-workspace-before-tablet.png` |
| `??` | `docs/verification/evidence/deal-workspace/deal-workspace-before.json` |
| `??` | `docs/verification/evidence/deal-workspace/deal-workspace-tab-Báo giá.png` |
| `??` | `docs/verification/evidence/deal-workspace/deal-workspace-tab-Công việc.png` |
| `??` | `docs/verification/evidence/deal-workspace/deal-workspace-tab-Dự báo.png` |
| `??` | `docs/verification/evidence/deal-workspace/deal-workspace-tab-Hoạt động.png` |
| `??` | `docs/verification/evidence/deal-workspace/deal-workspace-tab-Tổng quan.png` |
| `??` | `docs/verification/evidence/deal-workspace/presentation/pipeline-custom-desktop.png` |
| `??` | `docs/verification/evidence/deal-workspace/presentation/pipeline-custom-mobile.png` |
| `??` | `docs/verification/evidence/deal-workspace/presentation/pipeline-default-desktop.png` |
| `??` | `docs/verification/evidence/deal-workspace/presentation/pipeline-default-mobile.png` |
| `??` | `docs/verification/evidence/deal-workspace/presentation/pipeline-few-desktop.png` |
| `??` | `docs/verification/evidence/deal-workspace/presentation/pipeline-few-mobile.png` |
| `??` | `docs/verification/evidence/deal-workspace/presentation/pipeline-inactive-desktop.png` |
| `??` | `docs/verification/evidence/deal-workspace/presentation/pipeline-inactive-mobile.png` |
| `??` | `docs/verification/evidence/deal-workspace/presentation/pipeline-long-desktop.png` |
| `??` | `docs/verification/evidence/deal-workspace/presentation/pipeline-long-mobile.png` |
| `??` | `docs/verification/evidence/deal-workspace/presentation/pipeline-lost-desktop.png` |
| `??` | `docs/verification/evidence/deal-workspace/presentation/pipeline-lost-mobile.png` |
| `??` | `docs/verification/evidence/deal-workspace/presentation/pipeline-many-desktop.png` |
| `??` | `docs/verification/evidence/deal-workspace/presentation/pipeline-many-mobile.png` |
| `??` | `docs/verification/evidence/deal-workspace/presentation/pipeline-reordered-desktop.png` |
| `??` | `docs/verification/evidence/deal-workspace/presentation/pipeline-reordered-mobile.png` |
| `??` | `docs/verification/evidence/deal-workspace/presentation/pipeline-won-desktop.png` |
| `??` | `docs/verification/evidence/deal-workspace/presentation/pipeline-won-mobile.png` |
| `??` | `docs/verification/evidence/deal-workspace/presentation/quote-ACCEPTED-desktop.png` |
| `??` | `docs/verification/evidence/deal-workspace/presentation/quote-ACCEPTED-mobile.png` |
| `??` | `docs/verification/evidence/deal-workspace/presentation/quote-DRAFT-desktop.png` |
| `??` | `docs/verification/evidence/deal-workspace/presentation/quote-DRAFT-mobile.png` |
| `??` | `docs/verification/evidence/deal-workspace/presentation/quote-EXPIRED-desktop.png` |
| `??` | `docs/verification/evidence/deal-workspace/presentation/quote-EXPIRED-mobile.png` |
| `??` | `docs/verification/evidence/deal-workspace/presentation/quote-REJECTED-desktop.png` |
| `??` | `docs/verification/evidence/deal-workspace/presentation/quote-REJECTED-mobile.png` |
| `??` | `docs/verification/evidence/deal-workspace/presentation/quote-REVIEW-desktop.png` |
| `??` | `docs/verification/evidence/deal-workspace/presentation/quote-REVIEW-mobile.png` |
| `??` | `docs/verification/evidence/deal-workspace/presentation/quote-SENT-desktop.png` |
| `??` | `docs/verification/evidence/deal-workspace/presentation/quote-SENT-mobile.png` |
| `??` | `docs/verification/evidence/deal-workspace/presentation/quote-multiple-desktop.png` |
| `??` | `docs/verification/evidence/deal-workspace/presentation/quote-multiple-mobile.png` |
| `??` | `docs/verification/evidence/deal-workspace/presentation/quote-none-desktop.png` |
| `??` | `docs/verification/evidence/deal-workspace/presentation/quote-none-mobile.png` |
| `??` | `docs/verification/evidence/deal-workspace/presentation/results.json` |
| `??` | `docs/verification/evidence/deal-workspace/scope.json` |
| `??` | `docs/verification/evidence/deal-workspace/sql-activity-relations.json` |
| `??` | `docs/verification/evidence/internal-notices/after-data-preservation.json` |
| `??` | `docs/verification/evidence/internal-notices/backend-scope.json` |
| `??` | `docs/verification/evidence/internal-notices/frontend-scope.json` |
| `??` | `docs/verification/evidence/internal-notices/full-verify.json` |
| `??` | `docs/verification/evidence/internal-notices/internal-notices-after-customer-desktop.png` |
| `??` | `docs/verification/evidence/internal-notices/internal-notices-after-customer-laptop.png` |
| `??` | `docs/verification/evidence/internal-notices/internal-notices-after-customer-mobile.png` |
| `??` | `docs/verification/evidence/internal-notices/internal-notices-after-deal-desktop.png` |
| `??` | `docs/verification/evidence/internal-notices/internal-notices-after-deal-laptop.png` |
| `??` | `docs/verification/evidence/internal-notices/internal-notices-after-deal-mobile.png` |
| `??` | `docs/verification/evidence/internal-notices/internal-notices-after-error.png` |
| `??` | `docs/verification/evidence/internal-notices/internal-notices-after.json` |
| `??` | `docs/verification/evidence/internal-notices/internal-notices-before-customer-desktop-elapsed.png` |
| `??` | `docs/verification/evidence/internal-notices/internal-notices-before-customer-desktop-initial.png` |
| `??` | `docs/verification/evidence/internal-notices/internal-notices-before-customer-laptop-elapsed.png` |
| `??` | `docs/verification/evidence/internal-notices/internal-notices-before-customer-laptop-initial.png` |
| `??` | `docs/verification/evidence/internal-notices/internal-notices-before-customer-mobile-elapsed.png` |
| `??` | `docs/verification/evidence/internal-notices/internal-notices-before-customer-mobile-initial.png` |
| `??` | `docs/verification/evidence/internal-notices/internal-notices-before-deal-desktop-elapsed.png` |
| `??` | `docs/verification/evidence/internal-notices/internal-notices-before-deal-desktop-initial.png` |
| `??` | `docs/verification/evidence/internal-notices/internal-notices-before-deal-laptop-elapsed.png` |
| `??` | `docs/verification/evidence/internal-notices/internal-notices-before-deal-laptop-initial.png` |
| `??` | `docs/verification/evidence/internal-notices/internal-notices-before-deal-mobile-elapsed.png` |
| `??` | `docs/verification/evidence/internal-notices/internal-notices-before-deal-mobile-initial.png` |
| `??` | `docs/verification/evidence/internal-notices/internal-notices-before.json` |
| `??` | `docs/verification/evidence/internal-notices/quality.access-control-contracts.json` |
| `??` | `docs/verification/evidence/internal-notices/quality.api-contract.json` |
| `??` | `docs/verification/evidence/internal-notices/quality.customer-contact-ui-parity.json` |
| `??` | `docs/verification/evidence/internal-notices/quality.customers.json` |
| `??` | `docs/verification/evidence/internal-notices/quality.deals.json` |
| `??` | `docs/verification/evidence/internal-notices/quality.effective-record-access.json` |
| `??` | `docs/verification/evidence/internal-notices/quality.error-handling.json` |
| `??` | `docs/verification/evidence/internal-notices/quality.frontend-backend-separation.json` |
| `??` | `docs/verification/evidence/internal-notices/quality.guidance-contracts.json` |
| `??` | `docs/verification/evidence/internal-notices/quality.guidance-runtime.json` |
| `??` | `docs/verification/evidence/internal-notices/quality.mutation-conflict-ux.json` |
| `??` | `docs/verification/evidence/internal-notices/quality.presentation-responsibility.json` |
| `??` | `docs/verification/evidence/internal-notices/quality.quality-pipeline.json` |
| `??` | `docs/verification/evidence/internal-notices/quality.read-only-cross-domain.json` |
| `??` | `docs/verification/evidence/internal-notices/quality.user-facing-error-presentation.json` |
| `??` | `docs/verification/evidence/internal-notices/quality.user-facing-error-safety.json` |
| `??` | `docs/verification/evidence/internal-notices/typecheck.json` |
| `??` | `docs/verification/evidence/remaining-access/after-data-preservation.json` |
| `??` | `docs/verification/evidence/remaining-access/backend-scope.json` |
| `??` | `docs/verification/evidence/remaining-access/frontend-scope.json` |
| `??` | `docs/verification/evidence/remaining-access/full-verify.json` |
| `??` | `docs/verification/evidence/remaining-access/quality.access-control-contracts.json` |
| `??` | `docs/verification/evidence/remaining-access/quality.customer-contact-ui-parity.json` |
| `??` | `docs/verification/evidence/remaining-access/quality.customers.json` |
| `??` | `docs/verification/evidence/remaining-access/quality.deals.json` |
| `??` | `docs/verification/evidence/remaining-access/quality.effective-record-access.json` |
| `??` | `docs/verification/evidence/remaining-access/quality.guidance-contracts.json` |
| `??` | `docs/verification/evidence/remaining-access/quality.guidance-runtime.json` |
| `??` | `docs/verification/evidence/remaining-access/quality.people-access-ux.json` |
| `??` | `docs/verification/evidence/remaining-access/quality.presentation-responsibility.json` |
| `??` | `docs/verification/evidence/remaining-access/quality.quality-pipeline.json` |
| `??` | `docs/verification/evidence/remaining-access/quality.studio-single-authority.json` |
| `??` | `docs/verification/evidence/remaining-access/remaining-access-after-critical-error.png` |
| `??` | `docs/verification/evidence/remaining-access/remaining-access-after-customer-desktop-elapsed.png` |
| `??` | `docs/verification/evidence/remaining-access/remaining-access-after-customer-desktop-initial.png` |
| `??` | `docs/verification/evidence/remaining-access/remaining-access-after-customer-laptop-elapsed.png` |
| `??` | `docs/verification/evidence/remaining-access/remaining-access-after-customer-laptop-initial.png` |
| `??` | `docs/verification/evidence/remaining-access/remaining-access-after-customer-mobile-elapsed.png` |
| `??` | `docs/verification/evidence/remaining-access/remaining-access-after-customer-mobile-initial.png` |
| `??` | `docs/verification/evidence/remaining-access/remaining-access-after-deal-desktop-elapsed.png` |
| `??` | `docs/verification/evidence/remaining-access/remaining-access-after-deal-desktop-initial.png` |
| `??` | `docs/verification/evidence/remaining-access/remaining-access-after-deal-laptop-elapsed.png` |
| `??` | `docs/verification/evidence/remaining-access/remaining-access-after-deal-laptop-initial.png` |
| `??` | `docs/verification/evidence/remaining-access/remaining-access-after-deal-mobile-elapsed.png` |
| `??` | `docs/verification/evidence/remaining-access/remaining-access-after-deal-mobile-initial.png` |
| `??` | `docs/verification/evidence/remaining-access/remaining-access-after.json` |
| `??` | `docs/verification/evidence/remaining-access/remaining-access-before-customer-desktop-elapsed.png` |
| `??` | `docs/verification/evidence/remaining-access/remaining-access-before-customer-desktop-initial.png` |
| `??` | `docs/verification/evidence/remaining-access/remaining-access-before-customer-laptop-elapsed.png` |
| `??` | `docs/verification/evidence/remaining-access/remaining-access-before-customer-laptop-initial.png` |
| `??` | `docs/verification/evidence/remaining-access/remaining-access-before-customer-mobile-elapsed.png` |
| `??` | `docs/verification/evidence/remaining-access/remaining-access-before-customer-mobile-initial.png` |
| `??` | `docs/verification/evidence/remaining-access/remaining-access-before-deal-desktop-elapsed.png` |
| `??` | `docs/verification/evidence/remaining-access/remaining-access-before-deal-desktop-initial.png` |
| `??` | `docs/verification/evidence/remaining-access/remaining-access-before-deal-laptop-elapsed.png` |
| `??` | `docs/verification/evidence/remaining-access/remaining-access-before-deal-laptop-initial.png` |
| `??` | `docs/verification/evidence/remaining-access/remaining-access-before-deal-mobile-elapsed.png` |
| `??` | `docs/verification/evidence/remaining-access/remaining-access-before-deal-mobile-initial.png` |
| `??` | `docs/verification/evidence/remaining-access/remaining-access-before.json` |
| `??` | `docs/verification/evidence/remaining-access/typecheck.json` |
| `??` | `docs/verification/internal-notices-removal-results.md` |
| `??` | `docs/verification/remaining-access-notices-results.md` |
| `??` | `playwright.ai-workspace.config.ts` |
| `??` | `playwright.connections.config.ts` |
| `??` | `playwright.dashboard-connected.config.ts` |
| `??` | `playwright.deal-pipeline-review.config.ts` |
| `??` | `playwright.deal-pipeline.config.ts` |
| `??` | `playwright.deal-presentation.config.ts` |
| `??` | `playwright.multicurrency-connected.config.ts` |
| `??` | `playwright.quote-acceptance.config.ts` |
| `??` | `playwright.quote-approval.config.ts` |
| `??` | `playwright.quote-core.config.ts` |
| `??` | `playwright.quote-customer-response.config.ts` |
| `??` | `playwright.quote-delivery.config.ts` |
| `??` | `playwright.quote-documents.config.ts` |
| `??` | `playwright.quote-identity.config.ts` |
| `??` | `playwright.quote-independent-review.config.ts` |
| `??` | `playwright.quote-lifecycle.config.ts` |
| `??` | `playwright.quote-list-pipeline.config.ts` |
| `??` | `playwright.quote-manual-source.config.ts` |
| `??` | `playwright.quote-proof.config.ts` |
| `??` | `playwright.quote-scanner.config.ts` |
| `??` | `playwright.realistic-demo.config.ts` |
| `??` | `playwright.studio-connected.config.ts` |
| `??` | `review-artifacts/B2_DEAL_PRODUCT_EVIDENCE.txt` |
| `??` | `review-artifacts/B2_contact_INVENTORY_AUDIT.json` |
| `??` | `review-artifacts/B2_deal-product_INVENTORY_AUDIT.json` |
| `??` | `review-artifacts/B2_lead_INVENTORY_AUDIT.json` |
| `??` | `review-artifacts/B2_organization-customer_INVENTORY_AUDIT.json` |
| `??` | `review-artifacts/B2_shared_INVENTORY_AUDIT.json` |
| `??` | `review-artifacts/B2_task-commercial_INVENTORY_AUDIT.json` |
| `??` | `review-artifacts/CRM_B2_B5_PRESENTATION_INVENTORY.json` |
| `??` | `review-artifacts/CRM_B2_B5_PRESENTATION_INVENTORY.md` |
| `??` | `review-artifacts/CRM_B2_B5_PRESENTATION_PARITY_FROM_LEAD_001.patch` |
| `??` | `review-artifacts/CRM_B2_B5_PRESENTATION_PARITY_FROM_LEAD_001_REPORT.txt` |
| `??` | `review-artifacts/CRM_B2_B5_PRESENTATION_PARITY_FROM_LEAD_001_REVISION_01_REPORT.txt` |
| `??` | `review-artifacts/CRM_B2_B5_PRESENTATION_PARITY_MATRIX.json` |
| `??` | `review-artifacts/CRM_B2_B5_PRESENTATION_PARITY_RESULT.json` |
| `??` | `review-artifacts/CRM_B2_B5_VERIFICATION_CHECKPOINT.json` |
| `??` | `review-artifacts/CRM_B2_FORM_PARITY_REPORT.txt` |
| `??` | `review-artifacts/CRM_B2_ORGANIZATION_CUSTOMER_EVIDENCE.json` |
| `??` | `review-artifacts/CRM_B3_POPUP_PARITY_REPORT.txt` |
| `??` | `review-artifacts/CRM_B4_DIALOG_PARITY_REPORT.txt` |
| `??` | `review-artifacts/CRM_B5_CROSS_SCREEN_CONSISTENCY_REPORT.txt` |
| `??` | `review-artifacts/lead-auxiliary-audit.mts` |
| `??` | `src/ai/application/aiProviderFailure.ts` |
| `??` | `src/components/ai/AiHistoryPanel.tsx` |
| `??` | `src/components/ai/aiPresentation.ts` |
| `??` | `src/modules/contacts/presentation/model/contactFormDraft.ts` |
| `??` | `src/modules/customers/presentation/components/CustomerCode.tsx` |
| `??` | `src/modules/customers/presentation/detail/CustomerConnectedRecordHeader.tsx` |
| `??` | `src/modules/customers/presentation/detail/CustomerRelatedDeals.tsx` |
| `??` | `src/modules/customers/presentation/hooks/useCustomerConnectedWorkspace.ts` |
| `??` | `src/modules/customers/presentation/hooks/useCustomerRelatedDeals.ts` |
| `??` | `src/modules/customers/presentation/model/customerConnectedListModel.ts` |
| `??` | `src/modules/customers/presentation/model/customerPresentationLabels.ts` |
| `??` | `src/modules/deals/application/ports/DealPipelineGateway.ts` |
| `??` | `src/modules/deals/infrastructure/http/DealPipelineHttpAdapter.ts` |
| `??` | `src/modules/deals/presentation/components/DealAssignedPipelineBoards.tsx` |
| `??` | `src/modules/deals/presentation/hooks/useDealActivities.ts` |
| `??` | `src/modules/deals/presentation/hooks/useDealBuyerLabel.ts` |
| `??` | `src/modules/deals/presentation/hooks/useDealRelatedQuotes.ts` |
| `??` | `src/modules/deals/presentation/views/DealDetailField.tsx` |
| `??` | `src/modules/deals/presentation/views/DealDetailForecast.tsx` |
| `??` | `src/modules/deals/presentation/views/DealDetailNextActionPanel.tsx` |
| `??` | `src/modules/deals/presentation/views/DealDetailOverview.tsx` |
| `??` | `src/modules/deals/presentation/views/DealDetailProducts.tsx` |
| `??` | `src/modules/deals/presentation/views/dealDetailPresentation.ts` |
| `??` | `src/modules/deals/public/dealPipelines.ts` |
| `??` | `src/modules/quotes/application/ports/QuoteCommandMetadata.ts` |
| `??` | `src/modules/quotes/application/read-models/quoteAcceptanceProof.ts` |
| `??` | `src/modules/quotes/application/read-models/quoteAcceptanceReadiness.ts` |
| `??` | `src/modules/quotes/application/read-models/quoteDocumentModels.ts` |
| `??` | `src/modules/quotes/application/read-models/quoteIdentityAuthority.ts` |
| `??` | `src/modules/quotes/application/read-models/quoteIndependentReview.ts` |
| `??` | `src/modules/quotes/infrastructure/http/QuoteIdentityHttpAdapter.ts` |
| `??` | `src/modules/quotes/infrastructure/http/QuoteIndependentReviewHttpAdapter.ts` |
| `??` | `src/modules/quotes/infrastructure/http/QuoteProofHttpAdapter.ts` |
| `??` | `src/modules/quotes/presentation/components/QuoteAcceptanceProofsSection.tsx` |
| `??` | `src/modules/quotes/presentation/components/QuoteAcceptanceReadinessPanel.tsx` |
| `??` | `src/modules/quotes/presentation/components/QuoteBuyerSelector.tsx` |
| `??` | `src/modules/quotes/presentation/components/QuoteCommercialPreview.tsx` |
| `??` | `src/modules/quotes/presentation/components/QuoteCustomerResponsePanel.tsx` |
| `??` | `src/modules/quotes/presentation/components/QuoteDocumentPanel.tsx` |
| `??` | `src/modules/quotes/presentation/components/QuoteIdentityAuthorityPanel.tsx` |
| `??` | `src/modules/quotes/presentation/components/QuoteIndependentReviewPanel.tsx` |
| `??` | `src/modules/quotes/presentation/components/QuoteLifecyclePanel.tsx` |
| `??` | `src/modules/quotes/presentation/components/QuoteManualSourceControls.tsx` |
| `??` | `src/modules/quotes/presentation/components/QuoteProofScanCard.tsx` |
| `??` | `src/modules/quotes/presentation/hooks/useQuoteDocuments.ts` |
| `??` | `src/modules/quotes/presentation/hooks/useQuoteIdentityAuthority.ts` |
| `??` | `src/modules/quotes/presentation/hooks/useQuoteIndependentReview.ts` |
| `??` | `src/modules/quotes/presentation/hooks/useQuoteListQuery.ts` |
| `??` | `src/modules/quotes/presentation/hooks/useQuoteProofScan.ts` |
| `??` | `src/modules/quotes/presentation/hooks/useQuoteRevisionChain.ts` |
| `??` | `src/modules/quotes/presentation/model/quoteApprovalFeedback.ts` |
| `??` | `src/modules/quotes/presentation/model/quoteDeliveryFeedback.ts` |
| `??` | `src/modules/quotes/presentation/model/quoteDocumentLabels.ts` |
| `??` | `src/modules/quotes/presentation/model/quotePaymentAgreementDraft.ts` |
| `??` | `src/modules/quotes/presentation/model/quotePricingReconciliation.ts` |
| `??` | `src/modules/quotes/presentation/services/saveQuoteCustomerResponse.ts` |
| `??` | `src/modules/quotes/presentation/services/saveQuoteLifecycleAction.ts` |
| `??` | `src/modules/quotes/public/acceptance.ts` |
| `??` | `src/modules/quotes/public/documents.ts` |
| `??` | `src/modules/quotes/public/identity.ts` |
| `??` | `src/modules/quotes/public/proofs.ts` |
| `??` | `src/modules/quotes/public/review.ts` |
| `??` | `src/modules/tasks/presentation/model/taskCreateModalTypes.ts` |
| `??` | `src/platform/workspace-config/useWorkspaceCurrencyChoices.ts` |
| `??` | `src/platform/workspace-config/useWorkspaceOperationalConfigurationResource.ts` |
| `??` | `src/platform/workspace-config/workspaceRuntimeSettings.ts` |
| `??` | `src/workflows/lead-qualification/presentation/model/leadOpportunityFieldErrors.ts` |
| `??` | `src/workspaces/crm/ai-context/application/connectedAiRouteContext.ts` |
| `??` | `src/workspaces/crm/presentation/dashboard/ConnectedDashboardView.tsx` |
| `??` | `src/workspaces/crm/presentation/dashboard/DashboardPrimitives.tsx` |
| `??` | `src/workspaces/crm/presentation/dashboard/dashboardPresentation.ts` |
| `??` | `src/workspaces/crm/read-models/dashboard/connectedDashboardQueries.ts` |
| `??` | `src/workspaces/crm/read-models/dashboard/useConnectedDashboardReadModel.ts` |
| `??` | `src/workspaces/studio/presentation/components/AiModelSelect.tsx` |
| `??` | `src/workspaces/studio/presentation/components/ConnectionsPageFrame.tsx` |
| `??` | `src/workspaces/studio/presentation/components/SellerDocumentRequirements.tsx` |
| `??` | `src/workspaces/studio/presentation/hooks/useConnectionsOverview.ts` |
| `??` | `src/workspaces/studio/presentation/hooks/usePipelinesStatusesController.ts` |
| `??` | `src/workspaces/studio/presentation/views/ApiAccessView.tsx` |
| `??` | `src/workspaces/studio/presentation/views/AppsServicesView.tsx` |
| `??` | `src/workspaces/studio/presentation/views/ConnectionsOverviewView.tsx` |
| `??` | `src/workspaces/studio/presentation/views/InboundDataView.tsx` |
| `??` | `tests/contract/access-notice-lifetime.test.mts` |
| `??` | `tests/contract/currency-authority-audit.mts` |
| `??` | `tests/contract/customer-cursor-pagination.test.mts` |
| `??` | `tests/contract/customer-related-deals.test.mts` |
| `??` | `tests/contract/dashboard-connected.test.mts` |
| `??` | `tests/contract/deal-workspace-pipeline-selection.test.mts` |
| `??` | `tests/contract/deal-workspace.test.mts` |
| `??` | `tests/contract/studio-connections.test.mts` |
| `??` | `tests/contract/studio-first-visit.test.mts` |
| `??` | `tests/contract/workspace-currency-context.test.mts` |
| `??` | `tests/e2e/access-notices.acceptance.ts` |
| `??` | `tests/e2e/ai-ui.acceptance.ts` |
| `??` | `tests/e2e/ai-workspace-connected.spec.ts` |
| `??` | `tests/e2e/customer-ui.acceptance.ts` |
| `??` | `tests/e2e/dashboard-connected.spec.ts` |
| `??` | `tests/e2e/deal-pipeline-connected.spec.ts` |
| `??` | `tests/e2e/deal-pipeline-final-review.spec.ts` |
| `??` | `tests/e2e/deal-presentation.spec.ts` |
| `??` | `tests/e2e/deal-workspace.acceptance.ts` |
| `??` | `tests/e2e/internal-notices.acceptance.ts` |
| `??` | `tests/e2e/multicurrency-connected.spec.ts` |
| `??` | `tests/e2e/quote-acceptance-connected.spec.ts` |
| `??` | `tests/e2e/quote-approval-connected.spec.ts` |
| `??` | `tests/e2e/quote-core-connected.spec.ts` |
| `??` | `tests/e2e/quote-customer-response-connected.spec.ts` |
| `??` | `tests/e2e/quote-delivery-connected.spec.ts` |
| `??` | `tests/e2e/quote-documents-connected.spec.ts` |
| `??` | `tests/e2e/quote-identity-connected.spec.ts` |
| `??` | `tests/e2e/quote-independent-review-connected.spec.ts` |
| `??` | `tests/e2e/quote-lifecycle-connected.spec.ts` |
| `??` | `tests/e2e/quote-list-pipeline-connected.spec.ts` |
| `??` | `tests/e2e/quote-manual-source-connected.spec.ts` |
| `??` | `tests/e2e/quote-proof-connected.spec.ts` |
| `??` | `tests/e2e/quote-scanner-connected.spec.ts` |
| `??` | `tests/e2e/realistic-demo-connected.spec.ts` |
| `??` | `tests/e2e/remaining-access.acceptance.ts` |
| `??` | `tests/e2e/studio-connections.spec.ts` |
| `??` | `tests/e2e/studio-first-visit.spec.ts` |
| `??` | `tests/e2e/ui-cleanup.acceptance.ts` |
| `??` | `tests/fixtures/accessGovernanceFixture.mts` |
| `??` | `tests/fixtures/deal-workspace.fixture.tsx` |
| `??` | `tests/fixtures/deal-workspace.html` |
| `??` | `tests/quality/integration/check-deal-pipeline-authority.mts` |
| `??` | `tests/quality/unit/quoteListPagingChecks.ts` |
| `??` | `tests/tooling/openapi-primary-success.test.mjs` |

### control

| Git status | Path |
|---|---|
| ` M` | `PACKAGE_MANIFEST.json` |
| ` M` | `manifests/dependency-allowlist.json` |
| ` M` | `manifests/module-map.json` |
| ` M` | `manifests/operation-integrity.json` |
| ` M` | `manifests/operations/by-id/acceptQuote.json` |
| ` M` | `manifests/operations/by-id/acceptQuoteAndCloseDeal.json` |
| ` M` | `manifests/operations/by-id/acceptWorkspaceInvitation.json` |
| ` M` | `manifests/operations/by-id/activatePaymentPlan.json` |
| ` M` | `manifests/operations/by-id/addSupportCaseInternalNote.json` |
| ` M` | `manifests/operations/by-id/addSupportCaseReply.json` |
| ` M` | `manifests/operations/by-id/advanceLeadWorkState.json` |
| ` M` | `manifests/operations/by-id/advanceLeadWorkStateBatch.json` |
| ` M` | `manifests/operations/by-id/allocateCustomerCredit.json` |
| ` M` | `manifests/operations/by-id/allocatePayment.json` |
| ` M` | `manifests/operations/by-id/allocatePaymentSource.json` |
| ` M` | `manifests/operations/by-id/anonymizeLead.json` |
| ` M` | `manifests/operations/by-id/applyLeadTagBatch.json` |
| ` M` | `manifests/operations/by-id/approveOrderCreditApproval.json` |
| ` M` | `manifests/operations/by-id/approveQuoteCommand.json` |
| ` M` | `manifests/operations/by-id/approveReturnCommand.json` |
| ` M` | `manifests/operations/by-id/archiveAccessRole.json` |
| ` M` | `manifests/operations/by-id/archiveDealCommand.json` |
| ` M` | `manifests/operations/by-id/archiveDealsBatch.json` |
| ` M` | `manifests/operations/by-id/archiveLead.json` |
| ` M` | `manifests/operations/by-id/archiveLeadBatch.json` |
| ` M` | `manifests/operations/by-id/archiveOrderCommand.json` |
| ` M` | `manifests/operations/by-id/archiveOrdersBatch.json` |
| ` M` | `manifests/operations/by-id/archiveProduct.json` |
| ` M` | `manifests/operations/by-id/archiveProductsBatch.json` |
| ` M` | `manifests/operations/by-id/archiveQuoteCommand.json` |
| ` M` | `manifests/operations/by-id/archiveQuotesBatch.json` |
| ` M` | `manifests/operations/by-id/archiveTask.json` |
| ` M` | `manifests/operations/by-id/assignDealOwner.json` |
| ` M` | `manifests/operations/by-id/assignLeadOwner.json` |
| ` M` | `manifests/operations/by-id/assignLeadOwnerBatch.json` |
| ` M` | `manifests/operations/by-id/assignSupportCase.json` |
| ` M` | `manifests/operations/by-id/assignTask.json` |
| ` M` | `manifests/operations/by-id/awaitReturnItemCommand.json` |
| ` M` | `manifests/operations/by-id/beginReturnCarrierPickup.json` |
| ` M` | `manifests/operations/by-id/beginReturnReplacementResolution.json` |
| ` M` | `manifests/operations/by-id/cancelOrder.json` |
| ` M` | `manifests/operations/by-id/cancelPaymentIntent.json` |
| ` M` | `manifests/operations/by-id/cancelPaymentPlan.json` |
| ` M` | `manifests/operations/by-id/cancelShippingBookingCommand.json` |
| ` M` | `manifests/operations/by-id/cancelTask.json` |
| ` M` | `manifests/operations/by-id/changeDealStageCommand.json` |
| ` M` | `manifests/operations/by-id/changeQuoteStatusCommand.json` |
| ` M` | `manifests/operations/by-id/changeShippingProviderCommand.json` |
| ` M` | `manifests/operations/by-id/changeWorkspaceMemberStatus.json` |
| ` M` | `manifests/operations/by-id/claimLeadFromQueue.json` |
| ` M` | `manifests/operations/by-id/closeReturnCommand.json` |
| ` M` | `manifests/operations/by-id/completeCustomerOnboarding.json` |
| ` M` | `manifests/operations/by-id/completeOrderFromFulfillmentEvidence.json` |
| ` M` | `manifests/operations/by-id/completeReturnRepairResolution.json` |
| ` M` | `manifests/operations/by-id/completeReturnReplacementFromDelivery.json` |
| ` M` | `manifests/operations/by-id/completeReturnResolutionCommand.json` |
| ` M` | `manifests/operations/by-id/completeStudioQuickSetupStep.json` |
| ` M` | `manifests/operations/by-id/completeTask.json` |
| ` M` | `manifests/operations/by-id/configureReturnMethodCommand.json` |
| ` M` | `manifests/operations/by-id/confirmLeadDuplicatesDistinct.json` |
| ` M` | `manifests/operations/by-id/confirmOrderWithPaymentPlan.json` |
| ` M` | `manifests/operations/by-id/convertAcceptedQuoteToOrderDraft.json` |
| ` M` | `manifests/operations/by-id/createAccessRole.json` |
| ` M` | `manifests/operations/by-id/createContact.json` |
| ` M` | `manifests/operations/by-id/createCrmObjectField.json` |
| ` M` | `manifests/operations/by-id/createCrmPipeline.json` |
| ` M` | `manifests/operations/by-id/createCrmPipelineStage.json` |
| ` M` | `manifests/operations/by-id/createDealCommand.json` |
| ` M` | `manifests/operations/by-id/createIntegrationConnection.json` |
| ` M` | `manifests/operations/by-id/createInvoiceCreditNote.json` |
| ` M` | `manifests/operations/by-id/createInvoiceDraft.json` |
| ` M` | `manifests/operations/by-id/createLead.json` |
| ` M` | `manifests/operations/by-id/createOrderDraftCommand.json` |
| ` M` | `manifests/operations/by-id/createOrderOutboundShippingBooking.json` |
| ` M` | `manifests/operations/by-id/createOrganization.json` |
| ` M` | `manifests/operations/by-id/createPaymentIntent.json` |
| ` M` | `manifests/operations/by-id/createProduct.json` |
| ` M` | `manifests/operations/by-id/createProductConfigurationType.json` |
| ` M` | `manifests/operations/by-id/createQuoteCommand.json` |
| ` M` | `manifests/operations/by-id/createQuoteForDeal.json` |
| ` M` | `manifests/operations/by-id/createRefundIntent.json` |
| ` M` | `manifests/operations/by-id/createReturnRequestCommand.json` |
| ` M` | `manifests/operations/by-id/createShippingBookingCommand.json` |
| ` M` | `manifests/operations/by-id/createSupportCase.json` |
| ` M` | `manifests/operations/by-id/createTask.json` |
| ` M` | `manifests/operations/by-id/deleteCrmObjectField.json` |
| ` M` | `manifests/operations/by-id/deleteCrmPipeline.json` |
| ` M` | `manifests/operations/by-id/deleteCrmPipelineStage.json` |
| ` M` | `manifests/operations/by-id/deleteProductConfigurationType.json` |
| ` M` | `manifests/operations/by-id/discardInvoiceDraft.json` |
| ` M` | `manifests/operations/by-id/disconnectIntegrationConnection.json` |
| ` M` | `manifests/operations/by-id/dismissStudioQuickSetupAutoOpen.json` |
| ` M` | `manifests/operations/by-id/disqualifyLead.json` |
| ` M` | `manifests/operations/by-id/disqualifyLeadBatch.json` |
| ` M` | `manifests/operations/by-id/duplicateOrderDraft.json` |
| ` M` | `manifests/operations/by-id/evaluateEffectiveRecordAccess.json` |
| ` M` | `manifests/operations/by-id/expireQuoteCommand.json` |
| ` M` | `manifests/operations/by-id/expireQuotesBatch.json` |
| ` M` | `manifests/operations/by-id/getBuyerAccountStatement.json` |
| ` M` | `manifests/operations/by-id/getContact.json` |
| ` M` | `manifests/operations/by-id/getContactRelationshipSummary.json` |
| ` M` | `manifests/operations/by-id/getCrmObjectSchema.json` |
| ` M` | `manifests/operations/by-id/getCurrentAuthorizationContext.json` |
| ` M` | `manifests/operations/by-id/getCurrentSession.json` |
| ` M` | `manifests/operations/by-id/getCustomer.json` |
| ` M` | `manifests/operations/by-id/getCustomer360.json` |
| ` M` | `manifests/operations/by-id/getDeal.json` |
| ` M` | `manifests/operations/by-id/getDealForecastSummary.json` |
| ` M` | `manifests/operations/by-id/getInvoice.json` |
| ` M` | `manifests/operations/by-id/getInvoiceIssueReadiness.json` |
| ` M` | `manifests/operations/by-id/getInvoiceSellerInformationConfiguration.json` |
| ` M` | `manifests/operations/by-id/getLead.json` |
| ` M` | `manifests/operations/by-id/getOrder.json` |
| ` M` | `manifests/operations/by-id/getOrderCreditApproval.json` |
| ` M` | `manifests/operations/by-id/getOrderFulfillmentEligibility.json` |
| ` M` | `manifests/operations/by-id/getOrderInvoiceEligibility.json` |
| ` M` | `manifests/operations/by-id/getOrderPaymentProjection.json` |
| ` M` | `manifests/operations/by-id/getOrganization.json` |
| ` M` | `manifests/operations/by-id/getOrganizationOverview.json` |
| ` M` | `manifests/operations/by-id/getPaymentIntent.json` |
| ` M` | `manifests/operations/by-id/getPaymentIntentStatus.json` |
| ` M` | `manifests/operations/by-id/getPaymentMethodCatalog.json` |
| ` M` | `manifests/operations/by-id/getPaymentRecordDetail.json` |
| ` M` | `manifests/operations/by-id/getProduct.json` |
| ` M` | `manifests/operations/by-id/getProductAvailability.json` |
| ` M` | `manifests/operations/by-id/getProductPriceProjection.json` |
| ` M` | `manifests/operations/by-id/getQuote.json` |
| ` M` | `manifests/operations/by-id/getReceivablesAging.json` |
| ` M` | `manifests/operations/by-id/getReceivablesSummary.json` |
| ` M` | `manifests/operations/by-id/getRefund.json` |
| ` M` | `manifests/operations/by-id/getReturn.json` |
| ` M` | `manifests/operations/by-id/getReturnCreditRefundResolution.json` |
| ` M` | `manifests/operations/by-id/getShippingBooking.json` |
| ` M` | `manifests/operations/by-id/getStudioQuickSetup.json` |
| ` M` | `manifests/operations/by-id/getSupportCase.json` |
| ` M` | `manifests/operations/by-id/getTask.json` |
| ` M` | `manifests/operations/by-id/getWorkspaceAccessDirectory.json` |
| ` M` | `manifests/operations/by-id/getWorkspaceBootstrap.json` |
| ` M` | `manifests/operations/by-id/getWorkspaceConfiguration.json` |
| ` M` | `manifests/operations/by-id/getWorkspaceCurrencies.json` |
| ` M` | `manifests/operations/by-id/handoverLeadWithTasks.json` |
| ` M` | `manifests/operations/by-id/importLeadBatch.json` |
| ` M` | `manifests/operations/by-id/inviteWorkspaceMember.json` |
| ` M` | `manifests/operations/by-id/issueInvoice.json` |
| ` D` | `manifests/operations/by-id/linkContactToOrganization.json` |
| ` M` | `manifests/operations/by-id/listActivities.json` |
| ` M` | `manifests/operations/by-id/listContacts.json` |
| ` M` | `manifests/operations/by-id/listCreditNotes.json` |
| ` M` | `manifests/operations/by-id/listCrmObjectSchemas.json` |
| ` M` | `manifests/operations/by-id/listCrmPipelines.json` |
| ` M` | `manifests/operations/by-id/listCustomerCredits.json` |
| ` M` | `manifests/operations/by-id/listCustomers.json` |
| ` M` | `manifests/operations/by-id/listDeals.json` |
| ` M` | `manifests/operations/by-id/listIntegrationConnections.json` |
| ` M` | `manifests/operations/by-id/listIntegrationProviders.json` |
| ` M` | `manifests/operations/by-id/listInvoiceDeliveries.json` |
| ` M` | `manifests/operations/by-id/listInvoices.json` |
| ` M` | `manifests/operations/by-id/listLeads.json` |
| ` M` | `manifests/operations/by-id/listMyWorkspaces.json` |
| ` M` | `manifests/operations/by-id/listOrders.json` |
| ` M` | `manifests/operations/by-id/listOrganizations.json` |
| ` M` | `manifests/operations/by-id/listPaymentAllocations.json` |
| ` M` | `manifests/operations/by-id/listPaymentIntents.json` |
| ` M` | `manifests/operations/by-id/listPaymentPlans.json` |
| ` M` | `manifests/operations/by-id/listPaymentReceivingAccounts.json` |
| ` M` | `manifests/operations/by-id/listPaymentRecords.json` |
| ` M` | `manifests/operations/by-id/listPaymentScheduleLines.json` |
| ` M` | `manifests/operations/by-id/listProductConfigurationTypes.json` |
| ` M` | `manifests/operations/by-id/listProducts.json` |
| ` M` | `manifests/operations/by-id/listQuotes.json` |
| ` M` | `manifests/operations/by-id/listReceivables.json` |
| ` M` | `manifests/operations/by-id/listRefundProviderAttempts.json` |
| ` M` | `manifests/operations/by-id/listRefunds.json` |
| ` M` | `manifests/operations/by-id/listReturns.json` |
| ` M` | `manifests/operations/by-id/listShippingBookings.json` |
| ` M` | `manifests/operations/by-id/listShippingPickupLocations.json` |
| ` M` | `manifests/operations/by-id/listShippingProviders.json` |
| ` M` | `manifests/operations/by-id/listShippingReturnLocations.json` |
| ` M` | `manifests/operations/by-id/listSupportCases.json` |
| ` M` | `manifests/operations/by-id/listTasks.json` |
| ` M` | `manifests/operations/by-id/listWorkspaceConfigurationAudit.json` |
| ` M` | `manifests/operations/by-id/listWorkspaceExchangeRates.json` |
| ` M` | `manifests/operations/by-id/logActivity.json` |
| ` M` | `manifests/operations/by-id/markDealLostAndPlanRecycle.json` |
| ` M` | `manifests/operations/by-id/markDealLostCommand.json` |
| ` M` | `manifests/operations/by-id/markDealWonCommand.json` |
| ` M` | `manifests/operations/by-id/mergeLeadDuplicates.json` |
| ` M` | `manifests/operations/by-id/onboardExistingCustomer.json` |
| ` M` | `manifests/operations/by-id/openStudioQuickSetup.json` |
| ` M` | `manifests/operations/by-id/previewPaymentPlan.json` |
| ` M` | `manifests/operations/by-id/provisionWorkspaceMember.json` |
| ` M` | `manifests/operations/by-id/publishWorkspaceConfiguration.json` |
| ` M` | `manifests/operations/by-id/putWorkspaceExchangeRate.json` |
| ` M` | `manifests/operations/by-id/qualifyLead.json` |
| ` M` | `manifests/operations/by-id/qualifyLeadForDirectSale.json` |
| ` M` | `manifests/operations/by-id/qualifyLeadForNurture.json` |
| ` M` | `manifests/operations/by-id/qualifyLeadForOpportunity.json` |
| ` M` | `manifests/operations/by-id/receiveReturnItemsCommand.json` |
| ` M` | `manifests/operations/by-id/reconcilePaymentRecord.json` |
| ` M` | `manifests/operations/by-id/recordCodCustomerCollection.json` |
| ` M` | `manifests/operations/by-id/recordCodMerchantRemittance.json` |
| ` M` | `manifests/operations/by-id/recordLeadConsent.json` |
| ` M` | `manifests/operations/by-id/recordManualPayment.json` |
| ` M` | `manifests/operations/by-id/recordOrderSendEvidenceCommand.json` |
| ` M` | `manifests/operations/by-id/recordPaymentRequestDelivery.json` |
| ` M` | `manifests/operations/by-id/recordQuoteSendEvidenceCommand.json` |
| ` M` | `manifests/operations/by-id/refreshSession.json` |
| ` M` | `manifests/operations/by-id/registerAccount.json` |
| ` M` | `manifests/operations/by-id/rejectOrderCreditApproval.json` |
| ` M` | `manifests/operations/by-id/rejectQuoteCommand.json` |
| ` M` | `manifests/operations/by-id/rejectReturnCommand.json` |
| ` M` | `manifests/operations/by-id/reopenDisqualifiedLead.json` |
| ` M` | `manifests/operations/by-id/replaceAccessRole.json` |
| ` M` | `manifests/operations/by-id/replaceInvoiceSellerInformationConfiguration.json` |
| ` M` | `manifests/operations/by-id/replaceLeadProfile.json` |
| ` M` | `manifests/operations/by-id/replacePaymentReceivingAccounts.json` |
| ` M` | `manifests/operations/by-id/replaceProduct.json` |
| ` M` | `manifests/operations/by-id/replaceSupportCaseProfile.json` |
| ` M` | `manifests/operations/by-id/replaceWorkspaceCurrencies.json` |
| ` M` | `manifests/operations/by-id/replaceWorkspaceMemberAccess.json` |
| ` M` | `manifests/operations/by-id/repriceOrderDraft.json` |
| ` M` | `manifests/operations/by-id/repriceQuoteDraft.json` |
| ` M` | `manifests/operations/by-id/requestLeadExport.json` |
| ` M` | `manifests/operations/by-id/requestOrderCreditApproval.json` |
| ` M` | `manifests/operations/by-id/requestPasswordReset.json` |
| ` M` | `manifests/operations/by-id/requestQuoteApprovalBatch.json` |
| ` M` | `manifests/operations/by-id/requestQuoteApprovalChangesCommand.json` |
| ` M` | `manifests/operations/by-id/requestQuoteApprovalCommand.json` |
| ` M` | `manifests/operations/by-id/requestRefundCancellation.json` |
| ` M` | `manifests/operations/by-id/rescheduleTask.json` |
| ` M` | `manifests/operations/by-id/resendWorkspaceInvitation.json` |
| ` M` | `manifests/operations/by-id/resetPassword.json` |
| ` M` | `manifests/operations/by-id/resolveReturnCreditRefund.json` |
| ` M` | `manifests/operations/by-id/restoreProduct.json` |
| ` M` | `manifests/operations/by-id/restoreProductsBatch.json` |
| ` M` | `manifests/operations/by-id/retryInvoiceIssue.json` |
| ` M` | `manifests/operations/by-id/retryPaymentIntent.json` |
| ` M` | `manifests/operations/by-id/retryRefundIntent.json` |
| ` M` | `manifests/operations/by-id/retryShippingBookingCommand.json` |
| ` M` | `manifests/operations/by-id/reversePaymentAllocation.json` |
| ` M` | `manifests/operations/by-id/reviseQuoteCommand.json` |
| ` M` | `manifests/operations/by-id/revokeOrderCreditApproval.json` |
| ` M` | `manifests/operations/by-id/revokeWorkspaceInvitation.json` |
| ` M` | `manifests/operations/by-id/rotateManagedMemberPassword.json` |
| ` M` | `manifests/operations/by-id/saveInvoiceDraft.json` |
| ` M` | `manifests/operations/by-id/savePaymentPlanDraft.json` |
| ` M` | `manifests/operations/by-id/scheduleLeadFollowUpBatch.json` |
| ` M` | `manifests/operations/by-id/sendInvoice.json` |
| ` M` | `manifests/operations/by-id/signIn.json` |
| ` M` | `manifests/operations/by-id/signOut.json` |
| ` M` | `manifests/operations/by-id/skipStudioQuickSetupStep.json` |
| ` M` | `manifests/operations/by-id/syncShippingBookingCommand.json` |
| ` M` | `manifests/operations/by-id/transitionSupportCase.json` |
| ` M` | `manifests/operations/by-id/updateContact.json` |
| ` M` | `manifests/operations/by-id/updateCrmObjectField.json` |
| ` M` | `manifests/operations/by-id/updateCrmPipeline.json` |
| ` M` | `manifests/operations/by-id/updateCrmPipelineStage.json` |
| ` D` | `manifests/operations/by-id/updateCustomerLifecycle.json` |
| ` M` | `manifests/operations/by-id/updateDealCommand.json` |
| ` M` | `manifests/operations/by-id/updateDealForecast.json` |
| ` M` | `manifests/operations/by-id/updateDealNextAction.json` |
| ` M` | `manifests/operations/by-id/updateIntegrationConnection.json` |
| ` M` | `manifests/operations/by-id/updateOrderDraftCommand.json` |
| ` M` | `manifests/operations/by-id/updateOrganization.json` |
| ` M` | `manifests/operations/by-id/updateProductConfigurationType.json` |
| ` M` | `manifests/operations/by-id/updateQuoteDraftCommand.json` |
| ` M` | `manifests/operations/by-id/updateWorkspaceBlueprint.json` |
| ` M` | `manifests/operations/by-id/updateWorkspaceBusinessInformation.json` |
| ` M` | `manifests/operations/by-id/updateWorkspaceFeatures.json` |
| ` M` | `manifests/operations/by-id/updateWorkspaceLocaleRegion.json` |
| ` M` | `manifests/operations/by-id/verifyEmail.json` |
| ` M` | `manifests/operations/by-id/verifyIntegrationConnection.json` |
| ` M` | `manifests/operations/by-id/verifyMfa.json` |
| ` M` | `manifests/operations/by-id/voidInvoice.json` |
| ` M` | `manifests/operations/index.json` |
| ` M` | `manifests/persistence-ownership.json` |
| ` M` | `schemas/task-packet.schema.json` |
| ` M` | `schemas/task-state.schema.json` |
| ` M` | `tools/_authority.py` |
| ` M` | `tools/_gate_lib.py` |
| ` M` | `tools/_runtime_lib.py` |
| ` M` | `tools/create_task_skeleton.py` |
| ` M` | `tools/freeze_task.py` |
| ` M` | `tools/run_gate.py` |
| ` M` | `tools/run_required_gates.py` |
| ` M` | `tools/transition_task.py` |
| ` M` | `tools/validate_evidence.py` |
| ` M` | `tools/validate_pack.py` |
| ` M` | `tools/validate_reviews.py` |
| ` M` | `tools/validate_task.py` |
| ` M` | `tools/verify_repo_integration.py` |
| `??` | `backend-work/state/UCRM-ARCHITECTURE-CHECKER-CORRECTIONS-V2-20261020-ROOT.json` |
| `??` | `backend-work/state/UCRM-ARCHITECTURE-DISCOVERY-FAIL-CLOSED-FIX.json` |
| `??` | `backend-work/tasks/UCRM-ARCHITECTURE-CHECKER-CORRECTIONS-V2-20261020-ROOT.json` |
| `??` | `backend-work/tasks/UCRM-ARCHITECTURE-DISCOVERY-FAIL-CLOSED-FIX.json` |
| `??` | `governance/changes/OPPORTUNITY_CORE_REGISTRY_PREREQUISITES_20261013/architecture-record.json` |
| `??` | `governance/changes/OPPORTUNITY_CORE_REGISTRY_PREREQUISITES_20261013/proposal.json` |
| `??` | `governance/changes/OPPORTUNITY_CORE_REGISTRY_PREREQUISITES_20261013/ratification.json` |
| `??` | `governance/changes/OPPORTUNITY_CORE_REGISTRY_PREREQUISITES_20261013/writer-authorization.json` |
| `??` | `governance/changes/PRODUCT_CATALOG_SEARCH_20261014/architecture-record.json` |
| `??` | `governance/changes/PRODUCT_CATALOG_SEARCH_20261014/ratification.json` |
| `??` | `governance/changes/PRODUCT_CATALOG_SEARCH_20261014/writer-admission.json` |
| `??` | `governance/changes/PRODUCT_CATALOG_SEARCH_20261014/writer-authorization.json` |
| `??` | `governance/changes/PRODUCT_CATALOG_SEARCH_20261014_API_GATE/architecture-record.json` |
| `??` | `governance/changes/PRODUCT_CATALOG_SEARCH_20261014_API_GATE/ratification.json` |
| `??` | `governance/changes/PRODUCT_CATALOG_SEARCH_20261014_API_GATE/writer-authorization.json` |
| `??` | `governance/changes/PRODUCT_CATALOG_SEARCH_20261014_PACKAGE_PIN/architecture-record.json` |
| `??` | `governance/changes/PRODUCT_CATALOG_SEARCH_20261014_PACKAGE_PIN/ratification.json` |
| `??` | `governance/changes/PRODUCT_CATALOG_SEARCH_20261014_PACKAGE_PIN/writer-authorization.json` |
| `??` | `governance/changes/PRODUCT_CATALOG_SEARCH_20261014_QUERY_VERSION/architecture-record.json` |
| `??` | `governance/changes/PRODUCT_CATALOG_SEARCH_20261014_QUERY_VERSION/ratification.json` |
| `??` | `governance/changes/PRODUCT_CATALOG_SEARCH_20261014_QUERY_VERSION/writer-authorization.json` |
| `??` | `governance/changes/WF10_LEAD_OPPORTUNITY_20261014/architecture-record.json` |
| `??` | `governance/changes/WF10_LEAD_OPPORTUNITY_20261014/ratification.json` |
| `??` | `governance/changes/WF10_LEAD_OPPORTUNITY_20261014/writer-admission.json` |
| `??` | `governance/changes/WF21_DEAL_FOLLOW_UP_20261014/architect-approval.json` |
| `??` | `governance/changes/WF21_DEAL_FOLLOW_UP_20261014/ratification.json` |
| `??` | `governance/changes/WF21_DEAL_FOLLOW_UP_20261014/writer-admission.json` |
| `??` | `governance/owner-baseline-replacement.json` |
| `??` | `governance/policies/solo-development-v2.json` |
| `??` | `manifests/governance-baseline.json` |
| `??` | `manifests/operation-admission.json` |
| `??` | `manifests/operations/by-id/approveQuoteAcceptanceEvidenceSource.json` |
| `??` | `manifests/operations/by-id/archiveContact.json` |
| `??` | `manifests/operations/by-id/archiveCustomer.json` |
| `??` | `manifests/operations/by-id/archiveOrganization.json` |
| `??` | `manifests/operations/by-id/assessQuoteIdentityAuthority.json` |
| `??` | `manifests/operations/by-id/convertLeadToCustomer.json` |
| `??` | `manifests/operations/by-id/createContactCustomerRelationship.json` |
| `??` | `manifests/operations/by-id/createContactOrganizationRelationship.json` |
| `??` | `manifests/operations/by-id/createCustomer.json` |
| `??` | `manifests/operations/by-id/createQuoteAcceptanceCase.json` |
| `??` | `manifests/operations/by-id/downloadQuoteAcceptanceProofContent.json` |
| `??` | `manifests/operations/by-id/downloadQuoteDocument.json` |
| `??` | `manifests/operations/by-id/endContactCustomerRelationship.json` |
| `??` | `manifests/operations/by-id/endContactOrganizationRelationship.json` |
| `??` | `manifests/operations/by-id/evaluateQuoteCustomerAcceptance.json` |
| `??` | `manifests/operations/by-id/getQuoteAcceptanceCase.json` |
| `??` | `manifests/operations/by-id/getQuoteAcceptanceProof.json` |
| `??` | `manifests/operations/by-id/getQuoteAcceptanceProofScanHistory.json` |
| `??` | `manifests/operations/by-id/getQuoteAcceptanceProofSecurity.json` |
| `??` | `manifests/operations/by-id/getQuoteCustomerAcceptanceReadiness.json` |
| `??` | `manifests/operations/by-id/getQuoteDocument.json` |
| `??` | `manifests/operations/by-id/getQuoteDocumentSeller.json` |
| `??` | `manifests/operations/by-id/getQuoteIdentityAuthority.json` |
| `??` | `manifests/operations/by-id/getQuoteIndependentReview.json` |
| `??` | `manifests/operations/by-id/issueQuoteDocumentCommand.json` |
| `??` | `manifests/operations/by-id/listQuoteAcceptanceCases.json` |
| `??` | `manifests/operations/by-id/listQuoteAcceptanceEvidenceSourceHistory.json` |
| `??` | `manifests/operations/by-id/listQuoteAcceptanceEvidenceSources.json` |
| `??` | `manifests/operations/by-id/listQuoteAcceptanceProofs.json` |
| `??` | `manifests/operations/by-id/listQuoteDocuments.json` |
| `??` | `manifests/operations/by-id/listQuoteIdentityAuthorityHistory.json` |
| `??` | `manifests/operations/by-id/listQuoteIndependentReviewHistory.json` |
| `??` | `manifests/operations/by-id/recordQuoteAcceptanceSourceCheck.json` |
| `??` | `manifests/operations/by-id/recordQuoteCustomerDecisionCommand.json` |
| `??` | `manifests/operations/by-id/registerQuoteAcceptanceEvidenceSource.json` |
| `??` | `manifests/operations/by-id/reviseQuoteAcceptanceEvidenceSource.json` |
| `??` | `manifests/operations/by-id/revokeQuoteAcceptanceEvidenceSource.json` |
| `??` | `manifests/operations/by-id/saveQuoteIdentityAuthorityBinding.json` |
| `??` | `manifests/operations/by-id/searchProductSelections.json` |
| `??` | `manifests/operations/by-id/submitQuoteIndependentReviewDecision.json` |
| `??` | `manifests/operations/by-id/updateContactCustomerRelationship.json` |
| `??` | `manifests/operations/by-id/updateContactOrganizationRelationship.json` |
| `??` | `manifests/operations/by-id/updateCustomer.json` |
| `??` | `manifests/operations/by-id/uploadQuoteAcceptanceProof.json` |
| `??` | `tools/_bounded_operation_authority.py` |
| `??` | `tools/solo_governance_conformance_test.py` |
| `??` | `tools/test_bounded_operation_authority.py` |
| `??` | `tools/verify_repo_integration_test.py` |

