using FileHandler.Api.Common;
using FileHandler.Api.Diagnostics;

namespace FileHandler.Api.Modules.Markdown;

/// <summary>
/// Original Markdown bytes and decoded source metadata.
/// </summary>
/// <param name="Bytes">Original file bytes.</param>
/// <param name="Text">Decoded text without BOM.</param>
/// <param name="HasBom">Whether source begins with UTF-8 BOM.</param>
/// <param name="Lines">Source line offset map.</param>
internal sealed record MarkdownSource(byte[] Bytes, string Text, bool HasBom, LineMap Lines);

/// <summary>
/// Maps character offsets to source line numbers.
/// </summary>
/// <param name="Starts">Zero-based character offset of each line start.</param>
internal sealed record LineMap(int[] Starts)
{

    /// <summary>
    /// Returns one-based line number for character offset.
    /// </summary>
    /// <param name="offset">Zero-based character offset.</param>
    /// <returns>One-based line number containing this offset.</returns>
    public int GetLine(int offset)
    {
        using var trace = DebugTrace.Enter("LineMap", "GetLine", () => new { offset });
        try
        {
            var index = Array.BinarySearch(Starts, Math.Max(0, offset));
            return trace.Return<int>(index >= 0 ? index + 1 : ~index);
        }
        catch (Exception traceError)
        {
            trace.Error(traceError);
            throw;
        }
    }

    /// <summary>
    /// Maps exclusive-end character span to inclusive line range.
    /// </summary>
    /// <param name="start">Inclusive start character offset.</param>
    /// <param name="end">Exclusive end character offset.</param>
    /// <returns>Inclusive, one-based source line range.</returns>
    public SourceLineRange GetRange(int start, int end)
    {
        using var trace = DebugTrace.Enter("LineMap", "GetRange", () => new { start, end });
        try
        {
            return trace.Return<SourceLineRange>(new(GetLine(start), GetLine(Math.Max(start, end - 1))));
        }
        catch (Exception traceError)
        {
            trace.Error(traceError);
            throw;
        }
    }
}

/// <summary>
/// Source preservation behavior for marker pairs.
/// </summary>
internal enum MarkerKind
{

    /// <summary>
    /// Formatting syntax around translatable content.
    /// </summary>
    Formatting,

    /// <summary>
    /// Source content excluded from translation.
    /// </summary>
    Protected
}

/// <summary>
/// Source syntax preserved by marker pair.
/// </summary>
/// <param name="Id">Marker ID.</param>
/// <param name="Kind">Marker&apos;s preservation behavior.</param>
/// <param name="OpenSource">Source restored at opening marker.</param>
/// <param name="CloseSource">Source restored at closing marker.</param>
internal sealed record MarkerDefinition(int Id, MarkerKind Kind, string OpenSource, string CloseSource);

/// <summary>
/// Translatable source span with preservation metadata.
/// </summary>
/// <param name="Start">Inclusive start character offset.</param>
/// <param name="End">Exclusive end character offset.</param>
/// <param name="Text">Extracted text with preservation markers.</param>
/// <param name="Markers">Marker definitions keyed by ID.</param>
/// <param name="Line">Inclusive source line range.</param>
/// <param name="IsHeading">Whether unit belongs to heading.</param>
/// <param name="NewlineReplacement">Source newline sequence and any required prefix.</param>
/// <param name="HasSoftBreak">Whether unit contains soft line break.</param>
/// <param name="TokenTemplate">Public run and anchor mapping, when extracted from Markdown.</param>
internal sealed record MarkdownUnit(int Start, int End, string Text, IReadOnlyDictionary<int, MarkerDefinition> Markers, SourceLineRange Line, bool IsHeading, string NewlineReplacement, bool HasSoftBreak, MarkdownTokenTemplate? TokenTemplate = null);

/// <summary>
/// Extracted units and document validation metadata.
/// </summary>
/// <param name="Source">Original source document.</param>
/// <param name="Units">Translation units in source order.</param>
/// <param name="Errors">Extraction errors.</param>
/// <param name="HasInternalLinks">Whether document contains internal anchor links.</param>
internal sealed record MarkdownExtraction(MarkdownSource Source, IReadOnlyList<MarkdownUnit> Units, IReadOnlyList<FileError> Errors, bool HasInternalLinks = false);

/// <summary>
/// Encoded inline text and its original source span.
/// </summary>
/// <param name="Text">Text with preservation markers.</param>
/// <param name="Start">Inclusive start character offset.</param>
/// <param name="End">Exclusive end character offset.</param>
/// <param name="Markers">Marker definitions keyed by ID.</param>
internal sealed record EncodedInline(string Text, int Start, int End, IReadOnlyDictionary<int, MarkerDefinition> Markers);
