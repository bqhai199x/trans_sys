using System.Text;
using FileHandler.Api.Common;
using FileHandler.Api.Modules.Markdown;
using Microsoft.Extensions.Options;

namespace FileHandler.Tests.Modules.Markdown;

/// <summary>
/// Verifies flowchart label extraction and source-preserving Markdown export.
/// </summary>
public sealed class MermaidFlowchartTests
{

    /// <summary>
    /// User flowchart containing node, pipe-edge and dotted-edge labels.
    /// </summary>
    private const string Diagram = """
        ```mermaid
        flowchart LR
            U[Người dùng gửi câu hỏi] --> G[Initialization and Configuration]
            G --> Q[1. Query Understanding]
            Q --> V[2. Vector Retrieval]
            V -->|Có candidates| H[3. Knowledge Graph Expansion]
            V -->|Không có kết quả / lỗi| R
            H --> R[4. LLM Reasoning]
            R --> O[5. Result Packaging and Persistence]
            O --> X[Trả Query Pipeline Result]

            Q -. lỗi .-> V
            V -. lỗi .-> R
            H -. lỗi .-> R
        ```
        """;

    /// <summary>
    /// Checks every label is extracted in source order and identity preserves original bytes.
    /// </summary>
    /// <param name="newline">Source line ending.</param>
    /// <returns>Task completing after import and round-trip assertions.</returns>
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task UserFlowchart_ExtractsAllLabelsAndRoundTrips(string newline)
    {
        var source = Diagram.ReplaceLineEndings(newline);
        var bytes = Encoding.UTF8.GetBytes(source);
        var service = MarkdownService.Create(Options.Create(new FileHandlingOptions()));
        var imported = await service.ImportAsync(new MemoryStream(bytes));
        Assert.Empty(imported.Errors);
        Assert.Equal(new[]
        {
            "Người dùng gửi câu hỏi", "Initialization and Configuration", "1. Query Understanding",
            "2. Vector Retrieval", "Có candidates", "3. Knowledge Graph Expansion", "Không có kết quả / lỗi",
            "4. LLM Reasoning", "5. Result Packaging and Persistence", "Trả Query Pipeline Result", "lỗi", "lỗi", "lỗi"
        }, imported.Texts);
        var identity = await service.ExportAsync(new MemoryStream(bytes), imported.Texts);
        Assert.Empty(identity.Errors);
        Assert.Equal(bytes, identity.Content);
        var translations = imported.Texts.Select((_, index) => $"Bản dịch {index}").ToArray();
        var exported = await service.ExportAsync(new MemoryStream(bytes), translations);
        Assert.Empty(exported.Errors);
        var reimported = await service.ImportAsync(new MemoryStream(exported.Content!));
        Assert.Empty(reimported.Errors);
        Assert.Equal(translations, reimported.Texts);
        var result = Encoding.UTF8.GetString(exported.Content!);
        Assert.Contains("V -->|\"Bản dịch 6\"| R", result);
        Assert.Contains("Q -. \"Bản dịch 10\" .-> V", result);
        Assert.Contains("flowchart LR" + newline, result);
        Assert.DoesNotContain("keepme", result);
    }

    /// <summary>
    /// Checks quoted punctuation cannot inject Mermaid statements and decodes on reimport.
    /// </summary>
    /// <returns>Task completing after escaped-label assertions.</returns>
    [Fact]
    public async Task SpecialCharacters_AreEncodedForMermaidInsteadOfMarkdown()
    {
        const string source = "```mermaid\ngraph TD\n A[Track2：JAVA21/25互換対応] -->|Có| B[\"Done\"]\n```";
        var service = MarkdownService.Create(Options.Create(new FileHandlingOptions()));
        string[] translations = ["A ] --> Z[evil] \"quoted\" # <tag> `code` \\ | & ;", "Có (yes)", "2026年3月31日"];
        var exported = await service.ExportAsync(new MemoryStream(Encoding.UTF8.GetBytes(source)), translations);
        Assert.Empty(exported.Errors);
        var reimported = await service.ImportAsync(new MemoryStream(exported.Content!));
        Assert.Empty(reimported.Errors);
        Assert.Equal(translations, reimported.Texts);
        Assert.Contains("#34;quoted#34;", Encoding.UTF8.GetString(exported.Content!));
    }

    /// <summary>
    /// Checks fenced container offsets, comments, config and unrelated code stay intact.
    /// </summary>
    /// <returns>Task completing after protected-source assertions.</returns>
    [Fact]
    public async Task ContainersAndOtherCode_PreserveSource()
    {
        const string source = "Before\n\n> ```mermaid\n> flowchart LR\n> A[One] --> B(Two)\n> %% C[Comment]\n> style A fill:#fff\n> click A \"https://example.test/X[URL]\"\n> ```\n\n```js\nA[Not a label]\n```\n\n```mermaid\ngantt\ndateFormat YYYY-MM-DD\nsection Section\nTask: a1, 2024-01-01, 30d\n```\n\nAfter";
        var bytes = Encoding.UTF8.GetBytes(source);
        var service = MarkdownService.Create(Options.Create(new FileHandlingOptions()));
        var imported = await service.ImportAsync(new MemoryStream(bytes));
        Assert.Empty(imported.Errors);
        Assert.Equal(new[] { "Before", "One", "Two", "After" }, imported.Texts);
        var exported = await service.ExportAsync(new MemoryStream(bytes), ["Before", "Một", "Hai", "After"]);
        Assert.Empty(exported.Errors);
        Assert.Equal(source.Replace("A[One] --> B(Two)", "A[\"Một\"] --> B(\"Hai\")"), Encoding.UTF8.GetString(exported.Content!));
    }

    /// <summary>
    /// Checks shape wrappers and alternate edge labels are preserved.
    /// </summary>
    /// <returns>Task completing after ordered-label assertions.</returns>
    [Fact]
    public async Task ClassicShapesAndEdges_KeepSyntax()
    {
        const string source = "```mermaid\nflowchart TD; A[[One]] -- yes --> B{Two}; B == no ==> C((Three))\n```";
        var service = MarkdownService.Create(Options.Create(new FileHandlingOptions()));
        var bytes = Encoding.UTF8.GetBytes(source);
        var imported = await service.ImportAsync(new MemoryStream(bytes));
        Assert.Empty(imported.Errors);
        Assert.Equal(new[] { "One", "yes", "Two", "no", "Three" }, imported.Texts);
        var output = await service.ExportAsync(new MemoryStream(bytes), ["Một", "Có", "Hai", "Không", "Ba"]);
        Assert.Empty(output.Errors);
        Assert.Contains("A[[\"Một\"]] -- \"Có\" --> B{\"Hai\"}; B == \"Không\" ==> C((\"Ba\"))", Encoding.UTF8.GetString(output.Content!));
    }

    /// <summary>
    /// Checks label errors retain translation index and source line.
    /// </summary>
    /// <param name="translation">Invalid label translation.</param>
    /// <param name="code">Expected error code.</param>
    /// <returns>Task completing after source preservation and located skip assertions.</returns>
    [Theory]
    [InlineData("", "empty_translation")]
    [InlineData("A\nB", "invalid_structure")]
    public async Task InvalidLabels_PreserveSourceWithLocatedSkip(string translation, string code)
    {
        var service = MarkdownService.Create(Options.Create(new FileHandlingOptions()));
        var result = await service.ExportAsync(new MemoryStream(Encoding.UTF8.GetBytes("```mermaid\nflowchart LR\nA[Label]\n```")), [translation]);
        Assert.Equal("```mermaid\nflowchart LR\nA[Label]\n```", Encoding.UTF8.GetString(result.Content!));
        Assert.Empty(result.Errors);
        var error = Assert.Single(result.Metadata.Skipped, s => s.Stage == "translation");
        Assert.Equal(code, error.Code);
        Assert.Equal(0, error.UnitIndex);
        Assert.Equal(new SourceLineRange(3, 3), error.Location.Line);
    }

    /// <summary>
    /// Checks flowchart labels participate in existing extraction quotas.
    /// </summary>
    /// <returns>Task completing after quota assertions.</returns>
    [Fact]
    public async Task LabelUnits_RespectUnitLimit()
    {
        var service = MarkdownService.Create(Options.Create(new FileHandlingOptions { MaxUnits = 2 }));
        var imported = await service.ImportAsync(new MemoryStream(Encoding.UTF8.GetBytes(Diagram)));
        Assert.Empty(imported.Texts);
        Assert.Equal("too_many_units", Assert.Single(imported.Errors).Code);
    }

    /// <summary>
    /// Checks unlabelled arrows cannot be mistaken for inline labels at a suffix offset.
    /// </summary>
    /// <returns>Task completing after graph syntax assertions.</returns>
    [Fact]
    public async Task CompactAndUnlabelledEdges_DoNotConsumeNodeLabels()
    {
        const string source = "```mermaid\nflowchart LR\nA---B[One]-->C[Two]\nC===D[Three]-.->E[Four]\n```";
        var service = MarkdownService.Create(Options.Create(new FileHandlingOptions()));
        var imported = await service.ImportAsync(new MemoryStream(Encoding.UTF8.GetBytes(source)));
        Assert.Empty(imported.Errors);
        Assert.Equal(new[] { "One", "Two", "Three", "Four" }, imported.Texts);
        var output = await service.ExportAsync(new MemoryStream(Encoding.UTF8.GetBytes(source)), ["Một", "Hai", "Ba", "Bốn"]);
        Assert.Empty(output.Errors);
        Assert.Equal(source.Replace("[One]", "[\"Một\"]").Replace("[Two]", "[\"Hai\"]").Replace("[Three]", "[\"Ba\"]").Replace("[Four]", "[\"Bốn\"]"), Encoding.UTF8.GetString(output.Content!));
    }

    /// <summary>
    /// Checks flowchart subgraphs and lowercase orientation declarations extract titles and round-trip.
    /// </summary>
    /// <returns>Task completing after subgraph extraction assertions.</returns>
    [Fact]
    public async Task FlowchartSubgraphsAndLowercaseDirection_ExtractsTitles()
    {
        const string source = """
            ```mermaid
            flowchart td
                subgraph Container [Main Group]
                    A[Node One] --> B[Node Two]
                end
                subgraph Secondary Title
                    C[Node Three]
                end
            ```
            """;
        var bytes = Encoding.UTF8.GetBytes(source);
        var service = MarkdownService.Create(Options.Create(new FileHandlingOptions()));
        var imported = await service.ImportAsync(new MemoryStream(bytes));
        Assert.Empty(imported.Errors);
        Assert.Equal(new[] { "Main Group", "Node One", "Node Two", "Secondary Title", "Node Three" }, imported.Texts);

        var identity = await service.ExportAsync(new MemoryStream(bytes), imported.Texts);
        Assert.Empty(identity.Errors);
        Assert.Equal(bytes, identity.Content);

        var translated = imported.Texts.Select(t => t + " Đã Dịch").ToArray();
        var exported = await service.ExportAsync(new MemoryStream(bytes), translated);
        Assert.Empty(exported.Errors);

        var reimported = await service.ImportAsync(new MemoryStream(exported.Content!));
        Assert.Empty(reimported.Errors);
        Assert.Equal(translated, reimported.Texts);
    }
}
