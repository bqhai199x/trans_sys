# Quy tắc làm việc trong repository

## Phạm vi thay đổi

- Đọc source, cấu hình và test liên quan trước khi sửa; xác nhận hành vi bằng implementation và assertion, không suy đoán từ tên file, tên test hoặc comment cũ.
- Chỉ sửa trong phạm vi yêu cầu. Với tác vụ chỉ sửa comment hoặc tài liệu, giữ nguyên logic, contract, configuration và test.
- Giữ nguyên các thay đổi không liên quan đang có trong workspace. Không sửa trực tiếp file sinh tự động trong `bin/` hoặc `obj/`.
- Dùng tên class, method, property, enum, route, configuration key, filename và HTTP header đúng như source khi viết comment hoặc tài liệu.

## Quy tắc viết comment

Áp dụng cho toàn bộ mã C# viết tay và test trong repository.

### Ngôn ngữ và nội dung

- Viết hướng dẫn trong `AGENTS.md` bằng tiếng Việt. Doc comment trong code vẫn dùng tiếng Anh ngắn gọn.
- Dùng XML documentation (`///`) để mô tả mục đích, ý nghĩa, đơn vị, giới hạn và hành vi cần biết. Bỏ từ thừa, mạo từ không cần thiết như `the`, và cách viết khuôn mẫu như `Gets or sets`.
- Mọi method, hàm có tên, constructor, property, field và indexer đều phải có comment, kể cả `internal`, `private`, `protected`, `public`, `static`, `readonly` và `const`.
- Áp dụng cả với thành viên interface, property dạng expression-bodied, lớp lồng nhau và helper trong test. Accessor không cần comment riêng khi property đã có mô tả.
- Mọi enum và từng thành viên enum đều phải có `<summary>`, bất kể phạm vi truy cập.
- Mỗi property sinh từ positional record phải được mô tả bằng `<param name="...">` tương ứng trên khai báo record. Property khai báo trực tiếp trong record vẫn cần `<summary>` riêng.

### Định dạng

- Đặt doc comment ngay trên khai báo, trước các attribute nếu có.
- Cách khối comment với nội dung phía trên đúng một dòng trống, kể cả sau dấu `{`. Không chèn dòng trống giữa comment và khai báo hoặc attribute.
- Chỉ `<summary>` xuống dòng: dòng mở thẻ, dòng mô tả ngắn, dòng đóng thẻ.
- Mỗi thẻ khác, gồm `<param>`, `<returns>`, `<typeparam>`, `<value>`, `<remarks>` và `<exception>`, phải nằm trọn trên một dòng cùng nội dung và thẻ đóng.
- Bổ sung `<param name="...">mô tả</param>` cho đầy đủ tham số theo đúng thứ tự khai báo, kể cả tham số constructor và primary constructor.
- Mọi method hoặc hàm có tên phải có `<returns>mô tả</returns>`. Nêu giá trị trả về, kết quả của task và trường hợp null/lỗi có ý nghĩa. Dùng `No return value.` cho `void`; constructor không cần `<returns>`.
- Mọi tham số kiểu generic phải có `<typeparam name="...">mô tả</typeparam>` trên một dòng.
- Cập nhật comment khi chữ ký hoặc hành vi thay đổi. Với yêu cầu chỉ sửa comment, giữ nguyên logic và các thay đổi không liên quan đang có trong workspace.

### Ví dụ

```csharp
internal sealed class SourceReader
{

    /// <summary>
    /// Maximum source size in bytes.
    /// </summary>
    private long MaxBytes { get; init; }

    /// <summary>
    /// Checks source size against configured limit.
    /// </summary>
    /// <param name="byteCount">Source size in bytes.</param>
    /// <returns>True when source size is nonnegative and within limit.</returns>
    internal bool IsWithinLimit(long byteCount) => byteCount >= 0 && byteCount <= MaxBytes;

    /// <summary>
    /// Returns supplied value unchanged.
    /// </summary>
    /// <typeparam name="T">Value type.</typeparam>
    /// <param name="value">Value to return.</param>
    /// <returns>Supplied value, unchanged.</returns>
    private static T Identity<T>(T value) => value;
}

/// <summary>
/// Supported source file formats.
/// </summary>
internal enum SourceFormat
{

    /// <summary>
    /// Markdown source document.
    /// </summary>
    Markdown
}
```

## Mã C# và thành phần dùng chung

- Theo convention của module đang sửa: file-scoped namespace, nullable annotations và visibility phù hợp; giữ các cấu hình nullable, deterministic build và warnings-as-errors hiện có.
- Tái sử dụng helper, parser và model đã có khi cùng ngữ nghĩa. Trong FileHandler, dùng `ProcessingStatus`, `SkipCodes`, `SkipSeverity`, `SkipStage`, `SkipScope` và `ProcessingMessages` cho các giá trị dùng chung; không rải lại literal tương đương trong từng format.
- Đặt giới hạn xử lý trong Options tương ứng và dùng đơn vị rõ ràng: byte, số unit, số token hoặc UTF-16 code unit. Khi thay đổi default, kiểm tra cả giá trị trong class Options và `appsettings.json`.
- Truyền `CancellationToken` qua chuỗi xử lý async/I/O; kiểm tra cancellation trong vòng lặp xử lý lớn. Không chuyển cancellation thành kết quả thành công hoặc skip nội dung.
- Giữ đúng ownership của stream: method nhận stream từ caller không tự dispose stream đó trừ khi contract quy định khác; dispose tài nguyên do method tạo bằng `using`/`await using`.
- Các Service singleton phải giữ dữ liệu từng request độc lập; không lưu source, selection, translations hoặc metadata đang xử lý vào mutable state dùng chung giữa request.

## Contract và bảo toàn dữ liệu của FileHandler

- Giữ route, field JSON/form, mã lỗi, HTTP status và cấu trúc multipart hiện có khi yêu cầu không thay đổi contract. Đối chiếu cả Controller, Service, response writer và test HTTP khi sửa contract.
- Phân biệt lỗi fatal trong `errors` với vùng giữ nguồn trong `metadata.skipped`. Lỗi recoverable giữ nguồn của unit/vùng tương ứng và cho phép xử lý phần độc lập; count mismatch và quota vẫn là fatal theo contract hiện có.
- Dùng quy tắc status chung: fatal là `failed`, có warning là `partial`, chỉ có info vẫn là `success`. Tính `skipCount` trước khi lọc info theo `debug`; không suy số unit từ số đối tượng bị skip.
- Không làm mất nội dung ngoài vùng được dịch: giữ BOM, separator/newline, formatting, XML structure, relationships và phần ngoài selection theo rule của từng format. Identity export phải giữ bytes nguồn và vẫn kiểm tra quota.
- Giữ mapping theo thứ tự nguồn và native ID của sheet/slide. Không thay ID bằng tên hoặc index; không dùng nội dung chuỗi để ghép các unit trùng text.
- Dùng parser/codec token hiện có để kiểm tra ID, escape và boundary trước khi apply. Không tự bỏ token hoặc normalize text theo cách làm mất liên kết với nguồn.
- Với Office, giữ source/output validation, schema baseline và edit-mask checks. Skip một vùng không được vô hiệu hóa validation cho cả part hoặc package.
- Không đưa file content, translations hoặc chi tiết exception ngoài dự kiến vào response lỗi hay log mới. Dùng mã lỗi/message và thông tin chẩn đoán cần thiết theo cơ chế hiện có.

## Kiểm thử

- Kiểm tra hành vi qua input/output và contract; method private được kiểm tra qua caller. Không dùng reflection hoặc đổi visibility chỉ để test.
- Khi sửa logic, bổ sung hoặc cập nhật test cho hành vi thay đổi và trường hợp lỗi tái hiện được. Không tạo test riêng cho getter/setter, constructor hoặc code compiler sinh chỉ để tăng coverage.
- Với xử lý file, chọn các kiểm tra liên quan đến thay đổi: identity bytes, formatting/vùng giữ nguyên, token và thứ tự unit, Unicode/BOM/newline, selection, quota, cancellation hoặc request isolation.
- Thay đổi ở HTTP boundary cần test request/response thực tế; thay đổi parser/codec cần test trực tiếp kết quả và lỗi. Tận dụng fixture/helper đang có trong test project.
- Chỉ báo cáo test đã thực sự chạy; phân biệt đọc test source với test pass. Nếu build/test bị chặn bởi môi trường, nêu command và nguyên nhân; không tắt warning hoặc validation để che lỗi.

## Tài liệu kỹ thuật

- Viết bằng tiếng Việt, giữ technical terms phổ biến bằng English và tên trong source nguyên dạng.
- Mô tả hành vi hiện tại bằng bảng, flow, bullet list hoặc snippet ngắn; không giải thích lại khái niệm cơ bản của framework.
- Trace method call thực tế từ input đến output. Xác định route từ routing/Controller, schema từ model/writer, configuration từ code/config và test coverage từ assertion hiện có.
- Khi chưa xác định được hành vi từ source, ghi rõ “Không xác định được từ source code hiện tại.”; không điền assumption thành fact.
- Chia tài liệu theo nhóm nội dung, có trang bắt đầu và cross-link; tránh lặp cùng flow ở nhiều file. Dùng exact file path, class và method để developer tìm source.
- Khi yêu cầu chỉ mô tả cách service hoạt động, không tự thêm đánh giá kiến trúc, technical debt, refactoring hoặc recommendation. Không đưa tính năng đã bỏ vào tài liệu runtime hiện tại.
- Kiểm tra link Markdown, tên symbol và example với source. Khi contract thay đổi trong phạm vi được giao, cập nhật tài liệu liên quan để phản ánh cùng hành vi.

## Kiểm tra trước khi hoàn tất

- Rà soát toàn bộ property, field, enum, thành viên enum và property của positional record, không lọc theo phạm vi truy cập.
- Kiểm tra đủ tên và thứ tự tham số, tham số kiểu generic, mô tả giá trị trả về, định dạng XML và khoảng cách dòng.
- Khi thay đổi C# hoặc XML documentation trong code, build với XML documentation được bật để phát hiện lỗi thẻ hoặc tên tham số không khớp; giữ nguyên các cấu hình bỏ qua cảnh báo đã có của dự án.
- Chạy các test phù hợp với thay đổi logic/contract; với thay đổi chỉ ở Markdown, kiểm tra nội dung, link và diff.
- Kiểm tra diff cuối cùng để bảo đảm chỉ thay đổi file thuộc phạm vi yêu cầu và không ghi đè công việc không liên quan.
