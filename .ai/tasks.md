# Danh sách task

Theo dõi tiến độ công việc. Cả Antigravity `[AGY]` và Codex `[CDX]` đều cập nhật file này.

## Trạng thái

| Ký hiệu | Ý nghĩa |
|----------|----------|
| `[ ]` | Chưa bắt đầu |
| `[/]` | Đang thực hiện |
| `[x]` | Hoàn thành |
| `[!]` | Bị chặn / cần review |

## Task đang hoạt động

*Không còn task đang mở.*

## Task đã hoàn thành

### [AGY] 2026-09-12 18:40 — Refactor source, tối ưu hiệu năng, DI architecture và syntax (`+07:00`)

- [x] **A3 & A4 (MarkdownExtractor):** Loại bỏ cấp phát chuỗi trong `AddHardBreak` (kiểm tra ký tự trực tiếp) và `FindNewlinePolicy` (dùng `ReadOnlySpan<char>`).
- [x] **A5 & A6 (DebugController):** Tối ưu `TryGetProp` dùng `stackalloc Span<char>` cho zero-allocation property lookup; bọc `File.Delete` bằng try-catch an toàn.
- [x] **B1 (MarkdownService DI):** Loại bỏ bastard injection; constructor tường minh nhận `IMarkdownExtractor`; bổ sung static factory `Create()` cho test.
- [x] **B2 (DebugTraceMiddleware):** Cache đường dẫn thư mục log trace tránh phân giải và kiểm tra I/O lặp lại trên mỗi request.
- [x] **B3 (TraceValue Reflection Cache):** Thêm `ConcurrentDictionary<Type, PropertyInfo[]>` cache phản chiếu thuộc tính cho mọi đối tượng snapshot.
- [x] **C1–C6 (Syntax & Conventions):** Rút gọn nullable comparison; dùng `Path.GetFileName`; pre-allocate capacity danh sách JSON; property pattern matching `{ IsHard: true }`; raw string literal cho OpenAPI example.
- [x] **D1 & D2 (Memory & Allocations):** Tối ưu `Utf8TextReader` dùng `TryGetBuffer` giảm áp lực LOH; fast-path cho `NormalizeJsonStringNewlines`.
- [x] **G2 (Test Suite Concurrency):** Thay thế `Barrier` chặn thread đồng bộ bằng `TaskCompletionSource` bất đồng bộ, triệt tiêu nguy cơ ThreadPool starvation.
- [x] **Kiểm chứng:** 274/274 tests pass; build Release XML docs 0 warning, 0 error; formatter sạch không thay đổi; tuân thủ nghiêm ngặt `AGENTS.md`.

### [CDX] 2026-09-12 18:20 — Review và đặt lại trace state (`+07:00`)

- [x] Rà soát toàn bộ call site state, scope, trace infrastructure và các luồng import/export Markdown/TXT.
- [x] Sửa state loại file chưa nhận diện; chuyển item extraction về trước xử lý; tập trung buffer ở một tầng, bổ sung marker/patch/normalization/decision/stage và metadata ở nhánh quota/error.
- [x] Thêm 18 test JSON trace; cô lập test toggle dùng cờ toàn process sau khi tái hiện race với test HTTP.
- [x] 274/274 tests pass; build Release XML docs 0 warning/error; formatter sạch; audit 56 file/465 khai báo không lỗi; cập nhật README, review note, decisions và handoff.

Chi tiết: [Review trace state](notes/trace-state-review.md).

### Hỗ trợ `.txt` — hoàn thành `[CDX]` 2026-09-12 17:58 (`+07:00`)

Chi tiết: [Kế hoạch hỗ trợ TXT](notes/plain-text-support-plan.md). Người dùng đã yêu cầu implement theo kiến trúc trình bày, chia unit theo đoạn ngăn bởi dòng trống.

- [x] T1 — Baseline Release 176/176 tests pass; áp dụng contract chia đoạn trong plan.
- [x] T2 — Tách `Utf8TextSource`/`Utf8TextReader`, giữ adapter/line map Markdown; 176 test cũ vẫn pass.
- [x] T3 — Scanner đoạn một lượt, giữ span/line/separator, Unicode whitespace, CR/LF/CRLF, quota và cancellation.
- [x] T4 — `PlainTextService` import/export literal, validate count/null/empty/Unicode/length, identity và output budget trước cấp phát.
- [x] T5 — Detector `.txt`/`.TXT`, dispatch controller/DI, filename an toàn, MIME; cập nhật test âm tính `.pdf`.
- [x] T6 — Trace/redaction TXT, OpenAPI binary/JSON đúng status, README; smoke HTTP Kestrel TXT/Markdown đúng bytes BOM/CRLF và attachment.
- [x] T7 — 256/256 tests pass; build Release XML docs 0 warnings/errors; format verify không đổi; audit Roslyn 54 file/447 khai báo không lỗi; cập nhật bàn giao.

- [x] `[CDX]` Lập kế hoạch chi tiết hỗ trợ `.txt` dựa trên source hiện tại; xác định contract, kiến trúc tối thiểu, thuật toán, backlog và test matrix. Chỉ cập nhật `.ai/`, chưa triển khai (2026-09-12 10:04, `+07:00`).

### Xử lý sau review — hoàn thành toàn bộ backlog (Phases A-E)

- [x] `[AGY]` A — Sửa mất hard break/formatting, escaping text, literal marker và newline prefix; bổ sung regression tests (F01–F03, B01–B02).
- [x] `[AGY]` B — Sửa tổng multipart limit/status, decode Unicode, lenient JSON và HTTP error contract (F04–F05, B03–B04).
- [x] `[AGY]` C — Bảo vệ debug API theo môi trường (giữ Production hoạt động theo yêu cầu), sửa quota/redaction/cancellation; kiểm chứng concurrency và chi phí capture/log store (F06–F09, B05).
- [x] `[AGY]` D — Đồng bộ Swagger example/body/curl, giới hạn interceptor đúng scope; sửa selection race và highlight của viewer (F10–F12).
- [x] `[AGY]` E — Bổ sung returns còn thiếu, sửa toàn bộ chênh lệch format; chuẩn hóa XML docs, verify formatter và cập nhật README.

### Review source code và coding convention — 2026-09-12

- [x] `[CDX]` R1 — Restore/build Release XML docs 0 warning/error; 162 tests pass; formatter verify ghi 8 chênh lệch trên 4 file.
- [x] `[CDX]` R2 — Audit 46 file C#, 365 declaration; ghi một lỗi thiếu returns và ma trận convention đề xuất.
- [x] `[CDX]` R3 — Review Markdown và tái hiện mất hard break, emphasis, literal escaping cùng các case biên.
- [x] `[CDX]` R4 — Review API/DI/limits và chạy HTTP probes, gồm host giới hạn multipart 1 KiB.
- [x] `[CDX]` R5 — Review diagnostics; tái hiện quota, redaction, cancellation và đo allocation snapshot JSON tổng hợp.
- [x] `[CDX]` R6 — Browser smoke Swagger/debug; probe JavaScript race/highlight/interceptor; đối chiếu test và tài liệu.
- [x] `[CDX]` R7 — Viết báo cáo có bằng chứng, convention matrix và backlog A–E; giữ nguyên source đã khảo sát.

- [x] `[CDX]` Khảo sát source hiện tại và lập kế hoạch review source code/coding convention; cập nhật plan và handoff (2026-09-12, `+07:00`).

- [x] `[AGY]` Tạo cấu trúc `.ai/` và các file tài liệu chia sẻ
- [x] `[AGY]` Chuyển đổi toàn bộ hệ thống debug trace sang format JSON có cấu trúc cây lồng nhau
- [x] `[AGY]` Tạo REST API quản lý debug (`DebugController`: toggle, status, list, detail, delete)
- [x] `[AGY]` Xây dựng trang HTML viewer giao diện sáng (`/debug`) với tree view expand/collapse, tô màu phân biệt In/Out/State/Item/Error
- [x] `[AGY]` Lọc giới hạn chỉ trace endpoint `/import` và `/export`
- [x] `[AGY]` Hiển thị tên file request (`requestFileName`) trong danh sách
- [x] `[AGY]` Định dạng State theo dạng `name: value`
- [x] `[AGY]` Tìm kiếm tập trung trong cụ thể 1 log đã chọn, loại bỏ auto-refresh
- [x] `[AGY]` Phân biệt rõ rệt màu sắc cú pháp JSON (Key xanh đậm, Colon xám, String xanh lá, Number cam, Boolean tím)
- [x] `[AGY]` Gom nhóm biến State cùng tên và hiển thị theo dạng tiến trình giá trị: `key: value 1 -> value 2 -> ...`
- [x] `[AGY]` Loại bỏ hiển thị số lượng và số lần update trong State
- [x] `[AGY]` Lọc bỏ trùng lặp khi 2 lần update liên tiếp của biến State không thay đổi giá trị
- [x] `[AGY]` Sửa lỗi logic heading index trong `MarkdownTranslationApplier` khi có link nội bộ
- [x] `[AGY]` Tối ưu hiệu năng, chống crash reflection và thread-safe cho hệ thống DebugTrace
- [x] `[AGY]` Tối ưu N+1 Log Streaming trong `DebugController` đọc tuần tự file stream, tránh cấp phát bộ nhớ lớn
- [x] `[AGY]` Tách rời kiến trúc Middleware thông qua `[Traceable]` attribute và Options
- [x] `[AGY]` Trích xuất `IMarkdownExtractor` và đăng ký DI container cho `MarkdownService`
- [x] `[AGY]` Khử trùng lặp logic upload trong `FilesController` và tích hợp `DebugTrace.TraceAsync`
- [x] `[AGY]` Chuẩn hóa 100% XML doc comment tuân thủ `AGENTS.md` (0 warnings, 0 errors với doc check)
- [x] `[AGY]` Gia cố bộ test suite (độ bền đường dẫn test, Dispose pattern, ContentType)
- [x] `[AGY]` Đổi màu tên biến State giống màu thuộc tính JSON, bỏ hoàn toàn nhãn/pill badge
- [x] `[AGY]` Hỗ trợ copy tên hàm trong Tree View (chọn text hoặc nút bấm 1-click `📋`)
- [x] `[AGY]` Nâng cấp Swagger UI cho `/export`: hỗ trợ dán `translatedTexts` dạng JSON đa dòng (textarea monospace, schema example, hỗ trợ cả upload file JSON)
- [x] `[AGY]` Tự động chuẩn hóa (normalize) ký tự xuống dòng thô trong chuỗi JSON và cho phép trailing commas trong `FilesController`
- [x] `[AGY]` Loại bỏ logic tự sinh file mẫu vào `docs/examples` trong `DebugTraceTests`

---

## Hướng dẫn thêm task mới

Thêm task mới vào section "Task đang hoạt động" theo format:

```markdown
### [Tên nhóm task]

- [ ] `[AGY|CDX]` Mô tả task ngắn gọn
  - Chi tiết phụ nếu cần
```
