# F01 — intentional security contract narrowing: approval requested

Status: **APPROVAL_REQUESTED**, chưa được phê duyệt. Đây không phải lệnh chấp nhận baseline hay quality waiver.

Phạm vi phê duyệt được đề nghị: chỉ thu hẹp `POST /activities` / `LogActivityRequest.type` từ CALL/EMAIL/MEETING/NOTE/MESSAGE/SYSTEM xuống CALL/EMAIL/MEETING/NOTE/MESSAGE. Read `ActivityType` và `ActivityReadModel` vẫn giữ SYSTEM. Backend đã từ chối SYSTEM với 422; thay đổi đồng bộ contract/client với security behavior, không mở quyền hay tạo system writer.

Before/after và generated excerpts: [F01 evidence](evidence/activity-write-contract-20261021/after-contract.json), `before-contract.json`, `before-generated.txt`, `after-generated.txt`. Caller inventory và runtime evidence nằm trong [F01 report](activity-write-contract.md). Lead bỏ fallback unknown→SYSTEM; manual mapper/public command chặn loại không hợp lệ trước port. Deal/Quote không cố ghi SYSTEM sau Quote transition nữa; không thay bằng NOTE hay dựng audit.

Pins dùng để review:

- Accepted baseline giữ nguyên SHA-256 `c8e77d670453860796e3c32bcf17bc9ffbefe06bc0a8915d9509338bcabf66ef`.
- Spec candidate SHA-256 `d894e313d9dad7ae78c0c9bb8fd865f06d2d04546bfbaca3683465780e7e1659`.
- Breaking finding chính xác: `Schema LogActivityRequest [REQUEST].type: enum domain is incompatible.`
- Before→after F01 có đúng 1 finding mới. Accepted baseline→candidate vẫn có **43 findings**, gồm 42 cũ chưa được chấp nhận; [ledger riêng](evidence/p0-checkpoint-finalization-20261021/compatibility-42-ledger.md).

Evidence thực thi: schema/mapper và compile-time Red→Green; CALL/NOTE và ba loại manual còn lại 201; SYSTEM/unknown 422; missing/foreign/contradictory references và forged source bị từ chối; canonical multi-reference/relationship-only thành công. Cumulative current candidate chạy lại 580 security/performance assertions và UI 12 bước; kết quả connected cập nhật trong [checkpoint report](p0-conditional-checkpoint.md). SYSTEM read declaration vẫn có test compile/runtime.

Consumer impact: generated client cũ vẫn cho phép SYSTEM write sẽ bị Backend từ chối; client mới không compile payload đó. Unknown types không được đổi thành NOTE. Known repo callers được kiểm kê; chưa chứng minh mọi external consumer đã nâng cấp. Phiên bản `0.24.1-contract.0` là **đề xuất**, chưa đổi package/release/spec identity hoặc phát hành.

## Điều đề nghị người dùng quyết định

1. Phê duyệt **intentional security narrowing F01 riêng**, với hai SHA pins trên và current cumulative source manifest.
2. Có chấp nhận một **conditional functional checkpoint chỉ cho paired current frontend/backend và hành trình P0 đã test** trong khi compatibility gate vẫn FAIL hay không. Quyết định này không tuyên bố hỗ trợ historical clients.
3. Review riêng 42 response-enum findings và các baseline quality-gate waivers được liệt kê trong checkpoint report; không coi F01 approval là approval cho các mục đó.

Không chạy `--accept-breaking-baseline`: quy trình hiện tại nhận toàn snapshot và sẽ nhận luôn 42 findings chưa được phê duyệt. Accepted baseline tiếp tục giữ nguyên. Không triển khai exception, filter findings, sửa checker hoặc làm gate báo PASS. Nếu sau này cần một exception chính xác cho F01, cần thiết kế/review riêng với baseline/spec hashes, operation/schema path, old/new enum, expiry và kết quả checker đầy đủ; approval artifact có thể ghi quyết định nhưng không được đổi compatibility FAIL thành PASS ngầm.

Checkpoint conditional có thể được review độc lập; **production release vẫn bị chặn** bởi compatibility/consumer decisions, quality gates chưa có waiver, Quote mutation implementation gap và các điều kiện release khác trong report.
