using Markdig;

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
    public static MarkdownPipeline CreatePipeline() =>
        new MarkdownPipelineBuilder().UsePipeTables().UseTaskLists().UseEmphasisExtras().UseMathematics().UseYamlFrontMatter().Build();
}
