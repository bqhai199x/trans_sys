using System.Xml.Linq;
using DocumentFormat.OpenXml;

namespace FileHandler.Api.Modules.Office;

/// <summary>
/// Compares explicit run formatting independently of language and proofing metadata.
/// </summary>
internal static class OfficeStyleFingerprint
{

    /// <summary>
    /// Wordprocessing namespace used for run properties.
    /// </summary>
    private const string Word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    /// <summary>
    /// Drawing namespace used for run properties.
    /// </summary>
    private const string Drawing = "http://schemas.openxmlformats.org/drawingml/2006/main";

    /// <summary>
    /// Spreadsheet namespace used for rich text properties.
    /// </summary>
    private const string Spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    /// <summary>
    /// Known on/off property elements supporting implicit true values.
    /// </summary>
    private static readonly HashSet<string> OnOff = ["b", "bCs", "i", "iCs", "strike", "dstrike", "caps", "smallCaps", "outline", "shadow", "emboss", "imprint", "vanish", "webHidden", "rtl", "cs", "snapToGrid", "specVanish", "oMath", "condense", "extend"];

    /// <summary>
    /// Creates deterministic formatting key without modifying source XML.
    /// </summary>
    /// <param name="properties">Explicit run properties, or null.</param>
    /// <returns>Canonical formatting key; empty for no meaningful properties.</returns>
    internal static string Create(OpenXmlElement? properties)
    {
        if (properties is null) return "";
        var root = XElement.Parse(properties.OuterXml);
        foreach (var element in root.DescendantsAndSelf().ToArray())
        {
            if (element.Name.NamespaceName == Word && element.Name.LocalName is "lang" or "noProof")
            {
                element.Remove();
                continue;
            }
            foreach (var attribute in element.Attributes().ToArray())
            {
                if (attribute.IsNamespaceDeclaration ||
                    element.Name.NamespaceName == Drawing && attribute.Name.NamespaceName.Length == 0 &&
                    attribute.Name.LocalName is "lang" or "altLang" or "dirty" or "err" or "smtClean" or "smtId" or "noProof")
                    attribute.Remove();
                else if (element.Name.NamespaceName == Drawing && attribute.Name.NamespaceName.Length == 0 &&
                    attribute.Name.LocalName is "b" or "i" or "kumimoji" or "normalizeH")
                    attribute.Value = Boolean(attribute.Value);
            }
            if (element.Name.NamespaceName is Word or Spreadsheet && OnOff.Contains(element.Name.LocalName))
            {
                var name = element.Name.NamespaceName == Word ? XName.Get("val", Word) : XName.Get("val");
                element.SetAttributeValue(name, Boolean(element.Attribute(name)?.Value ?? "1"));
            }
        }
        if (!root.HasAttributes && !root.HasElements) return "";
        return Part(string.Concat(root.Attributes().OrderBy(a => a.Name.ToString(), StringComparer.Ordinal).Select(a => Part(a.Name.ToString()) + Part(a.Value)))) +
            Part(string.Concat(root.Elements().Select(Canonical).Order(StringComparer.Ordinal)));
    }

    /// <summary>
    /// Encodes XML names and values without namespace-prefix or attribute-order differences.
    /// </summary>
    /// <param name="element">Property element.</param>
    /// <returns>Canonical property representation retaining child order.</returns>
    private static string Canonical(XElement element) => Part(element.Name.ToString()) +
        Part(string.Concat(element.Attributes().OrderBy(a => a.Name.ToString(), StringComparer.Ordinal).Select(a => Part(a.Name.ToString()) + Part(a.Value)))) +
        Part(string.Concat(element.Nodes().Select(n => n is XElement child ? Canonical(child) : Part(n.ToString()))));

    /// <summary>
    /// Normalizes known boolean lexical values.
    /// </summary>
    /// <param name="value">Source boolean value.</param>
    /// <returns>One or zero for recognized values; original value otherwise.</returns>
    private static string Boolean(string value) => value switch { "true" or "on" or "1" => "1", "false" or "off" or "0" => "0", _ => value };

    /// <summary>
    /// Encodes one unambiguous key component.
    /// </summary>
    /// <param name="value">Component value.</param>
    /// <returns>Length-prefixed component.</returns>
    private static string Part(string value) => $"{value.Length}:{value}";
}
