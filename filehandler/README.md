# FileHandler

REST API ASP.NET Core 10 xử lý Markdown (`.md`), văn bản thuần (`.txt`) và Office Open XML (`.docx`, `.xlsx`, `.pptx`) theo luồng stateless: `POST /import` trả array chuỗi cần dịch; `POST /export` nhận lại đúng file nguồn và `translatedTexts` là JSON array nằm trong một field multipart, rồi trả file có cùng tên gốc (basename), giữ nguyên extension và chữ hoa/thường, không thêm `.translated`.

## Chạy và kiểm thử

Solution dùng .NET 10 và C# 14 theo SDK hiện tại.

```powershell
dotnet restore FileHandler.sln
dotnet test FileHandler.sln
dotnet run --project src/FileHandler.Api
```

Swagger UI ở `/swagger`; OpenAPI JSON ở `/swagger/v1/swagger.json`.
Trang quản lý và xem debug trace trực quan ở `/debug` (hoặc `/debug.html`).

## Debug trace

Bật `DebugTrace.Enabled` trong `src/FileHandler.Api/appsettings.json`, hoặc dùng biến môi trường khi chạy:

```powershell
$env:DebugTrace__Enabled = "true"
dotnet run --project src/FileHandler.Api
```

Có thể bật/tắt realtime ngay trên trang `/debug` hoặc qua API `POST /debug/toggle`.

Mỗi HTTP request tạo một file JSON có cấu trúc cây lồng nhau theo luồng gọi hàm trong `logs/debug/`, tính từ content root của API. Tên file gồm giờ UTC và ID ngẫu nhiên.

Các file log được lưu trong thư mục `logs/debug/` và có thể xem trực quan trên trang `/debug`.

Giao diện `/debug` hỗ trợ:
- Bật/tắt Debug Mode tức thì (không cần restart app).
- Xem danh sách trace log với status code, method, path, thời gian chạy.
- Tìm kiếm theo text đã extract hoặc nội dung bất kỳ trong trace.
- Lọc theo function/method đã gọi.
- Xem cây gọi hàm chi tiết (tree view) có thể expand/collapse:
  - `In`: tham số đầu vào (màu xanh dương).
  - `Out`: giá trị trả về (màu xanh lá).
  - `State`: snapshot biến và dữ liệu trung gian (màu vàng/hổ phách).
  - `Item`: vòng lặp xử lý dữ liệu (màu tím).
  - `Error`: lỗi hoặc ngoại lệ nếu có (màu đỏ).
  - `Time`: thời gian chạy từng method (ms).
- Copy / Download JSON trace.
- Xóa từng log hoặc xóa toàn bộ log.

Tracing bao phủ các method hiện có của controller, service, reader, extractor, marker codec, translation applier, line map và file type detector. Không instrument constructor, property, lambda, nội bộ .NET/Markdig hay chính hệ thống tracing. Khi thêm method mới, dùng mẫu dưới đây; không tự động instrument method mới bằng attribute.

```csharp
using var trace = DebugTrace.Enter("MyService", "Process", () => new { input });
try
{
    var result = ProcessCore(input);
    trace.State("unitCount", () => result.Count);
    return trace.Return(result);
}
catch (Exception error)
{
    trace.Error(error);
    throw;
}
```

Đặt `State` ở nơi giá trị vừa thay đổi hoặc quyết định xử lý vừa được xác định; dùng tên camelCase có nghĩa và giữ cùng tên khi cần xem chuỗi thay đổi. `stage` được ghi **trước** mỗi bước service để chỉ bước đã bắt đầu; lỗi hoặc cancellation giữ lại bước cuối, không có nghĩa bước đó đã thành công. Đầu vào/giá trị trả về đã nằm ở `In`/`Out`, không cần chụp lại toàn bộ bằng state.

- Controller ghi `fileType` sau khi nhận diện thành công, nguồn `translationInput`, tiến trình `parseMode` và JSON sau chuẩn hóa newline.
- Reader ghi `bytesRead` trước khi trả lỗi kích thước hoặc decode; `hasBom` có sẵn cả khi UTF-8 lỗi. Số byte lúc vượt giới hạn là lượng đã đọc đến khi phát hiện lỗi, có thể chưa phải toàn bộ file.
- Markdown mở `Item` trước khi xử lý từng leaf block đủ điều kiện. `Item.index` là thứ tự block được xét (từ 1), `unitIndex` là chỉ số unit được trích xuất (từ 0); block không có chữ cần dịch có `outcome: noTranslatableText` và không có `unitIndex`.
- Buffer trước/sau chỉ ghi tại `EncodeInline`, gồm text và số marker; snapshot cuối vẫn giữ buffer dở dang khi lỗi. Hàm tạo marker ghi riêng `marker` ngay sau khi đăng ký. Export ghi `tokens` trước/sau chuẩn hóa, quyết định từng unit, patch sau khi áp dụng và signature trước/sau kiểm tra cấu trúc.
- TXT ghi span/line khi chốt đoạn, quyết định identity/thay thế và chuỗi `outputBytes` gồm BOM, separator, bản dịch trước khi kiểm tra giới hạn. `unitIndex` khi vượt byte limit chỉ unit gây dừng; vượt ở separator cuối chỉ có tổng byte.

Mọi phép dựng snapshot phải nằm trong lambda `State(..., () => ...)` để giữ cơ chế bỏ qua khi tracing tắt, ẩn nội dung hoặc hết quota. Không tạo state theo từng ký tự hay chụp lặp toàn bộ buffer/dictionary ở các tầng helper.

`CaptureContent: true` ghi nội dung nguồn/bản dịch; đặt `false` để chỉ theo dõi luồng với giá trị `[Hidden]` (và `[Redacted]` cho thông báo lỗi). Object/AST được chụp theo các trường dữ liệu; stream không bị đọc thêm và lazy enumerable không bị thực thi (`[Deferred]`). Mỗi snapshot tối đa 20 phần tử/collection và có giới hạn độ sâu. `MaxValueLength` mặc định 1000 ký tự; phần bị cắt ghi `[Truncated]`. Log xuất cấu trúc cây JSON chuẩn gồm `calls`, `states`, `in`, `out`, `error`, `durationMs`. `MaxEvents` mặc định 10000 sự kiện/request; khi chạm ngưỡng, log ghi `[Truncated - MaxEvents reached]` và dừng chi tiết nhưng vẫn ghi `Result` khi hoàn tất.

Middleware serialize và flush bất đồng bộ khi kết thúc request. `MaxTraceBytes` mặc định 4 MiB (cấu hình được giới hạn trong 4 KiB–64 MiB) chặn cả capture và file JSON; khi vượt mức serialize, ghi bản tóm tắt JSON hợp lệ. Log mới có `version: 1`; danh sách không lọc nội dung chỉ đọc metadata, còn log cũ và tìm kiếm nội dung dùng đường đọc đầy đủ. Tắt `Enabled` để ngừng tạo file cho request mới; request đang chạy giữ cấu hình ban đầu. Lỗi ghi trace không làm thay đổi kết quả API. Thư mục `logs/` được Git bỏ qua; file cũ chưa tự động xóa, cần chính sách retention bên ngoài hoặc xóa qua `/debug`.

```bash
curl -F "file=@guide.md" http://localhost:5000/import
curl -OJ -F "file=@guide.md" -F 'translatedTexts=["Bắt đầu nhanh"]' http://localhost:5000/export
```

Thành công import trả array JSON thuần. Thành công export trả `text/markdown; charset=utf-8` cho `.md`, `text/plain; charset=utf-8` cho `.txt`, hoặc MIME tương ứng cho Office (`.docx`, `.xlsx`, `.pptx`) với attachment. Chọn handler bằng extension cuối, không phân biệt hoa thường, không dựa vào MIME client gửi. Mọi lỗi trả array gồm `code`, `message` và các field định vị nếu có. HTTP 400 dùng cho multipart/JSON sai; 413 cho giới hạn tài nguyên; 415 cho extension ngoài `.md`/`.txt`/`.docx`/`.xlsx`/`.pptx` hoặc Content-Type request không được hỗ trợ; 422 cho UTF-8, count, nội dung bản dịch, marker/token hoặc mapping sai; 500 cho lỗi ngoài dự kiến. Reverse proxy có thể chặn request trước ứng dụng nên response của proxy không được ứng dụng chuẩn hóa.

## Token thống nhất Markdown và Office

Sau cập nhật hardening, client phải import lại nguồn trước khi export: soft break Markdown, whitespace và ký tự điều khiển Office được bảo vệ bằng token `k`; giữ nguyên thứ tự token `r/k`. Không tái sử dụng array dịch từ phiên bản template cũ. Office từ chối bound/locked SDT, markup compatibility không được hỗ trợ và complex field xuyên paragraph; nội dung cached field được bảo vệ.

Markdown và Office dùng cùng wire syntax; unit có một vùng dịch, không anchor, trả chuỗi Plain (ví dụ `**Hello**` import thành `Hello`). Unit phức tạp dùng `<ox:r0>text</ox:r0>` cho vùng dịch và `<ox:k0/>` cho code, HTML, hard break hoặc nội dung phải giữ. Định dạng lồng nhau nằm trong mapping nguồn, không lồng thẻ r trong chuỗi import.

Ví dụ nguồn Markdown gồm Before, chữ red in đậm, after và inline code:

```text
<ox:r0>Before </ox:r0><ox:r1>red</ox:r1><ox:r2> after </ox:r2><ox:k0/>
```

Dịch nội dung trong r, giữ nguyên IDs/thứ tự/token. Các span chỉ có whitespace được giữ bằng k nếu nằm riêng giữa các cấu trúc. Trong Structured, backslash encode thành hai backslash và `<` encode thành backslash + `<`; không tự xóa escape. Plain/TXT giữ literal, không diễn giải token. Markdown vẫn escape ký tự Markdown và bảo toàn cấu trúc nguồn khi export. Mỗi r-slot không được rỗng/chỉ whitespace; lỗi trả `invalid_marker_syntax` hoặc `empty_translation` với index/line của unit.

**Đổi contract Markdown:** Public import không còn phát `<keepme...>`. Hãy import lại file nguồn trước khi dịch/export theo phiên bản mới; không gửi lại array token cũ. Marker nội bộ vẫn phục vụ restoration của parser Markdown, không là format trao đổi API. Token builders/escaping dùng chung tại `Common/TranslationTokenSyntax.cs`, không khiến Markdown phụ thuộc module Office.

**Tên tải xuống:** `guide.md`, `Guide.TXT`, `Report.DOCX`, `Budget.xlsx`, `Deck.pptx` giữ nguyên tên khi export; đường dẫn client bị loại, không thêm hậu tố. Nếu tên gốc đã là `already.translated.md` thì giữ nguyên tên đó. Tên thiếu dùng `document.{ext}`; nội dung MIME và loại file không đổi.

## Văn bản thuần TXT

Mỗi đoạn gồm các dòng có nội dung liên tiếp là một chuỗi cần dịch. Dòng rỗng hoặc chỉ chứa whitespace (space, tab, Unicode whitespace) phân cách các đoạn. Scanner nhận diện CRLF, LF và CR; không diễn giải heading, link, HTML, code, entity hay marker. Ví dụ cùng nội dung `# Hello`, `.md` import thành `["Hello"]`, còn `.txt` thành `["# Hello"]`.

Nguồn `guide.txt` (hiển thị newline bằng escape):

```text
Hello world.\r\nSecond line.\r\n\r\n# Plain text.\r\n
```

Import trả:

```json
["Hello world.\r\nSecond line.", "# Plain text."]
```

Gửi lại file nguồn cùng bản dịch theo đúng thứ tự, bằng field JSON hoặc upload file JSON:

```bash
curl -F "file=@guide.txt" http://localhost:5000/import
curl -OJ -F "file=@guide.txt" -F 'translatedTexts=["Xin chào.\r\nDòng thứ hai.","# Văn bản thuần."]' http://localhost:5000/export
curl -OJ -F "file=@guide.txt" -F "translatedTexts=@translations.json;type=application/json" http://localhost:5000/export
```

Output là `guide.txt`:

```text
Xin chào.\r\nDòng thứ hai.\r\n\r\n# Văn bản thuần.\r\n
```

- Import giữ nguyên space/tab đầu cuối và newline nội bộ mỗi đoạn; không trim hoặc normalize Unicode.
- Export thay nguyên đoạn bằng bản dịch, không thêm escaping hoặc kiểm tra cấu trúc. Bản dịch có thể thay số dòng, thêm dòng trống hoặc chứa literal `<ox:r0>...</ox:r0>`; TXT luôn là Plain, không parse token.
- BOM, newline kết thúc đoạn, các dòng trống ngăn đoạn và phần trống đầu/cuối file được giữ nguyên. Khoảng trắng bên trong đoạn thuộc nội dung dịch và được thay theo chuỗi client gửi.
- Newline trong giá trị JSON hợp lệ được chèn nguyên văn: bản dịch LF vào nguồn CRLF có thể tạo output trộn EOL. Parser JSON chịu lỗi hiện tại có thể chuyển CRLF thô bên trong string thành LF; dùng JSON escape chuẩn để giữ chính xác.
- File rỗng, chỉ BOM hoặc toàn whitespace import thành `[]`; export với `[]` giữ bytes gốc trong giới hạn output.
- Số bản dịch phải đúng số đoạn; mỗi bản dịch không được null, rỗng hoặc chỉ whitespace. Lỗi từng đoạn có `index` từ 0 và `line.start`/`line.end` từ 1. Có lỗi thì không trả file một phần.
- TXT dùng các giới hạn chung bên dưới và chỉ nhận UTF-8 nghiêm ngặt có/không BOM. Chưa hỗ trợ tự đoán encoding hoặc tự chia đoạn dài theo token/ký tự.
- Import có thể trả đoạn nguồn dài hơn `MaxTranslationChars`; export vẫn kiểm tra giới hạn này trên từng bản dịch, kể cả identity. Identity export giữ nguyên bytes khi đáp ứng mọi giới hạn.

## Office Open XML (.docx, .xlsx, .pptx)

Hỗ trợ tài liệu Microsoft Word (`.docx`), Excel (`.xlsx`) và PowerPoint (`.pptx`) tuân thủ chuẩn ISO/IEC 29500 Transitional Profile (office-v1).

- **Word (.docx)**:
  - Trích xuất paragraphs trong Body, Headers/Footers theo section, Footnotes và Endnotes.
  - Bảng (`w:tbl`): duyệt cell theo hàng/cột (kể cả bảng lồng nhau theo thứ tự Outer, Nested, After, Right). Bỏ qua cell tiếp nối merge dọc rỗng và cell chỉ chứa paragraph rỗng.
  - Đoạn văn bản có nhiều runs formatting khác nhau được mã hóa bằng token canonical: `<ox:r0>Run 1</ox:r0><ox:r1>Run 2</ox:r1>`.
  - Phân cách dòng mềm/cứng: `<ox:k0/>` (Break), `<ox:k1/>` (Cr), `<ox:k2/>` (Tab), `<ox:k3/>` (NoBreakHyphen), `<ox:k4/>` (SoftHyphen), `<ox:k5/>` (Sym).
  - Khối trường (`w:fldSimple`, `w:fldChar`): chỉ dịch kết quả hiển thị của trường an toàn; giữ nguyên instruction và field codes.

- **Excel (.xlsx)**:
  - Trích xuất các ô chuỗi ký tự (`SharedStringTable` và `inlineStr`) trong các sheet hiển thị (`Visible`).
  - Bỏ qua sheet ẩn (`Hidden`, `VeryHidden`), hàng và cột ẩn. Workbook có `Chartsheet` ngoài profile hiện tại bị từ chối.
  - Bỏ qua ô công thức, ô số, ô ngày tháng, boolean và error.
  - Bảo vệ tiêu đề bảng Excel (`Table` / `ListObject`): các ô thuộc header row và `TableColumn.Name` được giữ nguyên, không trích xuất unit.
  - Quản lý Shared String Table (SST): giải chỉ mục, tạo mới SST sạch cho bản dịch, tự động loại bỏ các thuộc tính bộ đếm tùy chọn (`count`, `uniqueCount`) theo đặc tả OpenXML.

- **PowerPoint (.pptx)**:
  - Trích xuất văn bản trong slide shapes (`p:sp`) và bảng DrawingML (`a:tbl`).
  - Bỏ qua slide ẩn (`show="0"`), slide layouts và master slides.
  - Hỗ trợ cell chứa rich text runs và line breaks (`a:br` -> `<ox:k0/>`).
  - Bỏ qua ô tiếp nối của ô merge ngang/dọc (`hMerge="1"` / `vMerge="1"`).

- **Quy tắc bảo toàn và tính bất biến**:
  - File nguồn được mở hoàn toàn Read-Only với `AutoSave = false` và chế độ tương thích markup `NoProcess`.
  - Identity export (nội dung dịch giống hệt văn bản trích xuất) đảm bảo trả về chính xác 100% từng byte của file gốc.
  - Xác thực hai lớp sau khi xuất: Open XML SDK package validation và structure topology validation đảm bảo file đích hợp lệ và không bị hỏng hóc.

## Giới hạn mặc định

- File nguồn: 5 MiB (Office: 50 MiB); multipart: 25 MiB; output: 20 MiB.
- 10.000 unit/tệp; 100.000 UTF-16 code unit/bản dịch.
- Cấu hình chung trong `FileHandling` và giới hạn gói Office trong `OfficeProcessing` của `appsettings.json`.

API chỉ nhận file và `string[]`, không lưu phiên. Vì vậy server không thể chứng minh file export giống file import trước đó; caller phải gửi đúng nguồn. Các đoạn không có marker bị đảo thứ tự cũng không thể luôn được phát hiện. Marker kiểm tra tính toàn vẹn của từng unit, không chứng minh lịch sử import.

V1 giữ code, URL, autolink và HTML inline dưới marker bảo vệ; dịch heading/paragraph cùng emphasis và nhãn link. Code fence, front matter và thematic break không sinh unit. Soft break được biểu diễn bằng `\n`; hard line break (hai space hoặc backslash) cùng newline CRLF/LF được bảo toàn nguyên vẹn; khi bản dịch thay đổi, ký tự Markdown nhạy cảm được escape.

Token public dùng r0/r1… cho vùng dịch và k0/k1… cho phần bảo vệ, đánh số riêng từ 0 trong mỗi unit; không có zero dư. Sai cú pháp, thiếu/thừa/lặp/đổi thứ tự token bị từ chối trước khi xuất file.
