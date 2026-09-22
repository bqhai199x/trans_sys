using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;

namespace FileHandler.Tests.Api;

/// <summary>
/// Unit tests for global exception handler error response mapping.
/// </summary>
public sealed class GlobalExceptionHandlerTests
{

    /// <summary>
    /// Verifies error responses omit exception details.
    /// </summary>
    /// <param name="kind">Exception scenario to test.</param>
    /// <param name="status">Expected HTTP status code.</param>
    /// <param name="code">Machine-readable error code.</param>
    /// <returns>Task representing test completion.</returns>
    [Theory]
    [InlineData("http", 413, "request_too_large")]
    [InlineData("multipart", 413, "request_too_large")]
    [InlineData("other", 500, "internal_error")]
    [InlineData("invalid-data", 500, "internal_error")]
    public async Task TryHandleAsync_MapsErrorsWithoutLeakingExceptionDetails(string kind, int status, string code)
    {
        using var services = new ServiceCollection().AddLogging().AddOptions().BuildServiceProvider();
        using var body = new MemoryStream();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Response.Body = body;
        Exception exception = kind switch
        {
            "http" => new BadHttpRequestException("secret", 413),
            "multipart" => new InvalidDataException("secret LENGTH LIMIT exceeded"),
            "invalid-data" => new InvalidDataException("secret"),
            _ => new InvalidOperationException("secret")
        };
        var logger = new CapturingLogger();
        var handler = new GlobalExceptionHandler(logger);
        Assert.True(await handler.TryHandleAsync(context, exception, default));
        Assert.Equal(status, context.Response.StatusCode);
        Assert.StartsWith("application/json", context.Response.ContentType);
        body.Position = 0;
        using var json = await JsonDocument.ParseAsync(body);
        Assert.Equal(code, json.RootElement.GetProperty("errors")[0].GetProperty("code").GetString());
        Assert.DoesNotContain("secret", json.RootElement.GetRawText());
        Assert.DoesNotContain("secret", string.Join("\n", logger.Messages));
        Assert.Null(logger.Exception);
    }

    /// <summary>
    /// Captures logger output for exception handling assertions.
    /// </summary>
    private sealed class CapturingLogger : ILogger<GlobalExceptionHandler>
    {

        /// <summary>
        /// Rendered log messages.
        /// </summary>
        internal List<string> Messages { get; } = [];

        /// <summary>
        /// Exception supplied to logging sink, if any.
        /// </summary>
        internal Exception? Exception { get; private set; }

        /// <summary>
        /// Accepts an unused scope.
        /// </summary>
        /// <typeparam name="TState">Scope state type.</typeparam>
        /// <param name="state">Scope state.</param>
        /// <returns>Null because scopes are not captured.</returns>
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        /// <summary>
        /// Enables every log level for inspection.
        /// </summary>
        /// <param name="logLevel">Requested level.</param>
        /// <returns>True.</returns>
        public bool IsEnabled(LogLevel logLevel) => true;

        /// <summary>
        /// Captures rendered state and raw exception separately.
        /// </summary>
        /// <typeparam name="TState">Log state type.</typeparam>
        /// <param name="logLevel">Severity.</param>
        /// <param name="eventId">Event identifier.</param>
        /// <param name="state">Structured state.</param>
        /// <param name="exception">Optional raw exception.</param>
        /// <param name="formatter">Message formatter.</param>
        /// <returns>No return value.</returns>
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Exception = exception;
            Messages.Add(formatter(state, exception));
        }
    }
}
