using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace FileHandler.Api.Diagnostics;

/// <summary>
/// Creates middleware that writes optional request traces.
/// </summary>
/// <param name="next">Next request delegate.</param>
/// <param name="options">Trace capture settings and limits.</param>
/// <param name="environment">Host environment used to resolve trace paths.</param>
/// <param name="logger">Logger for tracing failures.</param>
internal sealed class DebugTraceMiddleware(RequestDelegate next, IOptionsMonitor<DebugTraceOptions> options, IWebHostEnvironment environment, ILogger<DebugTraceMiddleware> logger)
{

    /// <summary>
    /// Cached resolved absolute trace directory path; null until first request.
    /// </summary>
    private string? _directory;

    /// <summary>
    /// Checks whether request matches endpoint metadata or configured traceable paths.
    /// </summary>
    /// <param name="context">Current HTTP request context.</param>
    /// <param name="configured">Active trace options.</param>
    /// <returns>True when request is eligible for tracing.</returns>
    private static bool IsTraceableEndpoint(HttpContext context, DebugTraceOptions configured)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<TraceableAttribute>() is not null)
            return true;

        var path = context.Request.Path.Value ?? string.Empty;
        if (configured.TraceablePaths is { Length: > 0 } paths)
        {
            return paths.Any(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        }

        return path.Equals("/import", StringComparison.OrdinalIgnoreCase) ||
               path.Equals("/export", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Traces request when enabled and restores previous trace scope.
    /// </summary>
    /// <param name="context">Current HTTP request context.</param>
    /// <returns>Task representing request processing and trace completion.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        var configured = options.CurrentValue;
        if (!DebugTrace.IsEnabled(configured) || !IsTraceableEndpoint(context, configured)) { await next(context); return; }
        TraceSession session;
        try
        {
            var snapshot = new DebugTraceOptions
            {
                Enabled = true,
                CaptureContent = configured.CaptureContent,
                MaxValueLength = Math.Clamp(configured.MaxValueLength, 64, 20000),
                MaxEvents = Math.Clamp(configured.MaxEvents, 100, 100000),
                MaxTraceBytes = Math.Clamp(configured.MaxTraceBytes, 4096, 67108864)
            };
            var directory = _directory ??= Path.GetFullPath(configured.Directory, environment.ContentRootPath);
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);
            var filePath = Path.Combine(directory, $"{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.json");
            session = new TraceSession(filePath, snapshot, logger);
            session.Document.Method = context.Request.Method;
            session.Document.Path = context.Request.Path.Value ?? string.Empty;
        }
        catch (Exception ex)
        {
            TraceSession.Warn(logger, ex, "Cannot create debug trace file.");
            await next(context);
            return;
        }
        await using (session)
        {
            var previous = DebugTrace.Current;
            using var request = new TraceCall(session, previous, "API", "Request");
            DebugTrace.Current = request;
            request.Input(() => new { Id = context.TraceIdentifier, Method = context.Request.Method, Path = context.Request.Path.Value });
            var stopwatch = Stopwatch.StartNew();
            try
            {
                await next(context);
                request.Return(new { Status = context.Response.StatusCode });
                session.WriteResult(context.Response.StatusCode, stopwatch.Elapsed.TotalMilliseconds, null, context.RequestAborted.IsCancellationRequested);
            }
            catch (Exception ex)
            {
                request.Error(ex);
                var statusCode = context.Response.HasStarted ? context.Response.StatusCode : 500;
                session.WriteResult(statusCode, stopwatch.Elapsed.TotalMilliseconds, ex, ex is OperationCanceledException || context.RequestAborted.IsCancellationRequested);
                throw;
            }
            finally { DebugTrace.Current = previous; }
        }
    }
}
