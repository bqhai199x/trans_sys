namespace FileHandler.Api.Common;

/// <summary>
/// Stable public reasons for preserving source content.
/// </summary>
internal static class SkipCodes
{

    /// <summary>
    /// sheet_not_selected value in public metadata.
    /// </summary>
    internal const string SheetNotSelected = "sheet_not_selected";

    /// <summary>
    /// slide_not_selected value in public metadata.
    /// </summary>
    internal const string SlideNotSelected = "slide_not_selected";

    /// <summary>
    /// unsupported_sheet value in public metadata.
    /// </summary>
    internal const string UnsupportedSheet = "unsupported_sheet";

    /// <summary>
    /// unsafe_sheet_reference value in public metadata.
    /// </summary>
    internal const string UnsafeSheetReference = "unsafe_sheet_reference";

    /// <summary>
    /// hidden_row value in public metadata.
    /// </summary>
    internal const string HiddenRow = "hidden_row";

    /// <summary>
    /// hidden_column value in public metadata.
    /// </summary>
    internal const string HiddenColumn = "hidden_column";

    /// <summary>
    /// implicit_cell_address value in public metadata.
    /// </summary>
    internal const string ImplicitCellAddress = "implicit_cell_address";

    /// <summary>
    /// protected_table_cell value in public metadata.
    /// </summary>
    internal const string ProtectedTableCell = "protected_table_cell";

    /// <summary>
    /// formula_cell value in public metadata.
    /// </summary>
    internal const string FormulaCell = "formula_cell";

    /// <summary>
    /// non_text_cell value in public metadata.
    /// </summary>
    internal const string NonTextCell = "non_text_cell";

    /// <summary>
    /// whitespace_cell value in public metadata.
    /// </summary>
    internal const string WhitespaceCell = "whitespace_cell";

    /// <summary>
    /// phonetic_content value in public metadata.
    /// </summary>
    internal const string PhoneticContent = "phonetic_content";

    /// <summary>
    /// merged_follower_text value in public metadata.
    /// </summary>
    internal const string MergedFollowerText = "merged_follower_text";

    /// <summary>
    /// unsupported_graphic_frame value in public metadata.
    /// </summary>
    internal const string UnsupportedGraphicFrame = "unsupported_graphic_frame";

    /// <summary>
    /// protected_content_control value in public metadata.
    /// </summary>
    internal const string ProtectedContentControl = "protected_content_control";

    /// <summary>
    /// unsupported_revision value in public metadata.
    /// </summary>
    internal const string UnsupportedRevision = "unsupported_revision";

    /// <summary>
    /// unsupported_ruby value in public metadata.
    /// </summary>
    internal const string UnsupportedRuby = "unsupported_ruby";

    /// <summary>
    /// unsupported_altchunk value in public metadata.
    /// </summary>
    internal const string UnsupportedAltchunk = "unsupported_altchunk";

    /// <summary>
    /// unsupported_alternate_content value in public metadata.
    /// </summary>
    internal const string UnsupportedAlternateContent = "unsupported_alternate_content";

    /// <summary>
    /// merged_follower value in public metadata.
    /// </summary>
    internal const string MergedFollower = "merged_follower";

    /// <summary>
    /// cross_paragraph_field value in public metadata.
    /// </summary>
    internal const string CrossParagraphField = "cross_paragraph_field";

    /// <summary>
    /// unbounded_field_story value in public metadata.
    /// </summary>
    internal const string UnboundedFieldStory = "unbounded_field_story";

    /// <summary>
    /// protected_inline value in public metadata.
    /// </summary>
    internal const string ProtectedInline = "protected_inline";

    /// <summary>
    /// unsupported_drawing value in public metadata.
    /// </summary>
    internal const string UnsupportedDrawing = "unsupported_drawing";

    /// <summary>
    /// unreferenced_story value in public metadata.
    /// </summary>
    internal const string UnreferencedStory = "unreferenced_story";

    /// <summary>
    /// system_note value in public metadata.
    /// </summary>
    internal const string SystemNote = "system_note";

    /// <summary>
    /// unsupported_block value in public metadata.
    /// </summary>
    internal const string UnsupportedBlock = "unsupported_block";

    /// <summary>
    /// protected_code_block value in public metadata.
    /// </summary>
    internal const string ProtectedCodeBlock = "protected_code_block";

    /// <summary>
    /// unsupported_mermaid value in public metadata.
    /// </summary>
    internal const string UnsupportedMermaid = "unsupported_mermaid";

    /// <summary>
    /// protected_block value in public metadata.
    /// </summary>
    internal const string ProtectedBlock = "protected_block";

    /// <summary>
    /// empty_translation value in public metadata.
    /// </summary>
    internal const string EmptyTranslation = "empty_translation";

    /// <summary>
    /// invalid_translation value in public metadata.
    /// </summary>
    internal const string InvalidTranslation = "invalid_translation";

    /// <summary>
    /// internal_anchor_change_unsupported value in public metadata.
    /// </summary>
    internal const string InternalAnchorChangeUnsupported = "internal_anchor_change_unsupported";

    /// <summary>
    /// invalid_structure value in public metadata.
    /// </summary>
    internal const string InvalidStructure = "invalid_structure";

    /// <summary>
    /// invalid_marker_syntax value in public metadata.
    /// </summary>
    internal const string InvalidMarkerSyntax = "invalid_marker_syntax";

    /// <summary>
    /// protected_marker_not_empty value in public metadata.
    /// </summary>
    internal const string ProtectedMarkerNotEmpty = "protected_marker_not_empty";

    /// <summary>
    /// unexpected_marker value in public metadata.
    /// </summary>
    internal const string UnexpectedMarker = "unexpected_marker";

    /// <summary>
    /// duplicate_marker value in public metadata.
    /// </summary>
    internal const string DuplicateMarker = "duplicate_marker";

    /// <summary>
    /// invalid_marker_nesting value in public metadata.
    /// </summary>
    internal const string InvalidMarkerNesting = "invalid_marker_nesting";

    /// <summary>
    /// missing_marker value in public metadata.
    /// </summary>
    internal const string MissingMarker = "missing_marker";

    /// <summary>
    /// office_token_mismatch value in public metadata.
    /// </summary>
    internal const string OfficeTokenMismatch = "office_token_mismatch";
}
