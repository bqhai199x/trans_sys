using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace FileHandler.Api.OpenApi;

/// <summary>
/// Describes supported file formats and multiline JSON export translations in Swagger.
/// </summary>
public sealed class ExportOperationFilter : IOperationFilter
{

    /// <summary>
    /// Describes file fields and applies textarea format and JSON example to export translations.
    /// </summary>
    /// <param name="operation">OpenAPI operation to modify.</param>
    /// <param name="context">Filter context for current action.</param>
    /// <returns>No return value.</returns>
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var isExport = context.ApiDescription.RelativePath?.EndsWith("export", StringComparison.OrdinalIgnoreCase) == true;
        var isImport = context.ApiDescription.RelativePath?.EndsWith("import", StringComparison.OrdinalIgnoreCase) == true;
        if (!isExport && !isImport)
            return;

        if (operation.RequestBody?.Content?.TryGetValue("multipart/form-data", out var mediaType) != true || mediaType?.Schema?.Properties is null)
            return;

        if (mediaType.Schema.Properties.TryGetValue("file", out var fileProp) && fileProp is OpenApiSchema fileSchema)
            fileSchema.Description = "Tệp nguồn cần xử lý, định dạng tương ứng với endpoint.";

        if (!isExport)
            return;

        if (mediaType.Schema.Properties.TryGetValue("texts", out var prop) && prop is OpenApiSchema schema)
        {
            schema.Format = "textarea";
            schema.Description = "JSON array string chứa các bản dịch (ví dụ: [\"Xin chào\", \"Thế giới\"]).";
            schema.Example = JsonValue.Create("""
                [
                  "Xin chào",
                  "Thế giới"
                ]
                """);
        }
    }
}
