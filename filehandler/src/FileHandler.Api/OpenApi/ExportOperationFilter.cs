using System.Text.Json;
using System.Text.Json.Nodes;
using FileHandler.Api.Common;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace FileHandler.Api.OpenApi;

/// <summary>
/// Describes file requests, examples and multipart responses in Vietnamese.
/// </summary>
public sealed class ExportOperationFilter : IOperationFilter
{

    /// <summary>
    /// Readable JSON formatting for fixed documentation examples.
    /// </summary>
    private static readonly JsonSerializerOptions ExampleJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Adds localized guidance and format-specific response examples.
    /// </summary>
    /// <param name="operation">Operation being documented.</param>
    /// <param name="context">Current action and schema context.</param>
    /// <returns>No return value.</returns>
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var route = context.ApiDescription.RelativePath?.Split('/');
        if (route is not { Length: 3 } || route[0] != "api") return;
        var format = route[1];
        var action = route[2];
        var import = action == "import";
        var export = action == "export";
        var discovery = action is "sheets" or "slides";
        if (!import && !export && !discovery) return;
        var label = format switch { "plaintext" => "TXT", "powerpoint" => "PowerPoint", "markdown" => "Markdown", "excel" => "Excel", _ => "Word" };
        operation.Summary = discovery ? $"Lấy danh sách {(action == "sheets" ? "sheet Excel" : "slide PowerPoint")}" : $"{(import ? "Import" : "Export")} tệp {label}";
        operation.Description = discovery
            ? "Đọc ID, thứ tự và trạng thái hiển thị.\n\nKhông trích xuất unit dịch."
            : import
                ? "Mặc định trả JSON `{ texts, metadata, errors }`.\n\n- `debug=true`: multipart có thêm `units.json` và skip info.\n- `skipped`: mặc định chỉ có warning. `skipCount`: luôn đếm cả warning/info.\n- Lỗi toàn tác vụ: JSON, không có tệp."
                : "Dùng lại tệp nguồn và lựa chọn lúc import.\n\n- Kết quả: multipart gồm JSON metadata và tệp đã xử lý.\n- Unit dịch lỗi được giữ nguồn; `skipped` chỉ có warning.\n- `skipCount` đếm cả warning/info. Không nhận debug.\n- Lỗi toàn tác vụ: JSON, không có tệp.";
        if (operation.RequestBody?.Content?.TryGetValue("multipart/form-data", out var form) == true && form.Schema?.Properties is { } properties)
        {
            operation.RequestBody.Description = "Gửi các trường bằng multipart/form-data.";
            if (form.Schema is OpenApiSchema formSchema)
            {
                formSchema.Required ??= new HashSet<string>();
                formSchema.Required.Add("file");
                if (export) formSchema.Required.Add("texts");
            }
            foreach (var (name, value) in properties)
            {
                if (value is not OpenApiSchema schema) continue;
                schema.Description = name switch
                {
                    "file" => $"Tệp .{Extension(format)} bắt buộc.\n\nExport dùng lại tệp đã import.",
                    "texts" => "Mảng JSON chuỗi hoặc tệp JSON.\n\nGiữ thứ tự và token; không nhận null. Excel có tên sheet trước nội dung.",
                    "debug" => "`false`: chỉ trả skip warning.\n\n`true`: thêm skip info và `units.json`. Chỉ áp dụng cho import.",
                    "sheetIds" => "Mảng JSON sheetId từ `/api/excel/sheets`.\n\nTrống: sheet hiển thị. `[]`: không chọn. Có thể chọn ID sheet ẩn.",
                    "slideIds" => "Mảng JSON slideId từ `/api/powerpoint/slides`.\n\nTrống: slide hiển thị. `[]`: không chọn. Có thể chọn ID slide ẩn.",
                    _ => schema.Description
                };
                if (name == "texts")
                {
                    schema.Format = "textarea";
                    schema.Example = JsonValue.Create(JsonSerializer.Serialize(Texts(format), ExampleJsonOptions));
                }
                if (name is "sheetIds" or "slideIds")
                {
                    schema.Default = JsonValue.Create("");
                    schema.Example = JsonValue.Create("");
                }
                if (name == "debug") { schema.Default = JsonValue.Create(false); schema.Example = JsonValue.Create(false); }
            }
        }
        if (operation.Responses is null) return;
        foreach (var (status, response) in operation.Responses)
        {
            if (response is not OpenApiResponse concrete) continue;
            if (status == "200")
            {
                concrete.Description = "Thành công hoặc thành công một phần; xem metadata.status và metadata.skipped.";
                concrete.Content ??= new Dictionary<string, OpenApiMediaType>();
                var schema = concrete.Content.Values.FirstOrDefault()?.Schema;
                concrete.Content.Clear();
                if (import || discovery)
                    concrete.Content["application/json"] = new() { Schema = schema, Example = discovery ? Discovery(format) : Envelope(format, true) };
                if (import || export)
                    concrete.Content["multipart/mixed"] = new()
                    {
                        Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary", Description = "Hai phần MIME theo thứ tự; boundary sinh cho từng response. Dùng parser multipart để đọc JSON và tải riêng tệp." },
                        Example = JsonValue.Create(MultipartExample(format, import))
                    };
                continue;
            }
            concrete.Description = status switch
            {
                "400" => "Thiếu trường hoặc dữ liệu request không hợp lệ.",
                "413" => "Vượt giới hạn tài nguyên hoặc kích thước request/tệp.",
                "415" => "Loại tệp hoặc Content-Type không được hỗ trợ.",
                "422" => "Nguồn không hợp lệ, lệch số bản dịch hoặc ID lựa chọn không tồn tại.",
                "429" => "Vượt giới hạn request đang xử lý đồng thời; thử lại sau.",
                "500" => "Lỗi hệ thống ngoài dự kiến; không trả chi tiết exception.",
                _ => "Không thể hoàn tất tác vụ."
            };
            var code = status switch { "400" => "invalid_request", "413" => "file_too_large", "415" => "unsupported_file_type", "422" => "invalid_office_package", "429" => "request_limit_exceeded", _ => "internal_error" };
            if (status == "422" && format is "markdown" or "plaintext") code = "invalid_encoding";
            var message = status switch
            {
                "400" => ProcessingMessages.InvalidRequest,
                "413" => ProcessingMessages.RequestTooLarge,
                "415" => ProcessingMessages.UnsupportedFileType,
                "422" => code == "invalid_encoding" ? ProcessingMessages.InvalidEncoding : ProcessingMessages.InvalidOfficePackage,
                "429" => ProcessingMessages.RequestLimitExceeded,
                _ => ProcessingMessages.InternalError
            };
            var metadata = new JsonObject { ["format"] = format, ["status"] = ProcessingStatus.Failed, ["skipped"] = new JsonArray(), ["skipCount"] = new JsonObject { ["warning"] = 0, ["info"] = 0 } };
            if (!discovery) metadata["unitCount"] = null;
            if (format == "plaintext") metadata["encoding"] = "utf-8";
            if (format == "markdown") metadata["newlinePolicy"] = "preserve";
            var errorSchema = context.SchemaGenerator.GenerateSchema(discovery ? typeof(DiscoveryFailureResponse) : typeof(FileResponse), context.SchemaRepository);
            concrete.Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/json"] = new() { Schema = errorSchema, Example = new JsonObject { ["metadata"] = metadata, ["errors"] = new JsonArray(new JsonObject { ["code"] = code, ["message"] = message }) } }
            };
        }
        if (import) context.SchemaGenerator.GenerateSchema(typeof(UnitsFileResponse), context.SchemaRepository);
    }

    /// <summary>
    /// Returns source text examples matching documented mapping sizes.
    /// </summary>
    /// <param name="format">Lowercase format identifier.</param>
    /// <returns>Two units including worksheet name for Excel.</returns>
    private static string[] Texts(string format) => format == "excel" ? ["Sheet1", "Xin chào"] : ["Xin chào", "Nội dung"];

    /// <summary>
    /// Resolves source file extension for examples.
    /// </summary>
    /// <param name="format">Lowercase format identifier.</param>
    /// <returns>Extension without leading period.</returns>
    private static string Extension(string format) => format switch { "excel" => "xlsx", "word" => "docx", "powerpoint" => "pptx", "markdown" => "md", _ => "txt" };

    /// <summary>
    /// Creates a successful JSON envelope without inline mapping.
    /// </summary>
    /// <param name="format">Lowercase format identifier.</param>
    /// <param name="import">Whether envelope contains extracted texts.</param>
    /// <returns>JSON response example.</returns>
    private static JsonObject Envelope(string format, bool import)
    {
        var metadata = new JsonObject { ["format"] = format, ["status"] = ProcessingStatus.Success, ["unitCount"] = 2, ["skipped"] = new JsonArray(), ["skipCount"] = new JsonObject { ["warning"] = 0, ["info"] = 0 } };
        if (format == "markdown") metadata["newlinePolicy"] = "preserve";
        if (format == "plaintext") metadata["encoding"] = "utf-8";
        if (format is "excel" or "powerpoint")
        {
            var key = format == "excel" ? "sheets" : "slides";
            var items = (JsonArray)Discovery(format)[key]!.DeepClone();
            var item = (JsonObject)items[0]!;
            item["selected"] = true;
            if (import) { item["unitStartIndex"] = 0; item["unitEndIndex"] = 2; }
            metadata[key] = items;
        }
        if (format == "excel" && !import) metadata["sheetNameChanges"] = new JsonArray();
        var result = new JsonObject();
        if (import) result["texts"] = JsonSerializer.SerializeToNode(Texts(format));
        result["metadata"] = metadata;
        result["errors"] = new JsonArray();
        return result;
    }

    /// <summary>
    /// Creates native inventory examples without translation mapping.
    /// </summary>
    /// <param name="format">Excel or PowerPoint format identifier.</param>
    /// <returns>Discovery response example.</returns>
    private static JsonObject Discovery(string format) => new()
    {
        [format == "excel" ? "sheets" : "slides"] = new JsonArray(format == "excel"
            ? new JsonObject { ["sheetId"] = "1", ["index"] = 1, ["name"] = "Sheet1", ["state"] = SheetVisibility.Visible, ["kind"] = "worksheet", ["canImport"] = true }
            : new JsonObject { ["slideId"] = "256", ["index"] = 1, ["title"] = "Xin chào", [SheetVisibility.Hidden] = false }),
        ["metadata"] = new JsonObject { ["format"] = format, ["status"] = ProcessingStatus.Success, ["skipped"] = new JsonArray(), ["skipCount"] = new JsonObject { ["warning"] = 0, ["info"] = 0 } },
        ["errors"] = new JsonArray()
    };

    /// <summary>
    /// Creates wire-format examples identifying envelope and attachment parts.
    /// </summary>
    /// <param name="format">Lowercase format identifier.</param>
    /// <param name="import">Whether attachment contains requested unit mapping.</param>
    /// <returns>Illustrative multipart body with example-boundary delimiter.</returns>
    private static string MultipartExample(string format, bool import)
    {
        var units = new JsonArray();
        for (var index = 0; index < 2; index++)
        {
            var location = format switch
            {
                "excel" => index == 0 ? new JsonObject { ["partUri"] = "/xl/workbook.xml", ["path"] = "/x:workbook[1]/x:sheets[1]/x:sheet[1]", ["sheetId"] = "1" } : new JsonObject { ["partUri"] = "/xl/worksheets/sheet1.xml", ["sheetId"] = "1", ["cellReference"] = "A1" },
                "word" => new JsonObject { ["partUri"] = "/word/document.xml", ["path"] = $"/w:document[1]/w:body[1]/w:p[{index + 1}]" },
                "powerpoint" => new JsonObject { ["partUri"] = "/ppt/slides/slide1.xml", ["slideId"] = "256", ["shapeId"] = "2", ["path"] = $"/p:sld[1]/p:cSld[1]/p:spTree[1]/p:sp[1]/p:txBody[1]/a:p[{index + 1}]" },
                _ => new JsonObject { ["line"] = new JsonObject { ["start"] = index * 2 + 1, ["end"] = index * 2 + 1 } }
            };
            units.Add(new JsonObject { ["index"] = index, ["kind"] = format == "excel" ? index == 0 ? "sheetName" : SkipScope.Cell : "paragraph", ["location"] = location });
        }
        var contentType = format switch
        {
            "excel" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "word" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "powerpoint" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            "markdown" => "text/markdown; charset=utf-8",
            _ => "text/plain; charset=utf-8"
        };
        var attachment = import ? new JsonObject { ["units"] = units }.ToJsonString(ExampleJsonOptions) : format is "markdown" or "plaintext" ? "Xin chào\n\nNội dung" : "[Bytes tệp Office; phần này là dữ liệu nhị phân, không phải chuỗi JSON]";
        return $"--example-boundary\r\nContent-Type: application/json; charset=utf-8\r\nContent-ID: <metadata>\r\n\r\n{Envelope(format, import).ToJsonString(ExampleJsonOptions)}\r\n--example-boundary\r\nContent-Type: {(import ? "application/json; charset=utf-8" : contentType)}\r\nContent-ID: <{(import ? "units" : "file")}>\r\nContent-Disposition: attachment; filename=\"{(import ? "units.json" : "translated." + Extension(format))}\"\r\n\r\n{attachment}\r\n--example-boundary--\r\n";
    }
}
