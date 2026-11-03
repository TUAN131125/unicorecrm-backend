# Gemini pre-header timeout — chẩn đoán transport

Ngày 2026-10-22. Correlation ID do người dùng cung cấp: `bb3d6b48-267e-404b-8f81-98b50e85e8ed`.

## Kết luận đã xác minh

Log của request lỗi xác nhận **application deadline hết khi SendAsync chưa trả response headers**: ApplicationDeadline, SEND_OR_WAIT_HEADERS, HTTP status null, elapsed toàn thao tác 30013 ms. Đây không phải bằng chứng Google trả HTTP 504. Chưa có bằng chứng API key sai, JSON lỗi hoặc parser gây timeout; parser chưa được chạy trong phase này.

Kiểm tra kết nối hiện tại từ process console trên Windows qua chính named HttpClient đăng ký cho Gemini đã thành công tới host Google, TCP 443 và TLS mặc định. Nhận HTTP 404 của HEAD `/` trong 268 ms. **404 ở root là kết quả kiểm tra transport, không phải lỗi model và không phải real-provider acceptance PASS.** Không gửi generateContent, key, CRM data hoặc body.

**Nguyên nhân gốc của request cũ và subphase bị treo vẫn CHƯA XÁC MINH.** Không có trace DNS/socket/TLS/gửi body của request cũ trong bằng chứng hiện có. Người dùng xác nhận Backend chạy trực tiếp trên Windows local này, ASP.NET Core ApiHost từ `D:\Project_All\UnicoreCRM\backend`, API `http://localhost:5080`, không Docker/WSL. Probe đã chạy trên đúng Windows và qua source đăng ký named client, nhưng không bên trong process Backend đã tạo correlation ID; không đọc môi trường/secrets của process đó. Kết nối hiện tại không loại trừ lỗi mạng gián đoạn, proxy/PAC khác theo process/user, connection pool, gửi request hoặc Google xử lý chậm tại thời điểm request cũ.

**Runtime capture hiện BLOCKED:** truy vấn `Get-NetTCPConnection -State Listen` thành công, số listener trên 5080 bằng 0. TCP tới 127.0.0.1:5080 và [::1]:5080 không kết nối được trong budget 2 giây mỗi địa chỉ. Không có ApiHost trên port này để gắn trace/tái hiện theo cấu hình người dùng cung cấp. Không tự khởi động lại môi trường đã được yêu cầu giữ tắt trong phạm vi trước đó. Việc hiện không có listener không phải nguyên nhân của timeout cũ.

## Truy vết source và handler

`ApiHost/Program.cs` → `AddAIModule` → `ProvidersModule.AddProvidersModule` → `AddHttpClient<GeminiAiProvider>` → HttpClientFactory → `SocketsHttpHandler` → GeminiAiProvider.SendAsync(ResponseHeadersRead).

- BaseAddress: `https://generativelanguage.googleapis.com/`.
- Không thấy global HttpClient defaults override, proxy override, ConnectCallback hoặc certificate bypass trong ApiHost/AI source.
- Handler thật của named client trong probe: SocketsHttpHandler, UseProxy=true; hệ thống chọn DIRECT. Không đặt UseProxy=false, không xóa proxy environment, không sửa trust/certificate.
- Client timeout 100 s. ConnectTimeout mặc định InfiniteTimeSpan; không có timeout kết nối riêng. Toàn bộ attempt Test vẫn bị application deadline hữu hạn 30 s chặn. Không thay các giá trị này.
- Probe ghi nhận HTTP/1.1, RequestVersionOrLower, không có custom certificate validation callback. Backend process cũ có thể có môi trường hoặc trạng thái pool khác; không suy ra config runtime cũ chỉ từ probe.
- Default mock transport chỉ thay handler nếu Development và UseDeterministicTransport=true. Probe dùng cấu hình rỗng/source defaults nên xác minh bằng handler thật, không dùng mock.

[Microsoft networking events](https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/telemetry/events) mô tả các event source HTTP, DNS, sockets, security để chia phase. [DefaultProxy](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpclient.defaultproxy?view=net-10.0) dùng environment/user proxy trên Windows khi không override. [ConnectTimeout](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.socketshttphandler.connecttimeout?view=net-10.0) mặc định vô hạn; điều này không vô hiệu application cancellation.

## Bằng chứng mạng hiện tại

File JSON an toàn: `docs/verification/evidence/gemini-pre-header-20261022/transport-probe.json`.

- Thời điểm: 2026-10-22 11:23:53 UTC (18:23:53 giờ Việt Nam).
- Runtime thực thi verifier: .NET 10.0.12. Probe shell sơ bộ trước đó dùng .NET 10.0.11; không gán phiên bản này cho Backend process cũ.
- ProxySelectionStart 0 ms → Stop 13 ms: hệ thống chọn DIRECT.
- ResolutionStart 48 ms → Stop 56 ms: DNS hoàn tất.
- ConnectStart 60 ms → Stop 99 ms: TCP hoàn tất tới HTTPS port 443.
- HandshakeStart 103 ms → Stop 192 ms: TLS hoàn tất với kiểm tra chứng chỉ mặc định.
- ConnectionEstablished 195 ms, RequestLeftQueue 197 ms.
- RequestHeadersStart 198 ms → Stop 199 ms: HEAD request headers được transport ghi.
- ResponseHeadersStart 263 ms → Stop 264 ms, RequestStop 268 ms: nhận HTTP 404, không đọc body.

Một lần DNS sơ bộ cũng thành công (45 ms, 8 địa chỉ IPv4). Các giá trị là đo thật hiện tại. Không có lỗi DNS/TCP/TLS tái hiện trong các probe; không suy ra POST có body hoặc model generation đã thành công.

## Cancellation có che lỗi gốc không?

- Application tạo deadline riêng, linked caller token, truyền token vào adapter và dùng WaitAsync cùng token. Khi task transport còn pending tại deadline, WaitAsync trả cancellation; application chuyển sang ApplicationDeadline. Cơ chế này không cung cấp mốc DNS/TCP/TLS bên trong SendAsync.
- Adapter ưu tiên nhận biết token được truyền vào đã bị hủy và rethrow OperationCanceledException. Application ưu tiên caller cancellation; chỉ gán ApplicationDeadline nếu deadline thực sự bị hủy và caller chưa hủy.
- Nếu HttpRequestException đã trả trước deadline: adapter giữ HttpRequestError enum; application log safe NetworkError, phân loại PROVIDER_NETWORK_FAILURE. DNS/TLS/connection regression hiện có PASS. Không có bằng chứng code biến một lỗi kết nối đã hoàn tất trước deadline thành timeout trong request được cung cấp.
- Khi cancellation xảy ra trước khi OS/handler trả lỗi cụ thể, không có lỗi kết nối hoàn tất để khôi phục. Nếu OCE có nested socket/network exception thì logger hiện chỉ ghi phase/status/elapsed và application tạo exception timeout mới, không giữ nested safe codes. Đây là giới hạn diagnostic của đường cancellation, **không chứng minh có lỗi kết nối gốc đã bị che trong correlation ID này**. Không log nguyên exception/message để tránh rò rỉ dữ liệu.
- Caller cancellation, HttpClient deadline, provider HTTP 504 và application deadline có regression riêng. Không thay ưu tiên cancellation hoặc tăng thời gian đợi để ép lấy lỗi muộn.

## Cách phân biệt phase khi tái hiện

| Bằng chứng cùng request trong cùng process | Điều có thể xác nhận |
|---|---|
| Chưa có resolution/connect, proxy selection chưa kết thúc | Có thể đang trước kết nối/proxy; không gán DNS chỉ từ SEND_OR_WAIT_HEADERS |
| ResolutionStart chưa có Stop/Failed | DNS đang pending ở thời điểm quan sát |
| ConnectStart chưa có Stop/Failed | TCP connect đang pending |
| HandshakeStart chưa có Stop/Failed | TLS đang pending |
| ConnectionEstablished, RequestContentStart chưa có Stop | Đang gửi/serialize body; cần event của POST thật |
| RequestContentStop, chưa nhận response headers | Đã qua mốc ghi body vào transport; có thể đang chờ response/network/provider, chưa chứng minh Google đã xử lý |

Event có thể thiếu cặp start/stop khi bị hủy; connection reuse có thể không có DNS/TCP/TLS mới. Phải correlate chính request và tính tới pool/connection reuse, không lấy event của request khác để kết luận.

## File thay đổi trong lần chẩn đoán này

1. `scripts/AiProviderVerifier/Program.cs`: thêm nhánh `transport` độc lập trước nhánh live, không đọc API key.
2. `scripts/AiProviderVerifier/GeminiConnectivityProbe.cs`: HEAD root duy nhất qua named Gemini client; thu allowlisted event name/timestamp, status và safe error enums. Không lấy event payload, URL từ event, header, cookie, exception message hay body. Không sửa handler/proxy/TLS. Diagnostic budget 12 s riêng cho probe, không sửa timeout của Backend.
3. `docs/verification/evidence/gemini-pre-header-20261022/transport-probe.json`: kết quả đo thật an toàn.
4. Báo cáo này.

Không sửa production adapter, ProvidersModule, cancellation logic, parser, AI Chat, DataProtection, SQL hoặc Workspace trong lần chẩn đoán này: chưa có bằng chứng lỗi cấu hình/code transport để sửa root cause. Các thay đổi có trước trong working tree được giữ nguyên.

## Kiểm thử đã thực hiện

```powershell
dotnet run --project scripts/AiProviderVerifier/UnicoreCRM.AI.ProviderVerifier.csproj --no-restore -- transport
dotnet run --project scripts/AiProviderVerifier/UnicoreCRM.AI.ProviderVerifier.csproj --no-restore
git diff --check
```

- Connectivity HEAD: HEADERS RECEIVED, HTTP 404, 268 ms ở lần lưu evidence; DNS/TCP/TLS có mốc hoàn tất.
- Focused verifier mặc định: `GEMINI_REST_CONTRACT_MOCK_PASS`, `AI_PROVIDER_ADAPTER_VERIFIER_PASS`. Bao gồm lỗi DNS/TLS/connection trước deadline, application deadline trước headers/trong body, transport không hợp tác cancellation, HttpClient timeout, caller cancellation, provider 504, validation/Activate gating và Gemini/OpenAI regressions. Đây là regression mock, không phải model acceptance.
- Không chạy Backend/Frontend hoặc Test/Activate thật. Không gọi Gemini generateContent.
- SHA-256 trước/sau lần chẩn đoán: 103 P0 pinned files không đổi.
- Không stage/commit/push.

## Phần còn cần xác minh tại đúng runtime lỗi

Backend đã được xác nhận là Windows local trực tiếp. Khi ApiHost thực sự nghe port 5080 trở lại, cần thu các mốc transport an toàn trong process đó cho một lần tái hiện, cùng correlation ID mới và trạng thái deadline/caller. Không thể phục hồi DNS/TCP/TLS subphase của correlation ID cũ chỉ bằng các dòng log đã có. Không dùng full/raw HTTP tracing hoặc Private.InternalDiagnostics vì có thể chứa dữ liệu nhạy cảm. Probe `transport` chỉ kiểm tra process được gọi; chạy thành công trên cùng Windows không chứng minh môi trường hoặc trạng thái kết nối của process Backend cũ giống hệt.
