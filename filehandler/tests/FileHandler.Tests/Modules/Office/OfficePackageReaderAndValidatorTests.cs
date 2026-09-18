using System.IO.Compression;
using System.Text;
using FileHandler.Api.Common;
using FileHandler.Api.Modules.Office;
using Microsoft.Extensions.Options;

namespace FileHandler.Tests.Modules.Office;

/// <summary>
/// Tests covering Office package preflight, reader, inspector, and Open XML validator (PK01-PK12).
/// </summary>
public sealed class OfficePackageReaderAndValidatorTests
{

    /// <summary>
    /// Verifies valid Word document passes preflight and reader (PK01).
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task ReadAsync_ValidWordDocument_SucceedsWithSource()
    {
        var docx = OfficeFixtureFactory.CreateWordDocument("Hello world");
        var reader = new OfficePackageReader(new OfficeProcessingOptions());

        using var stream = new MemoryStream(docx);
        var result = await reader.ReadAsync(stream, OfficeFormat.Word, default);

        Assert.Empty(result.Errors);
        Assert.NotNull(result.Source);
        Assert.Equal(OfficeFormat.Word, result.Source!.Format);
        Assert.False(string.IsNullOrEmpty(result.Source.SourceHash));
    }

    /// <summary>
    /// Verifies valid Excel workbook passes preflight and reader (PK02).
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task ReadAsync_ValidExcelWorkbook_SucceedsWithSource()
    {
        var xlsx = OfficeFixtureFactory.CreateExcelWithInlineStrings(new[] { new[] { "Cell1", "Cell2" } });
        var reader = new OfficePackageReader(new OfficeProcessingOptions());

        using var stream = new MemoryStream(xlsx);
        var result = await reader.ReadAsync(stream, OfficeFormat.Excel, default);

        Assert.Empty(result.Errors);
        Assert.NotNull(result.Source);
        Assert.Equal(OfficeFormat.Excel, result.Source!.Format);
    }

    /// <summary>
    /// Verifies valid PowerPoint presentation passes preflight and reader (PK03).
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task ReadAsync_ValidPowerPointPresentation_SucceedsWithSource()
    {
        var pptx = OfficeFixtureFactory.CreatePowerPointPresentation("Slide 1 title");
        var reader = new OfficePackageReader(new OfficeProcessingOptions());

        using var stream = new MemoryStream(pptx);
        var result = await reader.ReadAsync(stream, OfficeFormat.PowerPoint, default);

        Assert.Empty(result.Errors);
        Assert.NotNull(result.Source);
        Assert.Equal(OfficeFormat.PowerPoint, result.Source!.Format);
    }

    /// <summary>
    /// Verifies non-ZIP stream is rejected with invalid_office_package (PK04).
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task ReadAsync_NonZipBytes_ReturnsOfficePackageCorrupt()
    {
        var randomBytes = Encoding.UTF8.GetBytes("Not a zip file at all.");
        var reader = new OfficePackageReader(new OfficeProcessingOptions());

        using var stream = new MemoryStream(randomBytes);
        var result = await reader.ReadAsync(stream, OfficeFormat.Word, default);

        Assert.Null(result.Source);
        Assert.Contains(result.Errors, e => e.Code == "invalid_office_package");
    }

    /// <summary>
    /// Verifies non-office ZIP without [Content_Types].xml is rejected with invalid_office_package (PK05).
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task ReadAsync_MissingContentTypes_ReturnsOfficePackageCorrupt()
    {
        var zipBytes = OfficeFixtureFactory.CreateNonOfficeZip();
        var reader = new OfficePackageReader(new OfficeProcessingOptions());

        using var stream = new MemoryStream(zipBytes);
        var result = await reader.ReadAsync(stream, OfficeFormat.Word, default);

        Assert.Null(result.Source);
        Assert.Contains(result.Errors, e => e.Code == "invalid_office_package");
    }

    /// <summary>
    /// Verifies format mismatch is detected and rejected with office_format_mismatch (PK06).
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task ReadAsync_FormatMismatch_ReturnsOfficeFormatMismatch()
    {
        var pptx = OfficeFixtureFactory.CreatePowerPointPresentation("Slide 1");
        var reader = new OfficePackageReader(new OfficeProcessingOptions());

        using var stream = new MemoryStream(pptx);
        var result = await reader.ReadAsync(stream, OfficeFormat.Word, default);

        Assert.Null(result.Source);
        Assert.Contains(result.Errors, e => e.Code == "office_format_mismatch");
    }

    /// <summary>
    /// Verifies corrupted ZIP entry payload is rejected with invalid_office_package (PK07).
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task ReadAsync_CorruptedZipPayload_ReturnsOfficePackageCorrupt()
    {
        var docx = OfficeFixtureFactory.CreateWordDocument("Text");
        // Corrupt middle bytes of the zip
        if (docx.Length > 200)
        {
            for (var i = 100; i < 150; i++)
            {
                docx[i] = 0xFF;
            }
        }

        var reader = new OfficePackageReader(new OfficeProcessingOptions());
        using var stream = new MemoryStream(docx);
        var result = await reader.ReadAsync(stream, OfficeFormat.Word, default);

        Assert.Null(result.Source);
        Assert.Contains(result.Errors, e => e.Code == "invalid_office_package");
    }

    /// <summary>
    /// Verifies package exceeding MaxZipEntries is rejected with office_package_limit_exceeded (PK08).
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task ReadAsync_ExceedsMaxZipEntries_ReturnsLimitExceeded()
    {
        var docx = OfficeFixtureFactory.CreateWordDocument("Small");
        var options = new OfficeProcessingOptions { MaxPackageEntries = 2 };
        var reader = new OfficePackageReader(options);

        using var stream = new MemoryStream(docx);
        var result = await reader.ReadAsync(stream, OfficeFormat.Word, default);

        Assert.Null(result.Source);
        Assert.Contains(result.Errors, e => e.Code == "office_package_limit_exceeded");
    }

    /// <summary>
    /// Verifies package exceeding MaxPartBytes is rejected with office_package_limit_exceeded (PK09).
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task ReadAsync_ExceedsMaxEntryBytes_ReturnsLimitExceeded()
    {
        var docx = OfficeFixtureFactory.CreateWordDocument("Small");
        var options = new OfficeProcessingOptions { MaxPartBytes = 50 };
        var reader = new OfficePackageReader(options);

        using var stream = new MemoryStream(docx);
        var result = await reader.ReadAsync(stream, OfficeFormat.Word, default);

        Assert.Null(result.Source);
        Assert.Contains(result.Errors, e => e.Code == "office_package_limit_exceeded");
    }

    /// <summary>
    /// Verifies XML containing DTD is rejected with invalid_office_package (PK10).
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task ReadAsync_PackageWithDtd_ReturnsOfficePackageCorrupt()
    {
        var dtdZip = OfficeFixtureFactory.CreatePackageWithDtd();
        var reader = new OfficePackageReader(new OfficeProcessingOptions());

        using var stream = new MemoryStream(dtdZip);
        var result = await reader.ReadAsync(stream, OfficeFormat.Word, default);

        Assert.Null(result.Source);
        Assert.Contains(result.Errors, e => e.Code == "invalid_office_package");
    }

    /// <summary>
    /// Verifies package inspector correctly indexes parts and relationships (PK11).
    /// </summary>
    /// <returns>No return value.</returns>
    [Fact]
    public void Inspect_ValidWordDocument_CollectsPartsAndRelationships()
    {
        var docx = OfficeFixtureFactory.CreateWordDocument("Hello");
        var options = new OfficeProcessingOptions();
        var source = new OfficeSource(docx, "hash1", OfficeFormat.Word, options);
        var inspector = new OfficePackageInspector(options);

        var inventory = inspector.Inspect(source, default);

        Assert.NotEmpty(inventory.Parts);
        Assert.Contains(inventory.Parts, p => p.PartUri.Equals("/word/document.xml", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(inventory.Parts, p => p.PartUri.Equals("/[Content_Types].xml", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Verifies package validator validates source schema (PK12).
    /// </summary>
    /// <returns>No return value.</returns>
    [Fact]
    public void ValidateSource_ValidWordDocument_PassesValidation()
    {
        var docx = OfficeFixtureFactory.CreateWordDocument("Valid content");
        var options = new OfficeProcessingOptions();
        var source = new OfficeSource(docx, "hash1", OfficeFormat.Word, options);
        var validator = new OfficePackageValidator(options);

        var result = validator.ValidateSource(source, new[] { "/word/document.xml" }, default);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }
}
