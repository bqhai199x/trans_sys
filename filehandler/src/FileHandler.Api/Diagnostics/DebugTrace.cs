using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FileHandler.Api.Diagnostics;

/// <summary>
/// Ambient diagnostic tracing entry point and scope coordinator.
/// </summary>
internal static class DebugTrace
{

    /// <summary>
    /// Active trace scope for each asynchronous flow.
    /// </summary>
    private static readonly AsyncLocal<TraceScope?> Active = new();

    /// <summary>
    /// Optional runtime override for debug trace enablement.
    /// </summary>
    public static bool? EnabledOverride { get; set; }

    /// <summary>
    /// Active scope for current asynchronous flow.
    /// </summary>
    public static TraceScope? Current { get => Active.Value; set => Active.Value = value; }

    /// <summary>
    /// Evaluates whether tracing is currently enabled considering overrides.
    /// </summary>
    /// <param name="options">Configured debug trace options.</param>
    /// <returns>True when tracing is enabled.</returns>
    public static bool IsEnabled(DebugTraceOptions options) => EnabledOverride ?? options.Enabled;

    /// <summary>
    /// Starts nested trace call when current session accepts events.
    /// </summary>
    /// <param name="owner">Component name shown in trace list.</param>
    /// <param name="method">Traced method name.</param>
    /// <param name="input">Factory for captured input values.</param>
    /// <returns>Nested trace call, or no-op call when tracing is unavailable.</returns>
    public static TraceCall Enter(string owner, string method, Func<object?> input)
    {
        var parent = Current;
        if (parent is null || !parent.Session.Accepting) return TraceCall.Empty;
        var call = new TraceCall(parent.Session, parent, owner, method);
        Current = call;
        call.Input(input);
        return call;
    }

    /// <summary>
    /// Executes synchronous operation within a tracked trace call with automatic error capturing.
    /// </summary>
    /// <typeparam name="T">Result type.</typeparam>
    /// <param name="owner">Component name shown in trace list.</param>
    /// <param name="method">Traced method name.</param>
    /// <param name="input">Factory for captured input values.</param>
    /// <param name="action">Action receiving active trace call.</param>
    /// <returns>Result of traced operation.</returns>
    public static T Trace<T>(string owner, string method, Func<object?> input, Func<TraceCall, T> action)
    {
        using var trace = Enter(owner, method, input);
        try
        {
            var result = action(trace);
            return trace.Return(result);
        }
        catch (Exception ex)
        {
            trace.Error(ex);
            throw;
        }
    }

    /// <summary>
    /// Executes asynchronous operation within a tracked trace call with automatic error capturing.
    /// </summary>
    /// <typeparam name="T">Result type.</typeparam>
    /// <param name="owner">Component name shown in trace list.</param>
    /// <param name="method">Traced method name.</param>
    /// <param name="input">Factory for captured input values.</param>
    /// <param name="action">Asynchronous action receiving active trace call.</param>
    /// <returns>Task containing result of traced operation.</returns>
    public static async Task<T> TraceAsync<T>(string owner, string method, Func<object?> input, Func<TraceCall, Task<T>> action)
    {
        using var trace = Enter(owner, method, input);
        try
        {
            var result = await action(trace);
            return trace.Return(result);
        }
        catch (Exception ex)
        {
            trace.Error(ex);
            throw;
        }
    }

    /// <summary>
    /// Starts business loop item scope when current session accepts events.
    /// </summary>
    /// <param name="index">One-based iteration index.</param>
    /// <returns>Loop item scope, or no-op scope when tracing is unavailable.</returns>
    public static TraceItem Item(int index)
    {
        var parent = Current;
        if (parent is null || !parent.Session.Accepting) return TraceItem.Empty;
        var item = new TraceItem(parent.Session, parent, index);
        Current = item;
        return item;
    }
}

/// <summary>
/// Abstract diagnostic tracing scope associated with an active trace session.
/// </summary>
internal abstract class TraceScope : IDisposable
{

    /// <summary>
    /// Session that owns this trace scope.
    /// </summary>
    internal TraceSession Session { get; }

    /// <summary>
    /// Enclosing trace scope, if any.
    /// </summary>
    internal TraceScope? Parent { get; }

    /// <summary>
    /// Trace node associated with this scope in document tree.
    /// </summary>
    internal TraceNode Node { get; set; }

    /// <summary>
    /// Nesting level for this scope.
    /// </summary>
    internal int Level { get; }

    /// <summary>
    /// Indentation level for nested events and child scopes.
    /// </summary>
    internal int InnerLevel => Level + 1;

    /// <summary>
    /// Creates trace scope with session, parent, and level.
    /// </summary>
    /// <param name="session">Session that owns this scope.</param>
    /// <param name="parent">Enclosing trace scope, if any.</param>
    /// <param name="level">Nesting level for this scope.</param>
    protected TraceScope(TraceSession session, TraceScope? parent, int level)
    {
        Session = session;
        Parent = parent;
        Level = level;
        Node = new TraceNode();
    }

    /// <summary>
    /// Records named state snapshot for current scope.
    /// </summary>
    /// <param name="name">State value name.</param>
    /// <param name="value">Factory for captured trace value.</param>
    /// <returns>No return value.</returns>
    public abstract void State(string name, Func<object?> value);

    /// <summary>
    /// Records unnamed state snapshot for current scope.
    /// </summary>
    /// <param name="value">Factory for captured trace value.</param>
    /// <returns>No return value.</returns>
    public void State(Func<object?> value) => State(string.Empty, value);

    /// <summary>
    /// Completes scope and restores parent scope.
    /// </summary>
    /// <returns>No return value.</returns>
    public abstract void Dispose();
}

/// <summary>
/// Traced method invocation scope capturing inputs, outputs, errors, and intermediate states.
/// </summary>
internal sealed class TraceCall : TraceScope
{

    /// <summary>
    /// Shared no-op trace call.
    /// </summary>
    internal static readonly TraceCall Empty = new();

    /// <summary>
    /// Stopwatch timestamp captured when this call starts.
    /// </summary>
    private readonly long _started;

    /// <summary>
    /// Whether this call has recorded its completion.
    /// </summary>
    private bool _finished;

    /// <summary>
    /// Unique call ID within this session.
    /// </summary>
    private readonly int _id;

    /// <summary>
    /// Creates no-op trace call.
    /// </summary>
    private TraceCall() : base(null!, null, 0) { }

    /// <summary>
    /// Creates trace call linked to its session and parent.
    /// </summary>
    /// <param name="session">Session that owns this trace call.</param>
    /// <param name="parent">Enclosing trace scope, if any.</param>
    /// <param name="owner">Component name shown in trace list.</param>
    /// <param name="method">Traced method name.</param>
    internal TraceCall(TraceSession session, TraceScope? parent, string owner, string method)
        : base(session, parent, parent?.InnerLevel ?? 0)
    {
        _id = session.NextCall();
        _started = Stopwatch.GetTimestamp();
        Node = session.CreateCallNode(parent?.Node, _id, owner, method);
    }

    /// <summary>
    /// Records call input values.
    /// </summary>
    /// <param name="input">Factory for captured input values.</param>
    /// <returns>No return value.</returns>
    internal void Input(Func<object?> input)
    {
        if (this == Empty) return;
        Session.SetInput(Node, input);
    }

    /// <summary>
    /// Records named state value for current call.
    /// </summary>
    /// <param name="name">State value name.</param>
    /// <param name="value">Factory for captured trace value.</param>
    /// <returns>No return value.</returns>
    public override void State(string name, Func<object?> value)
    {
        if (this != Empty) Session.AddState(Node, name, value);
    }

    /// <summary>
    /// Records return value and completes call.
    /// </summary>
    /// <typeparam name="T">Return value type.</typeparam>
    /// <param name="value">Return value to record.</param>
    /// <returns>Supplied value, unchanged.</returns>
    public T Return<T>(T value)
    {
        if (this != Empty) Finish("Out", () => value);
        return value;
    }

    /// <summary>
    /// Completes call with error or cancellation result.
    /// </summary>
    /// <param name="error">Failure to record.</param>
    /// <returns>No return value.</returns>
    public void Error(Exception error)
    {
        if (this == Empty || _finished) return;
        _finished = true;
        var elapsed = Stopwatch.GetElapsedTime(_started).TotalMilliseconds;
        Session.SetError(Node, error, elapsed);
    }

    /// <summary>
    /// Writes call result and elapsed time once.
    /// </summary>
    /// <param name="label">Trace result label.</param>
    /// <param name="value">Factory for captured trace value.</param>
    /// <returns>No return value.</returns>
    private void Finish(string label, Func<object?> value)
    {
        if (this == Empty || _finished) return;
        _finished = true;
        var elapsed = Stopwatch.GetElapsedTime(_started).TotalMilliseconds;
        Session.SetOutput(Node, value, elapsed);
    }

    /// <summary>
    /// Completes tracing and releases current tracing scope.
    /// </summary>
    /// <returns>No return value.</returns>
    public override void Dispose()
    {
        if (this == Empty) return;
        Finish("Out", () => "void");
        DebugTrace.Current = Parent;
    }
}

/// <summary>
/// Traced loop iteration scope capturing per-item intermediate processing states.
/// </summary>
internal sealed class TraceItem : TraceScope
{

    /// <summary>
    /// Shared no-op trace item.
    /// </summary>
    internal static readonly TraceItem Empty = new();

    /// <summary>
    /// One-based loop item index.
    /// </summary>
    private readonly int _index;

    /// <summary>
    /// Creates no-op trace item.
    /// </summary>
    private TraceItem() : base(null!, null, 0) { }

    /// <summary>
    /// Creates loop item scope under parent scope.
    /// </summary>
    /// <param name="session">Session that owns this item.</param>
    /// <param name="parent">Enclosing trace scope.</param>
    /// <param name="index">One-based iteration index.</param>
    internal TraceItem(TraceSession session, TraceScope parent, int index)
        : base(session, parent, parent.InnerLevel)
    {
        _index = index;
        Node = session.CreateItemNode(parent.Node, _index);
    }

    /// <summary>
    /// Records named state value for current loop item.
    /// </summary>
    /// <param name="name">State value name.</param>
    /// <param name="value">Factory for captured trace value.</param>
    /// <returns>No return value.</returns>
    public override void State(string name, Func<object?> value)
    {
        if (this != Empty) Session.AddState(Node, name, value);
    }

    /// <summary>
    /// Restores parent scope on item completion.
    /// </summary>
    /// <returns>No return value.</returns>
    public override void Dispose()
    {
        if (this == Empty) return;
        DebugTrace.Current = Parent;
    }
}

/// <summary>
/// Active diagnostic trace session managing tree structure, event limits, and JSON persistence.
/// </summary>
internal sealed class TraceSession : IDisposable, IAsyncDisposable
{

    /// <summary>
    /// JSON serialization options for formatted trace document.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// File path where JSON trace is written.
    /// </summary>
    private readonly string _path;

    /// <summary>
    /// Trace capture settings and event limits.
    /// </summary>
    private readonly DebugTraceOptions _options;

    /// <summary>
    /// Logger for trace failures.
    /// </summary>
    private readonly ILogger _logger;

    /// <summary>
    /// Lock protecting trace session state and output.
    /// </summary>
    private readonly object _gate = new();

    /// <summary>
    /// Structured trace document representation.
    /// </summary>
    private readonly TraceDocument _document = new();

    /// <summary>
    /// Counter used to generate unique call IDs.
    /// </summary>
    private int _calls;

    /// <summary>
    /// Number of attempted trace events.
    /// </summary>
    private int _events;

    /// <summary>
    /// Conservative cumulative snapshot and event byte estimate.
    /// </summary>
    private long _capturedBytes;

    /// <summary>
    /// Whether trace output has failed.
    /// </summary>
    private bool _failed;

    /// <summary>
    /// Whether this trace session has been closed.
    /// </summary>
    private bool _closed;

    /// <summary>
    /// Whether request summary result has been written.
    /// </summary>
    private bool _resultWritten;

    /// <summary>
    /// Whether session can accept more trace events.
    /// </summary>
    internal bool Accepting
    {
        get
        {
            lock (_gate)
            {
                return !_failed && !_closed && _events < _options.MaxEvents && _capturedBytes < _options.MaxTraceBytes;
            }
        }
    }

    /// <summary>
    /// Gets in-memory trace document model.
    /// </summary>
    internal TraceDocument Document => _document;

    /// <summary>
    /// Creates JSON trace session and initializes document metadata.
    /// </summary>
    /// <param name="path">Destination trace file path.</param>
    /// <param name="options">Trace capture settings and limits.</param>
    /// <param name="logger">Logger for diagnostic failures.</param>
    internal TraceSession(string path, DebugTraceOptions options, ILogger logger)
    {
        _path = path;
        _options = options;
        _logger = logger;
        _document.Id = Path.GetFileNameWithoutExtension(path);
        _document.Timestamp = DateTime.UtcNow;
    }

    /// <summary>
    /// Returns next unique call ID within session.
    /// </summary>
    /// <returns>Unique numeric call ID.</returns>
    internal int NextCall() => Interlocked.Increment(ref _calls);

    /// <summary>
    /// Creates and registers call node in document tree.
    /// </summary>
    /// <param name="parentNode">Enclosing parent node, or null for root calls.</param>
    /// <param name="id">Unique call ID.</param>
    /// <param name="owner">Component name.</param>
    /// <param name="method">Method name.</param>
    /// <returns>Registered call node.</returns>
    internal TraceNode CreateCallNode(TraceNode? parentNode, int id, string owner, string method)
    {
        lock (_gate)
        {
            var target = string.IsNullOrEmpty(owner) ? method : $"{owner}.{method}";
            var node = new TraceNode
            {
                Type = "call",
                Id = id,
                Target = target
            };
            if (!RecordEvent()) return node;
            if (parentNode is not null)
                parentNode.Children.Add(node);
            else
                _document.Calls.Add(node);
            return node;
        }
    }

    /// <summary>
    /// Creates and registers iteration item node in document tree.
    /// </summary>
    /// <param name="parentNode">Enclosing call node.</param>
    /// <param name="index">One-based iteration index.</param>
    /// <returns>Registered item node.</returns>
    internal TraceNode CreateItemNode(TraceNode parentNode, int index)
    {
        lock (_gate)
        {
            var node = new TraceNode
            {
                Type = "item",
                Index = index
            };
            if (!RecordEvent()) return node;
            parentNode.Children.Add(node);
            return node;
        }
    }

    /// <summary>
    /// Sets input parameters on specified node.
    /// </summary>
    /// <param name="node">Target trace node.</param>
    /// <param name="input">Factory for captured input values.</param>
    /// <returns>No return value.</returns>
    internal void SetInput(TraceNode node, Func<object?> input)
    {
        lock (_gate)
        {
            if (!RecordEvent()) return;
            node.In = Capture(input);
            if (string.IsNullOrEmpty(_document.FileName) && node.In is IDictionary<string, object?> dict)
            {
                ExtractFileName(dict);
            }
        }
    }

    /// <summary>
    /// Attempts to extract client file name from captured request input structure.
    /// </summary>
    /// <param name="dict">Captured input dictionary.</param>
    /// <returns>No return value.</returns>
    private void ExtractFileName(IDictionary<string, object?> dict)
    {
        if (dict.TryGetValue("request", out var reqObj) && reqObj is IDictionary<string, object?> reqDict)
        {
            if (reqDict.TryGetValue("file", out var fileObj) && fileObj is IDictionary<string, object?> fileDict)
            {
                if (fileDict.TryGetValue("fileName", out var fn) && fn is string fnStr && !string.IsNullOrEmpty(fnStr))
                {
                    _document.FileName = fnStr;
                    return;
                }
            }
        }
        if (dict.TryGetValue("clientFileName", out var cfn) && cfn is string cfnStr && !string.IsNullOrEmpty(cfnStr))
        {
            _document.FileName = cfnStr;
        }
    }

    /// <summary>
    /// Appends intermediate state snapshot to specified node.
    /// </summary>
    /// <param name="node">Target trace node.</param>
    /// <param name="name">Optional state name.</param>
    /// <param name="value">Factory for captured state value.</param>
    /// <returns>No return value.</returns>
    internal void AddState(TraceNode node, string name, Func<object?> value)
    {
        lock (_gate)
        {
            if (!RecordEvent()) return;
            node.States.Add(new TraceStateEntry
            {
                Name = name,
                Value = Capture(value)
            });
        }
    }

    /// <summary>
    /// Sets return value and elapsed time on specified node.
    /// </summary>
    /// <param name="node">Target trace node.</param>
    /// <param name="output">Factory for captured output value.</param>
    /// <param name="elapsedMs">Elapsed time in milliseconds.</param>
    /// <returns>No return value.</returns>
    internal void SetOutput(TraceNode node, Func<object?> output, double elapsedMs)
    {
        lock (_gate)
        {
            if (!RecordEvent()) return;
            node.Out = Capture(output);
            node.DurationMs = Math.Round(elapsedMs, 3);
        }
    }

    /// <summary>
    /// Sets error details and elapsed time on specified node.
    /// </summary>
    /// <param name="node">Target trace node.</param>
    /// <param name="error">Failure to record.</param>
    /// <param name="elapsedMs">Elapsed time in milliseconds.</param>
    /// <returns>No return value.</returns>
    internal void SetError(TraceNode node, Exception error, double elapsedMs)
    {
        lock (_gate)
        {
            if (!RecordEvent()) return;
            if (_options.CaptureContent)
            {
                node.Error = Capture(() => new { Type = error.GetType().Name, error.Message });
            }
            else
            {
                node.Error = new { Type = error.GetType().Name, Message = "[Redacted]" };
            }
            node.DurationMs = Math.Round(elapsedMs, 3);
        }
    }

    /// <summary>
    /// Writes request summary result with status code, duration, and error.
    /// </summary>
    /// <param name="statusCode">HTTP response status code.</param>
    /// <param name="elapsedMs">Total request duration in milliseconds.</param>
    /// <param name="error">Optional unhandled exception.</param>
    /// <param name="isCancelled">Whether request was cancelled.</param>
    /// <returns>No return value.</returns>
    internal void WriteResult(int statusCode, double elapsedMs, Exception? error = null, bool isCancelled = false)
    {
        lock (_gate)
        {
            if (_resultWritten) return;
            _resultWritten = true;
            _document.Status = statusCode;
            _document.DurationMs = Math.Round(elapsedMs, 3);
            _document.Cancelled = isCancelled || error is OperationCanceledException;
            var msg = _options.CaptureContent ? error?.Message : "[Redacted]";
            if (_events >= _options.MaxEvents)
            {
                var prefix = error is not null ? $"{error.GetType().Name}: {msg} - " : "";
                _document.Error = $"{prefix}[Truncated - MaxEvents reached]";
            }
            else if (error is not null)
            {
                if (_options.CaptureContent)
                {
                    _document.Error = Capture(() => new { Type = error.GetType().Name, error.Message });
                }
                else
                {
                    _document.Error = new { Type = error.GetType().Name, Message = "[Redacted]" };
                }
            }
        }
    }

    /// <summary>
    /// Increments event counter and checks session event limits.
    /// </summary>
    /// <returns>True when event was accepted within limit.</returns>
    private bool RecordEvent()
    {
        if (_failed || _closed) return false;
        if (_capturedBytes >= _options.MaxTraceBytes) return false;
        _capturedBytes += 256;
        if (_events >= _options.MaxEvents) return false;
        if (++_events >= _options.MaxEvents)
        {
            _document.Error = "[Truncated - MaxEvents reached]";
        }
        return true;
    }

    /// <summary>
    /// Records synthetic line write event to support limit testing.
    /// </summary>
    /// <param name="line">Line text; ignored in JSON mode.</param>
    /// <returns>No return value.</returns>
    internal void WriteLine(string line)
    {
        lock (_gate)
        {
            RecordEvent();
        }
    }

    /// <summary>
    /// Captures formatted string value for backward compatibility.
    /// </summary>
    /// <param name="factory">Factory for captured trace values.</param>
    /// <returns>Formatted trace value or hidden, truncated, or unavailable indicator.</returns>
    internal string Value(Func<object?> factory)
    {
        if (!Accepting) return "[Truncated]";
        if (!_options.CaptureContent) return "[Hidden]";
        try { return TraceValue.Format(factory(), _options); }
        catch { return "[Unavailable]"; }
    }

    /// <summary>
    /// Captures structured snapshot object respecting content settings.
    /// </summary>
    /// <param name="factory">Factory for captured trace values.</param>
    /// <returns>Captured object snapshot or indicator string.</returns>
    internal object? Capture(Func<object?> factory)
    {
        if (!Accepting) return "[Truncated]";
        if (!_options.CaptureContent) return "[Hidden]";
        try
        {
            var captured = TraceValue.Capture(factory(), _options);
            lock (_gate)
            {
                _capturedBytes += TraceValue.EstimateBytes(captured);
                if (_capturedBytes >= _options.MaxTraceBytes)
                {
                    _document.Error = "[Truncated - MaxTraceBytes reached]";
                    return "[Truncated]";
                }
            }
            return captured;
        }
        catch { return "[Unavailable]"; }
    }

    /// <summary>
    /// Serializes trace document tree to JSON file on disk.
    /// </summary>
    /// <returns>No return value.</returns>
    private void Flush()
    {
        if (_failed) return;
        try
        {
            using var output = new BoundedTraceFile(_path, Math.Max(4096, _options.MaxTraceBytes));
            try
            {
                JsonSerializer.Serialize(output, _document, JsonOptions);
            }
            catch (TraceSizeException)
            {
                output.SetLength(0);
                output.Position = 0;
                JsonSerializer.Serialize(output, new TraceDocument
                {
                    Id = _document.Id,
                    Timestamp = _document.Timestamp,
                    Status = _document.Status,
                    DurationMs = _document.DurationMs,
                    Cancelled = _document.Cancelled,
                    Error = "[Truncated - MaxTraceBytes reached]"
                }, JsonOptions);
            }
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }

    /// <summary>
    /// Disables further writes and reports first trace failure.
    /// </summary>
    /// <param name="error">Failure to record.</param>
    /// <returns>No return value.</returns>
    private void Fail(Exception error)
    {
        if (!_failed) Warn(_logger, error, "Debug trace file unavailable.");
        _failed = true;
    }

    /// <summary>
    /// Logs warning without allowing logging failures to escape.
    /// </summary>
    /// <param name="logger">Logger for diagnostic failures.</param>
    /// <param name="error">Failure to record.</param>
    /// <param name="message">Error description.</param>
    /// <returns>No return value.</returns>
    internal static void Warn(ILogger logger, Exception error, string message)
    {
        try { logger.LogWarning(error, "{Message}", message); }
        catch { /* A failing log provider must not interrupt file processing. */ }
    }

    /// <summary>
    /// Flushes trace document to disk and disposes session.
    /// </summary>
    /// <returns>No return value.</returns>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_closed) return;
            _closed = true;
            Flush();
        }
    }

    /// <summary>
    /// Completes trace serialization without blocking request threads on disk writes.
    /// </summary>
    /// <returns>Task completing after trace publication or contained trace failure.</returns>
    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_closed) return;
            _closed = true;
            if (_failed) return;
        }
        try
        {
            await using var output = new BoundedTraceFile(_path, Math.Max(4096, _options.MaxTraceBytes));
            try
            {
                await JsonSerializer.SerializeAsync(output, _document, JsonOptions).ConfigureAwait(false);
            }
            catch (TraceSizeException)
            {
                output.SetLength(0);
                output.Position = 0;
                await JsonSerializer.SerializeAsync(output, new TraceDocument
                {
                    Id = _document.Id,
                    Timestamp = _document.Timestamp,
                    Status = _document.Status,
                    DurationMs = _document.DurationMs,
                    Cancelled = _document.Cancelled,
                    Error = "[Truncated - MaxTraceBytes reached]"
                }, JsonOptions).ConfigureAwait(false);
            }
        }
        catch (Exception ex) { Fail(ex); }
    }
}
