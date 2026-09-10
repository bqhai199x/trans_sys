using System.Text;
using FileHandler.Api.Common;

namespace FileHandler.Api.Modules.Markdown;

internal static class MarkdownTranslationApplier
{
    public static (string? Text, IReadOnlyList<FileError> Errors) Apply(
        MarkdownExtraction extraction, IReadOnlyList<string> translations, FileHandlingOptions options, CancellationToken cancellationToken)
    {
        var errors = ValidateBatch(extraction, translations, options);
        if (errors.Count > 0) return (null, errors);

        if (extraction.HasInternalLinks && extraction.Units.Where(x => x.IsHeading).Select((unit, index) => (unit, index))
            .Any(x => translations[x.index] != x.unit.Text))
            return (null, [new("internal_anchor_change_unsupported", "Không thể đổi heading khi tài liệu có liên kết anchor nội bộ trong profile V1.")]);

        var replacements = new List<(int Start, int End, string Value)>();
        for (var i = 0; i < extraction.Units.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var unit = extraction.Units[i];
            var translated = translations[i];
            if (translated == unit.Text) continue;
            if (unit.IsHeading && translated.IndexOfAny(['\r', '\n']) >= 0)
            {
                errors.Add(new("invalid_structure", "Bản dịch heading không được tạo thêm dòng hoặc block.", i, unit.Line));
                continue;
            }
            var (value, markerErrors) = DecodeTranslation(unit, translated, i);
            errors.AddRange(markerErrors);
            if (value is not null) replacements.Add((unit.Start, unit.End, value));
        }
        if (errors.Count > 0) return (null, errors);

        replacements.Sort((a, b) => b.Start.CompareTo(a.Start));
        for (var i = 1; i < replacements.Count; i++)
            if (replacements[i - 1].Start < replacements[i].End)
                errors.Add(new("patch_conflict", "Các vùng thay thế bị chồng lấn."));
        if (errors.Count > 0) return (null, errors);

        var output = new StringBuilder(extraction.Source.Text);
        foreach (var patch in replacements) output.Remove(patch.Start, patch.End - patch.Start).Insert(patch.Start, patch.Value);
        return (output.ToString(), []);
    }

    private static List<FileError> ValidateBatch(MarkdownExtraction extraction, IReadOnlyList<string> translations, FileHandlingOptions options)
    {
        var errors = new List<FileError>();
        if (extraction.Errors.Count > 0) errors.AddRange(extraction.Errors);
        if (translations.Count != extraction.Units.Count)
            errors.Add(new("translation_count_mismatch", $"Cần {extraction.Units.Count} bản dịch nhưng nhận được {translations.Count}."));
        var count = Math.Min(translations.Count, extraction.Units.Count);
        for (var i = 0; i < count; i++)
        {
            if (translations[i] is null) errors.Add(new("invalid_translation", "Bản dịch không được null.", i, extraction.Units[i].Line));
            else if (string.IsNullOrWhiteSpace(translations[i])) errors.Add(new("empty_translation", "Bản dịch không được rỗng hoặc chỉ chứa khoảng trắng.", i, extraction.Units[i].Line));
            else if (translations[i].Length > options.MaxTranslationChars) errors.Add(new("translation_too_long", $"Bản dịch vượt giới hạn {options.MaxTranslationChars} ký tự.", i, extraction.Units[i].Line));
        }
        return errors;
    }

    private static (string? Value, List<FileError> Errors) DecodeTranslation(MarkdownUnit unit, string translation, int index)
    {
        var errors = new List<FileError>();
        var tokens = MarkdownMarkerCodec.Parse(translation);
        var seenOpen = new HashSet<int>();
        var seenClose = new HashSet<int>();
        var stack = new Stack<int>();
        var output = new StringBuilder();

        foreach (var token in tokens)
        {
            if (!token.IsMarker)
            {
                if (token.Value.Contains("<keepme", StringComparison.Ordinal))
                    errors.Add(new("invalid_marker_syntax", "Marker keepme không đúng cú pháp hoặc vượt miền ID hỗ trợ.", index, unit.Line));
                output.Append(EscapeText(token.Value, unit.NewlineReplacement));
                continue;
            }
            var canonical = token.IsClosing ? MarkdownMarkerCodec.Close(token.Id) : MarkdownMarkerCodec.Open(token.Id);
            if (!string.Equals(token.Value, canonical, StringComparison.Ordinal))
            {
                errors.Add(new("invalid_marker_syntax", $"Marker {token.Value} không ở dạng chuẩn {canonical}.", index, unit.Line, token.Value));
                continue;
            }
            if (!unit.Markers.TryGetValue(token.Id, out var definition))
            {
                errors.Add(new("unexpected_marker", $"Marker {token.Value} không thuộc đơn vị này.", index, unit.Line, token.Value));
                continue;
            }
            if (!token.IsClosing)
            {
                if (!seenOpen.Add(token.Id)) errors.Add(new("duplicate_marker", $"Marker {token.Value} bị lặp.", index, unit.Line, token.Value));
                stack.Push(token.Id);
                output.Append(definition.OpenSource);
            }
            else
            {
                if (!seenClose.Add(token.Id)) errors.Add(new("duplicate_marker", $"Marker {token.Value} bị lặp.", index, unit.Line, token.Value));
                if (stack.Count == 0 || stack.Pop() != token.Id)
                    errors.Add(new("invalid_marker_nesting", $"Marker {token.Value} đóng sai thứ tự.", index, unit.Line, token.Value));
                output.Append(definition.CloseSource);
            }
        }

        foreach (var marker in unit.Markers.Values)
        {
            if (!seenOpen.Contains(marker.Id)) errors.Add(new("missing_marker", $"Thiếu marker mở {MarkdownMarkerCodec.Open(marker.Id)}.", index, unit.Line, MarkdownMarkerCodec.Open(marker.Id)));
            if (!seenClose.Contains(marker.Id)) errors.Add(new("missing_marker", $"Thiếu marker đóng {MarkdownMarkerCodec.Close(marker.Id)}.", index, unit.Line, MarkdownMarkerCodec.Close(marker.Id)));
        }

        foreach (var marker in unit.Markers.Values.Where(x => x.Kind == MarkerKind.Protected))
        {
            var openIndex = translation.IndexOf(MarkdownMarkerCodec.Open(marker.Id), StringComparison.Ordinal);
            var closeIndex = translation.IndexOf(MarkdownMarkerCodec.Close(marker.Id), StringComparison.Ordinal);
            if (openIndex >= 0 && closeIndex >= 0 && closeIndex != openIndex + MarkdownMarkerCodec.Open(marker.Id).Length)
                errors.Add(new("protected_marker_not_empty", $"Marker bảo vệ {MarkdownMarkerCodec.Open(marker.Id)} phải rỗng.", index, unit.Line, MarkdownMarkerCodec.Open(marker.Id)));
        }
        return errors.Count == 0 ? (output.ToString(), errors) : (null, errors);
    }

    private static string EscapeText(string text, string newlineReplacement)
    {
        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') { sb.Append(newlineReplacement); i++; continue; }
            if (c is '\r' or '\n') { sb.Append(newlineReplacement); continue; }
            if (c is '\\' or '`' or '*' or '_' or '{' or '}' or '[' or ']' or '(' or ')' or '<' or '>' or '#' or '!' or '|' or '+' or '-' or '=' or '~') sb.Append('\\');
            sb.Append(c);
        }
        return sb.ToString();
    }
}
