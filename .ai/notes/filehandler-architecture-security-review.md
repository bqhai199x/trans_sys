# FileHandler — Code Review & Architecture Review

**Reviewer:** [CDX] — ngày 2026-09-15, múi giờ `+07:00`.

**Đối tượng:** Working tree hiện tại của `trans_sys`, bao gồm Office và migration token chưa commit. Đây là review, chưa triển khai remediation. Source/config được định danh trong [SHA-256 manifest](filehandler-review-evidence/source-manifest.json).

## 1. Executive Summary

**Production Readiness Score: 4/10. Chưa nên deploy luồng Office vào production.** Đây là đánh giá kỹ thuật dựa trên bằng chứng hiện tại, không phải thang điểm tự động.

Rủi ro lớn nhất là **export trả thành công nhưng mất, lặp hoặc đặt sai nội dung dịch**. Đã tái hiện trên cả Word, Excel và PowerPoint với fixtures có **0 lỗi schema trước/sau**. Vì vậy, việc build sạch và tất cả test pass hiện chưa chứng minh được các bất biến quan trọng nhất.

### Điểm mạnh đã kiểm chứng

- Ranh giới bốn module đúng hướng: `Modules/Office` không import ngược Word/Excel/PowerPoint; semantics bảng nằm trong từng module định dạng.
- DI tường minh; `GetRequiredService` trong registration factories thuộc composition root. Không tìm thấy service locator trong domain services hoặc constructor tự tạo dependency thay thế. Các `Create()` factories dành cho test không phải Bastard Injection trong đường DI production.
- Trạng thái xử lý tài liệu, DOM, plan, SST writer và export session thuộc từng request. Singleton services không giữ document mutable dùng chung. `AsyncLocal` và khóa từng trace session hỗ trợ cô lập request; chưa thấy race nghiệp vụ rõ ràng qua review source.
- SDK được mở read-only. Các probe success/failure giữ nguyên source bytes; output ZIP độc lập và controller chỉ trả bytes khi service hoàn tất. Cần phân biệt tính nguyên tử công bố với tính đúng đắn của validation.
- SST copy-on-write giữ entry cũ: probe cùng SST index giữa sheet visible và `VeryHidden` trả `Translated` ở sheet visible, `Shared` ở sheet ẩn.
- TXT có reader UTF-8 strict, quota trong vòng đọc, budget output trước compose và identity dùng source bytes. Markdown có token mapping phẳng, escaping và forward composition; bộ test có nested formatting, literal tokens, BOM/EOL.
- Việc bỏ **cả** `count` và `uniqueCount` khỏi SST là phù hợp quy tắc optional của ISO/IEC 29500 được Microsoft dẫn lại. Không coi đây là lỗi. Cần corpus Office desktop để kết luận interoperability thực tế. [Microsoft SharedStringTable](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.spreadsheet.sharedstringtable?view=openxml-3.0.1).

### Bằng chứng chạy lại

| Kiểm tra | Kết quả |
| --- | --- |
| SDK / target / compiler language | `10.0.401` / `net10.0` / MSBuild `LangVersion=14.0`; mô tả C# 13 chưa khớp cấu hình thực tế |
| Release build, XML documentation bật | 0 warnings, 0 errors |
| Test suite | **345/345 pass**, gồm 52 tests thuộc Office/Word/Excel/PPT |
| Formatter verify | Exit 0, không có thay đổi |
| Review probes | 24 observations chính, 4 supplemental, 4 HTTP requests; có lỗi được xác nhận và đối chứng đúng |
| Audit documentation | 99 files, 816 khai báo; 27 dùng inheritdoc thiếu explicit tags theo AGENTS; 8 vị trí cần biên tập từ ngữ |

[TRX](filehandler-review-evidence/test-results/review-baseline.trx), [observations](filehandler-review-evidence/observations.json), [supplemental](filehandler-review-evidence/extra-observations.json), [HTTP](filehandler-review-evidence/http-observations.json), [hướng dẫn tái hiện](filehandler-review-evidence/README.md).

**Giới hạn:** Không mở/render bằng Microsoft Office desktop; chưa xác nhận no-repair/layout fidelity. Không chạy load test đồng thời ở quy mô production hoặc cố tình gây OOM. NuGet vulnerability feed không truy cập được trong sandbox; chưa có kết luận CVE dependency. Restore dùng package cache và tắt NuGetAudit riêng trên command line; cấu hình project không thay đổi.

## 2. Findings Table

Severity theo yêu cầu: CRITICAL cho sai tính đúng đắn nghiêm trọng; HIGH cho lỗi xử lý Office/bảo vệ giới hạn; MEDIUM cho hiệu năng và sai lệch thiết kế; LOW cho convention/docs. P0/P1 là thứ tự thực hiện, không đồng nhất với mức exploitability.

| ID | Severity | Phát hiện | Bằng chứng chính | Ưu tiên |
| --- | --- | --- | --- | --- |
| F01 | **CRITICAL** | Word bỏ bindings, gán theo text-node ordinal, sửa cả protected field | `XY` thành `XYC`; field bị ghi đè | P0 |
| F02 | **CRITICAL** | Excel chỉ áp dụng slot đầu, xóa rich formatting | Hai slot `X/Y` còn `X` | P0 |
| F03 | **CRITICAL** | PPT mất paragraph/table identity khi apply | Hai paragraph còn paragraph thứ hai; bảng thứ hai ghi vào bảng đầu | P0 |
| F04 | HIGH | Excel drawing có unit import nhưng không được apply | Export success, drawing vẫn là `Drawing` | P0 |
| F05 | HIGH | Prepare chỉ đánh dấu phần đổi, Apply lại serialize mọi unit | Đổi body nhưng header/footer bị CRC mismatch; đổi riêng r1 bị reject | P0 |
| F06 | HIGH | Word topology validator đếm bảng body so với bảng mọi story | Header có bảng: output count 0, plan count 1 | P0 |
| F07 | HIGH | Edit masks không được enforce; topology/schema baseline chưa đầy đủ | Sửa bold dưới mask chỉ cho text vẫn qua cả hai validator | P0 |
| F08 | HIGH | Office token template mất thứ tự r/k và tạo slot whitespace không export lại được | Leading break encode sai thứ tự; identity trả empty_translation | P0 |
| F09 | HIGH | Tổng multipart quota bị bypass bằng chunked | 5.453 bytes / quota 4.096: Content-Length 413, chunked 200 | P0 |
| F10 | HIGH | XML preflight dựa suffix thay vì content type | Main XML đổi sang `.dat` vượt MaxXmlDepth vẫn được nhận | P0 |
| F11 | HIGH | Office quota không đồng nhất giữa import/export và HTTP errors | MaxUnits export bị bỏ qua; output limit/duplicate rel thành 500 | P0 |
| F12 | HIGH | Capability/visibility guards thiếu bound SDT và hidden columns | Bound SDT được dịch; hidden column tạo unit | P0 |
| F13 | HIGH | CaptureContent=false không che exception trong log thường | Sentinel relationship xuất hiện trong ILogger log | P0 |
| F14 | MEDIUM | Quét sibling/cell lặp gây O(N²); Markdown còn đường xử lý bậc hai | Word 2k/4k/8k paragraphs: 132/541/1.916 ms | P1 |
| F15 | MEDIUM | Quota kiểm sau cấp phát lớn; nhiều buffer và trace flush đồng bộ | Reader/serializer/Markdown output; benchmark allocations | P1 |
| F16 | MEDIUM | Soft break Markdown vẫn literal, chưa là protected token | `Alpha\r\nBeta` có thể bị gộp khi export | P1 |
| F17 | MEDIUM | Nhiều điểm mở SDK không truyền OpenSettings theo bất biến | Extractors/services/structure validators dùng overload mặc định | P1 |
| F18 | LOW | Build XML sạch chưa đồng nghĩa đạt convention 100% | 27 inheritdoc-only, 8 chỗ wording; C# thực tế 14 | P2 |

**Tổng:** 3 CRITICAL, 10 HIGH, 4 MEDIUM, 1 LOW. Các vấn đề được nhóm theo nguyên nhân để tránh đếm mỗi định dạng thành một lỗi riêng.

## 3. Detailed Findings

Các đoạn **Sau** dưới đây minh họa hướng sửa và contract mong muốn; helper được nêu tên là phần cần triển khai, không phải patch đã build/test. Không nên sửa cục bộ chỉ để vượt qua validator hiện tại.

### F01 — CRITICAL — Word không dùng bindings khi apply

**File/dòng:** `filehandler/src/FileHandler.Api/Modules/Word/WordTranslationApplier.cs#L215-L258`; `WordExtractor.cs#L293-L320`, `#L328-L359`, `#L512-L524`.

**Vấn đề:** Extractor có thể gộp nhiều `w:t` thành một slot, nhưng `ApplyStructuredSlots` gán slot s vào text node s. Nó gọi `Descendants<W.Text>()` trên toàn paragraph nên kéo cả cached field hoặc text trong textbox. `OriginalTextHash`, `SlotId` và locator trong bindings không được xác minh. Các locator hiện cũng có lỗi: `w:t` luôn ordinal 1; hyperlink luôn ordinal 1 và run ordinal không được scope theo parent. Không thể chỉ chuyển sang dùng bindings mà giữ nguyên cách xây locator.

**Tái hiện:** Hai run thường `A`,`B` và một run bold `C` import thành r0=`AB`, r1=`C`. Dịch thành `X`,`Y` xuất `[X,Y,C]`, không phải `XY`. Paragraph có `fldSimple(FIELD)` trước `Body` xuất `[Translated,Body]`: field protected bị sửa, text cần dịch còn nguyên. Word complex field cached result còn bị import thành unit (`word-complex-field-cache`). Mọi fixture nêu trên có schema hợp lệ.

**Rủi ro:** Sai văn bản và field result, gán bản dịch sai formatting, dữ liệu cũ còn lại. Fingerprint chỉ xét một tập con của `rPr` còn có thể gộp các run khác `rStyle`, `vertAlign`, strike, RTL hoặc thuộc tính khác.

**Trước:**

```csharp
var textNodes = paragraph.Descendants<W.Text>().Where(t => !string.IsNullOrEmpty(t.Text)).ToList();
textNodes[s].Text = decoded.DecodedSlots[s];
```

**Sau — cần triển khai:**

```csharp
var targets = ResolveAndVerifyBindings(unit, partRoot);
foreach (var slot in changedSlots)
    ApplyToBoundTextNodes(targets.ForSlot(slot.Id), slot.Text);
```

Mỗi locator phải xác định expanded name + ordinal đúng parent + part; xác nhận expected text hash trước mutation; reject thiếu/trùng target. Gộp run chỉ khi toàn bộ formatting/semantic context tương đương. Field instruction và cached result phải protected; theo dõi stack field qua các paragraph trong story và kiểm tra cân bằng. Textbox phải là unit có ownership riêng.

### F02 — CRITICAL — Excel bỏ mọi slot ngoài slot đầu

**File/dòng:** `filehandler/src/FileHandler.Api/Modules/Excel/ExcelTranslationApplier.cs#L136-L162`; `ExcelSharedStringWriter.cs#L52-L88`.

**Tái hiện:** SST có bold `A`, italic `B`; import hai slot. Dịch r0=`X`, r1=`Y` xuất cell trỏ tới `<si><t>X</t></si>`, mất `Y` và rich runs. Inline rich strings cũng bị thay toàn bộ bằng một `S.Text`. Đường identity toàn file che khuất lỗi này trong tests.

**Trước:**

```csharp
var newText = decoded.DecodedSlots[0];
var newIndex = sstWriter.ResolveOrAppend(sst, newText, null);
cell.InlineString = new InlineString(new S.Text(newText));
```

**Sau — cần triển khai:**

```csharp
var payload = CloneAndPatchStringPayload(originalPayload, verifiedBindings, decoded.DecodedSlots);
var newIndex = sstWriter.ResolveOrAppendPayload(sst, payload);
```

Giữ run properties, `xml:space`, từng decoded slot và XML children ngoài vùng cho phép. Dedup SST theo full payload canonical, không chỉ `InnerText` hoặc chuỗi fingerprint nối bằng delimiter. Cell ẩn/protected giữ index cũ. Không dùng `originalRuns` với thuật toán dồn hết text vào run đầu hiện tại làm bản sửa cuối cùng.

### F03 — CRITICAL — PPT không xác định paragraph và bảng đích

**File/dòng:** `filehandler/src/FileHandler.Api/Modules/PowerPoint/PowerPointTranslationApplier.cs#L134-L160`, `#L199-L225`; `PowerPointExtractor.cs#L160-L194`; `PowerPointTableReader.cs#L46-L79`.

**Tái hiện:** Textbox chứa `First`,`Second`, translations `One`,`Two` xuất `[Two,""]`. Hai bảng trên cùng slide có `A/B` và `C/D`, translations `AA/BB/CC/DD` xuất `[CC,DD,C,D]`. Nguồn và output có **0 schema errors**.

**Nguyên nhân:** Mỗi unit là paragraph nhưng helper quét text toàn text body. Nhánh table luôn lấy bảng đầu tiên trên slide, không dùng `ShapeId`. Việc lọc text rỗng còn làm tập target thay đổi sau mỗi mutation.

**Trước:**

```csharp
var table = slide.Descendants<A.Table>().FirstOrDefault();
var textNodes = body.Descendants<A.Text>().Where(t => !string.IsNullOrEmpty(t.Text)).ToList();
```

**Sau — cần triển khai:**

```csharp
var frame = index.GraphicFramesByShapeId[unit.Location.ShapeId!];
var paragraph = ResolveBoundParagraph(frame, unit.Location, unit.Bindings);
ApplyVerifiedSlots(paragraph, decoded);
```

Persist paragraph ordinal/full path ngay trong unit, kết hợp slide part + shape ID + row/column + paragraph. Index tất cả target trước khi mutate. Bảo vệ `a:fld` và hyperlink contexts như Word. Không xóa text của paragraph khác.

**Schema order:** Đường apply hiện tại không xóa/reinsert `a:txBody`/`a:tcPr`, nên không tìm thấy bằng chứng đảo thứ tự hai tag. Vấn đề đã xác nhận là mapping nội dung. Chưa có kết luận no-repair trong PowerPoint desktop.

### F04 — HIGH — Excel drawing import được nhưng export bỏ qua

**File/dòng:** `filehandler/src/FileHandler.Api/Modules/Excel/ExcelExtractor.cs#L212-L250`; `ExcelTranslationApplier.cs#L122-L164`.

**Tái hiện:** Worksheet có cell `Cell` và shape text `Drawing`; import nhận cả hai. Export `ChangedCell`,`ChangedDrawing` trả success nhưng drawing vẫn `Drawing`. Fixture có schema hợp lệ.

**Nguyên nhân:** Map apply chỉ chứa `WorksheetParts`. Drawing unit có `PartUri` của `DrawingsPart` nên rơi ra ngoài nhánh xử lý mà không có lỗi. Source schema selected parts cũng cần bao gồm drawings được dùng để lập plan.

**Trước:**

```csharp
if (worksheetPartsByUri.TryGetValue(unit.Location.PartUri, out var wsPart))
    ApplyCell(wsPart, unit);
```

**Sau — cần triển khai:**

```csharp
var target = ResolveRequiredTarget(unit, worksheetIndex, drawingIndex);
ApplyVerifiedUnit(target, decoded);
```

Hỗ trợ drawing paragraph bằng bindings đầy đủ hoặc reject preflight với capability error. Mọi accepted unit phải có đúng một applied/unchanged outcome; không silently continue khi target không tồn tại.

### F05 — HIGH — Prepare và Apply bất đồng về touched parts

**File/dòng:** `filehandler/src/FileHandler.Api/Modules/Word/WordTranslationApplier.cs#L55-L91`, `#L149-L192`; `Modules/Excel/ExcelTranslationApplier.cs#L51-L73`; `Modules/PowerPoint/PowerPointTranslationApplier.cs#L51-L64`.

**Tái hiện:** Chỉ đổi body trong Word có header/footer làm export fail `office_output_invalid` vì header/footer bị serialize lại dù không có trong mask. Excel chỉ đổi r1 giữ r0 gây mask rỗng nhưng apply vẫn sửa SST và worksheet, rồi CRC validation reject.

**Trước:**

```csharp
if (oldFirstSlot != newFirstSlot) touchedParts.Add(partUri);
// Apply still processes every plan unit.
```

**Sau — cần triển khai:**

```csharp
var changes = PrepareChangedBindings(plan, decodedUnits);
foreach (var change in changes)
    ApplyChange(change);
SerializeOnly(changes.Select(c => c.PartUri).Distinct());
```

Prepared patch phải chứa tập mutation cụ thể, không chỉ plan + decoded units. So sánh mọi slot; derive masks từ cùng tập mutation; bảo toàn raw payload cho untouched parts. SST URI lấy từ relationship thực tế, không hardcode `/xl/sharedStrings.xml`.

### F06 — HIGH — Word topology validator sai scope bảng

**File/dòng:** `filehandler/src/FileHandler.Api/Modules/Word/WordStructureValidator.cs#L44-L47`; `WordExtractor.cs#L80-L128`, `#L437-L454`.

**Tái hiện:** Header có một bảng, body không có bảng. Mọi unit đều đổi, tránh F05. Plan có 1 bảng; validator chỉ đếm body được 0 và từ chối output hợp lệ. Bảng trong footer/footnote/endnote có cùng vấn đề. Bảng nested chỉ chứa text rỗng có thể không được đưa vào plan nhưng vẫn tồn tại trong DOM output.

**Trước:**

```csharp
var tablesAfter = doc.MainDocumentPart.Document.Body.Descendants<W.Table>().ToList();
if (tablesAfter.Count != plan.Tables.Count) return Failure();
```

**Sau — cần triển khai:**

```csharp
foreach (var expected in plan.Tables)
    CompareTableTopology(expected, ResolveTableByLocation(output, expected.Location));
VerifyInventoryAcrossAllSelectedStories();
```

Snapshot topology tách khỏi eligibility tạo unit: bảng rỗng vẫn phải được kiểm kê. So sánh từng vị trí, row/cell counts, grid, `gridSpan`, `vMerge`, nesting và paragraph order.

### F07 — HIGH — Hai tầng validation chưa bảo vệ các bất biến đã công bố

**File/dòng:** `filehandler/src/FileHandler.Api/Modules/Office/OfficePackageValidator.cs#L55-L79`, `#L119-L164`; `Modules/Word/WordStructureValidator.cs#L44-L51`; `Modules/Excel/ExcelStructureValidator.cs#L45-L73`; `Modules/PowerPoint/PowerPointStructureValidator.cs#L44-L51`.

**Vấn đề:**

- `ValidateOutput` chỉ đọc **keys** của masks; nội dung permitted paths và SST flags không được enforce.
- Untouched payload chỉ so CRC-32 từ ZIP metadata; comment nói hash nhưng không so SHA-256 payload. CRC không là bằng chứng mật mã về tính toàn vẹn.
- Word chỉ đếm bảng; PPT chỉ đếm slide; Excel chỉ tìm table name và so column names. Chưa so relationship graph, geometry, merges, formulas, protected fields hoặc semantic target result.
- `Validate(...).Take(MaxSchemaErrors)` chạy trước lọc selected/touched part. Chạm cap vẫn có thể return success; một nhóm errors ở excluded parts có thể che lỗi cần reject phía sau.
- Source selection lấy story/worksheet/slide nhưng thiếu workbook/presentation/SST/drawings và metadata bắt buộc. Test factory PPT hiện hữu thiếu slideMaster/notesSz vẫn được service nhận do policy lọc lỗi. Baseline errors chưa lưu fingerprint/multiplicity để đối chiếu output.

**Tái hiện:** Tạo output chỉ thêm bold vào Word, mask chỉ cho `//w:t`. Cả package và structure validator đều trả valid (`edit-mask-ignored`). F01–F03 cho thấy tác hại thực tế của các validator quá yếu.

**Trước:**

```csharp
var touchedSet = new HashSet<string>(masks.Keys);
if (!touchedSet.Contains(uri) && sourceEntry.Crc32 != outputEntry.Crc32)
    fatalErrors.Add(error);
```

**Sau — cần triển khai:**

```csharp
VerifyPartAndRelationshipSets(sourceInventory, outputInventory);
CompareUntouchedPayloadHashes(sourceInventory, outputInventory);
VerifyXmlDiffAgainstExactMasks(source, output, masks);
CompareCompleteSchemaBaseline(sourceErrors, outputErrors, validationCompleted);
```

Reject khi validation bị cắt do cap; selected dependencies phải bao phủ toàn bộ dữ liệu được đọc/ghi. Reopen output và verify bindings ứng với decoded text, cộng invariants riêng từng format. Giữ source-error policy cho excluded untouched parts theo design, không thay bằng việc bỏ qua tất cả lỗi ngoài touched set.

### F08 — HIGH — Office token r/k chưa có thứ tự chung; identity lỗi với whitespace

**File/dòng:** `filehandler/src/FileHandler.Api/Modules/Office/OfficeTextCodec.cs#L61-L79`, `#L305-L325`, `#L383-L386`, `#L417-L420`; `OfficeModels.cs` (`OfficeTextTemplate`).

**Tái hiện:** Paragraph bắt đầu bằng break rồi `A` được encode thành `<ox:r0>A</ox:r0><ox:k0/>`, ngược thứ tự nguồn. Decoder chấp nhận k0 trước r0 hoặc sau r0 vì chỉ theo dõi hai counters riêng. Paragraph `A` + run bold chứa một space + `B` import thành ba r-slots, nhưng export nguyên array import trả `empty_translation`.

**Rủi ro:** Translation engine thấy anchor sai chỗ; thay đổi vị trí protected token không được phát hiện. Một số nguồn hợp lệ không thỏa identity dù không thay nội dung.

**Trước:**

```csharp
while (hasSlots || hasAnchors) { AppendNextSlot(); AppendNextAnchor(); }
if (string.IsNullOrWhiteSpace(slotText)) return EmptyTranslation();
```

**Sau — cần triển khai:**

```csharp
foreach (var part in template.OrderedParts)
    EncodePart(part);
ValidateExactPartSequence(template.OrderedParts, parsedParts);
```

Dùng ordered union của Slot/Anchor theo source traversal, như hướng `MarkdownTokenTemplate.Parts`. Whitespace-only/source-empty text cần protected anchor hoặc policy slot không dịch; import phải tạo wire mà decoder chấp nhận. Kiểm tra roundtrip `Decode(Encode(source))` cho mọi template hợp lệ, kể cả leading/trailing/consecutive anchors.

### F09 — HIGH — Chunked bypass tổng multipart quota

**File/dòng:** `filehandler/src/FileHandler.Api/Program.cs#L99-L100`, `#L111-L120`; `Controllers/FilesController.cs#L116-L127`.

**Tái hiện HTTP:** Host đặt `MaxMultipartBytes=4096`. Body 5.453 bytes gồm file nhỏ và hai fields 2.600 bytes. Content-Length được trả 413; cùng body gửi chunked được trả 200 `["Hello"]`.

`FormOptions.MultipartBodyLengthLimit` giới hạn **mỗi multipart section**, không phải tổng request. Vì vậy Content-Length check không bảo vệ requests không biết trước chiều dài. Kestrel có limit riêng, nhưng nó không tự đồng bộ với option của ứng dụng. [Microsoft FormOptions](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.http.features.formoptions.multipartbodylengthlimit?view=aspnetcore-10.0).

**Trước:**

```csharp
if (context.Request.ContentLength > limits.MaxMultipartBytes) return;
```

**Sau — cần triển khai trước mọi lần đọc body/model binding:**

```csharp
var feature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
if (feature is { IsReadOnly: false }) feature.MaxRequestBodySize = limits.MaxMultipartBytes;
context.Request.Body = CreateCountingReadStream(context.Request.Body, limits.MaxMultipartBytes);
```

Cần thực thi một giới hạn tổng byte thực đọc, giữ per-section limits, đồng bộ Kestrel/IIS/proxy và error mapping. Dùng dedicated counting read stream; `OfficeBoundedStream` hiện tại là wrapper write/seek, không áp trực tiếp cho non-seek HTTP input. Giới hạn số lượng/độ dài JSON phần tử trước khi materialize toàn array nếu có thể.

### F10 — HIGH — XML limits có thể né bằng tên part không kết thúc `.xml`

**File/dòng:** `filehandler/src/FileHandler.Api/Modules/Office/OfficePackageReader.cs#L243-L251`.

**Tái hiện:** Word XML bình thường với `MaxXmlDepth=2` bị reject ở depth 3. Đổi part thành `/word/document.dat`, chỉnh Content_Types/relationship đúng OPC: reader accept, schema nguồn độc lập vẫn hợp lệ. XML nội dung không thay đổi.

**Rủi ro:** Bypass `MaxXmlDepth`, `MaxXmlNodes`, `MaxXmlCharactersPerPart` khi SDK parse part theo content type. Đây là bypass quota đã xác nhận, **không phải kết luận XXE đã khai thác**. DTD probe hiện tại trên `.xml` bị chặn đúng; không có network entity fetch được chứng minh.

**Trước:**

```csharp
if (!name.EndsWith(".xml") && !name.EndsWith(".rels")) continue;
```

**Sau — cần triển khai:**

```csharp
var contentType = contentTypes.Resolve(partUri);
if (IsXmlContentType(contentType) || IsRelationshipPart(partUri))
    ScanWithBoundedSecureXmlReader(entry);
```

Preflight Content_Types bằng secure reader, xác định mọi XML part theo MIME/SDK semantics. Dùng `DtdProcessing.Prohibit`, `XmlResolver=null`, char/depth/node quotas cho tất cả nhánh và `MaxCharactersInPart` làm lớp bảo vệ bổ sung khi SDK mở package.

### F11 — HIGH — Office quotas và error classification chưa thống nhất

**File/dòng:** `filehandler/src/FileHandler.Api/Modules/Word/WordService.cs#L148-L152`, `#L229-L249`, `#L288-L293`; Excel/PowerPoint services có cùng pipeline; `OfficeBoundedStream.cs#L129-L139`; `OfficePackageInspector.cs#L113-L129`; `WordExtractor.cs#L139-L140`; `Program.cs#L171-L178`.

**Tái hiện:**

- `MaxUnits=1`, source 2 units: import trả `too_many_units`; export identity vẫn success. Export ba format không kiểm tra MaxUnits.
- `MaxPlanChars=1`, source `Hello`: error là `office_unsupported_content` (422), không phải `office_plan_limit_exceeded` (413).
- `MaxOutputBytes=100`, Office export có thay đổi: bounded stream throw `InvalidOperationException`; HTTP trả 500/internal_error, thay vì 413/output_too_large. Check sau `FinalizeAsync` không chạy khi bounded stream đã throw, kể cả khi ZIP đang đóng central directory.
- Duplicate relationship ID: inspector throw, HTTP trả 500/internal_error thay vì 422/invalid_office_package.
- `MaxObjects` chỉ được khai báo/validate option; không có enforcement. MaxTokensPerUnit chỉ được kiểm khi decode structured export, không áp vào lúc tạo plan import.

**Trước:**

```csharp
catch (InvalidOperationException ex)
    { return UnsupportedContent(ex.Message); }
var output = await session.FinalizeAsync(cancellationToken);
```

**Sau — cần triển khai:**

```csharp
ValidatePlanBudgetsForImportAndExport(plan);
// Map only known boundary failures, preserve cancellation and unexpected bugs.
catch (OfficeQuotaExceededException ex) { return QuotaFailure(ex.Code); }
catch (InvalidOfficePackageException) { return InvalidPackageFailure(); }
```

Enforce counters ngay khi tạo objects/units/tokens, tránh build xong mới so. Dedicated error types/result codes cho input-invalid, quota, unsupported và unexpected. Không catch mọi exception thành 422. Chuẩn hóa `file_too_large`/`request_too_large` theo cùng contract; thêm tests end-to-end cho status, code, không output, source unchanged.

**Payload hiện tại:** `code`, `message`, optional `index`, `line`, `marker`; không có `target`. `line` của Office vắng mặt là phù hợp design đã chốt. Nếu schema yêu cầu mới bắt buộc `target`, cần version/đồng bộ DTO + OpenAPI + tests; không tự thêm chỉ ở một format.

### F12 — HIGH — Scope guard thiếu, nên có thể dịch nội dung đã quy định excluded/unsupported

**File/dòng:** `filehandler/src/FileHandler.Api/Modules/Word/WordExtractor.cs#L186-L191`, `#L533-L557`; `Modules/Excel/ExcelExtractor.cs#L95-L158`; design `office-implementation-design.md#L43-L59`.

**Tái hiện:** Bound Word SDT có `w:dataBinding` được import và đổi `BOUND` thành `CHANGED`, success/schema-valid. Cột A Excel đặt `Hidden=true` vẫn import và dịch `Hidden`; sheet ẩn và row ẩn có check nhưng column không có.

**Rủi ro:** Bound SDT có thể bị Word đồng bộ lại từ custom XML và mất bản dịch; text ngoài visibility scope bị gửi đi dịch. Guard revisions/chart ở Word mới kiểm main body/main parts; header/footer, MC AlternateContent, locked SDT, phonetic annotations cần review traversal đầy đủ, chưa thể tuyên bố fail-closed theo profile.

**Trước:**

```csharp
WalkContainerElements(sdt.SdtContentBlock, ...);
if (row.Hidden?.Value == true) continue;
```

**Sau — cần triển khai:**

```csharp
RejectBoundOrLockedSdt(sdt);
if (visibility.IsHidden(sheet, row, columnIndex)) continue;
RequireSupportedCapabilityForEverySelectedTextObject();
```

Đối chiếu inventory với capability matrix đã chốt. Không mở scope SDT binding, revisions hoặc MC dual text ngầm. Kiểm tra các story thực sự được section/footnote references chọn, tránh lấy orphan parts làm units.

### F13 — HIGH — Redaction chỉ áp dụng JSON trace, chưa áp dụng log thường

**File/dòng:** `filehandler/src/FileHandler.Api/Program.cs#L173`; `Modules/Office/OfficePackageInspector.cs#L115`; `Diagnostics/DebugTrace.cs#L603-L651`.

**Tái hiện:** Host `CaptureContent=false`; synthetic duplicate relationship ID `SENSITIVE_SENTINEL` xuất hiện trong `server.log` qua `logger.LogError(exception, ...)`. JSON trace đã che dữ liệu nhưng logger nhận toàn exception, message chứa input metadata.

**Trước:**

```csharp
logger.LogError(exception, "Unhandled file handling failure (request content omitted).");
```

**Sau — cần triển khai:**

```csharp
logger.LogError("File failure {ErrorCode}; type {ExceptionType}; request {RequestId}",
    safeErrorCode, exception.GetType().Name, context.TraceIdentifier);
```

Sanitize tại nơi tạo exception/public errors; chỉ giữ metadata đã cho phép. Xem cả framework exception logging và provider bên ngoài: đổi một call site không đủ bảo đảm mọi sink. Test logger + persisted trace cùng sentinel trong filename, relationship IDs, URI, message và schema error; JSON trace đơn lẻ pass chưa đủ.

**Điều kiện triển khai:** Debug API đang public ở code (`DebugController` không có authorization), trace enabled/capture true trong appsettings, không có retention tự động. Lịch sử dự án yêu cầu giữ debug routes ở Production; review không đề nghị vô hiệu hóa trái yêu cầu. Cần biên mạng/ủy quyền và retention phù hợp khi deploy. Chưa kiểm tra ingress thực tế nên không khẳng định dữ liệu đang lộ ra Internet.

### F14 — MEDIUM — Nhiều thuật toán vẫn O(N²)

**File/dòng:** `filehandler/src/FileHandler.Api/Modules/Word/WordExtractor.cs#L465-L489`; `WordTranslationApplier.cs#L267-L293`; `Modules/Excel/ExcelTranslationApplier.cs#L148`; `Modules/PowerPoint/PowerPointTranslationApplier.cs#L155-L156`; `Modules/Markdown/MarkdownTranslationApplier.cs#L263-L332`.

**Phân tích:**

- Word dựng path mỗi paragraph bằng quét sibling từ đầu: N paragraph cùng body tạo khoảng N(N−1)/2 visits. Apply lại resolve bằng quét sibling.
- Excel tìm cell bằng `worksheet.Descendants<Cell>().FirstOrDefault(...)` cho từng unit: U×C.
- PPT tìm shape từ đầu slide cho từng unit; paragraph có nhiều unit lặp scan toàn text body.
- Markdown forward composition tốt, nhưng `CanonicalizeFormattingTokens` còn nested scans và `List.Insert` gây dịch phần tử. Protected-marker check còn tìm trong translation cho từng marker; sort replacements làm riêng bước ordering O(U log U). Vì vậy không thể gọi toàn pipeline O(N).

**Đo trên Word Analyze riêng, median 3 lần sau warm-up, tiered compilation tắt:**

| Paragraphs | Median |
| --- | ---: |
| 2.000 | 132 ms |
| 4.000 | 541 ms |
| 8.000 | 1.916 ms |

Số đo phụ thuộc máy; tăng gần 4 lần khi gấp đôi input phù hợp phân tích vòng lặp, không là SLA production.

**Trước:**

```csharp
var cell = worksheet.Descendants<Cell>().FirstOrDefault(c => c.CellReference == cellRef);
```

**Sau — cần triển khai:**

```csharp
var cells = BuildCellIndexOnce(worksheet);
var cell = cells[cellRef];
```

Word dựng path trong DFS bằng counters theo parent/QName, resolve target index một lần. Markdown precompute matching pairs bằng stack, emit một forward buffer; bỏ repeated `Insert`. Đo scaling sau sửa với corpus paragraph/cell dày và cancellation.

### F15 — MEDIUM — Giới hạn bộ nhớ được kiểm quá muộn

**File/dòng:** `filehandler/src/FileHandler.Api/Modules/Office/OfficePackageReader.cs#L87-L89`, `#L185-L189`; `Modules/Word/WordService.cs#L121-L125`; `Modules/Office/OfficePackageInspector.cs#L63-L71`; `Modules/Word/WordTranslationApplier.cs#L187-L192`; `Modules/Office/OfficeExportSession.cs#L99-L104`, `#L131-L161`; `Modules/Markdown/MarkdownService.cs#L92-L104`; `Diagnostics/DebugTrace.cs#L715-L721`.

**Vấn đề:** Office đọc toàn source vào MemoryStream rồi ToArray, scan/decompress/CRC/XML xong mới service kiểm MaxFileBytes. Mỗi part được decompress/copy nhiều lần. Serialized touched part cũng có byte array hoàn chỉnh trước `WritePart` kiểm MaxPartBytes. Markdown compose string, parse lại AST, encode byte array rồi mới so MaxOutputBytes. Đây là quota về kết quả, chưa phải giới hạn cấp phát peak. Trên HTTP vẫn có giới hạn host, nên không gọi mọi allocation là “vô hạn”; request hợp lệ trong body quota vẫn có amplification và nhiều bản sao.

**Trước:**

```csharp
await sourceStream.CopyToAsync(ms, cancellationToken);
var bytes = ms.ToArray();
// Preflight, then service checks MaxFileBytes.
```

**Sau — cần triển khai:**

```csharp
await CopyInputWithActualByteBudget(sourceStream, spool, maxFileBytes, cancellationToken);
await SerializePartWithExpandedByteBudget(root, partSpool, cancellationToken);
ValidateProjectedOutputBytesBeforeComposition(replacements);
```

Stream SHA/CRC thay vì ToArray khi chỉ cần hash, dùng pooled buffers/spool có ngưỡng RAM, giữ tổng expanded output budget. `MemoryStream.TryGetBuffer` của UTF-8 giảm một số bản sao nhưng vẫn cần copy khi capacity khác logical length; không đồng nghĩa zero-LOH.

**Trace overhead đo thực tế:** 300 paragraph Word import, median 5 lượt sau warm-up, gồm flush file trace; tiered compilation tắt.

| Trace mode | Median ms | Managed allocated/request | Trace bytes |
| --- | ---: | ---: | ---: |
| Off | 9,80 | 3.384.424 | 0 |
| CaptureContent=false | 9,98 | 3.940.715 | 152.893 |
| CaptureContent=true | 11,65 | 4.225.336 | 200.409 |

Ở fixture này, capture tăng khoảng 19% thời gian và 25% allocations; redacted tăng khoảng 16% allocations. Sentinel không có trong trace redacted, có trong capture. Không ngoại suy thành throughput/p99; không đo peak RSS.

TraceValue cache reflection là điểm tốt; snapshot có depth/node/list limits. Tuy nhiên `MaxValueLength` không phải tổng serialized-event/file byte budget; Markdown snapshots còn materialize `container.ToList()`/`blocks.ToArray()` trước truncation. `Flush` serialize toàn cây thành string rồi `WriteAllText` đồng bộ; log listing dùng `JsonDocument.Parse(stream)` vẫn materialize document, không phải streaming selective parsing. Đề xuất cap byte tổng, stream JSON, bounded queue/retention và list summary index. Tránh tăng MaxEvents như cách chữa thiếu trace.

### F16 — MEDIUM — Markdown soft break chưa tuân thủ token protection mới

**File/dòng:** `filehandler/src/FileHandler.Api/Modules/Markdown/MarkdownExtractor.cs#L276-L281`; `#L140-L145`; `MarkdownTokenCodec.cs#L24-L39`.

**Tái hiện:** `Alpha\r\nBeta\r\n` import thành literal `Alpha\nBeta` không có k token. Export `Gamma Delta` thành công và mất soft break bên trong unit. Hard break có protected path và regression tests.

**Trước:**

```csharp
case LineBreakInline:
    sb.Append('\n');
    break;
```

**Sau — theo bất biến mới của yêu cầu:**

```csharp
case LineBreakInline lineBreak:
    AddProtectedSourceBreak(lineBreak, source, markers, output);
    break;
```

Giữ source slice gồm EOL/prefix cần thiết; đưa soft break vào ordered token template và structure signature. Đây là thay đổi với hành vi soft-newline cũ, cần migration/version note cùng tests; không đánh đồng việc gộp soft break với mất hard break hoặc hỏng schema Markdown. TXT tiếp tục literal theo contract.

### F17 — MEDIUM — OpenSettings chưa áp dụng thống nhất

**File/dòng:** `filehandler/src/FileHandler.Api/Modules/Word/WordExtractor.cs#L56-L57`, `WordService.cs#L281-L282`; `Modules/Excel/ExcelExtractor.cs#L57-L58`; `Modules/PowerPoint/PowerPointExtractor.cs#L56-L57`; các structure validators tương tự. Helper có settings đầy đủ mới nằm private trong `OfficePackageValidator.cs#L192-L209`.

**Vấn đề:** Bất biến yêu cầu explicit read-only + AutoSave=false + NoProcess. Nhiều call site chỉ truyền `false`; AutoSave mặc định true theo SDK. [Microsoft OpenSettings.AutoSave](https://learn.microsoft.com/en-us/dotnet/api/documentformat.openxml.packaging.opensettings.autosave?view=openxml-3.0.1).

**Không suy diễn:** Chế độ read-only hiện tại và probe source hashes không cho thấy source bị ghi. Đây là vi phạm cấu hình bất biến và thiếu một lớp hardening, không là bằng chứng source mutation đang xảy ra.

**Trước:**

```csharp
using var doc = WordprocessingDocument.Open(ms, false);
```

**Sau:**

```csharp
var settings = new OpenSettings
{
    AutoSave = false,
    MaxCharactersInPart = options.MaxXmlCharactersPerPart,
    MarkupCompatibilityProcessSettings = new(
        MarkupCompatibilityProcessMode.NoProcess, FileFormatVersions.Office2019)
};
using var doc = WordprocessingDocument.Open(ms, false, settings);
```

Đưa helper open/settings/stream ownership vào Office shared infrastructure, dùng cho tất cả read/validate/apply DOM sessions. Không thêm phụ thuộc ngược từ Office vào format modules.

### F18 — LOW — Convention/docs chưa đạt mức tuyên bố 100%

**File/dòng ví dụ:** `filehandler/src/FileHandler.Api/Modules/Office/OfficeBoundedStream.cs#L50-L90`; `Modules/Word/WordService.cs#L99-L100`; `Diagnostics/DebugTrace.cs#L58-L59`, `#L133-L136`; `filehandler/Directory.Build.props` và `src/FileHandler.Api/FileHandler.Api.csproj`.

**Audit:** 99 files/816 declaration nodes. Không có declaration mất hoàn toàn XML documentation trong tập audit. Có 27 declaration chỉ dùng `<inheritdoc/>`, nên thiếu explicit summary/param/returns mà AGENTS yêu cầu; compiler chấp nhận inheritdoc. Có 8 vị trí chứa wording/article cần review, không tự coi mọi từ “the” đều lỗi ngữ pháp. XML structural checks, record params, enum members và khoảng cách dòng trong tập audit không phát hiện nhóm lỗi mới khác.

**Trước:**

```csharp
/// <inheritdoc />
public override void Flush() => _inner.Flush();
```

**Sau:**

```csharp

/// <summary>
/// Flushes buffered writes to underlying stream.
/// </summary>
/// <returns>No return value.</returns>
public override void Flush() => _inner.Flush();
```

Build XML không kiểm được đầy đủ private docs, mandatory returns, một dòng trống hoặc nội dung comment. Dùng audit/analyzer làm CI check phù hợp repository. Đồng bộ mô tả C# 13 với `LangVersion=14.0` thực tế hoặc pin language version theo quyết định riêng; không đổi compiler chỉ vì mô tả đầu bài.

## 4. Test Gaps

### Tại sao 345 tests pass vẫn lọt lỗi

- Có **52** tests thuộc Office family, không phải toàn bộ 111 acceptance items trong design đã được thực hiện. Test names/comments kiểu `E01–E18`, `AT01–AT10` không chứng minh đủ coverage.
- `ExcelModuleTests.E02_SharedStrings_ExtractsAndTranslates` chỉ assert không lỗi và có output, không resolve từng SST index để so expected text.
- PPT test hai slide, mỗi slide một textbox một paragraph không kích hoạt lỗi nhiều paragraph/bảng cùng slide. Rich identity bypasses mutation path.
- Test factory PPT hiện hữu thiếu một số phần schema bắt buộc; suite không assert toàn-package baseline zero. Review probes đã bổ sung fixture master/notesSize riêng để không quy lỗi sai nội dung cho input malformed.
- Bounded stream/session tests assert InvalidOperationException, nhưng thiếu end-to-end expectation 413. Các tests này chứng minh primitive throw, không chứng minh API contract đúng.

### Test cần thêm trước deploy — P0

1. **Word binding matrix:** nhiều `w:t` trong một run; nhiều run cùng fingerprint; hai hyperlink khác nhau cùng paragraph; formatting `rStyle`, `vertAlign`, strike/RTL; empty `w:t`; leading protected field; field begin/separate/end lồng và qua paragraph; field cache không tạo unit; textbox/nested table không bị quét lẫn.
2. **Word story/table:** header/footer chứa table; nested table 3 cấp có cell trống; vMerge restart/continue và gridSpan phức tạp; bảng trong footnote/endnote; chỉ đổi một story, những story khác payload byte-identical.
3. **Excel rich text:** SST/inline một và nhiều rich runs, chỉ đổi slot cuối, bản dịch từng slot có leading/trailing whitespace; mọi cell resolve đúng index/text/properties. Shared index giữa visible/Hidden/VeryHidden/hidden row/column; hai bản dịch khác nhau cho cùng source index.
4. **Excel metadata:** table header/totals cùng SST với data cell; structured-reference formula và cached string giữ nguyên; SST URI không mặc định; duplicate entries, `xml:space`, phonetic unsupported; drawing text thực sự được đổi; unsupported object phải reject preflight.
5. **PPT:** một shape nhiều paragraph, paragraph rỗng xen kẽ; hai tables trên cùng slide; group shape; một cell nhiều paragraph; rich run cùng/khác formatting; a:fld đầu/giữa/cuối; merged cells; tcPr order/geometry và hyperlinks giữ nguyên.
6. **Token algebra:** `Decode(Encode(source))` thành công cho mọi template hợp lệ; leading/trailing/consecutive k, whitespace-only source, empty rich runs, thứ tự r/k chung, escaped `<`/backslash, missing/duplicate/reordered/nested/malformed tokens. Identity byte-for-byte cho nguồn biên, không chỉ tài liệu một run.
7. **Validation fault injection:** đổi field/formula/style/relationship/merge ngoài mask nhưng vẫn schema-valid phải reject; cùng CRC nhưng payload khác không được coi là identical; thiếu/đổi target không được silent continue; errors vượt cap không được success; baseline excluded errors giữ fingerprint/multiplicity.
8. **Quotas/API:** Content-Length và chunked cho tổng nhiều sections; non-seek stream; actual bytes vượt limit; XML part có suffix `.dat`; exact-boundary và +1 cho mọi quota; MaxUnits import/export như nhau; MaxObjects thực sự chạy; output quota ở serialize/write/ZIP close trả 413, không output.
9. **Input lỗi/trace:** duplicate/dangling rel, malformed content types, DTD/entity cả MIME XML có extension lạ, invalid XML chars; status/code consistent; mọi log sink không chứa sentinels khi redacted; request cancellation và lỗi disk không tạo partial publication hoặc lỗi giả thành công.

### P1 — Performance và compatibility

- Scaling 1k/2k/4k/8k/10k paragraphs/cells; dữ liệu rich/anchors dày; verify growth gần tuyến tính sau remediation.
- Managed allocations, peak working set, Gen2/LOH, p95/p99; concurrent requests với trace off/redacted/capture; quota rejection phải rẻ và có cancellation.
- Real Office corpus, mở/save không repair trong Word/Excel/PowerPoint; inspect XML + render/layout, không chỉ OpenXmlValidator.
- Markdown nested formatting dày, soft/hard breaks, CR/LF/CRLF hỗn hợp, BOM, replacement byte amplification; TXT empty file/blank-only/separators/literal protocol strings tiếp tục pass.
- Office source hash/bytes giữ nguyên cả success, validation failure, cancellation, serialization exception và ZIP close exception.

## 5. Prioritized Action Plan

### P0 — Trước deploy

1. **Chốt invariant tests trước khi sửa:** chuyển probe đúng/sai thành acceptance tests với expected đúng; bảo đảm fixture schema-valid. Khóa các ca mất dữ liệu F01–F04 bằng assertions trên vị trí và formatting, không chỉ array text hoặc success status.
2. **Sửa model plan/patch:** ordered token parts, locators đầy đủ, bindings đã verify, changed-only mutations. Triển khai Word/Excel/PPT appliers trên contract này; không vá bằng cách nới validator.
3. **Hoàn thiện validation:** enforce edit masks, required dependency schema coverage, reject truncated validation, topology/relationship/baseline comparison. Sửa scope bảng Word và kiểm actual translated results.
4. **Đóng các đường quota/scope:** tổng multipart thực đọc, XML classification theo content type, import/export cùng budgets, typed errors/HTTP mapping, hidden columns và bound SDT guards.
5. **Che thông tin lỗi qua mọi log sink:** xác nhận runtime logging + JSON trace khi CaptureContent=false. Giữ khả năng debug Production theo quyết định hiện hữu, quản lý quyền truy cập và lưu trữ ở deployment boundary.

**Exit criteria:** Toàn bộ P0 regression tests pass; không có accepted unit bị bỏ qua; không thay XML ngoài mask; identity đúng byte-for-byte cho edge cases; quotas đúng status/code; source immutable cả khi lỗi; corpus Office đại diện vượt no-repair gate. Chưa coi “345 tests pass” là exit criterion đủ.

### P1 — Sprint tiếp theo

- Thay sibling/cell scans bằng indices/traversal counters; bỏ nested scans/List.Insert còn lại trong Markdown.
- Đặt input/output/part budgets trước cấp phát; stream hash/JSON, spool khi cần; benchmark peak RAM và concurrent throughput.
- Đồng bộ SDK OpenSettings; hoàn thiện soft-break token migration; bổ sung cancellation ở các vòng XML/validation dài.
- Trace byte caps, bounded asynchronous flush, retention và summary index cho log viewer; đo overhead trên workload thật.

### P2 — Nợ kỹ thuật và mở rộng

- Enforce XML documentation bằng analyzer/audit CI; chỉnh wording và đồng bộ SDK/C# version docs.
- Giữ peer modules; giảm duplication pipeline ba service bằng helpers nhỏ cho validation/quota/error mapping khi đã có regression tests, không tạo OfficeService dispatch ngược.
- Chart/SmartArt, revisions, bound SDT synchronization và các scope mở rộng vẫn là O7/O8; chỉ mở sau fixture/cache/dependency và interoperability gates tương ứng.

### Những điều không được kết luận quá mức

- **Không xác nhận Zip Slip ghi ra filesystem:** code không extract entries thành đường dẫn filesystem. Path canonicalization/OPC malformed-input handling vẫn cần tests; không có primitive ghi file tùy ý được tìm thấy.
- **Không xác nhận ZIP bomb bằng forged-length probe:** .NET 10 giới hạn lượng đọc theo declared size và CRC check reject probe. F10 là XML quota bypass khác, có bằng chứng riêng.
- **Không xác nhận XXE/SSRF:** `.xml/.rels` preflight có DTD Prohibit và resolver null; external relationships được lưu metadata, không có code tải URL. Cần bao phủ mọi MIME XML như F10 trước khi tuyên bố hardening hoàn chỉnh.
- **Không xác nhận source mutation hiện tại:** các probe giữ source bytes; F17 yêu cầu settings explicit là defense-in-depth/convention invariant.
- **Không coi SST optional counters là lỗi ISO:** bỏ cả hai là hợp lệ; cần kiểm Office desktop riêng.
- **Không có bằng chứng đảo `a:txBody`/`a:tcPr` trong apply hiện tại:** PPT lỗi mapping vẫn đủ nghiêm trọng ngay cả khi schema order đúng.
- **Tên file export:** `FileTypeDetector.GetFileName` cắt cả slash/backslash; output đi qua framework FileContentResult, không dùng client filename làm filesystem path. Bộ test hiện tại xác nhận giữ basename/extension/case. Không tìm thấy bypass path traversal ở luồng này.
