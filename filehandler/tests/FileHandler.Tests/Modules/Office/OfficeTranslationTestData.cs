using FileHandler.Api.Common;

namespace FileHandler.Tests.Modules.Office;

/// <summary>
/// Applies content translations while preserving worksheet name units in shared tests.
/// </summary>
internal static class OfficeTranslationTestData
{

    /// <summary>
    /// Returns content mapping independently from worksheet names.
    /// </summary>
    /// <param name="result">Imported source mapping.</param>
    /// <returns>Texts belonging to content units.</returns>
    internal static IReadOnlyList<string> ContentTexts(ImportResult result)
    {
        Assert.NotNull(result.Metadata.Units);
        Assert.Equal(result.Texts.Count, result.Metadata.Units.Count);
        return result.Metadata.Units.Where(u => u.Kind != "sheetName").Select(u => result.Texts[u.Index]).ToArray();
    }

    /// <summary>
    /// Builds complete translation batch from source mapping, retaining name units.
    /// </summary>
    /// <param name="service">Format service.</param>
    /// <param name="source">Original package bytes.</param>
    /// <param name="translations">Content translations with independent expected count.</param>
    /// <returns>Export result using original sheet names.</returns>
    internal static async Task<ExportResult> ExportContentAsync(IFileHandler service, byte[] source, IReadOnlyList<string> translations)
    {
        using var input = new MemoryStream(source);
        var imported = await service.ImportAsync(input, true, default);
        Assert.Empty(imported.Errors);
        var mapping = imported.Metadata.Units!.Where(u => u.Kind != "sheetName").ToArray();
        Assert.Equal(mapping.Length, translations.Count);
        var effective = imported.Texts.ToArray();
        for (var i = 0; i < mapping.Length; i++) effective[mapping[i].Index] = translations[i];
        using var outputSource = new MemoryStream(source);
        return await service.ExportAsync(outputSource, effective);
    }
}
