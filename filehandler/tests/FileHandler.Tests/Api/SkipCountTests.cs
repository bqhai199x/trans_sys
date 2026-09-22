using System.Text.Json;
using FileHandler.Api.Common;

namespace FileHandler.Tests.Api;

/// <summary>
/// Verifies object totals survive informational visibility filtering and serialization.
/// </summary>
public sealed class SkipCountTests
{

    /// <summary>
    /// Counts objects in grouped skips rather than counting entries or translation units.
    /// </summary>
    /// <returns>No return value.</returns>
    [Fact]
    public void Projection_RetainsGroupedTotalsWithoutMutatingInternalFacts()
    {
        var source = FileMetadata.Create("excel") with
        {
            Skipped =
            [
                new("hidden_row", "info", "extraction", "row", 3, "Rows", new(SheetId: "1")),
                new("formula_cell", "info", "extraction", "cell", 2, "Cells", new(SheetId: "1")),
                new("unsupported_graphic_frame", "warning", "extraction", "shape", 4, "Shapes", new(SheetId: "1"))
            ]
        };
        var ordinary = source.ForResponse(false);
        Assert.Equal(new SkipCounts(4, 5), ordinary.SkipCount);
        Assert.Single(ordinary.Skipped);
        Assert.Equal(3, source.Skipped.Count);
        Assert.Equal(new SkipCounts(4, 5), source.ForResponse(true).SkipCount);
        Assert.Equal(new SkipCounts(4, 5), ordinary.ForResponse(false).SkipCount);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(ordinary, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal(5, json.RootElement.GetProperty("skipCount").GetProperty("info").GetInt64());
        Assert.Equal(4, json.RootElement.GetProperty("skipCount").GetProperty("warning").GetInt64());
    }
}
