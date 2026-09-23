# Runtime

[Trang bắt đầu](README.md) · [API](api.md) · [Status và skip](skip-status-messages.md)

## Validation & error handling

| Condition | Location | Behavior |
|---|---|---|
| `Content-Length` vượt `MaxMultipartBytes` | Middleware trong `Program.cs` | 413 `file_too_large` trước khi đọc form. |
| Body thực tế vượt limit, kể cả chunked | `LimitedReadStream`, request body limit | 413; code tùy điểm bắt lỗi (`request_too_large` hoặc `file_too_large`). |
| POST import/export/sheets/slides không phải multipart | Middleware trong `Program.cs` | 415 `unsupported_media_type`. |
| Hết permit xử lý | Rate limiter `file-processing` | 429 `request_limit_exceeded`, queue bằng 0. |
| Model binding lỗi, ví dụ `debug` không phải boolean | `InvalidModelStateResponseFactory` | 400 `invalid_request`; lỗi được nhận diện là vượt limit trả 413 `file_too_large`. |
| Thiếu file / sai extension | 5 Controller, discovery actions | 400 `missing_file` / 415 `unsupported_file_type`. |
| Thiếu/sai `texts` | `TranslationInputParser.TryParseAsync()` | 400 `missing_texts`, `invalid_json`, `invalid_texts`; vượt `MaxUnits`: 413 `too_many_units`. |
| Selection sai / ID không tồn tại | `SelectionInput.Parse()` / extractor | 400 `invalid_selection` / 422 `unknown_selection_id`. |
| Markdown/TXT không phải UTF-8 | `Utf8TextReader.ReadAsync()` | 422 `invalid_encoding`. |
| Số `texts` không khớp unit | Applier/Service/`OfficeTextCodec.ValidateAndDecode()` | 422 `translation_count_mismatch`. |
| Source/unit/translation/output vượt quota | Reader, extractor, codec, applier, output session | 413 với code trong danh sách dưới. |
| Office package hỏng / khác format | `OfficePackageReader.ReadAsync()`, Office Services | 422 `invalid_office_package` / `office_format_mismatch`. |
| OLE, signed package, Strict OOXML | `OfficePackageReader.ReadAsync()` | 422 `office_unsupported_content`. |
| Output Office đổi ngoài edit mask / sai schema/cấu trúc | `OfficePackageValidator`, structure validators | 422 `office_output_invalid`. |
| Token, text hoặc cấu trúc unit không apply được nhưng cô lập được | Codec/applier | Giữ unit nguồn, warning trong `metadata.skipped`; tác vụ vẫn có thể trả 200. |
| Markdown patch overlap / cấu trúc cuối không hợp lệ | `MarkdownTranslationApplier`, `MarkdownExtractor` | 422 `patch_conflict` / `invalid_structure`. |
| Exception vượt ra ngoài xử lý Service/Controller | `GlobalExceptionHandler.TryHandleAsync()` | Limit được nhận diện: 413 `request_too_large`; còn lại: 500 `internal_error`. |

`ControllerErrorMapper.ToActionResult()` ánh xạ các code sau thành 413:

```text
file_too_large, too_many_units, translation_too_long, output_too_large,
office_package_limit_exceeded, office_plan_limit_exceeded,
office_translation_limit_exceeded, office_schema_limit_exceeded
```

Các lỗi Service khác qua mapper là 422. Lỗi request được Controller xử lý trực tiếp có status riêng trong bảng. `errors` chứa lỗi fatal; không suy status chỉ từ code trong `metadata.skipped`.

```text
Lỗi xử lý dự kiến → Service result.Errors → ControllerErrorMapper → JSON { metadata, errors }
Exception chưa xử lý → UseExceptionHandler → GlobalExceptionHandler → FileResponses.Failure → JSON
```

Fatal không trả file hoặc units attachment. Metadata giữ thông tin đã thu thập, có thể chưa đủ mapping; discovery dùng `DiscoveryMetadata`. `FileResponses.Failure()` xác định format từ request path. Exception handler không gửi chi tiết exception ngoài dự kiến trong response.

## Configuration

Nguồn: [appsettings.json](../src/FileHandler.Api/appsettings.json), [FileHandlingOptions.cs](../src/FileHandler.Api/Common/FileHandlingOptions.cs), [OfficeProcessingOptions.cs](../src/FileHandler.Api/Modules/Office/OfficeProcessingOptions.cs), [Program.cs](../src/FileHandler.Api/Program.cs).

“Default” dưới đây là giá trị trong `appsettings.json` khi không bị override. Cột ghi chú chỉ ra các fallback trong class khác với file cấu hình. Đơn vị ký tự trong giới hạn string là UTF-16 code unit.

| Key | Default | Purpose | Used by |
|---|---|---|---|
| `FileHandling:MaxFileBytes` | 26214400 byte (25 MiB) | Giới hạn source; fallback class là 5242880 byte. | `Utf8TextReader`, Office Services qua `LimitedReadStream`. |
| `FileHandling:MaxMultipartBytes` | 26214400 byte (25 MiB) | Toàn bộ request gồm source, translations và multipart overhead. | Middleware. |
| `FileHandling:MaxUnits` | 10000 | Số unit nguồn và số phần tử bản dịch. | Parser, segmenter, extractors, Services. |
| `FileHandling:MaxConcurrentRequests` | 8 | Permit chung mỗi process, clamp 1–64, không queue. | `file-processing` policy; cả discovery. |
| `FileHandling:MaxTranslationChars` | 100000 | Chiều dài mỗi translation trước decode. | TXT/Markdown/Office validation. |
| `FileHandling:MaxOutputBytes` | 20971520 byte (20 MiB) | Giới hạn bytes file output. | Services, applier, `OfficeExportSession`. |
| `OfficeProcessing:MaxPackageEntries` | 2000 | Số ZIP entry. | `OfficePackageReader`. |
| `OfficeProcessing:MaxRelationships` | 20000 | Tổng relationships. | `OfficePackageInspector`. |
| `OfficeProcessing:MaxExpandedBytes` | 104857600 byte | Tổng payload sau giải nén. | Reader, export session. |
| `OfficeProcessing:MaxPartBytes` | 33554432 byte | Payload một part. | Reader, export session. |
| `OfficeProcessing:MaxXmlCharactersPerPart` | 12000000 | Ký tự XML một part; fallback class 8000000. | Reader, SDK settings, validators. |
| `OfficeProcessing:MaxXmlNodes` | 1000000 | Tổng XML node events. | Reader. |
| `OfficeProcessing:MaxXmlDepth` | 64 | Độ sâu XML. | Reader. |
| `OfficeProcessing:MaxObjects` | 200000 | XML elements/objects và plan; fallback class 100000. | Reader, extractors, `OfficeUnitCollection`. |
| `OfficeProcessing:MaxBindings` | 100000 | Binding references của extraction. | `OfficeTemplateBuilder`, `OfficeUnitCollection`. |
| `OfficeProcessing:MaxAttributesPerElement` | 256 | Attribute trên một XML element. | Reader. |
| `OfficeProcessing:MaxPlanChars` | 2000000 | Tổng ký tự encoded text trong plan. | Extractors, `OfficeUnitCollection`. |
| `OfficeProcessing:MaxTotalTranslationChars` | 2000000 | Tổng ký tự translation Office. | `OfficeTextCodec`. |
| `OfficeProcessing:MaxTokensPerUnit` | 1024 | Số slot/anchor/token mỗi unit. | Template builder, codec, extractors. |
| `OfficeProcessing:MaxCellTextChars` | 32767 | Giới hạn decoded text trong xử lý Excel. | `OfficeTextCodec`. |
| `OfficeProcessing:MaxErrors` | 100 | Setting tương thích, phải dương; không cắt recoverable skips. | `OfficeProcessingOptions.Validate()`. |
| `OfficeProcessing:MaxSchemaErrors` | 100 | Số lỗi schema trước khi abort. | `OfficePackageValidator`. |
| `Logging:LogLevel:Default` | `Information` | Mức log chung. | Hosting logging. |
| `Logging:LogLevel:Microsoft.AspNetCore` | `Warning` | Mức log ASP.NET Core. | Hosting logging. |
| `AllowedHosts` | `*` | Host filtering configuration. | ASP.NET Core host. |

`OfficeProcessingOptions.Validate()` chạy trước `builder.Build()`: các limit phải dương và `MaxPartBytes <= MaxExpandedBytes`; sai cấu hình ném `InvalidOperationException` lúc startup. Không có configuration allowlist extension; extension được viết trực tiếp trong Controller.

`Program.cs` đặt các form limits (value length/count, multipart body/boundary/header và buffer) thành `int.MaxValue`, Kestrel `MaxRequestBodySize=null`, IIS body/buffer limit thành `int.MaxValue`. Middleware vẫn áp `MaxMultipartBytes` cho các operation nêu trên và bọc request body bằng `LimitedReadStream`.

## Logging

Host được tạo bằng `WebApplication.CreateBuilder(args)`; source không đăng ký provider riêng, dùng logging mặc định của host. Không có log Import/Export riêng trong Service. `GlobalExceptionHandler` ghi `LogError` cho exception không được nhận diện là limit, gồm tên loại exception và request identifier; không ghi text/file hoặc toàn exception vào lời gọi log này. Limit exception không đi qua nhánh log đó.

## Swagger / OpenAPI

- UI: `/swagger` hoặc `/swagger/index.html`; document: `/swagger/v1/swagger.json`.
- `UseSwagger()` và `UseSwaggerUI()` không có điều kiện environment; expose 12 action trong bảng [API](api.md#endpoint-summary).
- `ExportOperationFilter` và `VietnameseSchemaFilter` mô tả request, response multipart và schema bằng tiếng Việt.
- `UseStaticFiles()` phục vụ `wwwroot/`: `swagger-custom.css`, `swagger-custom.js`, `swagger-multipart.js`, `swagger-response.js`, `swagger-response-worker.js`. UI có xử lý multipart, preview và download attachment; đây là static assets, không phải endpoint xử lý file.
