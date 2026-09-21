using FileHandler.Api.Common;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace FileHandler.Api.OpenApi;

/// <summary>
/// Localizes public JSON field descriptions and hides detached mapping from metadata schema.
/// </summary>
public sealed class VietnameseSchemaFilter : ISchemaFilter
{

    /// <summary>
    /// Vietnamese descriptions shared by public response fields.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> Descriptions = new Dictionary<string, string>
    {
        ["texts"] = "Các chuỗi cần dịch theo thứ tự nguồn. Excel có unit tên sheet trước nội dung mỗi worksheet được chọn.",
        ["metadata"] = "Thông tin đã thu thập về nguồn, lựa chọn và phần bị bỏ qua.",
        ["errors"] = "Chỉ chứa lỗi làm thất bại toàn tác vụ; mảng rỗng khi thành công hoặc thành công một phần.",
        ["format"] = "Định dạng: markdown, plaintext, word, excel hoặc powerpoint.",
        ["status"] = "success: không có warning; partial: có phần bị bỏ qua với warning; failed: không thể hoàn tất.",
        ["unitCount"] = "Số unit trong mapping, bao gồm tên sheet; null khi chưa xác định được.",
        ["units"] = "Mapping chỉ nằm trong tệp units.json khi import có debug=true.",
        ["skipped"] = "Các phần được giữ nguồn: luôn trả warning; info chỉ xuất hiện khi import có debug=true. Export/discovery không nhận debug.",
        ["skipCount"] = "Tổng count của skip theo severity trước lọc debug; không phải số unit.",
        ["warning"] = "Tổng count của skip warning đã thu thập.",
        ["info"] = "Tổng count của skip info đã thu thập, kể cả khi không hiển thị chi tiết.",
        ["sheets"] = "Các sheet theo thứ tự nguồn, có ID gốc và trạng thái hiển thị.",
        ["slides"] = "Các slide theo thứ tự nguồn, có ID gốc và trạng thái ẩn.",
        ["sheetNameChanges"] = "Tên sheet gốc, tên yêu cầu và tên thực tế, kể cả khi phải giữ tên nguồn.",
        ["newlinePolicy"] = "preserve: giữ quy tắc xuống dòng của Markdown nguồn.",
        ["encoding"] = "utf-8: encoding của tệp TXT; BOM được bảo toàn nội bộ.",
        ["code"] = "Mã lý do ổn định để client xử lý.",
        ["message"] = "Thông báo tiếng Anh ngắn gọn.",
        ["severity"] = "info: bỏ qua theo lựa chọn/quy tắc, chỉ trả khi import debug=true; warning: nội dung hoặc bản dịch chưa xử lý được, luôn trả.",
        ["stage"] = "Giai đoạn: selection, extraction, translation hoặc rename.",
        ["scope"] = "Phạm vi đối tượng bị bỏ qua, ví dụ sheet, slide, unit, cell, shape, block hoặc story.",
        ["count"] = "Số đối tượng được ghi nhận trong phạm vi scope.",
        ["unitIndex"] = "Index unit từ 0; không có thuộc tính này nếu đối tượng chưa trở thành unit.",
        ["location"] = "Vị trí trong nguồn; chỉ gồm tọa độ áp dụng cho định dạng tương ứng.",
        ["sheetId"] = "ID gốc của sheet dạng chuỗi; không phải tên sheet.",
        ["slideId"] = "ID gốc của slide dạng chuỗi.",
        ["name"] = "Tên sheet trong nguồn.",
        ["title"] = "Nội dung placeholder tiêu đề slide; null nếu không có.",
        ["state"] = "visible: hiển thị; hidden: ẩn; veryHidden: ẩn và không thể mở lại bằng menu Unhide thông thường.",
        [SheetVisibility.Hidden] = "true khi slide đang bị ẩn; export giữ trạng thái này.",
        ["canImport"] = "Khả năng xử lý loại sheet; chartsheet được liệt kê nhưng không trích xuất thành unit.",
        ["selected"] = "Sheet/slide được chọn xử lý; không có trong API discovery.",
        ["unitStartIndex"] = "Index bắt đầu từ 0 của vùng unit; chỉ có trong metadata import.",
        ["unitEndIndex"] = "Index kết thúc không bao gồm phần tử tại index này; chỉ có trong metadata import.",
        ["originalName"] = "Tên sheet trong nguồn.",
        ["requestedName"] = "Tên dịch client yêu cầu.",
        ["finalName"] = "Tên thực tế sau normalize, xử lý trùng hoặc giữ nguồn.",
        ["partUri"] = "URI thực tế của part trong gói Office.",
        ["path"] = "Đường dẫn XML gồm root, prefix namespace và ordinal từ 1; không phải số trang Word.",
        ["cellReference"] = "Địa chỉ ô Excel theo ký hiệu A1.",
        ["shapeId"] = "ID shape trong nguồn.",
        ["rowIndex"] = "Thứ tự hàng từ 1.",
        ["columnIndex"] = "Thứ tự cột từ 1.",
        ["line"] = "Khoảng dòng nguồn từ 1, bao gồm cả hai đầu.",
        ["start"] = "Dòng bắt đầu từ 1, bao gồm dòng này.",
        ["end"] = "Dòng kết thúc từ 1, bao gồm dòng này."
    };

    /// <summary>
    /// Applies descriptions without changing code documentation language.
    /// </summary>
    /// <param name="schema">Generated public schema.</param>
    /// <param name="context">CLR type and schema generation context.</param>
    /// <returns>No return value.</returns>
    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        if (schema.Properties is null) return;
        if (context.Type == typeof(FileMetadata))
        {
            schema.Properties.Remove("units");
            schema.Required?.Remove("units");
        }
        foreach (var (name, value) in schema.Properties)
        {
            if (value is not OpenApiSchema property) continue;
            if (Descriptions.TryGetValue(name, out var description)) property.Description = description;
            if (name == "index") property.Description = context.Type == typeof(UnitMetadata) || context.Type == typeof(FileError) ? "Index unit từ 0." : "Thứ tự trong nguồn từ 1.";
            if (name == "kind") property.Description = context.Type == typeof(UnitMetadata) ? "Loại unit, ví dụ paragraph, heading, cell hoặc sheetName." : "Loại sheet, ví dụ worksheet hoặc chartsheet.";
        }
    }
}
