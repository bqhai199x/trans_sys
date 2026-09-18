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

Nhập **Index câu (từ 0)** trên `/debug`, ví dụ `0, 3, 12`, rồi bấm **Lưu index** trước khi gửi lại import/export. Index là vị trí trong array JSON do import trả về, trùng `errors[].index`; không phải số token, slide, sheet hay dòng. Lựa chọn áp dụng cho mọi request tiếp theo trong tiến trình hiện tại, mất khi restart. `DebugTrace.UnitIndexes` trong cấu hình cung cấp giá trị mặc định (mặc định `[]`). Request đang chạy giữ bản chụp cấu hình lúc bắt đầu.

`GET /debug/settings` trả `{ "unitIndexes": [...] }`; `PUT /debug/settings` nhận cùng cấu trúc, loại trùng và sắp xếp index. Số âm hoặc danh sách null bị từ chối. Danh sách rỗng chỉ ghi metadata, stage và kết quả request. Index không có hoặc chưa được xử lý xuất hiện trong `missingUnitIndexes`; không tự bật ghi toàn bộ tài liệu.

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

Đặt `State` ở nơi giá trị vừa thay đổi hoặc quyết định xử lý vừa được xác định; dùng tên camelCase có nghĩa. `stage` được ghi **trước** mỗi bước service: lỗi hoặc cancellation giữ lại bước cuối, không có nghĩa bước đó đã thành công. Trace mới không capture `In`/`Out` tổng quát vì các giá trị này có thể chứa cả tài liệu hoặc toàn bộ bản dịch. Ghi dữ liệu riêng của câu bằng snapshot tường minh trong scope `DebugTrace.Unit(index, phase)`.

- `unitIndex` luôn bắt đầu từ 0 và chỉ đơn vị dịch thực tế. Block chỉ có code hoặc không có text cần dịch không chiếm index. Scope vòng lặp slide/sheet độc lập với scope đơn vị dịch.
- Office ghi text span, XML thuộc tính run, fingerprint, quyết định gộp/tách, token/anchor, binding, đầu vào dịch, kết quả decode, lỗi và scalar được thay thế. Anchor dùng metadata/hash để không chụp nhầm text box có index khác. Excel ghi XML rich text của ô kể cả khi tái sử dụng template shared string.
- Markdown ghi buffer, marker, token, bản dịch và vùng thay thế của câu được chọn; không ghi toàn bộ AST, source hay buffer đầu ra tài liệu. TXT ghi source span/line, bản dịch và quyết định giữ nguyên/thay thế.

Mọi phép dựng snapshot phải nằm trong lambda `State(..., () => ...)` để không chạy khi tracing tắt, ẩn nội dung hoặc index không được chọn. Snapshot trong scope unit chỉ được chứa dữ liệu của unit đó, không truyền stream, AST hoặc cây OpenXML vào serializer.

`CaptureContent: true` ghi đầy đủ nội dung câu được chọn; đặt `false` để ẩn snapshot bằng `[Hidden]` và thông báo lỗi bằng `[Redacted]`. Trace theo index không áp dụng cắt chuỗi/collection hoặc các trần `MaxValueLength`, `MaxEvents`, `MaxTraceBytes` của trace cũ. Những cấu hình này chỉ còn dùng cho session nội bộ kiểu cũ. Lọc index diễn ra trước khi dựng snapshot, không ghi full rồi mới lọc file.

Middleware serialize và flush bất đồng bộ khi kết thúc request. Log mới có `version: 2`, `unitIndexes`, `missingUnitIndexes` và `unitIndex` trên các node liên quan; viewer vẫn đọc log cũ. Tắt `Enabled` để ngừng tạo file cho request mới. Lỗi ghi trace không làm thay đổi kết quả API. Thư mục `logs/` được Git bỏ qua; file cũ chưa tự động xóa, có thể xóa qua `/debug`.

```bash
curl -F "file=@guide.md" http://localhost:5000/import
curl -OJ -F "file=@guide.md" -F 'translatedTexts=["Bắt đầu nhanh"]' http://localhost:5000/export
```

Thành công import trả array JSON thuần. Thành công export trả `text/markdown; charset=utf-8` cho `.md`, `text/plain; charset=utf-8` cho `.txt`, hoặc MIME tương ứng cho Office (`.docx`, `.xlsx`, `.pptx`) với attachment. Chọn handler bằng extension cuối, không phân biệt hoa thường, không dựa vào MIME client gửi. Mọi lỗi trả array gồm `code`, `message` và các field định vị nếu có. HTTP 400 dùng cho multipart/JSON sai; 413 cho giới hạn tài nguyên; 415 cho extension ngoài `.md`/`.txt`/`.docx`/`.xlsx`/`.pptx` hoặc Content-Type request không được hỗ trợ; 422 cho UTF-8, count, nội dung bản dịch, marker/token hoặc mapping sai; 500 cho lỗi ngoài dự kiến. Reverse proxy có thể chặn request trước ứng dụng nên response của proxy không được ứng dụng chuẩn hóa.

## Token thống nhất Markdown và Office

Run chỉ chứa dấu cách được gộp vào text liền trước khi cùng định dạng và cùng ngữ cảnh sở hữu. Ví dụ `[.NET] [ ] [Framework/JAVA] [換装について]` trở thành `.NET Framework/JAVA換装について`, giữ nguyên dấu cách. Khoảng trắng khác style hoặc chưa có text liền trước vẫn được bảo vệ; tab và xuống dòng vẫn giữ ranh giới hiện có.

Office gộp các run liền nhau khi định dạng trực tiếp tương đương, bỏ khác biệt ngôn ngữ và metadata kiểm tra chính tả. Ví dụ sáu run `2026`, `年`, `3`, `月`, `31`, `日` cùng style được import thành `2026年3月31日` (Plain), không tự thêm khoảng trắng. Khác font, màu, đậm/nghiêng hoặc ranh giới hyperlink/anchor vẫn được giữ. Với shape PowerPoint không có placeholder hoặc style reference, phép so sánh áp dụng mặc định từ presentation, slide master, text body và paragraph trước định dạng run; thuộc tính mặc định ghi tường minh không tạo token riêng. Các ngữ cảnh khác chưa giải toàn bộ style kế thừa/theme. Sau cập nhật này cần import lại nguồn trước khi dịch/export; không tái sử dụng bản dịch theo cách chia token cũ.

Sau cập nhật hardening, client phải import lại nguồn trước khi export: soft break Markdown, whitespace và ký tự điều khiển Office được bảo vệ bằng token `k`; giữ nguyên thứ tự token `r/k`. Không tái sử dụng array dịch từ phiên bản template cũ. Office từ chối bound/locked SDT, markup compatibility không được hỗ trợ và complex field xuyên paragraph; nội dung cached field được bảo vệ.

Markdown và Office dùng cùng wire syntax; unit có một vùng dịch, không anchor, trả chuỗi Plain (ví dụ `**Hello**` import thành `Hello`). Unit phức tạp dùng `<ox:r0>text</ox:r0>` cho vùng dịch và `<ox:k0/>` cho code, HTML, hard break hoặc nội dung phải giữ. Định dạng lồng nhau nằm trong mapping nguồn, không lồng thẻ r trong chuỗi import.

Ví dụ nguồn Markdown gồm Before, chữ red in đậm, after và inline code:

```text
<ox:r0>Before </ox:r0><ox:r1>red</ox:r1><ox:r2> after </ox:r2><ox:k0/>
```

Dịch nội dung trong r, giữ nguyên IDs/thứ tự/token. Các span chỉ có whitespace được giữ bằng k nếu nằm riêng giữa các cấu trúc. Trong Structured, backslash encode thành hai backslash và `<` encode thành backslash + `<`; không tự xóa escape. Plain/TXT giữ literal, không diễn giải token. Office và Markdown cho phép r-slot riêng lẻ rỗng/chỉ whitespace khi ít nhất một slot trong unit còn nội dung; vẫn phải giữ đủ thẻ và đúng thứ tự. Nếu toàn bộ slot rỗng/chỉ whitespace, trả `empty_translation` với index của unit. Markdown vẫn escape ký tự Markdown và kiểm tra cấu trúc khi export; dấu định dạng nhấn mạnh bao quanh vùng đã rỗng được bỏ để tránh tạo dấu `**`/`*` thừa, còn link, code và anchor vẫn được bảo vệ.

**Đổi contract Markdown:** Public import dùng token `ox`. Extract/restore dùng binding có kiểu dữ liệu, không tạo rồi parse lại chuỗi `<keepme...>`; trace import/export không còn marker keepme do hệ thống sinh ra. Nội dung keepme có sẵn trong file nguồn vẫn là dữ liệu nguồn. Các đoạn liền nhau cùng định dạng và cùng phạm vi sở hữu được gộp, kể cả Nhật/Latin xen kẽ hoặc `**Track**__2__`; link khác nhau và anchor vẫn giữ ranh giới. Hãy import lại file nguồn trước khi dịch/export theo phiên bản mới; không gửi lại array token cũ. Token builders/escaping dùng chung tại `Common/TranslationTokenSyntax.cs`, không khiến Markdown phụ thuộc module Office.

**Mermaid diagram trong Markdown:** Fence `mermaid` hỗ trợ trích xuất nhãn từ:
- **Flowchart / Graph (`flowchart`, `graph`):** Nhãn node dạng shape cổ điển, nhãn cạnh (`-->|text|`, `-- text -->`, `-. text .->`, `== text ==>`) và tiêu đề `subgraph`. Hướng sơ đồ không phân biệt hoa thường.
- **Sequence Diagram (`sequenceDiagram`):** Nhãn alias của participant/actor (`participant U as Người dùng`), thông điệp mũi tên (`->>`, `-->>`, `->`, `-->`, `-x`, `--x`, `-)`, `--))`, `<<->>`, `<<-->>`), khối điều khiển (`alt`, `else`, `opt`, `loop`, `par`, `and`, `critical`, `option`, `break`), ghi chú `Note`, nhóm `box`, tiêu đề `title` và liên kết `link`.
- **State Diagram (`stateDiagram`, `stateDiagram-v2`):** Nhãn trạng thái `state "..." as s1`, transition `s1 --> s2 : text` và ghi chú `note`.
- **Class Diagram (`classDiagram`):** Ghi chú `note "..."`.
- **Entity-Relationship Diagram (`erDiagram`):** Nhãn quan hệ `entity ||--o{ entity : "text"`.

Mỗi nhãn một dòng là một unit Plain, có index và dòng nguồn riêng; ID, mũi tên, delimiter, comment (`%%`) và cấu hình giữ nguyên. Khi export, nhãn flowchart và nhãn có sẵn dấu nháy kép được đặt trong dấu nháy kép và escape ký tự đặc biệt theo entity Mermaid; nhãn thông điệp/rẽ nhánh sequence diagram không bị bao dấu nháy kép thừa và được mã hóa entity an toàn cho `;`, `#`, `"`, `<>`. Identity export giữ nguyên byte nguồn; bản dịch nhãn rỗng hoặc chứa ký tự điều khiển bị từ chối. Sau cập nhật cần import lại vì các nhãn Mermaid bổ sung unit vào thứ tự văn bản.

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
  - Quản lý Shared String Table (SST): giữ nguyên các mục và chỉ mục cũ, thêm mục mới cho ô được dịch; sheet ẩn và ô công thức tiếp tục tham chiếu nội dung cũ. Loại bỏ thuộc tính bộ đếm tùy chọn (`count`, `uniqueCount`) khi cập nhật SST.

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

- Tối đa 8 request import/export đồng thời trong mỗi process (`FileHandling:MaxConcurrentRequests`, được chặn trong khoảng 1–64). Không xếp hàng; hết suất trả HTTP 429 với `request_limit_exceeded` trước khi đọc multipart. Cấu hình theo ngân sách bộ nhớ thực tế; giới hạn này không thay thế giới hạn toàn cụm tại proxy.
- Office: tối đa 100.000 binding trên toàn bộ unit (`MaxBindings`, tính cả tham chiếu SST lặp) và 256 thuộc tính mỗi XML element (`MaxAttributesPerElement`). Vượt quota trả HTTP 413. File nhiều cấu trúc nhỏ có thể bị từ chối dù ZIP nhỏ.
- Word/PPT từ chối continuation cell của merge nếu còn văn bản (HTTP 422), tránh sửa nhầm ô không sở hữu nội dung. Excel bảo toàn sheet ẩn có ô thiếu địa chỉ; ô thiếu địa chỉ trong worksheet được chọn sửa vẫn ngoài profile hỗ trợ (HTTP 422).
- File nguồn: 5 MiB (Office: 50 MiB); multipart: 25 MiB; output: 20 MiB.
- 10.000 unit/tệp; 100.000 UTF-16 code unit/bản dịch.
- Cấu hình chung trong `FileHandling` và giới hạn gói Office trong `OfficeProcessing` của `appsettings.json`.

API chỉ nhận file và `string[]`, không lưu phiên. Vì vậy server không thể chứng minh file export giống file import trước đó; caller phải gửi đúng nguồn. Các đoạn không có marker bị đảo thứ tự cũng không thể luôn được phát hiện. Marker kiểm tra tính toàn vẹn của từng unit, không chứng minh lịch sử import.

V1 giữ code, URL, autolink và HTML inline dưới marker bảo vệ; dịch heading/paragraph cùng emphasis và nhãn link. Code fence, front matter và thematic break không sinh unit. Soft break được biểu diễn bằng `\n`; hard line break (hai space hoặc backslash) cùng newline CRLF/LF được bảo toàn nguyên vẹn; khi bản dịch thay đổi, ký tự Markdown nhạy cảm được escape.

Token public dùng r0/r1… cho vùng dịch và k0/k1… cho phần bảo vệ, đánh số riêng từ 0 trong mỗi unit; không có zero dư. Sai cú pháp, thiếu/thừa/lặp/đổi thứ tự token bị từ chối trước khi xuất file.
