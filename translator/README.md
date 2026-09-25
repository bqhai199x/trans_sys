# Translator

Python API xử lý input, prompt và output trong một HTTP request. Gemini nhận API key từ JSON body của từng request. Codex CLI được cung cấp bởi package tùy chọn [translator-codex-cli](../plugins/translator-codex-cli/README.md).

- [Contract và ví dụ API](docs/api.md)
- [Flow source và kiểm thử](docs/development.md)
- [OpenAPI của core](docs/openapi.json)
- [Cấu hình mẫu](config.example.json)

## Chạy service

Yêu cầu Python 3.12+. Core không cần Node.js, SQLite hoặc worker.

~~~sh
python -m venv .venv
.venv/bin/python -m pip install -r requirements.lock .
export TRANSLATOR_CONFIG=/etc/translator/config.json
.venv/bin/python -m uvicorn translation_service.api:create_app --factory --host 127.0.0.1 --port 8000 --no-access-log
~~~

Trên Windows, mở PowerShell trong thư mục `translator`:

~~~powershell
python -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r .\requirements.lock .
$env:TRANSLATOR_CONFIG = '.\config.example.json'
$env:PYTHONPATH = '.\src'
.\.venv\Scripts\python.exe -m uvicorn translation_service.api:create_app --factory --host 127.0.0.1 --port 8000 --no-access-log
~~~

Truy cập /docs để xem Swagger. Các endpoint POST hiển thị ô riêng cho từng field nhưng **Execute** gửi `application/json`. Trong ô `texts`, dán JSON array như `["hello", "world"]`; giao diện đặt array này vào property `texts` của JSON body. `/api/validate` còn có `multipart/form-data` để upload file. API vẫn nhận URL-encoded form từ client cũ, nhưng Swagger không hiển thị lựa chọn đó. Các endpoint /api không yêu cầu header Authorization. Cấu hình chứa giới hạn xử lý, capability overrides và plugin được bật; Gemini api_key được gửi theo từng request. Có thể chạy core với cấu hình mặc định khi không đặt TRANSLATOR_CONFIG. Nếu dùng file cấu hình cũ, xóa key callers trước khi khởi động lại service.

[Unit systemd](deploy/translator-api.service) và [Dockerfile](Dockerfile) chỉ chạy API. Đặt HTTPS ở reverse proxy cho truy cập từ máy khách. Timeout trong cấu hình áp dụng cho từng discovery/lượt; một request nhiều lượt có thể kéo dài hơn giá trị này. Disconnect hủy phần I/O đang chờ, không tạo công việc chạy nền.

## Hành vi chính

- Chỉ có sync và batch; mặc định batch. Cả hai trả kết quả trực tiếp, không có job ID.
- Mỗi lượt chạy tuần tự. Sync truyền cursor mới nhất sang lượt tiếp; batch luôn bắt đầu session mới.
- Không retry tự động, không tự đổi mode hoặc tạo session thay thế khi lỗi.
- Genprompt trả một prompt sau filter/dedup. Validate nhận input gốc cùng raw output hoặc file JSON.
- Output sai token ở từng unit giữ source tương ứng, trả partial và ghi unit đó trong `fallbacks` với `input`/`output`. Sai schema/count từ chối toàn bộ.
- Không lưu source, translations hoặc Gemini key vào database của core; không cache Gemini catalog giữa request.

Client HTML, proxy, launcher local và implementation cũ đã chuyển ra backup ngoài repository. Các route job/custom không còn tồn tại. Filehandler vẫn là service độc lập, nhận texts theo thứ tự nguồn khi export.
