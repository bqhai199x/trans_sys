namespace FileHandler.Api.Modules.Office;

/// <summary>
/// Configuration options governing Office document limits and validation thresholds.
/// </summary>
public sealed class OfficeProcessingOptions
{

    /// <summary>
    /// Configuration section name within application settings.
    /// </summary>
    public const string SectionName = "OfficeProcessing";

    /// <summary>
    /// Maximum allowed number of package ZIP entries.
    /// </summary>
    public int MaxPackageEntries { get; init; } = 2000;

    /// <summary>
    /// Maximum allowed number of total package relationships.
    /// </summary>
    public int MaxRelationships { get; init; } = 20000;

    /// <summary>
    /// Maximum allowed total decompressed bytes across all package parts.
    /// </summary>
    public long MaxExpandedBytes { get; init; } = 104857600L;

    /// <summary>
    /// Maximum allowed decompressed byte count for a single package part.
    /// </summary>
    public long MaxPartBytes { get; init; } = 33554432L;

    /// <summary>
    /// Maximum allowed character count in XML stream per part.
    /// </summary>
    public long MaxXmlCharactersPerPart { get; init; } = 8000000L;

    /// <summary>
    /// Maximum allowed total XML node events across entire package.
    /// </summary>
    public long MaxXmlNodes { get; init; } = 1000000L;

    /// <summary>
    /// Maximum allowed XML element nesting depth.
    /// </summary>
    public int MaxXmlDepth { get; init; } = 64;

    /// <summary>
    /// Maximum XML elements across package, guarding object allocation before extraction.
    /// </summary>
    public int MaxObjects { get; init; } = 100000;

    /// <summary>
    /// Maximum allowed total characters stored in extraction plan.
    /// </summary>
    public long MaxPlanChars { get; init; } = 2000000L;

    /// <summary>
    /// Maximum allowed total characters across all translations.
    /// </summary>
    public long MaxTotalTranslationChars { get; init; } = 2000000L;

    /// <summary>
    /// Maximum allowed total tokens (slots plus anchors) per unit.
    /// </summary>
    public int MaxTokensPerUnit { get; init; } = 1024;

    /// <summary>
    /// Product cap on decoded plain text characters in single Excel cell.
    /// </summary>
    public int MaxCellTextChars { get; init; } = 32767;

    /// <summary>
    /// Maximum allowed errors accumulated before early stopping.
    /// </summary>
    public int MaxErrors { get; init; } = 100;

    /// <summary>
    /// Maximum allowed schema validation errors before aborting.
    /// </summary>
    public int MaxSchemaErrors { get; init; } = 100;

    /// <summary>
    /// Validates configured limits ensuring positive values and sound relationships.
    /// </summary>
    /// <returns>No return value.</returns>
    /// <exception cref="InvalidOperationException">One or more configured limits are invalid.</exception>
    public void Validate()
    {
        if (MaxPackageEntries <= 0)
            throw new InvalidOperationException($"{nameof(MaxPackageEntries)} must be positive.");
        if (MaxRelationships <= 0)
            throw new InvalidOperationException($"{nameof(MaxRelationships)} must be positive.");
        if (MaxExpandedBytes <= 0)
            throw new InvalidOperationException($"{nameof(MaxExpandedBytes)} must be positive.");
        if (MaxPartBytes <= 0)
            throw new InvalidOperationException($"{nameof(MaxPartBytes)} must be positive.");
        if (MaxPartBytes > MaxExpandedBytes)
            throw new InvalidOperationException($"{nameof(MaxPartBytes)} cannot exceed {nameof(MaxExpandedBytes)}.");
        if (MaxXmlCharactersPerPart <= 0)
            throw new InvalidOperationException($"{nameof(MaxXmlCharactersPerPart)} must be positive.");
        if (MaxXmlNodes <= 0)
            throw new InvalidOperationException($"{nameof(MaxXmlNodes)} must be positive.");
        if (MaxXmlDepth <= 0)
            throw new InvalidOperationException($"{nameof(MaxXmlDepth)} must be positive.");
        if (MaxObjects <= 0)
            throw new InvalidOperationException($"{nameof(MaxObjects)} must be positive.");
        if (MaxPlanChars <= 0)
            throw new InvalidOperationException($"{nameof(MaxPlanChars)} must be positive.");
        if (MaxTotalTranslationChars <= 0)
            throw new InvalidOperationException($"{nameof(MaxTotalTranslationChars)} must be positive.");
        if (MaxTokensPerUnit <= 0)
            throw new InvalidOperationException($"{nameof(MaxTokensPerUnit)} must be positive.");
        if (MaxCellTextChars <= 0)
            throw new InvalidOperationException($"{nameof(MaxCellTextChars)} must be positive.");
        if (MaxErrors <= 0)
            throw new InvalidOperationException($"{nameof(MaxErrors)} must be positive.");
        if (MaxSchemaErrors <= 0)
            throw new InvalidOperationException($"{nameof(MaxSchemaErrors)} must be positive.");
    }
}
