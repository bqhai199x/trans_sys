using FileHandler.Api.Common;

namespace FileHandler.Api.Modules.Markdown;

internal sealed record MarkdownSource(byte[] Bytes, string Text, bool HasBom, LineMap Lines);
internal sealed record LineMap(int[] Starts)
{
    public int GetLine(int offset)
    {
        var index = Array.BinarySearch(Starts, Math.Max(0, offset));
        return index >= 0 ? index + 1 : ~index;
    }

    public SourceLineRange GetRange(int start, int end) => new(GetLine(start), GetLine(Math.Max(start, end - 1)));
}

internal enum MarkerKind { Formatting, Protected }
internal sealed record MarkerDefinition(int Id, MarkerKind Kind, string OpenSource, string CloseSource);
internal sealed record MarkdownUnit(
    int Start,
    int End,
    string Text,
    IReadOnlyDictionary<int, MarkerDefinition> Markers,
    SourceLineRange Line,
    bool IsHeading,
    string NewlineReplacement,
    bool HasSoftBreak);
internal sealed record MarkdownExtraction(MarkdownSource Source, IReadOnlyList<MarkdownUnit> Units, IReadOnlyList<FileError> Errors, bool HasInternalLinks = false);
internal sealed record EncodedInline(string Text, int Start, int End, IReadOnlyDictionary<int, MarkerDefinition> Markers);
