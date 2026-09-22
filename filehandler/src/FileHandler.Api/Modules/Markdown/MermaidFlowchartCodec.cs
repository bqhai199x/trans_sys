using FileHandler.Api.Common;
using Markdig.Syntax;

namespace FileHandler.Api.Modules.Markdown;

/// <summary>
/// Flowchart label extraction adapter delegating to MermaidCodec.
/// </summary>
internal static class MermaidFlowchartCodec
{

    /// <summary>
    /// Extracts labels from flowchart fences using MermaidCodec.
    /// </summary>
    /// <param name="block">Parsed fenced code block.</param>
    /// <param name="cancellationToken">Token for cancelling extraction.</param>
    /// <returns>Ordered label spans with decoded text.</returns>
    internal static IEnumerable<MermaidLabel> Extract(FencedCodeBlock block, CancellationToken cancellationToken) =>
        MermaidCodec.Extract(block, cancellationToken);

    /// <summary>
    /// Encodes flowchart labels using MermaidCodec.
    /// </summary>
    /// <param name="text">Validated single-line translated label.</param>
    /// <returns>Quoted label safe for node and edge delimiters.</returns>
    internal static string Encode(string text) =>
        MermaidCodec.Encode(text, quoted: true);
}
