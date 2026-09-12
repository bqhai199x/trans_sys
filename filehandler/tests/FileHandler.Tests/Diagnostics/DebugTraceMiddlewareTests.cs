using FileHandler.Api.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FileHandler.Tests.Diagnostics;

/// <summary>
/// Unit tests for debug trace middleware invocation, logging, and error propagation.
/// </summary>
public sealed class DebugTraceMiddlewareTests : IDisposable
{

    /// <summary>
    /// Temporary trace directory for this test fixture.
    /// </summary>
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"middleware-unit-{Guid.NewGuid():N}");

    /// <summary>
    /// Deletes trace files created by this test fixture.
    /// </summary>
    /// <returns>No return value.</returns>
    public void Dispose()
    {
        // This instance owns only files created under its unique temporary directory.
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    /// <summary>
    /// Creates middleware with fixed options and test environment.
    /// </summary>
    /// <param name="next">Next request delegate.</param>
    /// <param name="enabled">Whether tracing is enabled.</param>
    /// <param name="directory">Trace output directory.</param>
    /// <returns>Middleware configured for testing.</returns>
    private DebugTraceMiddleware Create(RequestDelegate next, bool enabled = true, string? directory = null) =>
        new(next, new FixedOptions(new() { Enabled = enabled, Directory = directory ?? "traces" }),
            new TestEnvironment { ContentRootPath = _directory }, NullLogger<DebugTraceMiddleware>.Instance);

    /// <summary>
    /// Verifies disabled tracing processes requests without creating files.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task InvokeAsync_DisabledCallsNextWithoutCreatingFiles()
    {
        var called = 0;
        await Create(context => { called++; context.Response.StatusCode = 204; return Task.CompletedTask; }, false)
            .InvokeAsync(new DefaultHttpContext());
        Assert.Equal(1, called);
        Assert.False(Directory.Exists(_directory));
    }

    /// <summary>
    /// Verifies enabled tracing records responses and restores ambient state.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task InvokeAsync_EnabledLogsResponseAndRestoresAmbientState()
    {
        var previous = DebugTrace.Current;
        var context = new DefaultHttpContext { Request = { Path = "/import" } };
        await Create(ctx =>
        {
            Assert.NotNull(DebugTrace.Current);
            ctx.Response.StatusCode = 201;
            return Task.CompletedTask;
        }).InvokeAsync(context);
        Assert.Same(previous, DebugTrace.Current);
        var log = File.ReadAllText(Assert.Single(Directory.GetFiles(Path.Combine(_directory, "traces"), "*.json")));
        using (var doc = System.Text.Json.JsonDocument.Parse(log))
        {
            Assert.Equal(201, doc.RootElement.GetProperty("status").GetInt32());
        }
    }

    /// <summary>
    /// Verifies request failures are traced and rethrown unchanged.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task InvokeAsync_LogsAndRethrowsOriginalException()
    {
        var previous = DebugTrace.Current;
        var error = new InvalidOperationException("failure");
        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Create(_ => throw error).InvokeAsync(new DefaultHttpContext { Request = { Path = "/export" } }));
        Assert.Same(error, actual);
        Assert.Same(previous, DebugTrace.Current);
        var log = File.ReadAllText(Assert.Single(Directory.GetFiles(Path.Combine(_directory, "traces"), "*.json")));
        using var doc = System.Text.Json.JsonDocument.Parse(log);
        Assert.Equal(500, doc.RootElement.GetProperty("status").GetInt32());
        Assert.Contains("failure", log);
    }

    /// <summary>
    /// Verifies trace directory failures still process requests once.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task InvokeAsync_CannotCreateDirectoryStillCallsNextOnce()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "existing-file");
        File.WriteAllText(path, "preserve");
        var called = 0;
        await Create(_ => { called++; return Task.CompletedTask; }, directory: path).InvokeAsync(new DefaultHttpContext { Request = { Path = "/import" } });
        Assert.Equal(1, called);
        Assert.Equal("preserve", File.ReadAllText(path));
    }

    /// <summary>
    /// Verifies non-traceable endpoints are passed through without creating traces.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task InvokeAsync_NonTraceableEndpointSkipsTracing()
    {
        var called = 0;
        await Create(context => { called++; context.Response.StatusCode = 200; return Task.CompletedTask; })
            .InvokeAsync(new DefaultHttpContext { Request = { Path = "/debug" } });
        Assert.Equal(1, called);
        Assert.False(Directory.Exists(_directory));
    }

    /// <summary>
    /// Verifies logging failures do not escape warning handling.
    /// </summary>
    /// <returns>No return value.</returns>
    [Fact]
    public void Warn_ContainsLoggingProviderFailures() =>
        TraceSession.Warn(new ThrowingLogger(), new IOException("failure"), "warning");

    /// <summary>
    /// Creates fixed trace options for middleware tests.
    /// </summary>
    /// <param name="value">Trace options returned by this stub.</param>
    private sealed class FixedOptions(DebugTraceOptions value) : IOptionsMonitor<DebugTraceOptions>
    {

        /// <summary>
        /// Fixed trace options.
        /// </summary>
        public DebugTraceOptions CurrentValue => value;

        /// <summary>
        /// Returns fixed trace options for any name.
        /// </summary>
        /// <param name="name">Options name; ignored by this stub.</param>
        /// <returns>Fixed trace options.</returns>
        public DebugTraceOptions Get(string? name) => value;

        /// <summary>
        /// Ignores option change subscriptions.
        /// </summary>
        /// <param name="listener">Callback for option changes; unused by this stub.</param>
        /// <returns>Null because this stub does not track changes.</returns>
        public IDisposable? OnChange(Action<DebugTraceOptions, string?> listener) => null;
    }

    /// <summary>
    /// Test hosting environment stub providing simulated paths and file providers.
    /// </summary>
    private sealed class TestEnvironment : IWebHostEnvironment
    {

        /// <summary>
        /// Application name used by middleware tests.
        /// </summary>
        public string ApplicationName { get; set; } = "FileHandler.Tests";

        /// <summary>
        /// Test host environment name.
        /// </summary>
        public string EnvironmentName { get; set; } = "Testing";

        /// <summary>
        /// Root directory for application content.
        /// </summary>
        public string ContentRootPath { get; set; } = "";

        /// <summary>
        /// Root directory for web content.
        /// </summary>
        public string WebRootPath { get; set; } = "";

        /// <summary>
        /// Provider for application content files.
        /// </summary>
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();

        /// <summary>
        /// Provider for web content files.
        /// </summary>
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }

    /// <summary>
    /// Test logger stub throwing on log writes to verify resilient error isolation.
    /// </summary>
    private sealed class ThrowingLogger : ILogger
    {

        /// <summary>
        /// Creates no logging scope.
        /// </summary>
        /// <typeparam name="TState">Logging state type.</typeparam>
        /// <param name="state">Logging state.</param>
        /// <returns>Null because this logger creates no scopes.</returns>
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        /// <summary>
        /// Reports logging enabled at every level.
        /// </summary>
        /// <param name="logLevel">Log level to query.</param>
        /// <returns>True for every log level.</returns>
        public bool IsEnabled(LogLevel logLevel) => true;

        /// <summary>
        /// Throws to simulate logging provider failure.
        /// </summary>
        /// <typeparam name="TState">Logging state type.</typeparam>
        /// <param name="level">Log level for this event.</param>
        /// <param name="eventId">Logging event ID.</param>
        /// <param name="state">Logging state.</param>
        /// <param name="exception">Optional exception attached to this event.</param>
        /// <param name="formatter">Log message formatter.</param>
        /// <returns>No return value.</returns>
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            throw new InvalidOperationException("Logger unavailable");
    }
}
