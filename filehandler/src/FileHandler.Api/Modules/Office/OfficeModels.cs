using System.Security.Cryptography;
using System.Text;
using FileHandler.Api.Common;

namespace FileHandler.Api.Modules.Office;

/// <summary>
/// Supported Office document formats.
/// </summary>
public enum OfficeFormat
{

    /// <summary>
    /// WordprocessingML document (.docx).
    /// </summary>
    Word,

    /// <summary>
    /// SpreadsheetML workbook (.xlsx).
    /// </summary>
    Excel,

    /// <summary>
    /// PresentationML presentation (.pptx).
    /// </summary>
    PowerPoint
}

/// <summary>
/// Translation unit encoding mode.
/// </summary>
public enum UnitMode
{

    /// <summary>
    /// Plain text without structured formatting or anchors.
    /// </summary>
    Plain,

    /// <summary>
    /// Structured text with formatting slots and protected anchors.
    /// </summary>
    Structured
}

/// <summary>
/// Kinds of protected anchors preserved during translation.
/// </summary>
public enum AnchorKind
{

    /// <summary>
    /// Document or slide field.
    /// </summary>
    Field,

    /// <summary>
    /// Hard line or column break.
    /// </summary>
    Break,

    /// <summary>
    /// Tab character.
    /// </summary>
    Tab,

    /// <summary>
    /// Raw control character preserved inside text run.
    /// </summary>
    RawTextControl,

    /// <summary>
    /// Mathematical equation.
    /// </summary>
    Math,

    /// <summary>
    /// Inline picture or drawing object.
    /// </summary>
    Picture,

    /// <summary>
    /// Bookmark or proofing marker.
    /// </summary>
    Marker,

    /// <summary>
    /// Standalone whitespace span.
    /// </summary>
    Whitespace
}

/// <summary>
/// Classification of semantic Office objects.
/// </summary>
public enum OfficeObjectKind
{

    /// <summary>
    /// Document body or main story.
    /// </summary>
    Story,

    /// <summary>
    /// Paragraph of text.
    /// </summary>
    Paragraph,

    /// <summary>
    /// Table element.
    /// </summary>
    Table,

    /// <summary>
    /// Table row.
    /// </summary>
    TableRow,

    /// <summary>
    /// Table cell.
    /// </summary>
    TableCell,

    /// <summary>
    /// Header story.
    /// </summary>
    Header,

    /// <summary>
    /// Footer story.
    /// </summary>
    Footer,

    /// <summary>
    /// Footnote story.
    /// </summary>
    Footnote,

    /// <summary>
    /// Endnote story.
    /// </summary>
    Endnote,

    /// <summary>
    /// DrawingML shape.
    /// </summary>
    Shape,

    /// <summary>
    /// DrawingML paragraph.
    /// </summary>
    DrawingParagraph,

    /// <summary>
    /// Worksheet grid cell.
    /// </summary>
    SpreadsheetCell,

    /// <summary>
    /// Presentation slide.
    /// </summary>
    Slide
}

/// <summary>
/// Processing capability policy applied to Office objects.
/// </summary>
public enum OfficeCapabilityPolicy
{

    /// <summary>
    /// Object is candidate for text translation.
    /// </summary>
    Translate,

    /// <summary>
    /// Object is protected metadata, identifier, or non-translatable structure.
    /// </summary>
    Protected,

    /// <summary>
    /// Object is excluded from translation scope by profile.
    /// </summary>
    Excluded,

    /// <summary>
    /// Object is unsupported in current profile and causes preflight rejection.
    /// </summary>
    Unsupported
}

/// <summary>
/// Segment of hierarchical element path within Open XML document part.
/// </summary>
/// <param name="NamespaceUri">XML namespace URI of element.</param>
/// <param name="LocalName">XML local name of element.</param>
/// <param name="SiblingOrdinal">One-based ordinal among siblings sharing same expanded name.</param>
public sealed record OfficeElementPathSegment(string NamespaceUri, string LocalName, int SiblingOrdinal);

/// <summary>
/// Location locator identifying XML element within Office package.
/// </summary>
/// <param name="PartUri">Canonical part URI within package.</param>
/// <param name="ElementPath">Path segments from root to target element.</param>
/// <param name="CellReference">Optional A1-style spreadsheet cell reference.</param>
/// <param name="SlideId">Optional presentation slide identifier.</param>
/// <param name="ShapeId">Optional drawing shape identifier.</param>
/// <param name="TableLocation">Optional table reference name or coordinates.</param>
/// <param name="RowIndex">Optional table or grid row index.</param>
/// <param name="ColumnIndex">Optional table or grid column index.</param>
public sealed record OfficeLocation(
    string PartUri,
    IReadOnlyList<OfficeElementPathSegment> ElementPath,
    string? CellReference = null,
    string? SlideId = null,
    string? ShapeId = null,
    string? TableLocation = null,
    int? RowIndex = null,
    int? ColumnIndex = null)
{

    /// <summary>
    /// Actual part root for root-inclusive public paths.
    /// </summary>
    public OfficeElementPathSegment? Root { get; init; }

    /// <summary>
    /// Native workbook sheet identifier.
    /// </summary>
    public string? SheetId { get; init; }
}

/// <summary>
/// Mutable text slot corresponding to format run or cell text.
/// </summary>
/// <param name="SlotId">Slot identifier such as r0.</param>
/// <param name="OriginalText">Original decoded plain text of slot.</param>
/// <param name="FormatFingerprint">Fingerprint of explicit run formatting properties.</param>
public sealed record OfficeTextSlot(string SlotId, string OriginalText, string FormatFingerprint);

/// <summary>
/// Non-translatable anchor preserved in structured text.
/// </summary>
/// <param name="AnchorId">Anchor identifier such as k0.</param>
/// <param name="Kind">Category of protected anchor.</param>
/// <param name="SourceFingerprint">Fingerprint of underlying XML element.</param>
public sealed record OfficeProtectedAnchor(string AnchorId, AnchorKind Kind, string SourceFingerprint);

/// <summary>
/// Physical XML binding mapping slot text to target XML node.
/// </summary>
/// <param name="TargetPartUri">Target part URI within package.</param>
/// <param name="Location">Hierarchical locator of target XML element.</param>
/// <param name="ScalarKind">Kind of XML scalar node such as w:t or a:t.</param>
/// <param name="OriginalValueHash">SHA-256 hash of original scalar text.</param>
/// <param name="SpanOffset">Zero-based character offset within target scalar.</param>
/// <param name="SpanLength">Character length within target scalar.</param>
/// <param name="EditGroupId">Identifier linking co-dependent bindings.</param>
/// <param name="SourceValue">Original complete scalar for reconstructing partial-span edits.</param>
public sealed record OfficeTextBinding(
    string TargetPartUri,
    OfficeLocation Location,
    string ScalarKind,
    string OriginalValueHash,
    int SpanOffset = 0,
    int SpanLength = 0,
    string? EditGroupId = null,
    string? SourceValue = null);

/// <summary>
/// Single translation unit extracted from Office document.
/// </summary>
/// <param name="Index">Zero-based unit index in document sequence.</param>
/// <param name="UnitId">Canonical deterministic unit hash identifier.</param>
/// <param name="ObjectId">Identifier of parent semantic object.</param>
/// <param name="Location">Document location locator.</param>
/// <param name="Mode">Text structure encoding mode.</param>
/// <param name="EncodedSource">Canonical encoded string exposed to caller.</param>
/// <param name="Slots">Ordered text slots composing unit.</param>
/// <param name="Anchors">Ordered protected anchors composing unit.</param>
/// <param name="Bindings">Physical bindings required to apply translation.</param>
/// <param name="OriginalPlainTextHash">Hash of concatenated unescaped original text.</param>
public sealed record OfficeTranslationUnit(
    int Index,
    string UnitId,
    string ObjectId,
    OfficeLocation Location,
    UnitMode Mode,
    string EncodedSource,
    IReadOnlyList<OfficeTextSlot> Slots,
    IReadOnlyList<OfficeProtectedAnchor> Anchors,
    IReadOnlyList<OfficeTextBinding> Bindings,
    string OriginalPlainTextHash)
{

    /// <summary>
    /// Semantic unit kind, including dedicated worksheet name units.
    /// </summary>
    public string Kind { get; init; } = "paragraph";
}

/// <summary>
/// Semantic document object discovered during package inspection.
/// </summary>
/// <param name="ObjectId">Canonical deterministic object identifier.</param>
/// <param name="Kind">Semantic object classification.</param>
/// <param name="Location">Location of object root.</param>
/// <param name="PhysicalStorageKey">Identifier of physical storage holding object.</param>
/// <param name="Policy">Capability policy governing object processing.</param>
/// <param name="Reason">Diagnostic reason when object is excluded or unsupported.</param>
public sealed record OfficeObject(
    string ObjectId,
    OfficeObjectKind Kind,
    OfficeLocation Location,
    string PhysicalStorageKey,
    OfficeCapabilityPolicy Policy,
    string? Reason = null);

/// <summary>
/// Package relationship connecting parts.
/// </summary>
/// <param name="OwnerPartUri">Part URI owning relationship, or empty for root.</param>
/// <param name="Id">Relationship identifier (rId).</param>
/// <param name="Type">Relationship type URI.</param>
/// <param name="TargetUri">Resolved target part URI or external URL.</param>
/// <param name="TargetMode">Internal or External target mode.</param>
public sealed record OfficeRelationship(string OwnerPartUri, string Id, string Type, string TargetUri, string TargetMode);

/// <summary>
/// Metadata describing single part inside Office package.
/// </summary>
/// <param name="PartUri">Canonical part URI.</param>
/// <param name="ContentType">MIME content type of part.</param>
/// <param name="SdkKind">Open XML SDK part type name.</param>
/// <param name="CompressedSize">Compressed size in bytes.</param>
/// <param name="UncompressedSize">Uncompressed size in bytes.</param>
/// <param name="PayloadHash">SHA-256 hash of decompressed payload.</param>
/// <param name="Selected">True when part is selected for translation processing.</param>
public sealed record OfficePartInfo(
    string PartUri,
    string ContentType,
    string SdkKind,
    long CompressedSize,
    long UncompressedSize,
    string PayloadHash,
    bool Selected);

/// <summary>
/// Diagnostic finding reported during package inspection or validation.
/// </summary>
/// <param name="Code">Stable machine-readable diagnostic code.</param>
/// <param name="Severity">Diagnostic severity level.</param>
/// <param name="Policy">Policy associated with diagnostic.</param>
/// <param name="Location">Optional location of finding.</param>
/// <param name="UnitIndex">Optional translation unit index.</param>
/// <param name="Message">Human-readable diagnostic explanation.</param>
public sealed record OfficeDiagnostic(
    string Code,
    string Severity,
    OfficeCapabilityPolicy Policy,
    OfficeLocation? Location = null,
    int? UnitIndex = null,
    string? Message = null);

/// <summary>
/// Physical and structural inventory of Office package.
/// </summary>
/// <param name="Parts">All discovered parts in package.</param>
/// <param name="Relationships">All discovered relationships.</param>
/// <param name="Objects">Semantic objects discovered.</param>
/// <param name="Diagnostics">Diagnostics encountered during inventory scan.</param>
public sealed record OfficeInventory(
    IReadOnlyList<OfficePartInfo> Parts,
    IReadOnlyList<OfficeRelationship> Relationships,
    IReadOnlyList<OfficeObject> Objects,
    IReadOnlyList<OfficeDiagnostic> Diagnostics)
{

    /// <summary>
    /// Fast lookup table of package parts keyed by canonical URI.
    /// </summary>
    public IReadOnlyDictionary<string, OfficePartInfo> PartsByUri { get; } =
        Parts.ToDictionary(p => p.PartUri, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Owned immutable source snapshot for Office processing.
/// </summary>
public sealed partial class OfficeSource : IDisposable
{

    /// <summary>
    /// Request-local extraction facts retained when mapping cannot complete.
    /// </summary>
    internal FileMetadata? ProcessingMetadata { get; set; }

    /// <summary>
    /// Independent copy of original document bytes for external callers.
    /// </summary>
    public byte[] OriginalBytes => Bytes.ToArray();

    /// <summary>
    /// Hexadecimal lowercase SHA-256 digest of original bytes.
    /// </summary>
    public string SourceHash { get; }

    /// <summary>
    /// Office document format.
    /// </summary>
    public OfficeFormat Format { get; }

    /// <summary>
    /// Active profile version constant.
    /// </summary>
    public string ProfileVersion { get; }

    /// <summary>
    /// Processing limits active for this source.
    /// </summary>
    public OfficeProcessingOptions Limits { get; }

    /// <summary>
    /// Creates owned Office source snapshot.
    /// </summary>
    /// <param name="originalBytes">Source document bytes.</param>
    /// <param name="sourceHash">Hexadecimal SHA-256 digest.</param>
    /// <param name="format">Detected Office format.</param>
    /// <param name="limits">Active processing limits.</param>
    /// <param name="profileVersion">Profile version identifier.</param>
    public OfficeSource(
        byte[] originalBytes,
        string sourceHash,
        OfficeFormat format,
        OfficeProcessingOptions limits,
        string profileVersion = "office-v1") : this(originalBytes, sourceHash, format, limits, profileVersion, false) { }

    /// <summary>
    /// Disposes resources held by source.
    /// </summary>
    /// <returns>No return value.</returns>
    public void Dispose()
    {
        // Byte array is garbage collected.
    }
}

/// <summary>
/// Output payload produced by Office export operation.
/// </summary>
/// <param name="Content">Output ZIP archive bytes.</param>
/// <param name="OutputHash">Hexadecimal SHA-256 digest of output bytes.</param>
/// <param name="OutputBytes">Total byte count of output.</param>
/// <param name="Summary">Summary description of generated output.</param>
public sealed record OfficeOutput(byte[] Content, string OutputHash, long OutputBytes, string Summary);

/// <summary>
/// Result of reading and preflighting Office package.
/// </summary>
/// <param name="Source">Valid source snapshot when reading succeeded.</param>
/// <param name="Errors">Validation errors when reading failed.</param>
public sealed record OfficeReadResult(OfficeSource? Source, IReadOnlyList<FileError> Errors)
{

    /// <summary>
    /// Creates successful read result.
    /// </summary>
    /// <param name="source">Valid source snapshot.</param>
    /// <returns>Successful read result.</returns>
    public static OfficeReadResult Success(OfficeSource source) => new(source, Array.Empty<FileError>());

    /// <summary>
    /// Creates failed read result.
    /// </summary>
    /// <param name="errors">Validation errors.</param>
    /// <returns>Failed read result.</returns>
    public static OfficeReadResult Failure(IReadOnlyList<FileError> errors) => new(null, errors);
}

/// <summary>
/// Decoded translation units ready for patch preparation.
/// </summary>
/// <param name="Index">Zero-based unit index.</param>
/// <param name="EncodedInput">Original encoded input text.</param>
/// <param name="DecodedSlots">Decoded plain text values for each slot in order.</param>
public sealed record OfficeDecodedUnit(int Index, string EncodedInput, IReadOnlyList<string> DecodedSlots);

/// <summary>
/// Result of decoding caller-supplied translation texts.
/// </summary>
/// <param name="DecodedUnits">Decoded units when decoding succeeded.</param>
/// <param name="Errors">Codec errors when validation failed.</param>
public sealed record OfficeDecodeResult(IReadOnlyList<OfficeDecodedUnit>? DecodedUnits, IReadOnlyList<FileError> Errors)
{

    /// <summary>
    /// Invalid translation units retained directly from source.
    /// </summary>
    public IReadOnlyList<SkipMetadata> Skipped { get; init; } = [];

    /// <summary>
    /// Creates successful decode result.
    /// </summary>
    /// <param name="decodedUnits">Decoded unit list.</param>
    /// <returns>Successful decode result.</returns>
    public static OfficeDecodeResult Success(IReadOnlyList<OfficeDecodedUnit> decodedUnits) => new(decodedUnits, Array.Empty<FileError>());

    /// <summary>
    /// Creates failed decode result.
    /// </summary>
    /// <param name="errors">Codec validation errors.</param>
    /// <returns>Failed decode result.</returns>
    public static OfficeDecodeResult Failure(IReadOnlyList<FileError> errors) => new(null, errors);
}

/// <summary>
/// Result of package or structure validation.
/// </summary>
/// <param name="IsValid">True when validation passed.</param>
/// <param name="Errors">Validation errors encountered.</param>
public sealed record OfficeValidationResult(bool IsValid, IReadOnlyList<FileError> Errors)
{

    /// <summary>
    /// Creates successful validation result.
    /// </summary>
    /// <returns>Valid result with no errors.</returns>
    public static OfficeValidationResult Success() => new(true, Array.Empty<FileError>());

    /// <summary>
    /// Creates failed validation result.
    /// </summary>
    /// <param name="errors">Encountered errors.</param>
    /// <returns>Failed result containing errors.</returns>
    public static OfficeValidationResult Failure(IReadOnlyList<FileError> errors) => new(false, errors);
}

/// <summary>
/// Allowed modification mask specifying permitted XML changes for touched part.
/// </summary>
/// <param name="PartUri">Canonical URI of touched part.</param>
/// <param name="AllowedElementPaths">Permitted XPath-like element paths.</param>
/// <param name="SstOptionalCountersRemoved">True when optional count attributes were removed from SST.</param>
/// <param name="ScalarEdits">Exact root-relative scalar edits, or null for legacy masks.</param>
/// <param name="AppendedXml">Exact appended shared string payloads in index order.</param>
public sealed record OfficeEditMask(
    string PartUri,
    IReadOnlyList<string> AllowedElementPaths,
    bool SstOptionalCountersRemoved = false,
    IReadOnlyDictionary<string, OfficeScalarEdit>? ScalarEdits = null,
    IReadOnlyList<string>? AppendedXml = null)
{

    /// <summary>
    /// Exact attribute edits, limited to explicitly planned rename references.
    /// </summary>
    public IReadOnlyList<OfficeAttributeEdit> AttributeEdits { get; init; } = [];
}

/// <summary>
/// Exact attribute modification with expected source value.
/// </summary>
/// <param name="ElementPath">Canonical root-relative element key.</param>
/// <param name="NamespaceUri">Attribute namespace URI.</param>
/// <param name="LocalName">Attribute local name.</param>
/// <param name="SourceValue">Expected original value.</param>
/// <param name="Value">Planned replacement value.</param>
public sealed record OfficeAttributeEdit(string ElementPath, string NamespaceUri, string LocalName, string SourceValue, string Value);

/// <summary>
/// Expected immutable source hash and translated scalar value.
/// </summary>
/// <param name="SourceHash">Original scalar SHA-256.</param>
/// <param name="Value">Expected translated value.</param>
public sealed record OfficeScalarEdit(string SourceHash, string Value);

/// <summary>
/// Template for encoding and decoding structured Office text.
/// </summary>
/// <param name="Mode">Encoding mode.</param>
/// <param name="Slots">Ordered text slots.</param>
/// <param name="Anchors">Ordered protected anchors.</param>
/// <param name="Bindings">Associated physical bindings.</param>
/// <param name="Order">Interleaved slot and anchor identifiers in source order.</param>
public sealed record OfficeTextTemplate(
    UnitMode Mode,
    IReadOnlyList<OfficeTextSlot> Slots,
    IReadOnlyList<OfficeProtectedAnchor> Anchors,
    IReadOnlyList<OfficeTextBinding> Bindings,
    IReadOnlyList<string>? Order = null);

/// <summary>
/// Deterministic identity builder for Office translation units and semantic objects.
/// </summary>
public static class OfficeIdentity
{

    /// <summary>
    /// Builds deterministic lowercase 64-hex SHA-256 unit identifier.
    /// </summary>
    /// <param name="profileVersion">Profile version identifier.</param>
    /// <param name="sourceHash">Hexadecimal SHA-256 of source file.</param>
    /// <param name="format">Document format.</param>
    /// <param name="partUri">Canonical part URI.</param>
    /// <param name="kind">Object kind.</param>
    /// <param name="elementPath">Hierarchical element path.</param>
    /// <param name="localOrdinal">One-based ordinal within container.</param>
    /// <returns>Deterministic 64-hex SHA-256 unit ID.</returns>
    public static string CreateUnitId(
        string profileVersion,
        string sourceHash,
        OfficeFormat format,
        string partUri,
        OfficeObjectKind kind,
        IReadOnlyList<OfficeElementPathSegment> elementPath,
        int localOrdinal)
    {
        var sb = new StringBuilder();
        AppendLengthPrefixed(sb, profileVersion);
        AppendLengthPrefixed(sb, sourceHash);
        AppendLengthPrefixed(sb, format.ToString());
        AppendLengthPrefixed(sb, partUri);
        AppendLengthPrefixed(sb, kind.ToString());
        foreach (var segment in elementPath)
        {
            AppendLengthPrefixed(sb, segment.NamespaceUri);
            AppendLengthPrefixed(sb, segment.LocalName);
            AppendLengthPrefixed(sb, segment.SiblingOrdinal.ToString());
        }
        AppendLengthPrefixed(sb, localOrdinal.ToString());
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    /// <summary>
    /// Appends length-prefixed string component to builder.
    /// </summary>
    /// <param name="builder">Target string builder.</param>
    /// <param name="value">String value to append.</param>
    /// <returns>No return value.</returns>
    private static void AppendLengthPrefixed(StringBuilder builder, string value)
    {
        var byteCount = Encoding.UTF8.GetByteCount(value);
        builder.Append(byteCount).Append(':').Append(value);
    }
}
