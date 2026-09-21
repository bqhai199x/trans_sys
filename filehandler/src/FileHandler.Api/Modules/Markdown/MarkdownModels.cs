using FileHandler.Api.Common;

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
        var index = Array.BinarySearch(Starts, Math.Max(0, offset));
        return index >= 0 ? index + 1 : ~index;
    }

    /// <summary>
    /// Maps exclusive-end character span to inclusive line range.
    /// </summary>
    /// <param name="start">Inclusive start character offset.</param>
    /// <param name="end">Exclusive end character offset.</param>
    /// <returns>Inclusive, one-based source line range.</returns>
    public SourceLineRange GetRange(int start, int end)
    {
        return new(GetLine(start), GetLine(Math.Max(start, end - 1)));
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
internal sealed record MarkerDefinition(int Id, MarkerKind Kind, string OpenSource, string CloseSource)
{

    /// <summary>
    /// Whether empty emphasis delimiters can be omitted during restoration.
    /// </summary>
    public bool IsEmphasis { get; init; }
}

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
internal sealed record MarkdownUnit(int Start, int End, string Text, IReadOnlyDictionary<int, MarkerDefinition> Markers, SourceLineRange Line, bool IsHeading, string NewlineReplacement, bool HasSoftBreak, MarkdownTokenTemplate? TokenTemplate = null)
{

    /// <summary>
    /// Inclusive enclosing block start for local structure validation.
    /// </summary>
    public int? BlockStart { get; init; }

    /// <summary>
    /// Exclusive enclosing block end for local structure validation.
    /// </summary>
    public int? BlockEnd { get; init; }

    /// <summary>
    /// Whether translated text requires Mermaid label encoding instead of Markdown escaping.
    /// </summary>
    public bool IsMermaidLabel { get; init; }

    /// <summary>
    /// Whether Mermaid label requires enclosing double quotes on export.
    /// </summary>
    public bool MermaidQuoted { get; init; }
}

/// <summary>
/// Extracted units and document validation metadata.
/// </summary>
/// <param name="Source">Original source document.</param>
/// <param name="Units">Translation units in source order.</param>
/// <param name="Errors">Extraction errors.</param>
/// <param name="HasInternalLinks">Whether document contains internal anchor links.</param>
internal sealed record MarkdownExtraction(MarkdownSource Source, IReadOnlyList<MarkdownUnit> Units, IReadOnlyList<FileError> Errors, bool HasInternalLinks = false)
{

    /// <summary>
    /// Protected source blocks and inline objects excluded during extraction.
    /// </summary>
    public IReadOnlyList<SkipMetadata> Skipped { get; init; } = [];
}

/// <summary>
/// Encoded inline text and its original source span.
/// </summary>
/// <param name="Tokens">Typed text and syntax bindings without serialized markers.</param>
/// <param name="Start">Inclusive start character offset.</param>
/// <param name="End">Exclusive end character offset.</param>
/// <param name="Markers">Marker definitions keyed by ID.</param>
internal sealed record EncodedInline(IReadOnlyList<MarkerToken> Tokens, int Start, int End, IReadOnlyDictionary<int, MarkerDefinition> Markers);
