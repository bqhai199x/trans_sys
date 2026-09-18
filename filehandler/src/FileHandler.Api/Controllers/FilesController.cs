using System.Text;
using System.Text.Json;
using FileHandler.Api.Common;
using FileHandler.Api.Contracts;
using FileHandler.Api.Diagnostics;
using FileHandler.Api.Modules.Markdown;
using FileHandler.Api.Modules.PlainText;
using FileHandler.Api.Modules.Word;
using FileHandler.Api.Modules.Excel;
using FileHandler.Api.Modules.PowerPoint;
using Microsoft.AspNetCore.Mvc;

namespace FileHandler.Api.Controllers;

/// <summary>
/// REST API controller handling file imports and exports.
/// </summary>
[ApiController]
[Route("")]
[Traceable]
public sealed class FilesController : ControllerBase
{

    /// <summary>
    /// Markdown import and export service.
    /// </summary>
    private readonly MarkdownService _markdown;

    /// <summary>
    /// Plain text import and export service.
    /// </summary>
    private readonly PlainTextService _plainText;

    /// <summary>
    /// Word document import and export service.
    /// </summary>
    private readonly WordService _word;

    /// <summary>
    /// Excel spreadsheet import and export service.
    /// </summary>
    private readonly ExcelService _excel;

    /// <summary>
    /// PowerPoint presentation import and export service.
    /// </summary>
    private readonly PowerPointService _powerPoint;

    /// <summary>
    /// Request translation limits.
    /// </summary>
    private readonly FileHandlingOptions _options;

    /// <summary>
    /// Creates controller with format-specific services.
    /// </summary>
    /// <param name="markdown">Markdown import and export service.</param>
    /// <param name="plainText">Plain text import and export service.</param>
    /// <param name="word">Word document import and export service.</param>
    /// <param name="excel">Excel spreadsheet import and export service.</param>
    /// <param name="powerPoint">PowerPoint presentation import and export service.</param>
    /// <param name="options">Configured request limits, or defaults for direct callers.</param>
    public FilesController(
        MarkdownService markdown,
        PlainTextService plainText,
        WordService word,
        ExcelService excel,
        PowerPointService powerPoint,
        Microsoft.Extensions.Options.IOptions<FileHandlingOptions>? options = null)
    {
        _markdown = markdown;
        _plainText = plainText;
        _word = word;
        _excel = excel;
        _powerPoint = powerPoint;
        _options = options?.Value ?? new FileHandlingOptions();
    }

    /// <summary>
    /// Imports uploaded Markdown or plain text file as text array.
    /// </summary>
    /// <param name="request">Multipart request data.</param>
    /// <param name="cancellationToken">Token for cancelling this operation.</param>
    /// <returns>Task containing extracted texts or error response.</returns>
    [HttpPost("import")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(string[]), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(FileError[]), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(FileError[]), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(FileError[]), StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType(typeof(FileError[]), StatusCodes.Status422UnprocessableEntity)]
    public Task<IActionResult> Import([FromForm] ImportRequest request, CancellationToken cancellationToken) =>
        DebugTrace.TraceAsync("FilesController", "Import", () => new { request, cancellationToken }, async trace =>
        {
            if (ValidateUpload(request.File, out var fileType) is { } invalid)
                return invalid;
            trace.State("fileType", () => fileType);

            await using var stream = request.File!.OpenReadStream();
            var result = await GetHandler(fileType).ImportAsync(stream, cancellationToken);
            return result.Errors.Count == 0 ? Ok(result.Texts) : ErrorResult(result.Errors);
        });

    /// <summary>
    /// Exports uploaded Markdown or plain text file with supplied translations.
    /// </summary>
    /// <param name="request">Multipart request data.</param>
    /// <param name="cancellationToken">Token for cancelling this operation.</param>
    /// <returns>Task containing translated file content or error response.</returns>
    [HttpPost("export")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK, "text/markdown", "text/plain", WordService.ContentType, ExcelService.ContentType, PowerPointService.ContentType)]
    [ProducesResponseType(typeof(FileError[]), StatusCodes.Status400BadRequest, "application/json")]
    [ProducesResponseType(typeof(FileError[]), StatusCodes.Status413PayloadTooLarge, "application/json")]
    [ProducesResponseType(typeof(FileError[]), StatusCodes.Status415UnsupportedMediaType, "application/json")]
    [ProducesResponseType(typeof(FileError[]), StatusCodes.Status422UnprocessableEntity, "application/json")]
    public Task<IActionResult> Export([FromForm] ExportRequest request, CancellationToken cancellationToken) =>
        DebugTrace.TraceAsync("FilesController", "Export", () => new { request, cancellationToken }, async trace =>
        {
            if (ValidateUpload(request.File, out var fileType) is { } invalid)
                return invalid;
            trace.State("fileType", () => fileType);

            var jsonText = request.TranslatedTexts;
            if (string.IsNullOrWhiteSpace(jsonText) && Request?.HasFormContentType == true && Request.Form.Files["translatedTexts"] is { Length: > 0 } file)
            {
                trace.State("translationInput", () => "fileUpload");
                using var reader = new StreamReader(file.OpenReadStream());
                jsonText = await reader.ReadToEndAsync(cancellationToken);
            }
            else
                trace.State("translationInput", () => jsonText is null ? "missing" : "formField");

            if (!TryParseTranslations(jsonText, out var translations, out var parseError))
                return parseError!;
            trace.State("translationCount", () => translations!.Count);

            await using var stream = request.File!.OpenReadStream();
            var result = await GetHandler(fileType).ExportAsync(stream, translations!, cancellationToken);
            return result.Errors.Count == 0
                ? File(result.Content!, result.ContentType, FileTypeDetector.GetTranslatedFileName(request.File.FileName, fileType))
                : ErrorResult(result.Errors);
        });

    /// <summary>
    /// Validates uploaded file presence and supported file format.
    /// </summary>
    /// <param name="file">Uploaded form file.</param>
    /// <param name="fileType">Detected source format when validation succeeds.</param>
    /// <returns>Error action result if validation fails; otherwise null.</returns>
    private IActionResult? ValidateUpload(IFormFile? file, out FileType fileType)
    {
        fileType = default;
        if (file is null)
            return BadRequest(Errors("missing_file", "Field file là bắt buộc."));
        if (!FileTypeDetector.TryDetect(file.FileName, out fileType))
            return StatusCode(415, Errors("unsupported_file_type", "Chỉ hỗ trợ tệp .md, .txt, .docx, .xlsx và .pptx."));
        return null;
    }

    /// <summary>
    /// Selects handler for validated source format.
    /// </summary>
    /// <param name="fileType">Detected supported source format.</param>
    /// <returns>Format-specific import and export handler.</returns>
    /// <exception cref="ArgumentOutOfRangeException">File type is unsupported.</exception>
    private IFileHandler GetHandler(FileType fileType) =>
        DebugTrace.Trace<IFileHandler>("FilesController", "GetHandler", () => new { fileType }, _ => fileType switch
        {
            FileType.Markdown => _markdown,
            FileType.PlainText => _plainText,
            FileType.Word => _word,
            FileType.Excel => _excel,
            FileType.PowerPoint => _powerPoint,
            _ => throw new ArgumentOutOfRangeException(nameof(fileType))
        });

    /// <summary>
    /// Document options allowing trailing commas and skipping comments in translation JSON.
    /// </summary>
    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    /// <summary>
    /// Parses and validates JSON array of translated text units.
    /// </summary>
    /// <param name="jsonText">Serialized JSON array string.</param>
    /// <param name="translations">Parsed string array when valid.</param>
    /// <param name="errorResult">Error response when parsing or structure fails.</param>
    /// <returns>True when parsing succeeds; otherwise false.</returns>
    private bool TryParseTranslations(string? jsonText, out IReadOnlyList<string>? translations, out IActionResult? errorResult)
    {
        using var trace = DebugTrace.Enter("FilesController", "TryParseTranslations", () => new { jsonText });
        if (jsonText is null)
        {
            translations = null;
            errorResult = BadRequest(Errors("missing_translated_texts", "Field translatedTexts là bắt buộc."));
            return trace.Return(false);
        }

        try
        {
            trace.State("parseMode", () => "original");
            return trace.Return(TryParseJsonArray(jsonText, out translations, out errorResult));
        }
        catch (JsonException)
        {
            try
            {
                trace.State("parseMode", () => "normalizedNewlines");
                var normalized = NormalizeJsonStringNewlines(jsonText);
                trace.State("jsonText", () => normalized);
                return trace.Return(TryParseJsonArray(normalized, out translations, out errorResult));
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or DecoderFallbackException)
            {
                translations = null;
                errorResult = BadRequest(Errors("invalid_json", "translatedTexts không phải JSON hợp lệ."));
                return trace.Return(false);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or DecoderFallbackException)
        {
            translations = null;
            errorResult = BadRequest(Errors("invalid_json", "translatedTexts chứa chuỗi Unicode không hợp lệ."));
            return trace.Return(false);
        }
    }

    /// <summary>
    /// Parses JSON text into a list of strings using configured parser options.
    /// </summary>
    /// <param name="jsonText">Serialized JSON text to parse.</param>
    /// <param name="translations">Extracted list of strings.</param>
    /// <param name="errorResult">Error response when structure fails.</param>
    /// <returns>True when parsing succeeds; otherwise false.</returns>
    private bool TryParseJsonArray(string jsonText, out IReadOnlyList<string>? translations, out IActionResult? errorResult)
    {
        using var json = JsonDocument.Parse(jsonText, JsonOptions);
        if (json.RootElement.ValueKind != JsonValueKind.Array)
        {
            translations = null;
            errorResult = BadRequest(Errors("invalid_translated_texts", "translatedTexts phải là JSON array chuỗi."));
            return false;
        }

        if (json.RootElement.GetArrayLength() > _options.MaxUnits)
        {
            translations = null;
            errorResult = StatusCode(StatusCodes.Status413PayloadTooLarge, Errors("too_many_units", "Số lượng bản dịch vượt giới hạn."));
            return false;
        }
        var list = new List<string>(json.RootElement.GetArrayLength());
        foreach (var element in json.RootElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.String)
            {
                translations = null;
                errorResult = BadRequest(Errors("invalid_translated_texts", "Mỗi phần tử translatedTexts phải là chuỗi và không được null."));
                return false;
            }

            try
            {
                list.Add(element.GetString()!);
            }
            catch (Exception ex) when (ex is InvalidOperationException or FormatException or DecoderFallbackException)
            {
                translations = null;
                errorResult = BadRequest(Errors("invalid_json", "translatedTexts chứa chuỗi Unicode không hợp lệ."));
                return false;
            }
        }

        translations = list;
        errorResult = null;
        return true;
    }

    /// <summary>
    /// Normalizes unescaped literal newlines inside JSON string literals while respecting comments.
    /// </summary>
    /// <param name="json">Raw JSON text containing potential unescaped newlines.</param>
    /// <returns>Normalized JSON text with escaped newlines.</returns>
    private static string NormalizeJsonStringNewlines(string json)
    {
        // Fast path: if no raw newlines at all, return as-is without any allocation.
        if (json.IndexOfAny(['\r', '\n']) < 0)
            return json;

        var sb = new StringBuilder(json.Length + 64);
        var inString = false;
        var inBlockComment = false;
        var inLineComment = false;
        var isEscaped = false;

        for (var i = 0; i < json.Length; i++)
        {
            var c = json[i];
            if (inBlockComment)
            {
                if (c == '*' && i + 1 < json.Length && json[i + 1] == '/')
                {
                    sb.Append("*/");
                    i++;
                    inBlockComment = false;
                    continue;
                }
                sb.Append(c);
                continue;
            }
            if (inLineComment)
            {
                sb.Append(c);
                if (c is '\r' or '\n')
                    inLineComment = false;
                continue;
            }
            else if (inString)
            {
                if (isEscaped)
                {
                    sb.Append(c);
                    isEscaped = false;
                }
                else if (c == '\\')
                {
                    sb.Append(c);
                    isEscaped = true;
                }
                else if (c == '"')
                {
                    sb.Append(c);
                    inString = false;
                }
                else if (c == '\r')
                {
                    if (i + 1 < json.Length && json[i + 1] == '\n')
                        i++;
                    sb.Append("\\n");
                }
                else if (c == '\n')
                {
                    sb.Append("\\n");
                }
                else
                {
                    sb.Append(c);
                }
            }
            else
            {
                if (c == '/' && i + 1 < json.Length && json[i + 1] == '*')
                {
                    inBlockComment = true;
                    sb.Append("/*");
                    i++;
                    continue;
                }
                if (c == '/' && i + 1 < json.Length && json[i + 1] == '/')
                {
                    inLineComment = true;
                    sb.Append("//");
                    i++;
                    continue;
                }
                if (c == '"')
                    inString = true;
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Maps file validation errors to HTTP response.
    /// </summary>
    /// <param name="errors">File validation errors.</param>
    /// <returns>HTTP response containing validation errors.</returns>
    private IActionResult ErrorResult(IReadOnlyList<FileError> errors) =>
        DebugTrace.Trace("FilesController", "ErrorResult", () => new { errors }, _ =>
        {
            var status = errors.Any(x => x.Code is "file_too_large" or "too_many_units" or "translation_too_long" or "output_too_large"
                or "office_package_limit_exceeded" or "office_plan_limit_exceeded" or "office_translation_limit_exceeded" or "office_schema_limit_exceeded") ? 413 : 422;
            return StatusCode(status, errors);
        });

    /// <summary>
    /// Creates single-item file error array.
    /// </summary>
    /// <param name="code">Machine-readable error code.</param>
    /// <param name="message">Error description.</param>
    /// <returns>Single-item error array.</returns>
    private static FileError[] Errors(string code, string message) =>
        DebugTrace.Trace<FileError[]>("FilesController", "Errors", () => new { code, message }, _ => [new(code, message)]);
}
