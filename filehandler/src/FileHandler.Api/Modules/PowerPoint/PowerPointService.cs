using DocumentFormat.OpenXml.Packaging;
using FileHandler.Api.Common;
using FileHandler.Api.Diagnostics;
using FileHandler.Api.Modules.Office;
using Microsoft.Extensions.Options;

namespace FileHandler.Api.Modules.PowerPoint;

/// <summary>
/// Handles PowerPoint presentation (.pptx) imports and exports.
/// </summary>
public sealed class PowerPointService : IFileHandler
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
    /// PowerPoint extractor for analyzing slides, shapes, and translation units.
    /// </summary>
    private readonly IPowerPointExtractor _extractor;

    /// <summary>
    /// Text codec for canonical token encoding and decoding.
    /// </summary>
    private readonly OfficeTextCodec _codec;

    /// <summary>
    /// Translation applier for modifying shape text and tables.
    /// </summary>
    private readonly PowerPointTranslationApplier _applier;

    /// <summary>
    /// Package validator for schema and preservation checks.
    /// </summary>
    private readonly OfficePackageValidator _packageValidator;

    /// <summary>
    /// PowerPoint structure validator for slide and shape invariants.
    /// </summary>
    private readonly PowerPointStructureValidator _structureValidator;

    /// <summary>
    /// Office processing options.
    /// </summary>
    private readonly OfficeProcessingOptions _options;

    /// <summary>
    /// General file handling limits.
    /// </summary>
    private readonly FileHandlingOptions _fileHandlingOptions;

    /// <summary>
    /// MIME content type for PresentationML documents.
    /// </summary>
    public const string ContentType = "application/vnd.openxmlformats-officedocument.presentationml.presentation";

    /// <summary>
    /// Creates PowerPoint document handler service.
    /// </summary>
    /// <param name="reader">Office package reader.</param>
    /// <param name="inspector">Office package inspector.</param>
    /// <param name="extractor">PowerPoint presentation extractor.</param>
    /// <param name="codec">Office text codec.</param>
    /// <param name="applier">PowerPoint translation applier.</param>
    /// <param name="packageValidator">Office package validator.</param>
    /// <param name="structureValidator">PowerPoint structure validator.</param>
    /// <param name="options">Office processing options.</param>
    /// <param name="fileHandlingOptions">General file handling options.</param>
    public PowerPointService(
        OfficePackageReader reader,
        OfficePackageInspector inspector,
        IPowerPointExtractor extractor,
        OfficeTextCodec codec,
        PowerPointTranslationApplier applier,
        OfficePackageValidator packageValidator,
        PowerPointStructureValidator structureValidator,
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
        using var trace = DebugTrace.Enter("PowerPointService", "ImportAsync", () => new
        {
            format = "PowerPoint",
            profile = "office-v1",
            sourceCanRead = stream.CanRead,
            sourceCanSeek = stream.CanSeek
        });

        try
        {
            trace.State("stage", () => "readSource");
            var readResult = await _reader.ReadAsync(new LimitedReadStream(stream, _fileHandlingOptions.MaxFileBytes, "file_too_large"), OfficeFormat.PowerPoint, cancellationToken).ConfigureAwait(false);
            if (readResult.Errors.Count > 0 || readResult.Source is null)
            {
                trace.Return(new { outcome = "failed", errorCodes = readResult.Errors.Select(e => e.Code).ToArray() });
                return new([], readResult.Errors);
            }

            using var source = readResult.Source;
            if (source.OriginalBytes.Length > _fileHandlingOptions.MaxFileBytes)
            {
                var err = new FileError("file_too_large", $"Kích thước tệp ({source.OriginalBytes.Length} bytes) vượt quá giới hạn ({_fileHandlingOptions.MaxFileBytes} bytes).");
                trace.Return(new { outcome = "failed", errorCodes = new[] { err.Code } });
                return new([], [err]);
            }

            trace.State("source", () => new { digest = source.SourceHash, size = source.OriginalBytes.Length, format = "PowerPoint" });

            trace.State("stage", () => "inspectPackage");
            var inventory = _inspector.Inspect(source, cancellationToken);

            trace.State("stage", () => "analyzeDocument");
            PowerPointPlan plan;
            try
            {
                plan = _extractor.Analyze(source, inventory, cancellationToken);
            }
            catch (InvalidOperationException ex) when (ex is not FileLimitException)
            {
                var err = new FileError("office_unsupported_content", ex.Message);
                trace.Return(new { outcome = "failed", errorCodes = new[] { err.Code } });
                return new([], [err]);
            }

            trace.State("plan", () => new { unitCount = plan.Units.Count, slideCount = plan.Slides.Count });

            if (plan.Units.Count > _fileHandlingOptions.MaxUnits)
            {
                var err = new FileError("too_many_units", $"Số lượng đơn vị dịch ({plan.Units.Count}) vượt quá giới hạn ({_fileHandlingOptions.MaxUnits}).");
                trace.Return(new { outcome = "failed", errorCodes = new[] { err.Code } });
                return new([], [err]);
            }

            trace.State("stage", () => "validatePlan");
            var selectedPartUris = plan.Slides.Where(s => s.Show).Select(s => s.PartUri).ToList();
            var sourceValidation = _packageValidator.ValidateSource(source, selectedPartUris, cancellationToken);
            if (!sourceValidation.IsValid)
            {
                trace.Return(new { outcome = "failed", errorCodes = sourceValidation.Errors.Select(e => e.Code).ToArray() });
                return new([], sourceValidation.Errors);
            }

            trace.State("stage", () => "extractTexts");
            var texts = plan.Units.Select(u => u.EncodedSource).ToArray();

            trace.Return(new { outcome = "success", unitCount = texts.Length });
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
    /// Applies validated translations and publishes an atomic output.
    /// </summary>
    /// <param name="stream">Caller-owned source stream.</param>
    /// <param name="translations">Ordered caller translations.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task containing output bytes or validation errors.</returns>
    public async Task<ExportResult> ExportAsync(Stream stream, IReadOnlyList<string> translations, CancellationToken cancellationToken = default)
    {
        using var trace = DebugTrace.Enter("PowerPointService", "ExportAsync", () => new
        {
            format = "PowerPoint",
            profile = "office-v1",
            sourceCanRead = stream.CanRead,
            sourceCanSeek = stream.CanSeek,
            translationCount = translations.Count
        });

        try
        {
            trace.State("stage", () => "readSource");
            var readResult = await _reader.ReadAsync(new LimitedReadStream(stream, _fileHandlingOptions.MaxFileBytes, "file_too_large"), OfficeFormat.PowerPoint, cancellationToken).ConfigureAwait(false);
            if (readResult.Errors.Count > 0 || readResult.Source is null)
            {
                trace.Return(new { outcome = "failed", errorCodes = readResult.Errors.Select(e => e.Code).ToArray() });
                return new(null, ContentType, readResult.Errors);
            }

            using var source = readResult.Source;
            if (source.OriginalBytes.Length > _fileHandlingOptions.MaxFileBytes)
            {
                var err = new FileError("file_too_large", $"Kích thước tệp ({source.OriginalBytes.Length} bytes) vượt quá giới hạn ({_fileHandlingOptions.MaxFileBytes} bytes).");
                trace.Return(new { outcome = "failed", errorCodes = new[] { err.Code } });
                return new(null, ContentType, [err]);
            }

            trace.State("source", () => new { digest = source.SourceHash, size = source.OriginalBytes.Length, format = "PowerPoint" });

            trace.State("stage", () => "inspectPackage");
            var inventory = _inspector.Inspect(source, cancellationToken);

            trace.State("stage", () => "analyzeDocument");
            PowerPointPlan plan;
            try
            {
                plan = _extractor.Analyze(source, inventory, cancellationToken);
            }
            catch (InvalidOperationException ex) when (ex is not FileLimitException)
            {
                var err = new FileError("office_unsupported_content", ex.Message);
                trace.Return(new { outcome = "failed", errorCodes = new[] { err.Code } });
                return new(null, ContentType, [err]);
            }

            trace.State("plan", () => new { unitCount = plan.Units.Count, slideCount = plan.Slides.Count });

            if (plan.Units.Count > _fileHandlingOptions.MaxUnits)
                throw new FileLimitException("too_many_units");
            trace.State("stage", () => "validatePlan");
            var selectedPartUris = plan.Slides.Where(s => s.Show).Select(s => s.PartUri).ToList();
            var sourceValidation = _packageValidator.ValidateSource(source, selectedPartUris, cancellationToken);
            if (!sourceValidation.IsValid)
            {
                trace.Return(new { outcome = "failed", errorCodes = sourceValidation.Errors.Select(e => e.Code).ToArray() });
                return new(null, ContentType, sourceValidation.Errors);
            }

            trace.State("stage", () => "validateTranslations");
            var decodeResult = _codec.ValidateAndDecode(plan.Units, translations, OfficeFormat.PowerPoint, cancellationToken);
            if (decodeResult.Errors.Count > 0 || decodeResult.DecodedUnits is null)
            {
                trace.Return(new { outcome = "failed", errorCodes = decodeResult.Errors.Select(e => e.Code).ToArray() });
                return new(null, ContentType, decodeResult.Errors);
            }

            trace.State("stage", () => "preparePatch");
            var patch = _applier.Prepare(plan, decodeResult.DecodedUnits, cancellationToken);

            trace.State("stage", () => "checkIdentity");
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
                trace.State("decision", () => "identity");
                if (source.OriginalBytes.Length > _fileHandlingOptions.MaxOutputBytes)
                {
                    var err = new FileError("output_too_large", "Kích thước tệp vượt quá giới hạn đầu ra cho phép.");
                    trace.Return(new { outcome = "failed", errorCodes = new[] { err.Code } });
                    return new(null, ContentType, [err]);
                }

                trace.State("stage", () => "returnOutput");
                trace.Return(new { outcome = "success", outputBytes = source.OriginalBytes.Length });
                return new(source.OriginalBytes, ContentType, []);
            }

            trace.State("decision", () => "patch");
            trace.State("stage", () => "applyPatch");

            using var session = OfficeExportSession.Create(source, _options, _fileHandlingOptions);
            using (var docMs = new MemoryStream(source.OriginalBytes))
            using (var doc = PresentationDocument.Open(docMs, false, OfficeTextBindings.Settings(_options)))
            {
                _applier.Apply(session, doc, patch, cancellationToken);
            }

            trace.State("stage", () => "serializeOutput");
            var output = await session.FinalizeAsync(cancellationToken).ConfigureAwait(false);
            if (output.OutputBytes > _fileHandlingOptions.MaxOutputBytes)
            {
                var err = new FileError("output_too_large", "Kích thước tệp vượt quá giới hạn đầu ra cho phép.");
                trace.Return(new { outcome = "failed", errorCodes = new[] { err.Code } });
                return new(null, ContentType, [err]);
            }

            trace.State("stage", () => "validateOutput");
            var outputValidation = _packageValidator.ValidateOutput(source, output, patch.EditMasks, cancellationToken);
            if (!outputValidation.IsValid)
            {
                trace.Return(new { outcome = "failed", errorCodes = outputValidation.Errors.Select(e => e.Code).ToArray() });
                return new(null, ContentType, outputValidation.Errors);
            }

            var structureValidation = _structureValidator.Validate(output.Content, plan, cancellationToken);
            if (!structureValidation.IsValid)
            {
                trace.Return(new { outcome = "failed", errorCodes = structureValidation.Errors.Select(e => e.Code).ToArray() });
                return new(null, ContentType, structureValidation.Errors);
            }

            trace.State("stage", () => "returnOutput");
            trace.Return(new { outcome = "success", outputBytes = output.OutputBytes });
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
    /// Creates PowerPoint service with default dependencies for testing.
    /// </summary>
    /// <param name="fileHandlingOptions">File processing limits.</param>
    /// <param name="officeOptions">Office processing options.</param>
    /// <returns>Configured PowerPoint service instance.</returns>
    public static PowerPointService Create(
        IOptions<FileHandlingOptions>? fileHandlingOptions = null,
        IOptions<OfficeProcessingOptions>? officeOptions = null)
    {
        var fOpts = fileHandlingOptions ?? Microsoft.Extensions.Options.Options.Create(new FileHandlingOptions());
        var oOpts = officeOptions ?? Microsoft.Extensions.Options.Options.Create(new OfficeProcessingOptions());
        var reader = new OfficePackageReader(oOpts.Value);
        var inspector = new OfficePackageInspector(oOpts.Value);
        var codec = new OfficeTextCodec(oOpts.Value, fOpts.Value);
        var tableReader = new PowerPointTableReader();
        var extractor = new PowerPointExtractor(codec, tableReader, oOpts.Value);
        var applier = new PowerPointTranslationApplier();
        var pkgValidator = new OfficePackageValidator(oOpts.Value);
        var structValidator = new PowerPointStructureValidator();
        return new PowerPointService(reader, inspector, extractor, codec, applier, pkgValidator, structValidator, oOpts, fOpts);
    }
}
