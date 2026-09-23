using FileHandler.Api.Common;

namespace FileHandler.Api.Modules.Markdown;

/// <summary>
/// Allocates internal restoration IDs independently of public wire syntax.
/// </summary>
internal sealed class MarkerAllocationContext
{

    /// <summary>
    /// Marker IDs unavailable for allocation.
    /// </summary>
    private readonly HashSet<int> _reserved;

    /// <summary>
    /// Next candidate marker ID.
    /// </summary>
    private int _next = 1;

    /// <summary>
    /// Creates allocator with reserved marker IDs.
    /// </summary>
    /// <param name="reserved">Marker IDs already used in source text.</param>
    public MarkerAllocationContext(HashSet<int> reserved) => _reserved = reserved;

    /// <summary>
    /// Allocates marker ID that does not collide with reserved IDs.
    /// </summary>
    /// <returns>Next available positive marker ID.</returns>
    public int AllocateId()
    {
        while (_reserved.Contains(_next))
        {
            if (_next == int.MaxValue)
                throw new InvalidOperationException(ProcessingMessages.MarkerIdsExhausted);
            _next++;
        }

        var value = _next;
        _reserved.Add(value);
        if (_next < int.MaxValue)
            _next++;
        return value;
    }
}

/// <summary>
/// Literal text segment or typed internal restoration boundary.
/// </summary>
/// <param name="IsMarker">Whether token is marker.</param>
/// <param name="Id">Restoration binding ID, or zero for literal text.</param>
/// <param name="Value">Literal content; empty for generated restoration boundaries.</param>
/// <param name="IsClosing">Whether token closes marker pair.</param>
internal sealed record MarkerToken(bool IsMarker, int Id, string Value, bool IsClosing);
