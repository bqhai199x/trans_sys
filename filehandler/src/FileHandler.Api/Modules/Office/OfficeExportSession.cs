using System.IO.Compression;
using System.Security.Cryptography;
using FileHandler.Api.Common;
using FileHandler.Api.Diagnostics;

namespace FileHandler.Api.Modules.Office;

/// <summary>
/// Manages atomic creation and bounded serialization of output Office ZIP package.
/// </summary>
public sealed class OfficeExportSession : IDisposable
{

    /// <summary>
    /// Source document snapshot.
    /// </summary>
    private readonly OfficeSource _source;

    /// <summary>
    /// File handling limits for output sizing.
    /// </summary>
    private readonly FileHandlingOptions _fileHandlingOptions;

    /// <summary>
    /// Office processing options.
    /// </summary>
    private readonly OfficeProcessingOptions _options;

    /// <summary>
    /// Serialized replacement payloads for touched parts, keyed by canonical URI.
    /// </summary>
    private readonly Dictionary<string, byte[]> _touchedParts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Current aggregate replacement payload bytes.
    /// </summary>
    private long _touchedBytes;

    /// <summary>
    /// Creates export session.
    /// </summary>
    /// <param name="source">Source Office document snapshot.</param>
    /// <param name="options">Office processing options.</param>
    /// <param name="fileHandlingOptions">File handling options.</param>
    /// <returns>Initialized export session.</returns>
    public static OfficeExportSession Create(
        OfficeSource source,
        OfficeProcessingOptions options,
        FileHandlingOptions fileHandlingOptions)
    {
        using var trace = DebugTrace.Enter("OfficeExportSession", "Create", () => new
        {
            sourceHash = source.SourceHash,
            outputLimit = fileHandlingOptions.MaxOutputBytes
        });

        try
        {
            trace.State("stage", () => "prepareSession");
            var session = new OfficeExportSession(source, options, fileHandlingOptions);
            trace.Return(new { outcome = "ready", maxOutputBytes = fileHandlingOptions.MaxOutputBytes });
            return session;
        }
        catch (Exception ex)
        {
            trace.Error(ex);
            throw;
        }
    }

    /// <summary>
    /// Private constructor for export session.
    /// </summary>
    /// <param name="source">Source document snapshot.</param>
    /// <param name="options">Office processing options.</param>
    /// <param name="fileHandlingOptions">General file handling options.</param>
    private OfficeExportSession(
        OfficeSource source,
        OfficeProcessingOptions options,
        FileHandlingOptions fileHandlingOptions)
    {
        _source = source;
        _options = options;
        _fileHandlingOptions = fileHandlingOptions;
    }

    /// <summary>
    /// Writes serialized replacement bytes for touched package part.
    /// </summary>
    /// <param name="partUri">Canonical URI of part.</param>
    /// <param name="content">Serialized XML bytes.</param>
    /// <returns>No return value.</returns>
    /// <exception cref="InvalidOperationException">Serialized part exceeds maximum allowed part bytes.</exception>
    public void WritePart(string partUri, byte[] content)
    {
        using var trace = DebugTrace.Enter("OfficeExportSession", "WritePart", () => new
        {
            partUri,
            byteLength = content.Length
        });

        try
        {
            trace.State("stage", () => "serializePart");
            if (content.Length > _options.MaxPartBytes)
                throw new FileLimitException("office_package_limit_exceeded");

            var total = _touchedBytes - (_touchedParts.TryGetValue(partUri, out var previous) ? previous.Length : 0) + content.Length;
            if (total > _options.MaxExpandedBytes) throw new FileLimitException("office_package_limit_exceeded");
            _touchedBytes = total;

            var hash = Convert.ToHexStringLower(SHA256.HashData(content));
            _touchedParts[partUri] = content;

            trace.Return(new { outcome = "success", partUri, bytes = content.Length, hash });
        }
        catch (Exception ex)
        {
            trace.Error(ex);
            throw;
        }
    }

    /// <summary>
    /// Serializes a modified XML root through a bounded stream.
    /// </summary>
    /// <param name="partUri">Canonical target URI.</param>
    /// <param name="root">Modified XML root.</param>
    /// <returns>No return value.</returns>
    public void WritePart(string partUri, DocumentFormat.OpenXml.OpenXmlElement root)
    {
        using var buffer = new MemoryStream();
        using var bounded = new OfficeBoundedStream(buffer, _options.MaxPartBytes);
        using (var writer = System.Xml.XmlWriter.Create(bounded, new System.Xml.XmlWriterSettings { Encoding = System.Text.Encoding.UTF8 }))
            root.WriteTo(writer);
        WritePart(partUri, buffer.ToArray());
    }

    /// <summary>
    /// Finalizes and assembles final output ZIP package.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task producing assembled Office output.</returns>
    public async Task<OfficeOutput> FinalizeAsync(CancellationToken cancellationToken)
    {
        using var trace = DebugTrace.Enter("OfficeExportSession", "FinalizeAsync", () => new
        {
            touchedPartsCount = _touchedParts.Count,
            outputLimit = _fileHandlingOptions.MaxOutputBytes
        });

        try
        {
            trace.State("stage", () => "writeEntries");
            using var outputMs = new MemoryStream();
            using var boundedStream = new OfficeBoundedStream(outputMs, _fileHandlingOptions.MaxOutputBytes);

            using (var originalZip = new ZipArchive(new MemoryStream(_source.OriginalBytes), ZipArchiveMode.Read, false))
            using (var targetZip = new ZipArchive(boundedStream, ZipArchiveMode.Create, true))
            {
                var expandedBytes = originalZip.Entries.Sum(e => _touchedParts.TryGetValue("/" + e.FullName.TrimStart('/'), out var replacement) ? replacement.LongLength : e.Length);
                if (expandedBytes > _options.MaxExpandedBytes) throw new FileLimitException("office_package_limit_exceeded");
                var entryIndex = 0;
                foreach (var entry in originalZip.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    entryIndex++;

                    var canonicalUri = "/" + entry.FullName.Replace('\\', '/').TrimStart('/');
                    var targetEntry = targetZip.CreateEntry(entry.FullName, CompressionLevel.Optimal);

                    if (_touchedParts.TryGetValue(canonicalUri, out var touchedBytes))
                    {
                        using var targetStream = targetEntry.Open();
                        await targetStream.WriteAsync(touchedBytes, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        using var sourceStream = entry.Open();
                        using var targetStream = targetEntry.Open();
                        await sourceStream.CopyToAsync(targetStream, cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            trace.State("stage", () => "closeArchive");
            var finalBytes = outputMs.ToArray();
            var outputHash = Convert.ToHexStringLower(SHA256.HashData(finalBytes));

            trace.Return(new { outcome = "success", outputBytes = finalBytes.Length, outputHash });
            return new OfficeOutput(finalBytes, outputHash, finalBytes.Length, $"Exported {_touchedParts.Count} touched parts.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            trace.Error(ex);
            throw;
        }
    }

    /// <summary>
    /// Disposes session resources.
    /// </summary>
    /// <returns>No return value.</returns>
    public void Dispose()
    {
        _touchedParts.Clear();
    }
}
