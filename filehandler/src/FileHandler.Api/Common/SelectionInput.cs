using System.Text.Json;

namespace FileHandler.Api.Common;

/// <summary>
/// Parses optional native identifier selection without reading uploaded files.
/// </summary>
internal static class SelectionInput
{

    /// <summary>
    /// Parses an array while distinguishing default selection from an empty set.
    /// </summary>
    /// <param name="json">Optional JSON form field.</param>
    /// <param name="ids">Distinct IDs, or null for default visibility selection.</param>
    /// <returns>Request error, or null for valid selection.</returns>
    internal static FileError? Parse(string? json, out IReadOnlyList<string>? ids)
    {
        ids = null;
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.EnumerateArray().Any(e => e.ValueKind != JsonValueKind.String))
                return new("invalid_selection", ProcessingMessages.InvalidSelection);
            ids = document.RootElement.EnumerateArray().Select(e => e.GetString()!).Distinct(StringComparer.Ordinal).ToArray();
            return null;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return new("invalid_selection", ProcessingMessages.InvalidSelectionJson);
        }
    }
}

/// <summary>
/// Workbook selection by native sheet IDs.
/// </summary>
/// <param name="SheetIds">Explicit IDs, or null for visible sheets.</param>
public sealed record ExcelSelection(IReadOnlyList<string>? SheetIds);

/// <summary>
/// Presentation selection by native slide IDs.
/// </summary>
/// <param name="SlideIds">Explicit IDs, or null for visible slides.</param>
public sealed record PowerPointSelection(IReadOnlyList<string>? SlideIds);

/// <summary>
/// Valid request selection referencing absent source identifiers.
/// </summary>
/// <param name="metadata">Collected source inventory.</param>
internal sealed class UnknownSelectionException(FileMetadata metadata) : Exception("Selection contains an unknown source ID.")
{

    /// <summary>
    /// Source facts available before selection failed.
    /// </summary>
    internal FileMetadata Metadata { get; } = metadata;
}
