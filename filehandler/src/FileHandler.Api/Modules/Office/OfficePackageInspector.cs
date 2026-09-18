using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml;
using FileHandler.Api.Common;
using FileHandler.Api.Diagnostics;

namespace FileHandler.Api.Modules.Office;

/// <summary>
/// Inspects physical inventory and relationship graph of Office package.
/// </summary>
public sealed class OfficePackageInspector
{

    /// <summary>
    /// Configuration options governing inventory limits.
    /// </summary>
    private readonly OfficeProcessingOptions _options;

    /// <summary>
    /// Creates package inspector instance.
    /// </summary>
    /// <param name="options">Active processing options.</param>
    public OfficePackageInspector(OfficeProcessingOptions options)
    {
        _options = options;
    }

    /// <summary>
    /// Inspects physical parts and relationship graph of Office package source.
    /// </summary>
    /// <param name="source">Source Office document snapshot.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Populated physical package inventory.</returns>
    /// <exception cref="InvalidOperationException">Package structure or relationship graph violates limits or integrity.</exception>
    public OfficeInventory Inspect(OfficeSource source, CancellationToken cancellationToken)
    {
        using var trace = DebugTrace.Enter("OfficePackageInspector", "Inspect", () => new
        {
            sourceHash = source.SourceHash,
            format = source.Format.ToString()
        });

        try
        {
            trace.State("stage", () => "indexParts");
            using var zip = new ZipArchive(new MemoryStream(source.OriginalBytes), ZipArchiveMode.Read, false);

            var contentTypes = ParseContentTypes(zip);
            var parts = new List<OfficePartInfo>();
            var partIndex = 0;

            foreach (var entry in zip.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.FullName.EndsWith('/'))
                    continue;

                partIndex++;
                var canonicalUri = "/" + entry.FullName.Replace('\\', '/').TrimStart('/');
                using var partItem = DebugTrace.Item(partIndex);

                using var payload = entry.Open();
                var payloadHash = Convert.ToHexStringLower(SHA256.HashData(payload));
                var contentType = ResolveContentType(canonicalUri, contentTypes);
                var sdkKind = DetermineSdkKind(canonicalUri, contentType);

                var partInfo = new OfficePartInfo(
                    canonicalUri,
                    contentType,
                    sdkKind,
                    entry.CompressedLength,
                    entry.Length,
                    payloadHash,
                    Selected: false);

                parts.Add(partInfo);
            }

            trace.State("stage", () => "resolveRelationships");
            var relationships = new List<OfficeRelationship>();
            var relKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in zip.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = entry.FullName.Replace('\\', '/').TrimStart('/');
                if (!name.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
                    continue;

                var ownerPartUri = DetermineOwnerPartUri(name);
                using var relStream = entry.Open();
                var relDoc = new XmlDocument { XmlResolver = null };
                relDoc.Load(relStream);

                foreach (XmlNode node in relDoc.GetElementsByTagName("Relationship"))
                {
                    var id = node.Attributes?["Id"]?.Value;
                    var type = node.Attributes?["Type"]?.Value;
                    var target = node.Attributes?["Target"]?.Value;
                    var targetMode = node.Attributes?["TargetMode"]?.Value ?? "Internal";

                    if (id is null || type is null || target is null)
                        continue;

                    var key = $"{ownerPartUri}|{id}";
                    if (!relKeys.Add(key))
                        throw new InvalidDataException("Duplicate relationship identifier.");

                    string resolvedTarget;
                    if (string.Equals(targetMode, "External", StringComparison.OrdinalIgnoreCase))
                    {
                        resolvedTarget = target;
                    }
                    else
                    {
                        resolvedTarget = ResolveRelativeUri(ownerPartUri, target);
                    }

                    relationships.Add(new OfficeRelationship(ownerPartUri, id, type, resolvedTarget, targetMode));
                    if (relationships.Count > _options.MaxRelationships)
                        throw new FileHandler.Api.Common.FileLimitException("office_package_limit_exceeded");
                }
            }

            trace.State("stage", () => "inventoryObjects");
            var objects = new List<OfficeObject>();
            var diagnostics = new List<OfficeDiagnostic>();

            trace.Return(new
            {
                outcome = "success",
                partsCount = parts.Count,
                relationshipsCount = relationships.Count,
                objectsCount = objects.Count
            });

            return new OfficeInventory(parts, relationships, objects, diagnostics);
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
    /// Parses content types map from [Content_Types].xml.
    /// </summary>
    /// <param name="zip">ZIP archive.</param>
    /// <returns>Pair of default extension mappings and override part mappings.</returns>
    private static (Dictionary<string, string> Defaults, Dictionary<string, string> Overrides) ParseContentTypes(ZipArchive zip)
    {
        var defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var entry = zip.GetEntry("[Content_Types].xml");
        if (entry is null)
            return (defaults, overrides);

        using var s = entry.Open();
        var doc = new XmlDocument { XmlResolver = null };
        doc.Load(s);

        foreach (XmlNode node in doc.GetElementsByTagName("Default"))
        {
            var ext = node.Attributes?["Extension"]?.Value;
            var ct = node.Attributes?["ContentType"]?.Value;
            if (ext is not null && ct is not null)
                defaults[ext] = ct;
        }

        foreach (XmlNode node in doc.GetElementsByTagName("Override"))
        {
            var partName = node.Attributes?["PartName"]?.Value;
            var ct = node.Attributes?["ContentType"]?.Value;
            if (partName is not null && ct is not null)
            {
                var canonical = "/" + partName.Replace('\\', '/').TrimStart('/');
                overrides[canonical] = ct;
            }
        }

        return (defaults, overrides);
    }

    /// <summary>
    /// Resolves MIME content type for part URI.
    /// </summary>
    /// <param name="canonicalUri">Part URI.</param>
    /// <param name="contentTypes">Parsed content types tables.</param>
    /// <returns>Resolved MIME content type.</returns>
    private static string ResolveContentType(string canonicalUri, (Dictionary<string, string> Defaults, Dictionary<string, string> Overrides) contentTypes)
    {
        if (contentTypes.Overrides.TryGetValue(canonicalUri, out var overrideType))
            return overrideType;

        var ext = Path.GetExtension(canonicalUri).TrimStart('.');
        if (contentTypes.Defaults.TryGetValue(ext, out var defaultType))
            return defaultType;

        return "application/octet-stream";
    }

    /// <summary>
    /// Determines owning part URI for .rels part.
    /// </summary>
    /// <param name="relsName">Relative path to .rels part.</param>
    /// <returns>Canonical URI of owner part, or empty string for root.</returns>
    private static string DetermineOwnerPartUri(string relsName)
    {
        if (string.Equals(relsName, "_rels/.rels", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        // E.g. word/_rels/document.xml.rels -> /word/document.xml
        var normalized = "/" + relsName.Replace('\\', '/').TrimStart('/');
        var idx = normalized.LastIndexOf("/_rels/", StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return string.Empty;

        var folder = normalized[..idx];
        var fileWithRels = normalized[(idx + 7)..];
        var targetFile = fileWithRels.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)
            ? fileWithRels[..^5]
            : fileWithRels;

        return folder + "/" + targetFile;
    }

    /// <summary>
    /// Resolves relative target URI against owner part URI.
    /// </summary>
    /// <param name="ownerPartUri">Owner part canonical URI.</param>
    /// <param name="target">Target relative URI.</param>
    /// <returns>Resolved canonical part URI.</returns>
    private static string ResolveRelativeUri(string ownerPartUri, string target)
    {
        if (target.StartsWith('/'))
            return target;

        var ownerDir = string.IsNullOrEmpty(ownerPartUri)
            ? "/"
            : Path.GetDirectoryName(ownerPartUri)?.Replace('\\', '/') ?? "/";

        if (!ownerDir.EndsWith('/'))
            ownerDir += "/";

        var combined = ownerDir + target;
        var parts = combined.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var stack = new List<string>();
        foreach (var seg in parts)
        {
            if (seg == ".")
                continue;
            if (seg == "..")
            {
                if (stack.Count > 0)
                    stack.RemoveAt(stack.Count - 1);
            }
            else
            {
                stack.Add(seg);
            }
        }
        return "/" + string.Join("/", stack);
    }

    /// <summary>
    /// Maps part URI and content type to Open XML SDK part name.
    /// </summary>
    /// <param name="canonicalUri">Part URI.</param>
    /// <param name="contentType">Part content type.</param>
    /// <returns>Descriptive SDK part type.</returns>
    private static string DetermineSdkKind(string canonicalUri, string contentType)
    {
        if (contentType.Contains("wordprocessingml.document.main")) return "MainDocumentPart";
        if (contentType.Contains("spreadsheetml.sheet.main")) return "WorkbookPart";
        if (contentType.Contains("spreadsheetml.worksheet")) return "WorksheetPart";
        if (contentType.Contains("spreadsheetml.chartsheet")) return "ChartsheetPart";
        if (contentType.Contains("spreadsheetml.sharedStrings")) return "SharedStringTablePart";
        if (contentType.Contains("presentationml.presentation.main")) return "PresentationPart";
        if (contentType.Contains("presentationml.slide+xml")) return "SlidePart";
        if (contentType.Contains("drawingml")) return "DrawingsPart";
        return Path.GetFileName(canonicalUri);
    }
}
