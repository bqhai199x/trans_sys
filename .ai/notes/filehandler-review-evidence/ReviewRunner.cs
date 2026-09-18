using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using FileHandler.Api.Common;
using FileHandler.Api.Diagnostics;
using FileHandler.Api.Modules.Excel;
using FileHandler.Api.Modules.Office;
using FileHandler.Api.Modules.Markdown;
using FileHandler.Api.Modules.PowerPoint;
using FileHandler.Api.Modules.Word;
using FileHandler.Tests.Modules.Office;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;
using S = DocumentFormat.OpenXml.Spreadsheet;
using W = DocumentFormat.OpenXml.Wordprocessing;

/// <summary>
/// Runs bounded review probes against current production implementation.
/// </summary>
internal static class ReviewRunner
{

    /// <summary>
    /// Collected observations, including source and output schema errors.
    /// </summary>
    private static readonly List<object> Results = [];

    /// <summary>
    /// Executes isolated review probes and saves observed results.
    /// </summary>
    /// <param name="args">Optional documentation audit command.</param>
    /// <returns>Task completing after observations are persisted.</returns>
    private static async Task Main(string[] args)
    {
        if (args.Contains("audit"))
        {
            DocumentationAudit.Run(Path.GetFullPath("../../../filehandler"));
            return;
        }
        if (args.Contains("extra"))
        {
            await ExtraBoundaryProbes();
            File.WriteAllText("extra-observations.json", JsonSerializer.Serialize(Results, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine(JsonSerializer.Serialize(Results, new JsonSerializerOptions { WriteIndented = true }));
            return;
        }

        var word = WordService.Create();
        var excel = ExcelService.Create();
        var ppt = PowerPointService.Create();

        await RoundTrip("word-grouped-runs", word, OfficeFormat.Word,
            OfficeFixtureFactory.CreateWordDocumentWithElements(new W.Paragraph(
                new W.Run(new W.Text("A")), new W.Run(new W.Text("B")),
                new W.Run(new W.RunProperties(new W.Bold()), new W.Text("C")))),
            _ => ["<ox:r0>X</ox:r0><ox:r1>Y</ox:r1>"], "XY");

        await RoundTrip("word-protected-simple-field", word, OfficeFormat.Word,
            OfficeFixtureFactory.CreateWordDocumentWithElements(new W.Paragraph(
                new W.SimpleField(new W.Run(new W.Text("FIELD"))) { Instruction = "DATE" },
                new W.Run(new W.Text("Body")))),
            texts => texts.Select(t => t.Replace("Body", "Translated")).ToArray(), "FIELDTranslated");

        await RoundTrip("word-partial-story-change", word, OfficeFormat.Word,
            OfficeFixtureFactory.CreateWordWithStories("Body", "Header", "Footer"),
            _ => ["Changed", "Header", "Footer"], "success; header/footer unchanged");

        var headerTable = MutateWord(OfficeFixtureFactory.CreateWordWithStories("Body", "Header", "Footer"), doc =>
        {
            doc.MainDocumentPart!.HeaderParts.First().Header!.AppendChild(
                OfficeFixtureFactory.MakeWordTable(new W.TableCell(new W.Paragraph(new W.Run(new W.Text("Cell"))))));
        });
        await RoundTrip("word-header-table", word, OfficeFormat.Word, headerTable,
            texts => texts.Select(t => t + "X").ToArray(), "success; header table preserved");

        var whiteSlot = OfficeFixtureFactory.CreateWordDocumentWithElements(new W.Paragraph(
            new W.Run(new W.Text("A")),
            new W.Run(new W.RunProperties(new W.Bold()), new W.Text(" ") { Space = SpaceProcessingModeValues.Preserve }),
            new W.Run(new W.Text("B"))));
        await RoundTrip("word-whitespace-slot-identity", word, OfficeFormat.Word, whiteSlot, t => t, "byte-identical success");

        var multiParagraph = MutatePpt(OfficeFixtureFactory.CreatePowerPointPresentation("First"), doc =>
            doc.PresentationPart!.SlideParts.First().Slide!.Descendants<P.Shape>().First().TextBody!
                .AppendChild(new A.Paragraph(new A.Run(new A.Text("Second")))));
        await RoundTrip("ppt-two-paragraphs", ppt, OfficeFormat.PowerPoint, multiParagraph,
            _ => ["One", "Two"], "paragraphs [One, Two]");

        var twoTables = MutatePpt(OfficeFixtureFactory.CreatePowerPointWithTable(new A.TableRow(
            OfficeFixtureFactory.DrawingCell(new A.Paragraph(new A.Run(new A.Text("A")))),
            OfficeFixtureFactory.DrawingCell(new A.Paragraph(new A.Run(new A.Text("B")))))), doc =>
        {
            var tree = doc.PresentationPart!.SlideParts.First().Slide!.CommonSlideData!.ShapeTree!;
            var clone = (P.GraphicFrame)tree.Elements<P.GraphicFrame>().First().CloneNode(true);
            clone.NonVisualGraphicFrameProperties!.NonVisualDrawingProperties!.Id = 3U;
            clone.NonVisualGraphicFrameProperties.NonVisualDrawingProperties.Name = "Second table";
            var nodes = clone.Descendants<A.Text>().ToArray();
            nodes[0].Text = "C";
            nodes[1].Text = "D";
            tree.AppendChild(clone);
        });
        await RoundTrip("ppt-two-tables", ppt, OfficeFormat.PowerPoint, twoTables,
            _ => ["AA", "BB", "CC", "DD"], "tables [AA,BB], [CC,DD]");

        var richExcel = MutateExcel(OfficeFixtureFactory.CreateExcelWithSharedStrings(["AB"], [[0]]), doc =>
        {
            var item = doc.WorkbookPart!.SharedStringTablePart!.SharedStringTable!.Elements<S.SharedStringItem>().Single();
            item.RemoveAllChildren();
            item.Append(new S.Run(new S.RunProperties(new S.Bold()), new S.Text("A")),
                new S.Run(new S.RunProperties(new S.Italic()), new S.Text("B")));
        });
        await RoundTrip("excel-rich-sst", excel, OfficeFormat.Excel, richExcel,
            _ => ["<ox:r0>X</ox:r0><ox:r1>Y</ox:r1>"], "rich runs X (bold), Y (italic)");
        await RoundTrip("excel-only-second-slot", excel, OfficeFormat.Excel, richExcel,
            _ => ["<ox:r0>A</ox:r0><ox:r1>Y</ox:r1>"], "rich runs A (bold), Y (italic)");

        var small = OfficeFixtureFactory.CreateWordDocument("One", "Two");
        File.WriteAllBytes("artifacts/http-source.docx", small);
        var unitLimited = WordService.Create(Options.Create(new FileHandlingOptions { MaxUnits = 1 }));
        await RoundTrip("office-export-unit-limit", unitLimited, OfficeFormat.Word, small,
            _ => ["One", "Two"], "too_many_units on import and export", allowImportErrors: true);
        var outputLimited = WordService.Create(Options.Create(new FileHandlingOptions { MaxOutputBytes = 100 }));
        await RoundTrip("office-output-quota-exception", outputLimited, OfficeFormat.Word, small,
            _ => ["Changed", "Two"], "output_too_large result");

        await ZipLengthProbe();
        await TokenOrderProbe();
        await EditMaskProbe();
        await AdditionalProbes();
        await TraceBenchmarks();
        await ScalingBenchmarks();
        File.WriteAllText("observations.json", JsonSerializer.Serialize(Results, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(Results, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>
    /// Records import/export results and independently inspects successful output.
    /// </summary>
    /// <param name="id">Probe name.</param>
    /// <param name="handler">Format service under review.</param>
    /// <param name="format">Package format.</param>
    /// <param name="source">Fresh synthetic source bytes.</param>
    /// <param name="translate">Translation generator.</param>
    /// <param name="expected">Expected observable behavior.</param>
    /// <param name="allowImportErrors">Whether to continue export when import rejects source.</param>
    /// <returns>Task completing after observation is recorded.</returns>
    private static async Task RoundTrip(string id, IFileHandler handler, OfficeFormat format, byte[] source,
        Func<string[], string[]> translate, string expected, bool allowImportErrors = false)
    {
        var before = source.ToArray();
        var imported = await handler.ImportAsync(new MemoryStream(source));
        var texts = imported.Texts.ToArray();
        if (imported.Errors.Count > 0 && !allowImportErrors)
        {
            Results.Add(new { id, expected, sourceSchema = Schema(source, format), importErrors = imported.Errors });
            return;
        }
        try
        {
            var exported = await handler.ExportAsync(new MemoryStream(source), translate(texts));
            Results.Add(new
            {
                id, expected, imported = texts, importErrors = imported.Errors, exportErrors = exported.Errors,
                sourceSchema = Schema(source, format), outputSchema = exported.Content is null ? null : Schema(exported.Content, format),
                sourceUnchanged = before.SequenceEqual(source), identity = exported.Content?.SequenceEqual(source),
                output = exported.Content is null ? null : ReadContent(exported.Content, format)
            });
        }
        catch (Exception exception)
        {
            Results.Add(new { id, expected, imported = texts, exception = exception.GetType().Name, exception.Message, sourceUnchanged = before.SequenceEqual(source) });
        }
    }

    /// <summary>
    /// Opens package for independent schema and content checks.
    /// </summary>
    /// <param name="bytes">Package bytes.</param>
    /// <param name="format">Package format.</param>
    /// <returns>Caller-owned read-only package.</returns>
    private static OpenXmlPackage Open(byte[] bytes, OfficeFormat format)
    {
        var stream = new MemoryStream(bytes, false);
        var settings = new OpenSettings { AutoSave = false };
        return format switch
        {
            OfficeFormat.Word => WordprocessingDocument.Open(stream, false, settings),
            OfficeFormat.Excel => SpreadsheetDocument.Open(stream, false, settings),
            _ => PresentationDocument.Open(stream, false, settings)
        };
    }

    /// <summary>
    /// Checks complete synthetic package schema without selected-part filtering.
    /// </summary>
    /// <param name="bytes">Package bytes.</param>
    /// <param name="format">Package format.</param>
    /// <returns>First five schema errors, empty when valid.</returns>
    private static string[] Schema(byte[] bytes, OfficeFormat format)
    {
        using var doc = Open(bytes, format);
        return new OpenXmlValidator(FileFormatVersions.Office2019).Validate(doc).Take(5).Select(e => e.Description).ToArray();
    }

    /// <summary>
    /// Reads actual serialized text and rich-run markup.
    /// </summary>
    /// <param name="bytes">Exported bytes.</param>
    /// <param name="format">Package format.</param>
    /// <returns>Text-node payload and format-specific XML.</returns>
    private static object ReadContent(byte[] bytes, OfficeFormat format)
    {
        using var doc = Open(bytes, format);
        return doc switch
        {
            WordprocessingDocument word => new { texts = word.MainDocumentPart!.Document!.Descendants<W.Text>().Select(t => t.Text).ToArray() },
            PresentationDocument ppt => new { texts = ppt.PresentationPart!.SlideParts.SelectMany(p => p.Slide!.Descendants<A.Text>()).Select(t => t.Text).ToArray() },
            SpreadsheetDocument excel => new { sheet = excel.WorkbookPart!.WorksheetParts.First().Worksheet!.OuterXml, sst = excel.WorkbookPart.SharedStringTablePart?.SharedStringTable?.OuterXml },
            _ => throw new InvalidOperationException()
        };
    }

    /// <summary>
    /// Mutates a synthetic Word fixture independently from production code.
    /// </summary>
    /// <param name="source">Synthetic source.</param>
    /// <param name="change">Fixture mutation.</param>
    /// <returns>New fixture bytes.</returns>
    private static byte[] MutateWord(byte[] source, Action<WordprocessingDocument> change)
    {
        using var ms = new MemoryStream();
        ms.Write(source);
        using (var doc = WordprocessingDocument.Open(ms, true)) change(doc);
        return ms.ToArray();
    }

    /// <summary>
    /// Mutates a synthetic workbook independently from production code.
    /// </summary>
    /// <param name="source">Synthetic source.</param>
    /// <param name="change">Fixture mutation.</param>
    /// <returns>New fixture bytes.</returns>
    private static byte[] MutateExcel(byte[] source, Action<SpreadsheetDocument> change)
    {
        using var ms = new MemoryStream();
        ms.Write(source);
        using (var doc = SpreadsheetDocument.Open(ms, true)) change(doc);
        return ms.ToArray();
    }

    /// <summary>
    /// Mutates a synthetic presentation independently from production code.
    /// </summary>
    /// <param name="source">Synthetic source.</param>
    /// <param name="change">Fixture mutation.</param>
    /// <returns>New fixture bytes.</returns>
    private static byte[] MutatePpt(byte[] source, Action<PresentationDocument> change)
    {
        using var ms = new MemoryStream();
        ms.Write(source);
        using (var doc = PresentationDocument.Open(ms, true))
        {
            var main = doc.PresentationPart!;
            if (!main.SlideMasterParts.Any())
            {
                var master = main.AddNewPart<SlideMasterPart>();
                master.SlideMaster = new P.SlideMaster(new P.CommonSlideData(new P.ShapeTree(
                    new P.NonVisualGroupShapeProperties(new P.NonVisualDrawingProperties { Id = 1U, Name = "" }, new P.NonVisualGroupShapeDrawingProperties(), new P.ApplicationNonVisualDrawingProperties()), new P.GroupShapeProperties())),
                    new P.ColorMap { Background1 = A.ColorSchemeIndexValues.Light1, Text1 = A.ColorSchemeIndexValues.Dark1, Background2 = A.ColorSchemeIndexValues.Light2, Text2 = A.ColorSchemeIndexValues.Dark2, Accent1 = A.ColorSchemeIndexValues.Accent1, Accent2 = A.ColorSchemeIndexValues.Accent2, Accent3 = A.ColorSchemeIndexValues.Accent3, Accent4 = A.ColorSchemeIndexValues.Accent4, Accent5 = A.ColorSchemeIndexValues.Accent5, Accent6 = A.ColorSchemeIndexValues.Accent6, Hyperlink = A.ColorSchemeIndexValues.Hyperlink, FollowedHyperlink = A.ColorSchemeIndexValues.FollowedHyperlink });
                main.Presentation!.PrependChild(new P.SlideMasterIdList(new P.SlideMasterId { Id = 2147483648U, RelationshipId = main.GetIdOfPart(master) }));
                main.Presentation.AppendChild(new P.NotesSize { Cx = 6858000L, Cy = 9144000L });
            }
            change(doc);
        }
        return ms.ToArray();
    }

    /// <summary>
    /// Tests declared ZIP sizes against a bounded 256 KiB actual payload.
    /// </summary>
    /// <returns>Task completing after ZIP observation.</returns>
    private static async Task ZipLengthProbe()
    {
        using var ms = new MemoryStream();
        ms.Write(OfficeFixtureFactory.CreateWordDocument("Hello"));
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Update, true))
        {
            using var stream = zip.CreateEntry("payload.bin").Open();
            stream.Write(new byte[262144]);
        }
        var bytes = ms.ToArray();
        for (var offset = 0; offset + 46 < bytes.Length; offset++)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset)) != 0x02014b50) continue;
            var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 28));
            var name = Encoding.UTF8.GetString(bytes, offset + 46, nameLength);
            if (name != "payload.bin") continue;
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 24), 1);
            var local = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 42));
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(local + 22), 1);
            break;
        }
        using var actualZip = new ZipArchive(new MemoryStream(bytes));
        using var payload = actualZip.GetEntry("payload.bin")!.Open();
        using var sink = new MemoryStream();
        await payload.CopyToAsync(sink);
        var options = new OfficeProcessingOptions { MaxPartBytes = 4096, MaxExpandedBytes = 8192 };
        var read = await new OfficePackageReader(options).ReadAsync(new MemoryStream(bytes), OfficeFormat.Word, default);
        Results.Add(new { id = "zip-forged-expanded-size", compressedPackageBytes = bytes.Length, actualPayload = sink.Length, declaredPayload = 1, options.MaxPartBytes, options.MaxExpandedBytes, accepted = read.Source is not null, errors = read.Errors });
        read.Source?.Dispose();
    }

    /// <summary>
    /// Tests exact anchor/run ordering using source with leading break.
    /// </summary>
    /// <returns>Task completing after token observation.</returns>
    private static async Task TokenOrderProbe()
    {
        var bytes = OfficeFixtureFactory.CreateWordDocumentWithElements(new W.Paragraph(new W.Run(new W.Break(), new W.Text("A"))));
        var options = new OfficeProcessingOptions();
        var codec = new OfficeTextCodec(options, new FileHandlingOptions());
        var source = (await new OfficePackageReader(options).ReadAsync(new MemoryStream(bytes), OfficeFormat.Word, default)).Source!;
        using (source)
        {
            var plan = new WordExtractor(codec, new WordTableReader(), options).Analyze(source, new OfficePackageInspector(options).Inspect(source, default), default);
            var alternateOrder = "<ox:k0/><ox:r0>X</ox:r0>";
            var decoded = codec.ValidateAndDecode(plan.Units, [alternateOrder], OfficeFormat.Word, default);
            Results.Add(new { id = "office-anchor-order", sourceOrder = "break then text", imported = plan.Units[0].EncodedSource, alternateOrder, accepted = decoded.Errors.Count == 0 });
        }
    }

    /// <summary>
    /// Checks whether text-only mask rejects unrelated formatting mutations.
    /// </summary>
    /// <returns>Task completing after validator observation.</returns>
    private static async Task EditMaskProbe()
    {
        var bytes = OfficeFixtureFactory.CreateWordDocument("Hello");
        var options = new OfficeProcessingOptions();
        using var source = (await new OfficePackageReader(options).ReadAsync(new MemoryStream(bytes), OfficeFormat.Word, default)).Source!;
        var inventory = new OfficePackageInspector(options).Inspect(source, default);
        var plan = new WordExtractor(new OfficeTextCodec(options, new FileHandlingOptions()), new WordTableReader(), options).Analyze(source, inventory, default);
        var changed = MutateWord(bytes, doc => doc.MainDocumentPart!.Document!.Descendants<W.Run>().First().RunProperties = new W.RunProperties(new W.Bold()));
        var masks = new Dictionary<string, OfficeEditMask> { [plan.Stories[0].PartUri] = new(plan.Stories[0].PartUri, ["//w:t"]) };
        var packageCheck = new OfficePackageValidator(options).ValidateOutput(source, new OfficeOutput(changed, "probe", changed.Length, "probe"), masks, default);
        var structureCheck = new WordStructureValidator().Validate(changed, plan, default);
        Results.Add(new { id = "edit-mask-ignored", unauthorizedMutation = "added bold run properties under text-only mask", packageAccepted = packageCheck.IsValid, structureAccepted = structureCheck.IsValid });
    }

    /// <summary>
    /// Measures tracing cost and redaction with warmed synthetic Word import.
    /// </summary>
    /// <returns>Task completing after three trace-mode measurements.</returns>
    private static async Task TraceBenchmarks()
    {
        var bytes = OfficeFixtureFactory.CreateWordDocument(Enumerable.Repeat("SENSITIVE_SENTINEL", 300).ToArray());
        var handler = WordService.Create();
        await handler.ImportAsync(new MemoryStream(bytes));
        foreach (var mode in new[] { "off", "redacted", "capture" })
        {
            var times = new List<double>();
            var allocations = new List<long>();
            long logBytes = 0;
            var leaked = false;
            for (var i = 0; i < 6; i++)
            {
                GC.Collect();
                var allocatedBefore = GC.GetTotalAllocatedBytes(true);
                var timer = Stopwatch.StartNew();
                TraceSession? session = null;
                TraceCall? request = null;
                var path = $"trace-{mode}.json";
                if (mode != "off")
                {
                    session = new TraceSession(path, new DebugTraceOptions { CaptureContent = mode == "capture", MaxEvents = 10000 }, NullLogger.Instance);
                    request = new TraceCall(session, null, "Probe", "Request");
                    DebugTrace.Current = request;
                }
                var imported = await handler.ImportAsync(new MemoryStream(bytes));
                request?.Return(imported);
                request?.Dispose();
                session?.WriteResult(200, timer.Elapsed.TotalMilliseconds);
                session?.Dispose();
                DebugTrace.Current = null;
                timer.Stop();
                var allocated = GC.GetTotalAllocatedBytes(true) - allocatedBefore;
                if (i > 0) { times.Add(timer.Elapsed.TotalMilliseconds); allocations.Add(allocated); }
                if (mode != "off")
                {
                    logBytes = new FileInfo(path).Length;
                    leaked = File.ReadAllText(path).Contains("SENSITIVE_SENTINEL", StringComparison.Ordinal);
                }
            }
            Results.Add(new { id = "trace-benchmark", mode, paragraphs = 300, medianMs = times.Order().ElementAt(2), averageAllocatedBytes = allocations.Average(), logBytes, sentinelPresent = leaked });
        }
    }

    /// <summary>
    /// Measures repeated sibling scans in Word extraction.
    /// </summary>
    /// <returns>Task completing after scaling observations.</returns>
    private static async Task ScalingBenchmarks()
    {
        foreach (var count in new[] { 2000, 4000, 8000 })
        {
            var options = new OfficeProcessingOptions();
            var bytes = OfficeFixtureFactory.CreateWordDocument(Enumerable.Repeat("Text", count).ToArray());
            using var source = (await new OfficePackageReader(options).ReadAsync(new MemoryStream(bytes), OfficeFormat.Word, default)).Source!;
            var inventory = new OfficePackageInspector(options).Inspect(source, default);
            var extractor = new WordExtractor(new OfficeTextCodec(options, new FileHandlingOptions()), new WordTableReader(), options);
            extractor.Analyze(source, inventory, default);
            var times = new List<double>();
            for (var i = 0; i < 3; i++)
            {
                var timer = Stopwatch.StartNew();
                extractor.Analyze(source, inventory, default);
                times.Add(timer.Elapsed.TotalMilliseconds);
            }
            Results.Add(new { id = "word-analysis-scaling", paragraphs = count, medianMs = times.Order().ElementAt(1) });
        }
    }

    /// <summary>
    /// Probes scope gaps and soft-break semantics without altering production code.
    /// </summary>
    /// <returns>Task completing after additional observations.</returns>
    private static async Task AdditionalProbes()
    {
        var markdown = MarkdownService.Create(Options.Create(new FileHandlingOptions()));
        var mdBytes = Encoding.UTF8.GetBytes("Alpha\r\nBeta\r\n");
        var imported = await markdown.ImportAsync(new MemoryStream(mdBytes));
        var exported = await markdown.ExportAsync(new MemoryStream(mdBytes), ["Gamma Delta"]);
        Results.Add(new { id = "markdown-soft-break", imported = imported.Texts, errors = exported.Errors, output = exported.Content is null ? null : Encoding.UTF8.GetString(exported.Content) });

        var hiddenColumn = MutateExcel(OfficeFixtureFactory.CreateExcelWithSharedStrings(["Hidden", "Visible"], [[0, 1]]), doc =>
            doc.WorkbookPart!.WorksheetParts.First().Worksheet!.PrependChild(new S.Columns(new S.Column { Min = 1U, Max = 1U, Hidden = true })));
        await RoundTrip("excel-hidden-column", ExcelService.Create(), OfficeFormat.Excel, hiddenColumn, texts => texts.Select(t => t + "X").ToArray(), "only visible column B translated");

        var drawing = MutateExcel(OfficeFixtureFactory.CreateExcelWithSharedStrings(["Cell"], [[0]]), doc =>
        {
            var sheet = doc.WorkbookPart!.WorksheetParts.First();
            var part = sheet.AddNewPart<DrawingsPart>();
            part.WorksheetDrawing = new DocumentFormat.OpenXml.Drawing.Spreadsheet.WorksheetDrawing("""
                <xdr:wsDr xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
                  <xdr:absoluteAnchor><xdr:pos x="0" y="0"/><xdr:ext cx="1000000" cy="1000000"/>
                    <xdr:sp><xdr:nvSpPr><xdr:cNvPr id="1" name="Shape"/><xdr:cNvSpPr/></xdr:nvSpPr><xdr:spPr/>
                      <xdr:txBody><a:bodyPr/><a:lstStyle/><a:p><a:r><a:t>Drawing</a:t></a:r></a:p></xdr:txBody>
                    </xdr:sp><xdr:clientData/>
                  </xdr:absoluteAnchor>
                </xdr:wsDr>
                """);
            sheet.Worksheet!.AppendChild(new S.Drawing { Id = sheet.GetIdOfPart(part) });
        });
        var service = ExcelService.Create();
        var drawingImport = await service.ImportAsync(new MemoryStream(drawing));
        var drawingExport = await service.ExportAsync(new MemoryStream(drawing), ["ChangedCell", "ChangedDrawing"]);
        using (var doc = drawingExport.Content is null ? null : SpreadsheetDocument.Open(new MemoryStream(drawingExport.Content), false))
            Results.Add(new { id = "excel-drawing-not-applied", sourceSchema = Schema(drawing, OfficeFormat.Excel), imported = drawingImport.Texts, errors = drawingExport.Errors, drawingText = doc?.WorkbookPart?.WorksheetParts.First().DrawingsPart?.WorksheetDrawing?.InnerText });

        var field = OfficeFixtureFactory.CreateWordDocumentWithElements(new W.Paragraph(
            new W.Run(new W.FieldChar { FieldCharType = W.FieldCharValues.Begin }),
            new W.Run(new W.FieldCode("DATE")),
            new W.Run(new W.FieldChar { FieldCharType = W.FieldCharValues.Separate }),
            new W.Run(new W.Text("CACHED")),
            new W.Run(new W.FieldChar { FieldCharType = W.FieldCharValues.End })));
        var fieldImport = await WordService.Create().ImportAsync(new MemoryStream(field));
        Results.Add(new { id = "word-complex-field-cache", sourceSchema = Schema(field, OfficeFormat.Word), imported = fieldImport.Texts, errors = fieldImport.Errors });
    }

    /// <summary>
    /// Checks MIME-based XML quotas and unsupported bound content controls.
    /// </summary>
    /// <returns>Task completing after bounded supplemental probes.</returns>
    private static async Task ExtraBoundaryProbes()
    {
        var bytes = OfficeFixtureFactory.CreateWordDocument("Hello");
        using var memory = new MemoryStream();
        using (var input = new ZipArchive(new MemoryStream(bytes)))
        using (var output = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            foreach (var entry in input.Entries)
            {
                using var reader = new StreamReader(entry.Open());
                var text = reader.ReadToEnd().Replace("word/document.xml", "word/document.dat").Replace("/document.xml", "/document.dat");
                if (entry.FullName == "[Content_Types].xml")
                    text = text.Replace("</Types>", "<Override PartName=\"/word/document.dat\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>");
                var name = entry.FullName.Replace("word/document.xml", "word/document.dat");
                using var writer = new StreamWriter(output.CreateEntry(name).Open(), new UTF8Encoding(false));
                writer.Write(text);
            }
        }
        var changed = memory.ToArray();
        var readerOptions = new OfficeProcessingOptions { MaxXmlDepth = 2 };
        var original = await new OfficePackageReader(readerOptions).ReadAsync(new MemoryStream(bytes), OfficeFormat.Word, default);
        var renamed = await new OfficePackageReader(readerOptions).ReadAsync(new MemoryStream(changed), OfficeFormat.Word, default);
        Results.Add(new { id = "xml-part-extension-bypass", originalErrors = original.Errors, renamedErrors = renamed.Errors, accepted = renamed.Source is not null, sourceSchema = Schema(changed, OfficeFormat.Word) });
        original.Source?.Dispose();
        renamed.Source?.Dispose();

        var bound = OfficeFixtureFactory.CreateWordDocumentWithElements(new W.SdtBlock(
            new W.SdtProperties(new W.DataBinding { StoreItemId = "{11111111-1111-1111-1111-111111111111}", XPath = "/root/name" }),
            new W.SdtContentBlock(new W.Paragraph(new W.Run(new W.Text("BOUND"))))));
        await RoundTrip("word-bound-sdt", WordService.Create(), OfficeFormat.Word, bound, _ => ["CHANGED"], "office_unsupported_content");

        var limited = WordService.Create(officeOptions: Options.Create(new OfficeProcessingOptions { MaxPlanChars = 1 }));
        var import = await limited.ImportAsync(new MemoryStream(bytes));
        Results.Add(new { id = "plan-limit-wrong-code", errors = import.Errors });

        var shared = MutateExcel(OfficeFixtureFactory.CreateExcelWithSharedStrings(["Shared"], [[0]]), doc =>
        {
            var workbook = doc.WorkbookPart!;
            var hidden = workbook.AddNewPart<WorksheetPart>();
            hidden.Worksheet = (S.Worksheet)workbook.WorksheetParts.First().Worksheet!.CloneNode(true);
            workbook.Workbook!.Sheets!.AppendChild(new S.Sheet { Id = workbook.GetIdOfPart(hidden), SheetId = 2U, Name = "Hidden", State = S.SheetStateValues.VeryHidden });
        });
        var exported = await ExcelService.Create().ExportAsync(new MemoryStream(shared), ["Translated"]);
        using (var doc = SpreadsheetDocument.Open(new MemoryStream(exported.Content!), false))
        {
            var workbook = doc.WorkbookPart!;
            var texts = workbook.Workbook!.Sheets!.Elements<S.Sheet>().Select(s =>
            {
                var part = (WorksheetPart)workbook.GetPartById(s.Id!);
                var index = int.Parse(part.Worksheet!.Descendants<S.Cell>().First().CellValue!.Text);
                return new { sheet = s.Name!.Value, text = workbook.SharedStringTablePart!.SharedStringTable!.Elements<S.SharedStringItem>().ElementAt(index).InnerText };
            }).ToArray();
            Results.Add(new { id = "sst-hidden-sheet-control", errors = exported.Errors, texts });
        }
    }
}
