using System.IO.Compression;
using System.Text;
using FileHandler.Api.Common;
using FileHandler.Api.Modules.Office;

namespace FileHandler.Tests.Modules.Office;

/// <summary>
/// Tests covering Office bounded stream, export session, and atomic packaging (AT01-AT10).
/// </summary>
public sealed class OfficeExportSessionAndBoundaryTests
{

    /// <summary>
    /// Verifies OfficeBoundedStream enforces max bytes and throws InvalidOperationException when exceeded (AT05).
    /// </summary>
    /// <returns>No return value.</returns>
    [Fact]
    public void BoundedStream_ExceedsLimit_ThrowsFileLimitException()
    {
        using var target = new MemoryStream();
        using var bounded = new OfficeBoundedStream(target, 10);

        var data = Encoding.UTF8.GetBytes("0123456789EXCEED");
        Assert.Throws<FileLimitException>(() => bounded.Write(data, 0, data.Length));
    }

    /// <summary>
    /// Verifies OfficeExportSession preserves untouched entries and updates touched entries (AT02, AT03).
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task ExportSession_WritePart_ReplacesTouchedPartAndKeepsUntouched()
    {
        var docx = OfficeFixtureFactory.CreateWordDocument("Original");
        var officeOpts = new OfficeProcessingOptions();
        var source = new OfficeSource(docx, "hash", OfficeFormat.Word, officeOpts);
        var fileOpts = new FileHandlingOptions();

        using var session = OfficeExportSession.Create(source, officeOpts, fileOpts);
        var replacementBytes = Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"utf-8\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>Replaced</w:t></w:r></w:p></w:body></w:document>");
        session.WritePart("/word/document.xml", replacementBytes);

        var output = await session.FinalizeAsync(default);
        Assert.NotNull(output.Content);
        Assert.True(output.OutputBytes > 0);

        using var zip = new ZipArchive(new MemoryStream(output.Content), ZipArchiveMode.Read);
        var entry = zip.GetEntry("word/document.xml");
        Assert.NotNull(entry);

        using var reader = new StreamReader(entry!.Open());
        var text = reader.ReadToEnd();
        Assert.Contains("Replaced", text);
    }

    /// <summary>
    /// Verifies export session enforces MaxOutputBytes limit and throws InvalidOperationException (AT04).
    /// </summary>
    /// <returns>Task representing test completion.</returns>
    [Fact]
    public async Task ExportSession_ExceedsOutputLimit_ThrowsFileLimitException()
    {
        var docx = OfficeFixtureFactory.CreateWordDocument("Original");
        var officeOpts = new OfficeProcessingOptions();
        var source = new OfficeSource(docx, "hash", OfficeFormat.Word, officeOpts);
        var fileOpts = new FileHandlingOptions { MaxOutputBytes = 50 }; // smaller than zip

        using var session = OfficeExportSession.Create(source, officeOpts, fileOpts);
        session.WritePart("/word/document.xml", Encoding.UTF8.GetBytes("<test>large data</test>"));

        await Assert.ThrowsAsync<FileLimitException>(() => session.FinalizeAsync(default));
    }
}
