using DocumentFormat.OpenXml.Packaging;
using FileHandler.Api.Common;
using FileHandler.Api.Modules.Office;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace FileHandler.Api.Modules.Word;

/// <summary>
/// Validates Word semantic structure invariants before and after translation.
/// </summary>
public sealed class WordStructureValidator
{

    /// <summary>
    /// Validates structure preservation between original plan and translated document output.
    /// </summary>
    /// <param name="outputBytes">Translated document bytes.</param>
    /// <param name="plan">Original document plan.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Validation result.</returns>
    public OfficeValidationResult Validate(
        byte[] outputBytes,
        WordPlan plan,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var ms = new MemoryStream(outputBytes);
        using var doc = WordprocessingDocument.Open(ms, false, OfficeTextBindings.Settings(new OfficeProcessingOptions()));

        if (doc.MainDocumentPart?.Document?.Body is null)
            return OfficeValidationResult.Failure([new FileError("office_output_invalid", ProcessingMessages.MissingOutputBody)]);

        var roots = new OpenXmlPart[] { doc.MainDocumentPart }
            .Concat(doc.MainDocumentPart.HeaderParts).Concat(doc.MainDocumentPart.FooterParts)
            .Concat(new OpenXmlPart?[] { doc.MainDocumentPart.FootnotesPart, doc.MainDocumentPart.EndnotesPart }.OfType<OpenXmlPart>())
            .Where(p => plan.Stories.Any(s => s.PartUri == p.Uri.ToString()));
        var tablesAfter = roots.SelectMany(p => p.RootElement!.Descendants<W.Table>()).ToList();
        if (tablesAfter.Count != plan.Tables.Count)
        {
            return OfficeValidationResult.Failure([new FileError("office_output_invalid", ProcessingMessages.OutputTableCountMismatch(tablesAfter.Count, plan.Tables.Count))]);
        }

        return OfficeValidationResult.Success();
    }
}
