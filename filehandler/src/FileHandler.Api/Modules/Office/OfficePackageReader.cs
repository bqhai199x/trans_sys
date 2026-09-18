using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using FileHandler.Api.Common;
using FileHandler.Api.Diagnostics;

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
    /// Computes CRC-32 checksum for byte buffer.
    /// </summary>
    /// <param name="data">Data buffer to process.</param>
    /// <returns>Computed 32-bit CRC checksum.</returns>
    public static uint ComputeCrc32(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (var b in data)
            crc = (crc >> 8) ^ CrcTable[(crc & 0xFF) ^ b];
        return crc ^ 0xFFFFFFFFu;
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
        using var trace = DebugTrace.Enter("OfficePackageReader", "ReadAsync", () => new
        {
            expectedFormat,
            sourceCanRead = sourceStream.CanRead,
            sourceCanSeek = sourceStream.CanSeek
        });

        try
        {
            trace.State("stage", () => "readBytes");
            using var ms = new MemoryStream();
            await sourceStream.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
            var bytes = ms.ToArray();
            trace.State("bytesRead", () => bytes.Length);

            if (bytes.Length < 4)
            {
                trace.Return(new { outcome = "failed", code = "invalid_office_package" });
                return OfficeReadResult.Failure(new[] { new FileError("invalid_office_package", "Kích thước tệp không hợp lệ.") });
            }

            // Check for OLE compound document magic: D0 CF 11 E0 A1 B1 1A E1
            if (bytes.Length >= 8 && bytes[0] == 0xD0 && bytes[1] == 0xCF && bytes[2] == 0x11 && bytes[3] == 0xE0)
            {
                trace.Return(new { outcome = "failed", code = "office_unsupported_content" });
                return OfficeReadResult.Failure(new[] { new FileError("office_unsupported_content", "Định dạng nhị phân cũ (OLE) hoặc mã hóa không được hỗ trợ.") });
            }

            // Check ZIP magic: PK\x03\x04
            if (bytes[0] != 0x50 || bytes[1] != 0x4B || (bytes[2] != 0x03 && bytes[2] != 0x05 && bytes[2] != 0x07))
            {
                trace.Return(new { outcome = "failed", code = "invalid_office_package" });
                return OfficeReadResult.Failure(new[] { new FileError("invalid_office_package", "Tệp không phải là định dạng nén ZIP hợp lệ.") });
            }

            var sourceHash = Convert.ToHexStringLower(SHA256.HashData(bytes));

            trace.State("stage", () => "scanZip");
            ZipArchive zip;
            try
            {
                zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read, false);
            }
            catch (Exception)
            {
                trace.Return(new { outcome = "failed", code = "invalid_office_package" });
                return OfficeReadResult.Failure(new[] { new FileError("invalid_office_package", "Tệp ZIP bị hỏng hoặc không mở được.") });
            }

            using (zip)
            {
                if (zip.Entries.Count > _options.MaxPackageEntries)
                {
                    trace.Return(new { outcome = "failed", code = "office_package_limit_exceeded" });
                    return OfficeReadResult.Failure(new[] { new FileError("office_package_limit_exceeded", $"Số lượng tệp trong gói ({zip.Entries.Count}) vượt quá giới hạn ({_options.MaxPackageEntries}).") });
                }

                byte[]? contentTypesBytes = null;
                var normalizedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                long totalExpandedBytes = 0;
                var entryIndex = 0;
                string? mainContentType = null;
                var hasContentTypes = false;
                var hasRootRels = false;
                var hasDigitalSignature = false;
                var isStrictOoxml = false;

                foreach (var entry in zip.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    entryIndex++;

                    using var entryItem = DebugTrace.Item(entryIndex);
                    entryItem.State("entry", () => new { uri = entry.FullName, compressedSize = entry.Length, uncompressedSize = entry.Length });

                    var rawName = entry.FullName;
                    if (rawName.StartsWith('/') || rawName.StartsWith('\\') || rawName.Contains("../") || rawName.Contains("..\\") || rawName.EndsWith('/'))
                    {
                        if (rawName.EndsWith('/'))
                            continue; // Directory entry
                        trace.Return(new { outcome = "failed", code = "invalid_office_package" });
                        return OfficeReadResult.Failure(new[] { new FileError("invalid_office_package", $"Đường dẫn tệp con không an toàn hoặc chứa ký tự cấm: {rawName}.") });
                    }

                    var normalized = rawName.Replace('\\', '/').TrimStart('/');
                    if (!normalizedPaths.Add(normalized))
                    {
                        trace.Return(new { outcome = "failed", code = "invalid_office_package" });
                        return OfficeReadResult.Failure(new[] { new FileError("invalid_office_package", $"Trùng lặp đường dẫn tệp con trong gói: {normalized}.") });
                    }

                    if (entry.Length > _options.MaxPartBytes)
                    {
                        trace.Return(new { outcome = "failed", code = "office_package_limit_exceeded" });
                        return OfficeReadResult.Failure(new[] { new FileError("office_package_limit_exceeded", $"Kích thước phần ({entry.Length} bytes) vượt quá giới hạn phần ({_options.MaxPartBytes} bytes).") });
                    }

                    totalExpandedBytes += entry.Length;
                    if (totalExpandedBytes > _options.MaxExpandedBytes)
                    {
                        trace.Return(new { outcome = "failed", code = "office_package_limit_exceeded" });
                        return OfficeReadResult.Failure(new[] { new FileError("office_package_limit_exceeded", $"Tổng kích thước giải nén ({totalExpandedBytes} bytes) vượt quá giới hạn ({_options.MaxExpandedBytes} bytes).") });
                    }

                    // Decompress entry into memory and verify CRC32
                    byte[] entryBytes;
                    try
                    {
                        using (var entryStream = entry.Open())
                        using (var entryMs = new MemoryStream())
                        {
                            await entryStream.CopyToAsync(entryMs, cancellationToken).ConfigureAwait(false);
                            entryBytes = entryMs.ToArray();
                        }
                    }
                    catch (Exception ex) when (ex is InvalidDataException or IOException)
                    {
                        trace.Return(new { outcome = "failed", code = "invalid_office_package" });
                        return OfficeReadResult.Failure(new[] { new FileError("invalid_office_package", $"Tệp ZIP hoặc phần tử nén {normalized} bị hỏng.") });
                    }

                    var computedCrc = ComputeCrc32(entryBytes);
                    if (computedCrc != entry.Crc32)
                    {
                        trace.Return(new { outcome = "failed", code = "invalid_office_package" });
                        return OfficeReadResult.Failure(new[] { new FileError("invalid_office_package", $"Mã kiểm tra CRC-32 của tệp con {normalized} không khớp.") });
                    }

                    if (string.Equals(normalized, "[Content_Types].xml", StringComparison.OrdinalIgnoreCase))
                    {
                        hasContentTypes = true;
                        contentTypesBytes = entryBytes;
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
                {
                    trace.Return(new { outcome = "failed", code = "invalid_office_package" });
                    return OfficeReadResult.Failure(new[] { new FileError("invalid_office_package", "Thiếu thành phần bắt buộc [Content_Types].xml hoặc _rels/.rels trong gói.") });
                }

                if (hasDigitalSignature)
                {
                    trace.Return(new { outcome = "failed", code = "office_unsupported_content" });
                    return OfficeReadResult.Failure(new[] { new FileError("office_unsupported_content", "Gói chứa chữ ký số (digital signature) không được hỗ trợ chỉnh sửa.") });
                }

                trace.State("stage", () => "scanXml");
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
                    return OfficeReadResult.Failure([new FileError("invalid_office_package", "Content types XML không hợp lệ.")]);
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
                            nodeCount++;
                            totalXmlNodes++;
                            if (xmlReader.Depth > maxDepth)
                                maxDepth = xmlReader.Depth;

                            if (maxDepth > _options.MaxXmlDepth)
                            {
                                trace.Return(new { outcome = "failed", code = "office_package_limit_exceeded" });
                                return OfficeReadResult.Failure(new[] { new FileError("office_package_limit_exceeded", $"Độ sâu XML ({maxDepth}) vượt quá giới hạn ({_options.MaxXmlDepth}).") });
                            }

                            if (totalXmlNodes > _options.MaxXmlNodes)
                            {
                                trace.Return(new { outcome = "failed", code = "office_package_limit_exceeded" });
                                return OfficeReadResult.Failure(new[] { new FileError("office_package_limit_exceeded", $"Tổng số nút XML ({totalXmlNodes}) vượt quá giới hạn ({_options.MaxXmlNodes}).") });
                            }

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
                        trace.Return(new { outcome = "failed", code = "invalid_office_package" });
                        var code = ex.Message.Contains("MaxCharactersInDocument", StringComparison.Ordinal) ? "office_package_limit_exceeded" : "invalid_office_package";
                        return OfficeReadResult.Failure([new FileError(code, "XML không hợp lệ hoặc vượt giới hạn ký tự.")]);
                    }
                }

                if (isStrictOoxml)
                {
                    trace.Return(new { outcome = "failed", code = "office_unsupported_content" });
                    return OfficeReadResult.Failure(new[] { new FileError("office_unsupported_content", "Định dạng Strict OOXML không được hỗ trợ trong phiên bản office-v1.") });
                }

                trace.State("stage", () => "openPackage");
                // Check content types for main document part
                using (var ctStream = new MemoryStream(contentTypesBytes!))
                {
                    var ctDoc = new XmlDocument { XmlResolver = null };
                    ctDoc.Load(ctStream);
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

                trace.State("detectedFormat", () => detectedFormat?.ToString() ?? "unknown");

                if (detectedFormat is null)
                {
                    trace.Return(new { outcome = "failed", code = "invalid_office_package" });
                    return OfficeReadResult.Failure(new[] { new FileError("invalid_office_package", "Không tìm thấy kiểu nội dung tài liệu chính hợp lệ trong gói.") });
                }

                if (detectedFormat != expectedFormat)
                {
                    trace.Return(new { outcome = "failed", code = "office_format_mismatch" });
                    return OfficeReadResult.Failure(new[] { new FileError("office_format_mismatch", $"Định dạng tệp thực tế ({detectedFormat}) không khớp với phần mở rộng yêu cầu ({expectedFormat}).") });
                }

                var source = new OfficeSource(bytes, sourceHash, expectedFormat, _options);
                trace.Return(new { outcome = "success", bytes = bytes.Length, entryCount = zip.Entries.Count, format = expectedFormat.ToString() });
                return OfficeReadResult.Success(source);
            }
        }
        catch (FileLimitException ex)
        {
            return OfficeReadResult.Failure([new FileError(ex.Code, ex.Message)]);
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
}
