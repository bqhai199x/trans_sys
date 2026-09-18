# Quy tắc viết comment

Áp dụng cho toàn bộ mã C# viết tay và test trong repository. Không sửa file sinh tự động trong `bin/` hoặc `obj/`.

## Ngôn ngữ và nội dung

- Viết hướng dẫn trong `AGENTS.md` bằng tiếng Việt. Doc comment trong code vẫn dùng tiếng Anh ngắn gọn.
- Dùng XML documentation (`///`) để mô tả mục đích, ý nghĩa, đơn vị, giới hạn và hành vi cần biết. Bỏ từ thừa, mạo từ không cần thiết như `the`, và cách viết khuôn mẫu như `Gets or sets`.
- Mọi method, hàm có tên, constructor, property, field và indexer đều phải có comment, kể cả `internal`, `private`, `protected`, `public`, `static`, `readonly` và `const`.
- Áp dụng cả với thành viên interface, property dạng expression-bodied, lớp lồng nhau và helper trong test. Accessor không cần comment riêng khi property đã có mô tả.
- Mọi enum và từng thành viên enum đều phải có `<summary>`, bất kể phạm vi truy cập.
- Mỗi property sinh từ positional record phải được mô tả bằng `<param name="...">` tương ứng trên khai báo record. Property khai báo trực tiếp trong record vẫn cần `<summary>` riêng.

## Định dạng

- Đặt doc comment ngay trên khai báo, trước các attribute nếu có.
- Cách khối comment với nội dung phía trên đúng một dòng trống, kể cả sau dấu `{`. Không chèn dòng trống giữa comment và khai báo hoặc attribute.
- Chỉ `<summary>` xuống dòng: dòng mở thẻ, dòng mô tả ngắn, dòng đóng thẻ.
- Mỗi thẻ khác, gồm `<param>`, `<returns>`, `<typeparam>`, `<value>`, `<remarks>` và `<exception>`, phải nằm trọn trên một dòng cùng nội dung và thẻ đóng.
- Bổ sung `<param name="...">mô tả</param>` cho đầy đủ tham số theo đúng thứ tự khai báo, kể cả tham số constructor và primary constructor.
- Mọi method hoặc hàm có tên phải có `<returns>mô tả</returns>`. Nêu giá trị trả về, kết quả của task và trường hợp null/lỗi có ý nghĩa. Dùng `No return value.` cho `void`; constructor không cần `<returns>`.
- Mọi tham số kiểu generic phải có `<typeparam name="...">mô tả</typeparam>` trên một dòng.
- Cập nhật comment khi chữ ký hoặc hành vi thay đổi. Với yêu cầu chỉ sửa comment, giữ nguyên logic và các thay đổi không liên quan đang có trong workspace.

## Ví dụ

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

## Kiểm tra trước khi hoàn tất

- Rà soát toàn bộ property, field, enum, thành viên enum và property của positional record, không lọc theo phạm vi truy cập.
- Kiểm tra đủ tên và thứ tự tham số, tham số kiểu generic, mô tả giá trị trả về, định dạng XML và khoảng cách dòng.
- Build với XML documentation được bật để phát hiện lỗi thẻ hoặc tên tham số không khớp; giữ nguyên các cấu hình bỏ qua cảnh báo đã có của dự án.
