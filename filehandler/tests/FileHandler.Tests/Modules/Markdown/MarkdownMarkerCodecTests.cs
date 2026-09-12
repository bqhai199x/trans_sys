using FileHandler.Api.Modules.Markdown;

namespace FileHandler.Tests.Modules.Markdown;

/// <summary>
/// Unit tests for Markdown preservation marker encoding and parsing.
/// </summary>
public sealed class MarkdownMarkerCodecTests
{

    /// <summary>
    /// Verifies that literal marker IDs are reserved, including leading zeros.
    /// </summary>
    /// <returns>No return value.</returns>
    [Fact]
    public void ReservesLiteralIdsIncludingLeadingZeros()
    {
        var ids = MarkdownMarkerCodec.FindReservedIds("x <keepme1> y <keepme0002/> z <keepme999999999999999999>");
        Assert.Contains(1, ids);
        Assert.Contains(2, ids);
        var allocation = new MarkerAllocationContext(ids);
        Assert.Equal(3, allocation.AllocateId());
    }
}
