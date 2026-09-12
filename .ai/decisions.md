# Nhật ký quyết định thiết kế

Ghi lại các quyết định thiết kế quan trọng. Khi không đồng ý với quyết định trước đó, ghi phản hồi tại đây thay vì tự ý thay đổi code.

## Format

```markdown
### [AGENT_ID] YYYY-MM-DD HH:mm — Tiêu đề quyết định

**Bối cảnh:** Vì sao cần quyết định này.

**Phương án đã xem xét:**
1. Phương án A — ưu/nhược
2. Phương án B — ưu/nhược

**Quyết định:** Chọn phương án nào và lý do.

**Hệ quả:** Những gì thay đổi hoặc cần lưu ý sau quyết định.
```

---

## Quyết định

### [AGY] 2026-09-12 18:45 — Tối ưu ghép chuỗi Forward-Pass và Chuẩn hóa DI Container

**Bối cảnh:** Thuật toán áp dụng bản dịch Markdown trước đây sử dụng `StringBuilder.Remove().Insert()` lặp lại từ cuối lên đầu gây dời bộ nhớ $O(N^2)$. Ngoài ra, `MarkdownService` sử dụng pattern "Bastard Injection" tự khởi tạo extractor nếu không được truyền vào, làm suy yếu tính độc lập của DI container.

**Quyết định:**
1. **Forward-Pass Composition:** Thay thế `Remove/Insert` lặp bằng cơ chế duyệt từ đầu đến cuối một lượt (forward-pass) $O(N)$ ghép nối văn bản nguồn và bản dịch. Đồng thời duy trì việc phát sự kiện trace `patch` theo thứ tự nghịch đảo (descending) để bảo đảm tính tương thích với contract log và test suite đã có.
2. **Loại bỏ Bastard Injection:** Constructor của `MarkdownService` chỉ chấp nhận `IMarkdownExtractor` từ DI container. Cung cấp static factory method `MarkdownService.Create(options)` cho môi trường test suite độc lập.
3. **Tối ưu cấp phát bộ nhớ tầng thấp:** Áp dụng `stackalloc Span<char>` trong `TryGetProp`, `ReadOnlySpan<char>` trong `FindNewlinePolicy`, và `TryGetBuffer` trong `Utf8TextReader` để triệt tiêu việc cấp phát chuỗi/mảng thừa trên heap và LOH.

**Hệ quả:** Tốc độ ghép bản dịch đạt $O(N)$, loại bỏ điểm nghẽn hiệu năng với tài liệu lớn. Kiến trúc DI minh bạch, dễ bảo trì và mock. 274/274 tests pass, thời gian thực thi < 1s. Múi giờ `+07:00`.

---

### [CDX] 2026-09-12 18:20 — Đặt state theo bước xử lý và nơi thay đổi dữ liệu

**Bối cảnh:** Người dùng yêu cầu review toàn bộ và đặt lại các điểm trace state. State cũ có giá trị mặc định gây hiểu nhầm, buffer trùng nhiều tầng, item mở trễ và thiếu metadata khi dừng ở quota.

**Quyết định:** Dùng tên camelCase có nghĩa; ghi dữ liệu vừa thay đổi/quyết định đã xác định. Riêng `stage` ghi trước bước xử lý để chẩn đoán lỗi/cancellation. `EncodeInline` sở hữu snapshot buffer trước/sau, helper ghi marker vừa tạo. Mở item Markdown trước xử lý leaf block; phân biệt item index 1-based và unit index 0-based. Giữ In/Out, schema, lazy capture, redaction và quota hiện có.

**Hệ quả:** Log mới thay đổi state name và quan hệ cha–con của item, viewer hiện tại tiếp tục đọc được cả log cũ/mới. Không đổi contract import/export hoặc logic xử lý file. Có 18 regression case bổ sung; test toggle cờ process được cô lập để tránh ảnh hưởng test HTTP chạy đồng thời. Chi tiết tại [review trace state](notes/trace-state-review.md). Múi giờ `+07:00`.

---

### [CDX] 2026-09-12 17:58 — Triển khai PlainText theo kiến trúc đã trình bày

**Bối cảnh:** Người dùng yêu cầu `implement` sau khi xem plan và cấu trúc file dự kiến.

**Quyết định:** Áp dụng phương án module PlainText và chia đoạn bằng dòng trống. Dùng chung UTF-8 codec/I/O; giữ adapter và line map Markdown; controller switch theo `FileType`, không thêm registry/interface hoặc dependency mới.

**Quy tắc dữ liệu:** Unit giữ nguyên text nguồn gồm whitespace/newline nội bộ; export chèn bản dịch nguyên văn. BOM/separator ngoài span được giữ. Không diễn giải marker hoặc cấu trúc Markdown; identity chỉ thành công trong mọi giới hạn. Tính byte budget trước compose để tránh output allocation quá lớn.

**Hệ quả:** API hỗ trợ `.md`/`.txt` trên route cũ, output theo extension tương ứng. OpenAPI khai báo binary success riêng với JSON errors; tests 256/256 pass. Cách chia dòng/toàn file hoặc encoding khác là phạm vi mở rộng sau, không còn câu hỏi chặn triển khai hiện tại. Múi giờ `+07:00`.

---

### [CDX] 2026-09-12 10:04 — Định hướng đề xuất cho xử lý TXT

**Bối cảnh:** Người dùng yêu cầu lập kế hoạch xử lý `.txt` tương tự `.md`, không cần kỹ thuật extract phức tạp.

**Phương án đã xem xét:**
1. Đưa `.txt` qua parser Markdown — tái sử dụng nhiều code nhưng ký hiệu, code và marker sẽ không còn được xử lý như văn bản thuần.
2. Module `PlainText` nhẹ implement `IFileHandler`, chỉ dùng chung UTF-8/API/limits/trace — phù hợp semantics TXT và giữ thuật toán đơn giản.
3. Xây pipeline/plugin registry tổng quát cho nhiều định dạng — tăng phạm vi không cần thiết khi mới có hai loại file.

**Định hướng trong kế hoạch:** Chọn phương án 2; scanner một lượt, thay theo span, không AST/marker. Dispatch bằng switch nhỏ ở controller; tách UTF-8 reader dùng chung và giữ adapter Markdown. Chia unit theo đoạn ngăn bởi dòng trống là giả định đề xuất, chưa được người dùng xác nhận.

**Hệ quả:** API/DTO giữ nguyên; bổ sung `.translated.txt` và `text/plain; charset=utf-8`; phải kiểm chứng hồi quy Markdown khi tách reader. Chi tiết tại [kế hoạch TXT](notes/plain-text-support-plan.md).

**Trạng thái:** Chỉ là thiết kế đề xuất cho backlog, chưa triển khai, không ghi nhận thành quyết định sản phẩm đã được người dùng chốt. Múi giờ `+07:00`.

---

### [AGY] 2026-09-11 23:29 — Chọn `.ai/` làm thư mục chia sẻ

**Bối cảnh:** Cần không gian tài liệu chung mà cả Antigravity và Codex đều đọc/ghi được. Phải nằm trong repo để đồng bộ qua Git.

**Phương án đã xem xét:**
1. `.ai/` — gọn, rõ mục đích, không trùng convention phổ biến
2. `.agents/` — dài hơn, có thể trùng với một số framework
3. `docs/ai/` — lẫn với docs dự án, khó phân biệt

**Quyết định:** Chọn `.ai/` vì ngắn gọn, rõ ràng, và tách biệt với tài liệu dự án.

**Hệ quả:** Mọi tài liệu phối hợp AI nằm trong `.ai/`. Tài liệu kỹ thuật dự án vẫn ở `filehandler/docs/`.

---

### [AGY] 2026-09-12 00:55 — Quyết định kiến trúc & Tối ưu hiệu năng đợt refactoring lớn

**Bối cảnh:** Cần giải quyết các vấn đề hiệu năng LOH khi duyệt log, loại bỏ coupling cứng trong middleware & service, và đảm bảo thread-safety cho trace logging.

**Các quyết định:**
1. **Stream parsing cho `ListLogs`:** Đọc tuần tự các trường scalar qua `FileStream` thay vì load toàn bộ file JSON nhiều MB vào bộ nhớ. Chỉ parse full tree khi xem chi tiết log.
2. **Decouple Middleware bằng `[Traceable]` Attribute & Options:** Thay thế hardcoded check `/import`, `/export` bằng metadata attribute `[Traceable]` trên action và `TraceablePaths` trong `DebugTraceOptions`.
3. **Decouple `MarkdownService` qua `IMarkdownExtractor`:** Đưa `IMarkdownExtractor` vào DI container, cung cấp constructor mở rộng để dễ mock khi viết test độc lập.
4. **Chuẩn hóa Wrapper `DebugTrace.Trace` / `TraceAsync`:** Đóng gói try-catch và đo lường thời gian thực thi tự động, loại bỏ boilerplate lặp lại ở Controllers.

**Hệ quả:** Codebase sạch, giảm phụ thuộc vòng, tối ưu cấp phát bộ nhớ và tuân thủ chặt chẽ nguyên lý Single Responsibility.
