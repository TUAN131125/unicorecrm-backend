# Exchange Rate Authority — database integrity proposal

Review revision 22/10/2026. **DESIGN ONLY — NOT EXECUTED.** Các fragments dưới đây là specification để review, không phải migration hoặc SQL đã chạy. Hai bảng đã được chấp thuận về hướng kiến trúc; constraints, triggers và EF mapping chi tiết vẫn là đề xuất. Không có SQL acceptance PASS trước khi thực hiện test matrix trên SQL Server thật sau khi được phép implementation.

## 1. Sửa thiếu sót trong đề xuất ban đầu

Unique `(WorkspaceId, FromCurrency, ToCurrency, EffectiveAt, Version)` **đứng riêng không đủ**: một writer lỗi có thể tạo RateId B bắt đầu ở version 2 hoặc đổi pair ở version mới. App lock chỉ bảo vệ writer tuân thủ nó. Bản bổ sung dùng một root row version 1, filtered unique index trên roots, self-FK bắt buộc về root và FK tới predecessor. Không thêm bảng RateFamilies thứ ba.

SQL Server hỗ trợ self-referencing FK tới PK/UNIQUE; các cột FK root đều NOT NULL để không bị bỏ qua kiểm tra. [Microsoft: foreign-key relationships](https://learn.microsoft.com/en-us/sql/relational-databases/tables/create-foreign-key-relationships?view=sql-server-ver16). Unique filtered index áp dụng uniqueness cho tập root rows. [Microsoft: filtered indexes](https://learn.microsoft.com/en-us/sql/relational-databases/indexes/create-filtered-indexes?view=sql-server-ver16).

## 2. Cột và constraints

### ExchangeRateBooks

- PK `WorkspaceId nvarchar(128)`; FK WorkspaceDefinition, NO ACTION khi delete.
- `Revision bigint NOT NULL DEFAULT 0`, CHECK >= 0.
- `AuthorityState varchar(24) NOT NULL`: REVIEW_REQUIRED hoặc ACTIVE.
- `ActivatedAt datetimeoffset(7)` nullable khi REVIEW_REQUIRED; ACTIVE bắt buộc có timestamp UTC. Empty ACTIVE book revision 0 hợp lệ, không đồng nghĩa đã có tỷ giá.
- `LegacyRatesSha256 char(64)` và `LegacyRatesArchiveJson nvarchar(max)` nullable: immutable raw legacy-rate archive tại cutover, không phải current rate authority. Hash SHA-256 của UTF-8 text không BOM/normalization, không nhầm với HASHBYTES trên nvarchar UTF-16. Archive chỉ do reviewed cutover điền một lần, runtime không có quyền sửa/xóa các cột này; new empty book không có legacy có thể giữ null. Các header fields này không được serialize vào CRM.

### ExchangeRateVersions

Mọi field sau NOT NULL trừ những chỗ ghi rõ: WorkspaceId; RateId nvarchar(128); Version bigint; RootVersion bigint DEFAULT 1; PreviousVersion bigint nullable; FromCurrency/ToCurrency char(3); EffectiveAt/ValidUntil datetimeoffset(7); Action SET/WITHDRAW; RateText varchar(80) nullable cho WITHDRAW; SourceKind=MANUAL; SourceReference nvarchar(240); RecordedAt datetimeoffset(7); RecordedByMemberId; Reason nvarchar(1000); BookRevision bigint; CommandId; CorrelationId. Các identifier dùng độ dài hiện hữu <=128. RootVersion là cột thực có CHECK, không dùng computed column làm FK.

Thiết kế keys/FK cụ thể (các câu lệnh minh họa giả định bảng/cột đã được khai báo đúng type/collation):

```sql
-- SPECIFICATION ONLY. Không chạy file/tạo migration trong task thiết kế.
PRIMARY KEY (WorkspaceId, RateId, Version)

UNIQUE (WorkspaceId, RateId, Version, FromCurrency, ToCurrency, EffectiveAt)

CHECK (Version >= 1 AND RootVersion = 1)

CHECK (
  (Version = 1 AND PreviousVersion IS NULL AND Action = 'SET')
  OR
  (Version > 1 AND PreviousVersion IS NOT NULL AND PreviousVersion = Version - 1)
)

FOREIGN KEY (WorkspaceId) REFERENCES workspace.ExchangeRateBooks(WorkspaceId)

FOREIGN KEY (WorkspaceId, RateId, RootVersion, FromCurrency, ToCurrency, EffectiveAt)
  REFERENCES workspace.ExchangeRateVersions
    (WorkspaceId, RateId, Version, FromCurrency, ToCurrency, EffectiveAt)

FOREIGN KEY (WorkspaceId, RateId, PreviousVersion)
  REFERENCES workspace.ExchangeRateVersions(WorkspaceId, RateId, Version)
```

```sql
-- SPECIFICATION ONLY. FK root tham chiếu UNIQUE đầy đủ ở trên,
-- không tham chiếu filtered index.
CREATE UNIQUE INDEX UX_ExchangeRateVersions_RootSlot
ON workspace.ExchangeRateVersions
  (WorkspaceId, FromCurrency, ToCurrency, EffectiveAt)
WHERE Version = 1;
```

Các FK dùng NO ACTION, không cascade. Pair fields dùng binary collation nhất quán và CHECK đúng ba ký tự ASCII A–Z; From != To. Thời gian lưu UTC bằng CHECK DATEPART(TZOFFSET,...)=0 cho EffectiveAt, ValidUntil, RecordedAt; ValidUntil > EffectiveAt. Các cột identity/time/root không nullable. Không dùng `WITH NOCHECK`; inspect `is_disabled/is_not_trusted` trong acceptance.

Action/source CHECK: chỉ SET/WITHDRAW; SourceKind MANUAL; SET bắt buộc RateText không null, WITHDRAW bắt buộc RateText null; reason/source reference trimmed nonempty. Provider connection không nằm trong DTO MANUAL mới; nếu phải có compatibility field thì CHECK null. Không có trạng thái PENDING/EXPIRED được cập nhật lên lịch sử.

RateText validation SQL và application phải cùng grammar đã duyệt trong contract proposal: ASCII digits + tối đa một dấu chấm, không leading-zero integer ngoài `0`, ít nhất một digit khác zero, không exponent/sign/whitespace; tối đa 38 digits không tính dấu chấm, scale <=18; canonical stored value bỏ trailing fractional zero mà không round. CHECK có nhánh IS NOT NULL rõ với SET, tránh UNKNOWN do NULL. Không dùng TRY_CONVERT decimal(38,18) để chứng minh precision vì kiểu đó chỉ còn 20 integer digits và có thể round scale. SQL implementation có thể dùng LEN/CHARINDEX/REPLACE/binary LIKE; phải có parity tests với parser và schema. Các CHECK không được giả làm cross-row validation. [Microsoft: CHECK và NULL](https://learn.microsoft.com/en-us/sql/relational-databases/tables/unique-constraints-and-check-constraints?view=sql-server-ver16).

Index query: `(WorkspaceId,FromCurrency,ToCurrency,EffectiveAt DESC,BookRevision)` và history `(WorkspaceId,BookRevision,RateId,Version)`; không include trường quản trị dài nếu chưa cần. PK và UNIQUE phục vụ root FK gần trùng nhau là chi phí chủ động để đổi lấy identity constraint thuần SQL, tránh identity trigger phức tạp/bảng thứ ba. Cần xem execution plan và kích thước index khi implementation, không tuyên bố đã benchmark.

## 3. Chứng minh invariant về identity/version

Giả sử constraints đang enabled/trusted và các cột root đều NOT NULL:

1. Mọi row V phải có root `(WorkspaceId, RateId, Version=1)` với cùng pair/effectiveAt, do root FK + RootVersion=1.
2. Nếu có hai RateId khác nhau A/B cùng Workspace/pair/effectiveAt, cả hai bắt buộc có root v1 theo bước 1. Hai root vi phạm UX_RootSlot. Vậy không thể có hai RateId độc lập cùng slot, kể cả root A đã withdrawn: root không được xóa và uniqueness không lọc status.
3. RateId B không thể né bằng cách bắt đầu ở v2/v99: thiếu root v1 và predecessor FK. Cùng RateId không thể đổi pair/time ở v2: root FK không khớp.
4. PK chặn hai bản v2 cùng RateId; predecessor CHECK/FK buộc version n>1 có n-1. Quy nạp cho chuỗi liên tục từ v1. CHECK có `PreviousVersion IS NOT NULL` nên không lọt qua NULL/UNKNOWN.
5. Không thể sửa lại v1/v2 để thay lịch sử nếu UPDATE/DELETE guard mục 4 hoạt động. Không hard-delete root sau withdrawal rồi tạo family mới cùng slot.

Đây là chứng minh thiết kế theo constraints, **không phải SQL runtime proof đã PASS**. Cần negative SQL INSERT tests bỏ qua application lock để chứng minh migration/mapping thật hiện thực đúng specification.

## 4. Book revision và immutability ở mức SQL

Đề xuất bổ sung **hai trigger nhỏ trên bảng versions**, không framework mới:

1. `AdvanceExchangeRateBook`, AFTER INSERT: mỗi statement phải có ít nhất một row, đúng một Workspace, một CommandId và một BookRevision. Acquire book row với UPDLOCK/HOLDLOCK; require ACTIVE; require inserted.BookRevision = current Revision + 1; với correction require predecessor.BookRevision < inserted.BookRevision; advance book Revision đúng một lần. Dữ liệu trigger + row insert commit/rollback cùng transaction. Nếu empty inserted thì no-op, không tăng revision. Trigger cấp tính hợp lệ của sequence; client không được cấp BookRevision.
2. `ProtectExchangeRateHistory`, AFTER UPDATE/DELETE: reject mọi thay đổi row lịch sử. Runtime SQL principal không có UPDATE/DELETE/TRUNCATE/ALTER quyền trên versions, không được trực tiếp UPDATE cột Books.Revision/LegacyRatesArchiveJson/LegacyRatesSha256 hoặc DELETE book. Reviewed cutover dùng maintenance authority riêng để điền archive/state một lần; không qua public rate command. Migration principal tách khỏi runtime. Trigger cấp revision có module execution context tối thiểu để update đúng cột revision; quyền phải được review/test, không lấy login db_owner thử nghiệm làm bằng chứng least privilege.

Advance trigger là quy tắc SQL sau, không coi nó là CHECK: từ book R chỉ nhận đúng R+1, một insert statement là một command. Sau commit R+1, append bất kỳ row bổ sung vào R hoặc R+1 đều bị chặn vì lần tiếp theo phải là R+2. Vì vậy lookup fixed R không được thêm dữ liệu hồi tố vào chính R sau khi đã commit. Header revision không được tăng thủ công trước insert; trigger là writer duy nhất của cột revision. Version data, book sequence và snapshots không dựa vào timestamp đồng hồ để phân thứ tự.

Batch import là **một set-based INSERT statement** cho một Workspace/command; nhiều v1 đã kiểm chứng có cùng book revision. Không dùng EF AddRange để vô tình tạo nhiều statements cùng R. Không import nhiều version liên tiếp cùng family trong một R để bịa lịch sử đã mất. Dùng owner persistence path cụ thể (INSERT...SELECT từ payload được kiểm tra); size bound được review riêng. Các rate commands bình thường append một row.

Repo dùng EF Core SqlServer 10.0.11. EF mapping phải khai báo trigger và tắt OUTPUT clause không tương thích (`UseSqlOutputClause(false)`); đọc lại book revision sau insert trong transaction. Đây là dự kiến mapping, không package upgrade. [Microsoft: SQL Server provider, triggers và OUTPUT](https://learn.microsoft.com/en-us/ef/core/providers/sql-server/misc). Chi phí hai trigger bù cho việc không thêm bảng events/sequence thứ ba; phải có trigger regression tests và quyền runtime rõ.

Giới hạn assurance: không bảo vệ khỏi sysadmin/migration principal cố tình disable trigger/constraint hoặc sửa DB. Acceptance kiểm tra guard tồn tại/enabled/trusted và runtime quyền không thể bypass. Không có cam kết “append-only” nếu deploy runtime bằng principal có quyền DDL rồi coi đó là production proof.

## 5. Concurrent requests và transaction boundary

Thứ tự lock thống nhất cho admitted writer: trusted membership/capability → Serializable transaction → application lock `configuration:{WorkspaceId}` hiện hữu → replay/fingerprint → Studio global If-Match → book row lock → kiểm tra current rate version → một INSERT statement → trigger nâng book R → Studio envelope nâng global revision → audit/outbox/idempotency response → commit. Owner methods dùng chung connection/transaction; không đọc/ghi foreign DbContext. R mới do server đọc/chuẩn bị và SQL trigger xác nhận, không nhận từ public request.

| Race | Kết quả bắt buộc |
|---|---|
| Hai create cùng slot/cùng global If-Match | Một commit; request sau stale 412. Nếu refetch revision rồi create duplicate: 409. SQL độc lập vẫn chặn hai roots bằng unique index. |
| Writer bỏ qua app lock, hai roots cùng slot | Unique index/locking không cho cả hai commit. Deadlock victim/constraint error rollback, không fake success. |
| Hai correction cùng expected version | Một v(n+1), request còn lại 412; PK vẫn chặn nếu writer cấp version sai. |
| Hai pair khác nhau cùng book/global revision | Writer tuần tự theo lock; stale request refetch, không tự overwrite. Trigger cấp mỗi command một R liên tiếp; không thể late-append vào R đã commit. |
| Locale PATCH vs rate command | Cùng global lock/If-Match; tránh stale array rewrite. Rate-only command không đổi Bootstrap base/version. |
| Cùng idempotency key sau timeout | Replay authorized trước stale-token check; không INSERT, không chạy trigger tăng R lần nữa. Khác fingerprint trả 409. |
| Audit/outbox/response insert lỗi sau rate INSERT | Rollback cả ledger/book/global revision; không publish success trước commit. |
| Reader fixed R trong lúc writer commit | Reader consistent transaction lấy R và selection trong cùng snapshot/lock scope; không trộn book/header với rows khác revision. Không có mutable update row cũ. |

Resolver ban đầu có thể dùng Serializable read với book shared lock rồi đọc rows BookRevision<=R; writer lấy book lock trước insert theo cùng thứ tự. Không tự bật snapshot isolation/migration database options nếu chưa đánh giá. Map deadlock/timeout theo error/retry contract được admit; retry giữ idempotency key, không retry stale business content bằng If-Match mới tự động.

## 6. Acceptance bắt buộc trước khi gọi đây là SQL integrity PASS

Tất cả **NOT_RUN**: root self-FK v1 self-reference; duplicate independent RateId; bypass v1 bằng v2; đổi pair/start; skipped version/NULL predecessor; UPDATE/DELETE/TRUNCATE bị chặn ở runtime principal; fake BookRevision/gap/reappend current R; two-session concurrency; multirow import một R; rollback trigger/audit/outbox; fixed-R trước/sau hồi tố; constraints/trigger enabled và EF OUTPUT behavior. Chỉ chạy khi được phép implementation/test trong DB GUID riêng; không chạy DDL thử trên DB Phase 1 hiện tại.
