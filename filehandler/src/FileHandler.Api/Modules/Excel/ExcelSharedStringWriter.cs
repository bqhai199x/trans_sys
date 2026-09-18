using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Spreadsheet;
using FileHandler.Api.Diagnostics;
using FileHandler.Api.Modules.Office;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace FileHandler.Api.Modules.Excel;

/// <summary>
/// Manages copy-on-write modifications, payload deduplication, and optional counters on Shared String Table.
/// </summary>
public sealed class ExcelSharedStringWriter
{

    /// <summary>
    /// Cache of rich fingerprints mapped to their earliest SST index.
    /// </summary>
    private readonly Dictionary<string, int> _fingerprintIndex = new(StringComparer.Ordinal);

    /// <summary>
    /// Total items in SST including dynamically appended items.
    /// </summary>
    private int _itemCount;

    /// <summary>
    /// Creates shared string writer and indexes initial SST elements.
    /// </summary>
    /// <param name="sst">Shared string table to index.</param>
    public ExcelSharedStringWriter(SharedStringTable sst)
    {
        var index = 0;
        foreach (var item in sst.Elements<SharedStringItem>())
        {
            var fp = BuildItemFingerprint(item);
            if (!_fingerprintIndex.ContainsKey(fp))
            {
                _fingerprintIndex[fp] = index;
            }
            index++;
        }
        _itemCount = index;
    }

    /// <summary>
    /// Reuses identical complete payload or appends a detached rich string.
    /// </summary>
    /// <param name="sst">Target shared string table.</param>
    /// <param name="item">Detached translated payload.</param>
    /// <returns>Existing or appended index.</returns>
    public int ResolveOrAppend(SharedStringTable sst, SharedStringItem item)
    {
        var fingerprint = BuildItemFingerprint(item);
        if (_fingerprintIndex.TryGetValue(fingerprint, out var existing)) return existing;
        var index = _itemCount++;
        _fingerprintIndex.Add(fingerprint, index);
        var extension = sst.GetFirstChild<S.ExtensionList>();
        if (extension is null) sst.AppendChild(item);
        else sst.InsertBefore(item, extension);
        return index;
    }

    /// <summary>
    /// Removes optional count and uniqueCount attributes when SST is modified.
    /// </summary>
    /// <param name="sst">Shared string table to clean.</param>
    /// <returns>No return value.</returns>
    public static void CleanOptionalCounters(SharedStringTable sst)
    {
        sst.Count = null;
        sst.UniqueCount = null;
    }

    /// <summary>
    /// Builds deterministic fingerprint representing entire rich text payload.
    /// </summary>
    /// <param name="item">Shared string item.</param>
    /// <returns>Canonical fingerprint string.</returns>
    public static string BuildItemFingerprint(SharedStringItem item)
    {
        return item.OuterXml;
    }
}
