using System.Text;

namespace FileHandler.Api.Modules.Excel;

/// <summary>
/// Tokenizes sheet qualifiers while preserving formula literals and other syntax.
/// </summary>
internal static class ExcelFormulaReferences
{

    /// <summary>
    /// Rewrites recognized internal sheet qualifiers from original names simultaneously.
    /// </summary>
    /// <param name="formula">Original formula or internal hyperlink target.</param>
    /// <param name="names">Original-to-final sheet name map.</param>
    /// <param name="rewritten">Rewritten value when reference syntax is safe.</param>
    /// <returns>False for dynamic, external, 3D or unrecognized sheet reference syntax.</returns>
    internal static bool TryRewrite(string formula, IReadOnlyDictionary<string, string> names, out string rewritten)
    {
        rewritten = formula;
        if (!TryParse(formula, names, out var references)) return false;
        rewritten = Rewrite(formula, references, names);
        return true;
    }

    /// <summary>
    /// Collects sheet qualifier spans without interpreting structured column text.
    /// </summary>
    /// <param name="formula">Original formula or internal hyperlink target.</param>
    /// <param name="names">Known original sheet names.</param>
    /// <param name="references">Ordered qualifier spans when parsing succeeds.</param>
    /// <returns>False when reference scope cannot be rewritten safely.</returns>
    internal static bool TryParse(string formula, IReadOnlyDictionary<string, string> names, out IReadOnlyList<ExcelSheetReference> references)
    {
        var parsed = new List<ExcelSheetReference>();
        references = parsed;
        var position = 0;
        while (position < formula.Length)
        {
            var start = position;
            var character = formula[position];
            if (character == '"')
            {
                position++;
                var closed = false;
                while (position < formula.Length)
                {
                    if (formula[position++] != '"') continue;
                    if (position < formula.Length && formula[position] == '"') { position++; continue; }
                    closed = true;
                    break;
                }
                if (!closed) return false;
                continue;
            }
            if (character == '[')
            {
                if (!SkipStructuredReference(formula, ref position)) return false;
                continue;
            }
            if (character == '\'')
            {
                if (start > 0 && formula[start - 1] is ':' or ']') return false;
                var name = new StringBuilder();
                position++;
                var closed = false;
                while (position < formula.Length)
                {
                    var next = formula[position++];
                    if (next != '\'') { name.Append(next); continue; }
                    if (position < formula.Length && formula[position] == '\'') { name.Append('\''); position++; continue; }
                    closed = true;
                    break;
                }
                var originalName = name.ToString();
                if (!closed || position == formula.Length || formula[position] != '!' || originalName.IndexOfAny([':', '[', ']']) >= 0 ||
                    !names.ContainsKey(originalName)) return false;
                parsed.Add(new(start, position - start, originalName));
                position++;
                continue;
            }
            if (IsNameCharacter(character))
            {
                position++;
                while (position < formula.Length && IsNameCharacter(formula[position])) position++;
                var token = formula[start..position];
                var functionPosition = position;
                while (functionPosition < formula.Length && char.IsWhiteSpace(formula[functionPosition])) functionPosition++;
                if (functionPosition < formula.Length && formula[functionPosition] == '(' &&
                    (token.EndsWith("INDIRECT", StringComparison.OrdinalIgnoreCase) || token.EndsWith("HYPERLINK", StringComparison.OrdinalIgnoreCase)))
                    return false;
                if (position < formula.Length && formula[position] == '!')
                {
                    if (start > 0 && formula[start - 1] is ':' or ']' || !names.ContainsKey(token)) return false;
                    parsed.Add(new(start, position - start, token));
                    position++;
                }
                continue;
            }
            if (!char.IsWhiteSpace(character) && !"=+-*/^&<>(),;:{}%@#".Contains(character)) return false;
            position++;
        }
        return true;
    }

    /// <summary>
    /// Applies final names to parsed qualifier spans while preserving all other text.
    /// </summary>
    /// <param name="formula">Original parsed formula.</param>
    /// <param name="references">Ordered spans from successful parsing.</param>
    /// <param name="names">Original-to-final sheet name map.</param>
    /// <returns>Rewritten formula, or original string when no qualifier changes.</returns>
    internal static string Rewrite(string formula, IReadOnlyList<ExcelSheetReference> references, IReadOnlyDictionary<string, string> names)
    {
        StringBuilder? result = null;
        var position = 0;
        foreach (var reference in references)
        {
            var replacement = names[reference.Name];
            if (replacement.Equals(reference.Name, StringComparison.Ordinal)) continue;
            result ??= new StringBuilder(formula.Length);
            result.Append(formula, position, reference.Start - position).Append(Quote(replacement));
            position = reference.Start + reference.Length;
        }
        return result is null ? formula : result.Append(formula, position, formula.Length - position).ToString();
    }

    /// <summary>
    /// Locates all static qualifiers in unsupported formulas, including complete 3D sheet ranges.
    /// </summary>
    /// <param name="formula">Formula rejected by rewrite parser.</param>
    /// <param name="sheetNames">Original workbook names in source order.</param>
    /// <param name="positions">Original name indexes shared across formula scans.</param>
    /// <param name="affected">Names whose renames must be preserved.</param>
    /// <returns>False when dynamic or external syntax prevents proving affected scope.</returns>
    internal static bool TryFindAffectedSheets(string formula, IReadOnlyList<string> sheetNames, IReadOnlyDictionary<string, int> positions, out HashSet<string> affected)
    {
        affected = new(StringComparer.OrdinalIgnoreCase);
        var position = 0;
        while (position < formula.Length)
        {
            var character = formula[position];
            if (character == '"')
            {
                position++;
                var closed = false;
                while (position < formula.Length)
                {
                    if (formula[position++] != '"') continue;
                    if (position < formula.Length && formula[position] == '"') { position++; continue; }
                    closed = true;
                    break;
                }
                if (!closed) return false;
                continue;
            }
            if (character == '[')
            {
                if (!SkipStructuredReference(formula, ref position)) return false;
                continue;
            }
            if (character != '\'' && !IsNameCharacter(character))
            {
                if (!char.IsWhiteSpace(character) && !"=+-*/^&<>(),;:{}%@#".Contains(character)) return false;
                position++;
                continue;
            }
            if (!ReadName(formula, ref position, out var name)) return false;
            if (position < formula.Length && formula[position] == ':')
            {
                position++;
                if (!ReadName(formula, ref position, out var last)) return false;
                name += ":" + last;
            }
            var next = position;
            while (next < formula.Length && char.IsWhiteSpace(formula[next])) next++;
            if (next < formula.Length && formula[next] == '(' &&
                (name.EndsWith("INDIRECT", StringComparison.OrdinalIgnoreCase) || name.EndsWith("HYPERLINK", StringComparison.OrdinalIgnoreCase))) return false;
            if (position >= formula.Length || formula[position] != '!') continue;
            position++;
            var ends = name.Split(':');
            if (ends.Length > 2 || !positions.TryGetValue(ends[0], out var firstIndex) || !positions.TryGetValue(ends[^1], out var lastIndex)) return false;
            for (var index = Math.Min(firstIndex, lastIndex); index <= Math.Max(firstIndex, lastIndex); index++) affected.Add(sheetNames[index]);
        }
        return affected.Count > 0;
    }

    /// <summary>
    /// Skips nested structured brackets and apostrophe escapes without parsing column text.
    /// </summary>
    /// <param name="formula">Original formula.</param>
    /// <param name="position">Opening bracket offset, advanced past matching closing bracket.</param>
    /// <returns>False for incomplete brackets, uncertain escapes or external workbook qualifiers.</returns>
    private static bool SkipStructuredReference(string formula, ref int position)
    {
        var depth = 0;
        while (position < formula.Length)
        {
            var character = formula[position++];
            if (character == '\'')
            {
                if (position >= formula.Length || formula[position] is not ('[' or ']' or '#' or '\'' or '@')) return false;
                position++;
            }
            else if (character == '[') depth++;
            else if (character == ']' && --depth == 0)
                return position == formula.Length || !IsNameCharacter(formula[position]) && formula[position] is not ('\'' or '!' or '[');
        }
        return false;
    }

    /// <summary>
    /// Reads one unquoted or apostrophe-escaped sheet qualifier token.
    /// </summary>
    /// <param name="formula">Original formula.</param>
    /// <param name="position">Current offset, advanced past token.</param>
    /// <param name="name">Unescaped token text.</param>
    /// <returns>True for a complete nonempty token.</returns>
    private static bool ReadName(string formula, ref int position, out string name)
    {
        name = "";
        if (position >= formula.Length) return false;
        if (formula[position] != '\'')
        {
            var start = position;
            while (position < formula.Length && IsNameCharacter(formula[position])) position++;
            name = formula[start..position];
            return name.Length > 0;
        }
        position++;
        var builder = new StringBuilder();
        while (position < formula.Length)
        {
            var character = formula[position++];
            if (character != '\'') { builder.Append(character); continue; }
            if (position < formula.Length && formula[position] == '\'') { builder.Append('\''); position++; continue; }
            name = builder.ToString();
            return name.Length > 0;
        }
        return false;
    }

    /// <summary>
    /// Recognizes conservative unquoted formula-name characters.
    /// </summary>
    /// <param name="character">UTF-16 character to inspect.</param>
    /// <returns>True when character can belong to an unquoted name.</returns>
    private static bool IsNameCharacter(char character) => char.IsLetterOrDigit(character) || character is '_' or '.' or '\\' or '$';

    /// <summary>
    /// Quotes a sheet qualifier using Excel apostrophe escaping.
    /// </summary>
    /// <param name="name">Final worksheet name.</param>
    /// <returns>Quoted qualifier without exclamation separator.</returns>
    private static string Quote(string name) => "'" + name.Replace("'", "''", StringComparison.Ordinal) + "'";
}

/// <summary>
/// Original sheet qualifier span excluding exclamation separator.
/// </summary>
/// <param name="Start">Zero-based UTF-16 offset.</param>
/// <param name="Length">Qualifier length in UTF-16 code units.</param>
/// <param name="Name">Unescaped original sheet name.</param>
internal readonly record struct ExcelSheetReference(int Start, int Length, string Name);
