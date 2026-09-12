# Kế hoạch triển khai

Tài liệu kế hoạch tổng thể cho dự án. Cả Antigravity và Codex đều cập nhật file này khi có thay đổi kế hoạch.

## Tổng quan dự án

**FileHandler** — REST API ASP.NET Core 10 xử lý Markdown và văn bản thuần TXT theo luồng stateless:
- `POST /import` trả array chuỗi cần dịch
- `POST /export` nhận file nguồn + `translatedTexts` rồi trả `.translated.md` hoặc `.translated.txt`

## Phiên bản hiện tại

- **V1**: Xử lý heading, paragraph, emphasis, nhãn link. Code fence, front matter, thematic break không sinh unit.
- **TXT**: Chia đoạn bằng dòng trống, thay bản dịch nguyên văn, giữ UTF-8 BOM và separator; dùng chung API/limits/trace.

## Kế hoạch tiếp theo

### Milestone 3: Refactor toàn diện (Logic, Performance, DI Architecture, Syntax, Test Hardening)

**Trạng thái:** Đã hoàn thành ngày 2026-09-12 (`+07:00`); 274/274 tests pass.

**Mục tiêu:**
- **Logic & Performance:** Chuyển thuật toán áp dụng bản dịch Markdown sang forward-pass $O(N)$ tránh memory shift $O(N^2)$; tối ưu `stackalloc Span<char>` trong `TryGetProp`; loại bỏ cấp phát chuỗi trong `AddHardBreak` và `FindNewlinePolicy`; tối ưu `TryGetBuffer` cho LOH trong `Utf8TextReader`; fast-path cho `NormalizeJsonStringNewlines`.
- **Architecture & DI:** Xóa bỏ Bastard Injection trong `MarkdownService`, đăng ký DI tường minh qua `IMarkdownExtractor`, bổ sung static factory `Create()` cho test. Cache reflection `PropertyInfo[]` và đường dẫn trace directory.
- **Syntax & Conventions:** Áp dụng C# 11 raw string literals, property patterns, `Path.GetFileName`, nullable comparison rút gọn.
- **Test Hardening:** Khắc phục nguy cơ ThreadPool starvation trong concurrent test bằng `TaskCompletionSource` bất đồng bộ; đồng bộ hóa toàn bộ test suite.

### Milestone 2: Hỗ trợ văn bản thuần `.txt`

**Trạng thái:** Đã hoàn thành T1–T7 ngày 2026-09-12 (`+07:00`); 256/256 tests pass.

**Mục tiêu:** Dùng chung `/import`, `/export` và contract hiện tại cho `.md`/`.txt`; TXT đọc UTF-8, tách chuỗi và áp dụng bản dịch trực tiếp, không dùng AST/marker/escaping Markdown.

**Thiết kế đã triển khai:** `PlainTextService : IFileHandler`, scanner đoạn đơn giản, helper UTF-8 dùng chung, dispatch và tên file theo extension. Người dùng yêu cầu implement sau khi xem plan/kiến trúc, triển khai phương án chia đoạn bằng dòng trống đã trình bày.

**Kế hoạch chi tiết:** [Hỗ trợ file TXT](notes/plain-text-support-plan.md) — contract, quy tắc whitespace/EOL/BOM, kiến trúc, thuật toán, backlog T1–T7, ma trận test và tiêu chí nghiệm thu.

**Phụ thuộc:** Triển khai trên working tree hiện tại và kiểm chứng hồi quy Markdown; không thêm package hoặc API phiên bản mới.

### Milestone 1: Review source code, coding convention và triển khai cải tiến

**Trạng thái:** Đã hoàn thành toàn bộ review và xử lý 5 Phase (A–E) ngày 2026-09-12.

**Mục tiêu:** Đánh giá working tree, sửa lỗi logic Markdown, bảo vệ biên API, hoàn thiện diagnostics & quotas, Swagger UI/viewer và chuẩn hóa conventions.

**Phạm vi:**
- Toàn bộ C# API/test, xử lý Markdown, debug trace, Swagger/debug viewer, cấu hình và tài liệu liên quan.
- Chuẩn hóa convention theo `AGENTS.md` (100% XML docs, formatting).
- Hoàn thành toàn bộ 176/176 tests pass, 0 warnings/errors.

**Kết quả:** Toàn bộ 5 Phase A–E đã hoàn thành, tích hợp vào codebase và kiểm chứng tự động. Ghi nhận tại `.ai/tasks.md` và `.ai/handoff.md`.

**Phụ thuộc:** SDK .NET 10.0.401, dependency restore, working tree được ghi nhận tại thời điểm review. Kết quả build/test trong handoff trước phải được xác nhận lại.

---

## Lịch sử thay đổi kế hoạch

### [CDX] 2026-09-12 17:58 — Hoàn thành hỗ trợ TXT

Triển khai reader UTF-8 dùng chung, module PlainText, detector/dispatch/DI, filename/MIME, OpenAPI/README và 80 test case bổ sung. Baseline 176 → 256 tests pass; build Release XML docs 0 warnings/errors; formatter không đổi; audit 54 file/447 khai báo không lỗi. Smoke Kestrel cả TXT và Markdown giữ BOM/CRLF, đúng attachment/MIME. Múi giờ: `+07:00`.

### [CDX] 2026-09-12 10:04 — Lập kế hoạch hỗ trợ `.txt`

Đối chiếu controller/detector/reader/service/test hiện tại, tạo `notes/plain-text-support-plan.md` và backlog T1–T7. Chỉ sửa tài liệu `.ai/`; chưa triển khai hoặc chạy lại build/test. Múi giờ: `+07:00`.

### [CDX] 2026-09-12 02:06 — Hoàn thành review và đề xuất xử lý

Hoàn thành R1–R7 với bằng chứng build/test, audit XML, HTTP probes và browser smoke. Đề xuất năm đợt: bảo toàn Markdown, biên API, diagnostics, Swagger/viewer, convention/docs. Chưa sửa source hoặc áp dụng convention mới. Múi giờ: `+07:00`.

### [CDX] 2026-09-12 01:36 — Lập kế hoạch review source và convention

Khảo sát source/config/test hiện tại và tạo `notes/source-code-review-plan.md` với phạm vi, bằng chứng ban đầu, checklist, trình tự, lệnh kiểm tra và tiêu chí hoàn thành. Chỉ cập nhật tài liệu kế hoạch; chưa chạy đợt review đầy đủ. Múi giờ: `+07:00`.

### [AGY] 2026-09-11 23:29 — Khởi tạo

Tạo không gian làm việc chia sẻ `.ai/` và file kế hoạch ban đầu. Chưa có milestone cụ thể — chờ người dùng xác nhận mục tiêu tiếp theo.
