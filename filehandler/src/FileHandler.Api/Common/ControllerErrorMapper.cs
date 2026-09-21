using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace FileHandler.Api.Common;

/// <summary>
/// Maps file errors to appropriate HTTP status results.
/// </summary>
internal static class ControllerErrorMapper
{

    /// <summary>
    /// Maps validation errors to 413 Payload Too Large or 422 Unprocessable Entity result.
    /// </summary>
    /// <param name="errors">Validation errors encountered during operation.</param>
    /// <param name="metadata">Facts collected before failure.</param>
    /// <returns>HTTP action result containing metadata and fatal errors.</returns>
    internal static IActionResult ToActionResult(this IReadOnlyList<FileError> errors, FileMetadata? metadata = null)
    {
        var isPayloadLimit = errors.Any(x => x.Code is "file_too_large" or "too_many_units" or "translation_too_long" or "output_too_large"
            or "office_package_limit_exceeded" or "office_plan_limit_exceeded" or "office_translation_limit_exceeded" or "office_schema_limit_exceeded");
        var status = isPayloadLimit ? StatusCodes.Status413PayloadTooLarge : StatusCodes.Status422UnprocessableEntity;
        return new ObjectResult(new FileResponse((metadata ?? FileMetadata.Create("unknown")) with { Status = ProcessingStatus.Failed, Units = null }, errors)) { StatusCode = status };
    }
}
