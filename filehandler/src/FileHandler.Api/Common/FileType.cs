namespace FileHandler.Api.Common;

/// <summary>
/// Supported source file formats.
/// </summary>
public enum FileType
{

    /// <summary>
    /// Markdown source document.
    /// </summary>
    Markdown,

    /// <summary>
    /// UTF-8 plain text document.
    /// </summary>
    PlainText,

    /// <summary>
    /// Microsoft Word document (.docx).
    /// </summary>
    Word,

    /// <summary>
    /// Microsoft Excel spreadsheet (.xlsx).
    /// </summary>
    Excel,

    /// <summary>
    /// Microsoft PowerPoint presentation (.pptx).
    /// </summary>
    PowerPoint
}
