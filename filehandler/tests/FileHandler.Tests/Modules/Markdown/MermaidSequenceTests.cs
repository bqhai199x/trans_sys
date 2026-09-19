using System.Text;
using FileHandler.Api.Common;
using FileHandler.Api.Modules.Markdown;
using Microsoft.Extensions.Options;

namespace FileHandler.Tests.Modules.Markdown;

/// <summary>
/// Verifies sequence diagram label extraction, special character escaping, and source-preserving export.
/// </summary>
public sealed class MermaidSequenceTests
{

    /// <summary>
    /// User sequence diagram containing participants, message arrows, and conditional branches.
    /// </summary>
    private const string UserDiagram = """
        ```mermaid
        sequenceDiagram
            participant U as Người dùng
            participant P as Pipeline Orchestration
            participant N as Query Understanding
            participant V as Vector Database
            participant G as Knowledge Graph
            participant L as LLM Reasoning
            participant F as Result Persistence

            U->>P: Query
            P->>P: Validate model configuration
            P->>N: Raw query
            N-->>P: Query Object (retrieval plan)
            P->>V: Embedding + VectorDB target
            V-->>P: Ranked candidates
            alt Có candidates
                P->>G: Candidates + GraphDB target
                G-->>P: Normalized paths + enriched paths
            else Không tìm được đối tượng hoặc lỗi tìm kiếm
                P->>P: Use empty graph context
            end
            P->>L: Query + normalized paths
            L-->>P: Answer + reasoning + evidence + confidence
            P->>F: Query Pipeline Result JSON
            F-->>P: Persistence confirmation
            P-->>U: Query Pipeline Result
        ```
        """;

    /// <summary>
    /// Checks all 23 labels in user sequence diagram are extracted and identity export preserves original bytes.
    /// </summary>
    /// <param name="newline">Source line ending sequence.</param>
    /// <returns>Task completing after import and round-trip assertions.</returns>
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task UserSequenceDiagram_ExtractsAllLabelsAndRoundTrips(string newline)
    {
        var source = UserDiagram.ReplaceLineEndings(newline);
        var bytes = Encoding.UTF8.GetBytes(source);
        var service = MarkdownService.Create(Options.Create(new FileHandlingOptions()));
        var imported = await service.ImportAsync(new MemoryStream(bytes));
        Assert.Empty(imported.Errors);
        string[] expected =
        [
            "Người dùng",
            "Pipeline Orchestration",
            "Query Understanding",
            "Vector Database",
            "Knowledge Graph",
            "LLM Reasoning",
            "Result Persistence",
            "Query",
            "Validate model configuration",
            "Raw query",
            "Query Object (retrieval plan)",
            "Embedding + VectorDB target",
            "Ranked candidates",
            "Có candidates",
            "Candidates + GraphDB target",
            "Normalized paths + enriched paths",
            "Không tìm được đối tượng hoặc lỗi tìm kiếm",
            "Use empty graph context",
            "Query + normalized paths",
            "Answer + reasoning + evidence + confidence",
            "Query Pipeline Result JSON",
            "Persistence confirmation",
            "Query Pipeline Result"
        ];
        Assert.Equal(expected, imported.Texts);

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
        Assert.Contains("participant U as Bản dịch 0", result);
        Assert.Contains("U->>P: Bản dịch 7", result);
        Assert.Contains("alt Bản dịch 13", result);
        Assert.Contains("else Bản dịch 16", result);
        Assert.DoesNotContain("U->>P: \"Bản dịch 7\"", result);
        Assert.DoesNotContain("alt \"Bản dịch 13\"", result);
    }

    /// <summary>
    /// Checks reserved characters in sequence diagram translations are entity-encoded and round-trip successfully.
    /// </summary>
    /// <returns>Task completing after entity encoding assertions.</returns>
    [Fact]
    public async Task SpecialCharacters_AreEntityEncodedForSequenceDiagram()
    {
        const string source = """
            ```mermaid
            sequenceDiagram
                participant A as Client
                participant B as Server
                A->>B: Request
            ```
            """;
        var service = MarkdownService.Create(Options.Create(new FileHandlingOptions()));
        string[] translations =
        [
            "Khách #1; chính",
            "Máy chủ \"API\" <prod>",
            "Yêu cầu; thử lại #2 > kết quả"
        ];
        var bytes = Encoding.UTF8.GetBytes(source);
        var exported = await service.ExportAsync(new MemoryStream(bytes), translations);
        Assert.Empty(exported.Errors);

        var result = Encoding.UTF8.GetString(exported.Content!);
        Assert.Contains("participant A as Khách #35;1#59; chính", result);
        Assert.Contains("participant B as Máy chủ #34;API#34; #60;prod#62;", result);
        Assert.Contains("A->>B: Yêu cầu#59; thử lại #35;2 #62; kết quả", result);

        var reimported = await service.ImportAsync(new MemoryStream(exported.Content!));
        Assert.Empty(reimported.Errors);
        Assert.Equal(translations, reimported.Texts);
    }

    /// <summary>
    /// Checks sequence actors, notes, control blocks, grouping boxes, links, and inline comments are handled.
    /// </summary>
    /// <returns>Task completing after rich sequence syntax assertions.</returns>
    [Fact]
    public async Task RichSequenceSyntax_ExtractsAndPreservesStructure()
    {
        const string source = """
            ```mermaid
            sequenceDiagram
                title Auth Flow
                autonumber
                box "Identity Group"
                    actor U as User Stick
                    create participant A as Auth Service
                end
                U->>+A: Login %% inline comment
                Note over U,A: Shared credentials
                loop Every minute
                    A->>A: Heartbeat
                end
                critical High Security
                    A->>U: Token
                option Fallback
                    A->>U: Error
                end
                destroy A
                link U: Dashboard @ https://example.test
            ```
            """;
        var bytes = Encoding.UTF8.GetBytes(source);
        var service = MarkdownService.Create(Options.Create(new FileHandlingOptions()));
        var imported = await service.ImportAsync(new MemoryStream(bytes));
        Assert.Empty(imported.Errors);
        string[] expected =
        [
            "Auth Flow",
            "Identity Group",
            "User Stick",
            "Auth Service",
            "Login",
            "Shared credentials",
            "Every minute",
            "Heartbeat",
            "High Security",
            "Token",
            "Fallback",
            "Error",
            "Dashboard"
        ];
        Assert.Equal(expected, imported.Texts);

        var identity = await service.ExportAsync(new MemoryStream(bytes), imported.Texts);
        Assert.Empty(identity.Errors);
        Assert.Equal(bytes, identity.Content);

        var translations = imported.Texts.Select(t => t + " Dịch").ToArray();
        var exported = await service.ExportAsync(new MemoryStream(bytes), translations);
        Assert.Empty(exported.Errors);

        var result = Encoding.UTF8.GetString(exported.Content!);
        Assert.Contains("Login Dịch %% inline comment", result);
        Assert.Contains("destroy A", result);
        Assert.Contains("autonumber", result);
        Assert.Contains("box \"Identity Group Dịch\"", result);
        Assert.Contains("link U: Dashboard Dịch @ https://example.test", result);
    }

    /// <summary>
    /// Checks state diagram transitions and class diagram notes extract and round-trip.
    /// </summary>
    /// <returns>Task completing after state and class diagram assertions.</returns>
    [Fact]
    public async Task StateAndClassDiagrams_ExtractLabels()
    {
        const string stateSource = """
            ```mermaid
            stateDiagram-v2
                state "Initial Stage" as s1
                s1 --> s2 : Process Event
                note right of s1 : Note details
            ```
            """;
        var service = MarkdownService.Create(Options.Create(new FileHandlingOptions()));
        var importedState = await service.ImportAsync(new MemoryStream(Encoding.UTF8.GetBytes(stateSource)));
        Assert.Empty(importedState.Errors);
        Assert.Equal(new[] { "Initial Stage", "Process Event", "Note details" }, importedState.Texts);

        var exportedState = await service.ExportAsync(new MemoryStream(Encoding.UTF8.GetBytes(stateSource)), ["Giai đoạn đầu", "Xử lý sự kiện", "Chi tiết ghi chú"]);
        Assert.Empty(exportedState.Errors);
        var reimportedState = await service.ImportAsync(new MemoryStream(exportedState.Content!));
        Assert.Empty(reimportedState.Errors);
        Assert.Equal(new[] { "Giai đoạn đầu", "Xử lý sự kiện", "Chi tiết ghi chú" }, reimportedState.Texts);

        const string classSource = """
            ```mermaid
            classDiagram
                class OrderService
                note for OrderService "Handles payments"
            ```
            """;
        var importedClass = await service.ImportAsync(new MemoryStream(Encoding.UTF8.GetBytes(classSource)));
        Assert.Empty(importedClass.Errors);
        Assert.Equal(new[] { "Handles payments" }, importedClass.Texts);
    }

    /// <summary>
    /// Checks combined flowchart and sequence diagram document extracts labels and round-trips without errors.
    /// </summary>
    /// <returns>Task completing after import and export assertions.</returns>
    [Fact]
    public async Task CombinedFlowchartAndSequenceDiagram_ExtractsAndRoundTrips()
    {
        const string markdown = """
            # Architecture Overview

            ```mermaid
            flowchart TD
                A["Client Request"] --> B{"Validation"}
                B -->|Valid| C["Process Order"]
                B -->|Invalid| D["Error Handler"]
            ```

            ## Workflow Sequence

            ```mermaid
            sequenceDiagram
                Client->>Server: Submit Request
                Server-->>Database: Query Records
                Database-->>Server: Return Data
                Server-->>Client: Response Payload
            ```
            """;
        var bytes = Encoding.UTF8.GetBytes(markdown);
        var service = MarkdownService.Create(Options.Create(new FileHandlingOptions()));
        var imported = await service.ImportAsync(new MemoryStream(bytes));
        Assert.Empty(imported.Errors);

        Assert.Contains("Client Request", imported.Texts);
        Assert.Contains("Validation", imported.Texts);
        Assert.Contains("Process Order", imported.Texts);
        Assert.Contains("Error Handler", imported.Texts);
        Assert.Contains("Submit Request", imported.Texts);
        Assert.Contains("Query Records", imported.Texts);
        Assert.Contains("Return Data", imported.Texts);
        Assert.Contains("Response Payload", imported.Texts);

        var identity = await service.ExportAsync(new MemoryStream(bytes), imported.Texts);
        Assert.Empty(identity.Errors);
        Assert.Equal(bytes, identity.Content);
    }
}
