using Markdig;
using FileHandler.Api.Diagnostics;

namespace FileHandler.Api.Modules.Markdown;

/// <summary>
/// Configuration and pipeline factory for supported Markdown profile.
/// </summary>
internal static class MarkdownProfile
{

    /// <summary>
    /// Supported Markdown profile version.
    /// </summary>
    public const string Version = "filehandler-markdown-v1";

    /// <summary>
    /// Builds Markdown pipeline for supported profile.
    /// </summary>
    /// <returns>Pipeline configured for supported Markdown extensions.</returns>
    public static MarkdownPipeline CreatePipeline()
    {
        using var trace = DebugTrace.Enter("MarkdownProfile", "CreatePipeline", () => new { });
        try
        {
            return trace.Return<MarkdownPipeline>(new MarkdownPipelineBuilder().UsePipeTables().UseTaskLists().UseEmphasisExtras().UseMathematics().UseYamlFrontMatter().Build());
        }
        catch (Exception traceError)
        {
            trace.Error(traceError);
            throw;
        }
    }
}
