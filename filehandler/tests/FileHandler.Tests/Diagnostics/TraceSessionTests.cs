using System.Text.Json;
using FileHandler.Api.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace FileHandler.Tests.Diagnostics;

/// <summary>
/// Unit tests for trace session document building, limits, and file serialization.
/// </summary>
public sealed class TraceSessionTests : IDisposable
{

    /// <summary>
    /// Verifies trace overflow produces valid bounded JSON with a visible truncation reason.
    /// </summary>
    /// <returns>No return value.</returns>
    [Fact]
    public void Flush_EnforcesSerializedByteLimit()
    {
        using (var session = new TraceSession(_path, new() { MaxTraceBytes = 4096 }, NullLogger.Instance))
        {
            session.Document.FileName = new string('x', 10000);
            session.WriteResult(200, 1);
        }
        Assert.True(new FileInfo(_path).Length <= 4096);
        using var json = JsonDocument.Parse(File.ReadAllText(_path));
        Assert.Equal(200, json.RootElement.GetProperty("status").GetInt32());
        Assert.Contains("MaxTraceBytes", json.RootElement.GetProperty("error").GetString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Temporary trace file path for this test fixture.
    /// </summary>
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"trace-unit-{Guid.NewGuid():N}.json");

    /// <summary>
    /// Deletes trace files created by this test fixture.
    /// </summary>
    /// <returns>No return value.</returns>
    public void Dispose() => File.Delete(_path);

    /// <summary>
    /// Verifies call IDs are incremented sequentially.
    /// </summary>
    /// <returns>No return value.</returns>
    [Fact]
    public void NextCall_GeneratesSequentialUniqueCallIds()
    {
        using var session = new TraceSession(_path, new(), NullLogger.Instance);
        Assert.Equal(1, session.NextCall());
        Assert.Equal(2, session.NextCall());
        Assert.Equal(3, session.NextCall());
    }

    /// <summary>
    /// Verifies trace value factory errors are contained.
    /// </summary>
    /// <returns>No return value.</returns>
    [Fact]
    public void Value_ContainsFactoryErrors()
    {
        using var session = new TraceSession(_path, new(), NullLogger.Instance);
        Assert.Equal("[Unavailable]", session.Value(() => throw new InvalidOperationException()));
        Assert.Equal("42", session.Value(() => 42));
    }

    /// <summary>
    /// Verifies hidden values skip factory evaluation.
    /// </summary>
    /// <returns>No return value.</returns>
    [Fact]
    public void Value_WhenHiddenDoesNotEvaluateFactory()
    {
        using var session = new TraceSession(_path, new() { CaptureContent = false }, NullLogger.Instance);
        Assert.Equal("[Hidden]", session.Value(() => throw new InvalidOperationException()));
    }

    /// <summary>
    /// Verifies event limits stop detail events while result summary is still written.
    /// </summary>
    /// <returns>No return value.</returns>
    [Fact]
    public void Write_StopsAtLimitAndResultIsPreserved()
    {
        var session = new TraceSession(_path, new() { MaxEvents = 1 }, NullLogger.Instance);
        session.WriteLine("first");
        Assert.False(session.Accepting);
        Assert.Equal("[Truncated]", session.Value(() => throw new InvalidOperationException()));
        session.WriteLine("second");
        session.WriteResult(200, 5.0);
        session.Dispose();
        session.Dispose();
        var log = File.ReadAllText(_path);
        using var doc = JsonDocument.Parse(log);
        Assert.Equal(200, doc.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("[Truncated - MaxEvents reached]", doc.RootElement.GetProperty("error").GetString());
    }

    /// <summary>
    /// Verifies each call records one completion, duration, and restores its parent.
    /// </summary>
    /// <param name="kind">Trace completion scenario to test.</param>
    /// <returns>No return value.</returns>
    [Theory]
    [InlineData("return")]
    [InlineData("void")]
    [InlineData("error")]
    [InlineData("cancel")]
    public void TraceCall_RecordsOneCompletionAndRestoresParent(string kind)
    {
        var previous = DebugTrace.Current;
        try
        {
            using (var session = new TraceSession(_path, new(), NullLogger.Instance))
            using (var root = new TraceCall(session, null, "API", "Request"))
            {
                DebugTrace.Current = root;
                using (var child = DebugTrace.Enter("Service", "Work", () => "input"))
                {
                    Assert.Same(child, DebugTrace.Current);
                    child.State("Count", () => 2);
                    if (kind == "return") Assert.Equal(42, child.Return(42));
                    if (kind == "error") child.Error(new InvalidOperationException("failure"));
                    if (kind == "cancel") child.Error(new OperationCanceledException());
                }
                Assert.Same(root, DebugTrace.Current);
            }
            var log = File.ReadAllText(_path);
            using var doc = JsonDocument.Parse(log);
            var rootCall = doc.RootElement.GetProperty("calls")[0];
            var childCall = rootCall.GetProperty("children")[0];
            Assert.Equal("Service.Work", childCall.GetProperty("target").GetString());
            Assert.Equal("Count", childCall.GetProperty("states")[0].GetProperty("name").GetString());
            if (kind == "return") Assert.Equal(42, childCall.GetProperty("out").GetInt32());
            if (kind == "void") Assert.Equal("void", childCall.GetProperty("out").GetString());
            if (kind == "error") Assert.NotEqual(JsonValueKind.Null, childCall.GetProperty("error").ValueKind);
            if (kind == "cancel") Assert.NotEqual(JsonValueKind.Null, childCall.GetProperty("error").ValueKind);
        }
        finally { DebugTrace.Current = previous; }
    }

    /// <summary>
    /// Verifies item scopes group child calls and restore enclosing scope on disposal.
    /// </summary>
    /// <returns>No return value.</returns>
    [Fact]
    public void TraceItem_NestsChildCallsAndRestoresParent()
    {
        var previous = DebugTrace.Current;
        try
        {
            using (var session = new TraceSession(_path, new(), NullLogger.Instance))
            using (var root = new TraceCall(session, null, "Applier", "Apply"))
            {
                DebugTrace.Current = root;
                using (var item = DebugTrace.Item(1))
                {
                    Assert.Same(item, DebugTrace.Current);
                    item.State("Key", () => "val");
                    using var child = DebugTrace.Enter("Codec", "Decode", () => "arg");
                    child.Return("done");
                }
                Assert.Same(root, DebugTrace.Current);
            }
            var log = File.ReadAllText(_path);
            using var doc = JsonDocument.Parse(log);
            var rootCall = doc.RootElement.GetProperty("calls")[0];
            var itemNode = rootCall.GetProperty("children")[0];
            Assert.Equal("item", itemNode.GetProperty("type").GetString());
            Assert.Equal(1, itemNode.GetProperty("index").GetInt32());
            Assert.Equal("Key", itemNode.GetProperty("states")[0].GetProperty("name").GetString());
            var childCall = itemNode.GetProperty("children")[0];
            Assert.Equal("Codec.Decode", childCall.GetProperty("target").GetString());
            Assert.Equal("done", childCall.GetProperty("out").GetString());
        }
        finally { DebugTrace.Current = previous; }
    }

    /// <summary>
    /// Verifies absent sessions use no-op calls and preserve return values.
    /// </summary>
    /// <returns>No return value.</returns>
    [Fact]
    public void Enter_WithoutSessionReturnsNoOpAndPreservesReturnValue()
    {
        var previous = DebugTrace.Current;
        try
        {
            DebugTrace.Current = null;
            using var call = DebugTrace.Enter("Owner", "Method", () => throw new Exception());
            call.State("Ignored", () => throw new Exception());
            using var item = DebugTrace.Item(1);
            item.State("Ignored", () => throw new Exception());
            var value = new object();
            Assert.Same(value, call.Return(value));
            call.Error(new Exception());
            Assert.Null(DebugTrace.Current);
        }
        finally { DebugTrace.Current = previous; }
    }
}
