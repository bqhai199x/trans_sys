using System.Buffers;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using FileHandler.Api.Common;

namespace FileHandler.Api.Modules.Office;

/// <summary>
/// Reads, validates, and preflights Office ZIP packages.
/// </summary>
public sealed class OfficePackageReader
{

    /// <summary>
    /// Configuration options governing limits.
    /// </summary>
    private readonly OfficeProcessingOptions _options;

    /// <summary>
    /// Precomputed CRC-32 lookup table using reflected polynomial 0xEDB88320.
    /// </summary>
    private static readonly uint[] CrcTable = InitializeCrcTable();

    /// <summary>
    /// Initializes precomputed CRC-32 table.
    /// </summary>
    /// <returns>Populated 256-entry CRC table.</returns>
    private static uint[] InitializeCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var entry = i;
            for (var j = 0; j < 8; j++)
                entry = (entry & 1) != 0 ? (entry >> 1) ^ 0xEDB88320u : entry >> 1;
            table[i] = entry;
        }
        return table;
    }

    /// <summary>
    /// Creates package reader instance.
    /// </summary>
    /// <param name="options">Active processing options.</param>
    public OfficePackageReader(OfficeProcessingOptions options)
    {
        _options = options;
    }

    /// <summary>
    /// Reads and preflights Office package from input stream.
    /// </summary>
    /// <param name="sourceStream">Input document stream.</param>
    /// <param name="expectedFormat">Expected Office document format.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task producing read result.</returns>
    public async Task<OfficeReadResult> ReadAsync(
        Stream sourceStream,
        OfficeFormat expectedFormat,
        CancellationToken cancellationToken)
    {
        try
        {
            using var ms = new MemoryStream();
            await sourceStream.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
            var bytes = ms.ToArray();

            if (bytes.Length < 4)
                return OfficeReadResult.Failure([new FileError("invalid_office_package", ProcessingMessages.InvalidFileSize)]);

            // Check for OLE compound document magic: D0 CF 11 E0 A1 B1 1A E1
            if (bytes.Length >= 8 && bytes[0] == 0xD0 && bytes[1] == 0xCF && bytes[2] == 0x11 && bytes[3] == 0xE0)
                return OfficeReadResult.Failure([new FileError("office_unsupported_content", ProcessingMessages.UnsupportedOlePackage)]);

            // Check ZIP magic: PK\x03\x04
            if (bytes[0] != 0x50 || bytes[1] != 0x4B || (bytes[2] != 0x03 && bytes[2] != 0x05 && bytes[2] != 0x07))
                return OfficeReadResult.Failure([new FileError("invalid_office_package", ProcessingMessages.InvalidZipFormat)]);

            var sourceHash = Convert.ToHexStringLower(SHA256.HashData(bytes));

            ZipArchive zip;
            try
            {
                zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read, false);
            }
            catch (Exception)
            {
                return OfficeReadResult.Failure([new FileError("invalid_office_package", ProcessingMessages.UnreadableZip)]);
            }

            using (zip)
            {
                if (zip.Entries.Count > _options.MaxPackageEntries)
                    return OfficeReadResult.Failure([new FileError("office_package_limit_exceeded", ProcessingMessages.PackageEntryLimit(zip.Entries.Count, _options.MaxPackageEntries))]);

                byte[]? contentTypesBytes = null;
                var normalizedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                long totalExpandedBytes = 0;
                long actualExpandedBytes = 0;
                string? mainContentType = null;
                var hasContentTypes = false;
                var hasRootRels = false;
                var hasDigitalSignature = false;
                var isStrictOoxml = false;

                foreach (var entry in zip.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var rawName = entry.FullName;
                    var pathSegments = rawName.TrimEnd('/').Split('/');
                    if (rawName.StartsWith('/') || rawName.Contains('\\') || pathSegments.Any(segment =>
                    {
                        var decoded = Uri.UnescapeDataString(segment);
                        return decoded.Length == 0 || decoded is "." or ".." || decoded.IndexOfAny(['/', '\\', ':']) >= 0;
                    }))
                        return OfficeReadResult.Failure([new FileError("invalid_office_package", "Unsafe package entry path.")]);
                    if (rawName.EndsWith('/')) continue;

                    var normalized = rawName.Replace('\\', '/').TrimStart('/');
                    if (!normalizedPaths.Add(normalized))
                        return OfficeReadResult.Failure([new FileError("invalid_office_package", ProcessingMessages.DuplicatePackageEntry(normalized))]);

                    if (entry.Length > _options.MaxPartBytes)
                        return OfficeReadResult.Failure([new FileError("office_package_limit_exceeded", ProcessingMessages.PartSizeLimit(entry.Length, _options.MaxPartBytes))]);

                    totalExpandedBytes += entry.Length;
                    if (totalExpandedBytes > _options.MaxExpandedBytes)
                        return OfficeReadResult.Failure([new FileError("office_package_limit_exceeded", ProcessingMessages.ExpandedSizeLimit(totalExpandedBytes, _options.MaxExpandedBytes))]);

                    var retain = string.Equals(normalized, "[Content_Types].xml", StringComparison.OrdinalIgnoreCase);
                    try
                    {
                        using var entryStream = entry.Open();
                        var scanned = await ScanEntryAsync(entryStream, Math.Min(_options.MaxPartBytes,
                            _options.MaxExpandedBytes - actualExpandedBytes), retain, cancellationToken).ConfigureAwait(false);
                        actualExpandedBytes += scanned.Bytes;
                        if (scanned.Bytes != entry.Length || scanned.Crc != entry.Crc32)
                            return OfficeReadResult.Failure([new FileError("invalid_office_package", "Entry length or CRC mismatch.")]);
                        if (retain)
                        {
                            hasContentTypes = true;
                            contentTypesBytes = scanned.Content;
                        }
                    }
                    catch (Exception ex) when (ex is InvalidDataException or IOException)
                    {
                        return OfficeReadResult.Failure([new FileError("invalid_office_package", "Invalid compressed entry.")]);
                    }

                    if (string.Equals(normalized, "_rels/.rels", StringComparison.OrdinalIgnoreCase))
                        hasRootRels = true;

                    if (normalized.Contains("digital-signature", StringComparison.OrdinalIgnoreCase) ||
                        normalized.Contains("_xmlsignatures", StringComparison.OrdinalIgnoreCase))
                    {
                        hasDigitalSignature = true;
                    }
                }

                if (!hasContentTypes || !hasRootRels)
                    return OfficeReadResult.Failure([new FileError("invalid_office_package", ProcessingMessages.MissingPackageManifest)]);

                if (hasDigitalSignature)
                    return OfficeReadResult.Failure([new FileError("office_unsupported_content", ProcessingMessages.SignedPackageUnsupported)]);

                long totalXmlNodes = 0;
                long totalXmlElements = 0;
                var xmlReaderSettings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    IgnoreWhitespace = false,
                    MaxCharactersInDocument = _options.MaxXmlCharactersPerPart
                };

                var xmlOverrides = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var xmlExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "xml", "rels" };
                try
                {
                    using var typesReader = XmlReader.Create(new MemoryStream(contentTypesBytes!), xmlReaderSettings);
                    while (typesReader.Read())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (typesReader.AttributeCount > _options.MaxAttributesPerElement)
                            throw new FileLimitException("office_package_limit_exceeded");
                        var mime = typesReader.GetAttribute("ContentType");
                        if (mime is null || !(mime.EndsWith("+xml", StringComparison.OrdinalIgnoreCase) ||
                            mime.Equals("application/xml", StringComparison.OrdinalIgnoreCase) ||
                            mime.Equals("text/xml", StringComparison.OrdinalIgnoreCase))) continue;
                        if (typesReader.LocalName == "Override" && typesReader.GetAttribute("PartName") is { } uri)
                            xmlOverrides.Add(uri.TrimStart('/'));
                        if (typesReader.LocalName == "Default" && typesReader.GetAttribute("Extension") is { } extension)
                            xmlExtensions.Add(extension);
                    }
                }
                catch (XmlException)
                {
                    return OfficeReadResult.Failure([new FileError("invalid_office_package", ProcessingMessages.InvalidContentTypes)]);
                }

                foreach (var entry in zip.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var name = entry.FullName.Replace('\\', '/').TrimStart('/');
                    if (!xmlOverrides.Contains(name) && !xmlExtensions.Contains(Path.GetExtension(name).TrimStart('.')))
                        continue;

                    using var entryStream = entry.Open();
                    using var xmlReader = XmlReader.Create(entryStream, xmlReaderSettings);
                    var nodeCount = 0;
                    var maxDepth = 0;

                    try
                    {
                        while (xmlReader.Read())
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (xmlReader.AttributeCount > _options.MaxAttributesPerElement) throw new FileLimitException("office_package_limit_exceeded");
                            nodeCount++;
                            totalXmlNodes++;
                            if (xmlReader.Depth > maxDepth)
                                maxDepth = xmlReader.Depth;

                            if (maxDepth > _options.MaxXmlDepth)
                                return OfficeReadResult.Failure([new FileError("office_package_limit_exceeded", ProcessingMessages.XmlDepthLimit(maxDepth, _options.MaxXmlDepth))]);

                            if (totalXmlNodes > _options.MaxXmlNodes)
                                return OfficeReadResult.Failure([new FileError("office_package_limit_exceeded", ProcessingMessages.XmlNodeLimit(totalXmlNodes, _options.MaxXmlNodes))]);

                            if (xmlReader.NodeType == XmlNodeType.Element)
                            {
                                if (++totalXmlElements > _options.MaxObjects)
                                    throw new FileLimitException("office_package_limit_exceeded");
                                if (xmlReader.NamespaceURI.Contains("purl.oclc.org/ooxml", StringComparison.OrdinalIgnoreCase) ||
                                    xmlReader.NamespaceURI.Contains("purl.org/dc/elements", StringComparison.OrdinalIgnoreCase) && xmlReader.LocalName == "conformance")
                                {
                                    isStrictOoxml = true;
                                }
                            }
                        }
                    }
                    catch (XmlException ex)
                    {
                        var code = ex.Message.Contains("MaxCharactersInDocument", StringComparison.Ordinal) ? "office_package_limit_exceeded" : "invalid_office_package";
                        return OfficeReadResult.Failure([new FileError(code, ProcessingMessages.InvalidOrOversizedXml)]);
                    }
                }

                if (isStrictOoxml)
                    return OfficeReadResult.Failure([new FileError("office_unsupported_content", ProcessingMessages.StrictOoxmlUnsupported)]);

                // Check content types for main document part
                using (var ctStream = new MemoryStream(contentTypesBytes!))
                {
                    var ctDoc = new XmlDocument { XmlResolver = null };
                    using var ctReader = XmlReader.Create(ctStream, xmlReaderSettings);
                    ctDoc.Load(ctReader);
                    var nsmgr = new XmlNamespaceManager(ctDoc.NameTable);
                    nsmgr.AddNamespace("ct", "http://schemas.openxmlformats.org/package/2006/content-types");
                    var overrideNodes = ctDoc.SelectNodes("//ct:Override", nsmgr);
                    if (overrideNodes != null)
                    {
                        foreach (XmlNode node in overrideNodes)
                        {
                            var ct = node.Attributes?["ContentType"]?.Value;
                            if (ct is not null && (
                                ct.Contains("wordprocessingml.document.main") ||
                                ct.Contains("spreadsheetml.sheet.main") ||
                                ct.Contains("presentationml.presentation.main") ||
                                ct.Contains("spreadsheetml.chartsheet")))
                            {
                                mainContentType = ct;
                                break;
                            }
                        }
                    }

                    if (mainContentType is null)
                    {
                        var defaultNodes = ctDoc.SelectNodes("//ct:Default", nsmgr);
                        if (defaultNodes != null)
                        {
                            foreach (XmlNode node in defaultNodes)
                            {
                                var ct = node.Attributes?["ContentType"]?.Value;
                                if (ct is not null && (
                                    ct.Contains("wordprocessingml.document.main") ||
                                    ct.Contains("spreadsheetml.sheet.main") ||
                                    ct.Contains("presentationml.presentation.main") ||
                                    ct.Contains("spreadsheetml.chartsheet")))
                                {
                                    mainContentType = ct;
                                    break;
                                }
                            }
                        }
                    }
                }

                OfficeFormat? detectedFormat = mainContentType switch
                {
                    var ct when ct != null && ct.Contains("wordprocessingml.document.main") => OfficeFormat.Word,
                    var ct when ct != null && (ct.Contains("spreadsheetml.sheet.main") || ct.Contains("spreadsheetml.chartsheet")) => OfficeFormat.Excel,
                    var ct when ct != null && ct.Contains("presentationml.presentation.main") => OfficeFormat.PowerPoint,
                    _ => null
                };

                if (detectedFormat is null)
                    return OfficeReadResult.Failure([new FileError("invalid_office_package", ProcessingMessages.InvalidMainContentType)]);

                if (detectedFormat != expectedFormat)
                    return OfficeReadResult.Failure([new FileError("office_format_mismatch", ProcessingMessages.FormatMismatch(detectedFormat.ToString(), expectedFormat.ToString()))]);

                var source = OfficeSource.TakeOwnership(bytes, sourceHash, expectedFormat, _options);
                return OfficeReadResult.Success(source);
            }
        }
        catch (FileLimitException ex)
        {
            return OfficeReadResult.Failure([new FileError(ex.Code, ex.Message)]);
        }
    }

    /// <summary>
    /// Streams CRC and actual byte accounting, retaining only required metadata.
    /// </summary>
    /// <param name="input">Decompressed entry stream.</param>
    /// <param name="limit">Remaining expanded byte budget.</param>
    /// <param name="retain">Whether metadata bytes must be retained.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>Checksum, actual size, and optional metadata bytes.</returns>
    private static async Task<(uint Crc, long Bytes, byte[]? Content)> ScanEntryAsync(Stream input, long limit, bool retain, CancellationToken token)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(65536);
        using var metadata = retain ? new MemoryStream() : null;
        long total = 0;
        uint crc = uint.MaxValue;
        try
        {
            while (true)
            {
                var remaining = limit - total;
                var wanted = remaining >= 65536 ? 65536 : (int)remaining + 1;
                var read = await input.ReadAsync(buffer.AsMemory(0, wanted), token).ConfigureAwait(false);
                if (read == 0) return (crc ^ uint.MaxValue, total, metadata?.ToArray());
                if (read > remaining) throw new FileLimitException("office_package_limit_exceeded");
                total += read;
                for (var i = 0; i < read; i++) crc = (crc >> 8) ^ CrcTable[(crc ^ buffer[i]) & 0xff];
                metadata?.Write(buffer, 0, read);
            }
        }
        finally { ArrayPool<byte>.Shared.Return(buffer, clearArray: true); }
    }
}
