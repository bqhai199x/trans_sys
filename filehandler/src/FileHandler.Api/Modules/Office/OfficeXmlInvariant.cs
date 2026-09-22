using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace FileHandler.Api.Modules.Office;

/// <summary>
/// Compares XML structure while allowing text scalars and append-only shared strings.
/// </summary>
internal static class OfficeXmlInvariant
{

    /// <summary>
    /// Spreadsheet namespace.
    /// </summary>
    private static readonly XNamespace Spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    /// <summary>
    /// Wordprocessing namespace.
    /// </summary>
    private static readonly XNamespace Word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    /// <summary>
    /// Drawing namespace.
    /// </summary>
    private static readonly XNamespace Drawing = "http://schemas.openxmlformats.org/drawingml/2006/main";

    /// <summary>
    /// Verifies structure and metadata outside explicitly supported scalar edits.
    /// </summary>
    /// <param name="source">Original XML entry.</param>
    /// <param name="output">Translated XML entry.</param>
    /// <param name="mask">Permitted edit categories.</param>
    /// <param name="limits">Active XML quotas.</param>
    /// <returns>True when protected XML remains equivalent.</returns>
    internal static bool Matches(ZipArchiveEntry source, ZipArchiveEntry output, OfficeEditMask mask, OfficeProcessingOptions limits)
    {
        var before = Read(source, limits);
        var after = Read(output, limits);
        if (before.Name != after.Name) return false;
        if (mask.AttributeEdits.Count > 0)
        {
            var originals = Index(before);
            var outputs = Index(after);
            foreach (var edit in mask.AttributeEdits)
            {
                var name = XName.Get(edit.LocalName, edit.NamespaceUri);
                if (!originals.TryGetValue(edit.ElementPath, out var original) || !outputs.TryGetValue(edit.ElementPath, out var translated) ||
                    original.Attribute(name)?.Value != edit.SourceValue || translated.Attribute(name)?.Value != edit.Value)
                    return false;
                original.SetAttributeValue(name, "");
                translated.SetAttributeValue(name, "");
            }
        }
        if (mask.ScalarEdits is not null)
        {
            var sourceIndex = Index(before);
            var outputIndex = Index(after);
            foreach (var (key, edit) in mask.ScalarEdits)
            {
                if (!sourceIndex.TryGetValue(key, out var original) || !outputIndex.TryGetValue(key, out var translated) ||
                    original.HasElements || translated.HasElements ||
                    Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(original.Value))) != edit.SourceHash || translated.Value != edit.Value)
                    return false;
                original.Value = "";
                translated.Value = "";
                original.Attribute(XNamespace.Xml + "space")?.Remove();
                translated.Attribute(XNamespace.Xml + "space")?.Remove();
            }
            return XNode.DeepEquals(Normalize(before), Normalize(after));
        }
        if (before.Name == Spreadsheet + "sst")
        {
            var originalItems = before.Elements(Spreadsheet + "si").ToArray();
            var outputItems = after.Elements(Spreadsheet + "si").ToArray();
            if (outputItems.Length < originalItems.Length) return false;
            if (mask.AppendedXml is not null)
            {
                if (outputItems.Length != originalItems.Length + mask.AppendedXml.Count) return false;
                for (var i = 0; i < mask.AppendedXml.Count; i++)
                    if (!XNode.DeepEquals(Normalize(XElement.Parse(mask.AppendedXml[i])), Normalize(outputItems[originalItems.Length + i]))) return false;
            }
            for (var i = 0; i < originalItems.Length; i++)
                if (!XNode.DeepEquals(Normalize(originalItems[i]), Normalize(outputItems[i]))) return false;
            foreach (var item in originalItems) item.Remove();
            foreach (var item in outputItems) item.Remove();
            if (mask.SstOptionalCountersRemoved)
                foreach (var root in new[] { before, after })
                {
                    root.Attribute("count")?.Remove();
                    root.Attribute("uniqueCount")?.Remove();
                }
        }
        else
        {
            StripEditableScalars(before, mask);
            StripEditableScalars(after, mask);
        }
        return XNode.DeepEquals(Normalize(before), Normalize(after));
    }

    /// <summary>
    /// Reads XML without entity resolution and with character quotas.
    /// </summary>
    /// <param name="entry">XML entry.</param>
    /// <param name="limits">Active XML limits.</param>
    /// <returns>Parsed XML root.</returns>
    private static XElement Read(ZipArchiveEntry entry, OfficeProcessingOptions limits)
    {
        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = limits.MaxXmlCharactersPerPart
        });
        return XElement.Load(reader, LoadOptions.PreserveWhitespace);
    }

    /// <summary>
    /// Removes only scalar text and its whitespace preservation attribute.
    /// </summary>
    /// <param name="root">Root to normalize.</param>
    /// <param name="mask">Permitted edit categories.</param>
    /// <returns>No return value.</returns>
    private static void StripEditableScalars(XElement root, OfficeEditMask mask)
    {
        foreach (var element in root.Descendants())
        {
            var allowed = element.Name == Word + "t" && mask.AllowedElementPaths.Contains("//w:t") ||
                element.Name == Drawing + "t" && mask.AllowedElementPaths.Contains("//a:t") ||
                element.Name == Spreadsheet + "t" && mask.AllowedElementPaths.Contains("//x:t") ||
                element.Name == Spreadsheet + "v" && element.Parent?.Attribute("t")?.Value == "s" && mask.AllowedElementPaths.Contains("//x:v");
            if (!allowed) continue;
            if (element.HasElements) continue;
            element.Value = "";
            element.Attribute(XNamespace.Xml + "space")?.Remove();
        }
    }

    /// <summary>
    /// Normalizes namespace declarations, attribute ordering and layout whitespace.
    /// </summary>
    /// <param name="element">Element to copy.</param>
    /// <returns>Canonical semantic XML copy.</returns>
    private static XElement Normalize(XElement element) => new(element.Name,
        element.Attributes().Where(a => !a.IsNamespaceDeclaration).OrderBy(a => a.Name.ToString(), StringComparer.Ordinal)
            .Select(a => new XAttribute(a.Name, a.Value)),
        element.Nodes().Where(n => n is not XText text || !element.HasElements || !string.IsNullOrWhiteSpace(text.Value))
            .Select(n => n is XElement child ? (XNode)Normalize(child) : n));

    /// <summary>
    /// Indexes root-relative XML paths with one sibling scan per parent.
    /// </summary>
    /// <param name="root">Part XML root.</param>
    /// <returns>Elements keyed by canonical address.</returns>
    private static Dictionary<string, XElement> Index(XElement root)
    {
        var result = new Dictionary<string, XElement>(StringComparer.Ordinal);
        var pending = new Stack<(XElement Element, string Path)>();
        pending.Push((root, ""));
        while (pending.TryPop(out var item))
        {
            result.Add(item.Path, item.Element);
            var ordinals = new Dictionary<XName, int>();
            foreach (var child in item.Element.Elements())
            {
                ordinals.TryGetValue(child.Name, out var ordinal);
                ordinals[child.Name] = ++ordinal;
                var key = OfficeTextBindings.Key([new(child.Name.NamespaceName, child.Name.LocalName, ordinal)]);
                pending.Push((child, item.Path + key));
            }
        }
        return result;
    }
}
