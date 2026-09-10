using Markdig;

namespace FileHandler.Api.Modules.Markdown;

internal static class MarkdownProfile
{
    public const string Version = "filehandler-markdown-v1";

    public static MarkdownPipeline CreatePipeline() => new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseTaskLists()
        .UseEmphasisExtras()
        .UseMathematics()
        .UseYamlFrontMatter()
        .Build();
}
