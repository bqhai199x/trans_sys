using System.Text;
using FileHandler.Api.Common;
using FileHandler.Api.Modules.PlainText;
using Microsoft.Extensions.Options;

namespace FileHandler.Tests.Modules.PlainText;

/// <summary>
/// Plain text import and export acceptance, preservation and boundary tests.
/// </summary>
public sealed class PlainTextServiceTests
{

    /// <summary>
    /// Creates service with optional custom resource limits.
    /// </summary>
    /// <param name="options">Configured limits, or defaults when null.</param>
    /// <returns>Plain text service under test.</returns>
    private static PlainTextService Create(FileHandlingOptions? options = null) => new(Options.Create(options ?? new()));

    /// <summary>
    /// Imports UTF-8 text through an owned temporary stream.
    /// </summary>
    /// <param name="service">Service under test.</param>
    /// <param name="source">Decoded source text.</param>
    /// <returns>Import result from source text.</returns>
    private static async Task<ImportResult> Import(PlainTextService service, string source)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(source));
        return await service.ImportAsync(stream);
    }

    /// <summary>
    /// Exports UTF-8 text through an owned temporary stream.
    /// </summary>
    /// <param name="service">Service under test.</param>
    /// <param name="source">Decoded source text.</param>
    /// <param name="translations">Translated paragraphs.</param>
    /// <returns>Export result from source text and supplied translations.</returns>
    private static async Task<ExportResult> Export(PlainTextService service, string source, params string[] translations)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(source));
        return await service.ExportAsync(stream, translations);
    }

    /// <summary>
    /// Verifies full import and translated export contract without syntax interpretation.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task ImportsParagraphsAndExportsLiteralTranslations()
    {
        const string source = "Hello world.\r\nThis is line two.\r\n\r\n# This is plain text.\r\n";
        var service = Create();
        var imported = await Import(service, source);
        Assert.Empty(imported.Errors);
        Assert.Equal(new[] { "Hello world.\r\nThis is line two.", "# This is plain text." }, imported.Texts);
        var exported = await Export(service, source, "Xin chào.\n\nThêm một đoạn.", "# <keepme01> **Văn bản** &copy;");
        Assert.Empty(exported.Errors);
        Assert.Equal(PlainTextService.ContentType, exported.ContentType);
        Assert.Equal("Xin chào.\n\nThêm một đoạn.\r\n\r\n# <keepme01> **Văn bản** &copy;\r\n", Encoding.UTF8.GetString(exported.Content!));
    }

    /// <summary>
    /// Verifies identity export retains every source byte with and without BOM.
    /// </summary>
    /// <param name="text">Source including edge-case whitespace or literal syntax.</param>
    /// <returns>Task representing test completion.</returns>
    [Theory]
    [InlineData("")]
    [InlineData(" \t\r\n\r\n\u00a0")]
    [InlineData("one")]
    [InlineData("one\r\n")]
    [InlineData("\r\n \t\r\n  Chào 😀e\u0301 \t\r\nB\n\n# hi\r  \t")]
    [InlineData("```\n[link](https://example.com)\n```\n\n<keepme1>\n\n123")]
    public async Task IdentityIsBytePerfect(string text)
    {
        foreach (var bom in new[] { false, true })
        {
            var bytes = Utf8TextReader.Encode(text, bom);
            var service = Create();
            using var original = new MemoryStream(bytes);
            var imported = await service.ImportAsync(original);
            Assert.Empty(imported.Errors);
            using var source = new MemoryStream(bytes);
            var exported = await service.ExportAsync(source, imported.Texts);
            Assert.Empty(exported.Errors);
            Assert.Equal(bytes, exported.Content);
            Assert.True(original.CanRead);
            Assert.True(source.CanRead);
        }
    }

    /// <summary>
    /// Verifies repeated paragraphs are replaced by position and unchanged spans retain bytes.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task PreservesUntouchedSpansAroundChangedAndUnchangedParagraphs()
    {
        const string source = "\r\n  same \t\r\n \t\n  same \t\r\r  same \t\n\nlast\r\n";
        using var stream = new MemoryStream(Utf8TextReader.Encode(source, true));
        var result = await Create().ExportAsync(stream, ["  same \t", "  khác 😀\nnew  ", "  same \t", "cuối"]);
        Assert.Empty(result.Errors);
        Assert.Equal(Utf8TextReader.Encode("\r\n  same \t\r\n \t\n  khác 😀\nnew  \r\r  same \t\n\ncuối\r\n", true), result.Content);
    }

    /// <summary>
    /// Verifies invalid translations fail atomically with source locations.
    /// </summary>
    /// <param name="translation">Invalid second translation.</param>
    /// <param name="code">Expected validation error code.</param>
    /// <returns>Task representing test completion.</returns>
    [Theory]
    [InlineData(null, "invalid_translation")]
    [InlineData("", "empty_translation")]
    [InlineData(" \t\u00a0", "empty_translation")]
    [InlineData("longer", "translation_too_long")]
    public async Task RejectsInvalidTranslationsWithLocations(string? translation, string code)
    {
        var result = await Export(Create(new() { MaxTranslationChars = 4 }), "A\r\n\r\nB\nC", "ok", translation!);
        if (code == "empty_translation")
        {
            Assert.Empty(result.Errors);
            Assert.Equal("ok\r\n\r\nB\nC", Encoding.UTF8.GetString(result.Content!));
            var skipped = Assert.Single(result.Metadata.Skipped);
            Assert.Equal(code, skipped.Code);
            Assert.Equal(1, skipped.UnitIndex);
            Assert.Equal(new SourceLineRange(3, 4), skipped.Location.Line);
            return;
        }
        Assert.Null(result.Content);
        var error = Assert.Single(result.Errors);
        Assert.Equal(code, error.Code);
        Assert.Equal(1, error.Index);
        Assert.Equal(new SourceLineRange(3, 4), error.Line);
        Assert.Null(error.Marker);
    }

    /// <summary>
    /// Verifies malformed UTF-16 passed directly to service returns a validation error.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task RejectsLoneSurrogatesWithoutThrowing()
    {
        var result = await Export(Create(), "A", "\ud800");
        Assert.Empty(result.Errors);
        Assert.Equal("A", Encoding.UTF8.GetString(result.Content!));
        Assert.Equal("invalid_translation", Assert.Single(result.Metadata.Skipped).Code);
    }

    /// <summary>
    /// Verifies missing or excess translations cannot produce partial output.
    /// </summary>
    /// <param name="count">Supplied translation count.</param>
    /// <returns>Task representing test completion.</returns>
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task RejectsCountMismatch(int count)
    {
        var result = await Export(Create(), "A", Enumerable.Repeat("B", count).ToArray());
        Assert.Null(result.Content);
        Assert.Equal("translation_count_mismatch", Assert.Single(result.Errors).Code);
    }

    /// <summary>
    /// Verifies UTF-16 character and unit boundaries are inclusive.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task EnforcesUnitAndTranslationBoundaries()
    {
        var service = Create(new() { MaxUnits = 2, MaxTranslationChars = 2 });
        Assert.Empty((await Export(service, "A\n\nB", "😀", "é")).Errors);
        Assert.Equal("translation_too_long", Assert.Single((await Export(service, "A", "😀a")).Errors).Code);
        var imported = await Import(service, "A\n\nB\n\nC");
        Assert.Empty(imported.Texts);
        Assert.Equal("too_many_units", Assert.Single(imported.Errors).Code);
        var exported = await Export(service, "A\n\nB\n\nC", "A", "B", "C");
        Assert.Null(exported.Content);
        Assert.Equal("too_many_units", Assert.Single(exported.Errors).Code);
    }

    /// <summary>
    /// Verifies output byte limit includes BOM, source separators and multibyte translations.
    /// </summary>
    /// <param name="limit">Allowed output byte count.</param>
    /// <param name="success">Whether output should fit.</param>
    /// <returns>Task representing test completion.</returns>
    [Theory]
    [InlineData(12, true)]
    [InlineData(11, false)]
    public async Task EnforcesExactOutputByteBudget(int limit, bool success)
    {
        using var stream = new MemoryStream(Utf8TextReader.Encode("A\r\n\r\nB\n", true));
        var result = await Create(new() { MaxOutputBytes = limit }).ExportAsync(stream, ["é", "é"]);
        if (success)
        {
            Assert.Empty(result.Errors);
            Assert.Equal(Utf8TextReader.Encode("é\r\n\r\né\n", true), result.Content);
        }
        else
        {
            Assert.Null(result.Content);
            Assert.Equal("output_too_large", Assert.Single(result.Errors).Code);
        }
    }

    /// <summary>
    /// Verifies identity and whitespace-only outputs still obey output limit.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task IdentityDoesNotBypassLimits()
    {
        Assert.Equal("output_too_large", Assert.Single((await Export(Create(new() { MaxOutputBytes = 0 }), "A", "A")).Errors).Code);
        Assert.Equal("output_too_large", Assert.Single((await Export(Create(new() { MaxOutputBytes = 0 }), " \n")).Errors).Code);
        using var bomOnly = new MemoryStream(Encoding.UTF8.Preamble.ToArray());
        var result = await Create(new() { MaxOutputBytes = 2 }).ExportAsync(bomOnly, []);
        Assert.Null(result.Content);
        Assert.Equal("output_too_large", Assert.Single(result.Errors).Code);
    }

    /// <summary>
    /// Verifies source length does not silently split or truncate paragraphs.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task LongSourceParagraphCanImportButIdentityMustMeetTranslationLimit()
    {
        var service = Create(new() { MaxTranslationChars = 3 });
        var imported = await Import(service, "long source");
        Assert.Equal("long source", Assert.Single(imported.Texts));
        Assert.Empty(imported.Errors);
        Assert.Equal("translation_too_long", Assert.Single((await Export(service, "long source", "long source")).Errors).Code);
        Assert.Empty((await Export(service, "long source", "new")).Errors);
    }

    /// <summary>
    /// Verifies source size and encoding errors propagate through both workflows.
    /// </summary>
    /// <param name="sizeLimit">Whether input is oversized instead of malformed.</param>
    /// <returns>Task representing test completion.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PropagatesSourceErrors(bool sizeLimit)
    {
        var service = Create(new() { MaxFileBytes = sizeLimit ? 0 : 10 });
        byte[] bytes = sizeLimit ? [65] : [0xff];
        var code = sizeLimit ? "file_too_large" : "invalid_encoding";
        using var import = new MemoryStream(bytes);
        using var export = new MemoryStream(bytes);
        var imported = await service.ImportAsync(import);
        var exported = await service.ExportAsync(export, []);
        Assert.Empty(imported.Texts);
        Assert.Equal(code, Assert.Single(imported.Errors).Code);
        Assert.Null(exported.Content);
        Assert.Equal(code, Assert.Single(exported.Errors).Code);
    }

    /// <summary>
    /// Verifies aggregate output budget rejects large translations without allocating full output.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task RejectsOversizedAggregateBeforeAllocatingOutput()
    {
        var source = string.Join("\n\n", Enumerable.Repeat("A", 1_000));
        var translations = Enumerable.Repeat(new string('B', 100_000), 1_000).ToArray();
        var service = Create();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var result = await Export(service, source, translations);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Null(result.Content);
        Assert.Equal("output_too_large", Assert.Single(result.Errors).Code);
        Assert.InRange(allocated, 0, 5_000_000);
    }

    /// <summary>
    /// Verifies singleton service has no shared request state.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task ConcurrentRequestsRemainIsolated()
    {
        var service = Create();
        await Parallel.ForEachAsync(Enumerable.Range(0, 20), async (index, cancellationToken) =>
        {
            var source = $"Source {index}\n\nTail";
            var imported = await Import(service, source);
            Assert.Equal(new[] { $"Source {index}", "Tail" }, imported.Texts);
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(source));
            var exported = await service.ExportAsync(stream, [$"Dịch {index}", "Cuối"], cancellationToken);
            Assert.Empty(exported.Errors);
            Assert.Equal($"Dịch {index}\n\nCuối", Encoding.UTF8.GetString(exported.Content!));
        });
    }

    /// <summary>
    /// Verifies both workflows propagate cancellation rather than returning file errors.
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task PropagatesCancellation()
    {
        var service = Create();
        using var import = new MemoryStream([]);
        using var export = new MemoryStream([]);
        var token = new CancellationToken(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ImportAsync(import, token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ExportAsync(export, [], token));
    }

    /// <summary>
    /// Verifies cancellation propagates during validation, byte counting and composition.
    /// </summary>
    /// <param name="cancelAfter">Translation access at which cancellation is requested.</param>
    /// <returns>Task representing test completion.</returns>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public async Task ObservesCancellationDuringExport(int cancelAfter)
    {
        using var cancellation = new CancellationTokenSource();
        using var source = new MemoryStream(Encoding.UTF8.GetBytes("A\n\nB"));
        var translations = new CancellingTranslations(cancellation, cancelAfter);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Create().ExportAsync(source, translations, cancellation.Token));
        Assert.True(source.CanRead);
    }

    /// <summary>
    /// Supplies translations and requests cancellation at a deterministic processing boundary.
    /// </summary>
    /// <param name="cancellation">Cancellation source shared with export.</param>
    /// <param name="cancelAfter">One-based translation read that requests cancellation.</param>
    private sealed class CancellingTranslations(CancellationTokenSource cancellation, int cancelAfter) : IReadOnlyList<string>
    {

        /// <summary>
        /// Number of translation indexer reads.
        /// </summary>
        private int _reads;

        /// <summary>
        /// Number of paragraphs in test source.
        /// </summary>
        public int Count => 2;

        /// <summary>
        /// Valid translated text, requesting cancellation at configured read.
        /// </summary>
        /// <param name="index">Zero-based translation index.</param>
        public string this[int index]
        {
            get
            {
                if (++_reads == cancelAfter)
                    cancellation.Cancel();
                return $"Translated {index}";
            }
        }

        /// <summary>
        /// Enumerates text without changing cancellation timing.
        /// </summary>
        /// <returns>Enumerator over valid translations.</returns>
        public IEnumerator<string> GetEnumerator() => Enumerable.Repeat("Translated", Count).GetEnumerator();

        /// <summary>
        /// Enumerates valid translations through non-generic collection contract.
        /// </summary>
        /// <returns>Enumerator over valid translations.</returns>
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
