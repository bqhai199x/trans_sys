using System.Text;
using FileHandler.Api.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace FileHandler.Tests.Diagnostics;

/// <summary>
/// Unit tests for diagnostic trace value formatting, snapshotting, and truncation.
/// </summary>
public sealed class TraceValueTests
{

    /// <summary>
    /// Formats trace value for assertions.
    /// </summary>
    /// <param name="value">Value to process.</param>
    /// <returns>Formatted trace text.</returns>
    private static string Format(object? value) =>
        TraceValue.Format(value, new() { MaxValueLength = 20000 });

    /// <summary>
    /// Verifies primitive values and materialized collections are formatted as JSON.
    /// </summary>
    /// <returns>No return value.</returns>
    [Fact]
    public void Format_SerializesValuesAndMaterializedCollections()
    {
        Assert.Equal("null", Format(null));
        Assert.Equal("42", Format(42));
        Assert.Equal("Friday", Format(DayOfWeek.Friday));
        Assert.Equal("\"hello\"", Format(new StringBuilder("hello")));
        Assert.Equal("{\"cancelled\":true}", Format(new CancellationToken(true)));
        Assert.Equal("[1,2]", Format(new[] { 1, 2 }));
        Assert.Equal("{\"key\":\"value\"}", Format(new Dictionary<string, string> { ["key"] = "value" }));
        Assert.Equal("[1,\"two\"]", Format((1, "two")));
        Assert.Equal("{\"name\":\"value\"}", Format(new { Name = "value" }));
    }

    /// <summary>
    /// Verifies snapshots avoid stream reads and lazy sequence evaluation.
    /// </summary>
    /// <returns>No return value.</returns>
    [Fact]
    public void Format_DescribesStreamsWithoutReadingThemAndDefersLazySequences()
    {
        using var stream = new MemoryStream([65, 66]);
        Assert.Equal("Stream", Format(stream));
        Assert.Equal(0, stream.Position);
        var evaluated = false;
        var lazy = Enumerable.Range(0, 3).Select(x => { evaluated = true; return x; });
        Assert.Contains("[Deferred]", Format(lazy));
        Assert.False(evaluated);
    }

    /// <summary>
    /// Verifies HTTP responses and byte content are summarized.
    /// </summary>
    /// <returns>No return value.</returns>
    [Fact]
    public void Format_SummarizesHttpResultsAndBytes()
    {
        var content = Format(new FileContentResult([65], "text/plain") { FileDownloadName = "a.txt" });
        Assert.Contains("\"fileDownloadName\":\"a.txt\"", content);
        Assert.Contains("\"text\":\"A\"", content);
        Assert.Equal("{\"status\":400,\"body\":\"bad\"}", Format(new BadRequestObjectResult("bad")));
        Assert.Equal("{\"status\":204}", Format(new StatusCodeResult(204)));
    }

    /// <summary>
    /// Verifies snapshot length, depth, and collection limits.
    /// </summary>
    /// <returns>No return value.</returns>
    [Fact]
    public void Format_BoundsLengthDepthAndCollectionSize()
    {
        Assert.EndsWith(" [Truncated]", TraceValue.Format(new string('x', 100), new() { MaxValueLength = 20 }));
        var array = Format(Enumerable.Range(0, 30).ToArray());
        Assert.Contains("[Truncated]", array);
        var dictionary = Enumerable.Range(0, 30).ToDictionary(x => x.ToString(), x => x);
        Assert.Contains("[Truncated]", Format(dictionary));
        var cycle = new List<object>();
        cycle.Add(cycle);
        Assert.Contains("[Truncated]", TraceValue.Format(cycle, new()));
    }

    /// <summary>
    /// Verifies hidden content and snapshot failure handling.
    /// </summary>
    /// <returns>No return value.</returns>
    [Fact]
    public void Format_HidesContentAndContainsSnapshotFailures()
    {
        Assert.Equal("[Hidden]", TraceValue.Format("secret", new() { CaptureContent = false }));
        Assert.Equal("[Unavailable]", TraceValue.Format(double.NaN, new()));
    }

    /// <summary>
    /// Verifies structured objects are captured as typed dictionaries with camelCase properties.
    /// </summary>
    /// <returns>No return value.</returns>
    [Fact]
    public void Capture_ExtractsTypedDictionariesWithCamelCase()
    {
        var result = Assert.IsType<Dictionary<string, object?>>(TraceValue.Capture(new { FullTitle = "Hello" }, new()));
        Assert.Equal("Hello", result["fullTitle"]);
    }
}
