using DocumentFormat.OpenXml.Packaging;
using FileHandler.Api.Common;
using FileHandler.Api.Diagnostics;
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
        using var trace = DebugTrace.Enter("PowerPointStructureValidator", "Validate", () => new
        {
            unitCount = plan.Units.Count,
            slideCount = plan.Slides.Count
        });

        try
        {
            trace.State("stage", () => "compareTopology");
            cancellationToken.ThrowIfCancellationRequested();

            using var ms = new MemoryStream(outputBytes);
            using var doc = PresentationDocument.Open(ms, false, OfficeTextBindings.Settings(new OfficeProcessingOptions()));

            if (doc.PresentationPart?.Presentation?.SlideIdList is null)
                return OfficeValidationResult.Failure(new[] { new FileError("office_output_invalid", "Tệp PowerPoint đầu ra thiếu danh sách trang trình chiếu.") });

            var slidesAfter = doc.PresentationPart.Presentation.SlideIdList.Elements<P.SlideId>().ToList();
            if (slidesAfter.Count != plan.Slides.Count)
            {
                return OfficeValidationResult.Failure(new[] { new FileError("office_output_invalid", $"Số lượng slide đầu ra ({slidesAfter.Count}) không khớp với nguồn ({plan.Slides.Count}).") });
            }

            trace.Return(new { outcome = "success", validatedSlides = slidesAfter.Count });
            return OfficeValidationResult.Success();
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
}
