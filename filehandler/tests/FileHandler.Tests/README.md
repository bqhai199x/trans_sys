# Kiểm thử FileHandler

Chạy từ thư mục `filehandler`:

```powershell
dotnet build FileHandler.sln -p:GenerateDocumentationFile=true
dotnet test FileHandler.sln --no-build
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
| MarkdownController, PlainTextController | FilesControllerTests | Import, Export, ErrorResult/Errors qua action, JSON contract, HTTP status, download |
| WordController, ExcelController, PowerPointController | OfficeControllerTests | Import, Export Office qua action, JSON contract, metadata header, download |
| OfficePackageValidator, OfficeSource, skip metadata | OfficeOptimizationTests | Snapshot độc lập với buffer của caller, cache vẫn kiểm tra output/selection/quota, gọi validation lặp, skip ẩn giữ đúng vùng bảo toàn và số đếm khi lỗi |
| ExcelRenamePlanner, ExcelFormulaReferences | ExcelRenameReviewTests, ExcelRenameCollisionTests | Structured reference, hyperlink, conditional formatting, VML/control fallback, Unicode tên sheet, suffix trùng sau truncate và tên giữ chỗ |
| GlobalExceptionHandler | GlobalExceptionHandlerTests | HTTP 413/500 và không lộ nội dung exception |

`Api/FilesApiTests.cs` kiểm tra tích hợp qua HTTP host. Bảng trên mô tả phạm vi hành vi; không phải báo cáo 100% line/branch coverage. Constructor, property và code do compiler sinh không có test riêng chỉ để tăng coverage.
