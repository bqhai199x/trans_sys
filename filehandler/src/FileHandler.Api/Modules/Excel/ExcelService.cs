using DocumentFormat.OpenXml.Packaging;
using FileHandler.Api.Common;
using FileHandler.Api.Modules.Office;
using Microsoft.Extensions.Options;

namespace FileHandler.Api.Modules.Excel;

/// <summary>
/// Handles Excel spreadsheet (.xlsx) imports and exports.
/// </summary>
public sealed class ExcelService : IFileHandler
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
    public async Task<ImportResult> ImportAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        try
        {
            var readResult = await _reader.ReadAsync(new LimitedReadStream(stream, _fileHandlingOptions.MaxFileBytes, "file_too_large"), OfficeFormat.Excel, cancellationToken).ConfigureAwait(false);
            if (readResult.Errors.Count > 0 || readResult.Source is null)
            {
                return new([], readResult.Errors);
            }

            using var source = readResult.Source;
            if (source.OriginalBytes.Length > _fileHandlingOptions.MaxFileBytes)
            {
                var err = new FileError("file_too_large", $"Kích thước tệp ({source.OriginalBytes.Length} bytes) vượt quá giới hạn ({_fileHandlingOptions.MaxFileBytes} bytes).");
                return new([], [err]);
            }

            var inventory = _inspector.Inspect(source, cancellationToken);

            ExcelPlan plan;
            try
            {
                plan = _extractor.Analyze(source, inventory, cancellationToken);
            }
            catch (InvalidOperationException ex) when (ex is not FileLimitException)
            {
                var err = new FileError("office_unsupported_content", ex.Message);
                return new([], [err]);
            }

            if (plan.Units.Count > _fileHandlingOptions.MaxUnits)
            {
                var err = new FileError("too_many_units", $"Số lượng đơn vị dịch ({plan.Units.Count}) vượt quá giới hạn ({_fileHandlingOptions.MaxUnits}).");
                return new([], [err]);
            }

            var selectedPartUris = plan.Sheets.Where(s => s.State == "Visible").Select(s => s.PartUri).ToList();
            var sourceValidation = _packageValidator.ValidateSource(source, selectedPartUris, cancellationToken);
            if (!sourceValidation.IsValid)
            {
                return new([], sourceValidation.Errors);
            }

            var texts = plan.Units.Select(u => u.EncodedSource).ToArray();
            return new(texts, []);
        }
        catch (InvalidDataException)
        {
            return new([], [new FileError("invalid_office_package", "Cấu trúc gói Office không hợp lệ.")]);
        }
        catch (FileLimitException ex)
        {
            return new([], [new FileError(ex.Code, ex.Message)]);
        }
    }

    /// <summary>
    /// Applies validated translations and publishes an atomic output.
    /// </summary>
    /// <param name="stream">Caller-owned source stream.</param>
    /// <param name="translations">Ordered caller translations.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task containing output bytes or validation errors.</returns>
    public async Task<ExportResult> ExportAsync(Stream stream, IReadOnlyList<string> translations, CancellationToken cancellationToken = default)
    {
        try
        {
            var readResult = await _reader.ReadAsync(new LimitedReadStream(stream, _fileHandlingOptions.MaxFileBytes, "file_too_large"), OfficeFormat.Excel, cancellationToken).ConfigureAwait(false);
            if (readResult.Errors.Count > 0 || readResult.Source is null)
            {
                return new(null, ContentType, readResult.Errors);
            }

            using var source = readResult.Source;
            if (source.OriginalBytes.Length > _fileHandlingOptions.MaxFileBytes)
            {
                var err = new FileError("file_too_large", $"Kích thước tệp ({source.OriginalBytes.Length} bytes) vượt quá giới hạn ({_fileHandlingOptions.MaxFileBytes} bytes).");
                return new(null, ContentType, [err]);
            }

            var inventory = _inspector.Inspect(source, cancellationToken);

            ExcelPlan plan;
            try
            {
                plan = _extractor.Analyze(source, inventory, cancellationToken);
            }
            catch (InvalidOperationException ex) when (ex is not FileLimitException)
            {
                var err = new FileError("office_unsupported_content", ex.Message);
                return new(null, ContentType, [err]);
            }

            if (plan.Units.Count > _fileHandlingOptions.MaxUnits)
                throw new FileLimitException("too_many_units");

            var selectedPartUris = plan.Sheets.Where(s => s.State == "Visible").Select(s => s.PartUri).ToList();
            var sourceValidation = _packageValidator.ValidateSource(source, selectedPartUris, cancellationToken);
            if (!sourceValidation.IsValid)
            {
                return new(null, ContentType, sourceValidation.Errors);
            }

            var decodeResult = _codec.ValidateAndDecode(plan.Units, translations, OfficeFormat.Excel, cancellationToken);
            if (decodeResult.Errors.Count > 0 || decodeResult.DecodedUnits is null)
            {
                return new(null, ContentType, decodeResult.Errors);
            }

            var patch = _applier.Prepare(plan, decodeResult.DecodedUnits, cancellationToken);

            var isIdentity = true;
            for (var i = 0; i < plan.Units.Count; i++)
            {
                if (!string.Equals(plan.Units[i].EncodedSource, translations[i], StringComparison.Ordinal))
                {
                    isIdentity = false;
                    break;
                }
            }

            if (isIdentity)
            {
                if (source.OriginalBytes.Length > _fileHandlingOptions.MaxOutputBytes)
                {
                    var err = new FileError("output_too_large", "Kích thước tệp vượt quá giới hạn đầu ra cho phép.");
                    return new(null, ContentType, [err]);
                }

                return new(source.OriginalBytes, ContentType, []);
            }

            using var session = OfficeExportSession.Create(source, _options, _fileHandlingOptions);
            using (var docMs = new MemoryStream(source.OriginalBytes))
            using (var doc = SpreadsheetDocument.Open(docMs, false, OfficeTextBindings.Settings(_options)))
            {
                _applier.Apply(session, doc, patch, cancellationToken);
            }

            var output = await session.FinalizeAsync(cancellationToken).ConfigureAwait(false);
            if (output.OutputBytes > _fileHandlingOptions.MaxOutputBytes)
            {
                var err = new FileError("output_too_large", "Kích thước tệp vượt quá giới hạn đầu ra cho phép.");
                return new(null, ContentType, [err]);
            }

            var outputValidation = _packageValidator.ValidateOutput(source, output, patch.EditMasks, cancellationToken);
            if (!outputValidation.IsValid)
            {
                return new(null, ContentType, outputValidation.Errors);
            }

            var structureValidation = _structureValidator.Validate(output.Content, plan, cancellationToken);
            if (!structureValidation.IsValid)
            {
                return new(null, ContentType, structureValidation.Errors);
            }

            return new(output.Content, ContentType, []);
        }
        catch (InvalidDataException)
        {
            return new(null, ContentType, [new FileError("invalid_office_package", "Cấu trúc gói Office không hợp lệ.")]);
        }
        catch (FileLimitException ex)
        {
            return new(null, ContentType, [new FileError(ex.Code, ex.Message)]);
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
