using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/// <summary>
/// Audits handwritten declarations against explicit repository XML documentation rules.
/// </summary>
internal static class DocumentationAudit
{

    /// <summary>
    /// Writes complete declaration and violation inventory.
    /// </summary>
    /// <param name="root">Absolute FileHandler solution directory.</param>
    /// <returns>No return value.</returns>
    internal static void Run(string root)
    {
        var findings = new List<object>();
        var declarations = 0;
        var inherited = 0;
        var files = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Split(Path.DirectorySeparatorChar).Any(s => s is "bin" or "obj")).Order().ToArray();
        foreach (var file in files)
        {
            var source = File.ReadAllText(file);
            var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview, DocumentationMode.Diagnose));
            foreach (var node in tree.GetRoot().DescendantNodes().Where(n => n is BaseMethodDeclarationSyntax or LocalFunctionStatementSyntax
                or PropertyDeclarationSyntax or IndexerDeclarationSyntax or FieldDeclarationSyntax or EventFieldDeclarationSyntax
                or EnumDeclarationSyntax or EnumMemberDeclarationSyntax or RecordDeclarationSyntax))
            {
                declarations++;
                var line = tree.GetLineSpan(node.Span).StartLinePosition.Line + 1;
                var issues = new List<string>();
                var documentation = node.GetLeadingTrivia().Where(t => t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)).ToArray();
                if (documentation.Length == 0)
                    issues.Add("missing XML documentation");
                else
                {
                    var trivia = documentation[^1];
                    var xml = Regex.Replace(trivia.ToFullString(), @"(?m)^\s*/// ?", "");
                    try
                    {
                        var element = XElement.Parse("<doc>" + xml + "</doc>");
                        if (element.Element("inheritdoc") is not null) inherited++;
                        if (element.Element("summary") is null) issues.Add("missing explicit summary");
                        var parameters = node switch
                        {
                            BaseMethodDeclarationSyntax method => method.ParameterList.Parameters.Select(p => p.Identifier.ValueText).ToArray(),
                            LocalFunctionStatementSyntax local => local.ParameterList.Parameters.Select(p => p.Identifier.ValueText).ToArray(),
                            IndexerDeclarationSyntax indexer => indexer.ParameterList.Parameters.Select(p => p.Identifier.ValueText).ToArray(),
                            RecordDeclarationSyntax record => record.ParameterList?.Parameters.Select(p => p.Identifier.ValueText).ToArray() ?? [],
                            _ => []
                        };
                        var actual = element.Elements("param").Select(p => (string?)p.Attribute("name") ?? "").ToArray();
                        if (!parameters.SequenceEqual(actual)) issues.Add("parameter tags missing or out of order");
                        var typeParameters = node switch
                        {
                            MethodDeclarationSyntax method => method.TypeParameterList?.Parameters.Select(p => p.Identifier.ValueText).ToArray() ?? [],
                            LocalFunctionStatementSyntax local => local.TypeParameterList?.Parameters.Select(p => p.Identifier.ValueText).ToArray() ?? [],
                            RecordDeclarationSyntax record => record.TypeParameterList?.Parameters.Select(p => p.Identifier.ValueText).ToArray() ?? [],
                            _ => []
                        };
                        if (!typeParameters.SequenceEqual(element.Elements("typeparam").Select(p => (string?)p.Attribute("name") ?? "")))
                            issues.Add("generic parameter tags missing or out of order");
                        if (node is MethodDeclarationSyntax or LocalFunctionStatementSyntax or OperatorDeclarationSyntax or ConversionOperatorDeclarationSyntax)
                        {
                            if (element.Element("returns") is null) issues.Add("missing explicit returns");
                        }
                        foreach (var child in element.Elements())
                        {
                            if (child.Name == "summary")
                            {
                                if (!Regex.IsMatch(xml, @"<summary>\s*\r?\n[^\r\n]+\r?\n\s*</summary>")) issues.Add("summary must occupy three lines");
                            }
                            else if (child.Name != "inheritdoc" && Regex.IsMatch(child.ToString(SaveOptions.DisableFormatting), @"\r|\n"))
                                issues.Add("non-summary tag spans multiple lines");
                        }
                        if (Regex.IsMatch(xml, @"\bGets or sets\b|\bthe\b", RegexOptions.IgnoreCase)) issues.Add("boilerplate/article review required");
                        var docLine = tree.GetLineSpan(trivia.Span).StartLinePosition.Line;
                        var lines = tree.GetText().Lines;
                        if (docLine > 0 && (!string.IsNullOrWhiteSpace(lines[docLine - 1].ToString()) ||
                            docLine > 1 && string.IsNullOrWhiteSpace(lines[docLine - 2].ToString()))) issues.Add("not exactly one blank line before documentation");
                    }
                    catch (Exception exception) { issues.Add("invalid XML: " + exception.GetType().Name); }
                }
                if (issues.Count > 0) findings.Add(new { file = Path.GetRelativePath(root, file).Replace('\\', '/'), line, kind = node.Kind().ToString(), issues });
            }
        }
        var report = new { files = files.Length, declarations, inheritedOnlyOrMixed = inherited, affectedDeclarations = findings.Count, findings };
        File.WriteAllText("documentation-audit.json", JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(new { files = files.Length, declarations, inherited, affectedDeclarations = findings.Count }));
    }
}
