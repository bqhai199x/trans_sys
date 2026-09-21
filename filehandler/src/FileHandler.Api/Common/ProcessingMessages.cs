namespace FileHandler.Api.Common;

/// <summary>
/// Shared concise English messages for processing skips and common request failures.
/// </summary>
internal static class ProcessingMessages
{

    /// <summary>
    /// EmptyTranslation diagnostic message.
    /// </summary>
    internal const string EmptyTranslation = "Empty translation; source retained.";

    /// <summary>
    /// EmptySlots diagnostic message.
    /// </summary>
    internal const string EmptySlots = "At least one slot must contain text.";

    /// <summary>
    /// InvalidUnicode diagnostic message.
    /// </summary>
    internal const string InvalidUnicode = "Translation contains invalid Unicode.";

    /// <summary>
    /// InvalidXmlText diagnostic message.
    /// </summary>
    internal const string InvalidXmlText = "Translation contains invalid Unicode or XML characters.";

    /// <summary>
    /// RawOfficeWhitespace diagnostic message.
    /// </summary>
    internal const string RawOfficeWhitespace = "Word/PowerPoint text cannot contain raw line breaks or tabs.";

    /// <summary>
    /// NullTranslation diagnostic message.
    /// </summary>
    internal const string NullTranslation = "Translation cannot be null.";

    /// <summary>
    /// SheetNotSelected diagnostic message.
    /// </summary>
    internal const string SheetNotSelected = "Sheet not selected; source retained.";

    /// <summary>
    /// SlideNotSelected diagnostic message.
    /// </summary>
    internal const string SlideNotSelected = "Slide not selected; source retained.";

    /// <summary>
    /// UnsupportedSheet diagnostic message.
    /// </summary>
    internal const string UnsupportedSheet = "Unsupported sheet type; source retained.";

    /// <summary>
    /// UnsafeSheetReference diagnostic message.
    /// </summary>
    internal const string UnsafeSheetReference = "Unsafe references; sheet name retained.";

    /// <summary>
    /// HiddenRow diagnostic message.
    /// </summary>
    internal const string HiddenRow = "Hidden row retained.";

    /// <summary>
    /// HiddenColumn diagnostic message.
    /// </summary>
    internal const string HiddenColumn = "Cell in hidden column retained.";

    /// <summary>
    /// ImplicitCellAddress diagnostic message.
    /// </summary>
    internal const string ImplicitCellAddress = "Cell without explicit address retained.";

    /// <summary>
    /// ProtectedTableCell diagnostic message.
    /// </summary>
    internal const string ProtectedTableCell = "Table header or totals cell retained.";

    /// <summary>
    /// FormulaCell diagnostic message.
    /// </summary>
    internal const string FormulaCell = "Formula cell excluded from translation.";

    /// <summary>
    /// NonTextCell diagnostic message.
    /// </summary>
    internal const string NonTextCell = "Non-text cell retained.";

    /// <summary>
    /// WhitespaceCell diagnostic message.
    /// </summary>
    internal const string WhitespaceCell = "Whitespace-only cell retained.";

    /// <summary>
    /// PhoneticContent diagnostic message.
    /// </summary>
    internal const string PhoneticContent = "Unsupported phonetic cell retained.";

    /// <summary>
    /// MergedFollowerText diagnostic message.
    /// </summary>
    internal const string MergedFollowerText = "Merged follower text retained.";

    /// <summary>
    /// UnsupportedGraphicFrame diagnostic message.
    /// </summary>
    internal const string UnsupportedGraphicFrame = "Unsupported graphic frame retained.";

    /// <summary>
    /// ProtectedContentControl diagnostic message.
    /// </summary>
    internal const string ProtectedContentControl = "Locked or bound content control retained.";

    /// <summary>
    /// UnsupportedRevision diagnostic message.
    /// </summary>
    internal const string UnsupportedRevision = "Unsupported revision retained.";

    /// <summary>
    /// UnsupportedRuby diagnostic message.
    /// </summary>
    internal const string UnsupportedRuby = "Unsupported ruby retained.";

    /// <summary>
    /// UnsupportedAltchunk diagnostic message.
    /// </summary>
    internal const string UnsupportedAltchunk = "Unsupported altChunk retained.";

    /// <summary>
    /// UnsupportedAlternateContent diagnostic message.
    /// </summary>
    internal const string UnsupportedAlternateContent = "Unsupported AlternateContent retained.";

    /// <summary>
    /// MergedFollower diagnostic message.
    /// </summary>
    internal const string MergedFollower = "Empty merge continuation cell retained.";

    /// <summary>
    /// CrossParagraphField diagnostic message.
    /// </summary>
    internal const string CrossParagraphField = "Cross-paragraph field retained.";

    /// <summary>
    /// UnboundedFieldStory diagnostic message.
    /// </summary>
    internal const string UnboundedFieldStory = "Unknown field boundary; source region retained.";

    /// <summary>
    /// ProtectedInline diagnostic message.
    /// </summary>
    internal const string ProtectedInline = "Protected inline content retained.";

    /// <summary>
    /// UnsupportedDrawing diagnostic message.
    /// </summary>
    internal const string UnsupportedDrawing = "Unsupported chart or diagram retained.";

    /// <summary>
    /// UnreferencedStory diagnostic message.
    /// </summary>
    internal const string UnreferencedStory = "Unreferenced story retained.";

    /// <summary>
    /// SystemNote diagnostic message.
    /// </summary>
    internal const string SystemNote = "System note retained.";

    /// <summary>
    /// UnsupportedBlock diagnostic message.
    /// </summary>
    internal const string UnsupportedBlock = "Unsupported block retained.";

    /// <summary>
    /// ProtectedCodeBlock diagnostic message.
    /// </summary>
    internal const string ProtectedCodeBlock = "Code block retained.";

    /// <summary>
    /// UnsupportedMermaid diagnostic message.
    /// </summary>
    internal const string UnsupportedMermaid = "No translatable Mermaid labels; block retained.";

    /// <summary>
    /// ProtectedBlock diagnostic message.
    /// </summary>
    internal const string ProtectedBlock = "Protected block retained.";

    /// <summary>
    /// InternalAnchorChange diagnostic message.
    /// </summary>
    internal const string InternalAnchorChange = "Heading retained to preserve internal links.";

    /// <summary>
    /// HeadingStructure diagnostic message.
    /// </summary>
    internal const string HeadingStructure = "Heading cannot add lines or blocks.";

    /// <summary>
    /// BlockStructure diagnostic message.
    /// </summary>
    internal const string BlockStructure = "Protected block structure changed; source retained.";

    /// <summary>
    /// MermaidStructure diagnostic message.
    /// </summary>
    internal const string MermaidStructure = "Mermaid labels cannot contain control characters.";

    /// <summary>
    /// MarkdownTokens diagnostic message.
    /// </summary>
    internal const string MarkdownTokens = "Preserve source tokens, order and escapes.";

    /// <summary>
    /// LegacyMarkerSyntax diagnostic message.
    /// </summary>
    internal const string LegacyMarkerSyntax = "Invalid keepme marker syntax or ID.";

    /// <summary>
    /// ProtectedMarkerNotEmpty diagnostic message.
    /// </summary>
    internal const string ProtectedMarkerNotEmpty = "Protected marker must be empty.";

    /// <summary>
    /// MissingFile diagnostic message.
    /// </summary>
    internal const string MissingFile = "File is required.";

    /// <summary>
    /// MissingTexts diagnostic message.
    /// </summary>
    internal const string MissingTexts = "Texts are required.";

    /// <summary>
    /// InvalidTexts diagnostic message.
    /// </summary>
    internal const string InvalidTexts = "Texts must be a JSON string array.";

    /// <summary>
    /// InvalidTextElement diagnostic message.
    /// </summary>
    internal const string InvalidTextElement = "Each text must be a non-null string.";

    /// <summary>
    /// InvalidJson diagnostic message.
    /// </summary>
    internal const string InvalidJson = "Texts contain invalid JSON.";

    /// <summary>
    /// InvalidJsonUnicode diagnostic message.
    /// </summary>
    internal const string InvalidJsonUnicode = "Texts contain invalid Unicode.";

    /// <summary>
    /// InvalidSelection diagnostic message.
    /// </summary>
    internal const string InvalidSelection = "Selection must be a JSON string array.";

    /// <summary>
    /// InvalidSelectionJson diagnostic message.
    /// </summary>
    internal const string InvalidSelectionJson = "Selection contains invalid JSON strings.";

    /// <summary>
    /// InvalidRequest diagnostic message.
    /// </summary>
    internal const string InvalidRequest = "Invalid multipart request.";

    /// <summary>
    /// UnsupportedMediaType diagnostic message.
    /// </summary>
    internal const string UnsupportedMediaType = "Content-Type must be multipart/form-data.";

    /// <summary>
    /// UnsupportedFileType diagnostic message.
    /// </summary>
    internal const string UnsupportedFileType = "Unsupported file extension.";

    /// <summary>
    /// RequestTooLarge diagnostic message.
    /// </summary>
    internal const string RequestTooLarge = "Multipart request exceeds size limit.";

    /// <summary>
    /// TooManyTranslations diagnostic message.
    /// </summary>
    internal const string TooManyTranslations = "Too many translations.";

    /// <summary>
    /// InvalidOfficePackage diagnostic message.
    /// </summary>
    internal const string InvalidOfficePackage = "Invalid Office package.";

    /// <summary>
    /// PatchConflict diagnostic message.
    /// </summary>
    internal const string PatchConflict = "Replacement regions overlap.";

    /// <summary>
    /// RequestLimitExceeded diagnostic message.
    /// </summary>
    internal const string RequestLimitExceeded = "Processing capacity reached. Retry later.";

    /// <summary>
    /// InternalError diagnostic message.
    /// </summary>
    internal const string InternalError = "Internal server error.";

    /// <summary>
    /// TextOutsideToken diagnostic message.
    /// </summary>
    internal const string TextOutsideToken = "Text outside tokens is not allowed.";

    /// <summary>
    /// InvalidTokenPrefix diagnostic message.
    /// </summary>
    internal const string InvalidTokenPrefix = "Tokens must start with '<ox:'.";

    /// <summary>
    /// UnterminatedToken diagnostic message.
    /// </summary>
    internal const string UnterminatedToken = "Unterminated token.";

    /// <summary>
    /// TokenOrder diagnostic message.
    /// </summary>
    internal const string TokenOrder = "Token order differs from source.";

    /// <summary>
    /// DanglingEscape diagnostic message.
    /// </summary>
    internal const string DanglingEscape = "Incomplete escape sequence.";

    /// <summary>
    /// UnescapedSlotTag diagnostic message.
    /// </summary>
    internal const string UnescapedSlotTag = "Escape '<' as '\\<' inside slots.";

    /// <summary>
    /// InvalidTokenSyntax diagnostic message.
    /// </summary>
    internal const string InvalidTokenSyntax = "Invalid token syntax.";

    /// <summary>
    /// TokenCount diagnostic message.
    /// </summary>
    internal const string TokenCount = "Token count or order differs from source.";

    /// <summary>
    /// Resolves source preservation message from stable exclusion code.
    /// </summary>
    /// <param name="code">Recognized Word exclusion code.</param>
    /// <returns>Specific explanation for preserved source region.</returns>
    internal static string PreservedRegion(string code) => code switch
    {
        SkipCodes.ProtectedContentControl => ProtectedContentControl,
        SkipCodes.UnsupportedRevision => UnsupportedRevision,
        SkipCodes.UnsupportedRuby => UnsupportedRuby,
        SkipCodes.UnsupportedAltchunk => UnsupportedAltchunk,
        SkipCodes.UnsupportedAlternateContent => UnsupportedAlternateContent,
        SkipCodes.MergedFollowerText => MergedFollowerText,
        SkipCodes.MergedFollower => MergedFollower,
        SkipCodes.CrossParagraphField => CrossParagraphField,
        SkipCodes.UnboundedFieldStory => UnboundedFieldStory,
        SkipCodes.UnreferencedStory => UnreferencedStory,
        SkipCodes.SystemNote => SystemNote,
        SkipCodes.UnsupportedBlock => UnsupportedBlock,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown preservation code.")
    };

    /// <summary>
    /// Formats MultipartSizeLimit diagnostic with source-specific details.
    /// </summary>
    /// <param name="actual">Actual diagnostic value.</param>
    /// <param name="limit">Limit diagnostic value.</param>
    /// <returns>Concise English diagnostic message.</returns>
    internal static string MultipartSizeLimit(long actual, long limit) => $"Multipart size {actual} exceeds {limit} bytes.";

    /// <summary>
    /// Formats FileSizeLimit diagnostic with source-specific details.
    /// </summary>
    /// <param name="actual">Actual diagnostic value.</param>
    /// <param name="limit">Limit diagnostic value.</param>
    /// <returns>Concise English diagnostic message.</returns>
    internal static string FileSizeLimit(long actual, long limit) => $"File size {actual} exceeds {limit} bytes.";

    /// <summary>
    /// Formats UnitCountLimit diagnostic with source-specific details.
    /// </summary>
    /// <param name="actual">Actual diagnostic value.</param>
    /// <param name="limit">Limit diagnostic value.</param>
    /// <returns>Concise English diagnostic message.</returns>
    internal static string UnitCountLimit(long actual, long limit) => $"Unit count {actual} exceeds {limit}.";

    /// <summary>
    /// Formats DocumentUnitLimit diagnostic with source-specific details.
    /// </summary>
    /// <param name="limit">Limit diagnostic value.</param>
    /// <returns>Concise English diagnostic message.</returns>
    internal static string DocumentUnitLimit(long limit) => $"Document exceeds {limit} units.";

    /// <summary>
    /// Formats OutputLimit diagnostic with source-specific details.
    /// </summary>
    /// <param name="limit">Limit diagnostic value.</param>
    /// <returns>Concise English diagnostic message.</returns>
    internal static string OutputLimit(long limit) => $"Output exceeds {limit} bytes.";

    /// <summary>
    /// Formats TranslationCountMismatch diagnostic with source-specific details.
    /// </summary>
    /// <param name="expected">Expected diagnostic value.</param>
    /// <param name="actual">Actual diagnostic value.</param>
    /// <returns>Concise English diagnostic message.</returns>
    internal static string TranslationCountMismatch(long expected, long actual) => $"Expected {expected} translations; received {actual}.";

    /// <summary>
    /// Formats TranslationLimit diagnostic with source-specific details.
    /// </summary>
    /// <param name="limit">Limit diagnostic value.</param>
    /// <returns>Concise English diagnostic message.</returns>
    internal static string TranslationLimit(long limit) => $"Translation exceeds {limit} characters.";

    /// <summary>
    /// Formats NoncanonicalMarker diagnostic with source-specific details.
    /// </summary>
    /// <param name="marker">Marker diagnostic value.</param>
    /// <param name="canonical">Canonical diagnostic value.</param>
    /// <returns>Concise English diagnostic message.</returns>
    internal static string NoncanonicalMarker(string marker, string canonical) => $"Marker {marker} must use {canonical}.";

    /// <summary>
    /// Formats UnexpectedMarker diagnostic with source-specific details.
    /// </summary>
    /// <param name="marker">Marker diagnostic value.</param>
    /// <returns>Concise English diagnostic message.</returns>
    internal static string UnexpectedMarker(string marker) => $"Unexpected marker {marker}.";

    /// <summary>
    /// Formats DuplicateMarker diagnostic with source-specific details.
    /// </summary>
    /// <param name="marker">Marker diagnostic value.</param>
    /// <returns>Concise English diagnostic message.</returns>
    internal static string DuplicateMarker(string marker) => $"Duplicate marker {marker}.";

    /// <summary>
    /// Formats InvalidMarkerNesting diagnostic with source-specific details.
    /// </summary>
    /// <param name="marker">Marker diagnostic value.</param>
    /// <returns>Concise English diagnostic message.</returns>
    internal static string InvalidMarkerNesting(string marker) => $"Marker {marker} closes out of order.";

    /// <summary>
    /// Formats MissingOpeningMarker diagnostic with source-specific details.
    /// </summary>
    /// <param name="marker">Marker diagnostic value.</param>
    /// <returns>Concise English diagnostic message.</returns>
    internal static string MissingOpeningMarker(string marker) => $"Missing opening marker {marker}.";

    /// <summary>
    /// Formats MissingClosingMarker diagnostic with source-specific details.
    /// </summary>
    /// <param name="marker">Marker diagnostic value.</param>
    /// <returns>Concise English diagnostic message.</returns>
    internal static string MissingClosingMarker(string marker) => $"Missing closing marker {marker}.";

    /// <summary>
    /// Formats TranslationLengthLimit diagnostic with source-specific details.
    /// </summary>
    /// <param name="actual">Actual diagnostic value.</param>
    /// <param name="limit">Limit diagnostic value.</param>
    /// <returns>Concise English diagnostic message.</returns>
    internal static string TranslationLengthLimit(long actual, long limit) => $"Translation length {actual} exceeds {limit} characters.";

    /// <summary>
    /// Formats TotalTranslationLimit diagnostic with source-specific details.
    /// </summary>
    /// <param name="actual">Actual diagnostic value.</param>
    /// <param name="limit">Limit diagnostic value.</param>
    /// <returns>Concise English diagnostic message.</returns>
    internal static string TotalTranslationLimit(long actual, long limit) => $"Total translation length {actual} exceeds {limit} characters.";

    /// <summary>
    /// Formats CellTextLimit diagnostic with source-specific details.
    /// </summary>
    /// <param name="actual">Actual diagnostic value.</param>
    /// <param name="limit">Limit diagnostic value.</param>
    /// <returns>Concise English diagnostic message.</returns>
    internal static string CellTextLimit(long actual, long limit) => $"Cell text length {actual} exceeds {limit} characters.";

    /// <summary>
    /// Formats UnitTokenLimit diagnostic with source-specific details.
    /// </summary>
    /// <param name="actual">Actual diagnostic value.</param>
    /// <param name="limit">Limit diagnostic value.</param>
    /// <returns>Concise English diagnostic message.</returns>
    internal static string UnitTokenLimit(long actual, long limit) => $"Unit token count {actual} exceeds {limit}.";

    /// <summary>
    /// Formats InvalidSelfClosingToken diagnostic with source-specific details.
    /// </summary>
    /// <param name="token">Token diagnostic value.</param>
    /// <returns>Concise English diagnostic message.</returns>
    internal static string InvalidSelfClosingToken(string token) => $"Invalid self-closing token <ox:{token}/>.";

    /// <summary>
    /// Formats MismatchedAnchor diagnostic with source-specific details.
    /// </summary>
    /// <param name="token">Token diagnostic value.</param>
    /// <returns>Concise English diagnostic message.</returns>
    internal static string MismatchedAnchor(string token) => $"Anchor {token} differs from source.";

    /// <summary>
    /// Formats InvalidOpeningToken diagnostic with source-specific details.
    /// </summary>
    /// <param name="token">Token diagnostic value.</param>
    /// <returns>Concise English diagnostic message.</returns>
    internal static string InvalidOpeningToken(string token) => $"Invalid opening token <ox:{token}>.";

    /// <summary>
    /// Formats MismatchedSlot diagnostic with source-specific details.
    /// </summary>
    /// <param name="token">Token diagnostic value.</param>
    /// <returns>Concise English diagnostic message.</returns>
    internal static string MismatchedSlot(string token) => $"Slot {token} differs from source.";

    /// <summary>
    /// Formats InvalidEscape diagnostic with source-specific details.
    /// </summary>
    /// <param name="character">Character diagnostic value.</param>
    /// <returns>Concise English diagnostic message.</returns>
    internal static string InvalidEscape(char character) => $"Invalid escape '\\{character}'; use '\\\\' or '\\<'.";

    /// <summary>
    /// Formats MissingClosingToken diagnostic with source-specific details.
    /// </summary>
    /// <param name="token">Token diagnostic value.</param>
    /// <returns>Concise English diagnostic message.</returns>
    internal static string MissingClosingToken(string token) => $"Missing closing token {token}.";

    /// <summary>
    /// Formats InvalidSlotWhitespace diagnostic with source-specific details.
    /// </summary>
    /// <param name="token">Token diagnostic value.</param>
    /// <returns>Concise English diagnostic message.</returns>
    internal static string InvalidSlotWhitespace(string token) => $"Slot {token} cannot contain line breaks or tabs in Word/PowerPoint.";

    /// <summary>
    /// Formats TotalCellTextLimit diagnostic with source-specific details.
    /// </summary>
    /// <param name="limit">Limit diagnostic value.</param>
    /// <returns>Concise English diagnostic message.</returns>
    internal static string TotalCellTextLimit(long limit) => $"Cell text exceeds {limit} characters.";

    /// <summary>
    /// Formats UnsupportedExtension diagnostic with source-specific details.
    /// </summary>
    /// <param name="extension">Extension diagnostic value.</param>
    /// <returns>Concise English diagnostic message.</returns>
    internal static string UnsupportedExtension(string extension) => $"Only {extension} files are supported.";

    /// <summary>
    /// InvalidEncoding diagnostic message.
    /// </summary>
    internal const string InvalidEncoding = "File must use valid UTF-8.";

    /// <summary>
    /// OutputSizeLimit diagnostic message.
    /// </summary>
    internal const string OutputSizeLimit = "Output exceeds size limit.";

    /// <summary>
    /// MissingOutputWorkbook diagnostic message.
    /// </summary>
    internal const string MissingOutputWorkbook = "Output workbook is missing.";

    /// <summary>
    /// MarkdownStructureChanged diagnostic message.
    /// </summary>
    internal const string MarkdownStructureChanged = "Protected Markdown structure changed.";

    /// <summary>
    /// MarkerIdsExhausted diagnostic message.
    /// </summary>
    internal const string MarkerIdsExhausted = "No marker IDs available.";

    /// <summary>
    /// InvalidFileSize diagnostic message.
    /// </summary>
    internal const string InvalidFileSize = "Invalid file size.";

    /// <summary>
    /// UnsupportedOlePackage diagnostic message.
    /// </summary>
    internal const string UnsupportedOlePackage = "OLE or encrypted files are unsupported.";

    /// <summary>
    /// InvalidZipFormat diagnostic message.
    /// </summary>
    internal const string InvalidZipFormat = "File is not a valid ZIP archive.";

    /// <summary>
    /// UnreadableZip diagnostic message.
    /// </summary>
    internal const string UnreadableZip = "ZIP archive is corrupt or unreadable.";

    /// <summary>
    /// MissingPackageManifest diagnostic message.
    /// </summary>
    internal const string MissingPackageManifest = "Missing [Content_Types].xml or _rels/.rels.";

    /// <summary>
    /// SignedPackageUnsupported diagnostic message.
    /// </summary>
    internal const string SignedPackageUnsupported = "Editing digitally signed packages is unsupported.";

    /// <summary>
    /// InvalidContentTypes diagnostic message.
    /// </summary>
    internal const string InvalidContentTypes = "Invalid content types XML.";

    /// <summary>
    /// InvalidOrOversizedXml diagnostic message.
    /// </summary>
    internal const string InvalidOrOversizedXml = "XML is invalid or exceeds character limit.";

    /// <summary>
    /// StrictOoxmlUnsupported diagnostic message.
    /// </summary>
    internal const string StrictOoxmlUnsupported = "Strict OOXML is unsupported.";

    /// <summary>
    /// InvalidMainContentType diagnostic message.
    /// </summary>
    internal const string InvalidMainContentType = "Missing or invalid main document content type.";

    /// <summary>
    /// SchemaValidationLimit diagnostic message.
    /// </summary>
    internal const string SchemaValidationLimit = "Schema validation limit exceeded.";

    /// <summary>
    /// ProtectedXmlChanged diagnostic message.
    /// </summary>
    internal const string ProtectedXmlChanged = "XML outside editable regions changed.";

    /// <summary>
    /// MissingOutputSlides diagnostic message.
    /// </summary>
    internal const string MissingOutputSlides = "Output slide list is missing.";

    /// <summary>
    /// MissingOutputBody diagnostic message.
    /// </summary>
    internal const string MissingOutputBody = "Output document body is missing.";

    /// <summary>
    /// InputSizeLimit diagnostic message.
    /// </summary>
    /// <param name="limit">Limit diagnostic value.</param>
    /// <returns>Concise English diagnostic message.</returns>
    internal static string InputSizeLimit(long limit) => $"File exceeds {limit} bytes.";

    /// <summary>
    /// ParagraphLimit diagnostic message.
    /// </summary>
    /// <param name="limit">Limit diagnostic value.</param>
    /// <returns>Concise English diagnostic message.</returns>
    internal static string ParagraphLimit(long limit) => $"File exceeds {limit} paragraphs.";

    /// <summary>
    /// PackageEntryLimit diagnostic message.
    /// </summary>
    /// <param name="actual">Actual diagnostic value.</param>
    /// <param name="limit">Limit diagnostic value.</param>
    /// <returns>Concise English diagnostic message.</returns>
    internal static string PackageEntryLimit(long actual, long limit) => $"Package entry count {actual} exceeds {limit}.";

    /// <summary>
    /// DuplicatePackageEntry diagnostic message.
    /// </summary>
    /// <param name="path">Path diagnostic value.</param>
    /// <returns>Concise English diagnostic message.</returns>
    internal static string DuplicatePackageEntry(string path) => $"Duplicate package path: {path}.";

    /// <summary>
    /// PartSizeLimit diagnostic message.
    /// </summary>
    /// <param name="actual">Actual diagnostic value.</param>
    /// <param name="limit">Limit diagnostic value.</param>
    /// <returns>Concise English diagnostic message.</returns>
    internal static string PartSizeLimit(long actual, long limit) => $"Part size {actual} exceeds {limit} bytes.";

    /// <summary>
    /// ExpandedSizeLimit diagnostic message.
    /// </summary>
    /// <param name="actual">Actual diagnostic value.</param>
    /// <param name="limit">Limit diagnostic value.</param>
    /// <returns>Concise English diagnostic message.</returns>
    internal static string ExpandedSizeLimit(long actual, long limit) => $"Expanded size {actual} exceeds {limit} bytes.";

    /// <summary>
    /// XmlDepthLimit diagnostic message.
    /// </summary>
    /// <param name="actual">Actual diagnostic value.</param>
    /// <param name="limit">Limit diagnostic value.</param>
    /// <returns>Concise English diagnostic message.</returns>
    internal static string XmlDepthLimit(long actual, long limit) => $"XML depth {actual} exceeds {limit}.";

    /// <summary>
    /// XmlNodeLimit diagnostic message.
    /// </summary>
    /// <param name="actual">Actual diagnostic value.</param>
    /// <param name="limit">Limit diagnostic value.</param>
    /// <returns>Concise English diagnostic message.</returns>
    internal static string XmlNodeLimit(long actual, long limit) => $"XML node count {actual} exceeds {limit}.";

    /// <summary>
    /// FormatMismatch diagnostic message.
    /// </summary>
    /// <param name="actual">Actual diagnostic value.</param>
    /// <param name="expected">Expected diagnostic value.</param>
    /// <returns>Concise English diagnostic message.</returns>
    internal static string FormatMismatch(string? actual, string? expected) => $"File format {actual} does not match {expected}.";

    /// <summary>
    /// SourceSchemaError diagnostic message.
    /// </summary>
    /// <param name="partUri">PartUri diagnostic value.</param>
    /// <param name="detail">Detail diagnostic value.</param>
    /// <returns>Concise English diagnostic message.</returns>
    internal static string SourceSchemaError(string partUri, string? detail) => $"Invalid source schema in {partUri}: {detail}";

    /// <summary>
    /// OutputSchemaError diagnostic message.
    /// </summary>
    /// <param name="partUri">PartUri diagnostic value.</param>
    /// <param name="detail">Detail diagnostic value.</param>
    /// <returns>Concise English diagnostic message.</returns>
    internal static string OutputSchemaError(string partUri, string? detail) => $"Invalid output schema in {partUri}: {detail}";

    /// <summary>
    /// OutputEntryCountMismatch diagnostic message.
    /// </summary>
    /// <param name="actual">Actual diagnostic value.</param>
    /// <param name="expected">Expected diagnostic value.</param>
    /// <returns>Concise English diagnostic message.</returns>
    internal static string OutputEntryCountMismatch(long actual, long expected) => $"Output entry count {actual} differs from source {expected}.";

    /// <summary>
    /// UntouchedPartChanged diagnostic message.
    /// </summary>
    /// <param name="partUri">PartUri diagnostic value.</param>
    /// <returns>Concise English diagnostic message.</returns>
    internal static string UntouchedPartChanged(string partUri) => $"Unchanged part {partUri} failed CRC verification.";

    /// <summary>
    /// MissingOutputPart diagnostic message.
    /// </summary>
    /// <param name="partUri">PartUri diagnostic value.</param>
    /// <returns>Concise English diagnostic message.</returns>
    internal static string MissingOutputPart(string partUri) => $"Output part {partUri} is missing.";

    /// <summary>
    /// OutputSlideCountMismatch diagnostic message.
    /// </summary>
    /// <param name="actual">Actual diagnostic value.</param>
    /// <param name="expected">Expected diagnostic value.</param>
    /// <returns>Concise English diagnostic message.</returns>
    internal static string OutputSlideCountMismatch(long actual, long expected) => $"Output slide count {actual} differs from source {expected}.";

    /// <summary>
    /// OutputTableCountMismatch diagnostic message.
    /// </summary>
    /// <param name="actual">Actual diagnostic value.</param>
    /// <param name="expected">Expected diagnostic value.</param>
    /// <returns>Concise English diagnostic message.</returns>
    internal static string OutputTableCountMismatch(long actual, long expected) => $"Output table count {actual} differs from source {expected}.";
}
