# Danh mục trạng thái, skip và message

Đối chiếu mã nguồn ngày 2026-09-21. Tài liệu ghi lại hành vi **đang triển khai** của Markdown, TXT, Word, Excel và PowerPoint. Message đã được common hóa trong source và dùng tiếng Anh ngắn gọn; `{...}` biểu diễn giá trị được chèn lúc chạy. Mã JSON hiện có được giữ nguyên. Import debug=true trả cả info và warning; các kết quả khác chỉ trả warning.

## 1. Phân biệt trạng thái và lý do skip

Metadata của import, export, discovery và lỗi luôn có `skipCount: { "warning": x, "info": y }`. Mỗi giá trị là tổng `count` của các bản ghi cùng severity, tính trước khi ẩn info theo debug. Số lượng info vì thế vẫn được trả khi `debug=false`. Không cộng các giá trị này để suy ra unitCount: đối tượng được đếm có thể là sheet, cell, block hoặc unit. Khi tác vụ lỗi, chỉ đếm dữ liệu đã thu thập; trước khi có skip thì cả hai bằng 0.

### Trạng thái tác vụ: `metadata.status`

| Giá trị | Ý nghĩa | HTTP khi hoàn tất |
|---|---|---|
| `success` | Không có lỗi fatal hoặc skip `warning`; vẫn có thể có nhiều skip `info`. | 200 |
| `partial` | Tác vụ hoàn tất, có ít nhất một skip `warning`; nội dung tương ứng được giữ nguồn. | 200 |
| `failed` | Tác vụ thất bại; `errors` chứa lỗi fatal, không trả file kết quả. | 400, 413, 415, 422, 429 hoặc 500 tùy lỗi |

`skip.code` là **lý do**, không phải trạng thái tác vụ. Không tạo thêm status cho từng loại chart, field, cell hay bản dịch lỗi. Fatal có ưu tiên cao hơn warning. Metadata trên lỗi giữ các thông tin đã thu thập được, không đảm bảo đã phân tích hết nguồn hoặc toàn bộ batch.

### Các chiều mô tả skip

| Property | Giá trị hiện có | Ý nghĩa |
|---|---|---|
| `severity` | `info`, `warning` | `info`: bỏ qua theo lựa chọn/quy tắc; `warning`: nội dung chưa hỗ trợ hoặc bản dịch không áp dụng được. |
| `stage` | `selection`, `extraction`, `translation`, `rename` | Bước phát sinh lý do bỏ qua. |
| `scope` | `sheet`, `slide`, `row`, `cell`, `shape`, `block`, `inline`, `contentControl`, `story`, `region`, `unit` | Loại đối tượng mà `count` đang đếm. |
| `code` | Các mã trong mục 2–6 | Khóa ổn định cho client; không phân nhánh theo nội dung `message`. |
| `message` | Chuỗi giải thích tiếng Anh ngắn gọn từ catalog chung | Một mã có thể có nhiều message theo nguyên nhân cụ thể. |
| `count` | Hiện các điểm ghi skip đều đặt `1` | Số đối tượng trong phạm vi bản ghi, không phải số unit toàn tác vụ. Chưa có collector chung gộp các vùng liên tiếp. |
| `location` | Tọa độ nguồn phù hợp từng định dạng | Dòng TXT/Markdown; part/path Word; sheet/cell Excel; slide/shape/row/column PowerPoint. |
| `unitIndex` | Index từ 0, hoặc không có property | Có với unit dịch lỗi và rename bị chặn; không có với đối tượng bị loại trước khi tạo unit. |

`debug` điều khiển mức chi tiết skip trong kết quả service và HTTP: `false` chỉ trả warning, `true` trả cả info và warning. Cờ này chỉ có trên import, áp dụng cả response lỗi sau extraction. Export/discovery không trả info. Status vẫn dựa vào fatal/warning, không thay đổi do ẩn info. Thông tin bảo toàn đầy đủ được giữ nội bộ cho validation; lọc info sau khi xử lý hoàn tất. Sheet/slide ngoài lựa chọn được ghi ở cấp sheet/slide, không tạo danh sách unit bên trong phần đó. Export không có chi tiết unit nhưng skip vẫn có `unitIndex` nếu đã có mapping.

### Trạng thái nguồn, không phải trạng thái xử lý

| Property | Giá trị | Ý nghĩa |
|---|---|---|
| Sheet `state` | `visible`, `hidden`, `veryHidden` | `veryHidden` không mở lại bằng menu Unhide thông thường. |
| Sheet `kind` | `worksheet`, `chartsheet`, hoặc tên loại part SDK khác | Loại sheet thực tế. |
| Sheet `canImport` | Boolean | Hiện chỉ `worksheet` có khả năng trích xuất. |
| Slide `hidden` | Boolean | Slide bị ẩn trong nguồn. |
| Sheet/slide `selected` | Boolean khi xử lý import/export | Kết quả lựa chọn; discovery chưa thực hiện selection. |

Discovery hiện trả `success` với `skipped: []` khi đọc inventory thành công, kể cả có chartsheet `canImport=false`. Hai API này không chạy extractor nên không sinh skip extraction/translation/rename.

Nguồn: [FileMetadata](../src/FileHandler.Api/Common/FileMetadata.cs), [OfficeCatalog](../src/FileHandler.Api/Modules/Office/OfficeCatalog.cs), service Excel/PowerPoint.

## 2. Skip do lựa chọn và đổi tên

| Code | Định dạng | Severity / stage / scope | Điều kiện | Message hiện tại |
|---|---|---|---|---|
| `sheet_not_selected` | Excel | info / selection / sheet | Sheet ngoài lựa chọn, bao gồm sheet ẩn không được chọn rõ. | `Sheet not selected; source retained.` |
| `slide_not_selected` | PowerPoint | info / selection / slide | Slide ngoài lựa chọn. Nhánh hiện tại cũng dùng mã này khi `shapeTree` là null. | `Slide not selected; source retained.` |
| `unsupported_sheet` | Excel | warning / extraction / sheet | Sheet được chọn nhưng part không phải worksheet, ví dụ chartsheet. | `Unsupported sheet type; source retained.` |
| `unsafe_sheet_reference` | Excel | warning / rename / sheet | Không chứng minh được việc cập nhật tham chiếu an toàn; giữ tên nguồn và tiếp tục dịch nội dung. | `Unsafe references; sheet name retained.` |

`unsafe_sheet_reference` có `unitIndex` của tên sheet. Hiện dynamic/3D/external reference, pivot, extension, VML/control, shape liên kết ô, data consolidation hoặc hyperlink không xử lý an toàn dùng chung mã này; message chưa chỉ rõ loại tham chiếu gây chặn. Khi không xác định được sheet liên quan, các rename chưa chứng minh được an toàn đều bị giữ lại. VML chưa được phân tích nên cả workbook chỉ dùng VML cho comment cũng có thể bị chặn rename. Hyperlink sang workbook khác giữ nguyên `location`, không đổi theo tên sheet nội bộ.

Nguồn: [ExcelExtractor](../src/FileHandler.Api/Modules/Excel/ExcelExtractor.cs), [PowerPointExtractor](../src/FileHandler.Api/Modules/PowerPoint/PowerPointExtractor.cs), [ExcelRenamePlanner](../src/FileHandler.Api/Modules/Excel/ExcelRenamePlanner.cs).

## 3. Skip extraction của Excel và PowerPoint

Các bản ghi trong bảng này có `stage=extraction`, `count=1`, không có `unitIndex`.

| Code | Định dạng | Severity / scope | Thành phần bị giữ lại | Message hiện tại |
|---|---|---|---|---|
| `hidden_row` | Excel | info / row | Hàng ẩn và nội dung bên trong. | `Hidden row retained.` |
| `hidden_column` | Excel | info / cell | Từng ô trong cột ẩn; hiện không gom thành một bản ghi cột. | `Cell in hidden column retained.` |
| `implicit_cell_address` | Excel | warning / cell | Ô không có địa chỉ tường minh. | `Cell without explicit address retained.` |
| `protected_table_cell` | Excel | info / cell | Ô header hoặc totals của table. | `Table header or totals cell retained.` |
| `formula_cell` | Excel | info / cell | Ô công thức, không lấy làm unit dịch. | `Formula cell excluded from translation.` |
| `non_text_cell` | Excel | info / cell | Ô có giá trị nhưng không có payload text được hỗ trợ. | `Non-text cell retained.` |
| `whitespace_cell` | Excel | info / cell | Ô text chỉ chứa whitespace, độ dài lớn hơn 0. | `Whitespace-only cell retained.` |
| `phonetic_content` | Excel | warning / cell | Ô có phonetic run/properties; có thể định vị vào sharedStrings part. | `Unsupported phonetic cell retained.` |
| `merged_follower_text` | Excel, PowerPoint | warning / cell | Ô tiếp nối của vùng merge có text riêng. | `Merged follower text retained.` |
| `unsupported_graphic_frame` | Excel, PowerPoint | warning / shape | Graphic frame chưa hỗ trợ, ví dụ chart hoặc diagram; PowerPoint tiếp tục xử lý table được hỗ trợ. | `Unsupported graphic frame retained.` |

Thứ tự kiểm tra ô Excel ảnh hưởng mã được ghi: hàng ẩn → địa chỉ ngầm → cột ẩn → ô table được bảo vệ → công thức → payload/whitespace/phonetic → merge follower. Gặp nhánh skip thì dừng xử lý ô/vùng đó; không ghi mọi lý do có thể cùng áp dụng. Ô vừa ở cột ẩn vừa có công thức thường chỉ có `hidden_column`.

`formula_cell` nghĩa là không dịch công thức như văn bản. Qualifier tên sheet trong công thức vẫn có thể được cập nhật bởi rename planner để giữ tham chiếu đúng.

Location hiện tại của `hidden_row` dùng `cellReference` chứa chuỗi `row.RowIndex`, có thể null nếu nguồn không có thuộc tính này. Không nên hiểu đây luôn là địa chỉ A1. Đây là điểm nên chuẩn hóa sang `rowIndex` khi chỉnh contract.

Nguồn: [ExcelExtractor](../src/FileHandler.Api/Modules/Excel/ExcelExtractor.cs), [PowerPointExtractor](../src/FileHandler.Api/Modules/PowerPoint/PowerPointExtractor.cs), [PowerPointTableReader](../src/FileHandler.Api/Modules/PowerPoint/PowerPointTableReader.cs).

## 4. Skip extraction của Word

Tất cả có `stage=extraction`, `count=1`, không có `unitIndex`. Word dùng exclusion map cho subtree và tập đối tượng đã báo cáo để tránh trích xuất/ghi lại cùng subtree qua nhiều nhánh.

| Code | Severity / scope | Thành phần bị giữ lại | Message hiện tại |
|---|---|---|---|
| `protected_content_control` | warning / contentControl | Content control có lock hoặc data binding. | `Locked or bound content control retained.` |
| `unsupported_revision` | warning / region | Revision insert/delete/move hoặc thay đổi properties được nhận diện. | `Unsupported revision retained.` |
| `unsupported_ruby` | warning / region | Ruby annotation. | `Unsupported ruby retained.` |
| `unsupported_altchunk` | warning / region | Nội dung altChunk. | `Unsupported altChunk retained.` |
| `unsupported_alternate_content` | warning / region | AlternateContent. | `Unsupported AlternateContent retained.` |
| `merged_follower_text` | warning / cell | Ô tiếp nối merge có text. | `Merged follower text retained.` |
| `merged_follower` | info / cell | Ô tiếp nối merge không có text cần dịch. | `Empty merge continuation cell retained.` |
| `cross_paragraph_field` | warning / block | Từng paragraph thuộc phạm vi field xuyên paragraph. | `Cross-paragraph field retained.` |
| `unbounded_field_story` | warning / story hoặc region | Không xác định được biên field an toàn; giữ context tương ứng. Root part dùng `story`; textbox/note context có thể dùng `region`. | `Unknown field boundary; source region retained.` |
| `protected_inline` | info / inline | Simple field, drawing, picture hoặc điểm bắt đầu complex field được bảo toàn. | `Protected inline content retained.` |
| `unsupported_drawing` | warning / inline | Drawing/đối tượng inline chứa chart hoặc diagram được nhận diện. | `Unsupported chart or diagram retained.` |
| `unreferenced_story` | info / story | Header/footer/footnote/endnote không thuộc tập được tham chiếu. | `Unreferenced story retained.` |
| `system_note` | info / story | Note hệ thống được nhận diện trong nhánh xử lý note. | `System note retained.` |
| `unsupported_block` | warning / block | Block chưa hỗ trợ gặp trong container đang duyệt. | `Unsupported block retained.` |

Scope của exclusion trong `Prepare` được suy ra theo loại phần tử: content control → `contentControl`, ô → `cell`, paragraph → `block`, root → `story`, còn lại → `region`. Nếu nhiều exclusion lồng nhau, vùng cha được bảo toàn có thể bao trùm các lý do ở con; không phải mọi mã trong bảng đều xuất hiện đồng thời.

Nguồn: [WordExclusions](../src/FileHandler.Api/Modules/Word/WordExclusions.cs), [WordExtractor](../src/FileHandler.Api/Modules/Word/WordExtractor.cs).

## 5. Skip extraction của Markdown và TXT

| Code | Định dạng | Severity / stage / scope | Thành phần bị giữ lại | Message hiện tại |
|---|---|---|---|---|
| `protected_code_block` | Markdown | info / extraction / block | Fenced code không phải Mermaid và không có label được trích xuất. | `Code block retained.` |
| `unsupported_mermaid` | Markdown | warning / extraction / block | Fenced Mermaid không trích xuất được label nào, kể cả sơ đồ không có label dịch được. | `No translatable Mermaid labels; block retained.` |
| `protected_block` | Markdown | info / extraction / block | Code block khác, HTML block, YAML/front matter được nhận diện. | `Protected block retained.` |
| `protected_inline` | Markdown | info / extraction / inline | Inline code, autolink, HTML inline hoặc image. | `Protected inline content retained.` |

TXT hiện không phát sinh skip extraction riêng cho separator, newline hoặc BOM. Các phần đó được bảo toàn nội bộ. Không ghi một skip cho từng whitespace hay token định dạng.

Nguồn: [MarkdownExtractor](../src/FileHandler.Api/Modules/Markdown/MarkdownExtractor.cs), [PlainTextService](../src/FileHandler.Api/Modules/PlainText/PlainTextService.cs).

## 6. Skip bản dịch: mã chung và message theo nguyên nhân

Tất cả các bản ghi trong mục này có `severity=warning`, `stage=translation`, `scope=unit`, `count=1`, `unitIndex` từ 0. Lỗi unit giữ nguồn, các unit khác tiếp tục được xử lý.

### Mã dùng chung nhiều định dạng

| Code | Áp dụng | Nguyên nhân | Message hiện tại |
|---|---|---|---|
| `empty_translation` | TXT | Chuỗi rỗng hoặc chỉ whitespace. | `Empty translation; source retained.` |
| `empty_translation` | Markdown | Chuỗi rỗng hoặc chỉ whitespace. | `Empty translation; source retained.` |
| `empty_translation` | Word, Excel, PowerPoint | Unit plain rỗng hoặc chỉ whitespace. | `Empty translation; source retained.` |
| `empty_translation` | Word, Excel, PowerPoint | Toàn bộ chuỗi token structured rỗng. | `Empty translation; source retained.` |
| `empty_translation` | Markdown, Word, Excel, PowerPoint | Các slot đều không có nội dung. | `At least one slot must contain text.` |
| `invalid_translation` | TXT | Unicode không hợp lệ. | `Translation contains invalid Unicode.` |
| `invalid_translation` | Markdown | Unicode không hợp lệ. | `Translation contains invalid Unicode.` |
| `invalid_translation` | Excel | Tên sheet có Unicode không hợp lệ. | `Translation contains invalid Unicode.` |
| `invalid_translation` | Word, Excel, PowerPoint | Unicode hoặc ký tự điều khiển XML không hợp lệ. | `Translation contains invalid Unicode or XML characters.` |
| `invalid_translation` | Word, PowerPoint | Unit plain có newline/tab thô. | `Word/PowerPoint text cannot contain raw line breaks or tabs.` |
| `invalid_translation` | Word, PowerPoint | Slot có newline/tab không được phép. | `Slot {token} cannot contain line breaks or tabs in Word/PowerPoint.` |

`invalid_translation` với đầu vào null là **fatal ở service**, không phải skip. HTTP từ chối phần tử null trước service bằng lỗi request. Không thể chỉ dùng `code` để suy ra HTTP status hoặc khả năng phục hồi; phải xét lỗi nằm trong `errors` hay `metadata.skipped`.

### Mã riêng Markdown

| Code | Nguyên nhân | Message hiện tại |
|---|---|---|
| `internal_anchor_change_unsupported` | Đổi heading trong tài liệu có internal link. | `Heading retained to preserve internal links.` |
| `invalid_structure` | Heading tạo thêm dòng/block. | `Heading cannot add lines or blocks.` |
| `invalid_structure` | Bản dịch làm thay đổi cấu trúc được bảo vệ trong block. | `Protected block structure changed; source retained.` |
| `invalid_structure` | Nhãn Mermaid có control/newline. | `Mermaid labels cannot contain control characters.` |
| `invalid_marker_syntax` | Token ox sai cú pháp, thứ tự hoặc escape. | `Preserve source tokens, order and escapes.` |
| `invalid_marker_syntax` | Nhánh marker nội bộ cũ, cú pháp/ID sai. | `Invalid keepme marker syntax or ID.` |
| `invalid_marker_syntax` | Marker không ở dạng chuẩn. | `Marker {marker} must use {canonical}.` |
| `protected_marker_not_empty` | Marker bảo vệ có nội dung. | `Protected marker must be empty.` |
| `unexpected_marker` | Marker không thuộc unit. | `Unexpected marker {marker}.` |
| `duplicate_marker` | Marker mở hoặc đóng bị lặp. | `Duplicate marker {marker}.` |
| `invalid_marker_nesting` | Marker đóng sai thứ tự. | `Marker {marker} closes out of order.` |
| `missing_marker` | Thiếu marker mở. | `Missing opening marker {marker}.` |
| `missing_marker` | Thiếu marker đóng. | `Missing closing marker {marker}.` |

Các mã marker được liệt kê theo nhánh code đang có, gồm cả đường xử lý template nội bộ/legacy; không có nghĩa mọi mã đều dễ phát sinh từ wire format ox hiện tại. Applier tạo **một skip cho unit** từ danh sách lỗi decode: lấy code lỗi đầu tiên và nối các message khác nhau bằng dấu cách. Vì thế message thực tế có thể gồm nhiều câu trong bảng, còn code chỉ đại diện lỗi đầu tiên.

### `office_token_mismatch`: Word, Excel, PowerPoint

Mã chung này có các message cụ thể dưới đây; đây là các biến thể nguyên nhân, không phải các trạng thái tác vụ khác nhau.

| Nguyên nhân | Message hiện tại |
|---|---|
| Text ngoài token | `Text outside tokens is not allowed.` |
| Token không có prefix hợp lệ | `Tokens must start with '<ox:'.` |
| Thiếu kết thúc cú pháp token | `Unterminated token.` |
| Thứ tự sai | `Token order differs from source.` |
| Thẻ tự đóng sai | `Invalid self-closing token <ox:{token}/>.` |
| Anchor không khớp | `Anchor {token} differs from source.` |
| Thẻ mở sai | `Invalid opening token <ox:{token}>.` |
| Slot không khớp | `Slot {token} differs from source.` |
| Escape bỏ lửng | `Incomplete escape sequence.` |
| Escape không hỗ trợ | `Invalid escape '\{character}'; use '\\' or '\<'.` |
| Ký tự mở thẻ trong slot | `Escape '<' as '\<' inside slots.` |
| Thiếu thẻ đóng | `Missing closing token {token}.` |
| Cú pháp khác không hợp lệ | `Invalid token syntax.` |
| Số lượng/thứ tự không khớp | `Token count or order differs from source.` |

Nguồn mục 6: [OfficeTextCodec](../src/FileHandler.Api/Modules/Office/OfficeTextCodec.cs), [PlainTextService](../src/FileHandler.Api/Modules/PlainText/PlainTextService.cs), [MarkdownTranslationApplier](../src/FileHandler.Api/Modules/Markdown/MarkdownTranslationApplier.cs), [MarkdownTokenCodec](../src/FileHandler.Api/Modules/Markdown/MarkdownTokenCodec.cs).

## 7. Lỗi chung cần phân biệt với skip

Bảng này ghi các nhóm lỗi common liên quan trực tiếp đến contract; không thay cho danh mục đầy đủ lỗi schema/invariant của Office.

| Code/nhóm | HTTP | Message hiện tại tiêu biểu | Xử lý |
|---|---|---|---|
| `missing_file` | 400 | Import/export: `File is required.`; discovery dùng cùng message này | Thiếu file, fatal. |
| `missing_texts` | 400 | `Texts are required.` | Thiếu mảng dịch, fatal. |
| `invalid_texts` | 400 | `Texts must be a JSON string array.` / `Each text must be a non-null string.` | Sai kiểu dữ liệu, fatal. |
| `invalid_json` | 400 | `Texts contain invalid JSON.` / `Texts contain invalid Unicode.` | Không parse được texts, fatal. |
| `invalid_selection` | 400 | `Selection must be a JSON string array.` / `Selection contains invalid JSON strings.` | Không parse được lựa chọn, fatal. |
| `invalid_request` | 400 | `Invalid multipart request.` | Model binding/request sai, kể cả debug không phải boolean. |
| `unsupported_media_type` | 415 | `Content-Type must be multipart/form-data.` | Sai Content-Type. |
| `unsupported_file_type` | 415 | Theo endpoint: `Only .xlsx files are supported.`; discovery dùng cùng message theo extension | Extension không hỗ trợ. |
| `file_too_large`, `request_too_large` | 413 | Ví dụ `Multipart request exceeds size limit.` | Hai mã khác nhau tùy điểm phát hiện. |
| `too_many_units`, `translation_too_long`, `output_too_large` | 413 | Message có giới hạn thực tế hoặc mô tả vượt quota. | Quota luôn fatal. |
| `office_package_limit_exceeded`, `office_plan_limit_exceeded`, `office_translation_limit_exceeded`, `office_schema_limit_exceeded` | 413 | Theo giới hạn package/token/batch/schema bị vượt. | Không chuyển thành skip unit. |
| `translation_count_mismatch` | 422 | `Expected {expected} translations; received {actual}.` | Mapping không khớp, fatal. |
| `unknown_selection_id` | 422 | Theo ID không có trong inventory. | Khác với phần ngoài lựa chọn; đây là lỗi request tham chiếu nguồn. |
| `invalid_office_package` | 422 | Import/export: `Invalid Office package.`; discovery dùng cùng message này | Nguồn không thể xử lý an toàn. |
| `patch_conflict`, lỗi cấu trúc cuối không cô lập được | 422 | Ví dụ `Replacement regions overlap.` | Không trả file chưa đảm bảo hợp lệ. |
| `request_limit_exceeded` | 429 | `Processing capacity reached. Retry later.` | Không đủ permit xử lý đồng thời. |
| `internal_error` | 500 | `Internal server error.` | Không trả chi tiết exception ngoài dự kiến. |

Nguồn: [Program](../src/FileHandler.Api/Program.cs), [TranslationInputParser](../src/FileHandler.Api/Common/TranslationInputParser.cs), [SelectionInput](../src/FileHandler.Api/Common/SelectionInput.cs), [ControllerErrorMapper](../src/FileHandler.Api/Common/ControllerErrorMapper.cs), controllers và services tương ứng.

## 8. Thành phần common đang sử dụng trong source

| Thành phần | Trách nhiệm |
|---|---|
| [ProcessingStatus](../src/FileHandler.Api/Common/ProcessingStatus.cs) | `success`, `partial`, `failed` và hàm `Resolve` dùng chung để suy ra trạng thái. |
| [SkipSeverity](../src/FileHandler.Api/Common/SkipSeverity.cs) | `info`, `warning`. |
| [SkipStage](../src/FileHandler.Api/Common/SkipStage.cs) | `selection`, `extraction`, `translation`, `rename`. |
| [SkipScope](../src/FileHandler.Api/Common/SkipScope.cs) | Phạm vi sheet/slide/cell/unit và các loại vùng nguồn. |
| [SheetVisibility](../src/FileHandler.Api/Common/SheetVisibility.cs) | `visible`, `hidden`, `veryHidden`. |
| [SkipCodes](../src/FileHandler.Api/Common/SkipCodes.cs) | 41 mã skip hiện tại; giữ nguyên giá trị JSON để client không phải đổi phân nhánh. |
| [ProcessingMessages](../src/FileHandler.Api/Common/ProcessingMessages.cs) | Message tiếng Anh ngắn gọn dùng chung, gồm message tĩnh và hàm format có tham số cho token/count/quota. `PreservedRegion` chọn câu cụ thể theo exclusion Word. |
| [FileMetadata.ForResponse](../src/FileHandler.Api/Common/FileMetadata.cs) | Lọc info ở biên kết quả service khi debug=false, giữ nguyên warning, status, count và các dữ liệu khác. |
| [SkipCounts](../src/FileHandler.Api/Common/SkipCounts.cs) | Cộng count theo warning/info; ForResponse lưu tổng trước khi lọc để không mất số lượng info. |

Extractors và validators vẫn dùng tập skip nội bộ đầy đủ. Các service import/export gọi core xử lý trước, rồi mới project metadata cho người gọi. Cách này giữ nguyên kiểm tra schema/invariant đối với vùng nguồn được bảo toàn. `debug` không bật log server hoặc trả nội dung exception ngoài dự kiến.

Các message có cùng ý nghĩa như bản dịch rỗng, Unicode sai, missing file, sai selection, count mismatch được dùng chung giữa định dạng hoặc giữa import/export/discovery. Các biến thể token/slot vẫn có message cụ thể thay vì gom thành một câu không có thông tin. Những kiểm tra chuyên biệt schema/invariant không thuộc danh mục common này vẫn nằm ở module tương ứng.

## 9. Trùng lặp hợp lệ và phần còn cần xem xét

| Hiện tượng | Cách xử lý hiện tại / lưu ý |
|---|---|
| Nhiều nơi dùng cùng status/code/message | Đã tham chiếu catalog common, không khai báo lại literal skip trong các nhánh xử lý. |
| Nhiều mã Word từng dùng một câu quá chung | Đã có message riêng cho ruby/revision/field/content control/merge follower và các exclusion khác. |
| Sheet không chọn và sheet không hỗ trợ từng dùng cùng message | Đã tách message; giữ severity và stage khác nhau. |
| Code block và Mermaid không hỗ trợ từng dùng cùng message | Đã tách message theo code. |
| Cùng code ở nhiều vị trí nguồn | Giữ riêng: 100 ô công thức là 100 đối tượng, không phải bản ghi trùng. Các info này chỉ hiển thị khi import debug=true. |
| `unsafe_sheet_reference` bao gồm nhiều nguyên nhân | Vẫn dùng mã chung; chưa thêm property reason hay phân loại dynamic/pivot/external trong response. |
| `hidden_column` nhưng scope cell | Giữ nghĩa đếm từng ô; chưa gom thành phạm vi cột. |
| `hidden_row` dùng location cellReference | Chưa đổi sang rowIndex trong lần chỉnh common này. |
| `slide_not_selected` khi thiếu shapeTree | Nhánh cũ còn dùng cùng mã; cần fixture riêng và chốt cách phân loại lỗi trước khi thay đổi. |
| Merge follower không có text | Word ghi info; PowerPoint chỉ ghi khi có text. Chưa thay đổi quy tắc extraction. |
| Bản ghi trùng hoàn toàn cùng đối tượng | Chưa có bằng chứng tái hiện runtime từ lần rà soát này; không loại trùng theo code hoặc message. |

Không dùng `DistinctBy(code)` hay `DistinctBy(message)` vì sẽ làm mất vị trí. Nếu bổ sung collector loại trùng, cần so code/severity/stage/scope/unitIndex/location và chỉ bỏ bản ghi khi chắc chắn cùng đối tượng, cùng nguyên nhân. Gộp các vùng liên tiếp chỉ hợp lệ khi location mô tả đủ vùng và count đếm đúng đối tượng. Giữ riêng lỗi của từng unit; không cắt warning theo MaxErrors.

Các test HTTP/service kiểm tra default/false/true, info chỉ có ở debug, warning giữ nguyên trong cả hai chế độ, export chỉ có warning và byte fallback vẫn đúng, fatal vẫn trả JSON với metadata theo đúng debug, đồng thời không lẫn chế độ giữa các request.
