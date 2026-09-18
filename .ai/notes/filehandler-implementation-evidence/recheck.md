# Kiểm tra apply sau gián đoạn — 2026-09-18 [CDX]

## Kết luận

Không phát hiện patch đã lên kế hoạch bị bỏ sót do rate limit trong working tree hiện tại. Đã đối chiếu mã nguồn, các điểm nối service/controller, regression tests và chạy lại probe; không chỉ dựa vào trạng thái tasks/handoff. Lượt này không sửa production code.

## Đối chiếu F01–F18

| Mục | Mã hiện diện / kiểm chứng |
|---|---|
| F01 | WordTranslationApplier gọi OfficeTextBindings.Apply; probe grouped-runs và protected field đúng text, source không đổi. |
| F02 | ExcelTranslationApplier ApplyClone mọi slot, ExcelSharedStringWriter giữ complete payload; rich SST và second-slot probes đúng. |
| F03 | PowerPointTranslationApplier dùng exact paths; hai paragraph và hai table cập nhật độc lập. |
| F04 | Nhánh drawing Excel gọi Apply và WritePart; drawingText=ChangedDrawing. |
| F05 | Prepare/Apply đều kiểm Changed; partial story export không lỗi. |
| F06 | WordStructureValidator kiểm bảng theo selected story; header-table probe thành công. |
| F07 | OfficePackageValidator bắt buộc OfficeXmlInvariant cho touched part, SHA-256 cho untouched; unauthorized bold bị reject. |
| F08 | OfficeTemplateBuilder.Order và OfficeTextCodec kiểm thứ tự; whitespace identity byte-identical; regression đảo token bị reject. |
| F09 | Program đặt MaxRequestBodySize và LimitedReadStream trước binding; test ChunkedMultipart_EnforcesAggregateByteLimit pass. |
| F10 | OfficePackageReader scan MIME XML; supplemental renamed .dat bị chặn quota như .xml. |
| F11 | Ba service có LimitedReadStream, MaxUnits trước identity, FileLimitException; probe unit/output trả đúng code; controller map quota 413. |
| F12 | Word guards bound/locked SDT, MC; selected headers; Excel hidden columns/merge guards; probes bound SDT/hidden column đúng. |
| F13 | GlobalExceptionHandler chỉ log type/request ID; test xác nhận không có secret/raw exception trong sink. |
| F14 | Path/cell indexes, StringBuilder, scalar/anchor hash cache, hidden-column difference array, Markdown next/previous literal arrays đã hiện diện. |
| F15 | OfficeUnitCollection/template/serialize giới hạn trước giữ thêm dữ liệu; Markdown kiểm output bytes; BoundedTraceFile và async flush wired vào middleware. |
| F16 | MarkdownExtractor.AddSoftBreak tạo protected marker; xóa break trong probe bị reject. |
| F17 | Các OpenXmlPackage.Open ở extractors/services/validators đều truyền Settings với read-only, AutoSave=false, NoProcess. |
| F18 | Rebuild bật XML docs 0 warning/error; formatter exit 0; audit 108 files/918 declarations/0 findings. |

## Các patch từng bị gián đoạn

Đã xác nhận đầy đủ: hiddenColumnChanges và chặn khoảng trước index; Missing source slot binding; _scalarHashes/_anchorHashes; bỏ dòng trống thừa ExcelSharedStringWriter; MaxTraceBytes=4194304 trong appsettings; README .NET10/C#14, trace budget, migration; tasks không còn hai entry remediation trùng; handoff/decisions đã cập nhật.

## Kết quả chạy lại

- Rebuild Release với GenerateDocumentationFile=true: 0 warning, 0 error.
- 359/359 tests, 0 skipped; [TRX mới](test-results/recheck.trx).
- Formatter verify và git diff --check: exit 0.
- Roslyn audit: 0 findings.
- Chạy lại 24 runtime observations và 4 supplemental observations trên mã hiện tại; kiểm kết quả XML/text/error codes.
- [Manifest SHA-256](recheck-source-manifest.json) ghi source/config/test/docs để xác định snapshot đã kiểm.

HTTP Kestrel evidence là lượt 2026-09-17; lượt này chạy lại test API chunked, không tuyên bố đã chạy lại host HTTP. Probe cũ office-anchor-order dùng alternativeOrder nay trùng source order nên accepted=true là hợp lệ; test mới kiểm reorder reject. Structure validator riêng vẫn có thể chấp nhận mutation style, nhưng package XML invariant bắt buộc đã reject trước publish.

Kết luận chỉ xác nhận đầy đủ patch trong phạm vi remediation và các kiểm tra nêu trên; không chứng minh phần mềm không còn lỗi. Office desktop/no-repair, production load, online CVE và retention vận hành vẫn chưa hoàn tất. Thay đổi đang ở working tree, chưa commit/deploy.
