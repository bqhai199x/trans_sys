using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.AspNetCore.Mvc;

namespace FileHandler.Api.Diagnostics;

/// <summary>
/// Captures and serializes bounded snapshots of runtime values for diagnostic traces.
/// </summary>
internal static class TraceValue
{

    /// <summary>
    /// Estimates bounded snapshot storage using worst-case JSON string escaping.
    /// </summary>
    /// <param name="value">Already captured snapshot.</param>
    /// <returns>Conservative byte estimate.</returns>
    internal static long EstimateBytes(object? value)
    {
        if (value is null) return 4;
        if (value is string text) return 2L + text.Length * 6L;
        if (value is IDictionary dictionary)
        {
            long bytes = 32;
            foreach (DictionaryEntry entry in dictionary) bytes += EstimateBytes(entry.Key) + EstimateBytes(entry.Value) + 2;
            return bytes;
        }
        if (value is IEnumerable collection)
        {
            long bytes = 32;
            foreach (var item in collection) bytes += EstimateBytes(item) + 1;
            return bytes;
        }
        return 128;
    }

    /// <summary>
    /// JSON serializer settings for string and value escaping.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Captures bounded trace value object according to capture settings.
    /// </summary>
    /// <param name="value">Value to process.</param>
    /// <param name="options">Trace capture settings and limits.</param>
    /// <returns>Bounded captured object or indicator string.</returns>
    public static object? Capture(object? value, DebugTraceOptions options)
    {
        if (!options.CaptureContent) return "[Hidden]";
        try
        {
            var budget = 100;
            return Snapshot(value, 0, options.MaxValueLength, ref budget);
        }
        catch { return "[Unavailable]"; }
    }

    /// <summary>
    /// Formats bounded trace value into JSON string according to capture settings.
    /// </summary>
    /// <param name="value">Value to process.</param>
    /// <param name="options">Trace capture settings and limits.</param>
    /// <returns>Formatted JSON string or indicator text.</returns>
    public static string Format(object? value, DebugTraceOptions options)
    {
        if (!options.CaptureContent) return "[Hidden]";
        try
        {
            var captured = Capture(value, options);
            if (captured is null) return "null";
            if (captured is string s && s is "[Truncated]" or "[Hidden]" or "[Deferred]" or "[Unavailable]" or "void" or "Stream")
                return s;
            if (captured is Enum e) return e.ToString();
            var text = JsonSerializer.Serialize(captured, JsonOptions);
            return text.Length <= options.MaxValueLength ? text : text[..options.MaxValueLength] + " [Truncated]";
        }
        catch { return "[Unavailable]"; }
    }

    /// <summary>
    /// Formats property name using camelCase convention.
    /// </summary>
    /// <param name="name">Original property name.</param>
    /// <returns>Formatted property name in camelCase.</returns>
    internal static string FormatPropertyName(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        if (name.Contains('.')) return name;
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    /// <summary>
    /// Builds trace snapshot within depth, size, and item limits.
    /// </summary>
    /// <param name="value">Value to process.</param>
    /// <param name="depth">Current snapshot nesting depth.</param>
    /// <param name="limit">Maximum captured string length.</param>
    /// <param name="budget">Remaining snapshot item budget, updated during traversal.</param>
    /// <returns>Bounded snapshot or truncation indicator.</returns>
    private static object? Snapshot(object? value, int depth, int limit, ref int budget)
    {
        if (value is null) return null;
        if (--budget < 0 || depth > 4) return "[Truncated]";
        if (value is string text) return SnapshotString(text, depth, limit, ref budget);
        if (value is Enum) return value;
        if (value is double d && !double.IsFinite(d)) throw new NotSupportedException("Non-finite double not supported.");
        if (value is float f && !float.IsFinite(f)) throw new NotSupportedException("Non-finite float not supported.");
        if (value.GetType().IsPrimitive || value is decimal) return value;
        if (TrySnapshotSpecial(value, limit, out var special)) return special;
        if (value is IActionResult actionResult) return SnapshotActionResult(actionResult, depth, limit, ref budget);
        if (value is MarkdownObject node) return SnapshotMarkdownObject(node, depth, limit, ref budget);
        if (value is IDictionary dictionary) return SnapshotDictionary(dictionary, depth, limit, ref budget);
        if (value is ITuple tuple) return SnapshotTuple(tuple, depth, limit, ref budget);
        if (value is IEnumerable sequence) return SnapshotSequence(sequence, depth, limit, ref budget);
        return SnapshotReflection(value, depth, limit, ref budget);
    }

    /// <summary>
    /// Captures string or embedded JSON object.
    /// </summary>
    /// <param name="text">String value to process.</param>
    /// <param name="depth">Current snapshot nesting depth.</param>
    /// <param name="limit">Maximum captured string length.</param>
    /// <param name="budget">Remaining snapshot item budget.</param>
    /// <returns>Bounded string or JSON snapshot.</returns>
    private static object? SnapshotString(string text, int depth, int limit, ref int budget)
    {
        var trimmed = text.Trim();
        if (trimmed.Length <= Math.Max(limit * 8, 32768) && ((trimmed.StartsWith('{') && trimmed.EndsWith('}')) || (trimmed.StartsWith('[') && trimmed.EndsWith(']'))))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                return SnapshotJson(doc.RootElement, depth + 1, limit, ref budget);
            }
            catch
            {
                // Fall back to plain string when not valid JSON.
            }
        }
        return text.Length <= limit ? text : text[..limit] + " [Truncated]";
    }

    /// <summary>
    /// Captures well-known framework and primitive types.
    /// </summary>
    /// <param name="value">Value to evaluate.</param>
    /// <param name="limit">Maximum captured string length.</param>
    /// <param name="result">Snapshot representation when recognized.</param>
    /// <returns>True when value was recognized as a special type; otherwise false.</returns>
    private static bool TrySnapshotSpecial(object value, int limit, out object? result)
    {
        switch (value)
        {
            case CancellationToken token:
                result = new Dictionary<string, object?> { ["cancelled"] = token.IsCancellationRequested };
                return true;
            case Stream:
                result = "Stream";
                return true;
            case IFormFile file:
                result = new Dictionary<string, object?> { ["fileName"] = file.FileName, ["length"] = file.Length, ["contentType"] = file.ContentType };
                return true;
            case byte[] bytes:
                result = new Dictionary<string, object?> { ["bytes"] = bytes.Length, ["text"] = Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, limit)), ["truncated"] = bytes.Length > limit };
                return true;
            case StringBuilder builder:
                var textVal = builder.ToString(0, Math.Min(builder.Length, limit));
                result = builder.Length > limit ? textVal + " [Truncated]" : textVal;
                return true;
            default:
                result = null;
                return false;
        }
    }

    /// <summary>
    /// Captures action result status and payload.
    /// </summary>
    /// <param name="actionResult">Action result to process.</param>
    /// <param name="depth">Current snapshot nesting depth.</param>
    /// <param name="limit">Maximum captured string length.</param>
    /// <param name="budget">Remaining snapshot item budget.</param>
    /// <returns>Captured action result properties.</returns>
    private static object? SnapshotActionResult(IActionResult actionResult, int depth, int limit, ref int budget)
    {
        return actionResult switch
        {
            ObjectResult obj => new Dictionary<string, object?> { ["status"] = obj.StatusCode, ["body"] = Snapshot(obj.Value, depth + 1, limit, ref budget) },
            FileContentResult file => new Dictionary<string, object?>
            {
                ["contentType"] = file.ContentType,
                ["fileDownloadName"] = file.FileDownloadName,
                ["content"] = file.ContentType?.StartsWith("text/", StringComparison.OrdinalIgnoreCase) == true
                    ? Snapshot(file.FileContents, depth + 1, limit, ref budget)
                    : new Dictionary<string, object?> { ["bytes"] = file.FileContents.Length, ["kind"] = "binary" }
            },
            StatusCodeResult status => new Dictionary<string, object?> { ["status"] = status.StatusCode },
            _ => actionResult.GetType().Name
        };
    }

    /// <summary>
    /// Captures Markdig syntax tree node.
    /// </summary>
    /// <param name="node">Markdown syntax node.</param>
    /// <param name="depth">Current snapshot nesting depth.</param>
    /// <param name="limit">Maximum captured string length.</param>
    /// <param name="budget">Remaining snapshot item budget.</param>
    /// <returns>Captured Markdown node properties.</returns>
    private static object? SnapshotMarkdownObject(MarkdownObject node, int depth, int limit, ref int budget)
    {
        var info = new Dictionary<string, object?> { ["type"] = node.GetType().Name, ["span"] = $"{node.Span.Start}..{node.Span.End}" };
        if (node is LiteralInline literal) info["text"] = Snapshot(literal.Content.ToString(), depth + 1, limit, ref budget);
        if (node is ContainerInline container) info["children"] = Snapshot(container.Take(Math.Max(0, budget)).ToArray(), depth + 1, limit, ref budget);
        if (node is ContainerBlock blocks) info["blocks"] = Snapshot(blocks.Take(Math.Max(0, budget)).ToArray(), depth + 1, limit, ref budget);
        if (node is LeafBlock leaf) info["inline"] = Snapshot(leaf.Inline, depth + 1, limit, ref budget);
        return info;
    }

    /// <summary>
    /// Captures dictionary entries within item limits.
    /// </summary>
    /// <param name="dictionary">Dictionary to process.</param>
    /// <param name="depth">Current snapshot nesting depth.</param>
    /// <param name="limit">Maximum captured string length.</param>
    /// <param name="budget">Remaining snapshot item budget.</param>
    /// <returns>Captured dictionary entries.</returns>
    private static object? SnapshotDictionary(IDictionary dictionary, int depth, int limit, ref int budget)
    {
        var entries = new Dictionary<string, object?>();
        foreach (DictionaryEntry entry in dictionary)
        {
            if (entries.Count == 20 || budget <= 0) { entries["truncated"] = "[Truncated]"; break; }
            var key = FormatPropertyName(Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? "null");
            entries[key] = Snapshot(entry.Value, depth + 1, limit, ref budget);
        }
        return entries;
    }

    /// <summary>
    /// Captures tuple elements within item limits.
    /// </summary>
    /// <param name="tuple">Tuple to process.</param>
    /// <param name="depth">Current snapshot nesting depth.</param>
    /// <param name="limit">Maximum captured string length.</param>
    /// <param name="budget">Remaining snapshot item budget.</param>
    /// <returns>Captured tuple elements.</returns>
    private static object? SnapshotTuple(ITuple tuple, int depth, int limit, ref int budget)
    {
        var items = new List<object?>();
        for (var i = 0; i < Math.Min(tuple.Length, 20); i++) items.Add(Snapshot(tuple[i], depth + 1, limit, ref budget));
        return items;
    }

    /// <summary>
    /// Captures enumerable sequence items within item limits.
    /// </summary>
    /// <param name="sequence">Sequence to process.</param>
    /// <param name="depth">Current snapshot nesting depth.</param>
    /// <param name="limit">Maximum captured string length.</param>
    /// <param name="budget">Remaining snapshot item budget.</param>
    /// <returns>Captured sequence items or deferred indicator.</returns>
    private static object? SnapshotSequence(IEnumerable sequence, int depth, int limit, ref int budget)
    {
        var type = sequence.GetType();
        if (sequence is not ICollection && !type.GetInterfaces().Any(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IReadOnlyCollection<>)))
            return $"{type.Name} [Deferred]";
        var items = new List<object?>();
        foreach (var item in sequence)
        {
            if (items.Count == 20 || budget <= 0) { items.Add("[Truncated]"); break; }
            items.Add(Snapshot(item, depth + 1, limit, ref budget));
        }
        return items;
    }

    /// <summary>
    /// Cached public instance property arrays per type to avoid repeated reflection.
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, PropertyInfo[]> PropertyCache = new();

    /// <summary>
    /// Captures object properties using reflection with exception protection.
    /// </summary>
    /// <param name="value">Object to inspect.</param>
    /// <param name="depth">Current snapshot nesting depth.</param>
    /// <param name="limit">Maximum captured string length.</param>
    /// <param name="budget">Remaining snapshot item budget.</param>
    /// <returns>Captured property dictionary or type name.</returns>
    private static object? SnapshotReflection(object value, int depth, int limit, ref int budget)
    {
        var valueType = value.GetType();
        if (valueType.Namespace?.StartsWith("FileHandler.Api", StringComparison.Ordinal) != true && !valueType.IsDefined(typeof(CompilerGeneratedAttribute)))
            return valueType.Name;
        var props = PropertyCache.GetOrAdd(valueType, t =>
            t.GetProperties(BindingFlags.Instance | BindingFlags.Public)
             .Where(p => p.GetIndexParameters().Length == 0)
             .Take(20).ToArray());
        var properties = new Dictionary<string, object?>();
        foreach (var property in props)
        {
            object? propVal;
            try { propVal = property.GetValue(value); }
            catch (Exception ex) { propVal = $"[Error: {ex.GetType().Name}]"; }
            properties[FormatPropertyName(property.Name)] = Snapshot(propVal, depth + 1, limit, ref budget);
        }
        return properties.Count == 0 && !valueType.IsDefined(typeof(CompilerGeneratedAttribute)) ? valueType.Name : properties;
    }

    /// <summary>
    /// Converts parsed JSON element tree into bounded snapshot representation.
    /// </summary>
    /// <param name="element">JSON element to convert.</param>
    /// <param name="depth">Current snapshot nesting depth.</param>
    /// <param name="limit">Maximum string length limit.</param>
    /// <param name="budget">Remaining snapshot item budget.</param>
    /// <returns>Bounded JSON snapshot representation.</returns>
    private static object? SnapshotJson(JsonElement element, int depth, int limit, ref int budget)
    {
        if (--budget < 0 || depth > 4) return "[Truncated]";
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var dict = new Dictionary<string, object?>();
                foreach (var prop in element.EnumerateObject())
                {
                    if (dict.Count == 20 || budget <= 0) { dict["truncated"] = true; break; }
                    dict[FormatPropertyName(prop.Name)] = SnapshotJson(prop.Value, depth + 1, limit, ref budget);
                }
                return dict;
            case JsonValueKind.Array:
                var list = new List<object?>();
                foreach (var item in element.EnumerateArray())
                {
                    if (list.Count == 20 || budget <= 0) { list.Add("[Truncated]"); break; }
                    list.Add(SnapshotJson(item, depth + 1, limit, ref budget));
                }
                return list;
            case JsonValueKind.String:
                var str = element.GetString() ?? "";
                return str.Length <= limit ? str : str[..limit] + " [Truncated]";
            case JsonValueKind.Number:
                if (element.TryGetInt64(out var l)) return l;
                if (element.TryGetDouble(out var d)) return d;
                return element.GetRawText();
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.Null:
            default:
                return null;
        }
    }
}
