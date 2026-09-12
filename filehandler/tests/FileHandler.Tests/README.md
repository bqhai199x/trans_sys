# Kiểm thử FileHandler

Chạy từ thư mục `filehandler`:

```powershell
dotnet test FileHandler.sln
```

Các unit test gọi trực tiếp method public/internal. Method private được kiểm tra qua đầu vào và kết quả của method gọi nó, không dùng reflection hoặc đổi visibility chỉ để test.

| Thành phần | Test | Hành vi kiểm tra |
| --- | --- | --- |
| FileTypeDetector | FileTypeDetectorTests | Extension và tên download an toàn |
| Utf8TextReader | Utf8TextReaderTests | UTF-8/BOM, stream không seek, chunk nhỏ, byte limits, stream ownership và cancellation |
| PlainTextSegmenter | PlainTextSegmenterTests | Chia đoạn theo dòng trống, literal syntax, CR/LF/CRLF/mixed, Unicode whitespace, spans/line ranges và quota |
| PlainTextService | PlainTextServiceTests | Import/export TXT, identity bytes, giữ separator, error locations, giới hạn UTF-16/UTF-8, output allocation và concurrency |
| MarkdownSourceReader, LineMap | MarkdownSourceReaderTests, MarkdownPrimitiveTests | UTF-8 nghiêm ngặt, BOM, vị trí dòng, giới hạn byte, cancellation |
| MarkdownMarkerCodec, MarkerAllocationContext | MarkdownMarkerCodecTests, MarkdownPrimitiveTests | Parse, marker lỗi, ID biên, cấp ID không trùng |
| MarkdownProfile, MarkdownExtractor | MarkdownExtractorTests, MarkdownProfileTests | Parse, spans, cấu trúc, formatting lồng nhau, nội dung bảo vệ, newline, giới hạn unit |
| MarkdownTranslationApplier | MarkdownTranslationApplierTests | ValidateBatch, DecodeTranslation, EscapeText thông qua Apply; validation, marker, patch, cancellation |
| MarkdownService | MarkdownServiceTests, MarkdownServiceBoundaryTests, MarkdownProfileTests | Import/export, round trip, giới hạn tài nguyên, lỗi nguồn, concurrency |
| FilesController | FilesControllerTests | Import, Export, ErrorResult/Errors qua action, JSON contract, HTTP status, download |
| DebugController | DebugControllerTests | Toggle trace, status, list logs, get detail, delete log, an toàn I/O |
| GlobalExceptionHandler | GlobalExceptionHandlerTests | HTTP 413/500 và không lộ nội dung exception |
| DebugTrace, TraceCall, TraceSession | TraceSessionTests | Ambient scope, input/state/output/error/cancel, no-op, escaping, truncation, dispose |
| TraceValue | TraceValueTests | Snapshot qua Format, collections, giới hạn, lazy sequence, stream, HTTP result |
| DebugTraceMiddleware | DebugTraceMiddlewareTests | Bật/tắt, lỗi tạo file, lỗi downstream, khôi phục context; Warn với logger lỗi |
| TraceStatePlacement | TraceStatePlacementTests | Thứ tự và cấu trúc state trong log JSON, item grouping, reverse patch, buffer snapshot |

`Api/FilesApiTests.cs` và `Api/DebugTraceTests.cs` kiểm tra tích hợp qua HTTP host. Bảng trên mô tả phạm vi hành vi; không phải báo cáo 100% line/branch coverage. Constructor, property và code do compiler sinh không có test riêng chỉ để tăng coverage.
