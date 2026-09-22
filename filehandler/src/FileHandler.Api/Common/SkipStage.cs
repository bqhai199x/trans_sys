namespace FileHandler.Api.Common;

/// <summary>
/// Processing stages that can preserve source regions.
/// </summary>
internal static class SkipStage
{

    /// <summary>
    /// selection value in public metadata.
    /// </summary>
    internal const string Selection = "selection";

    /// <summary>
    /// extraction value in public metadata.
    /// </summary>
    internal const string Extraction = "extraction";

    /// <summary>
    /// translation value in public metadata.
    /// </summary>
    internal const string Translation = "translation";

    /// <summary>
    /// rename value in public metadata.
    /// </summary>
    internal const string Rename = "rename";
}
