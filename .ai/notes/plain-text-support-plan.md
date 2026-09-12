# Kế hoạch hỗ trợ file `.txt`

**Người lập:** `[CDX]` — 2026-09-12 10:04, múi giờ `+07:00`.
**Trạng thái:** Đã hoàn thành triển khai T1–T7, cập nhật 2026-09-12 17:58 (`+07:00`).
**Yêu cầu:** Xử lý `.txt` tương tự `.md` qua import/export, không cần kỹ thuật extract phức tạp.
**Contract triển khai:** Mỗi đoạn gồm các dòng có nội dung liên tiếp là một unit; dòng trống phân cách đoạn. Người dùng yêu cầu implement sau khi xem plan/kiến trúc này; áp dụng phương án đã trình bày, không thêm chế độ chia theo dòng/toàn file.

**Kết quả:** Baseline 176 → 256 tests pass; build Release với XML documentation 0 warnings/errors; formatter không đổi. Audit 54 file C#/447 khai báo không lỗi. Smoke HTTP TXT/Markdown đúng bytes BOM/CRLF, MIME và attachment; chi tiết bàn giao tại `../handoff.md`.

## 1. Mục tiêu và phạm vi

- Dùng lại `POST /import` và `POST /export`, cùng multipart fields `file`, `translatedTexts` và error array hiện tại.
- Import `.txt` trả JSON array chuỗi theo thứ tự xuất hiện; export nhận lại nguồn cùng array bản dịch và trả `<stem>.translated.txt`.
- Dùng `IFileHandler`, `ImportResult`, `ExportResult`, `FileError`, cấu hình giới hạn và diagnostics hiện có.
- Đọc UTF-8 nghiêm ngặt, giữ BOM nếu có, giữ dữ liệu ngoài các vùng thay thế; identity export giữ nguyên bytes khi dữ liệu đáp ứng giới hạn.
- Xử lý nội dung `.txt` như văn bản thuần, kể cả khi trông giống Markdown, HTML, URL, code hoặc marker `<keepme...>`.
- Không dùng Markdig, AST, marker, escaping Markdown, bảo vệ link/code, dò heading hay kiểm tra cấu trúc cho `.txt`.
- Chưa thêm `.text`, `.log`, `.csv`, encoding ANSI/Windows-1258/UTF-16, tự đoán encoding, chia câu, chia theo token, dịch tự động, batch nhiều file, session hoặc cơ sở dữ liệu.

## 2. Hiện trạng trước triển khai đã đối chiếu source

Đường dẫn trong tài liệu tính từ root repository `D:\Projects\trans`; code nằm dưới `filehandler/`.

| Thành phần hiện có | Hiện trạng | Thay đổi cần thiết |
|---|---|---|
| `filehandler/src/FileHandler.Api/Common/IFileHandler.cs` | Có sẵn contract import/export chung | Giữ contract, thêm implementation cho `.txt` |
| `.../Common/FileType.cs` | Chỉ có `Markdown` | Thêm `PlainText`, giữ giá trị enum cũ |
| `.../Common/FileTypeDetector.cs` | Chỉ nhận `.md`; output luôn `.translated.md` | Nhận `.txt` không phân biệt hoa thường; chọn extension output theo loại đã xác định |
| `.../Controllers/FilesController.cs` | Inject và gọi trực tiếp `MarkdownService`; validation bỏ kết quả detect | Inject thêm service TXT; validation trả loại đã detect, sau đó chọn `IFileHandler` |
| `.../Modules/Markdown/MarkdownSourceReader.cs` | Đọc stream có byte limit, decode/encode UTF-8 và tạo line map | Tách phần I/O + UTF-8 dùng chung; giữ adapter và line map riêng của Markdown |
| `.../Modules/Markdown/MarkdownService.cs` | Đọc → extract → apply → validate structure → encode | Giữ workflow; TXT có workflow nhẹ riêng |
| `filehandler/src/FileHandler.Api/Program.cs` | Đăng ký Markdown service singleton, limits, trace, Swagger | Đăng ký TXT singleton; cập nhật response metadata |
| `filehandler/tests/FileHandler.Tests/Api/FilesControllerTests.cs` | Có hai assertion xem `.txt` là không hỗ trợ; helper chỉ tạo Markdown service | Đổi extension âm tính sang `.pdf`/`.exe`, cập nhật helper và thêm case TXT |
| `filehandler/tests/FileHandler.Tests/Api/DebugTraceTests.cs` | Test hiện tại kiểm tra trace Markdown như `DecodeTranslation`, `ValidateStructure` | Giữ assertion riêng của Markdown; thêm trace TXT với các bước phù hợp |

Không cần đổi DTO, route, schema `translatedTexts`, JavaScript textarea, debug viewer hoặc thêm NuGet package. Chỉ chỉnh OpenAPI filter nếu cần mô tả định dạng được hỗ trợ; phần example textarea hiện tại đã dùng được cho TXT.

**Giới hạn khảo sát:** Working tree có nhiều thay đổi từ trước. Kế hoạch dựa vào source trên disk, không dựa riêng vào HEAD. Handoff trước ghi 176 tests pass; task lập kế hoạch này chưa chạy lại build/test và không coi đó là kết quả kiểm chứng mới.

## 3. Contract xử lý TXT

### 3.1. Nhận diện file và HTTP

- Chọn handler bằng extension cuối của tên file, so sánh không phân biệt hoa thường: `.md` → Markdown, `.txt` → PlainText.
- `report.TXT` hợp lệ; `report.txt.exe`, file không extension và các extension khác tiếp tục bị từ chối.
- MIME do client gửi không quyết định parser, nhất quán với luồng hiện tại; nội dung vẫn phải vượt qua kiểm tra UTF-8 và giới hạn bytes.
- Import thành công: `200 application/json`, body là array thuần, không thêm object wrapper hay metadata.
- Export thành công: `200 text/plain; charset=utf-8`, attachment `report.translated.txt`; bỏ cả đường dẫn Windows lẫn Unix trong tên client cung cấp.
- Giữ chữ hoa/thường của stem; chuẩn hóa extension output về `.txt`. `.md` tiếp tục xuất `.translated.md`.
- API stateless: client phải gửi đúng file nguồn và đúng thứ tự array. Không có hash/session để chứng minh nguồn đã được import; không thể phát hiện mọi trường hợp đảo hai chuỗi hợp lệ.

### 3.2. Quy tắc chia unit theo đoạn

1. Quét chuỗi một lần, nhận diện ba dạng kết thúc dòng vật lý: `\r\n`, `\n`, `\r`; CRLF tính là một lần xuống dòng.
2. Dòng trống là dòng không có ký tự hoặc chỉ gồm ký tự được `.NET char.IsWhiteSpace` nhận diện. Việc nhận diện này chỉ để phân đoạn, không xóa nội dung nguồn. Ký tự Unicode khác CR/LF không tự tạo ranh giới dòng.
3. Một unit là dãy tối đa các dòng không trống liên tiếp. Dòng chỉ có số, dấu câu, ký hiệu, URL hoặc code vẫn có nội dung và vẫn tạo unit.
4. Span unit bắt đầu tại đầu dòng đầu tiên và kết thúc ngay sau ký tự nội dung cuối dòng cuối cùng, không gồm newline kết thúc dòng cuối.
5. Nội dung unit lấy nguyên substring từ nguồn: giữ space/tab ở đầu và cuối, giữ newline nội bộ chính xác; không `Trim`, không normalize Unicode hay EOL.
6. Newline kết thúc đoạn, toàn bộ dòng trống giữa đoạn, phần trống đầu/cuối file nằm ngoài unit và được giữ nguyên.
7. Không tạo unit rỗng; file rỗng, chỉ BOM hoặc toàn whitespace import thành `[]`, export với `[]` trả lại bytes gốc nếu không vượt output limit.
8. Dừng khi số unit vượt `MaxUnits`; không trả một phần danh sách.

Ví dụ nguồn `guide.txt` dưới dạng chuỗi escape để thấy rõ newline:

```text
Hello world.\r\nThis is line two.\r\n\r\n# This is plain text.\r\n
```

Kết quả import:

```json
["Hello world.\r\nThis is line two.", "# This is plain text."]
```

Export với:

```json
["Xin chào thế giới.\r\nĐây là dòng thứ hai.", "# Đây là văn bản thuần."]
```

Cho kết quả:

```text
Xin chào thế giới.\r\nĐây là dòng thứ hai.\r\n\r\n# Đây là văn bản thuần.\r\n
```

Dấu `#` là một phần chuỗi dịch, không phải heading. Chuỗi `<keepme1>`, `**bold**`, `[link](url)`, `&copy;`, backslash và code fence đều được xử lý nguyên văn.

### 3.3. Quy tắc áp dụng bản dịch

- Số bản dịch phải bằng số unit của chính nguồn gửi trong request export; mapping theo index 0-based.
- Bản dịch không được null, rỗng hoặc chỉ whitespace, tương tự Markdown. Không dùng chuỗi rỗng để xóa đoạn trong V1.
- Thay nguyên span unit bằng chuỗi dịch; không escape, không tự thêm dấu, không trim khoảng trắng, không kiểm tra marker.
- Cho phép bản dịch có số dòng khác nguồn, kể cả tạo thêm dòng trống bên trong đoạn. Nội dung bên trong vùng dịch do client kiểm soát; không áp dụng kiểm tra cấu trúc Markdown.
- Với JSON hợp lệ, newline trong chuỗi dịch được giữ đúng như giá trị sau parse. Nguồn CRLF và bản dịch LF có thể tạo output trộn EOL; V1 không tự chuyển EOL. Luồng JSON chịu lỗi đang có vẫn có thể chuyển raw CRLF thành LF trong bước chuẩn hóa của controller.
- Space/tab và indentation bên trong span unit thuộc nội dung dịch: import giữ nguyên, export dùng bản dịch cung cấp. Chỉ các khoảng nằm ngoài span được bảo toàn độc lập với bản dịch.
- BOM và các đoạn nguồn ngoài span được giữ nguyên. Nếu tất cả chuỗi dịch bằng unit gốc theo so sánh ordinal thì trả bytes gốc sau khi kiểm tra đầy đủ giới hạn.
- Export là thao tác toàn bộ hoặc thất bại: có lỗi thì `Content = null`, không trả file một phần.

### 3.4. Encoding, limits và lỗi

Dùng nguyên `FileHandlingOptions`, không bổ sung cấu hình riêng cho TXT trong V1.

| Ràng buộc/tình huống | Hành vi |
|---|---|
| `MaxFileBytes` = 5 MiB | Kiểm tra số byte thực đọc, gồm BOM, kể cả stream không seek được; lỗi `file_too_large`, HTTP 413 |
| `MaxMultipartBytes` = 25 MiB | Tái sử dụng tầng multipart hiện có cho nguồn + JSON + overhead; kiểm tra hồi quy cùng cả hai loại file |
| `MaxUnits` = 10.000 | Vượt số đoạn → `too_many_units`, HTTP 413 |
| `MaxTranslationChars` = 100.000 | Tính bằng `string.Length`/UTF-16 code units cho mỗi bản dịch; vượt → `translation_too_long`, HTTP 413 |
| `MaxOutputBytes` = 20 MiB | Tính UTF-8 bytes gồm BOM và toàn bộ dữ liệu giữ lại; vượt → `output_too_large`, HTTP 413 |
| Nguồn UTF-8 không hợp lệ | `invalid_encoding`, HTTP 422; không decode bằng replacement character, không đoán encoding |
| Thiếu file/JSON, JSON sai hoặc phần tử không phải string | Dùng parser và mã lỗi HTTP 400 hiện có |
| Extension không hỗ trợ | `unsupported_file_type`, HTTP 415; message nêu `.md` và `.txt` |
| Sai số bản dịch | `translation_count_mismatch`, HTTP 422 |
| Bản dịch rỗng/chỉ whitespace | `empty_translation`, HTTP 422, có `Index` và `Line` |
| Null hoặc surrogate không hợp lệ khi gọi service trực tiếp | `invalid_translation`, HTTP 422 khi ánh xạ qua controller; qua HTTP JSON parser vẫn trả lỗi 400 hiện có |
| Hủy request | Truyền cancellation xuyên suốt read/scan/validate/write; không chuyển cancellation thành lỗi nghiệp vụ |

`Index` bắt đầu từ 0; `Line.Start`/`Line.End` là dòng nguồn inclusive, bắt đầu từ 1. TXT không sinh field `Marker`.

Giữ phân biệt hiện có giữa giới hạn nguồn và giới hạn bản dịch: import không tự chia hoặc từ chối một đoạn chỉ vì nó dài hơn `MaxTranslationChars`. Export của đoạn đó vẫn kiểm tra độ dài bản dịch, kể cả identity. Vì vậy bảo đảm identity áp dụng khi cả nguồn, số unit, bản dịch và output đều trong giới hạn. Không âm thầm cắt nội dung hoặc thêm chunking vào V1.

## 4. Thiết kế triển khai tối thiểu

### 4.1. Chọn handler

- Thêm `FileType.PlainText` sau `Markdown`, không đổi giá trị enum Markdown hiện có.
- `FilesController` nhận `MarkdownService` và `PlainTextService` qua constructor. Helper chọn handler bằng `switch` trả `IFileHandler`; với hai loại file chưa cần registry, reflection, base class hoặc resolver interface mới.
- Điều chỉnh `ValidateUpload` trả thêm loại file đã detect (ví dụ out parameter); mỗi request detect một lần, dùng cùng kết quả để chọn handler và tên output.
- Thêm overload `GetTranslatedFileName(clientFileName, fileType)`; controller gọi overload có loại file. Giữ overload một tham số để tránh phá caller/test cũ: suy ra loại được hỗ trợ từ tên, giữ fallback `document.translated.md`/Markdown với tên thiếu hoặc không hỗ trợ như hành vi cũ. API vẫn từ chối extension không hợp lệ trước bước này.
- Giữ `IFileHandler` và hai result record hiện có. Không thêm `FileType` vào DTO hoặc yêu cầu client truyền format riêng.

### 4.2. Dùng chung phần UTF-8

- Thêm `Common/Utf8TextSource.cs`: record tối thiểu `Bytes`, `Text`, `HasBom`, không chứa AST hoặc line map Markdown.
- Thêm `Common/Utf8TextReader.cs`: chuyển phần đọc stream có giới hạn, codec UTF-8 nghiêm ngặt, BOM và encode từ reader Markdown sang đây; không thay đổi error code hay ownership stream.
- `MarkdownSourceReader` giữ các entry point đang được gọi trong source/test; delegating read/decode/encode cho helper chung, rồi tạo `MarkdownSource` với `BuildLineMap` hiện tại.
- Giữ `LineMap` trong module Markdown. Scanner TXT biết số dòng ngay khi quét, không cần chuyển toàn bộ model Markdown sang Common.
- Không để module TXT tham chiếu `Modules.Markdown`; không sao chép nguyên reader, tạo reader riêng dùng default `StreamReader`, hoặc sửa thuật toán Markdown.
- Giữ trace scope Markdown cũ khi bọc helper chung để giảm thay đổi kiểm thử/debug; thêm scope helper chung có quota/redaction như hiện tại.

### 4.3. Module PlainText

Các file mới dưới `filehandler/src/FileHandler.Api/Modules/PlainText/`:

| File | Trách nhiệm |
|---|---|
| `PlainTextModels.cs` | Record unit với `Start`, `End` exclusive, `Line`; không marker, token hay cấu trúc AST |
| `PlainTextSegmenter.cs` | Quét dòng, xác định span đoạn, kiểm tra `MaxUnits`, trả danh sách hoặc `FileError` |
| `PlainTextService.cs` | Implement `IFileHandler`; điều phối reader/segmenter, validate bản dịch, dựng output, limits và trace |

Unit chỉ giữ offset/line metadata; import lấy substring để trả array. Trong V1 các helper validate/compose có thể là private method của `PlainTextService`; không bắt buộc thêm extractor interface hay translation-applier class riêng.

Service đăng ký singleton như Markdown, chỉ giữ options/dependency bất biến theo request; mọi source/unit/buffer/translation là biến cục bộ, không cache trạng thái giữa requests.

## 5. Thuật toán import/export

### Import

1. Controller validate upload, detect `.txt`, chọn `PlainTextService`.
2. Đọc stream bằng helper chung; kiểm tra byte limit, strict UTF-8, ghi nhận BOM.
3. Scanner duyệt tiến, ghi `Start`, `End`, số dòng đầu/cuối khi đóng từng đoạn; kiểm tra cancellation định kỳ, kể cả file chỉ có một dòng rất dài.
4. Kiểm tra quota ngay khi chuẩn bị thêm unit vượt giới hạn; lỗi thì trả `Texts = []`.
5. Lấy nguyên substring cho từng unit, trả `ImportResult(texts, [])`.

### Export

1. Controller dùng parser `translatedTexts` hiện tại, bao gồm dán JSON hoặc upload file JSON; mở nguồn và gọi đúng handler.
2. Đọc và tách lại nguồn bằng cùng scanner/config như import; không dùng trạng thái import cũ.
3. Kiểm tra extraction, count, null/empty, độ dài và UTF-16 hợp lệ. Gắn index/line cho lỗi từng unit; bất kỳ lỗi nào cũng dừng xuất file.
4. Tính trước tổng bytes UTF-8 của BOM, các đoạn nguồn ngoài unit và tất cả bản dịch bằng codec nghiêm ngặt. Dùng biến tổng `long`, dừng ngay khi vượt `MaxOutputBytes`; không tạo string/output khổng lồ rồi mới kiểm tra. Không tính BOM hai lần.
5. Nếu identity và đã qua mọi validation/limit thì trả bytes nguồn.
6. Nếu có thay đổi, dựng output theo thứ tự tăng dần: append nguồn trước unit → bản dịch → nguồn giữa các unit → phần còn lại. Có thể dùng một `StringBuilder` sau bước kiểm tra byte budget, rồi encode bằng helper chung; không lặp `Replace` theo nội dung hoặc `Remove/Insert` trên toàn file cho từng unit.
7. Trả `ExportResult(bytes, "text/plain; charset=utf-8", [])`; controller đặt attachment theo `FileType.PlainText`.

Độ phức tạp mục tiêu: scan O(n), compose O(n + tổng độ dài bản dịch), metadata O(số unit). Không dùng `Split`/`ReadLine` rồi `Join` vì sẽ mất separator/EOL, và không cần streaming framework mới trong phạm vi giới hạn hiện tại.

## 6. Các bước triển khai và điều kiện hoàn thành

Triển khai theo thứ tự T1 → T2 → T3 → T4 → T5; T6–T7 hoàn tất sau khi luồng chạy được. Toàn bộ T1–T7 đã hoàn thành; bảng dưới lưu phạm vi và điều kiện hoàn thành từng bước.

| Mã | Công việc | Đầu ra và điều kiện hoàn thành |
|---|---|---|
| T1 | Ghi baseline, cố định contract chia unit và fixtures | Xem lại thay đổi có sẵn; xác nhận có phản hồi về segmentation hay không; lưu case input/import/export; chạy suite hiện tại làm baseline |
| T2 | Tách helper UTF-8 dùng chung | Thêm 2 file Common, Markdown adapter delegating; test reader/BOM/limits và toàn bộ test Markdown vẫn pass |
| T3 | Viết scanner và model TXT | Offset/line chính xác cho empty, whitespace, CR/LF/CRLF/mixed; deterministic; quota và cancellation đúng |
| T4 | Viết `PlainTextService` | Import/export đúng ví dụ, validation/error positioning đúng, identity byte-perfect trong limits, output budget kiểm tra trước dựng file |
| T5 | Tích hợp detector/controller/DI/filename | `.md` và `.txt` đi đúng service, HTTP/MIME/attachment đúng; cập nhật test từ chối `.txt` cũ và constructor helpers |
| T6 | Trace, OpenAPI, README, HTTP smoke | Trace TXT có bước thực tế; schema nêu cả hai loại output; có ví dụ `.txt`, chính sách whitespace/EOL/encoding; cả dán và upload JSON hoạt động |
| T7 | Kiểm chứng và bàn giao | Build XML docs, test toàn suite, format verify, audit comment; cập nhật `.ai/tasks.md`, `.ai/handoff.md` theo kết quả thực tế |

Phân đợt thay đổi để dễ review: (1) helper UTF-8 và hồi quy Markdown; (2) TXT scanner/service cùng test; (3) tích hợp API/docs/trace và kiểm chứng tổng thể. Không yêu cầu commit/merge/deploy trong task lập kế hoạch này.

## 7. Ma trận kiểm thử

### 7.1. Scanner và xử lý nội dung

- `"A\nB\n\nC"` → `["A\nB", "C"]`; nguồn một dòng không newline cuối vẫn có một unit.
- Dòng trống ở đầu/cuối; nhiều dòng trống liên tiếp; dòng trống chứa space/tab/Unicode whitespace; giữ separator đúng từng ký tự.
- LF, CRLF, CR đơn và mixed endings; CRLF không đếm thành hai dòng; kiểm tra span exclusive và line range.
- Tiếng Việt, emoji, CJK, ký tự tổ hợp; không normalize Unicode hoặc trim unit.
- Literal Markdown, code fence, HTML, URL, entity, `<keepme1>` và marker sai cú pháp đều là text; không có lỗi marker/structure.
- Đoạn có nội dung trùng nhau vẫn ánh xạ theo vị trí, không thay tất cả bằng `string.Replace`.
- Bản dịch nhiều/ít dòng hơn nguồn, thêm dòng trống, khác EOL; xác nhận chính sách chèn nguyên văn và giữ separator nguồn.

### 7.2. Round-trip và giới hạn

- Identity byte equality cho BOM/no BOM, mixed EOL, indentation/trailing spaces, blank prefix/suffix, EOF có/không newline, file rỗng, BOM-only, whitespace-only.
- Export thay đúng một đoạn, các bytes tương ứng với dữ liệu ngoài span không đổi; text có dấu không dùng so sánh character count thay byte count.
- Count thiếu/thừa; null khi gọi service trực tiếp; empty/whitespace; surrogate đơn lẻ; kiểm tra `Index`, `Line`, không partial output.
- Boundary chính xác bằng/vượt 1 cho `MaxFileBytes`, `MaxUnits`, `MaxTranslationChars`, `MaxOutputBytes`; tính cả 3 bytes BOM và UTF-8 nhiều byte.
- Đoạn nguồn dài hơn `MaxTranslationChars`: import vẫn được nếu đáp ứng giới hạn nguồn; export kiểm tra giới hạn bản dịch đúng contract.
- Strict UTF-8 lỗi, UTF-16 có BOM bị từ chối, stream không seek được, đọc nhiều chunk, stream không bị service đóng.
- Cancellation trước đọc, trong scan dòng dài và trong export nhiều unit; nhiều request TXT/MD chạy đồng thời không lẫn dữ liệu.
- Dữ liệu gần các limit hiện tại hoàn tất với allocation hợp lý; output vượt budget bị chặn trước khi ghép chuỗi lớn. Không đặt cam kết throughput production khi chưa benchmark.

### 7.3. HTTP, tương thích và diagnostics

- `.txt`, `.TXT`, đường dẫn client Windows/Unix; `.txt.exe`, `.pdf`, không extension; filename fallback ở helper, attachment `.translated.txt` và MIME chính xác.
- Cùng nội dung `# Hello`: `.md` import `"Hello"`, `.txt` import `"# Hello"` để chứng minh dispatch đúng.
- Import bare array; export dán JSON, upload JSON, JSON chịu lỗi hiện có; 400/413/415/422 và error array.
- Cập nhật hai test controller từng từ chối `file.txt`/`a.txt` sang extension chưa hỗ trợ; không xóa coverage từ chối định dạng.
- OpenAPI `/export` có binary `text/plain`/`text/markdown` cho HTTP 200 và `application/json` cho các response lỗi; textarea vẫn hoạt động. Metadata import/export liệt kê đủ status lỗi 413/415.
- Trace TXT có service, read, segmentation, validation, output; không yêu cầu `DecodeTranslation`/`ValidateStructure`. Không thay assertion Markdown để che lỗi hồi quy.
- `CaptureContent = false` không lộ nguồn/bản dịch qua input/state/output/error; tắt trace không evaluate snapshot; quota/cancellation vẫn hoạt động.
- Chạy lại toàn suite Markdown, gồm hard break, emphasis, marker, anchor, escaping, UTF-8, multipart, Swagger và debug.

File test dự kiến: `Common/Utf8TextReaderTests.cs`, `Common/FileTypeDetectorTests.cs`, `Modules/PlainText/PlainTextSegmenterTests.cs`, `Modules/PlainText/PlainTextServiceTests.cs`, `Api/FilesControllerTests.cs`, `Api/FilesApiTests.cs`, `Api/DebugTraceTests.cs`; bổ sung test biên riêng nếu file service test quá lớn. Tất cả dưới `filehandler/tests/FileHandler.Tests/`.

## 8. Kiểm chứng và tiêu chí nghiệm thu

Lệnh dự kiến, chạy từ `filehandler/` khi triển khai:

```powershell
dotnet restore FileHandler.sln
dotnet build FileHandler.sln -c Release --no-restore -p:GenerateDocumentationFile=true
dotnet test FileHandler.sln -c Release --no-build --no-restore
dotnet format FileHandler.sln --verify-no-changes --no-restore
```

- Tất cả test mới và hồi quy pass; ghi số test và kết quả thực tế, không sao chép kết quả handoff cũ.
- Build XML docs không có lỗi/cảnh báo mới; giữ nguyên `NoWarn` hiện có. Audit thêm field/property/private helper/enum/record param, thứ tự param, returns/generic và khoảng cách comment theo `AGENTS.md`; compiler sạch chưa đủ chứng minh tuân thủ.
- Smoke HTTP import/export `.txt` và `.md`; kiểm tra response bytes, MIME, filename. Dùng instance riêng nếu API người dùng đang chạy, không tắt process đang dùng.
- OpenAPI/debug còn hoạt động; không thêm dependency hoặc thay DTO/routes.
- README ghi rõ chia đoạn, UTF-8, các giới hạn, literal text, newline/whitespace, identity và trách nhiệm gửi đúng nguồn/thứ tự.
- Chỉ sửa file liên quan; giữ các thay đổi sẵn có, không sửa `bin/`/`obj/` thủ công. Cập nhật tasks/handoff và quyết định khi có thay đổi thiết kế thực sự.

**Hoàn thành tính năng khi:** Client có thể import TXT, dịch từng chuỗi và export đúng file `.translated.txt` qua API hiện tại; dữ liệu và lỗi tuân thủ các quy tắc ở trên, toàn bộ luồng Markdown tiếp tục hoạt động.
