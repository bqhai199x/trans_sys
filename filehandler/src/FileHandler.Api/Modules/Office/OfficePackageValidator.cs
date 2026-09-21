using System.IO.Compression;
using System.Security.Cryptography;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using FileHandler.Api.Common;

namespace FileHandler.Api.Modules.Office;

/// <summary>
/// Validates schema correctness and structural preservation of Office packages.
/// </summary>
public sealed class OfficePackageValidator
{

    /// <summary>
    /// Configuration options governing limits.
    /// </summary>
    private readonly OfficeProcessingOptions _options;

    /// <summary>
    /// Creates package validator instance.
    /// </summary>
    /// <param name="options">Active processing options.</param>
    public OfficePackageValidator(OfficeProcessingOptions options)
    {
        _options = options;
    }

    /// <summary>
    /// Validates source document schema and collects baseline validation errors.
    /// </summary>
    /// <param name="source">Source document snapshot.</param>
    /// <param name="selectedPartUris">List of part URIs selected for translation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="skipped">Precisely located preserved source regions.</param>
    /// <returns>Validation result indicating source validity.</returns>
    public OfficeValidationResult ValidateSource(
        OfficeSource source,
        IReadOnlyList<string> selectedPartUris,
        CancellationToken cancellationToken,
        IReadOnlyList<SkipMetadata>? skipped = null)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var package = OpenPackageReadOnly(source.OriginalBytes, source.Format);
        var validator = new OpenXmlValidator(DocumentFormat.OpenXml.FileFormatVersions.Office2019) { MaxNumberOfErrors = _options.MaxSchemaErrors + 1 };
        var errors = validator.Validate(package, cancellationToken).Take(_options.MaxSchemaErrors + 1).ToList();

        var selectedSet = new HashSet<string>(selectedPartUris, StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<OpenXmlPart>(package.Parts.Select(p => p.OpenXmlPart));
        var visited = new HashSet<OpenXmlPart>();
        while (pending.TryPop(out var part))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!visited.Add(part)) continue;
            foreach (var child in part.Parts)
            {
                if (selectedSet.Contains(part.Uri.ToString())) selectedSet.Add(child.OpenXmlPart.Uri.ToString());
                pending.Push(child.OpenXmlPart);
            }
        }
        var fatalErrors = new List<FileError>();
        if (errors.Count > _options.MaxSchemaErrors)
            return OfficeValidationResult.Failure([new FileError("office_schema_limit_exceeded", ProcessingMessages.SchemaValidationLimit)]);

        foreach (var error in errors)
        {
            var partUri = error.Part?.Uri?.ToString() ?? string.Empty;
            var isSelected = selectedSet.Contains(partUri) || string.IsNullOrEmpty(partUri) || error.Part is WorkbookPart or PresentationPart or SharedStringTablePart;

            if (isSelected && !IsPreserved(error, skipped))
            {
                fatalErrors.Add(new FileError("invalid_office_package", ProcessingMessages.SourceSchemaError(partUri, error.Description)));
            }
        }

        if (fatalErrors.Count > 0)
        {
            return OfficeValidationResult.Failure(fatalErrors);
        }

        return OfficeValidationResult.Success();
    }

    /// <summary>
    /// Validates output package schema and verifies payload preservation.
    /// </summary>
    /// <param name="source">Original source document snapshot.</param>
    /// <param name="output">Generated output document payload.</param>
    /// <param name="masks">Allowed modification masks per touched part.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="skipped">Precisely located preserved source regions.</param>
    /// <returns>Validation result for output.</returns>
    public OfficeValidationResult ValidateOutput(
        OfficeSource source,
        OfficeOutput output,
        IReadOnlyDictionary<string, OfficeEditMask> masks,
        CancellationToken cancellationToken,
        IReadOnlyList<SkipMetadata>? skipped = null)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var package = OpenPackageReadOnly(output.Content, source.Format);
        var validator = new OpenXmlValidator(DocumentFormat.OpenXml.FileFormatVersions.Office2019) { MaxNumberOfErrors = _options.MaxSchemaErrors + 1 };
        var errors = validator.Validate(package, cancellationToken).Take(_options.MaxSchemaErrors + 1).ToList();

        using var originalPackage = OpenPackageReadOnly(source.OriginalBytes, source.Format);
        var baseline = validator.Validate(originalPackage, cancellationToken).Take(_options.MaxSchemaErrors + 1).ToList();
        if (baseline.Count > _options.MaxSchemaErrors)
            return OfficeValidationResult.Failure([new FileError("office_schema_limit_exceeded", ProcessingMessages.SchemaValidationLimit)]);
        var baselineCounts = baseline.GroupBy(ErrorKey).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        var touchedSet = new HashSet<string>(masks.Keys, StringComparer.OrdinalIgnoreCase);
        var fatalErrors = new List<FileError>();
        if (errors.Count > _options.MaxSchemaErrors)
            return OfficeValidationResult.Failure([new FileError("office_schema_limit_exceeded", ProcessingMessages.SchemaValidationLimit)]);

        foreach (var error in errors)
        {
            var partUri = error.Part?.Uri?.ToString() ?? string.Empty;
            var key = ErrorKey(error);
            baselineCounts.TryGetValue(key, out var remaining);
            if (remaining > 0) baselineCounts[key] = remaining - 1;
            if (remaining == 0 || touchedSet.Contains(partUri) && !IsPreserved(error, skipped) || string.IsNullOrEmpty(partUri))
            {
                fatalErrors.Add(new FileError("office_output_invalid", ProcessingMessages.OutputSchemaError(partUri, error.Description)));
            }
        }

        // Verify preservation of untouched parts
        using var sourceZip = new ZipArchive(new MemoryStream(source.OriginalBytes), ZipArchiveMode.Read, false);
        using var outputZip = new ZipArchive(new MemoryStream(output.Content), ZipArchiveMode.Read, false);

        var sourceEntries = sourceZip.Entries.ToDictionary(e => "/" + e.FullName.Replace('\\', '/').TrimStart('/'), StringComparer.OrdinalIgnoreCase);
        var outputEntries = outputZip.Entries.ToDictionary(e => "/" + e.FullName.Replace('\\', '/').TrimStart('/'), StringComparer.OrdinalIgnoreCase);

        if (sourceEntries.Count != outputEntries.Count)
        {
            fatalErrors.Add(new FileError("office_output_invalid", ProcessingMessages.OutputEntryCountMismatch(outputEntries.Count, sourceEntries.Count)));
        }

        foreach (var (uri, sourceEntry) in sourceEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!outputEntries.TryGetValue(uri, out var outputEntry))
            {
                fatalErrors.Add(new FileError("office_output_invalid", ProcessingMessages.MissingOutputPart(uri)));
                continue;
            }

            if (touchedSet.Contains(uri) && !OfficeXmlInvariant.Matches(sourceEntry, outputEntry, masks[uri], _options))
                fatalErrors.Add(new FileError("office_output_invalid", ProcessingMessages.ProtectedXmlChanged));

            if (!touchedSet.Contains(uri))
            {
                // Must have identical CRC32 and payload hash
                using var sourcePayload = sourceEntry.Open();
                using var outputPayload = outputEntry.Open();
                if (!SHA256.HashData(sourcePayload).AsSpan().SequenceEqual(SHA256.HashData(outputPayload)))
                {
                    fatalErrors.Add(new FileError("office_output_invalid", ProcessingMessages.UntouchedPartChanged(uri)));
                }
            }
        }

        if (fatalErrors.Count > 0)
        {
            return OfficeValidationResult.Failure(fatalErrors);
        }

        return OfficeValidationResult.Success();
    }

    /// <summary>
    /// Matches schema findings only inside explicitly preserved extraction subtrees.
    /// </summary>
    /// <param name="error">Located schema finding.</param>
    /// <param name="skipped">Request-local source exclusions.</param>
    /// <returns>True for a finding within an identified unchanged region.</returns>
    private static bool IsPreserved(ValidationErrorInfo error, IReadOnlyList<SkipMetadata>? skipped)
    {
        if (skipped is null || error.Node is not { } node || error.Part is null) return false;
        var root = node;
        while (root.Parent is not null) root = root.Parent;
        var location = OfficeMetadata.Location(new(error.Part.Uri.ToString(), OfficeTextBindings.Path(node))
        {
            Root = new(root.NamespaceUri, root.LocalName, 1)
        });
        var cell = node as DocumentFormat.OpenXml.Spreadsheet.Cell ?? node.Ancestors<DocumentFormat.OpenXml.Spreadsheet.Cell>().FirstOrDefault();
        return location.Path is not null && skipped.Any(s => s.Stage == SkipStage.Extraction && s.Location.PartUri == location.PartUri &&
            (s.Location.Path is { } path && (location.Path == path || location.Path.StartsWith(path + "/", StringComparison.Ordinal)) ||
             s.Scope == SkipScope.Cell && s.Location.CellReference is { } cellReference && cell?.CellReference?.Value == cellReference));
    }

    /// <summary>
    /// Identifies schema findings including location for multiset comparison.
    /// </summary>
    /// <param name="error">Schema validator finding.</param>
    /// <returns>Stable comparison key retained only in memory.</returns>
    private static string ErrorKey(ValidationErrorInfo error) =>
        $"{error.Id}|{error.Part?.Uri}|{error.Path?.XPath}|{error.Description}";

    /// <summary>
    /// Opens typed OpenXmlPackage in read-only mode with AutoSave disabled.
    /// </summary>
    /// <param name="bytes">Package bytes.</param>
    /// <param name="format">Office document format.</param>
    /// <returns>Opened OpenXmlPackage.</returns>
    private OpenXmlPackage OpenPackageReadOnly(byte[] bytes, OfficeFormat format)
    {
        var ms = new MemoryStream(bytes);
        var settings = OfficeTextBindings.Settings(_options);

        return format switch
        {
            OfficeFormat.Word => WordprocessingDocument.Open(ms, false, settings),
            OfficeFormat.Excel => SpreadsheetDocument.Open(ms, false, settings),
            OfficeFormat.PowerPoint => PresentationDocument.Open(ms, false, settings),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
    }
}
