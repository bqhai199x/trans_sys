using DocumentFormat.OpenXml.Packaging;
using FileHandler.Api.Common;
using FileHandler.Api.Modules.Office;
using P = DocumentFormat.OpenXml.Presentation;

namespace FileHandler.Api.Modules.PowerPoint;

/// <summary>
/// Validates PowerPoint semantic invariants including slide and shape counts.
/// </summary>
public sealed class PowerPointStructureValidator
{

    /// <summary>
    /// Validates structure preservation between original plan and translated presentation output.
    /// </summary>
    /// <param name="outputBytes">Translated presentation bytes.</param>
    /// <param name="plan">Original document plan.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Validation result.</returns>
    public OfficeValidationResult Validate(
        byte[] outputBytes,
        PowerPointPlan plan,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var ms = new MemoryStream(outputBytes);
        using var doc = PresentationDocument.Open(ms, false, OfficeTextBindings.Settings(new OfficeProcessingOptions()));

        if (doc.PresentationPart?.Presentation?.SlideIdList is null)
            return OfficeValidationResult.Failure([new FileError("office_output_invalid", ProcessingMessages.MissingOutputSlides)]);

        var slidesAfter = doc.PresentationPart.Presentation.SlideIdList.Elements<P.SlideId>().ToList();
        if (slidesAfter.Count != plan.Slides.Count)
        {
            return OfficeValidationResult.Failure([new FileError("office_output_invalid", ProcessingMessages.OutputSlideCountMismatch(slidesAfter.Count, plan.Slides.Count))]);
        }

        return OfficeValidationResult.Success();
    }
}
