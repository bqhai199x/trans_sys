# Development và source navigation

[Trang bắt đầu](README.md) · [API](api.md) · [Processing](processing.md)

## Project structure

```text
filehandler/
├── FileHandler.sln
├── global.json
├── Directory.Build.props
├── NuGet.Config
├── src/FileHandler.Api/
│   ├── Program.cs
│   ├── FileHandler.Api.csproj
│   ├── Controllers/
│   ├── Contracts/
│   ├── Common/
│   ├── Modules/{Markdown,PlainText,Office,Word,Excel,PowerPoint}/
│   ├── OpenApi/
│   ├── wwwroot/
│   ├── appsettings.json
│   └── Properties/launchSettings.json
├── tests/
│   ├── FileHandler.Tests/{Api,Common,Modules,Fixtures}/
│   ├── swagger-multipart.test.cjs
│   └── swagger-response.test.cjs
└── docs/
```

| Path | Responsibility |
|---|---|
| `src/FileHandler.Api/Program.cs` | DI, options, request limits, exception handler, limiter, Swagger và routes. |
| `src/FileHandler.Api/Controllers/` | 5 Controller validate multipart và gọi Service tương ứng. |
| `src/FileHandler.Api/Contracts/` | Request/response theo format và discovery upload. |
| `src/FileHandler.Api/Common/` | Metadata/error contract, token parser, UTF-8, limits và multipart response. |
| `src/FileHandler.Api/Modules/` | Extraction, application và validation theo format; `Office/` chứa package/token/binding dùng chung. |
| `src/FileHandler.Api/OpenApi/`, `wwwroot/` | Schema/operation descriptions và Swagger UI assets. |
| `tests/FileHandler.Tests/` | Unit test, HTTP integration test, fixture builders. |
| `tests/*.test.cjs` | Test multipart parser và response preview của Swagger. |

## Build & Run

- [global.json](../global.json): SDK `10.0.401`, `rollForward: latestPatch`.
- API và test target `net10.0`; [Directory.Build.props](../Directory.Build.props) bật nullable, implicit usings, deterministic build và warnings-as-errors.
- API dependencies: `DocumentFormat.OpenXml 3.5.1`, `Markdig 1.3.1`, `Swashbuckle.AspNetCore 10.2.3`.
- Test dependencies: `Microsoft.AspNetCore.Mvc.Testing 10.0.12`, `Microsoft.NET.Test.Sdk 18.0.1`, `xunit.v3 3.2.2`, `xunit.runner.visualstudio 3.1.5`.

Chạy từ `filehandler/`:

```powershell
dotnet restore FileHandler.sln
dotnet build FileHandler.sln --no-restore -p:GenerateDocumentationFile=true
dotnet test FileHandler.sln --no-build --no-restore
dotnet run --project src/FileHandler.Api --launch-profile FileHandler.Api
```

XML documentation được bật ở command build; giữ `NoWarn` hiện có của project. Launch profile đặt `ASPNETCORE_ENVIRONMENT=Development`, URL `https://localhost:57216;http://localhost:57217`, `launchBrowser=true`. Swagger tại `/swagger`, OpenAPI tại `/swagger/v1/swagger.json`.

JavaScript tests dùng Node.js built-in test runner:

```powershell
node --test tests/swagger-multipart.test.cjs tests/swagger-response.test.cjs
```

Không xác định được từ source code hiện tại version Node.js được pin trong phạm vi `filehandler/`.

## Tests hiện có

Bảng mô tả test source, không phải báo cáo tỷ lệ coverage. Các class nằm dưới [tests/FileHandler.Tests](../tests/FileHandler.Tests); fixture Office được dựng bằng `OfficeFixtureFactory` và `OfficeTranslationTestData`.

| Test class / file | Target | Main scenarios |
|---|---|---|
| `FilesControllerTests`, `OfficeControllerTests` | Controller actions | File/extension, texts JSON, result/error mapping, filename, cancellation. |
| `FilesApiTests` | HTTP host qua `WebApplicationFactory<Program>` | Import/export, multipart, upload JSON, UTF-8, quota, Swagger, token round trip. |
| `DiscoveryApiTests` | Sheets/slides HTTP | Inventory không gọi extractor, selection shape/native ID, lỗi không có mapping. |
| `OptionalUnitsApiTests`, `SkipCountTests` | Metadata projection | `debug`, `units.json`, info/warning, count trước lọc, fatal JSON. |
| `FileConcurrencyTests` | Limiter | Permit chung cho Import/Export/discovery, từ chối khi đầy và giải phóng permit. |
| `MultipartFileResultTests`, `GlobalExceptionHandlerTests` | Response/error writer | Binary/Unicode filename, cancellation; 413/500 không lộ exception text. |
| `FileTypeDetectorTests`, `Utf8TextReaderTests` | Common utilities | Extension/basename; strict UTF-8, BOM, byte quota, non-seek stream, cancellation. |
| `TranslationTokenParserTests`, `SelfDescribingTokenTests`, `CompactTokenTests` | Token | Syntax/ID/boundary, literal collision, di chuyển anchor và compact/restore. |
| `PlainTextSegmenterTests`, `PlainTextServiceTests` | TXT | Paragraph spans, newline/whitespace, literal text, identity bytes, lỗi unit/quota, concurrency. |
| `MarkdownSourceReaderTests`, `MarkdownPrimitiveTests` | Markdown nguồn/vị trí | UTF-8/BOM, line ranges, marker allocation, byte limits. |
| `MarkdownExtractorTests`, `MarkdownProfileTests` | Extraction/profile | Inline/protected content, nested formatting, pipeline, internal link, structure. |
| `MarkdownTranslationApplierTests`, `MarkdownServiceTests`, `MarkdownServiceBoundaryTests` | Markdown export | Restore/escape, newline, token lỗi, source fallback, output limits, identity. |
| `MarkdownUnifiedTokenTests` | Markdown wire token | Reorder/style, empty slot, HTML, literal prefix, invalid token. |
| `MermaidFlowchartTests`, `MermaidSequenceTests` | Mermaid labels | Flowchart/sequence, state/class syntax, entity encoding, control characters, round trip. |
| `WordModuleTests` | Word | Simple/nested/merged table, story order, identity, count mismatch. |
| `ExcelModuleTests` | Excel | Shared/inline strings, formula/table/merge, shared-string counts, cell output, identity và count mismatch. |
| `ExcelRenameTests`, `ExcelRenameReviewTests`, `ExcelRenameCollisionTests` | Sheet rename | Formula/reference rewrite, unsafe fallback, Unicode, swapping/collision/suffix. |
| `PowerPointModuleTests` | PowerPoint | Shape/table, break/token, merge continuation, identity. |
| `MetadataAndSelectionTests` | Office metadata | Public metadata, native inventory/selection, exclusions, optional mapping và request isolation. |
| `OfficePackageReaderAndValidatorTests`, `OfficeExportSessionAndBoundaryTests` | Package processing | ZIP/package format, quotas, schema, touched/untouched parts, output boundary. |
| `OfficeTextCodecAndBindingTests`, `OfficeStyleCoalescingTests`, `WordFontHintTests` | Text slots/style | Encode/decode/reorder, missing token, same/different style, effective fonts. |
| `OfficeDeepReviewTests`, `OfficeReviewRegressionTests`, `OfficeReorderingRegressionTests` | Office preservation | Merge/hidden/exclusion, raw controls, textbox lồng, edit masks, binding quotas. |
| `OfficeOptimizationTests` | Source snapshot/validation | Buffer isolation, cached baseline, selection/quota recheck, skip counts và preserved regions. |
| `swagger-multipart.test.cjs` | Browser multipart parser | Binary bytes, Unicode filename, units attachment, malformed MIME. |
| `swagger-response.test.cjs` | Browser response preparation | Preview giới hạn, download đầy đủ, multipart/JSON lớn và worker. |

## Important classes và methods

| Class | Responsibility | Method trung tâm / Called by |
|---|---|---|
| `MarkdownController`, `PlainTextController`, `WordController`, `ExcelController`, `PowerPointController` | HTTP boundary theo format. | `Import()`, `Export()` / MVC routing. |
| `MarkdownService`, `PlainTextService`, `WordService`, `ExcelService`, `PowerPointService` | Điều phối đọc, mapping, apply, validation. | `ImportAsync()`, `ExportAsync()` / Controller tương ứng. |
| `ExcelService`, `PowerPointService` | Inventory không extract text. | `GetSheetsAsync()`, `GetSlidesAsync()` / `ExcelController.Sheets()`, `PowerPointController.Slides()`. |
| `TranslationInputParser` | Parse field/file `texts`. | `TryParseAsync()` / 5 action Export. |
| `PlainTextSegmenter` | Chia paragraph và tọa độ nguồn. | `Segment()` / `PlainTextService`. |
| `MarkdownExtractor` | Parse và tạo units, kiểm tra cấu trúc. | `Extract()`, `ValidateStructure()` / `MarkdownService`. |
| `MarkdownTranslationApplier` | Decode/restore và patch nguồn. | `Apply()` / `MarkdownService.ExportAsync()`. |
| `OfficePackageReader`, `OfficePackageInspector` | Đọc/kiểm tra ZIP/XML và inventory. | `ReadAsync()`, `Inspect()` / Office Services; discovery chỉ gọi reader. |
| `WordExtractor`, `ExcelExtractor`, `PowerPointExtractor` | Tạo plan từ package/selection. | `Analyze()` / Service tương ứng. |
| `OfficeTextCodec` | Encode token và validate/decode translations. | `Encode()` / extractor; `ValidateAndDecode()` / Office Services Export. |
| `WordTranslationApplier`, `ExcelTranslationApplier`, `PowerPointTranslationApplier` | Lập edit masks và ghi text/fragment. | `Prepare()`, `Apply()` / Service tương ứng. |
| `ExcelRenamePlanner` | Normalize tên, collision, reference edits. | `Prepare()`, `Apply()` / `ExcelService.ExportAsync()`. |
| `OfficeExportSession` | Tạo output ZIP từ part thay đổi và nguồn. | `FinalizeAsync()` / Office Services Export. |
| `OfficePackageValidator` | Schema baseline và preservation checks. | `ValidateSource()`, `ValidateOutput()` / Office Services. |
| `MultipartFileResult`, `MultipartUnitsResult` | Ghi multipart HTTP sau xử lý. | `ExecuteResultAsync()` / MVC result execution. |

Input/output và các bước của method xử lý chính được trình bày tại [processing.md](processing.md); request parsing và response writing tại [api.md](api.md).

## Source code navigation

Mọi path dưới đây tính từ `filehandler/`. Link trỏ trực tiếp vào file; tên class/method ghi đúng source.

| Muốn tìm logic | Bắt đầu tại | Sau đó xem |
|---|---|---|
| Import Markdown | [src/FileHandler.Api/Controllers/MarkdownController.cs](../src/FileHandler.Api/Controllers/MarkdownController.cs): `MarkdownController.Import()` | [src/FileHandler.Api/Modules/Markdown/MarkdownService.cs](../src/FileHandler.Api/Modules/Markdown/MarkdownService.cs): `MarkdownService.ImportAsync()`. |
| Export Markdown | Cùng Controller: `MarkdownController.Export()` | Cùng Service: `MarkdownService.ExportAsync()` → [src/FileHandler.Api/Modules/Markdown/MarkdownTranslationApplier.cs](../src/FileHandler.Api/Modules/Markdown/MarkdownTranslationApplier.cs): `MarkdownTranslationApplier.Apply()`. |
| TXT Import/Export | [src/FileHandler.Api/Controllers/PlainTextController.cs](../src/FileHandler.Api/Controllers/PlainTextController.cs): `PlainTextController.Import()`, `Export()` | [src/FileHandler.Api/Modules/PlainText/PlainTextService.cs](../src/FileHandler.Api/Modules/PlainText/PlainTextService.cs): `PlainTextService.ImportAsync()`, `ExportAsync()`. |
| Word Import/Export | [src/FileHandler.Api/Controllers/WordController.cs](../src/FileHandler.Api/Controllers/WordController.cs): `WordController.Import()`, `Export()` | [src/FileHandler.Api/Modules/Word/WordService.cs](../src/FileHandler.Api/Modules/Word/WordService.cs): `WordService.ImportAsync()`, `ExportAsync()` → [WordExtractor.Analyze()](../src/FileHandler.Api/Modules/Word/WordExtractor.cs). |
| Excel Import/Export | [src/FileHandler.Api/Controllers/ExcelController.cs](../src/FileHandler.Api/Controllers/ExcelController.cs): `ExcelController.Import()`, `Export()` | [src/FileHandler.Api/Modules/Excel/ExcelService.cs](../src/FileHandler.Api/Modules/Excel/ExcelService.cs): `ExcelService.ImportAsync()`, `ExportAsync()` → [ExcelExtractor.Analyze()](../src/FileHandler.Api/Modules/Excel/ExcelExtractor.cs). |
| PowerPoint Import/Export | [src/FileHandler.Api/Controllers/PowerPointController.cs](../src/FileHandler.Api/Controllers/PowerPointController.cs): `PowerPointController.Import()`, `Export()` | [src/FileHandler.Api/Modules/PowerPoint/PowerPointService.cs](../src/FileHandler.Api/Modules/PowerPoint/PowerPointService.cs): `PowerPointService.ImportAsync()`, `ExportAsync()` → [PowerPointExtractor.Analyze()](../src/FileHandler.Api/Modules/PowerPoint/PowerPointExtractor.cs). |
| File type / download name | Các Controller: extension check trong `Import()`/`Export()` | [src/FileHandler.Api/Common/FileTypeDetector.cs](../src/FileHandler.Api/Common/FileTypeDetector.cs): `FileTypeDetector.TryDetect()`, `GetTranslatedFileName()`; [OfficePackageReader.ReadAsync()](../src/FileHandler.Api/Modules/Office/OfficePackageReader.cs) kiểm tra format Office thực tế. |
| Markdown parsing | [src/FileHandler.Api/Modules/Markdown/MarkdownExtractor.cs](../src/FileHandler.Api/Modules/Markdown/MarkdownExtractor.cs): `MarkdownExtractor.Extract()` | [src/FileHandler.Api/Modules/Markdown/MarkdownProfile.cs](../src/FileHandler.Api/Modules/Markdown/MarkdownProfile.cs): `MarkdownProfile.CreatePipeline()`; [MermaidCodec.Extract()](../src/FileHandler.Api/Modules/Markdown/MermaidCodec.cs). |
| Selection / discovery | [src/FileHandler.Api/Common/SelectionInput.cs](../src/FileHandler.Api/Common/SelectionInput.cs): `SelectionInput.Parse()`; `ExcelController.Sheets()`, `PowerPointController.Slides()` | [src/FileHandler.Api/Modules/Office/OfficeCatalog.cs](../src/FileHandler.Api/Modules/Office/OfficeCatalog.cs): `OfficeCatalog.Sheets()`, `Slides()`; Service discovery methods. |
| Tên sheet / formula | [src/FileHandler.Api/Modules/Excel/ExcelRenamePlanner.cs](../src/FileHandler.Api/Modules/Excel/ExcelRenamePlanner.cs): `ExcelRenamePlanner.Prepare()`, `Normalize()` | [src/FileHandler.Api/Modules/Excel/ExcelFormulaReferences.cs](../src/FileHandler.Api/Modules/Excel/ExcelFormulaReferences.cs): `ExcelFormulaReferences.TryParse()`, `Rewrite()`. |
| Token validation | [src/FileHandler.Api/Common/TranslationTokenParser.cs](../src/FileHandler.Api/Common/TranslationTokenParser.cs): `TranslationTokenParser.Validate()` | [src/FileHandler.Api/Modules/Office/OfficeTextCodec.cs](../src/FileHandler.Api/Modules/Office/OfficeTextCodec.cs): `OfficeTextCodec.ValidateAndDecode()`; [MarkdownTokenCodec.Decode()](../src/FileHandler.Api/Modules/Markdown/MarkdownTokenCodec.cs). |
| Request validation | [src/FileHandler.Api/Program.cs](../src/FileHandler.Api/Program.cs): middleware và model-state factory | [src/FileHandler.Api/Common/TranslationInputParser.cs](../src/FileHandler.Api/Common/TranslationInputParser.cs): `TranslationInputParser.TryParseAsync()`; Controller actions. |
| Error handling | [src/FileHandler.Api/Common/ControllerErrorMapper.cs](../src/FileHandler.Api/Common/ControllerErrorMapper.cs): `ControllerErrorMapper.ToActionResult()` | `GlobalExceptionHandler.TryHandleAsync()` trong [src/FileHandler.Api/Program.cs](../src/FileHandler.Api/Program.cs); [FileResponses.Failure()](../src/FileHandler.Api/Common/FileResponses.cs). |
| Metadata / skip | [src/FileHandler.Api/Common/FileMetadata.cs](../src/FileHandler.Api/Common/FileMetadata.cs): `FileMetadata.ForResponse()`, `ForExport()` | [src/FileHandler.Api/Modules/Office/OfficeMetadata.cs](../src/FileHandler.Api/Modules/Office/OfficeMetadata.cs): `OfficeMetadata.Describe()`, `WithUnits()`; [SkipCounts.From()](../src/FileHandler.Api/Common/SkipCounts.cs). |
| Output preservation | [src/FileHandler.Api/Modules/Office/OfficePackageValidator.cs](../src/FileHandler.Api/Modules/Office/OfficePackageValidator.cs): `OfficePackageValidator.ValidateOutput()` | [src/FileHandler.Api/Modules/Office/OfficeXmlInvariant.cs](../src/FileHandler.Api/Modules/Office/OfficeXmlInvariant.cs): `OfficeXmlInvariant.Matches()`. |
| Multipart response | [src/FileHandler.Api/Common/MultipartFileResult.cs](../src/FileHandler.Api/Common/MultipartFileResult.cs): `MultipartFileResult.ExecuteResultAsync()` | [src/FileHandler.Api/Common/MultipartResponseWriter.cs](../src/FileHandler.Api/Common/MultipartResponseWriter.cs): `MultipartResponseWriter.WriteHeadersAsync()`; [MultipartUnitsResult.ExecuteResultAsync()](../src/FileHandler.Api/Common/MultipartUnitsResult.cs). |
| Configuration | [src/FileHandler.Api/appsettings.json](../src/FileHandler.Api/appsettings.json), registrations trong `Program.cs` | [src/FileHandler.Api/Common/FileHandlingOptions.cs](../src/FileHandler.Api/Common/FileHandlingOptions.cs), [src/FileHandler.Api/Modules/Office/OfficeProcessingOptions.cs](../src/FileHandler.Api/Modules/Office/OfficeProcessingOptions.cs): `OfficeProcessingOptions.Validate()`. |
