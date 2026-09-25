# Contract API Translator

[Trang bắt đầu](../README.md) · [OpenAPI](openapi.json) · [Codex plugin](../../plugins/translator-codex-cli/README.md)

## Truy cập và input chung

Các endpoint /api và /health không yêu cầu caller credential. Header Authorization có thể bỏ qua; giá trị rỗng hoặc chuỗi null cũng không ảnh hưởng xử lý. Các endpoint Gemini vẫn yêu cầu api_key trong request body. Schema từ [schemas.py](../src/translation_service/schemas.py) từ chối field lạ và ép kiểu ngầm.

Trong `/docs`, chọn `application/json` và nhập từng ô riêng. Ô `texts` nhận JSON array như `["Hello, world","Goodbye"]`; ô `target_language` nhận `vi`. Khi bấm **Execute**, Swagger gửi `{"texts":["Hello, world","Goodbye"],"target_language":"vi"}`. Phần **Curl** phải hiển thị `Content-Type: application/json` và `texts` là array. API vẫn nhận URL-encoded form từ client cũ, nhưng không hiển thị lựa chọn đó trong Swagger. `/api/validate` còn hỗ trợ `multipart/form-data` với `input` và `output_file` để upload file.

| Field | Ý nghĩa |
|---|---|
| texts | string[] theo thứ tự source |
| target_language | Ngôn ngữ đích, 1–100 ký tự |
| context | Context tài liệu; mặc định chuỗi rỗng |
| custom_prompt | Chỉ dẫn dịch bổ sung; mặc định chuỗi rỗng |

Token tự mô tả dùng chung với Filehandler: run ox:rN, protected anchor ox:kN, boundary ox:bN. Token phải hợp lệ ngay ở input, kể cả unit không có chữ. Không gửi token_metadata.

## Gemini

POST /api/gemini/models nhận key trong body; form Swagger có field `api_key` riêng:

~~~json
{"api_key":"YOUR_GEMINI_API_KEY"}
~~~

Response có models, mỗi model gồm id, efforts, session, context_tokens, output_tokens. GET ở route này trả 405.

POST /api/gemini/translations:

~~~json
{
  "api_key": "YOUR_GEMINI_API_KEY",
  "texts": ["hello"],
  "target_language": "vi",
  "model": "gemini-2.5-flash",
  "mode": "batch",
  "context": "",
  "custom_prompt": ""
}
~~~

Chọn model từ catalog thực tế của key; ví dụ không bảo đảm model luôn khả dụng. reasoning_effort là field tùy chọn và phải thuộc efforts của model. Mode chỉ nhận sync/batch; auto bị từ chối. Sync cần session và context/output budget đã biết.

Form Swagger cho endpoint này có từng field `api_key`, `texts`, `target_language`, `context`, `custom_prompt`, `model`, `mode` và `reasoning_effort`. Ví dụ trong từng ô chỉ để hướng dẫn nhập; chọn model và reasoning_effort được catalog thực tế hỗ trợ. Để trống `reasoning_effort` nếu không dùng.

GeminiAdapter trong [providers.py](../src/translation_service/providers.py) lấy toàn bộ trang discovery và giao với GEMINI_TEXT_MODELS, đồng thời yêu cầu supported_actions chứa đúng generateContent. Catalog này được đối chiếu tài liệu Google ngày 2026-09-24 và version hóa cùng adapter. Model chuyên ảnh, audio/TTS, video, Live, embedding, agent hoặc model chưa xác nhận không xuất hiện. Endpoint dịch kiểm tra lại cùng catalog; capability override không thêm được model đã bị lọc.

Key chỉ đi vào ProviderContext và SDK client của request. Không đọc key từ cấu hình hoặc GEMINI_API_KEY, không đưa vào prompt, response, log hay database. Mỗi request discovery dùng lại key được gửi trong chính request đó; không có catalog cache qua request.

## Genprompt

POST /api/genprompt nhận input chung và trả JSON có field prompt:

~~~json
{"texts":["hello","123"],"target_language":"vi"}
~~~

Prompt chứa instruction và targets sau filter/dedup, kèm references chỉ để tham khảo. ChatGPT phải trả một object có duy nhất texts, số phần tử bằng số target trong prompt, đúng thứ tự target. Không dịch references.

Genprompt không chia prompt hoặc gọi provider. Input vượt giới hạn bị từ chối; giới hạn context của phiên ChatGPT không được suy đoán.

## Validate raw output

POST /api/validate với Content-Type: application/json:

~~~json
{
  "texts": ["hello","123"],
  "target_language": "vi",
  "raw_output": "{\"texts\":[\"xin chào\"]}"
}
~~~

Gửi lại đúng input đã dùng cho genprompt. Service tái tạo filter/dedup/mapping; không nhận job ID hoặc context lưu trên server. Raw output chấp nhận JSON thuần hoặc một code fence hoàn chỉnh có nhãn json hoặc không nhãn. Văn bản giải thích bên ngoài, nhiều fence, property trùng và sửa token ngầm không được chấp nhận.

## Validate file JSON

Cùng POST /api/validate, dùng multipart/form-data với đúng hai part:

| Part | Nội dung |
|---|---|
| input | Chuỗi JSON chứa input chung, giống request genprompt |
| output_file | Một file UTF-8/UTF-8 BOM chứa object JSON có duy nhất texts |

~~~sh
curl -X POST https://translator.example.com/api/validate \
  -F 'input={"texts":["hello","123"],"target_language":"vi"}' \
  -F 'output_file=@answer.json;type=application/json'
~~~

answer.json:

~~~json
{"texts":["xin chào"]}
~~~

File không chứa Markdown fence. Không gửi raw_output cùng file hoặc upload nhiều file. Tổng multipart body, gồm input gốc, file và framing, chịu max_request_bytes. Nội dung file được đóng/xóa khỏi upload storage sau request.

## Kết quả và lỗi

Dịch và validate trả cùng shape, HTTP 200:

~~~json
{
  "status": "success",
  "texts": ["xin chào","123"],
  "fallbacks": []
}
~~~

`fallbacks` chỉ chứa unit phải giữ source do lỗi token/output hoặc vượt budget. Mỗi phần tử có `source_index` (vị trí trong input), `input` (source), `output` (giá trị thực trả trong `texts` tại vị trí đó, bằng source) và `diagnostics` (mã nguyên nhân). Unit dịch thành công hoặc được bỏ qua không xuất hiện trong danh sách này.

~~~json
{"status":"partial","texts":["xin chào","original"],"fallbacks":[{"source_index":1,"input":"original","output":"original","diagnostics":"unit_too_large"}]}
~~~

Có fallback thì status là partial; còn lại là success. Mapping theo source_index và representative, không ghép kết quả theo text. Unit lỗi không làm mất output hợp lệ của unit khác. Với output sai JSON/schema/count, không có kết quả chấp nhận một phần.

| HTTP | Nhóm lỗi |
|---|---|
| 413 | Body/unit count/unit length vượt cấu hình |
| 415 | Validate nhận media type không hỗ trợ |
| 422 | Request/upload sai schema, output validate sai schema/count, model/effort không hợp lệ |
| 429 | Gemini rate limit; Retry-After được chuyển thành số giây nếu có |
| 502 | Gemini authentication/config, output provider sai schema/count, completion/session không hợp lệ |
| 503 | Provider không khả dụng |
| 504 | Discovery hoặc lượt xử lý timeout |
| 499 | Peer disconnect; provider operation bị hủy |

Response lỗi có detail là mã chẩn đoán, không chứa key, source, raw output hoặc nội dung exception. Không có cơ chế Idempotency-Key: gửi request mới là một lần xử lý mới.

Các route job/result/cancel/resume/custom đã bỏ. Contract Codex và gateway thuộc [tài liệu plugin](../../plugins/translator-codex-cli/README.md).
