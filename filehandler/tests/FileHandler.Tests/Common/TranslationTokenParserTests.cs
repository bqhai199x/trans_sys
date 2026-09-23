using System.Text.Json;
using FileHandler.Api.Common;

namespace FileHandler.Tests.Common;

/// <summary>
/// Cross-runtime conformance for canonical token grammar and movement.
/// </summary>
public sealed class TranslationTokenParserTests
{

    /// <summary>
    /// Checks shared source and candidate fixtures.
    /// </summary>
    /// <returns>No return value.</returns>
    [Fact]
    public void SharedFixturesAgree()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "token-fixtures.json")));
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var error = TranslationTokenParser.Validate(item.GetProperty("source").GetString()!, item.GetProperty("candidate").GetString()!, item.GetProperty("fixed_order").GetBoolean());
            Assert.Equal(item.GetProperty("error").GetString(), error);
        }
    }
}
