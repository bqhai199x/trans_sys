# API và data contracts

[Trang bắt đầu](README.md) · [Processing](processing.md) · [Validation & error handling](runtime.md#validation--error-handling)

## Endpoint summary

Tất cả endpoint nghiệp vụ nhận `multipart/form-data`. Mỗi Controller được gắn policy `file-processing`.

| Method | Route | Request | Response khi HTTP 200 | Purpose |
|---|---|---|---|---|
| POST | `/api/markdown/import` | `MarkdownImportRequest` | `MarkdownImportResponse` hoặc multipart khi `debug=true` | Extract Markdown. |
| POST | `/api/markdown/export` | `MarkdownExportRequest` | Metadata + file `.md` | Apply bản dịch Markdown. |
| POST | `/api/plaintext/import` | `PlainTextImportRequest` | `PlainTextImportResponse` hoặc multipart khi `debug=true` | Extract paragraph TXT. |
| POST | `/api/plaintext/export` | `PlainTextExportRequest` | Metadata + file `.txt` | Apply bản dịch TXT. |
| POST | `/api/word/import` | `WordImportRequest` | `WordImportResponse` hoặc multipart khi `debug=true` | Extract Word. |
| POST | `/api/word/export` | `WordExportRequest` | Metadata + file `.docx` | Apply bản dịch Word. |
| POST | `/api/excel/import` | `ExcelImportRequest` | `ExcelImportResponse` hoặc multipart khi `debug=true` | Extract worksheet được chọn. |
| POST | `/api/excel/export` | `ExcelExportRequest` | Metadata + file `.xlsx` | Apply bản dịch và tên sheet. |
| POST | `/api/excel/sheets` | `DiscoveryRequest` | `SheetsResponse` | Liệt kê inventory sheet. |
| POST | `/api/powerpoint/import` | `PowerPointImportRequest` | `PowerPointImportResponse` hoặc multipart khi `debug=true` | Extract slide được chọn. |
| POST | `/api/powerpoint/export` | `PowerPointExportRequest` | Metadata + file `.pptx` | Apply bản dịch slide. |
| POST | `/api/powerpoint/slides` | `DiscoveryRequest` | `SlidesResponse` | Liệt kê inventory slide. |

Route đến Controller và Service cố định theo format; Controller kiểm tra `File.FileName.EndsWith(...)`. Không có endpoint tự chọn format cho upload bất kỳ. `FileTypeDetector.TryDetect()` là utility; các action sử dụng `GetTranslatedFileName()` để tạo tên download.

## Import: `POST /api/{format}/import`

`{format}` chỉ là cách viết gọn cho 5 route cụ thể trong bảng trên.

### Request

```text
Content-Type: multipart/form-data; boundary=...
file: source file với extension đúng endpoint
debug: boolean, tùy chọn, mặc định false
sheetIds: JSON array chuỗi native ID, chỉ Excel
slideIds: JSON array chuỗi native ID, chỉ PowerPoint
```

### Processing

```text
Program.cs: giới hạn multipart → kiểm tra Content-Type → concurrency limiter
→ MarkdownController.Import() / PlainTextController.Import() /
  WordController.Import() / ExcelController.Import() / PowerPointController.Import()
→ kiểm tra file và extension → SelectionInput.Parse() nếu có selection
→ IFormFile.OpenReadStream()
→ MarkdownService.ImportAsync() / PlainTextService.ImportAsync() /
  WordService.ImportAsync() / ExcelService.ImportAsync() / PowerPointService.ImportAsync()
→ đọc nguồn → extract texts và metadata → FileMetadata.ForResponse(debug)
→ Ok(response) hoặc MultipartUnitsResult
```

Chi tiết extraction nằm trong [processing.md](processing.md).

### Response

Mặc định HTTP `200`, `application/json`: `{ "texts": [...], "metadata": {...}, "errors": [] }`.

Với `debug=true`, HTTP `200`, `multipart/mixed; boundary=filehandler_...`:

| Part | Headers | Body |
|---|---|---|
| 1 | `Content-ID: <metadata>`; `Content-Type: application/json; charset=utf-8` | Import response đầy đủ `texts`, `metadata`, `errors`. |
| 2 | `Content-ID: <units>`; JSON UTF-8; `Content-Disposition: attachment` với filename `units.json` | `{ "units": [{ "index": 0, "kind": "...", "location": {...} }] }`. |

JSON chính không có `metadata.units` trong cả hai chế độ. `debug=true` còn cho phép trả skip `info`; không làm thay đổi thứ tự `texts`. Lỗi fatal luôn trả JSON, không có attachment.

### Errors

| Condition | Result |
|---|---|
| Thiếu `file` | `400 missing_file`. |
| Extension không đúng endpoint | `415 unsupported_file_type`. |
| `sheetIds`/`slideIds` sai JSON array chuỗi | `400 invalid_selection`. |
| Selection chứa ID không có trong nguồn | `422 unknown_selection_id`. |
| Nguồn sai encoding/package hoặc vượt quota | Theo bảng [runtime](runtime.md#validation--error-handling). |

## Export: `POST /api/{format}/export`

### Request

```text
Content-Type: multipart/form-data; boundary=...
file: source file dùng để tạo mapping Import
texts: JSON array chuỗi, gửi dưới dạng text field hoặc file upload tên texts
sheetIds / slideIds: selection tương ứng lúc Import, nếu có
```

Request không có field metadata hay export options khác. Export dựng lại mapping từ file và selection được gửi; bản dịch liên kết bằng index, không bằng nội dung chuỗi.

`TranslationInputParser.TryParseAsync()` ưu tiên text field không trắng, sau đó mới đọc file part `texts` có `Length > 0`. Parser chấp nhận comment, trailing comma, JSON array bị serialize thêm một lớp string và thử chuẩn hóa newline thô trong JSON string. Mọi phần tử phải là string khác null; array rỗng hợp lệ ở bước parse, rồi được kiểm tra count với mapping nguồn.

### Processing

```text
Controller.Export()
→ kiểm tra file/extension
→ TranslationInputParser.TryParseAsync()
→ SelectionInput.Parse() với Excel/PowerPoint
→ IFormFile.OpenReadStream()
→ Service.ExportAsync(): đọc lại nguồn, extract mapping, validate và apply texts
→ validate output → ExportResult
→ FileTypeDetector.GetTranslatedFileName()
→ MultipartFileResult.ExecuteResultAsync()
```

`Controller.Export()` và `Service.ExportAsync()` ở đây là các method tương ứng của 5 cặp Controller/Service được liệt kê trong Import flow.

### Response

HTTP `200`, `Content-Type: multipart/mixed; boundary=filehandler_...`, kể cả khi `metadata.status="partial"`:

| Part | Headers | Body |
|---|---|---|
| 1 | `Content-ID: <metadata>`; `Content-Type: application/json; charset=utf-8` | `FileResponse`: `{ "metadata": {...}, "errors": [] }`. |
| 2 | `Content-ID: <file>`; `Content-Disposition: attachment` với filename an toàn | Bytes file output. |

`Content-Disposition` nằm trên **file part**. Tên file giữ basename, Unicode và chữ hoa/thường extension của upload; bỏ path và control character. Không thêm hậu tố dịch. Writer chỉ bắt đầu multipart sau khi Service đã tạo và validate output.

| File | Content-Type của file part |
|---|---|
| Markdown | `text/markdown; charset=utf-8` |
| TXT | `text/plain; charset=utf-8` |
| Word | `application/vnd.openxmlformats-officedocument.wordprocessingml.document` |
| Excel | `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet` |
| PowerPoint | `application/vnd.openxmlformats-officedocument.presentationml.presentation` |

### Errors

| Condition | Result |
|---|---|
| Thiếu `texts` | `400 missing_texts`. |
| JSON hỏng / sai kiểu / phần tử null | `400 invalid_json` hoặc `invalid_texts`. |
| Số bản dịch khác số unit | `422 translation_count_mismatch`. |
| Quota bản dịch/output bị vượt | `413`; không có file. |
| Unit rỗng hoặc token không hợp lệ, có thể giữ nguồn | `200`, warning trong `metadata.skipped`, giữ nguồn cho unit đó. |

Các lỗi chung và trường hợp fatal còn lại: [runtime](runtime.md#validation--error-handling). Rule token: [tokens.md](tokens.md).

## Discovery và selection

`POST /api/excel/sheets` và `POST /api/powerpoint/slides` chỉ nhận `file`:

```text
ExcelController.Sheets() → ExcelService.GetSheetsAsync() → OfficePackageReader.ReadAsync() → OfficeCatalog.Sheets()
PowerPointController.Slides() → PowerPointService.GetSlidesAsync() → OfficePackageReader.ReadAsync() → OfficeCatalog.Slides()
→ JSON inventory, metadata, errors
```

Không gọi translation extractor hoặc tạo mapping. Thành công trả `sheets`/`slides` ở cấp root, không có `selected`, `unitCount`, `units` hay khoảng index. Lỗi dùng `DiscoveryFailureResponse`, gồm `metadata` và `errors`. Thiếu file: 400; sai extension: 415; quota: 413; package không hợp lệ: 422.

| Selection field | Hành vi Import/Export |
|---|---|
| Bỏ qua hoặc whitespace | Chọn sheet `visible` / slide không hidden. |
| `[]` | Không chọn nội dung; mapping rỗng. |
| Array native ID | Gộp ID trùng, xử lý theo thứ tự nguồn; có thể chọn sheet/slide ẩn. |
| ID không tồn tại | Fatal `unknown_selection_id`. |

Selection không dùng index hay tên sheet. File output vẫn chứa phần ngoài selection; Excel rename có thể cập nhật reference tới sheet được đổi tên ở phần khác của workbook.

## Models / DTOs và metadata

Tên property C# giữ PascalCase; JSON dùng camelCase. Nguồn: [Contracts](../src/FileHandler.Api/Contracts/ExcelContracts.cs), [FileMetadata.cs](../src/FileHandler.Api/Common/FileMetadata.cs), [OfficeCatalogMetadata.cs](../src/FileHandler.Api/Common/OfficeCatalogMetadata.cs).

| Type | Purpose | Important fields |
|---|---|---|
| `MarkdownImportRequest`, `PlainTextImportRequest`, `WordImportRequest` | Import multipart | `File`, `Debug`. |
| `ExcelImportRequest`, `PowerPointImportRequest` | Import có selection | `File`, `Debug`, `SheetIds` / `SlideIds`. |
| `MarkdownExportRequest`, `PlainTextExportRequest`, `WordExportRequest` | Export multipart | `File`, `Texts`. |
| `ExcelExportRequest`, `PowerPointExportRequest` | Export có selection | `File`, `Texts`, `SheetIds` / `SlideIds`. |
| `MarkdownImportResponse`, `PlainTextImportResponse`, `WordImportResponse`, `ExcelImportResponse`, `PowerPointImportResponse` | Envelope Import | `Texts`, `Metadata`, `Errors`. |
| `ImportResult`, `ExportResult` | Kết quả Service nội bộ trước HTTP | `Texts` hoặc `Content`/`ContentType`; `Metadata`, `Errors`. |
| `FileResponse`, `DiscoveryFailureResponse` | Export metadata hoặc lỗi | `Metadata`, `Errors`. |
| `FileError` | Lỗi fatal | `Code`, `Message`; tùy chọn `Index`, `Line`, `Marker`. |
| `UnitsFileResponse`, `UnitMetadata` | Attachment mapping Import | `Units`; mỗi unit có `Index`, `Kind`, `Location`. |
| `SourceLocation` | Vị trí nguồn | `Line`, `PartUri`, `Path`, `SheetId`, `SlideId`, `CellReference`, `ShapeId`, `RowIndex`, `ColumnIndex`; bỏ property null. |
| `SheetsResponse`, `SlidesResponse` | Discovery | `Sheets` / `Slides`, `Metadata`, `Errors`. |
| `ExcelSelection`, `PowerPointSelection` | Selection đã parse | `SheetIds` / `SlideIds`; null là lựa chọn mặc định. |

### `FileMetadata`

Metadata được tạo khi Service đọc/extract nguồn, cập nhật khi apply và validate. Client nhận nó trong JSON hoặc part `<metadata>`; Export **không nhận lại metadata**, mà tính lại từ source/selection.

| JSON property | Nội dung / điều kiện |
|---|---|
| `format` | `markdown`, `plaintext`, `word`, `excel`, `powerpoint`. |
| `status` | `success`, `partial`, `failed`; xem [status/skip](skip-status-messages.md). |
| `unitCount` | Số unit, gồm tên worksheet; null khi chưa xác định được mapping. |
| `skipped`, `skipCount` | Vùng giữ nguồn và tổng đối tượng theo severity trước khi lọc info. |
| `units` | Có thể tồn tại trong `ImportResult.Metadata` khi `debug=true`; Controller tách sang `units.json`. |
| `newlinePolicy` | `preserve`, chỉ Markdown. |
| `encoding` | `utf-8`, chỉ TXT. |
| `sheets` | Excel: `sheetId`, `index`, `name`, `state`, `kind`, `canImport`, `selected`. |
| `slides` | PowerPoint: `slideId`, `index`, `title` (có thể null), `hidden`, `selected`. |
| `unitStartIndex`, `unitEndIndex` trong sheet/slide | Khoảng Import `[start, end)` khi vùng được extract; Export bỏ các property này. |
| `sheetNameChanges` | Excel Export: `sheetId`, `originalName`, `requestedName`, `finalName`. |

Unit index từ 0; dòng nguồn, index sheet/slide và ordinal XML từ 1. `SourceLineRange.Start`/`End` đều inclusive. `SheetMetadata.State` dùng `visible`, `hidden`, `veryHidden`; `Kind` là `worksheet`, `chartsheet` hoặc tên part SDK; hiện chỉ worksheet có `CanImport=true`. `DiscoveryMetadata` chỉ có `Format`, `Status`, `Skipped`, `SkipCount`.

Hash nguồn, BOM và XML bindings là dữ liệu processing nội bộ. Các plan quan trọng: `MarkdownExtraction`, `WordPlan`, `ExcelPlan`, `PowerPointPlan`; Office dùng `OfficeTranslationUnit` với `EncodedSource`, `Slots`, `Anchors`, `Bindings`, `Location`. `UnitMode` gồm `Plain`, `Structured`; `FileType` gồm `Markdown`, `PlainText`, `Word`, `Excel`, `PowerPoint`.

## End-to-end examples

### 1. Import TXT

File UTF-8 `hello.txt` có bytes tương ứng `Hello\n\nWorld\n`. Lệnh chạy với launch profile ở [Quick start](README.md#quick-start):

```powershell
curl.exe -F "file=@hello.txt" http://localhost:57217/api/plaintext/import
```

`PlainTextController.Import()` → `PlainTextService.ImportAsync()` → `PlainTextSegmenter.Segment()` → HTTP 200 JSON:

```json
{
  "texts": ["Hello", "World"],
  "metadata": {
    "format": "plaintext", "status": "success", "unitCount": 2,
    "skipped": [], "skipCount": { "warning": 0, "info": 0 }, "encoding": "utf-8"
  },
  "errors": []
}
```

### 2. Export cùng file

File `translations.json` chứa `["Xin chào", "Thế giới"]`:

```powershell
curl.exe -F "file=@hello.txt" -F "texts=@translations.json;type=application/json" http://localhost:57217/api/plaintext/export -o response.mime
```

`PlainTextController.Export()` → parse JSON → `PlainTextService.ExportAsync()` → HTTP 200 multipart. Part `<metadata>` chứa:

```json
{
  "metadata": {
    "format": "plaintext", "status": "success", "unitCount": 2,
    "skipped": [], "skipCount": { "warning": 0, "info": 0 }, "encoding": "utf-8"
  },
  "errors": []
}
```

Part `<file>` có filename `hello.txt`, `Content-Type: text/plain; charset=utf-8`, nội dung `Xin chào\n\nThế giới\n`. `response.mime` chứa cả multipart; bytes file nằm trong part `<file>`.

Example token/formatting Markdown: [Source → Import → Translation → Output](tokens.md#example-markdown).
