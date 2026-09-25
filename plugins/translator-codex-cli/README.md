# translator-codex-cli

Plugin Python bổ sung Codex CLI cho [Translator](../../translator/README.md). Module là translator_codex_cli; entry point translation_service.plugins có tên codex. Codex chạy trên gateway Windows của người dùng và chủ động kết nối HTTPS tới API.

- [Cấu hình đầy đủ](config.example.json)
- [OpenAPI core kèm plugin](docs/openapi.json)
- [Source Python](src/translator_codex_cli/)
- [Source gateway](gateway/src/)

## Cài phần server

~~~sh
python -m pip install -r translator/requirements.lock ./translator ./plugins/translator-codex-cli
~~~

Bật plugins.codex trong TRANSLATOR_CONFIG theo cấu hình mẫu. Core không cần cài hoặc bật plugin để chạy Gemini/genprompt/validate. Package được cài nhưng chưa khai báo trong plugins thì không thêm route hoặc tạo database. Khai báo plugin chưa cài làm startup thất bại rõ ràng.

GatewayStore tạo database riêng với gateways, enrollments và deliveries. Không dùng lại jobs.sqlite của implementation cũ; database chứa bảng jobs bị từ chối. Job/client cũ nằm trong backup ngoài repository. Cài lại gateway bằng enrollment mới; profile local cũ cũng đã được backup.

Với systemd, tạo thư mục database thuộc user service và thêm ReadWritePaths=/var/lib/translator-codex-cli trong override của unit. Dùng filesystem local cho SQLite. Không đặt database trên NFS/SMB.

## Build và cài gateway Windows

Yêu cầu Node.js 24 x64 và Codex CLI 0.155.0 theo profile đã kiểm thử.

~~~powershell
cd plugins/translator-codex-cli/gateway
npm.cmd ci
npm.cmd test
.\build-bundle.ps1 -NodeExecutable C:\runtime\node.exe -CodexNodeModules C:\runtime\node_modules -OutputZip C:\release\gateway-bundle.zip
~~~

Đặt ZIP trên server và cấu hình gateway_bundle trỏ đến file đó. Source gateway/installer nằm trong source distribution; ZIP chứa runtime được build và triển khai riêng, không nằm trong Python wheel.

Client gọi POST /api/gateways/enrollments với JSON {} hoặc client_instance_id. Response có client_instance_id, expires_at và download_url. Download chứa registration.json gắn đúng device, mã dùng một lần có hạn 30 phút.

Giải nén và chạy install.ps1 dưới user Windows cần dùng Codex. Installer đăng ký Scheduled Task chạy khi logon, khởi động ẩn và giới hạn quyền thư mục credential. Gateway lưu identity/journal dưới %LOCALAPPDATA%/TransSysGateway. Chạy login.ps1 nếu readiness là login_required. Core server không nhận credential đăng nhập Codex.

Installer hiện có từ cấu hình trước phải được thu hồi/gỡ trước khi ghép sang client instance khác. Không sửa file credential của một installation đang dùng.

## API và transport

Các route /api không yêu cầu Authorization hoặc cấu hình callers. Thiết bị được chọn bằng client_instance_id; mọi client gọi API đều có thể xem trạng thái, dùng hoặc thu hồi thiết bị đó. File cấu hình cũ cần bỏ key callers trước khi khởi động lại service.

Swagger cho POST /api/codex/translations và POST /api/gateways/enrollments dùng `application/json` nhưng hiển thị ô nhập riêng cho từng field. Dán `["hello", "world"]` vào ô `texts`; khi Execute, request JSON chứa `texts` là array. `model` lấy từ GET /api/codex/models. `X-Client-Instance-Id` vẫn là header riêng cho các route Codex models/translations. API vẫn nhận URL-encoded form từ client cũ, nhưng Swagger không hiển thị lựa chọn đó.

| Route | Contract |
|---|---|
| GET /api/codex/models | X-Client-Instance-Id bắt buộc; trả catalog của device |
| POST /api/codex/translations | Input chung + model/mode/reasoning_effort; header device bắt buộc; trả trực tiếp |
| POST /api/gateways/enrollments | Tạo enrollment/download |
| GET /api/gateways/{client_instance_id} | Connectivity, readiness, busy và thời điểm catalog |
| DELETE /api/gateways/{client_instance_id} | Thu hồi device, hủy delivery đang diễn ra |

Body Codex không nhận api_key. /internal/gateways/enroll nhận mã enrollment dùng một lần và device_token trong body. Gateway dùng Authorization: Bearer <device_token> cho /internal/gateways/heartbeat và /internal/gateways/tasks/*.

CodexAdapter.discover tìm gateway theo client_instance_id rồi kiểm tra online, readiness, busy và tuổi catalog. Offline/not-ready trả 503; busy trả 409; không có hàng chờ. Model/capability override đi qua pipeline chung. Sync cần context_tokens và output_tokens đã biết; CLI discovery hiện không cung cấp hai giá trị này nên phải cấu hình đúng theo model trước khi dùng sync.

Database gateway hiện có tiếp tục dùng được. Cột caller được giữ để tương thích schema cũ, không dùng để kiểm tra quyền hoặc chọn thiết bị; bản ghi mới ghi chuỗi rỗng vào cột này.

CodexAdapter.execute reserve đúng một gateway → gửi prompt/schema → await result → trả TurnResult cho core validate. Core là nơi filter/dedup, tạo prompt và validate output; plugin không xử lý translation units.

| Giới hạn transport | Default |
|---|---|
| Heartbeat gateway | 15 giây |
| Đánh dấu offline | 45 giây |
| Tuổi catalog tối đa | 600 giây |
| Long polling | 25 giây |
| Một delivery | 600 giây |
| Lease của HTTP request đang sống | 60 giây, được gia hạn khi await |
| Giữ receipt accepted/discarded | 86.400 giây |

Gateway journal giữ received → start_authorized → running → result_ready → acknowledged. Server chỉ chấp nhận result có đúng device/token. Mất ACK chỉ gửi lại cùng result; payload khác cùng receipt bị từ chối. Restart với execution không rõ trạng thái trả uncertain, không tự chạy lại CLI.

Disconnect/timeout hủy delivery. Heartbeat yêu cầu dừng CLI đang chạy; result đến muộn được acknowledged là discarded. Khi process API chết, lease không được gia hạn; delivery hết hạn được bỏ ở lần truy cập transport tiếp theo. Không khôi phục HTTP request. Prompt và raw response được xóa khỏi bản ghi khi request kết thúc; receipt giữ hash để chống xử lý trùng. SQLite có thể còn byte cũ trong page/WAL cho đến khi được thu hồi.

## Kiểm thử

~~~sh
python -m unittest discover -s plugins/translator-codex-cli/tests -v
cd plugins/translator-codex-cli/gateway
npm test
~~~

Python test dùng request HTTP và fake gateway để kiểm tra API không cần caller credential, form Swagger, device token, database cũ, enrollment, device isolation, busy/offline, sync/batch, ACK lặp/restart, timeout, ASGI disconnect và result muộn. Gateway test dùng fake CLI để kiểm tra JSONL UTF-8, completion, process cancellation, discovery và journal; không gọi model trả phí.
