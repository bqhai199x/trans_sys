# FileHandler

ASP.NET Core 10 API xử lý Markdown (`.md`), TXT (`.txt`), Word (`.docx`), Excel (`.xlsx`) và PowerPoint (`.pptx`). Server không lưu phiên import. Client gửi lại **đúng file nguồn và selection** khi export.

## Chạy và kiểm thử

SDK theo `global.json`: **10.0.401**, `latestPatch`. Chạy trong thư mục `filehandler`:

```powershell
dotnet restore FileHandler.sln
dotnet build FileHandler.sln --no-restore -p:GenerateDocumentationFile=true
dotnet test FileHandler.sln --no-restore -p:GenerateDocumentationFile=true
dotnet run --project src/FileHandler.Api
```

Swagger UI: `/swagger`; OpenAPI: `/swagger/v1/swagger.json`. Mô tả dùng tiếng Việt và ngắt dòng; message lỗi/skip trong response dùng tiếng Anh ngắn gọn. `sheetIds`/`slideIds` mặc định trống: chọn phần hiển thị; `[]` nghĩa là không chọn.

Khung **Kết quả multipart** mặc định thu gọn, ẩn cùng endpoint khi đóng và có link tải riêng metadata, `units.json` hoặc tệp kết quả. Response JSON lớn cũng có link tải đầy đủ. Preview tối đa 16.384 ký tự; JSON trên 32 KB không được parse/pretty-print để làm preview. Multipart và JSON lớn được xử lý bằng Web Worker. Swagger chỉ render thông báo ngắn thay cho body lớn; header gốc nằm trong khung kết quả. API và tệp tải xuống vẫn giữ đầy đủ dữ liệu.

## Contract HTTP

`format` nhận `markdown`, `plaintext`, `word`, `excel`, `powerpoint`. Request dùng `multipart/form-data`.

| Endpoint | Field | Response thành công |
|---|---|---|
| `POST /api/{format}/import` | `file`, selection tùy chọn, `debug` tùy chọn | Mặc định JSON `{ texts, metadata, errors }`; `debug=true` trả multipart có thêm `units.json` |
| `POST /api/{format}/export` | `file`, `texts`, selection tương ứng import | `multipart/mixed`, hai part |
| `POST /api/excel/sheets` | `file` | JSON `{ sheets, metadata, errors }` |
| `POST /api/powerpoint/slides` | `file` | JSON `{ slides, metadata, errors }` |

`texts` là JSON array chuỗi, gửi bằng field hoặc upload file JSON có field name `texts`. Giữ hỗ trợ comment, trailing comma và newline thô trong chuỗi theo parser hiện có. Phần tử `null`, số, boolean, JSON hỏng và surrogate escape hỏng là lỗi request.

Export luôn trả hai part khi hoàn tất, kể cả có skip:

1. `Content-Type: application/json; charset=utf-8`, `Content-ID: <metadata>`; body `{ "metadata": ..., "errors": [] }`.
2. MIME của file gốc, `Content-ID: <file>`, `Content-Disposition: attachment`; bytes file đầu ra. Tên download là basename an toàn, giữ Unicode và extension hoa/thường.

Import có `debug=true` trả thêm skip info trong metadata và hai part:

1. `Content-Type: application/json; charset=utf-8`, `Content-ID: <metadata>`; body `{ "texts": [...], "metadata": {...}, "errors": [] }`.
2. `Content-Type: application/json; charset=utf-8`, `Content-ID: <units>`, `Content-Disposition: attachment; filename="units.json"`; body `{ "units": [{ "index": 0, "kind": "paragraph", "location": {...} }] }`.

`debug` chỉ có trên import; bỏ field hoặc gửi `false` trả JSON thông thường. `metadata.units` không xuất hiện trong JSON chính ở cả hai chế độ. `metadata.skipped` luôn trả warning; chỉ import có `debug=true` mới trả thêm info. Export và discovery không nhận debug, không trả skip info. Lỗi toàn tác vụ luôn là JSON, không đính kèm mapping kể cả đã gửi `debug=true`.

`debug=false` bỏ qua việc tạo danh sách unit public và chuỗi đường dẫn vị trí cho danh sách này ngay trong service, đồng thời không serialize hoặc gửi `units.json`. Với Office, extraction cũng không tạo `SkipMetadata` hoặc chuỗi đường dẫn chẩn đoán cho skip info bị ẩn; vẫn giữ số đếm và tọa độ tối thiểu để validation kiểm tra chính xác vùng bảo toàn. Mapping nội bộ và warning được thu thập đầy đủ. Export dùng cùng cách thu thập gọn và không tạo danh sách unit public. Cờ này điều khiển mapping chẩn đoán và skip info trong response; không bật log server hay trả thêm chi tiết exception.

Khi gọi service trực tiếp, `ImportAsync(stream, cancellationToken)` mặc định không có `Metadata.Units`. Muốn lấy mapping, dùng `ImportAsync(stream, debug: true, cancellationToken: cancellationToken)`; overload có selection cũng nhận `debug` trước `cancellationToken`.

Không có `responseMode` hoặc header `X-File-Metadata`. Writer ghi trực tiếp từng part sau khi validate xong; boundary thay đổi theo response. So sánh identity bằng bytes **file part**.

| HTTP | Ý nghĩa |
|---|---|
| 200 | Hoàn tất hoặc hoàn tất một phần |
| 400 | Thiếu field, JSON hoặc kiểu dữ liệu request sai |
| 413 | Vượt quota nguồn, multipart, mapping, bản dịch, schema hoặc output |
| 415 | Extension hoặc Content-Type không được hỗ trợ |
| 422 | Nguồn hỏng, count mismatch, ID không tồn tại hoặc lỗi xử lý toàn tác vụ |
| 429 | Hết permit xử lý đồng thời |
| 500 | Lỗi ngoài dự kiến; response không chứa nội dung exception |

Lỗi toàn tác vụ trả JSON `{ metadata, errors }`, không có file. Metadata giữ những thông tin đã thu thập; `unitCount: null` nếu chưa xác định mapping. Discovery không có `unitCount`/`units`, kể cả khi lỗi. Reverse proxy có thể trả lỗi trước khi request tới ứng dụng.

## Metadata và skip

Xem [danh mục trạng thái, skip và message](docs/skip-status-messages.md) để tra cứu mã theo định dạng, message dùng chung trong source và quy tắc hiển thị theo debug.

Metadata luôn có `format`, `status`, `skipped`, `skipCount`. Import/export có `unitCount`, tính cả unit tên sheet. Chi tiết `units: [{ index, kind, location }]` chỉ nằm trong tệp `units.json` khi import có `debug=true`. Import vẫn có khoảng index của sheet/slide; export không có các khoảng index này và không có tệp unit.

`skipCount: { "warning": 2, "info": 7 }` là tổng `count` theo severity **trước khi lọc debug**. Vì vậy info vẫn có số lượng khi chi tiết info bị ẩn. Đây là số đối tượng theo scope, không phải số unit; lỗi chỉ đếm những phần đã thu thập được. Discovery hoặc lỗi trước extraction trả hai giá trị 0 khi chưa có skip.

- `success`: không có warning; vẫn có thể có skip `info` theo lựa chọn/quy tắc.
- `partial`: có vùng chưa hỗ trợ hoặc bản dịch lỗi được giữ nguyên.
- `failed`: không hoàn tất tác vụ.

`skipped` gồm `code`, `severity` (`info`/`warning`), `stage` (`selection`/`extraction`/`translation`/`rename`), `scope`, `count`, `message`, `location`. `unitIndex` chỉ có khi đối tượng đã trở thành unit. Không cắt danh sách warning theo `MaxErrors`; info chỉ xuất hiện khi import có debug=true.

Index unit từ **0**; dòng, thứ tự sheet/slide và ordinal XML từ **1**. Word location dùng `partUri` thật và path có root, dạng `/w:document[1]/w:body[1]/w:p[2]`; không phải số trang. Excel có `sheetId`/`cellReference`; PowerPoint có `slideId`/`shapeId` và tọa độ bảng khi áp dụng.

Metadata public không có `schemaVersion`, `operation`, `sourceHash`, `appliedUnitCount`, `changedUnitCount`, `skippedUnitCount`, `paragraphCount`, `hasFrontmatter`, `hasBom`. Hash/BOM/binding vẫn dùng nội bộ để bảo toàn nguồn.

Ví dụ TXT import:

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

Với `debug=true`, JSON trên nằm trong part `<metadata>`, còn tệp `units.json` chứa:

```json
{
  "units": [
    { "index": 0, "kind": "paragraph", "location": { "line": { "start": 1, "end": 1 } } },
    { "index": 1, "kind": "paragraph", "location": { "line": { "start": 3, "end": 3 } } }
  ]
}
```

Export cùng nguồn với `texts=["Xin chào", ""]` trả HTTP 200 multipart. JSON part:

```json
{
  "metadata": {
    "format": "plaintext", "status": "partial", "unitCount": 2,
    "skipCount": { "warning": 1, "info": 0 },
    "skipped": [{
      "code": "empty_translation", "severity": "warning", "stage": "translation",
      "scope": "unit", "unitIndex": 1, "count": 1,
      "message": "Empty translation; source retained.",
      "location": { "line": { "start": 3, "end": 3 } }
    }],
    "encoding": "utf-8"
  },
  "errors": []
}
```

File part chứa `Xin chào`, separator gốc và `World` được giữ lại. Markdown thêm `newlinePolicy: "preserve"`; Office trả inventory định dạng tương ứng và vị trí nguồn.

## Discovery và lựa chọn sheet/slide

Excel dùng `sheetIds`; PowerPoint dùng `slideIds`. Field là JSON array **chuỗi ID gốc**:

- Không truyền hoặc field trống: chọn phần hiển thị.
- `[]`: không chọn phần nào, mapping rỗng.
- ID trùng được gộp; xử lý theo thứ tự nguồn.
- Chọn ID ẩn thì vẫn dịch và giữ trạng thái ẩn.
- File xuất giữ toàn bộ sheet/slide ngoài lựa chọn.

Discovery đọc topology và title cần thiết, không gọi extractor dịch. ID bắt buộc thiếu hoặc trùng làm nguồn không hợp lệ.

Ví dụ `/api/excel/sheets`:

```json
{
  "sheets": [
    { "sheetId": "7", "index": 1, "name": "Sales", "state": "visible", "kind": "worksheet", "canImport": true },
    { "sheetId": "42", "index": 2, "name": "Internal", "state": "veryHidden", "kind": "worksheet", "canImport": true }
  ],
  "metadata": { "format": "excel", "status": "success", "skipped": [], "skipCount": { "warning": 0, "info": 0 } },
  "errors": []
}
```

`veryHidden` là trạng thái Excel không cho người dùng unhide bằng hộp thoại thông thường; khác `hidden`. Chartsheet được liệt kê với `canImport: false` và được bảo toàn khi import/export.

Ví dụ `/api/powerpoint/slides`:

```json
{
  "slides": [
    { "slideId": "300", "index": 1, "title": "Overview", "hidden": false },
    { "slideId": "900", "index": 2, "title": null, "hidden": true }
  ],
  "metadata": { "format": "powerpoint", "status": "success", "skipped": [], "skipCount": { "warning": 0, "info": 0 } },
  "errors": []
}
```

Metadata import có `sheets`/`slides`, thêm `selected` và khoảng `[unitStartIndex, unitEndIndex)` cho vùng đã trích xuất. Export giữ inventory/selection nhưng bỏ khoảng này.

## Tên sheet Excel

Mỗi worksheet được chọn có unit Plain `sheetName` **đầu vùng**, kể cả sheet rỗng. Ví dụ sheet `Sales` có ô `Hello`: `texts=["Sales", "Hello"]`. Không dùng tên để nhận diện; mapping liên kết bằng `sheetId`.

Tên mới được trim, thay ký tự cấm/control bằng `_`, bỏ nháy đơn đầu/cuối, giới hạn 31 UTF-16 code unit không cắt đôi surrogate pair. Rỗng sau normalize dùng `Sheet`; `History` thêm `_`; trùng tên không phân biệt hoa thường thêm ` (2)`, ` (3)` trong giới hạn. Bản dịch rỗng trước normalize giữ tên nguồn và ghi warning. Surrogate lỗi hoặc tên sau normalize chứa ký tự XML 1.0 cấm sẽ giữ tên nguồn với `invalid_translation`; các ô hợp lệ vẫn được dịch.

Rename cập nhật qualifier qua tokenizer trong công thức ô, defined name, table formula, conditional formatting/data validation (gồm ngưỡng `cfvo` có `type="formula"`), chart formula và hyperlink nội bộ được nhận diện. Giữ string literal, tên cột trong structured reference và cấu trúc shared/array formula. Hyperlink sang workbook khác giữ nguyên `location`. Tính từ bảng tên nguồn để hỗ trợ đổi chéo tên.

Dynamic/3D/external reference, pivot và extension chưa chứng minh được an toàn khiến rename liên quan được giữ nguyên; khi không xác định chắc phạm vi ảnh hưởng, giữ các rename của workbook. Nội dung ô vẫn được dịch. `sheetNameChanges` của export ghi `sheetId`, `originalName`, `requestedName`, `finalName`, kể cả rename bị skip.

VML/control, shape liên kết ô, data consolidation và hyperlink có đích dạng `#...` trong relationship hiện chặn rename với `unsafe_sheet_reference`. Đây là cơ chế giữ tên khi chưa phân tích được các dạng tham chiếu này; workbook chỉ dùng VML cho comment cũng có thể bị chặn rename. Structured reference có ngoặc hoặc escape không xác định được an toàn cũng giữ tên nguồn.

Với tham chiếu 3D tĩnh, hệ thống nhận diện cả khoảng sheet theo thứ tự nguồn và giữ tên các sheet liên quan; sheet độc lập vẫn có thể đổi tên. Collision được tính lại sau khi quyết định tên nào phải giữ nguyên.

## Bảo toàn theo định dạng

- **TXT:** UTF-8 nghiêm ngặt, giữ separator/newline/BOM. Paragraph rỗng hoặc Unicode dịch hỏng được giữ nguồn; null/count/quota vẫn fatal.
- **Markdown:** giữ code/front matter, URL, HTML, anchor, soft/hard break và escaping. Token sai, Mermaid label lỗi, heading không thể đổi an toàn hoặc thay đổi cấu trúc định vị được sẽ skip unit. Patch chồng lấn và lỗi cấu trúc cuối không cô lập được là fatal.
- **Word:** giữ subtree SDT khóa/binding, revision, ruby, altChunk, alternate content; tiếp tục vùng độc lập. Inline được giữ bằng anchor khi an toàn. Field xuyên paragraph giữ các paragraph liên quan; biên không xác định giữ story. Duyệt các story tham chiếu theo thứ tự hiện có, không lặp header/footer dùng chung.
- **Excel:** tiếp tục ô/drawing hỗ trợ khi có chart/chartsheet/SmartArt. Bảo toàn formula, table header/totals, hàng/cột ẩn, phonetic cell và merge follower chưa hỗ trợ. Shared strings dùng copy-on-write để không sửa ô ngoài lựa chọn.
- **PowerPoint:** tiếp tục shape/table hỗ trợ khi gặp graphic frame chưa hỗ trợ; bảo toàn merge continuation, thứ tự, ID và trạng thái ẩn.

Open XML dùng `AutoSave=false`, `NoProcess`, kiểm tra package/schema, relationships, inventory và edit mask chính xác. Chỉ chấp nhận schema baseline trong vùng đã xác định được giữ nguyên; lỗi mới và sửa ngoài mask làm export thất bại. Không có thay đổi hiệu lực thì trả bytes nguồn, sau khi kiểm tra quota.

## Token dịch

Markdown/Office dùng `<ox:r0>text</ox:r0>` cho slot dịch và `<ox:k0/>` cho anchor bảo vệ. Unit một slot không anchor là Plain; TXT luôn literal. Giữ IDs/thứ tự/cấu trúc token. Trong Structured, escape `\` thành `\\`, `<` thành `\<`.

Run liền nhau cùng style/ngữ cảnh được gộp, kể cả Nhật/Latin. Khác style, hyperlink hoặc anchor giữ ranh giới. Slot riêng lẻ có thể rỗng nếu unit còn nội dung; toàn bộ unit rỗng sẽ skip. Các test vẫn kiểm tra nội dung/style/XML độc lập với mapping.

## Client chuyển đổi

Client cần đọc import envelope, giữ bản sao file gốc và selection, sửa `texts` đúng index rồi parse **multipart response bằng parser nhị phân**. Nếu cần mapping, gửi `debug=true` và đọc tệp `units.json`; mapping không còn nằm trong `metadata.units`. Không đọc toàn response thành text hoặc lưu toàn multipart thành `.xlsx/.docx/.pptx/.json`.

Ví dụ Python với `requests`, parser MIME chuẩn:

```python
import json
from email import policy
from email.parser import BytesParser
from pathlib import Path
import requests

def read_response(response):
    if response.status_code != 200:
        raise RuntimeError(response.json())
    if response.headers["Content-Type"].startswith("application/json"):
        return response.json(), {}
    message = BytesParser(policy=policy.default).parsebytes(
        ("Content-Type: " + response.headers["Content-Type"] + "\r\nMIME-Version: 1.0\r\n\r\n").encode("ascii")
        + response.content)
    parts = list(message.iter_parts())
    assert len(parts) == 2 and parts[0]["Content-ID"] == "<metadata>"
    return json.loads(parts[0].get_payload(decode=True)), {
        parts[1]["Content-ID"]: parts[1].get_payload(decode=True)
    }

source = Path("source.xlsx")
selection = {"sheetIds": json.dumps(["7"])}
with source.open("rb") as file:
    imported, attachments = read_response(requests.post(
        "http://localhost:5000/api/excel/import",
        files={"file": (source.name, file)}, data={**selection, "debug": "true"}))
Path("units.json").write_bytes(attachments["<units>"])
texts = imported["texts"]
# Cập nhật các unit cần dịch, gồm unit sheetName nếu muốn đổi tên.
with source.open("rb") as file:
    response = requests.post("http://localhost:5000/api/excel/export",
                             files={"file": (source.name, file)},
                             data={**selection, "texts": json.dumps(texts)})
result, attachments = read_response(response)
metadata = result["metadata"]
Path("translated.xlsx").write_bytes(attachments["<file>"])
```

Sau thay đổi mapping/token hoặc khi dùng file xuất làm nguồn mới, **import lại** để lấy mapping mới. Server không chứng minh lịch sử import hoặc phát hiện mọi trường hợp đảo các unit Plain của client.

## Giới hạn và hiệu năng

Giữ cấu hình `FileHandling`, `OfficeProcessing` và các giới hạn tài nguyên hiện có. `MaxConcurrentRequests` áp dụng chung cho import/export/discovery, admission trước khi đọc multipart. Warning không vô hiệu hóa quota, cancellation hoặc stream ownership.

Office tái sử dụng hash payload và baseline lỗi schema trong từng request; output vẫn được validate, hash và đối chiếu edit mask. Snapshot không giữ DOM hoặc cache giữa các request. Khi gọi lớp nguồn trực tiếp, `OfficeSource` sao chép buffer đầu vào và mỗi lần đọc `OriginalBytes` trả một bản sao độc lập.

CI tại [filehandler.yml](../.github/workflows/filehandler.yml) build với XML documentation và chạy test C# cùng JavaScript. Quy tắc comment theo [AGENTS.md](../AGENTS.md); các yêu cầu về phạm vi thành viên, thứ tự param/typeparam, nội dung và định dạng cần được rà soát khi review code.

Kiểm thử parser multipart và preview giới hạn: `node --test tests/swagger-multipart.test.cjs tests/swagger-response.test.cjs`.
