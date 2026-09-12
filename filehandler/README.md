# FileHandler

REST API ASP.NET Core 10 xử lý Markdown (`.md`) và văn bản thuần (`.txt`) theo luồng stateless: `POST /import` trả array chuỗi cần dịch; `POST /export` nhận lại đúng file nguồn và `translatedTexts` là JSON array nằm trong một field multipart, rồi trả file `.translated.md` hoặc `.translated.txt` tương ứng.

## Chạy và kiểm thử

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

File được ghi và flush khi kết thúc request session (Dispose). Tắt `Enabled` để ngừng tạo file cho request mới; request đang chạy giữ cấu hình ban đầu. Lỗi ghi trace không làm thay đổi kết quả API. Thư mục `logs/` được Git bỏ qua; file cũ chưa tự động xóa, có thể quản lý hoặc xóa qua giao diện `/debug`.

```bash
curl -F "file=@guide.md" http://localhost:5000/import
curl -OJ -F "file=@guide.md" -F 'translatedTexts=["Bắt đầu nhanh"]' http://localhost:5000/export
```

Thành công import trả array JSON thuần. Thành công export trả `text/markdown; charset=utf-8` cho `.md` hoặc `text/plain; charset=utf-8` cho `.txt`, với attachment. Chọn handler bằng extension cuối, không phân biệt hoa thường, không dựa vào MIME client gửi. Mọi lỗi trả array gồm `code`, `message` và các field định vị nếu có. HTTP 400 dùng cho multipart/JSON sai; 413 cho giới hạn tài nguyên; 415 cho extension ngoài `.md`/`.txt` hoặc Content-Type request không được hỗ trợ; 422 cho UTF-8, count, nội dung bản dịch, marker hoặc mapping sai; 500 cho lỗi ngoài dự kiến. Reverse proxy có thể chặn request trước ứng dụng nên response của proxy không được ứng dụng chuẩn hóa.

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

Output là `guide.translated.txt`:

```text
Xin chào.\r\nDòng thứ hai.\r\n\r\n# Văn bản thuần.\r\n
```

- Import giữ nguyên space/tab đầu cuối và newline nội bộ mỗi đoạn; không trim hoặc normalize Unicode.
- Export thay nguyên đoạn bằng bản dịch, không thêm escaping hoặc kiểm tra cấu trúc. Bản dịch có thể thay số dòng, thêm dòng trống hoặc chứa literal `<keepme...>`.
- BOM, newline kết thúc đoạn, các dòng trống ngăn đoạn và phần trống đầu/cuối file được giữ nguyên. Khoảng trắng bên trong đoạn thuộc nội dung dịch và được thay theo chuỗi client gửi.
- Newline trong giá trị JSON hợp lệ được chèn nguyên văn: bản dịch LF vào nguồn CRLF có thể tạo output trộn EOL. Parser JSON chịu lỗi hiện tại có thể chuyển CRLF thô bên trong string thành LF; dùng JSON escape chuẩn để giữ chính xác.
- File rỗng, chỉ BOM hoặc toàn whitespace import thành `[]`; export với `[]` giữ bytes gốc trong giới hạn output.
- Số bản dịch phải đúng số đoạn; mỗi bản dịch không được null, rỗng hoặc chỉ whitespace. Lỗi từng đoạn có `index` từ 0 và `line.start`/`line.end` từ 1. Có lỗi thì không trả file một phần.
- TXT dùng các giới hạn chung bên dưới và chỉ nhận UTF-8 nghiêm ngặt có/không BOM. Chưa hỗ trợ tự đoán encoding hoặc tự chia đoạn dài theo token/ký tự.
- Import có thể trả đoạn nguồn dài hơn `MaxTranslationChars`; export vẫn kiểm tra giới hạn này trên từng bản dịch, kể cả identity. Identity export giữ nguyên bytes khi đáp ứng mọi giới hạn.

## Giới hạn mặc định

- File nguồn: 5 MiB; multipart: 25 MiB; output: 20 MiB.
- 10.000 unit/tệp; 100.000 UTF-16 code unit/bản dịch.
- Chỉ UTF-8 nghiêm ngặt, có hoặc không BOM. BOM và bytes ngoài vùng dịch được giữ; identity export giữ nguyên toàn bộ bytes khi đáp ứng mọi giới hạn.
- Cấu hình trong `FileHandling` của `appsettings.json`.

API chỉ nhận file và `string[]`, không lưu phiên. Vì vậy server không thể chứng minh file export giống file import trước đó; caller phải gửi đúng nguồn. Các đoạn không có marker bị đảo thứ tự cũng không thể luôn được phát hiện. Marker kiểm tra tính toàn vẹn của từng unit, không chứng minh lịch sử import.

V1 giữ code, URL, autolink và HTML inline dưới marker bảo vệ; dịch heading/paragraph cùng emphasis và nhãn link. Code fence, front matter và thematic break không sinh unit. Soft break được biểu diễn bằng `\n`; hard line break (hai space hoặc backslash) cùng newline CRLF/LF được bảo toàn nguyên vẹn; khi bản dịch thay đổi, ký tự Markdown nhạy cảm được escape.

Marker ID là số nguyên dương 32-bit ở dạng thập phân chuẩn, không có zero đầu. Dạng số quá lớn được coi là marker sai cú pháp và không gây overflow.
