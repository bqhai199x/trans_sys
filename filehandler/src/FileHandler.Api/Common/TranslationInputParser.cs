using System.Text;
using System.Text.Json;

namespace FileHandler.Api.Common;

/// <summary>
/// Parses and normalizes translated text arrays from multipart form data.
/// </summary>
internal static class TranslationInputParser
{

    /// <summary>
    /// Document options allowing trailing commas and skipping comments in JSON.
    /// </summary>
    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    /// <summary>
    /// Reads and parses translation texts from form field or uploaded form file.
    /// </summary>
    /// <param name="formFiles">Request form file collection.</param>
    /// <param name="rawTexts">String content supplied in texts field.</param>
    /// <param name="options">Configured limits for translations.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task containing success flag, parsed translations or null, and validation error or null.</returns>
    internal static async Task<(bool Success, IReadOnlyList<string>? Translations, FileError? Error)> TryParseAsync(
        IFormFileCollection? formFiles,
        string? rawTexts,
        FileHandlingOptions options,
        CancellationToken cancellationToken = default)
    {
        var jsonText = rawTexts;
        if (string.IsNullOrWhiteSpace(jsonText) && formFiles is not null)
        {
            var file = formFiles["texts"];
            if (file is { Length: > 0 })
            {
                using var reader = new StreamReader(file.OpenReadStream());
                jsonText = await reader.ReadToEndAsync(cancellationToken);
            }
        }

        if (string.IsNullOrWhiteSpace(jsonText))
            return (false, null, new FileError("missing_texts", ProcessingMessages.MissingTexts));

        jsonText = jsonText.Trim();

        try
        {
            return TryParseJsonArray(jsonText, options);
        }
        catch (JsonException)
        {
            try
            {
                var normalized = NormalizeJsonStringNewlines(jsonText);
                return TryParseJsonArray(normalized, options);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or DecoderFallbackException)
            {
                return (false, null, new FileError("invalid_json", ProcessingMessages.InvalidJson));
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or DecoderFallbackException)
        {
            return (false, null, new FileError("invalid_json", ProcessingMessages.InvalidJsonUnicode));
        }
    }

    /// <summary>
    /// Parses JSON array string into string list with double-serialization unwrapping.
    /// </summary>
    /// <param name="jsonText">Serialized JSON text to parse.</param>
    /// <param name="options">File handling options for limits.</param>
    /// <returns>Parsing outcome tuple.</returns>
    private static (bool Success, IReadOnlyList<string>? Translations, FileError? Error) TryParseJsonArray(string jsonText, FileHandlingOptions options)
    {
        using var json = JsonDocument.Parse(jsonText, JsonOptions);
        var root = json.RootElement;
        if (root.ValueKind == JsonValueKind.String)
        {
            var inner = root.GetString();
            if (!string.IsNullOrWhiteSpace(inner) && inner.TrimStart().StartsWith('['))
            {
                using var innerJson = JsonDocument.Parse(inner, JsonOptions);
                return ExtractStringList(innerJson.RootElement, options);
            }
        }

        return ExtractStringList(root, options);
    }

    /// <summary>
    /// Extracts string list from JSON array element.
    /// </summary>
    /// <param name="arrayElement">JSON root or nested array element.</param>
    /// <param name="options">File handling options for limits.</param>
    /// <returns>Extraction outcome tuple.</returns>
    private static (bool Success, IReadOnlyList<string>? Translations, FileError? Error) ExtractStringList(JsonElement arrayElement, FileHandlingOptions options)
    {
        if (arrayElement.ValueKind != JsonValueKind.Array)
            return (false, null, new FileError("invalid_texts", ProcessingMessages.InvalidTexts));

        if (arrayElement.GetArrayLength() > options.MaxUnits)
            return (false, null, new FileError("too_many_units", ProcessingMessages.TooManyTranslations));

        var list = new List<string>(arrayElement.GetArrayLength());
        foreach (var element in arrayElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.String)
                return (false, null, new FileError("invalid_texts", ProcessingMessages.InvalidTextElement));

            try
            {
                list.Add(element.GetString()!);
            }
            catch (Exception ex) when (ex is InvalidOperationException or FormatException or DecoderFallbackException)
            {
                return (false, null, new FileError("invalid_json", ProcessingMessages.InvalidJsonUnicode));
            }
        }

        return (true, list, null);
    }

    /// <summary>
    /// Normalizes unescaped literal newlines inside JSON string literals while respecting comments.
    /// </summary>
    /// <param name="json">Raw JSON text containing potential unescaped newlines.</param>
    /// <returns>Normalized JSON text with escaped newlines.</returns>
    private static string NormalizeJsonStringNewlines(string json)
    {
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
            if (inString)
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
}
