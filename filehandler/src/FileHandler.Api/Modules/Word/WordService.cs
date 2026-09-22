using DocumentFormat.OpenXml.Packaging;
using FileHandler.Api.Common;
using FileHandler.Api.Modules.Office;
using Microsoft.Extensions.Options;

namespace FileHandler.Api.Modules.Word;

/// <summary>
/// Handles Word document (.docx) imports and exports.
/// </summary>
public sealed class WordService : IFileHandler
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
    /// Word extractor for analyzing stories and translation units.
    /// </summary>
    private readonly IWordExtractor _extractor;

    /// <summary>
    /// Text codec for canonical token encoding and decoding.
    /// </summary>
    private readonly OfficeTextCodec _codec;

    /// <summary>
    /// Translation applier for modifying Word document parts.
    /// </summary>
    private readonly WordTranslationApplier _applier;

    /// <summary>
    /// Package validator for schema and preservation checks.
    /// </summary>
    private readonly OfficePackageValidator _packageValidator;

    /// <summary>
    /// Word structure validator for table and topology invariants.
    /// </summary>
    private readonly WordStructureValidator _structureValidator;

    /// <summary>
    /// Office processing options.
    /// </summary>
    private readonly OfficeProcessingOptions _options;

    /// <summary>
    /// General file handling limits.
    /// </summary>
    private readonly FileHandlingOptions _fileHandlingOptions;

    /// <summary>
    /// MIME content type for WordprocessingML documents.
    /// </summary>
    public const string ContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    /// <summary>
    /// Creates Word document handler service.
    /// </summary>
    /// <param name="reader">Office package reader.</param>
    /// <param name="inspector">Office package inspector.</param>
    /// <param name="extractor">Word document extractor.</param>
    /// <param name="codec">Office text codec.</param>
    /// <param name="applier">Word translation applier.</param>
    /// <param name="packageValidator">Office package validator.</param>
    /// <param name="structureValidator">Word structure validator.</param>
    /// <param name="options">Office processing options.</param>
    /// <param name="fileHandlingOptions">General file handling options.</param>
    public WordService(
        OfficePackageReader reader,
        OfficePackageInspector inspector,
        IWordExtractor extractor,
        OfficeTextCodec codec,
        WordTranslationApplier applier,
        OfficePackageValidator packageValidator,
        WordStructureValidator structureValidator,
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
        ImportAsync(stream, false, cancellationToken);

    /// <summary>
    /// Imports source while constructing public unit metadata only when requested.
    /// </summary>
    /// <param name="stream">Caller-owned readable source stream.</param>
    /// <param name="debug">Whether to return informational skips and build diagnostic unit mapping.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Texts, skips and optional public unit mapping, or fatal errors.</returns>
    public async Task<ImportResult> ImportAsync(Stream stream, bool debug, CancellationToken cancellationToken)
    {
        var result = await ImportCoreAsync(stream, debug, cancellationToken).ConfigureAwait(false);
        return result with { Metadata = result.Metadata.ForResponse(debug) };
    }

    /// <summary>
    /// Runs import with complete internal skip facts for preservation validation.
    /// </summary>
    /// <param name="stream">Caller-owned source stream.</param>
    /// <param name="debug">Whether to construct diagnostic unit mapping.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Processing result before public skip projection.</returns>
    private async Task<ImportResult> ImportCoreAsync(Stream stream, bool debug, CancellationToken cancellationToken)
    {
        var metadata = FileMetadata.Create("word");
        try
        {
            var readResult = await _reader.ReadAsync(new LimitedReadStream(stream, _fileHandlingOptions.MaxFileBytes, "file_too_large"), OfficeFormat.Word, cancellationToken).ConfigureAwait(false);
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

            WordPlan plan;
            try { plan = _extractor.Analyze(source, inventory, cancellationToken); }
            finally { metadata = source.ProcessingMetadata ?? metadata; }

            metadata = plan.Metadata;
            if (plan.Units.Count > _fileHandlingOptions.MaxUnits)
            {
                var err = new FileError("too_many_units", ProcessingMessages.UnitCountLimit(plan.Units.Count, _fileHandlingOptions.MaxUnits));
                return new([], [err]) { Metadata = metadata with { Status = ProcessingStatus.Failed } };
            }

            var selectedPartUris = plan.Stories.Select(s => s.PartUri).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var sourceValidation = _packageValidator.ValidateSource(source, selectedPartUris, cancellationToken, metadata.Skipped);
            if (!sourceValidation.IsValid)
            {
                return new([], sourceValidation.Errors) { Metadata = metadata with { Status = ProcessingStatus.Failed } };
            }

            if (debug) metadata = OfficeMetadata.WithUnits(metadata, plan.Units, cancellationToken);
            var texts = plan.Units.Select(u => u.EncodedSource).ToArray();
            return new(texts, []) { Metadata = metadata };
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
    public async Task<ExportResult> ExportAsync(Stream stream, IReadOnlyList<string> translations, CancellationToken cancellationToken = default)
    {
        var result = await ExportCoreAsync(stream, translations, cancellationToken).ConfigureAwait(false);
        return result with { Metadata = result.Metadata.ForResponse(false) };
    }

    /// <summary>
    /// Runs export with complete internal skip facts for preservation validation.
    /// </summary>
    /// <param name="stream">Caller-owned source stream.</param>
    /// <param name="translations">Translations in source mapping order.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Processing result before public skip projection.</returns>
    private async Task<ExportResult> ExportCoreAsync(Stream stream, IReadOnlyList<string> translations, CancellationToken cancellationToken)
    {
        var metadata = FileMetadata.Create("word");
        try
        {
            var readResult = await _reader.ReadAsync(new LimitedReadStream(stream, _fileHandlingOptions.MaxFileBytes, "file_too_large"), OfficeFormat.Word, cancellationToken).ConfigureAwait(false);
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

            WordPlan plan;
            try { plan = _extractor.Analyze(source, inventory, cancellationToken); }
            finally { metadata = source.ProcessingMetadata ?? metadata; }

            metadata = plan.Metadata;
            if (plan.Units.Count > _fileHandlingOptions.MaxUnits)
                throw new FileLimitException("too_many_units");

            var selectedPartUris = plan.Stories.Select(s => s.PartUri).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var sourceValidation = _packageValidator.ValidateSource(source, selectedPartUris, cancellationToken, metadata.Skipped);
            if (!sourceValidation.IsValid)
            {
                return new(null, ContentType, sourceValidation.Errors) { Metadata = metadata.ForExport(true) };
            }

            var decodeResult = _codec.ValidateAndDecode(plan.Units, translations, OfficeFormat.Word, cancellationToken);
            if (decodeResult.Errors.Count > 0 || decodeResult.DecodedUnits is null)
            {
                return new(null, ContentType, decodeResult.Errors) { Metadata = metadata.ForExport(true) };
            }

            metadata = metadata.AppendSkipped(decodeResult.Skipped).ForExport();
            var patch = _applier.Prepare(plan, decodeResult.DecodedUnits, cancellationToken);

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
            using (var docMs = new MemoryStream(source.Bytes))
            using (var doc = WordprocessingDocument.Open(docMs, false, OfficeTextBindings.Settings(_options)))
            {
                _applier.Apply(session, doc, patch, cancellationToken);
            }

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
    /// Creates Word service with default dependencies for testing.
    /// </summary>
    /// <param name="fileHandlingOptions">File processing limits.</param>
    /// <param name="officeOptions">Office processing options.</param>
    /// <returns>Configured Word service instance.</returns>
    public static WordService Create(
        IOptions<FileHandlingOptions>? fileHandlingOptions = null,
        IOptions<OfficeProcessingOptions>? officeOptions = null)
    {
        var fOpts = fileHandlingOptions ?? Microsoft.Extensions.Options.Options.Create(new FileHandlingOptions());
        var oOpts = officeOptions ?? Microsoft.Extensions.Options.Options.Create(new OfficeProcessingOptions());
        var reader = new OfficePackageReader(oOpts.Value);
        var inspector = new OfficePackageInspector(oOpts.Value);
        var codec = new OfficeTextCodec(oOpts.Value, fOpts.Value);
        var tableReader = new WordTableReader();
        var extractor = new WordExtractor(codec, tableReader, oOpts.Value);
        var applier = new WordTranslationApplier();
        var pkgValidator = new OfficePackageValidator(oOpts.Value);
        var structValidator = new WordStructureValidator();
        return new WordService(reader, inspector, extractor, codec, applier, pkgValidator, structValidator, oOpts, fOpts);
    }
}
