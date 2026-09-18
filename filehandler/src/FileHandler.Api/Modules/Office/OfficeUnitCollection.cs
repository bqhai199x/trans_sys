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
    /// Maximum units permitted before retaining another unit.
    /// </summary>
    private readonly int _maxUnits;

    /// <summary>
    /// Aggregate binding references representing downstream patch work.
    /// </summary>
    private long _bindings;

    /// <summary>
    /// Creates an empty bounded plan collection.
    /// </summary>
    /// <param name="options">Active extraction budgets.</param>
    /// <param name="maxUnits">General file unit limit.</param>
    internal OfficeUnitCollection(OfficeProcessingOptions options, int maxUnits = int.MaxValue)
    {
        _options = options;
        _maxUnits = maxUnits;
    }

    /// <summary>
    /// Checks unit, token and character budgets before retaining a unit.
    /// </summary>
    /// <param name="index">Insertion index.</param>
    /// <param name="item">Completed extraction unit.</param>
    /// <returns>No return value.</returns>
    protected override void InsertItem(int index, OfficeTranslationUnit item)
    {
        if (Count >= _maxUnits) throw new FileLimitException("too_many_units");
        if (Count >= _options.MaxObjects || item.Slots.Count + item.Anchors.Count > _options.MaxTokensPerUnit ||
            item.EncodedSource.Length > _options.MaxPlanChars - _characters ||
            item.Bindings.Count > _options.MaxBindings - _bindings)
            throw new FileLimitException("office_plan_limit_exceeded");
        _characters += item.EncodedSource.Length;
        _bindings += item.Bindings.Count;
        base.InsertItem(index, item);
    }
}
