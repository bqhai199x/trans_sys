using System.Text.Json;
using FileHandler.Api.Common;
using FileHandler.Api.Contracts;
using FileHandler.Api.Modules.Markdown;
using Microsoft.AspNetCore.Mvc;

namespace FileHandler.Api.Controllers;

[ApiController]
[Route("")]
public sealed class FilesController : ControllerBase
{
    private readonly MarkdownService _markdown;
    public FilesController(MarkdownService markdown) => _markdown = markdown;

    [HttpPost("import")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(string[]), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(FileError[]), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(FileError[]), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Import([FromForm] ImportRequest request, CancellationToken cancellationToken)
    {
        if (request.File is null) return BadRequest(Errors("missing_file", "Field file là bắt buộc."));
        if (!FileTypeDetector.TryDetect(request.File.FileName, out _)) return StatusCode(415, Errors("unsupported_file_type", "Chỉ hỗ trợ tệp .md."));
        await using var stream = request.File.OpenReadStream();
        var result = await _markdown.ImportAsync(stream, cancellationToken);
        return result.Errors.Count == 0 ? Ok(result.Texts) : ErrorResult(result.Errors);
    }

    [HttpPost("export")]
    [Consumes("multipart/form-data")]
    [Produces("text/markdown", "application/json")]
    public async Task<IActionResult> Export([FromForm] ExportRequest request, CancellationToken cancellationToken)
    {
        if (request.File is null) return BadRequest(Errors("missing_file", "Field file là bắt buộc."));
        if (request.TranslatedTexts is null) return BadRequest(Errors("missing_translated_texts", "Field translatedTexts là bắt buộc."));
        if (!FileTypeDetector.TryDetect(request.File.FileName, out _)) return StatusCode(415, Errors("unsupported_file_type", "Chỉ hỗ trợ tệp .md."));

        IReadOnlyList<string> translations;
        try
        {
            using var json = JsonDocument.Parse(request.TranslatedTexts);
            if (json.RootElement.ValueKind != JsonValueKind.Array) return BadRequest(Errors("invalid_translated_texts", "translatedTexts phải là JSON array chuỗi."));
            var list = new List<string>();
            foreach (var element in json.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.String) return BadRequest(Errors("invalid_translated_texts", "Mỗi phần tử translatedTexts phải là chuỗi và không được null."));
                list.Add(element.GetString()!);
            }
            translations = list;
        }
        catch (JsonException) { return BadRequest(Errors("invalid_json", "translatedTexts không phải JSON hợp lệ.")); }

        await using var stream = request.File.OpenReadStream();
        var result = await _markdown.ExportAsync(stream, translations, cancellationToken);
        return result.Errors.Count == 0
            ? File(result.Content!, result.ContentType, FileTypeDetector.GetTranslatedFileName(request.File.FileName))
            : ErrorResult(result.Errors);
    }

    private IActionResult ErrorResult(IReadOnlyList<FileError> errors)
    {
        var status = errors.Any(x => x.Code is "file_too_large" or "too_many_units" or "translation_too_long" or "output_too_large") ? 413 : 422;
        return StatusCode(status, errors);
    }

    private static FileError[] Errors(string code, string message) => [new(code, message)];
}
