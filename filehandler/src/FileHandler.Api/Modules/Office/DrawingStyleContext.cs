using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace FileHandler.Api.Modules.Office;

/// <summary>
/// Resolves run defaults for ordinary presentation shapes without placeholder or style references.
/// </summary>
internal static class DrawingStyleContext
{

    /// <summary>
    /// DrawingML namespace for formatting elements.
    /// </summary>
    private static readonly XNamespace Drawing = "http://schemas.openxmlformats.org/drawingml/2006/main";

    /// <summary>
    /// Builds inherited defaults when shape inheritance can be resolved completely.
    /// </summary>
    /// <param name="paragraph">Source paragraph attached to its shape.</param>
    /// <returns>Resolved defaults, or null for unsupported inheritance contexts.</returns>
    internal static XElement? Resolve(A.Paragraph paragraph)
    {
        if (paragraph.Parent?.Parent is not P.Shape shape || shape.ShapeStyle is not null ||
            shape.NonVisualShapeProperties?.ApplicationNonVisualDrawingProperties?.GetFirstChild<P.PlaceholderShape>() is not null)
            return null;

        var defaults = XElement.Parse("""
            <a:rPr xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" b="0" i="0" u="none" strike="noStrike" cap="none" spc="0" normalizeH="0" baseline="0">
              <a:ln><a:noFill/></a:ln><a:effectLst/><a:uLnTx/><a:uFillTx/><a:cs typeface="+mn-cs"/>
            </a:rPr>
            """);
        var level = (paragraph.ParagraphProperties?.Level?.Value ?? 0) + 1;
        var slide = paragraph.Ancestors<P.Slide>().FirstOrDefault()?.SlidePart;
        if (slide?.OpenXmlPackage is PresentationDocument document)
            ApplyList(defaults, document.PresentationPart?.Presentation?.DefaultTextStyle, level);
        ApplyList(defaults, slide?.SlideLayoutPart?.SlideMasterPart?.SlideMaster?.TextStyles?.OtherStyle, level);
        ApplyList(defaults, shape.TextBody?.ListStyle, level);
        Overlay(defaults, paragraph.ParagraphProperties?.GetFirstChild<A.DefaultRunProperties>());
        return defaults;
    }

    /// <summary>
    /// Creates a fingerprint after applying direct run formatting to inherited defaults.
    /// </summary>
    /// <param name="properties">Direct run properties.</param>
    /// <param name="defaults">Resolved paragraph defaults, or null.</param>
    /// <returns>Formatting fingerprint retaining meaningful overrides.</returns>
    internal static string Fingerprint(A.RunProperties? properties, XElement? defaults)
    {
        if (defaults is null) return OfficeStyleFingerprint.Create(properties);
        var effective = new XElement(defaults);
        Overlay(effective, properties);
        return OfficeStyleFingerprint.Create(new A.RunProperties(effective.ToString(SaveOptions.DisableFormatting)));
    }

    /// <summary>
    /// Applies default and matching-level paragraph properties from a text style list.
    /// </summary>
    /// <param name="target">Accumulated run properties.</param>
    /// <param name="list">Inherited style list, or null.</param>
    /// <param name="level">One-based paragraph level.</param>
    /// <returns>No return value.</returns>
    private static void ApplyList(XElement target, OpenXmlElement? list, int level)
    {
        if (list is null) return;
        foreach (var name in new[] { "defPPr", $"lvl{level}pPr" })
            Overlay(target, list.ChildElements.FirstOrDefault(e => e.LocalName == name)?.GetFirstChild<A.DefaultRunProperties>());
    }

    /// <summary>
    /// Applies explicit properties while replacing mutually exclusive formatting choices.
    /// </summary>
    /// <param name="target">Accumulated run properties.</param>
    /// <param name="properties">Higher-priority explicit properties, or null.</param>
    /// <returns>No return value.</returns>
    private static void Overlay(XElement target, OpenXmlElement? properties)
    {
        if (properties is null) return;
        var source = XElement.Parse(properties.OuterXml);
        foreach (var attribute in source.Attributes().Where(a => !a.IsNamespaceDeclaration))
            target.SetAttributeValue(attribute.Name, attribute.Value);
        foreach (var child in source.Elements())
        {
            var group = Group(child.Name);
            target.Elements().Where(e => Group(e.Name) == group).Remove();
            target.Add(new XElement(child));
        }
    }

    /// <summary>
    /// Identifies mutually exclusive run property groups.
    /// </summary>
    /// <param name="name">Qualified property name.</param>
    /// <returns>Group key, or qualified name for independent properties.</returns>
    private static string Group(XName name) => name.Namespace != Drawing ? name.ToString() : name.LocalName switch
    {
        "noFill" or "solidFill" or "gradFill" or "blipFill" or "pattFill" or "grpFill" => "fill",
        "effectLst" or "effectDag" => "effects",
        "uLn" or "uLnTx" => "underlineLine",
        "uFill" or "uFillTx" => "underlineFill",
        _ => name.ToString()
    };
}
