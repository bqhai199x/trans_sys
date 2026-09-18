using System.Collections.ObjectModel;
using FileHandler.Api.Common;

namespace FileHandler.Api.Modules.Office;

/// <summary>
/// Rejects extraction growth before retaining units beyond configured budgets.
/// </summary>
internal sealed class OfficeUnitCollection : Collection<OfficeTranslationUnit>, IReadOnlyList<OfficeTranslationUnit>
{

    /// <summary>
    /// Active extraction budgets.
    /// </summary>
    private readonly OfficeProcessingOptions _options;

    /// <summary>
    /// Aggregate encoded characters retained by this plan.
    /// </summary>
    private long _characters;

    /// <summary>
    /// Creates an empty bounded plan collection.
    /// </summary>
    /// <param name="options">Active extraction budgets.</param>
    internal OfficeUnitCollection(OfficeProcessingOptions options) => _options = options;

    /// <summary>
    /// Checks unit, token and character budgets before retaining a unit.
    /// </summary>
    /// <param name="index">Insertion index.</param>
    /// <param name="item">Completed extraction unit.</param>
    /// <returns>No return value.</returns>
    protected override void InsertItem(int index, OfficeTranslationUnit item)
    {
        if (Count >= _options.MaxObjects || item.Slots.Count + item.Anchors.Count > _options.MaxTokensPerUnit ||
            item.EncodedSource.Length > _options.MaxPlanChars - _characters)
            throw new FileLimitException("office_plan_limit_exceeded");
        _characters += item.EncodedSource.Length;
        base.InsertItem(index, item);
    }
}
