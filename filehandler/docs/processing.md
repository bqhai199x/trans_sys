# File processing

[Trang bắt đầu](README.md) · [API](api.md) · [Token/placeholder](tokens.md)

Các Service dựng lại mapping khi Export. `texts[i]` tương ứng unit thứ `i` của source và selection hiện tại. Metadata public mô tả kết quả; không phải input để restore file.

## Plain text

Nguồn: [PlainTextService.cs](../src/FileHandler.Api/Modules/PlainText/PlainTextService.cs), [PlainTextSegmenter.cs](../src/FileHandler.Api/Modules/PlainText/PlainTextSegmenter.cs).

**Import**

```text
PlainTextService.ImportAsync()
→ Utf8TextReader.ReadAsync(): giới hạn byte, decode UTF-8 nghiêm ngặt
→ PlainTextSegmenter.Segment(): paragraph spans và line ranges
→ TranslationTokenSyntax.EncodeLiteral() → texts + metadata
```

**Export**

```text
PlainTextService.ExportAsync()
→ đọc/segment lại nguồn → ValidateTranslations()
→ decode literal có prefix token; giữ nguồn cho unit bị skip
→ FitsOutputLimit() → Compose() → bytes UTF-8
```

- Dòng rỗng hoặc chỉ whitespace phân cách paragraph. CR, LF, CRLF và newline hỗn hợp không bị normalize khi đọc.
- Unit giữ indentation và line break bên trong paragraph, không chứa terminator cuối paragraph. Separator, khoảng trắng ngoài unit và BOM giữ theo nguồn.
- Ký hiệu Markdown/HTML trong TXT là literal. Translation được chèn nguyên văn, ngoại trừ giải mã literal đã được bọc token lúc Import.
- Unit rỗng/whitespace hoặc Unicode không hợp lệ giữ nguồn và ghi warning. Count mismatch, null ở lời gọi Service, quota là fatal.
- Không thay đổi nội dung thì trả bytes nguồn sau khi kiểm tra giới hạn; file rỗng có mapping rỗng.

## Markdown

Nguồn: [MarkdownService.cs](../src/FileHandler.Api/Modules/Markdown/MarkdownService.cs), [MarkdownExtractor.cs](../src/FileHandler.Api/Modules/Markdown/MarkdownExtractor.cs), [MarkdownTranslationApplier.cs](../src/FileHandler.Api/Modules/Markdown/MarkdownTranslationApplier.cs).

**Import**

```text
MarkdownSourceReader.ReadAsync()
→ MarkdownExtractor.Extract() → ParseDocument() với MarkdownProfile.CreatePipeline()
→ duyệt leaf block/inline; MermaidCodec.Extract() cho fenced code
→ MarkdownTokenCodec.Encode() → texts + vị trí dòng + skip
```

**Export**

```text
MarkdownService.ExportAsync() → đọc và Extract() lại
→ MarkdownTranslationApplier.Apply(): validate batch, decode token, tạo replacements
→ kiểm tra cấu trúc block; nối replacements với các span nguyên gốc
→ MarkdownExtractor.ValidateStructure() cho toàn tài liệu
→ encode theo BOM nguồn → kiểm tra MaxOutputBytes → output
```

- `MarkdownProfile` bật pipe tables, task lists, emphasis extras, mathematics và YAML front matter qua Markdig.
- Extract text của heading và leaf block có inline; `UnitMetadata.Kind` là `heading`, `paragraph` hoặc `mermaidLabel`.
- Giữ fenced code không có label dịch, code block, HTML block và YAML/front matter. Inline code/autolink, HTML, ảnh và các inline bảo vệ được giữ qua template/token; formatting và URL nguồn được restore.
- Soft/hard break nguồn được bảo vệ; newline trong bản dịch dùng `NewlineReplacement` của unit, gồm prefix cần thiết như blockquote. BOM và các span ngoài replacement giữ nguyên.
- Escape text được dịch trước khi chèn Markdown; không chèn bản dịch như raw Markdown tùy ý.
- Nếu tài liệu có link nội bộ bắt đầu `#`, heading bị thay đổi được giữ nguồn với `internal_anchor_change_unsupported`.
- Token sai, heading thêm dòng hoặc cấu trúc block thay đổi có thể cô lập: skip unit, tiếp tục các unit khác. Replacement overlap (`patch_conflict`) hoặc sai cấu trúc cuối chưa giải quyết được: fatal.
- Khi so sánh cấu trúc thất bại, code thử baseline restore trước khi kết luận lỗi. Identity export giữ bytes gốc.

### Mermaid trong Markdown

[MermaidCodec.cs](../src/FileHandler.Api/Modules/Markdown/MermaidCodec.cs) nhận diện label trong `flowchart`/`graph`, `sequenceDiagram`, `stateDiagram`/`stateDiagram-v2` và `classDiagram`/`classDiagram-v2` theo syntax parser hiện có. `MermaidFlowchartCodec` là wrapper gọi codec này. Mỗi label nhận diện được là một unit; phần syntax còn lại giữ nguồn.

Export encode ký tự đặc biệt theo Mermaid qua `MermaidCodec.Encode()`. Label chứa control character/newline bị skip. Fenced Mermaid không extract được label nào được giữ nguyên với warning `unsupported_mermaid`.

## Office: flow dùng chung

Áp dụng cho `WordService`, `ExcelService`, `PowerPointService`. Các interface extractor là `IWordExtractor`, `IExcelExtractor`, `IPowerPointExtractor`.

**Import**

```text
Service.ImportAsync()
→ LimitedReadStream(MaxFileBytes) → OfficePackageReader.ReadAsync(expectedFormat)
→ OfficePackageInspector.Inspect() → Extractor.Analyze()
→ kiểm tra unit quota → OfficePackageValidator.ValidateSource()
→ nếu debug: OfficeMetadata.WithUnits()
→ plan.Units[].EncodedSource + metadata
```

**Export**

```text
Service.ExportAsync()
→ đọc package, Inspect(), Analyze() lại → ValidateSource()
→ OfficeTextCodec.ValidateAndDecode()
→ TranslationApplier.Prepare(): bindings và edit masks
→ không có edit: kiểm tra output quota rồi trả bytes nguồn
→ OfficeExportSession.Create() → TranslationApplier.Apply()
→ OfficeExportSession.FinalizeAsync()
→ OfficePackageValidator.ValidateOutput() → StructureValidator.Validate()
→ bytes package + metadata
```

Excel thêm `ExcelRenamePlanner.Prepare()` trước kiểm tra identity và `ExcelRenamePlanner.Apply()` trước finalize.

- Reader kiểm tra ZIP, CRC/độ dài entry, path, manifest, XML và quota; đối chiếu content type của package với format endpoint. OLE, digital signature và Strict OOXML bị từ chối bằng `office_unsupported_content`.
- SDK mở package với `AutoSave=false`, markup compatibility `NoProcess`. Plan lưu vị trí, text slots, anchors và XML bindings để apply.
- Run liền nhau có style/context tương đương được gộp; khác style, hyperlink/container hoặc anchor giữ ranh giới. Token public được mô tả trong [tokens.md](tokens.md).
- `ValidateSource()` dùng schema Office 2019. Lỗi trong vùng chọn không được xác nhận là vùng giữ nguyên sẽ làm thất bại; baseline lỗi được giữ để đối chiếu output.
- Export chỉ ghi các part thay đổi, copy payload part khác vào ZIP mới. `ValidateOutput()` kiểm tra schema, inventory entry, hash part không đổi và XML edit mask của part đổi. Validator từng format còn kiểm tra cấu trúc riêng.
- Count/quota là fatal; bản dịch không hợp lệ theo unit thường giữ nguồn và đưa vào `metadata.skipped`. Word/PowerPoint không nhận raw CR/LF/tab trong text slot; Excel normalize CRLF/CR thành LF và kiểm tra giới hạn text.

## Word

Nguồn: [WordExtractor.cs](../src/FileHandler.Api/Modules/Word/WordExtractor.cs), [WordExclusions.cs](../src/FileHandler.Api/Modules/Word/WordExclusions.cs), [WordTranslationApplier.cs](../src/FileHandler.Api/Modules/Word/WordTranslationApplier.cs).

**Import:** `WordExtractor.Analyze()` → body → header theo thứ tự section (mỗi part một lần) → footer theo thứ tự section → footnote được tham chiếu → endnote được tham chiếu. Trong từng story: paragraph, table/nested table và textbox hỗ trợ được duyệt theo nguồn; template tạo unit paragraph.

**Export:** `WordTranslationApplier.Prepare()` → `Apply()` dùng bindings/parts đã decode để thay text hoặc dựng lại thứ tự fragment hợp lệ → finalize → `WordStructureValidator.Validate()`.

- Giữ run/paragraph properties, bảng, hyperlink và đối tượng nguồn qua binding/anchor; không tạo lại document từ plain text.
- SDT có lock/data binding, revision, ruby, altChunk, alternate content và block chưa hỗ trợ được giữ nguồn. Merge continuation không trở thành unit dịch.
- Field xuyên paragraph giữ các paragraph liên quan; field không cân bằng giữ story tương ứng. Footnote/endnote không tham chiếu, system note, header/footer không tham chiếu không được extract.
- Field, tab, break, picture/marker có thể trở thành protected anchor hoặc boundary theo loại và container.
- `WordRunFingerprint` có xử lý font hint: chỉ gộp run khác hint khi xác định được font ASCII/High ANSI/East Asian hiệu lực tương đương; XML formatting gốc vẫn được giữ.

## Excel

Nguồn: [ExcelExtractor.cs](../src/FileHandler.Api/Modules/Excel/ExcelExtractor.cs), [ExcelTranslationApplier.cs](../src/FileHandler.Api/Modules/Excel/ExcelTranslationApplier.cs), [ExcelRenamePlanner.cs](../src/FileHandler.Api/Modules/Excel/ExcelRenamePlanner.cs).

**Import:** `ExcelExtractor.Analyze()` → `OfficeCatalog.Sheets()` → áp selection → mỗi worksheet: unit `sheetName` đầu tiên → hàng theo số → ô theo số cột → drawing paragraphs. Worksheet rỗng vẫn có unit tên sheet; sheet ngoài selection không có unit.

**Export:** `ExcelTranslationApplier.Prepare()` → `ExcelRenamePlanner.Prepare()` → apply cell/drawing và rename/reference edits → finalize → `ExcelStructureValidator.Validate()`.

- Extract shared strings và inline strings có text. Rich text giữ style bằng slot/binding. Shared strings được ghi theo copy-on-write để bản dịch của một cell không đổi cell khác cùng index.
- Hàng/cột ẩn, table header/totals, formula cell và non-text cell giữ nguồn; chọn sheet ẩn không làm các hàng/cột ẩn trở thành unit.
- Whitespace-only cell, phonetic content, merged follower có text riêng và graphic frame chưa hỗ trợ bị bỏ qua theo rule. Chartsheet vẫn có inventory; nếu được chọn sẽ ghi warning `unsupported_sheet`.
- Duplicate/invalid cell coordinates hoặc shared-string reference hỏng làm package không hợp lệ.
- Không thay cell style, structure bảng hay formula như bản dịch text. Rename sheet là bước riêng có thể cập nhật qualifier trong formula/reference.

### Tên sheet và reference

Tên bản dịch lấy từ unit `sheetName`, liên kết bằng `SheetId`. `ExcelRenamePlanner.Normalize()` trim, thay control/ký tự `:\/?*[]` bằng `_`, bỏ nháy đơn/whitespace ở mép, giới hạn 31 UTF-16 code unit mà không cắt surrogate pair. Rỗng sau normalize dùng `Sheet`; `History` thêm `_`. Collision không phân biệt hoa/thường được thêm ` (2)`, ` (3)` trong giới hạn tên.

`ExcelFormulaReferences` nhận diện qualifier trong cell formula, defined name, table formula, conditional formatting/data validation, chart formula và hyperlink nội bộ. Chỉ reference nhận diện được mới được rewrite; string literal và structured column name giữ nguyên.

Reference dynamic/3D/external hoặc carrier như pivot, extension, VML/control không xác định được an toàn có thể chặn rename liên quan; `unsafe_sheet_reference` giữ tên nguồn, cell hợp lệ vẫn dịch. Với 3D reference xác định được phạm vi, sheet độc lập vẫn có thể rename. Export trả `sheetNameChanges` cho tên được yêu cầu thay đổi, gồm tên cuối thực tế.

## PowerPoint

Nguồn: [PowerPointExtractor.cs](../src/FileHandler.Api/Modules/PowerPoint/PowerPointExtractor.cs), [PowerPointTableReader.cs](../src/FileHandler.Api/Modules/PowerPoint/PowerPointTableReader.cs), [PowerPointTranslationApplier.cs](../src/FileHandler.Api/Modules/PowerPoint/PowerPointTranslationApplier.cs).

**Import:** `PowerPointExtractor.Analyze()` → `OfficeCatalog.Slides()` → selection → `WalkShapeTree()` theo thứ tự slide/shape → text paragraph, group đệ quy và table cell hỗ trợ → `DrawingTextCodec.ReadParagraph()`.

**Export:** `PowerPointTranslationApplier.Prepare()` → `Apply()` thay text/fragment qua bindings → finalize → `PowerPointStructureValidator.Validate()`.

- Mặc định chỉ slide không hidden; chọn native ID ẩn vẫn xử lý và giữ trạng thái ẩn.
- Giữ slide ID/thứ tự, shape, style, bảng và merge structure. Table merge continuation không được dịch như cell độc lập; có text riêng thì ghi warning.
- Graphic frame không phải table hỗ trợ (ví dụ chart/diagram) giữ nguồn với warning; shape/table khác vẫn được xử lý.
- Extractor duyệt slide shape tree; không duyệt speaker notes, slide master hoặc layout để tạo unit.
