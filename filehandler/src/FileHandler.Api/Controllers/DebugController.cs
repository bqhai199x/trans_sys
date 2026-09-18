using System.Text.Json;
using FileHandler.Api.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FileHandler.Api.Controllers;

/// <summary>
/// REST API controller managing diagnostic trace viewing, filtering, toggling, and log file maintenance.
/// </summary>
[ApiController]
[Route("debug")]
public sealed class DebugController : ControllerBase
{

    /// <summary>
    /// Debug trace configuration options monitor.
    /// </summary>
    private readonly IOptionsMonitor<DebugTraceOptions> _options;

    /// <summary>
    /// Hosting environment used to resolve physical file paths.
    /// </summary>
    private readonly IWebHostEnvironment _environment;

    /// <summary>
    /// Logger for diagnostic failures.
    /// </summary>
    private readonly ILogger<DebugController> _logger;

    /// <summary>
    /// Creates controller with trace configuration and environment services.
    /// </summary>
    /// <param name="options">Debug trace options monitor.</param>
    /// <param name="environment">Hosting environment.</param>
    /// <param name="logger">Logger for diagnostic operations.</param>
    public DebugController(IOptionsMonitor<DebugTraceOptions> options, IWebHostEnvironment environment, ILogger<DebugController> logger)
    {
        _options = options;
        _environment = environment;
        _logger = logger;
    }

    /// <summary>
    /// Serves debug trace viewer single-page application.
    /// </summary>
    /// <returns>HTML page or fallback status message.</returns>
    [HttpGet("")]
    [HttpGet("/debug.html")]
    [Produces("text/html")]
    public IActionResult Viewer()
    {
        var htmlPath = Path.Combine(_environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot"), "debug.html");
        if (System.IO.File.Exists(htmlPath))
            return PhysicalFile(htmlPath, "text/html; charset=utf-8");
        return Content("Debug viewer HTML not found.", "text/plain");
    }

    /// <summary>
    /// Returns current debug tracing status and stored log counts.
    /// </summary>
    /// <returns>Current status response.</returns>
    [HttpGet("status")]
    [ProducesResponseType(typeof(DebugStatusResponse), StatusCodes.Status200OK)]
    public IActionResult GetStatus()
    {
        var configured = _options.CurrentValue;
        var dir = GetTraceDirectory();
        var count = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.json").Length : 0;
        return Ok(new DebugStatusResponse
        {
            Enabled = DebugTrace.IsEnabled(configured),
            Directory = configured.Directory,
            CaptureContent = configured.CaptureContent,
            TotalLogs = count
        });
    }

    /// <summary>
    /// Toggles debug trace capture mode at runtime.
    /// </summary>
    /// <param name="request">Toggle request payload.</param>
    /// <returns>Updated enablement state.</returns>
    [HttpPost("toggle")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Toggle([FromBody] ToggleDebugRequest request)
    {
        DebugTrace.EnabledOverride = request.Enabled;
        return Ok(new { enabled = request.Enabled });
    }

    /// <summary>
    /// Lists stored trace log summaries with optional search and function filtering.
    /// </summary>
    /// <param name="search">Optional extracted text or content search term.</param>
    /// <param name="function">Optional function name to filter traces by.</param>
    /// <param name="status">Optional HTTP status code filter.</param>
    /// <returns>List of matching trace log summaries.</returns>
    [HttpGet("logs")]
    [ProducesResponseType(typeof(DebugLogSummary[]), StatusCodes.Status200OK)]
    public IActionResult ListLogs([FromQuery] string? search = null, [FromQuery] string? function = null, [FromQuery] int? status = null)
    {
        var dir = GetTraceDirectory();
        if (!Directory.Exists(dir))
            return Ok(Array.Empty<DebugLogSummary>());

        var files = Directory.GetFiles(dir, "*.json");
        var list = new List<DebugLogSummary>();
        var hasContentFilter = !string.IsNullOrWhiteSpace(search) || !string.IsNullOrWhiteSpace(function);

        foreach (var file in files)
        {
            try
            {
                if (hasContentFilter)
                {
                    var content = System.IO.File.ReadAllText(file);
                    if (!string.IsNullOrWhiteSpace(search) && !content.Contains(search, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!string.IsNullOrWhiteSpace(function) && !content.Contains(function, StringComparison.OrdinalIgnoreCase))
                        continue;

                    using var doc = JsonDocument.Parse(content);
                    var summary = ParseLogSummary(doc.RootElement, file, status);
                    if (summary is not null)
                        list.Add(summary);
                }
                else
                {
                    using var stream = System.IO.File.OpenRead(file);
                    var header = JsonSerializer.Deserialize<TraceHeader>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (header is { Version: >= 1 })
                    {
                        if (!status.HasValue || header.Status == status.Value)
                            list.Add(new DebugLogSummary
                            {
                                Id = header.Id ?? Path.GetFileNameWithoutExtension(file),
                                FileName = Path.GetFileName(file),
                                RequestFileName = header.FileName,
                                Timestamp = header.Timestamp,
                                Method = header.Method ?? "",
                                Path = header.Path ?? "",
                                Status = header.Status,
                                DurationMs = header.DurationMs,
                                FileSizeBytes = stream.Length,
                                Error = header.Error.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? null :
                                    header.Error.ValueKind == JsonValueKind.String ? header.Error.GetString() : header.Error.GetRawText()
                            });
                        continue;
                    }
                    stream.Position = 0;
                    using var doc = JsonDocument.Parse(stream);
                    var summary = ParseLogSummary(doc.RootElement, file, status);
                    if (summary is not null)
                        list.Add(summary);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse trace file {File}", file);
            }
        }

        list.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));
        return Ok(list);
    }

    /// <summary>
    /// Root metadata projection; serializer skips call trees without retaining them.
    /// </summary>
    private sealed class TraceHeader
    {

        /// <summary>
        /// Serialized metadata version.
        /// </summary>
        public int Version { get; set; }

        /// <summary>
        /// Request trace identifier.
        /// </summary>
        public string? Id { get; set; }

        /// <summary>
        /// Original client filename, or null.
        /// </summary>
        public string? FileName { get; set; }

        /// <summary>
        /// Trace start time in UTC.
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// HTTP method.
        /// </summary>
        public string? Method { get; set; }

        /// <summary>
        /// Request path.
        /// </summary>
        public string? Path { get; set; }

        /// <summary>
        /// HTTP status code.
        /// </summary>
        public int Status { get; set; }

        /// <summary>
        /// Request duration in milliseconds.
        /// </summary>
        public double DurationMs { get; set; }

        /// <summary>
        /// Bounded error summary, if present.
        /// </summary>
        public JsonElement Error { get; set; }
    }

    /// <summary>
    /// Extracts log summary metadata from trace document root element.
    /// </summary>
    /// <param name="root">Trace document root element.</param>
    /// <param name="file">Trace file path on disk.</param>
    /// <param name="status">Optional status code filter.</param>
    /// <returns>Log summary if matching status filter; otherwise null.</returns>
    private static DebugLogSummary? ParseLogSummary(JsonElement root, string file, int? status)
    {
        var statusCode = TryGetProp(root, "status", out var s) && s.TryGetInt32(out var code) ? code : 0;
        if (status.HasValue && statusCode != status.Value)
            return null;

        var id = TryGetProp(root, "id", out var i) ? i.GetString() ?? "" : Path.GetFileNameWithoutExtension(file);
        var timestamp = TryGetProp(root, "timestamp", out var t) && t.TryGetDateTime(out var dt) ? dt : System.IO.File.GetCreationTimeUtc(file);
        var method = TryGetProp(root, "method", out var m) ? m.GetString() ?? "" : "";
        var path = TryGetProp(root, "path", out var p) ? p.GetString() ?? "" : "";
        var durationMs = TryGetProp(root, "durationMs", out var d) && d.TryGetDouble(out var dur) ? dur : 0;
        string? error = null;
        if (TryGetProp(root, "error", out var err) && err.ValueKind != JsonValueKind.Null)
        {
            error = err.ValueKind == JsonValueKind.String ? err.GetString() : err.GetRawText();
        }

        var reqFileName = TryGetProp(root, "fileName", out var rfn) ? rfn.GetString() : null;
        if (string.IsNullOrEmpty(reqFileName))
        {
            reqFileName = ExtractClientFileNameFromContent(root);
        }

        var info = new FileInfo(file);
        return new DebugLogSummary
        {
            Id = id,
            FileName = Path.GetFileName(file),
            RequestFileName = reqFileName,
            Timestamp = timestamp,
            Method = method,
            Path = path,
            Status = statusCode,
            DurationMs = durationMs,
            FileSizeBytes = info.Length,
            Error = error
        };
    }

    /// <summary>
    /// Retrieves full structured JSON trace document by trace ID.
    /// </summary>
    /// <param name="id">Trace identifier.</param>
    /// <returns>JSON trace document content or not found response.</returns>
    [HttpGet("logs/{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetLog(string id)
    {
        var safeId = Path.GetFileName(id);
        var dir = GetTraceDirectory();
        var filePath = Path.Combine(dir, $"{safeId}.json");
        if (!System.IO.File.Exists(filePath))
            return NotFound(new { message = $"Log {id} not found." });

        var content = System.IO.File.ReadAllText(filePath);
        return Content(content, "application/json; charset=utf-8");
    }

    /// <summary>
    /// Deletes a specific trace log file by ID.
    /// </summary>
    /// <param name="id">Trace identifier.</param>
    /// <returns>Success confirmation or not found response.</returns>
    [HttpDelete("logs/{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult DeleteLog(string id)
    {
        var safeId = Path.GetFileName(id);
        var dir = GetTraceDirectory();
        var filePath = Path.Combine(dir, $"{safeId}.json");
        if (!System.IO.File.Exists(filePath))
            return NotFound(new { message = $"Log {id} not found." });

        try
        {
            System.IO.File.Delete(filePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete trace file {File}", filePath);
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = $"Could not delete log {id}." });
        }

        return Ok(new { message = $"Deleted {id}." });
    }

    /// <summary>
    /// Deletes all stored trace log files.
    /// </summary>
    /// <returns>Count of deleted files.</returns>
    [HttpDelete("logs")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult DeleteAllLogs()
    {
        var dir = GetTraceDirectory();
        if (!Directory.Exists(dir))
            return Ok(new { deleted = 0 });

        var files = Directory.GetFiles(dir, "*.json");
        var count = 0;
        foreach (var file in files)
        {
            try
            {
                System.IO.File.Delete(file);
                count++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete trace file {File}", file);
            }
        }
        return Ok(new { deleted = count });
    }

    /// <summary>
    /// Resolves absolute trace output directory path.
    /// </summary>
    /// <returns>Absolute directory path.</returns>
    private string GetTraceDirectory() =>
        Path.GetFullPath(_options.CurrentValue.Directory, _environment.ContentRootPath);

    /// <summary>
    /// Attempts to find JSON property using case-insensitive name matching.
    /// </summary>
    /// <param name="element">Parent JSON element.</param>
    /// <param name="name">Target property name.</param>
    /// <param name="value">Located property element if found.</param>
    /// <returns>True when property is found.</returns>
    private static bool TryGetProp(JsonElement element, string name, out JsonElement value)
    {
        if (element.TryGetProperty(name, out value)) return true;
        if (string.IsNullOrEmpty(name)) return false;
        Span<char> buffer = name.Length <= 128 ? stackalloc char[name.Length] : new char[name.Length];
        name.AsSpan().CopyTo(buffer);
        buffer[0] = char.ToUpperInvariant(buffer[0]);
        if (element.TryGetProperty(buffer, out value)) return true;
        buffer[0] = char.ToLowerInvariant(buffer[0]);
        return element.TryGetProperty(buffer, out value);
    }

    /// <summary>
    /// Searches for client file name in trace calls if not present at root.
    /// </summary>
    /// <param name="root">Trace document root element.</param>
    /// <returns>Extracted file name or null if not found.</returns>
    private static string? ExtractClientFileNameFromContent(JsonElement root)
    {
        if (!TryGetProp(root, "calls", out var calls) || calls.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var call in calls.EnumerateArray())
        {
            if (!TryGetProp(call, "children", out var children) || children.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var child in children.EnumerateArray())
            {
                if (!TryGetProp(child, "in", out var inObj) || inObj.ValueKind != JsonValueKind.Object)
                    continue;

                if (!TryGetProp(inObj, "request", out var reqObj) || reqObj.ValueKind != JsonValueKind.Object)
                    continue;

                if (!TryGetProp(reqObj, "file", out var fileObj) || fileObj.ValueKind != JsonValueKind.Object)
                    continue;

                if (TryGetProp(fileObj, "fileName", out var fn) && fn.ValueKind == JsonValueKind.String)
                    return fn.GetString();
            }
        }

        return null;
    }
}
