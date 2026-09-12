# Review và đặt lại trace state

### [CDX] 2026-09-12 18:20 — Hoàn thành (`+07:00`)

## Phạm vi

Rà soát toàn bộ call site `State` trong source, scope bao quanh và luồng gọi từ `/import`, `/export` qua controller, detector, UTF-8 reader, Markdown và PlainText. Đối chiếu cách snapshot, lazy factory, redaction, quota, restore scope, JSON schema và nhóm State của viewer. Dùng working tree hiện tại làm baseline; giữ các thay đổi sẵn có.

## Các vấn đề đã xử lý

| Vấn đề trước sửa | Điều chỉnh |
| --- | --- |
| Detector ghi `default(FileType)` thành `Markdown` khi chưa biết extension, kể cả file không hỗ trợ. | Bỏ state mặc định; ghi extension thực tế và chỉ ghi `fileType` khi nhận diện thành công. |
| Item extraction Markdown được mở sau `EncodeContainer`/line mapping, khiến các call xử lý nằm ngoài item. | Mở item trước xử lý leaf block; có thông tin block, `unitIndex`, unit và lý do không sinh unit. |
| Buffer và toàn bộ dictionary marker bị chụp lại ở cả dispatcher lẫn các helper. | Chỉ giữ buffer trước/sau tại `EncodeInline`, gồm text và marker count; helper ghi marker vừa đăng ký. Allocator ghi next candidate và input count thay vì lặp toàn bộ reserved set. |
| `Decoded` lặp lại dữ liệu trong `Out` và cả adapter Markdown/shared reader. | Dùng `Out` cho kết quả decode; ghi `hasBom` trước decode để có metadata cả khi UTF-8 lỗi. |
| Thiếu thông tin tại các nhánh đọc quá kích thước, output TXT vượt limit ngay trong loop, hoặc segmentation vượt quota. | Ghi bytes đã đọc, chuỗi output bytes gồm BOM/separator, unit index gây dừng và dòng bắt đầu đoạn vượt quota trước khi trả lỗi. |
| State export Markdown không có tên và không phân biệt identity, bản dịch lỗi, patch mới được chuẩn bị hay đã áp dụng. | State có tên camelCase; ghi outcome từng unit, lỗi heading, marker validation/partial output, conflict và patch sau khi áp dụng theo thứ tự offset giảm dần. |
| Không thấy token trước/sau canonicalization hay signature ở bước đối chiếu cấu trúc. | Snapshot cùng tên theo tiến trình để viewer so sánh; không ghi per-character. |
| Controller không thể hiện nguồn JSON field/upload và nhánh sửa newline; service khó xác định bước đã bắt đầu khi dừng sớm/cancel. | Thêm `translationInput`, `parseMode`, JSON đã normalize và `stage` trước các bước xử lý. |
| TXT import chỉ tạo item khi copy text ra array, thiếu span/line ở bước xác định đoạn. | Chuyển item sang lúc chốt đoạn; validation/compose có unit context, lỗi và outcome riêng. |

## Quy ước áp dụng

- `In`/`Out` giữ đầu vào/kết quả của call. State tập trung vào giá trị trung gian, thay đổi và quyết định cần giải thích.
- Tên state camelCase có nghĩa, cùng tên khi thể hiện tiến trình. Không dùng tên rỗng ở call site nghiệp vụ; API overload cũ vẫn tồn tại để tương thích.
- `stage` ghi trước bước xử lý, thể hiện bước đã bắt đầu. Việc thành công/thất bại đọc từ `Out`/`Error`; không ghi state giả cho bước chưa chạy.
- Markdown extraction: item index từ 1 đếm leaf block đủ điều kiện được xét; unit index từ 0 tương ứng thứ tự unit. Block không có text dịch không có unit index. Unit count trên lỗi quota là số unit trung gian tại lúc phát hiện vượt giới hạn; response vẫn không trả unit một phần.
- Snapshot cuối buffer tại `EncodeInline` cũng lưu buffer dở dang khi exception; không diễn giải nó là output thành công.
- TXT `outputBytes` trong vòng lặp là tổng đã tính tới thời điểm đó, không phải luôn là kích thước hoàn chỉnh. Reader `bytesRead` khi lỗi là lượng đã đọc trước khi dừng, không đoán tổng chiều dài stream.
- Factory phải lazy; mọi object/collection dùng riêng cho trace được dựng trong lambda. Giữ redaction và hard quota hiện có, không tự tăng `MaxEvents` để che trace dài.

## File source thay đổi

- `filehandler/src/FileHandler.Api/Common/FileTypeDetector.cs`
- `filehandler/src/FileHandler.Api/Common/Utf8TextReader.cs`
- `filehandler/src/FileHandler.Api/Controllers/FilesController.cs`
- `filehandler/src/FileHandler.Api/Modules/Markdown/MarkdownExtractor.cs`
- `filehandler/src/FileHandler.Api/Modules/Markdown/MarkdownMarkerCodec.cs`
- `filehandler/src/FileHandler.Api/Modules/Markdown/MarkdownService.cs`
- `filehandler/src/FileHandler.Api/Modules/Markdown/MarkdownSourceReader.cs`
- `filehandler/src/FileHandler.Api/Modules/Markdown/MarkdownTranslationApplier.cs`
- `filehandler/src/FileHandler.Api/Modules/PlainText/PlainTextSegmenter.cs`
- `filehandler/src/FileHandler.Api/Modules/PlainText/PlainTextService.cs`

## Kiểm chứng

- Baseline: 256/256 tests pass.
- Thêm 18 case trong `Diagnostics/TraceStatePlacementTests.cs`, đọc JSON đã persist và assert scope, thứ tự/value snapshots, buffer bất biến, marker, patch order, BOM/multibyte/separator, quota, identity, normalization/redaction và cancellation.
- Test API cũ cập nhật assertion `Decoded` sang state `unit` đã thay thế.
- Khi chạy toàn bộ, phát hiện test toggle `DebugTrace.EnabledOverride` có thể làm test HTTP khác không tạo log. Đưa riêng `DebugControllerTests` vào collection `DisableParallelization = true`; không tắt concurrency của ứng dụng hoặc toàn bộ bộ test.
- Cuối cùng: `dotnet test FileHandler.sln -c Release --no-restore -p:GenerateDocumentationFile=true` — **274/274 pass**, không skipped/failed.
- `dotnet build FileHandler.sln -c Release --no-restore -p:GenerateDocumentationFile=true` — **0 warning, 0 error**.
- `dotnet format FileHandler.sln --verify-no-changes --no-restore` với quyền đầy đủ — exit 0, không warning/thay đổi.
- Audit Roslyn + XML của **56 file C# / 465 khai báo** — không lỗi syntax, thiếu summary/returns/params/typeparams, tên/thứ tự tham số, nội dung thẻ, layout hoặc khoảng cách dòng. Bao gồm private members, fields, enum/member, indexer, record positional và primary constructor.
- `git diff --check` sạch; file test mới không có trailing whitespace.

## Giới hạn và bàn giao

Không đổi thuật toán import/export, HTTP contract, cấu trúc JSON trace hoặc viewer. State name và vị trí item thay đổi cho log mới; log cũ vẫn có thể đọc bởi viewer vì schema giữ nguyên. Không chạy browser smoke mới hoặc benchmark tải; kiểm chứng end-to-end qua integration tests và assertion JSON. Build/test cần quyền phù hợp cho cache `obj`; không còn blocker.
