using DocumentFormat.OpenXml.Packaging;
using FileHandler.Api.Common;
using FileHandler.Api.Modules.Office;
using Microsoft.Extensions.Options;

namespace FileHandler.Api.Modules.Excel;

/// <summary>
/// Handles Excel spreadsheet (.xlsx) imports and exports.
/// </summary>
public sealed class ExcelService : ISelectableFileHandler<ExcelSelection>
{

    /// <summary>
    /// Package reader for reading and preflighting ZIP packages.
    /// </summary>
    private readonly OfficePackageReader _reader;

    /// <summary>
    /// Package inspector for analyzing physical inventory and relationships.
    /// </summary>
    private readonly OfficePackageInspector _inspector;

    /// <summary>
    /// Excel extractor for analyzing worksheets, cells, and translation units.
    /// </summary>
    private readonly IExcelExtractor _extractor;

    /// <summary>
    /// Text codec for canonical token encoding and decoding.
    /// </summary>
    private readonly OfficeTextCodec _codec;

    /// <summary>
    /// Translation applier for modifying cell values and SharedStringTable.
    /// </summary>
    private readonly ExcelTranslationApplier _applier;

    /// <summary>
    /// Package validator for schema and preservation checks.
    /// </summary>
    private readonly OfficePackageValidator _packageValidator;

    /// <summary>
    /// Excel structure validator for table columns and formula preservation.
    /// </summary>
    private readonly ExcelStructureValidator _structureValidator;

    /// <summary>
    /// Office processing options.
    /// </summary>
    private readonly OfficeProcessingOptions _options;

    /// <summary>
    /// General file handling limits.
    /// </summary>
    private readonly FileHandlingOptions _fileHandlingOptions;

    /// <summary>
    /// MIME content type for SpreadsheetML documents.
    /// </summary>
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>
    /// Creates Excel document handler service.
    /// </summary>
    /// <param name="reader">Office package reader.</param>
    /// <param name="inspector">Office package inspector.</param>
    /// <param name="extractor">Excel workbook extractor.</param>
    /// <param name="codec">Office text codec.</param>
    /// <param name="applier">Excel translation applier.</param>
    /// <param name="packageValidator">Office package validator.</param>
    /// <param name="structureValidator">Excel structure validator.</param>
    /// <param name="options">Office processing options.</param>
    /// <param name="fileHandlingOptions">General file handling options.</param>
    public ExcelService(
        OfficePackageReader reader,
        OfficePackageInspector inspector,
        IExcelExtractor extractor,
        OfficeTextCodec codec,
        ExcelTranslationApplier applier,
        OfficePackageValidator packageValidator,
        ExcelStructureValidator structureValidator,
        IOptions<OfficeProcessingOptions> options,
        IOptions<FileHandlingOptions> fileHandlingOptions)
    {
        _reader = reader;
        _inspector = inspector;
        _extractor = extractor;
        _codec = codec;
        _applier = applier;
        _packageValidator = packageValidator;
        _structureValidator = structureValidator;
        _options = options.Value;
        _fileHandlingOptions = fileHandlingOptions.Value;
    }

    /// <summary>
    /// Imports validated translation units from caller-owned source.
    /// </summary>
    /// <param name="stream">Caller-owned source stream.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task containing units or validation errors.</returns>
    public Task<ImportResult> ImportAsync(Stream stream, CancellationToken cancellationToken = default) =>
        ImportAsync(stream, new ExcelSelection(null), cancellationToken);

    /// <summary>
    /// Processes explicitly selected native source objects.
    /// </summary>
    /// <param name="stream">Caller-owned source stream.</param>
    /// <param name="selection">Native object selection.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Processing result with complete collected metadata.</returns>
    public Task<ImportResult> ImportAsync(Stream stream, ExcelSelection selection, CancellationToken cancellationToken) =>
        ImportAsync(stream, selection, false, cancellationToken);

    /// <summary>
    /// Imports default visible objects with optional diagnostic mapping.
    /// </summary>
    /// <param name="stream">Caller-owned readable source stream.</param>
    /// <param name="debug">Whether to return informational skips and build diagnostic unit mapping.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Texts, skips and optional public unit mapping, or fatal errors.</returns>
    public Task<ImportResult> ImportAsync(Stream stream, bool debug, CancellationToken cancellationToken) =>
        ImportAsync(stream, new ExcelSelection(null), debug, cancellationToken);

    /// <summary>
    /// Imports selected objects while constructing public unit metadata only when requested.
    /// </summary>
    /// <param name="stream">Caller-owned readable source stream.</param>
    /// <param name="selection">Native object selection.</param>
    /// <param name="debug">Whether to return informational skips and build diagnostic unit mapping.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Texts, skips and optional public unit mapping, or fatal errors.</returns>
    public async Task<ImportResult> ImportAsync(Stream stream, ExcelSelection selection, bool debug, CancellationToken cancellationToken)
    {
        var result = await ImportCoreAsync(stream, selection, debug, cancellationToken).ConfigureAwait(false);
        return result with { Metadata = result.Metadata.ForResponse(debug) };
    }

    /// <summary>
    /// Runs import with complete internal skip facts for preservation validation.
    /// </summary>
    /// <param name="stream">Caller-owned source stream.</param>
    /// <param name="selection">Native object selection.</param>
    /// <param name="debug">Whether to construct diagnostic unit mapping.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Processing result before public skip projection.</returns>
    private async Task<ImportResult> ImportCoreAsync(Stream stream, ExcelSelection selection, bool debug, CancellationToken cancellationToken)
    {
        var metadata = FileMetadata.Create("excel");
        try
        {
            var readResult = await _reader.ReadAsync(new LimitedReadStream(stream, _fileHandlingOptions.MaxFileBytes, "file_too_large"), OfficeFormat.Excel, cancellationToken).ConfigureAwait(false);
            if (readResult.Errors.Count > 0 || readResult.Source is null)
            {
                return new([], readResult.Errors) { Metadata = metadata with { Status = ProcessingStatus.Failed } };
            }

            using var source = readResult.Source;
            if (source.Bytes.Length > _fileHandlingOptions.MaxFileBytes)
            {
                var err = new FileError("file_too_large", ProcessingMessages.FileSizeLimit(source.Bytes.Length, _fileHandlingOptions.MaxFileBytes));
                return new([], [err]) { Metadata = metadata with { Status = ProcessingStatus.Failed } };
            }

            source.IncludeInformationalSkips = debug;
            var inventory = _inspector.Inspect(source, cancellationToken);

            ExcelPlan plan;
            try { plan = _extractor.Analyze(source, inventory, selection, cancellationToken); }
            finally { metadata = source.ProcessingMetadata ?? metadata; }

            metadata = plan.Metadata;
            if (plan.Units.Count > _fileHandlingOptions.MaxUnits)
            {
                var err = new FileError("too_many_units", ProcessingMessages.UnitCountLimit(plan.Units.Count, _fileHandlingOptions.MaxUnits));
                return new([], [err]) { Metadata = metadata with { Status = ProcessingStatus.Failed } };
            }

            var selectedPartUris = metadata.Sheets!.Where(s => s.Selected == true && s.CanImport).Select(s => s.PartUri).ToList();
            var sourceValidation = _packageValidator.ValidateSource(source, selectedPartUris, cancellationToken, metadata.Skipped);
            if (!sourceValidation.IsValid)
            {
                return new([], sourceValidation.Errors) { Metadata = metadata with { Status = ProcessingStatus.Failed } };
            }

            if (debug) metadata = OfficeMetadata.WithUnits(metadata, plan.Units, cancellationToken);
            var texts = plan.Units.Select(u => u.EncodedSource).ToArray();
            return new(texts, []) { Metadata = metadata };
        }
        catch (UnknownSelectionException ex)
        {
            return new([], [new("unknown_selection_id", ex.Message)]) { Metadata = ex.Metadata with { Status = ProcessingStatus.Failed } };
        }
        catch (Exception ex) when (ex is InvalidDataException or System.Xml.XmlException or OpenXmlPackageException or FormatException or OverflowException)
        {
            return new([], [new FileError("invalid_office_package", ProcessingMessages.InvalidOfficePackage)]) { Metadata = metadata with { Status = ProcessingStatus.Failed } };
        }
        catch (FileLimitException ex)
        {
            return new([], [new FileError(ex.Code, ex.Message)]) { Metadata = metadata with { Status = ProcessingStatus.Failed } };
        }
    }

    /// <summary>
    /// Applies validated translations and publishes an atomic output.
    /// </summary>
    /// <param name="stream">Caller-owned source stream.</param>
    /// <param name="translations">Ordered caller translations.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task containing output bytes or validation errors.</returns>
    public Task<ExportResult> ExportAsync(Stream stream, IReadOnlyList<string> translations, CancellationToken cancellationToken = default) =>
        ExportAsync(stream, translations, new ExcelSelection(null), cancellationToken);

    /// <summary>
    /// Processes explicitly selected native source objects.
    /// </summary>
    /// <param name="stream">Caller-owned source stream.</param>
    /// <param name="translations">Translations in selected source order.</param>
    /// <param name="selection">Native object selection.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Processing result with complete collected metadata.</returns>
    public async Task<ExportResult> ExportAsync(Stream stream, IReadOnlyList<string> translations, ExcelSelection selection, CancellationToken cancellationToken)
    {
        var result = await ExportCoreAsync(stream, translations, selection, cancellationToken).ConfigureAwait(false);
        return result with { Metadata = result.Metadata.ForResponse(false) };
    }

    /// <summary>
    /// Runs export with complete internal skip facts for preservation validation.
    /// </summary>
    /// <param name="stream">Caller-owned source stream.</param>
    /// <param name="translations">Translations in source mapping order.</param>
    /// <param name="selection">Native object selection.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Processing result before public skip projection.</returns>
    private async Task<ExportResult> ExportCoreAsync(Stream stream, IReadOnlyList<string> translations, ExcelSelection selection, CancellationToken cancellationToken)
    {
        var metadata = FileMetadata.Create("excel");
        try
        {
            var readResult = await _reader.ReadAsync(new LimitedReadStream(stream, _fileHandlingOptions.MaxFileBytes, "file_too_large"), OfficeFormat.Excel, cancellationToken).ConfigureAwait(false);
            if (readResult.Errors.Count > 0 || readResult.Source is null)
            {
                return new(null, ContentType, readResult.Errors) { Metadata = metadata.ForExport(true) };
            }

            using var source = readResult.Source;
            if (source.Bytes.Length > _fileHandlingOptions.MaxFileBytes)
            {
                var err = new FileError("file_too_large", ProcessingMessages.FileSizeLimit(source.Bytes.Length, _fileHandlingOptions.MaxFileBytes));
                return new(null, ContentType, [err]) { Metadata = metadata.ForExport(true) };
            }

            source.IncludeInformationalSkips = false;
            var inventory = _inspector.Inspect(source, cancellationToken);

            ExcelPlan plan;
            try { plan = _extractor.Analyze(source, inventory, selection, cancellationToken); }
            finally { metadata = source.ProcessingMetadata ?? metadata; }

            metadata = plan.Metadata;
            if (plan.Units.Count > _fileHandlingOptions.MaxUnits)
                throw new FileLimitException("too_many_units");

            var selectedPartUris = metadata.Sheets!.Where(s => s.Selected == true && s.CanImport).Select(s => s.PartUri).ToList();
            var sourceValidation = _packageValidator.ValidateSource(source, selectedPartUris, cancellationToken, metadata.Skipped);
            if (!sourceValidation.IsValid)
            {
                return new(null, ContentType, sourceValidation.Errors) { Metadata = metadata.ForExport(true) };
            }

            var decodeResult = _codec.ValidateAndDecode(plan.Units, translations, OfficeFormat.Excel, cancellationToken);
            if (decodeResult.Errors.Count > 0 || decodeResult.DecodedUnits is null)
            {
                return new(null, ContentType, decodeResult.Errors) { Metadata = metadata.ForExport(true) };
            }

            metadata = metadata.AppendSkipped(decodeResult.Skipped).ForExport();
            var patch = _applier.Prepare(plan, decodeResult.DecodedUnits, cancellationToken);
            using var docMs = new MemoryStream(source.Bytes);
            using var doc = SpreadsheetDocument.Open(docMs, false, OfficeTextBindings.Settings(_options));
            var rename = ExcelRenamePlanner.Prepare(doc, plan, decodeResult.DecodedUnits, translations, patch, cancellationToken);
            metadata = (metadata.AppendSkipped(rename.Skipped) with { SheetNameChanges = rename.Changes }).ForExport();

            var isIdentity = patch.EditMasks.Count == 0;

            if (isIdentity)
            {
                if (source.Bytes.Length > _fileHandlingOptions.MaxOutputBytes)
                {
                    var err = new FileError("output_too_large", ProcessingMessages.OutputSizeLimit);
                    return new(null, ContentType, [err]) { Metadata = metadata.ForExport(true) };
                }

                return new(source.Bytes, ContentType, []) { Metadata = metadata };
            }

            using var session = OfficeExportSession.Create(source, _options, _fileHandlingOptions);
            _applier.Apply(session, doc, patch, cancellationToken);
            ExcelRenamePlanner.Apply(rename, session, cancellationToken);

            var output = await session.FinalizeAsync(cancellationToken).ConfigureAwait(false);
            if (output.OutputBytes > _fileHandlingOptions.MaxOutputBytes)
            {
                var err = new FileError("output_too_large", ProcessingMessages.OutputSizeLimit);
                return new(null, ContentType, [err]) { Metadata = metadata.ForExport(true) };
            }

            var outputValidation = _packageValidator.ValidateOutput(source, output, patch.EditMasks, cancellationToken, metadata.Skipped);
            if (!outputValidation.IsValid)
            {
                return new(null, ContentType, outputValidation.Errors) { Metadata = metadata.ForExport(true) };
            }

            var structureValidation = _structureValidator.Validate(output.Content, plan, cancellationToken);
            if (!structureValidation.IsValid)
            {
                return new(null, ContentType, structureValidation.Errors) { Metadata = metadata.ForExport(true) };
            }

            return new(output.Content, ContentType, []) { Metadata = metadata };
        }
        catch (UnknownSelectionException ex)
        {
            return new(null, ContentType, [new("unknown_selection_id", ex.Message)]) { Metadata = ex.Metadata.ForExport(true) };
        }
        catch (Exception ex) when (ex is InvalidDataException or System.Xml.XmlException or OpenXmlPackageException or FormatException or OverflowException)
        {
            return new(null, ContentType, [new FileError("invalid_office_package", ProcessingMessages.InvalidOfficePackage)]) { Metadata = metadata.ForExport(true) };
        }
        catch (FileLimitException ex)
        {
            return new(null, ContentType, [new FileError(ex.Code, ex.Message)]) { Metadata = metadata.ForExport(true) };
        }
    }

    /// <summary>
    /// Reads native inventory without extracting translation units.
    /// </summary>
    /// <param name="stream">Caller-owned upload stream.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Inventory or fatal source errors.</returns>
    public async Task<SheetsResponse> GetSheetsAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        try
        {
            var read = await _reader.ReadAsync(new LimitedReadStream(stream, _fileHandlingOptions.MaxFileBytes, "file_too_large"), OfficeFormat.Excel, cancellationToken);
            if (read.Source is null || read.Errors.Count > 0)
                return new([], new("excel", ProcessingStatus.Failed, []), read.Errors);
            using var source = read.Source;
            using var bytes = new MemoryStream(source.Bytes);
            using var document = SpreadsheetDocument.Open(bytes, false, OfficeTextBindings.Settings(_options));
            var items = OfficeCatalog.Sheets(document, cancellationToken);
            return new(items, new("excel", ProcessingStatus.Success, []), []);
        }
        catch (FileLimitException ex)
        {
            return new([], new("excel", ProcessingStatus.Failed, []), [new(ex.Code, ex.Message)]);
        }
        catch (Exception ex) when (ex is InvalidDataException or System.Xml.XmlException or OpenXmlPackageException or FormatException or OverflowException)
        {
            return new([], new("excel", ProcessingStatus.Failed, []), [new("invalid_office_package", ProcessingMessages.InvalidOfficePackage)]);
        }
    }

    /// <summary>
    /// Creates Excel service with default dependencies for testing.
    /// </summary>
    /// <param name="fileHandlingOptions">File processing limits.</param>
    /// <param name="officeOptions">Office processing options.</param>
    /// <returns>Configured Excel service instance.</returns>
    public static ExcelService Create(
        IOptions<FileHandlingOptions>? fileHandlingOptions = null,
        IOptions<OfficeProcessingOptions>? officeOptions = null)
    {
        var fOpts = fileHandlingOptions ?? Microsoft.Extensions.Options.Options.Create(new FileHandlingOptions());
        var oOpts = officeOptions ?? Microsoft.Extensions.Options.Options.Create(new OfficeProcessingOptions());
        var reader = new OfficePackageReader(oOpts.Value);
        var inspector = new OfficePackageInspector(oOpts.Value);
        var codec = new OfficeTextCodec(oOpts.Value, fOpts.Value);
        var tableReader = new ExcelTableReader();
        var extractor = new ExcelExtractor(codec, tableReader, oOpts.Value);
        var applier = new ExcelTranslationApplier();
        var pkgValidator = new OfficePackageValidator(oOpts.Value);
        var structValidator = new ExcelStructureValidator();
        return new ExcelService(reader, inspector, extractor, codec, applier, pkgValidator, structValidator, oOpts, fOpts);
    }
}
