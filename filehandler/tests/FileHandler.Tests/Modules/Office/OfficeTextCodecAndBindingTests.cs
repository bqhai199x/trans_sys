using FileHandler.Api.Common;
using FileHandler.Api.Modules.Office;

namespace FileHandler.Tests.Modules.Office;

/// <summary>
/// Tests covering Office text codec canonical tokens, escaping, and bindings (CD01-CD10, B01-B06).
/// </summary>
public sealed class OfficeTextCodecAndBindingTests
{

    /// <summary>
    /// Default file handling options for codec tests.
    /// </summary>
    private static readonly FileHandlingOptions DefaultFileOptions = new();

    /// <summary>
    /// Default office processing options for codec tests.
    /// </summary>
    private static readonly OfficeProcessingOptions DefaultOfficeOptions = new();

    /// <summary>
    /// Helper to create OfficeTranslationUnit for test scenarios.
    /// </summary>
    /// <param name="index">Zero-based unit index.</param>
    /// <param name="mode">Unit mode.</param>
    /// <param name="encodedSource">Canonical encoded source text.</param>
    /// <param name="slots">Text slots.</param>
    /// <returns>Populated translation unit.</returns>
    private static OfficeTranslationUnit MakeUnit(
        int index,
        UnitMode mode,
        string encodedSource,
        params OfficeTextSlot[] slots)
    {
        var loc = new OfficeLocation("/word/document.xml", Array.Empty<OfficeElementPathSegment>());
        return new OfficeTranslationUnit(
            index,
            $"unit{index}",
            $"obj{index}",
            loc,
            mode,
            encodedSource,
            slots,
            Array.Empty<OfficeProtectedAnchor>(),
            Array.Empty<OfficeTextBinding>(),
            "hash");
    }

    /// <summary>
    /// Verifies plain text template encoding produces literal text without tokens (CD01).
    /// </summary>
    /// <returns>No return value.</returns>
    [Fact]
    public void Encode_PlainUnit_ReturnsLiteralText()
    {
        var codec = new OfficeTextCodec(DefaultOfficeOptions, DefaultFileOptions);
        var template = new OfficeTextTemplate(
            UnitMode.Plain,
            new[] { new OfficeTextSlot("r0", "Hello world", "plain") },
            Array.Empty<OfficeProtectedAnchor>(),
            Array.Empty<OfficeTextBinding>());

        var encoded = codec.Encode(template);

        Assert.Equal("Hello world", encoded);
    }

    /// <summary>
    /// Verifies multi-slot template produces canonical tokens (CD02, CD03).
    /// </summary>
    /// <returns>No return value.</returns>
    [Fact]
    public void Encode_RichMultiSlot_ProducesCanonicalTokens()
    {
        var codec = new OfficeTextCodec(DefaultOfficeOptions, DefaultFileOptions);
        var template = new OfficeTextTemplate(
            UnitMode.Structured,
            new[]
            {
                new OfficeTextSlot("r0", "Bold", "bold"),
                new OfficeTextSlot("r1", " plain", "plain")
            },
            Array.Empty<OfficeProtectedAnchor>(),
            Array.Empty<OfficeTextBinding>());

        var encoded = codec.Encode(template);

        Assert.Equal("<ox:r0>Bold</ox:r0><ox:r1> plain</ox:r1>", encoded);
    }

    /// <summary>
    /// Verifies break elements are encoded as self-closing tokens (CD04).
    /// </summary>
    /// <returns>No return value.</returns>
    [Fact]
    public void Encode_WithBreak_ProducesSelfClosingBreakToken()
    {
        var codec = new OfficeTextCodec(DefaultOfficeOptions, DefaultFileOptions);
        var template = new OfficeTextTemplate(
            UnitMode.Structured,
            new[]
            {
                new OfficeTextSlot("r0", "Line1", "run1"),
                new OfficeTextSlot("r1", "Line2", "run2")
            },
            new[]
            {
                new OfficeProtectedAnchor("k0", AnchorKind.Break, "fp_break")
            },
            Array.Empty<OfficeTextBinding>());

        var encoded = codec.Encode(template);

        Assert.Contains("<ox:k0/>", encoded);
    }

    /// <summary>
    /// Verifies escaping of backslash and opening angle bracket (CD05).
    /// </summary>
    /// <returns>No return value.</returns>
    [Fact]
    public void Encode_SpecialCharacters_EscapedProperly()
    {
        var codec = new OfficeTextCodec(DefaultOfficeOptions, DefaultFileOptions);
        var template = new OfficeTextTemplate(
            UnitMode.Structured,
            new[] { new OfficeTextSlot("r0", @"C:\path\<tag>", "plain") },
            Array.Empty<OfficeProtectedAnchor>(),
            Array.Empty<OfficeTextBinding>());

        var encoded = codec.Encode(template);

        Assert.Equal(@"<ox:r0>C:\\path\\\<tag></ox:r0>", encoded);
    }

    /// <summary>
    /// Verifies decoding valid translated tokens extracts translated slots in order (CD07).
    /// </summary>
    /// <returns>No return value.</returns>
    [Fact]
    public void Decode_ValidTokens_ExtractsSlotsInOrder()
    {
        var codec = new OfficeTextCodec(DefaultOfficeOptions, DefaultFileOptions);
        var unit = MakeUnit(
            0,
            UnitMode.Structured,
            "<ox:r0>Hello</ox:r0><ox:r1> World</ox:r1>",
            new OfficeTextSlot("r0", "Hello", "fp1"),
            new OfficeTextSlot("r1", " World", "fp2"));

        var result = codec.ValidateAndDecode(
            new[] { unit },
            new[] { "<ox:r0>Xin chao</ox:r0><ox:r1> The gioi</ox:r1>" },
            OfficeFormat.Word,
            default);

        Assert.Empty(result.Errors);
        Assert.NotNull(result.DecodedUnits);
        Assert.Single(result.DecodedUnits);
        Assert.Equal(2, result.DecodedUnits![0].DecodedSlots.Count);
        Assert.Equal("Xin chao", result.DecodedUnits[0].DecodedSlots[0]);
        Assert.Equal(" The gioi", result.DecodedUnits[0].DecodedSlots[1]);
    }

    /// <summary>
    /// Verifies missing token is rejected with office_token_mismatch (CD08).
    /// </summary>
    /// <returns>No return value.</returns>
    [Fact]
    public void Decode_MissingToken_ReturnsInvalidTranslation()
    {
        var codec = new OfficeTextCodec(DefaultOfficeOptions, DefaultFileOptions);
        var unit = MakeUnit(
            0,
            UnitMode.Structured,
            "<ox:r0>Hello</ox:r0><ox:r1> World</ox:r1>",
            new OfficeTextSlot("r0", "Hello", "fp1"),
            new OfficeTextSlot("r1", " World", "fp2"));

        var result = codec.ValidateAndDecode(
            new[] { unit },
            new[] { "<ox:r0>Xin chao</ox:r0>" },
            OfficeFormat.Word,
            default);

        Assert.Contains(result.Errors, e => e.Code == "office_token_mismatch");
    }

    /// <summary>
    /// Verifies reordered tokens are rejected with office_token_mismatch (CD09).
    /// </summary>
    /// <returns>No return value.</returns>
    [Fact]
    public void Decode_ReorderedTokens_ReturnsInvalidTranslation()
    {
        var codec = new OfficeTextCodec(DefaultOfficeOptions, DefaultFileOptions);
        var unit = MakeUnit(
            0,
            UnitMode.Structured,
            "<ox:r0>Hello</ox:r0><ox:r1> World</ox:r1>",
            new OfficeTextSlot("r0", "Hello", "fp1"),
            new OfficeTextSlot("r1", " World", "fp2"));

        var result = codec.ValidateAndDecode(
            new[] { unit },
            new[] { "<ox:r1>The gioi</ox:r1><ox:r0>Xin chao</ox:r0>" },
            OfficeFormat.Word,
            default);

        Assert.Contains(result.Errors, e => e.Code == "office_token_mismatch");
    }

    /// <summary>
    /// Verifies unclosed or malformed token syntax is rejected with office_token_mismatch (CD10).
    /// </summary>
    /// <returns>No return value.</returns>
    [Fact]
    public void Decode_MalformedTokenSyntax_ReturnsInvalidTranslation()
    {
        var codec = new OfficeTextCodec(DefaultOfficeOptions, DefaultFileOptions);
        var unit = MakeUnit(
            0,
            UnitMode.Structured,
            "<ox:r0>Hello</ox:r0>",
            new OfficeTextSlot("r0", "Hello", "fp1"));

        var result = codec.ValidateAndDecode(
            new[] { unit },
            new[] { "<ox:r0>Xin chao" }, // unclosed
            OfficeFormat.Word,
            default);

        Assert.Contains(result.Errors, e => e.Code == "office_token_mismatch");
    }

    /// <summary>
    /// Verifies empty translation string is rejected with empty_translation (B01).
    /// </summary>
    /// <returns>No return value.</returns>
    [Fact]
    public void Decode_EmptyTranslation_ReturnsInvalidTranslation()
    {
        var codec = new OfficeTextCodec(DefaultOfficeOptions, DefaultFileOptions);
        var unit = MakeUnit(
            0,
            UnitMode.Plain,
            "Hello",
            new OfficeTextSlot("r0", "Hello", "fp1"));

        var result = codec.ValidateAndDecode(
            new[] { unit },
            new[] { "" },
            OfficeFormat.Word,
            default);

        Assert.Contains(result.Errors, e => e.Code == "empty_translation");
    }

    /// <summary>
    /// Verifies translation count mismatch is rejected with translation_count_mismatch (B03).
    /// </summary>
    /// <returns>No return value.</returns>
    [Fact]
    public void Decode_CountMismatch_ReturnsTranslationCountMismatch()
    {
        var codec = new OfficeTextCodec(DefaultOfficeOptions, DefaultFileOptions);
        var unit = MakeUnit(
            0,
            UnitMode.Plain,
            "Hello",
            new OfficeTextSlot("r0", "Hello", "fp1"));

        var result = codec.ValidateAndDecode(
            new[] { unit },
            new[] { "Hello", "Extra" },
            OfficeFormat.Word,
            default);

        Assert.Contains(result.Errors, e => e.Code == "translation_count_mismatch");
    }
}
