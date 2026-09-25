# Flow source và kiểm thử

[Trang bắt đầu](../README.md) · [Contract API](api.md)

## Call flow

| Flow | Chuỗi gọi |
|---|---|
| Gemini models | api.create_app → endpoint models → read_input → TranslationService.models → GeminiAdapter.discover |
| Dịch | endpoint translate → read_input → connected → TranslationService.translate → prepare/plan/prompt_for → Provider.execute → parse_response/apply_response/result_for |
| Genprompt | endpoint genprompt → read_input → TranslationService.genprompt → prepare → representatives → prompt_for |
| Validate | endpoint validate_output → read_validation → read_input (JSON/form) hoặc multipart parse → TranslationService.validate → prepare/parse_response/apply_response/result_for |
| Plugin | load_plugins → entry point translation_service.plugins → register(PluginHost, options) |

Source nằm trong [api.py](../src/translation_service/api.py), [service.py](../src/translation_service/service.py), [domain.py](../src/translation_service/domain.py), [providers.py](../src/translation_service/providers.py), [extensions.py](../src/translation_service/extensions.py) và [tokens.py](../src/translation_service/tokens.py).

input_openapi chỉ mô tả `application/json` trong Swagger và thêm ví dụ cho từng field, gồm `texts` là array. create_app phục vụ /docs cùng swagger_fields.js: script dựng ô nhập theo schema JSON và thay body của request Swagger bằng JSON ghép từ các ô. read_input vẫn hỗ trợ URL-encoded form cho client cũ: chuyển một chuỗi `texts` thành một unit hoặc parse JSON array thành nhiều unit, rồi kiểm tra bằng cùng Pydantic model của JSON body. read_validation giữ thêm nhánh upload `multipart/form-data`.

## Chuẩn bị và xử lý output

prepare kiểm tra source token, lọc unit không có ký tự alphabetic và tính representative. Dedup dùng NFC/lowercase/trim trên key; source thực tế không bị normalize. Unit dài tối đa 100 ký tự nội dung dùng thêm hai unit lân cận mỗi phía để phân biệt context. Whitespace ở ranh giới run vẫn thuộc key.

plan chọn tối đa max_batch_units target mỗi lượt, mặc định 40. Budget dùng ước lượng bảo thủ theo UTF-8 bytes, output dự kiến, reasoning budget và history sync. Unit quá lớn giữ source với unit_too_large. Nếu history sync làm hết budget, request dừng với session_budget_exhausted.

parse_response kiểm tra object duy nhất texts, count, kiểu chuỗi và Unicode trước khi apply_response validate từng unit. Apply theo representative nhưng kiểm tra từng source và luôn giữ đúng source tại vị trí fallback. result_for chỉ đưa unit fallback vào `fallbacks`, kèm source `input`, giá trị thực trả `output` và `diagnostics`; unit translated/skipped chỉ nằm trong `texts`. Không có retry subset hoặc checkpoint.

connected theo dõi ASGI disconnect, hủy task đang chờ và dọn watcher. Gemini dùng SDK async; mỗi discovery/lượt đóng client riêng, Interactions max_retries bằng 0. Không bắt cancellation để chuyển thành kết quả thành công.

## Cấu hình

[Settings](../src/translation_service/config.py) và [config.example.json](../config.example.json) dùng cùng default:

Settings.from_env đọc file từ TRANSLATOR_CONFIG nếu có; nếu không, dùng cấu hình mặc định. API không yêu cầu caller credential. File cấu hình cũ cần bỏ key callers vì Settings từ chối field lạ.

| Key | Default/đơn vị |
|---|---|
| max_request_bytes | 8.000.000 byte, toàn bộ body |
| max_units | 10.000 unit |
| max_unit_chars | 200.000 Unicode code point |
| max_batch_units | 40 target/lượt, cấu hình trong khoảng 1–40 |
| provider_timeout_seconds | 600 giây cho từng discovery hoặc lượt |
| reasoning_budget_tokens | 4.096 token/lượt |
| plugins | Rỗng; chỉ entry point được cấu hình mới được nạp |

model_capabilities ánh xạ provider:model sang context_tokens, output_tokens, session hoặc efforts. Override áp dụng sau discovery, không thể thay id hoặc đưa model bị lọc trở lại.

## Kiểm thử

Từ repository:

~~~sh
dotnet build filehandler/src/FileHandler.Api/FileHandler.Api.csproj -p:GenerateDocumentationFile=true
python -m pip install -r translator/requirements.lock ./translator
python -m unittest discover -s translator/tests -v
~~~

| Test | Assertion chính |
|---|---|
| test_api.py | HTTP không cần Authorization/caller config, legacy form so với JSON, route bị bỏ, upload/BOM/fence, schema/quota, fallback, session/key isolation và timeout |
| test_domain.py | Fixture token chung với Filehandler, boundary whitespace và prompt rules |
| test_providers.py | SDK request thật qua MockTransport: schema/session/key, pagination, lọc model và không retry |
| test_filehandler_flow.py | HTTP Filehandler thật: import → dịch hoặc genprompt/validate → export; bytes giữ chữ đậm khi reorder |

Test liên service dùng DLL đã build, hoặc FILEHANDLER_DLL. Nếu thiếu DLL, unittest đánh dấu skip. Test khởi động Filehandler từ thư mục source để dùng đúng content root và static assets; không thay cấu hình logging hay validation.

Plugin có [test suite riêng](../../plugins/translator-codex-cli/tests/test_plugin.py), được chạy sau khi cài package. CI tách core không cài plugin khỏi job kiểm thử plugin trên Linux/Windows; gateway có TypeScript build và fake CLI tests.

Docstring Python dùng English ngắn, mô tả input/output cần thiết. Field/property/constant có comment về ý nghĩa hoặc đơn vị. Test kiểm tra qua API hoặc kết quả công khai, không đổi visibility để tăng coverage.
