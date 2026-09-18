using System.Security.Cryptography;
using System.Text;
using DocumentFormat.OpenXml;
using A = DocumentFormat.OpenXml.Drawing;

namespace FileHandler.Api.Modules.Office;

/// <summary>
/// Extracts and parses text templates from DrawingML paragraphs.
/// </summary>
public static class DrawingTextCodec
{

    /// <summary>
    /// Reads DrawingML paragraph into an Office text template.
    /// </summary>
    /// <param name="paragraph">DrawingML paragraph to inspect.</param>
    /// <param name="parentLocation">Location of containing shape or cell.</param>
    /// <param name="paragraphOrdinal">Ordinal index of paragraph within container.</param>
    /// <param name="limits">Active template quotas, or defaults.</param>
    /// <returns>Extracted text template, or null if paragraph contains no translatable text.</returns>
    public static OfficeTextTemplate? ReadParagraph(
        A.Paragraph paragraph,
        OfficeLocation parentLocation,
        int paragraphOrdinal,
        OfficeProcessingOptions? limits = null)
    {
        var builder = new OfficeTemplateBuilder(limits);
        foreach (var child in paragraph.ChildElements)
        {
            if (child is A.Run run && run.GetFirstChild<A.Text>() is { } text)
                builder.Text(text, parentLocation.PartUri, run.RunProperties?.OuterXml ?? "");
            else if (child is A.Break)
                builder.Anchor(child, AnchorKind.Break);
            else if (child is A.Field)
                builder.Anchor(child, AnchorKind.Field);
        }
        return builder.Build();
    }

}
